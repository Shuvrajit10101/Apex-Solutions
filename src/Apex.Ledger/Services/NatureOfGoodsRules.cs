using System.Globalization;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// 🔴 <b>CENSUS 6.36 CLAUSE 1 (WAVE 13, TRACK ic) — THE REFERENTIAL RULES FOR ALTERING AND DELETING A NATURE OF
/// GOODS (§206C TCS) MASTER.</b> Until this wave the master was create-only and its predefined rows immutable, so a
/// wrong seeded TCS rate had no in-app remedy (the same defect T1-21 named for the Nature of Payment, closed there by
/// PR #214). This class mirrors <see cref="NatureOfPaymentRules"/> and is what the screen asks before touching the
/// book.
///
/// <para><b>Its own file</b>, per the wave plan: neither <c>MasterDeletionRules</c> nor
/// <see cref="NatureOfPaymentRules"/> is edited.</para>
///
/// <para><b>FIDELITY (R7, ruling 14) — what the vendor says, opened BY CONTENT 2026-11-24,
/// <c>help.tallysolutions.com/tcs-faq-tally/</c>:</b>
/// <list type="bullet">
///   <item>under the question on the TCS rate revised to 2%: <i>"Press Alt+G (Go To) &gt; Alter Master &gt; TCS
///     Nature of Goods and press Enter."</i>, then <i>"Select the required TCS Nature of Goods and press
///     Enter."</i>, and the rate is entered on the existing master — so a predefined nature's RATES are
///     operator-editable, and that is the ground for allowing them (and the route) at all;</item>
///   <item>under <i>"Can I update Section and Payment code in existing TCS Nature of Goods master?"</i>:
///     <i>"Updating Section or Payment code in existing TCS Nature of Goods will affect the payment entries"</i>,
///     and <i>"We recommend you to create a new Nature of Goods"</i> — the ground for refusing a collection-code
///     change on a nature posted TCS lines carry (<see cref="EnsureAlterable"/>).</item>
/// </list></para>
///
/// <para><b>WHAT IS OURS, labelled so (docs/invented-vs-cloned.md IV-382):</b> no vendor page we opened speaks to
/// DELETING a Nature of Goods, so every deletion rule here is ours — refusing a predefined nature outright, refusing
/// one a ledger or a stock item names, refusing one a posted (or cancelled) TCS line carries, and every message.
/// Locking a PREDEFINED nature's collection code on alteration is also ours. The threshold being alterable is ours
/// too: the vendor FAQ shows rate changes; the threshold sits on the same master and is altered on the same form.</para>
/// </summary>
public static class NatureOfGoodsRules
{
    /// <summary>How many ledgers, items and vouchers a refusal names before it summarises the rest as a count.</summary>
    private const int NamesShown = 3;

    /// <summary>The vouchers with a posted TCS line computed under <paramref name="natureId"/>, cancelled ones
    /// included — a cancelled voucher keeps its lines, so its <see cref="TcsLineTax.NatureId"/> would be orphaned by a
    /// deletion exactly as a live one's would.</summary>
    public static IReadOnlyList<Voucher> VouchersCarrying(Company company, Guid natureId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Vouchers
            .Where(v => v.Lines.Any(l => l.Tcs is { } t && t.NatureId == natureId))
            .ToList();
    }

    /// <summary>The ledgers whose Nature of Goods (<see cref="Domain.Ledger.TcsNatureOfGoodsId"/>) is
    /// <paramref name="natureId"/>.</summary>
    public static IReadOnlyList<Domain.Ledger> LedgersNaming(Company company, Guid natureId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Ledgers.Where(l => l.TcsNatureOfGoodsId == natureId).ToList();
    }

    /// <summary>The stock items whose Nature of Goods (<see cref="StockItem.TcsNatureOfGoodsId"/>) is
    /// <paramref name="natureId"/> — <c>TcsService.ResolveNature</c> reads the item's nature FIRST, so an orphaned
    /// item id would silently stop collecting TCS on every later sale of that item.</summary>
    public static IReadOnlyList<StockItem> StockItemsNaming(Company company, Guid natureId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.StockItems.Where(i => i.TcsNatureOfGoodsId == natureId).ToList();
    }

    /// <summary>
    /// The <b>live</b> vouchers whose TCS collection the alteration from <paramref name="existing"/> to
    /// <paramref name="replacement"/> would MOVE — the same measure <see cref="NatureOfPaymentRules.VouchersMovedBy"/>
    /// applies (T2-181): each posted TCS line on a non-cancelled voucher is assessed twice by the one engine
    /// (<see cref="TcsService.Compute"/>) over its own posted assessable value, collectee and date, and the voucher is
    /// counted when the rate or the amount differs. A collectee that no longer resolves is counted.
    /// </summary>
    public static IReadOnlyList<Voucher> VouchersMovedBy(Company company, NatureOfGoods existing, NatureOfGoods replacement)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);
        var engine = new TcsService(company);
        var moved = new List<Voucher>();
        foreach (var v in company.Vouchers.Where(v => !v.Cancelled))
        {
            foreach (var t in v.Lines.Select(l => l.Tcs).OfType<TcsLineTax>().Where(t => t.NatureId == existing.Id))
            {
                if (company.FindLedger(t.CollecteeLedgerId) is not { } collectee) { moved.Add(v); break; }
                // The posted AssessableValue is already the base the nature's flag chose (GST-inclusive for §206C),
                // and an alteration does not change that flag, so it is passed as the value with no further GST.
                var before = engine.Compute(t.AssessableValue, Money.Zero, existing, collectee, v.Date);
                var after = engine.Compute(t.AssessableValue, Money.Zero, replacement, collectee, v.Date);
                if (before.RateBasisPoints != after.RateBasisPoints || before.TcsAmount != after.TcsAmount)
                {
                    moved.Add(v);
                    break;
                }
            }
        }
        return moved;
    }

    /// <summary>
    /// Refuses deleting <paramref name="nature"/>, by name, when it is <b>predefined</b>, when any <b>posted TCS
    /// line</b> carries it, when any <b>ledger</b> names it, or when any <b>stock item</b> names it. None of those
    /// references has a foreign key in the store, so this guard is the only thing standing between a deletion and an
    /// orphaned id that Form 27EQ, the TCS reports and the §206C(1H) projection would then fail to resolve.
    /// </summary>
    /// <exception cref="InvalidOperationException">The nature is predefined or still in use.</exception>
    public static void EnsureDeletable(Company company, NatureOfGoods nature)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(nature);
        var what = $"Nature of Goods '{nature.CollectionCode}' ({nature.Name})";

        if (nature.IsPredefined)
            throw new InvalidOperationException(
                $"Cannot delete {what}: it is a predefined statutory nature. Alter its rates or threshold instead "
                + "(Ctrl+Enter on the row).");

        var vouchers = VouchersCarrying(company, nature.Id);
        if (vouchers.Count > 0)
            throw new InvalidOperationException(
                $"Cannot delete {what}: {Count(vouchers.Count, "posted voucher collects", "posted vouchers collect")} "
                + $"TCS under it ({Names(vouchers.Select(v => DescribeVoucher(company, v)).ToList())}). "
                + "Those collections are filed in Form 27EQ under this nature, so it must stay.");

        var ledgers = LedgersNaming(company, nature.Id);
        if (ledgers.Count > 0)
            throw new InvalidOperationException(
                $"Cannot delete {what}: {Count(ledgers.Count, "ledger names", "ledgers name")} it as its Nature of "
                + $"Goods ({Names(ledgers.Select(l => $"'{l.Name}'").ToList())}). Change those ledgers first.");

        var items = StockItemsNaming(company, nature.Id);
        if (items.Count > 0)
            throw new InvalidOperationException(
                $"Cannot delete {what}: {Count(items.Count, "stock item names", "stock items name")} it as its Nature "
                + $"of Goods ({Names(items.Select(i => $"'{i.Name}'").ToList())}). Change those stock items first.");
    }

    /// <summary>
    /// Refuses altering <paramref name="existing"/> into <paramref name="replacement"/> when the change would re-file
    /// collections already made: a <b>collection code</b> or <b>Form-27EQ code</b> change on a nature posted TCS lines
    /// carry (vendor FAQ: such a change "will affect the payment entries" — create a new Nature of Goods instead), and
    /// either change at all on a <b>predefined</b> nature (ours). Rates, the threshold and the name may always change;
    /// a posted voucher keeps the rate and amount stored on its <see cref="TcsLineTax"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The alteration is refused.</exception>
    public static void EnsureAlterable(Company company, NatureOfGoods existing, NatureOfGoods replacement)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);

        var codeChanged = !string.Equals(existing.CollectionCode, replacement.CollectionCode, StringComparison.Ordinal)
                          || !string.Equals(existing.FvuCode, replacement.FvuCode, StringComparison.Ordinal);
        if (!codeChanged) return;

        var what = $"Nature of Goods '{existing.CollectionCode}' ({existing.Name})";
        if (existing.IsPredefined)
            throw new InvalidOperationException(
                $"Cannot change the collection code of {what}: it is a predefined statutory nature. Only its name, "
                + "rates and threshold can be altered. For a different code, create a new Nature of Goods.");

        var vouchers = VouchersCarrying(company, existing.Id);
        if (vouchers.Count > 0)
            throw new InvalidOperationException(
                $"Cannot change the collection code of {what}: "
                + $"{Count(vouchers.Count, "posted voucher collects", "posted vouchers collect")} TCS under it "
                + $"({Names(vouchers.Select(v => DescribeVoucher(company, v)).ToList())}), and the change would "
                + "re-file those collections. Create a new Nature of Goods for the new code instead.");
    }

    private static string Names(IReadOnlyList<string> names) =>
        names.Count <= NamesShown
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(NamesShown)) + $" and {names.Count - NamesShown} more";

    private static string DescribeVoucher(Company company, Voucher voucher)
    {
        var typeName = company.FindVoucherType(voucher.TypeId)?.Name ?? "Voucher";
        var number = company.FormatVoucherNumber(voucher);
        var numberPart = string.IsNullOrWhiteSpace(number) ? string.Empty : $" No. {number}";
        return $"{typeName}{numberPart} dated "
             + voucher.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
    }

    private static string Count(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";
}
