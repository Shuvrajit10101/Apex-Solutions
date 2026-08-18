using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests.Support;

/// <summary>
/// A canonical, ordered, paisa-exact text dump of a company's ENTIRE derived surface
/// (phase-10-11-voucher-lifecycle-design §7.2). It exists for one assertion:
/// <b>an altered book must equal a directly-posted book on every derived figure</b> — which is the
/// correctness statement of S5a (<see cref="LedgerService.Replace(Guid, Voucher)"/>).
///
/// <para><b>Why a dump and not fifteen assertions.</b> §7.2: <i>"'Every' is the hard word, and asserting
/// fifteen figures by hand will miss the sixteenth."</i> Each report is BUILT explicitly here (so the section
/// list and its parameters are reviewable), but every field WITHIN a report record is rendered by reflection,
/// so a report that grows a column is covered on the day it grows it. A diff over this string names the
/// divergence instead of a boolean hiding it.</para>
///
/// <para><b>⚠️ COST — read §3.1/§8.1(10) before using this in a loop.</b>
/// <c>StockValuationService.TotalClosingStockValue</c> loops every item calling <c>ClosingValue</c>, which
/// itself replays the whole book, so this snapshot is roughly QUADRATIC in book size. <b>Build it once per
/// book, never once per assertion.</b></para>
///
/// <para><b>Guids are normalised, deliberately.</b> Two books built from the same seed hold different
/// <see cref="Guid"/>s for the same-named master, so a raw Guid dump could never compare equal across books.
/// Every Guid is therefore rendered as a canonical token: a master resolves to
/// <c>&lt;collection&gt;:&lt;Name&gt;</c>, a voucher to <c>V:&lt;type&gt;#&lt;number&gt;@&lt;list index&gt;</c>,
/// and anything unresolved to a first-appearance ordinal <c>#gN</c>.
/// <b>Consequence to know:</b> because the voucher token is built from type + number + index rather than the
/// raw Guid, this snapshot detects a lost NUMBER and a lost LIST POSITION but is BLIND to a swapped Guid on
/// its own. Guid preservation is pinned separately and explicitly by
/// <c>VoucherReplaceEngineTests.Replace_preserves_the_voucher_Guid</c>.</para>
/// </summary>
public static class DerivedStateSnapshot
{
    /// <summary>
    /// The canonical dump of every derived figure as of <paramref name="asOf"/>. Period reports run
    /// <c>company.BooksBeginFrom … asOf</c>. Deterministic: no clock, no culture, no dictionary order.
    /// </summary>
    public static string Snapshot(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);

        var names = new Names(company);
        var sb = new StringBuilder();
        var from = company.BooksBeginFrom;

        // 1 — Trial Balance.
        Section(sb, names, "01.TrialBalance", () => TrialBalance.Build(company, asOf));

        // 2 — Balance Sheet, BOTH closing-stock bases (the inventory-derived basis is the one that pulls the
        //     stock valuation engine into the statement, §7.2 item 2).
        Section(sb, names, "02.BalanceSheet.AsPostedLedger",
            () => BalanceSheet.Build(company, asOf, ClosingStockMode.AsPostedLedger));
        Section(sb, names, "02.BalanceSheet.InventoryDerived",
            () => BalanceSheet.Build(company, asOf, ClosingStockMode.InventoryDerived));

        // 3 — Profit & Loss, both bases.
        Section(sb, names, "03.ProfitAndLoss.AsPostedLedger",
            () => ProfitAndLoss.Build(company, asOf, ClosingStockMode.AsPostedLedger));
        Section(sb, names, "03.ProfitAndLoss.InventoryDerived",
            () => ProfitAndLoss.Build(company, asOf, ClosingStockMode.InventoryDerived));

        // 4 — Stock: on-hand AND closing valuation, per item and per item x godown.
        Section(sb, names, "04.Stock", () => StockSection(company, asOf));

        // 5 — Batch on-hand and batch valuation.
        Section(sb, names, "05.Batches", () => BatchSection(company, asOf));

        // 6 — Outstandings: every bill, its pending amount and its ageing bucket.
        Section(sb, names, "06.Outstandings", () => Outstandings.Build(company, asOf));

        // 7 — Cost: category summary, cost-centre breakup, ledger breakup.
        Section(sb, names, "07.Cost.CategorySummary", () => CostReports.BuildCategorySummary(company, from, asOf));
        Section(sb, names, "07.Cost.CentreBreakup", () => CostReports.BuildCostCentreBreakup(company, from, asOf));
        Section(sb, names, "07.Cost.LedgerBreakup", () => CostReports.BuildLedgerBreakup(company, from, asOf));

        // 8 — GSTR-1 (every section row) and GSTR-3B (every box).
        Section(sb, names, "08.Gstr1", () => Gstr1.Build(company, from, asOf));
        Section(sb, names, "08.Gstr3b", () => Gstr3b.Build(company, from, asOf));

        // 9 — Electronic cash/credit ledgers.
        Section(sb, names, "09.ElectronicLedgers", () => ElectronicLedgersView.Build(company, from, asOf));

        // 10 — Challan reconciliation: every section's Deducted / Deposited / Remaining.
        Section(sb, names, "10.ChallanReconciliation", () => ChallanReconciliation.Build(company, from, asOf));

        // 11 — Interest, budget variance, order fulfilment, reorder status.
        Section(sb, names, "11.Interest", () => InterestCalculation.Build(company, from, asOf));
        Section(sb, names, "11.BudgetVariance", () => BudgetSection(company));
        Section(sb, names, "11.OrderFulfilment", () => OrderFulfilment.Build(company, asOf));
        Section(sb, names, "11.ReorderStatus", () => ReorderStatus.Build(company, asOf));

        // 12 — The voucher identity vector: Id, Number, rendered number, Cancelled, and the LIST INDEX.
        Section(sb, names, "12.VoucherIdentity", () => IdentitySection(company));

        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------------
    // Sections whose shape is not a single report record.
    // ---------------------------------------------------------------------------------------------------

    private static object StockSection(Company company, DateOnly asOf)
    {
        var ledger = new InventoryLedger(company);
        var valuation = new StockValuationService(company);

        var rows = new List<object>();
        foreach (var item in company.StockItems.OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            var closing = valuation.ClosingValue(item.Id, asOf);
            var perGodown = new List<object>();
            foreach (var g in company.Godowns.OrderBy(x => x.Name, StringComparer.Ordinal))
                perGodown.Add(new StockGodownRow(g.Name, ledger.OnHand(item.Id, g.Id, asOf)));

            rows.Add(new StockItemRow(
                item.Name,
                ledger.OnHand(item.Id, asOf),
                closing.Quantity,
                closing.Value,
                perGodown));
        }

        return new StockSectionRow(rows, valuation.TotalClosingStockValue(asOf));
    }

    private static object BatchSection(Company company, DateOnly asOf)
    {
        var batches = new BatchStockService(company);
        var rows = new List<object>();
        foreach (var item in company.StockItems.OrderBy(i => i.Name, StringComparer.Ordinal))
            rows.Add(new BatchItemRow(item.Name, batches.BatchOnHands(item.Id, asOf)));
        return rows;
    }

    private static object BudgetSection(Company company)
    {
        var rows = new List<object>();
        foreach (var b in company.Budgets.OrderBy(x => x.Name, StringComparer.Ordinal))
            rows.Add(new BudgetRow(b.Name, BudgetVarianceReport.Build(company, b)));
        return rows;
    }

    private static object IdentitySection(Company company)
    {
        var rows = new List<object>();
        for (var i = 0; i < company.Vouchers.Count; i++)
        {
            var v = company.Vouchers[i];
            rows.Add(new VoucherIdentityRow(
                i,
                v.Id,
                v.TypeId,
                v.Number,
                company.FormatVoucherNumber(v),
                v.Date,
                v.Cancelled,
                v.Optional,
                v.PostDated,
                v.Lines.Count,
                v.TotalDebit,
                v.TotalCredit));
        }

        for (var i = 0; i < company.InventoryVouchers.Count; i++)
        {
            var v = company.InventoryVouchers[i];
            rows.Add(new InventoryVoucherIdentityRow(
                i, v.Id, v.TypeId, v.Number, company.FormatVoucherNumber(v), v.Date, v.Cancelled));
        }

        return rows;
    }

    private sealed record StockGodownRow(string Godown, decimal OnHand);

    private sealed record StockItemRow(
        string Item, decimal OnHand, decimal ClosingQuantity, Money ClosingValue, IReadOnlyList<object> Godowns);

    private sealed record StockSectionRow(IReadOnlyList<object> Items, Money TotalClosingStockValue);

    private sealed record BatchItemRow(string Item, object OnHands);

    private sealed record BudgetRow(string Budget, object Variance);

    private sealed record VoucherIdentityRow(
        int Index, Guid Id, Guid TypeId, int Number, string RenderedNumber, DateOnly Date,
        bool Cancelled, bool Optional, bool PostDated, int LineCount, Money TotalDebit, Money TotalCredit);

    private sealed record InventoryVoucherIdentityRow(
        int Index, Guid Id, Guid TypeId, int Number, string RenderedNumber, DateOnly Date, bool Cancelled);

    // ---------------------------------------------------------------------------------------------------
    // The renderer.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds one section and appends its rendered lines. A report that THROWS on this book renders its
    /// exception type and message rather than aborting the snapshot: the throw is then itself a comparable
    /// fact (book A throwing where book B does not is a divergence the diff names), and a snapshot helper
    /// that only works on GST-configured books would be useless to the accounts-only families §7.4 requires.
    /// </summary>
    private static void Section(StringBuilder sb, Names names, string heading, Func<object?> build)
    {
        object? value;
        try
        {
            value = build();
        }
        catch (Exception ex)
        {
            sb.Append(heading).Append(" !! ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n');
            return;
        }

        Render(sb, heading, value, names, depth: 0, seen: new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static void Render(StringBuilder sb, string path, object? value, Names names, int depth, HashSet<object> seen)
    {
        if (TryScalar(value, names, out var scalar))
        {
            sb.Append(path).Append(" = ").Append(scalar).Append('\n');
            return;
        }

        if (depth >= 12)
        {
            sb.Append(path).Append(" = <depth-limit>\n");
            return;
        }

        if (value is IEnumerable seq)
        {
            var items = new List<object?>();
            foreach (var o in seq) items.Add(o);

            // A list/array carries a MEANINGFUL order (the Day Book order is the whole point of clause 4);
            // anything else (a dictionary, a set) does not, and is sorted by its own rendered text so the
            // dump is canonical.
            if (value is not IList)
            {
                items = items
                    .Select(o => (Key: RenderToString(o, names, depth + 1), Item: o))
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => x.Item)
                    .ToList();
            }

            if (items.Count == 0)
            {
                sb.Append(path).Append(" = <empty>\n");
                return;
            }

            for (var i = 0; i < items.Count; i++)
                Render(sb, $"{path}[{i.ToString(CultureInfo.InvariantCulture)}]", items[i], names, depth + 1, seen);
            return;
        }

        if (!value!.GetType().IsValueType && !seen.Add(value))
        {
            sb.Append(path).Append(" = <cycle>\n");
            return;
        }

        var members = MembersOf(value.GetType());
        if (members.Count == 0)
        {
            sb.Append(path).Append(" = ").Append(Quote(value.ToString())).Append('\n');
            return;
        }

        foreach (var (name, get) in members)
        {
            object? child;
            try
            {
                child = get(value);
            }
            catch (Exception ex)
            {
                sb.Append(path).Append('.').Append(name).Append(" !! ").Append(ex.GetType().Name).Append('\n');
                continue;
            }

            Render(sb, path + "." + name, child, names, depth + 1, seen);
        }
    }

    private static string RenderToString(object? value, Names names, int depth)
    {
        var sb = new StringBuilder();
        Render(sb, "\u00b7", value, names, depth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return sb.ToString();
    }

    private static bool TryScalar(object? value, Names names, out string rendered)
    {
        switch (value)
        {
            case null: rendered = "null"; return true;
            case Money m: rendered = m.ToString(); return true;
            case string s: rendered = Quote(s); return true;
            case bool b: rendered = b ? "true" : "false"; return true;
            case Guid g: rendered = names.Of(g); return true;
            case DateOnly d: rendered = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return true;
            case DateTime dt: rendered = dt.ToString("O", CultureInfo.InvariantCulture); return true;
            case TimeSpan ts: rendered = ts.ToString("c", CultureInfo.InvariantCulture); return true;
            case decimal dec: rendered = Dec(dec); return true;
            case double dbl: rendered = dbl.ToString("R", CultureInfo.InvariantCulture); return true;
            case float f: rendered = f.ToString("R", CultureInfo.InvariantCulture); return true;
            case Enum e: rendered = e.ToString(); return true;
        }

        var t = value.GetType();
        if (t.IsPrimitive)
        {
            rendered = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return true;
        }

        // A reference to a MASTER (or to another voucher) is an identity, not a subtree: render its token
        // rather than exploding the whole master into the dump.
        if (t.Namespace == "Apex.Ledger.Domain" && TryIdentity(value, t, out var id))
        {
            rendered = names.Of(id);
            return true;
        }

        rendered = "";
        return false;
    }

    private static bool TryIdentity(object value, Type t, out Guid id)
    {
        id = Guid.Empty;
        var idProp = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProp is null || idProp.PropertyType != typeof(Guid)) return false;

        var hasName = t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string);
        var isVoucher = value is Voucher or InventoryVoucher;
        if (!hasName && !isVoucher) return false;

        id = (Guid)idProp.GetValue(value)!;
        return true;
    }

    private static readonly Dictionary<Type, IReadOnlyList<(string Name, Func<object, object?> Get)>> MemberCache = new();

    /// <summary>
    /// The public instance properties and fields of a type, ORDERED BY NAME. Reflection does not guarantee
    /// declaration order, so name order is what makes the dump canonical across runs.
    /// </summary>
    private static IReadOnlyList<(string Name, Func<object, object?> Get)> MembersOf(Type t)
    {
        lock (MemberCache)
        {
            if (MemberCache.TryGetValue(t, out var cached)) return cached;

            var list = new List<(string, Func<object, object?>)>();

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (!p.CanRead) continue;
                if (p.Name == "EqualityContract") continue;
                var prop = p;
                list.Add((p.Name, o => prop.GetValue(o)));
            }

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var field = f;
                list.Add((f.Name, o => field.GetValue(o)));
            }

            var ordered = list.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
            var result = ordered.Select(x => (Name: x.Item1, Get: x.Item2)).ToList();
            MemberCache[t] = result;
            return result;
        }
    }

    /// <summary>
    /// A scale-normalising decimal format. <c>decimal</c> PRESERVES trailing zeros, so <c>3.75m</c> and
    /// <c>3.750000m</c> are equal but render differently under the default format — which would make two
    /// equal books produce different snapshots. "0.##########" strips the tail.
    /// </summary>
    private static string Dec(decimal d) => d.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string Quote(string? s) =>
        s is null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

    // ---------------------------------------------------------------------------------------------------
    // Guid normalisation.
    // ---------------------------------------------------------------------------------------------------

    private sealed class Names
    {
        private readonly Dictionary<Guid, string> _known = new();
        private readonly Dictionary<Guid, string> _ordinal = new();
        private int _next;

        public Names(Company company)
        {
            // Every master collection on the aggregate that exposes an (Id, Name) pair, by reflection — so a
            // master type added later is normalised on the day it is added.
            foreach (var prop in typeof(Company).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.PropertyType == typeof(string)) continue;
                if (!typeof(IEnumerable).IsAssignableFrom(prop.PropertyType)) continue;

                object? value;
                try { value = prop.GetValue(company); } catch { continue; }
                if (value is not IEnumerable seq) continue;

                foreach (var item in seq)
                {
                    if (item is null) continue;
                    var t = item.GetType();
                    var idProp = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                    var nameProp = t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                    if (idProp?.PropertyType != typeof(Guid) || nameProp?.PropertyType != typeof(string)) continue;

                    var id = (Guid)idProp.GetValue(item)!;
                    if (id == Guid.Empty || _known.ContainsKey(id)) continue;
                    _known[id] = prop.Name + ":" + (nameProp.GetValue(item) as string ?? "");
                }
            }

            // A voucher has no Name. Its token carries the three identities S5a exists to preserve — type,
            // NUMBER and LIST INDEX — so losing any of them shows up EVERYWHERE the voucher is referenced,
            // not only in section 12.
            for (var i = 0; i < company.Vouchers.Count; i++)
            {
                var v = company.Vouchers[i];
                var typeName = company.FindVoucherType(v.TypeId)?.Name ?? "?";
                _known[v.Id] = $"V:{typeName}#{v.Number.ToString(CultureInfo.InvariantCulture)}@{i.ToString(CultureInfo.InvariantCulture)}";
            }

            for (var i = 0; i < company.InventoryVouchers.Count; i++)
            {
                var v = company.InventoryVouchers[i];
                var typeName = company.FindVoucherType(v.TypeId)?.Name ?? "?";
                _known[v.Id] = $"IV:{typeName}#{v.Number.ToString(CultureInfo.InvariantCulture)}@{i.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        public string Of(Guid id)
        {
            if (id == Guid.Empty) return "<empty-guid>";
            if (_known.TryGetValue(id, out var known)) return known;
            if (_ordinal.TryGetValue(id, out var ordinal)) return ordinal;

            ordinal = "#g" + (++_next).ToString(CultureInfo.InvariantCulture);
            _ordinal[id] = ordinal;
            return ordinal;
        }
    }
}
