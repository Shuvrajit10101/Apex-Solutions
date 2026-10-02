using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Price Level / Price List</b> master service (Phase 6 slice 5; RQ-26/RQ-27; Tally-Book pp.33–34).
/// Creates named price levels and appends dated price-list versions, enforcing the same discipline the other
/// inventory masters ship with — throwing <see cref="InvalidOperationException"/> on any violation WITHOUT
/// mutating the company (like <see cref="BatchService"/> / <see cref="InventoryService"/>). Framework- and
/// DB-agnostic; unit-tested like the accounting core.
/// <para>
/// <b>Append-only revisions (RQ-27).</b> <see cref="AddOrReviseList"/> never overwrites an existing version — it
/// adds a NEW <see cref="PriceList"/> with a strictly-later <see cref="PriceList.ApplicableFrom"/> for the
/// (level, item), so older versions survive as history (mirrors the multi-currency dated-quote pattern).
/// </para>
/// </summary>
public sealed class PriceListService
{
    private readonly Company _company;

    public PriceListService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates a named <see cref="PriceLevel"/> (RQ-26). The name must be non-blank and not already used by this
    /// company (case-insensitive uniqueness).
    /// </summary>
    public PriceLevel CreateLevel(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A price-level name is required.");
        if (_company.FindPriceLevelByName(trimmed) is not null)
            throw new InvalidOperationException($"A price level named '{trimmed}' already exists.");

        var level = new PriceLevel(Guid.NewGuid(), trimmed);
        _company.AddPriceLevel(level);
        return level;
    }

    /// <summary>
    /// <b>Renames an existing <see cref="PriceLevel"/></b> — census 3.10, W33 C3. Same two rules as
    /// <see cref="CreateLevel"/> (non-blank, case-insensitive uniqueness) except that the level being renamed
    /// does not block itself, so re-accepting an unchanged name — or correcting only its CASE — is allowed rather
    /// than refused as a duplicate.
    ///
    /// <para><b>FIDELITY (R7): VENDOR-ATTESTED, and this is the shape it attests.</b> TallyPrime reaches it at
    /// <i>Alt+G (Go To) &gt; Alter Master &gt; Price levels</i> (alternatively <i>Gateway of Tally &gt; Alter &gt;
    /// Price levels</i>) and the instruction is to <i>"Change the names of the Price Levels and press Ctrl+A to
    /// save"</i>, after which <i>"the modified names of the price levels appear in the relevant masters and
    /// transactions"</i> [help.tallysolutions.com/selling-buying-prices/, read 2026-09-25]. So a rename is
    /// <b>expected</b> to be reflected everywhere rather than versioned — which is exactly what happens here,
    /// because every referent (<c>price_lists.price_level_id</c>, <c>ledgers.default_price_level_id</c>) holds the
    /// <see cref="PriceLevel.Id"/> and not the name. <b>A rename therefore has no referential guard and needs
    /// none</b>, which is the opposite of <see cref="DeleteLevel"/> and the reason the two verbs differ.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No such level; blank name; or another level owns that name.</exception>
    public PriceLevel AlterLevel(Guid levelId, string name)
    {
        var level = _company.FindPriceLevel(levelId)
            ?? throw new InvalidOperationException($"Price level {levelId} not found.");

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A price-level name is required.");

        // 🔴 `is { } other && other.Id != levelId` — NOT a bare null check. FindPriceLevelByName is
        // case-insensitive, so on an unchanged (or merely re-cased) name it finds THIS level and a bare check
        // would refuse the operator their own name back. Excluding self by ID is what makes "open, look, accept"
        // a no-op instead of an error, and it is the same exclusion InventoryService.AlterStockCategory makes.
        if (_company.FindPriceLevelByName(trimmed) is { } other && other.Id != levelId)
            throw new InvalidOperationException($"A price level named '{trimmed}' already exists.");

        level.Name = trimmed;
        return level;
    }

    /// <summary>
    /// Appends a dated <see cref="PriceList"/> version for a (level, item) (RQ-27). Validates: the level and item
    /// exist and the item is an inventory item; the slabs are non-empty, ascending, contiguous, non-overlapping,
    /// with at most one open-ended (NULL <see cref="PriceListSlab.ToQty"/>) slab which must be the last; each rate
    /// ≥ 0 and paisa-exact; each discount in <c>[0, 100)</c>; and <paramref name="applicableFrom"/> is
    /// <b>strictly later</b> than the newest existing version for the (level, item) — anything else would be an
    /// edit of an existing version, not a revision. Adds a NEW list (never mutates an existing one).
    /// </summary>
    public PriceList AddOrReviseList(Guid levelId, Guid itemId, DateOnly applicableFrom,
        IReadOnlyList<PriceListSlab> slabs)
    {
        if (_company.FindPriceLevel(levelId) is null)
            throw new InvalidOperationException($"Price level {levelId} not found.");
        if (_company.FindStockItem(itemId) is null)
            throw new InvalidOperationException($"Stock item {itemId} not found.");

        ValidateSlabs(slabs);

        // RQ-27: a revision must be strictly newer than the latest existing version for this (level, item).
        DateOnly? newest = null;
        foreach (var existing in _company.PriceListsFor(levelId, itemId))
            if (newest is null || existing.ApplicableFrom > newest) newest = existing.ApplicableFrom;
        if (newest is { } n && applicableFrom <= n)
            throw new InvalidOperationException(
                $"A price list for this level/item already has an Applicable-From of {n:yyyy-MM-dd} or later; " +
                "a revision must carry a strictly later date (append-only history).");

        var list = new PriceList(Guid.NewGuid(), levelId, itemId, applicableFrom, slabs);
        _company.AddPriceList(list);
        return list;
    }

    /// <summary>
    /// Deletes a <see cref="PriceLevel"/>, blocked while any <see cref="PriceList"/> row or any ledger's
    /// <see cref="Ledger.DefaultPriceLevelId"/> references it (so no list version or party default is orphaned).
    /// </summary>
    public void DeleteLevel(Guid levelId)
    {
        var level = _company.FindPriceLevel(levelId)
            ?? throw new InvalidOperationException($"Price level {levelId} not found.");

        if (_company.PriceLists.Any(pl => pl.PriceLevelId == levelId))
            throw new InvalidOperationException(
                $"Price level '{level.Name}' has price lists and cannot be deleted.");
        if (_company.Ledgers.Any(l => l.DefaultPriceLevelId == levelId))
            throw new InvalidOperationException(
                $"Price level '{level.Name}' is a party default and cannot be deleted.");

        _company.RemovePriceLevel(level);
    }

    /// <summary>
    /// Deletes ONE dated <see cref="PriceList"/> version — census 3.11, W33 C3. The append-only history is how a
    /// price is revised; this is how a version entered in error is withdrawn, which is the one thing the history
    /// could not previously do at all (<c>Company.RemovePriceList</c> had no caller anywhere in the product).
    ///
    /// <para>🔴 <b>THERE IS NO REFERENTIAL GUARD AND THAT IS MEASURED, NOT FORGOTTEN.</b> The only foreign key in
    /// the schema into <c>price_lists(id)</c> is <c>price_list_lines.price_list_id</c>, and a slab row is written
    /// from <see cref="PriceList.Slabs"/> — the parent's own object graph — so it leaves with the parent on the
    /// next delete-all + re-insert and can never orphan. <b>Nor can a posted document be affected:</b> a price
    /// list is read by <c>PriceResolver</c> at ENTRY time and the resolved rate is then stored on the voucher
    /// line itself, so withdrawing a version changes what the NEXT invoice is offered and re-prices nothing that
    /// is already on the books.</para>
    ///
    /// <para><b>The one real consequence, stated because it is not obvious.</b> Deleting a version that is not
    /// the newest widens the date range the PRECEDING version governs (or, if it was the only one, leaves the
    /// (level, item) pair with no price list at all and the item's own rate applies). That is a configuration
    /// change with no wrong-figure consequence, so it is confirmed rather than refused.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No price list with that id exists.</exception>
    public void DeleteList(Guid priceListId)
    {
        var list = _company.PriceLists.FirstOrDefault(pl => pl.Id == priceListId)
            ?? throw new InvalidOperationException($"Price list {priceListId} not found.");

        _company.RemovePriceList(list);
    }

    /// <summary>
    /// Validates a slab set (RQ-27/RQ-28): non-empty; each FromQty ≥ 0 and to 6-dp precision; each closed slab
    /// FromQty &lt; ToQty; contiguous + ascending (each slab's FromQty == the previous slab's ToQty); at most one
    /// open-ended (NULL ToQty) slab, and if present it must be the LAST; each rate ≥ 0 and paisa-exact; each
    /// discount in <c>[0, 100)</c>. Throws on the first violation.
    /// </summary>
    private static void ValidateSlabs(IReadOnlyList<PriceListSlab> slabs)
    {
        ArgumentNullException.ThrowIfNull(slabs);
        if (slabs.Count == 0)
            throw new InvalidOperationException("A price list must carry at least one slab.");

        for (var i = 0; i < slabs.Count; i++)
        {
            var slab = slabs[i];
            var isLast = i == slabs.Count - 1;

            if (slab.FromQty < 0m)
                throw new InvalidOperationException("A slab From quantity must be ≥ 0.");
            if (!Quantities.IsWithinPrecision(slab.FromQty))
                throw new InvalidOperationException(
                    $"Slab From quantity {slab.FromQty} must be to {Quantities.DecimalPlaces} decimal places.");

            // Only the last slab may be open-ended (NULL To); any earlier NULL To would overlap everything above it.
            if (slab.ToQty is null && !isLast)
                throw new InvalidOperationException("Only the last slab may be open-ended (no To quantity).");

            if (slab.ToQty is { } to)
            {
                if (!Quantities.IsWithinPrecision(to))
                    throw new InvalidOperationException(
                        $"Slab To quantity {to} must be to {Quantities.DecimalPlaces} decimal places.");
                if (to <= slab.FromQty)
                    throw new InvalidOperationException(
                        $"Slab To quantity {to} must be greater than its From quantity {slab.FromQty}.");
            }

            // Contiguity + ascending: each slab starts exactly where the previous one ended (no gaps, no overlap).
            if (i > 0)
            {
                var prev = slabs[i - 1];
                if (prev.ToQty is not { } prevTo)
                    throw new InvalidOperationException("Only the last slab may be open-ended (no To quantity).");
                if (slab.FromQty != prevTo)
                    throw new InvalidOperationException(
                        $"Slabs must be contiguous and ascending: slab {i} starts at {slab.FromQty} " +
                        $"but the previous slab ends at {prevTo} (gaps and overlaps are not allowed).");
            }

            if (slab.Rate.Amount < 0m)
                throw new InvalidOperationException("A slab rate must be ≥ 0.");
            if (!slab.Rate.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Slab rate {slab.Rate.Amount} must be to the paisa (2 decimal places).");

            if (slab.DiscountPercent < 0m || slab.DiscountPercent >= 100m)
                throw new InvalidOperationException("A slab discount percent must be ≥ 0 and < 100.");
        }
    }
}
