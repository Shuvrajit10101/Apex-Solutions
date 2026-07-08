using System.IO;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// Loads a Robert/Bright study fixture (masters + vouchers, referencing masters by name)
/// into a real seeded <see cref="Company"/>, resolving name → id, then posts every voucher
/// through <see cref="LedgerService"/>. This is the regression harness of design §9: the
/// engine, not the fixture, computes the statements the tests then assert.
/// </summary>
public static class FixtureLoader
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public sealed record LoadedFixture(Company Company, LedgerService Service, DateOnly AsOf, JsonElement Expected);

    /// <summary>Resolves a stock-item name → id in a loaded fixture (empty when the item does not exist).</summary>
    public static Guid StockItemId(Company company, string name)
        => company.FindStockItemByName(name)?.Id ?? Guid.Empty;

    /// <summary>
    /// Loads a study fixture. <paramref name="skipManualClosingStock"/> excludes any voucher flagged
    /// <c>manualClosingStock</c> (the hand-posted closing-stock Journal) — used by the Phase-3
    /// <b>inventory-derived</b> re-verification, where closing stock is DERIVED from inventory (§6, BR-3) and a
    /// manual closing-stock entry would double-count it. The default (<c>false</c>) keeps every voucher, so the
    /// existing <see cref="ClosingStockMode.AsPostedLedger"/> Robert/Bright tests are byte-for-byte unchanged.
    /// </summary>
    public static LoadedFixture Load(string fileName, bool skipManualClosingStock = false)
    {
        var path = Path.Combine(FixturesDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var companyEl = root.GetProperty("company");
        var fyStart = ParseDate(companyEl.GetProperty("financialYearStart").GetString()!);
        var books = ParseDate(companyEl.GetProperty("booksBeginFrom").GetString()!);

        // A fully seeded company (28 groups / 2 ledgers / 24 voucher types), then the
        // fixture's own masters layered on top.
        var company = CompanyFactory.CreateSeeded(companyEl.GetProperty("name").GetString()!, fyStart, books);

        // Groups: every fixture group must already exist in the 28 seeds (by name). We assert
        // the resolved nature matches so the fixture's classification lines up with the engine.
        var masters = root.GetProperty("masters");
        foreach (var g in masters.GetProperty("groups").EnumerateArray())
        {
            var name = g.GetProperty("name").GetString()!;
            var group = company.FindGroupByName(name)
                ?? throw new InvalidOperationException($"Fixture group '{name}' is not a predefined seed group.");
            var declared = MapNature(g.GetProperty("nature").GetString()!);
            if (group.Nature != declared)
                throw new InvalidOperationException(
                    $"Fixture group '{name}' nature {declared} disagrees with seed {group.Nature}.");
        }

        // Ledgers: add each fixture ledger under its named group. If a ledger name collides
        // with a predefined ledger (Cash), reuse the seeded one but set its opening.
        foreach (var l in masters.GetProperty("ledgers").EnumerateArray())
        {
            var name = l.GetProperty("name").GetString()!;
            var underName = l.GetProperty("under").GetString()!;
            var opening = l.GetProperty("openingBalance").GetDecimal();
            var side = l.GetProperty("openingSide").GetString()!;
            var isDebit = string.Equals(side, "Debit", StringComparison.OrdinalIgnoreCase);

            var group = company.FindGroupByName(underName)
                ?? throw new InvalidOperationException($"Ledger '{name}' references unknown group '{underName}'.");

            var existing = company.FindLedgerByName(name);
            if (existing is not null)
            {
                existing.GroupId = group.Id;
                existing.OpeningBalance = Money.FromRupees(opening);
                existing.OpeningIsDebit = isDebit;
            }
            else
            {
                company.AddLedger(new Domain.Ledger(
                    Guid.NewGuid(), name, group.Id, Money.FromRupees(opening), isDebit));
            }
        }

        // Optional inventory block (Phase-3 accounts↔inventory fixtures like Bright). Purely ADDITIVE — a
        // fixture with no "inventory" key (Robert) is unaffected. Masters/opening balances must be created
        // BEFORE the vouchers so item-invoice lines can resolve their stock item/godown by name.
        if (root.TryGetProperty("inventory", out var inventory))
            LoadInventoryMasters(company, inventory);

        // Vouchers: resolve type + ledger names, build entry lines, post through the service.
        var service = new LedgerService(company);
        foreach (var v in root.GetProperty("vouchers").EnumerateArray())
        {
            // The hand-posted closing-stock Journal is excluded under inventory-derived loading (§6, BR-3):
            // closing stock is DERIVED there, so keeping the manual entry would double-count it.
            if (skipManualClosingStock && v.TryGetProperty("manualClosingStock", out var mcs) && mcs.GetBoolean())
                continue;

            var typeName = v.GetProperty("type").GetString()!;
            var type = company.FindVoucherTypeByName(typeName)
                ?? throw new InvalidOperationException($"Voucher references unknown type '{typeName}'.");
            var date = ParseDate(v.GetProperty("date").GetString()!);
            var narration = v.TryGetProperty("narration", out var n) ? n.GetString() : null;
            var number = v.TryGetProperty("no", out var no) ? no.GetInt32() : 0;

            var lines = new List<EntryLine>();
            foreach (var line in v.GetProperty("lines").EnumerateArray())
            {
                var ledgerName = line.GetProperty("ledger").GetString()!;
                var ledger = company.FindLedgerByName(ledgerName)
                    ?? throw new InvalidOperationException($"Voucher line references unknown ledger '{ledgerName}'.");
                var drcr = string.Equals(line.GetProperty("drCr").GetString(), "Debit", StringComparison.OrdinalIgnoreCase)
                    ? DrCr.Debit : DrCr.Credit;
                var amount = Money.FromRupees(line.GetProperty("amount").GetDecimal());
                lines.Add(new EntryLine(ledger.Id, amount, drcr));
            }

            // Optional item-invoice lines (Phase-3): a Purchase/Sales voucher carrying stock lines that move
            // stock in the SAME voucher as the accounting legs. Absent on every accounts-only voucher.
            var inventoryLines = ParseInventoryLines(company, v);

            var voucher = new Voucher(Guid.NewGuid(), type.Id, date, lines, number: number, narration: narration,
                inventoryLines: inventoryLines);
            service.Post(voucher);
        }

        // Stock-only inventory vouchers (Delivery/Receipt Notes) declared in the inventory block: posted AFTER
        // the accounting vouchers so on-hand is available. They post NO accounting entry (keeping the
        // AsPostedLedger statements byte-for-byte unchanged) but move stock so closing stock derives correctly.
        if (root.TryGetProperty("inventory", out var inv2))
            LoadInventoryVouchers(company, inv2);

        var expected = root.GetProperty("expected");
        var asOf = ParseDate(expected.GetProperty("asOf").GetString()!);
        return new LoadedFixture(company, service, asOf, expected.Clone());
    }

    // ------------------------------------------------------------------ inventory (Phase 3, additive)

    /// <summary>
    /// Creates the fixture's inventory masters (stock groups, units, stock items) and their opening balances
    /// via <see cref="InventoryService"/>, resolving parents/units/godowns by name. Called only when the
    /// fixture declares an <c>inventory</c> block, so accounts-only fixtures are untouched.
    /// </summary>
    private static void LoadInventoryMasters(Company company, JsonElement inventory)
    {
        var masters = new InventoryService(company);

        if (inventory.TryGetProperty("stockGroups", out var groups))
            foreach (var g in groups.EnumerateArray())
                masters.CreateStockGroup(
                    g.GetProperty("name").GetString()!,
                    addQuantities: !g.TryGetProperty("addQuantities", out var aq) || aq.GetBoolean());

        if (inventory.TryGetProperty("units", out var units))
            foreach (var u in units.EnumerateArray())
                masters.CreateSimpleUnit(
                    u.GetProperty("symbol").GetString()!,
                    u.GetProperty("formalName").GetString()!,
                    u.TryGetProperty("decimalPlaces", out var dp) ? dp.GetInt32() : 0);

        if (inventory.TryGetProperty("stockItems", out var items))
            foreach (var it in items.EnumerateArray())
            {
                var groupName = it.GetProperty("under").GetString()!;
                var group = company.FindStockGroupByName(groupName)
                    ?? throw new InvalidOperationException($"Stock item references unknown stock group '{groupName}'.");
                var unitName = it.GetProperty("unit").GetString()!;
                var unit = company.FindUnitByName(unitName)
                    ?? throw new InvalidOperationException($"Stock item references unknown unit '{unitName}'.");
                var method = it.TryGetProperty("valuationMethod", out var vm)
                    ? MapValuationMethod(vm.GetString()!)
                    : StockValuationMethod.AverageCost;
                masters.CreateStockItem(it.GetProperty("name").GetString()!, group.Id, unit.Id, valuationMethod: method);
            }

        if (inventory.TryGetProperty("openingBalances", out var openings))
            foreach (var b in openings.EnumerateArray())
            {
                var (itemId, godownId) = ResolveItemAndGodown(company, b);
                masters.AddOpeningBalance(itemId, godownId,
                    b.GetProperty("quantity").GetDecimal(),
                    Money.FromRupees(b.GetProperty("rate").GetDecimal()),
                    batchLabel: b.TryGetProperty("batch", out var bl) ? bl.GetString() : null);
            }
    }

    /// <summary>
    /// Posts the fixture's stock-only inventory vouchers (Delivery/Receipt Notes) — they move stock with NO
    /// accounting effect, so the accounting statements are unchanged while closing stock derives correctly.
    /// </summary>
    private static void LoadInventoryVouchers(Company company, JsonElement inventory)
    {
        var posting = new InventoryPostingService(company);

        PostNotes(inventory, "deliveryNotes", VoucherBaseType.DeliveryNote, StockDirection.Outward);
        PostNotes(inventory, "receiptNotes", VoucherBaseType.ReceiptNote, StockDirection.Inward);

        void PostNotes(JsonElement inv, string key, VoucherBaseType baseType, StockDirection direction)
        {
            if (!inv.TryGetProperty(key, out var notes)) return;
            var typeId = company.VoucherTypes.First(t => t.BaseType == baseType).Id;
            foreach (var note in notes.EnumerateArray())
            {
                var date = ParseDate(note.GetProperty("date").GetString()!);
                var allocations = new List<InventoryAllocation>();
                foreach (var line in note.GetProperty("lines").EnumerateArray())
                {
                    var (itemId, godownId) = ResolveItemAndGodown(company, line);
                    var rate = line.TryGetProperty("rate", out var r) ? (Money?)Money.FromRupees(r.GetDecimal()) : null;
                    allocations.Add(new InventoryAllocation(itemId, godownId,
                        line.GetProperty("quantity").GetDecimal(), direction, rate));
                }
                posting.Post(new InventoryVoucher(Guid.NewGuid(), typeId, date, allocations));
            }
        }
    }

    /// <summary>Parses a voucher's optional <c>inventoryLines</c> into item-invoice lines (null when absent).</summary>
    private static List<VoucherInventoryLine>? ParseInventoryLines(Company company, JsonElement voucher)
    {
        if (!voucher.TryGetProperty("inventoryLines", out var lines)) return null;

        var result = new List<VoucherInventoryLine>();
        foreach (var line in lines.EnumerateArray())
        {
            var (itemId, godownId) = ResolveItemAndGodown(company, line);
            result.Add(new VoucherInventoryLine(itemId, godownId,
                line.GetProperty("quantity").GetDecimal(),
                Money.FromRupees(line.GetProperty("rate").GetDecimal()),
                batchLabel: line.TryGetProperty("batch", out var bl) ? bl.GetString() : null));
        }
        return result;
    }

    private static (Guid ItemId, Guid GodownId) ResolveItemAndGodown(Company company, JsonElement el)
    {
        var itemName = el.GetProperty("item").GetString()!;
        var item = company.FindStockItemByName(itemName)
            ?? throw new InvalidOperationException($"Inventory line references unknown stock item '{itemName}'.");
        var godownName = el.GetProperty("godown").GetString()!;
        var godown = company.FindGodownByName(godownName)
            ?? throw new InvalidOperationException($"Inventory line references unknown godown '{godownName}'.");
        return (item.Id, godown.Id);
    }

    private static StockValuationMethod MapValuationMethod(string s) => s switch
    {
        "AverageCost" or "Average Cost" => StockValuationMethod.AverageCost,
        "Fifo" or "FIFO" => StockValuationMethod.Fifo,
        "Lifo" or "LIFO" => StockValuationMethod.Lifo,
        "LastPurchaseCost" or "Last Purchase Cost" => StockValuationMethod.LastPurchaseCost,
        "LastSaleCost" or "Last Sale Cost" => StockValuationMethod.LastSaleCost,
        "StandardCost" or "Standard Cost" => StockValuationMethod.StandardCost,
        _ => throw new InvalidOperationException($"Unknown valuation method '{s}'."),
    };

    private static DateOnly ParseDate(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static GroupNature MapNature(string s) => s switch
    {
        "Assets" or "Asset" => GroupNature.Asset,
        "Liabilities" or "Liability" => GroupNature.Liability,
        "Income" or "Incomes" => GroupNature.Income,
        "Expenses" or "Expense" => GroupNature.Expense,
        _ => throw new InvalidOperationException($"Unknown fixture nature '{s}'."),
    };
}
