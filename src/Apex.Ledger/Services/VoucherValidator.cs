using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The §6 posting invariants, factored out so they are directly unit-testable and
/// shared by <see cref="LedgerService.Post"/>. All money math is in <see cref="Money"/>
/// (decimal) — never <c>double</c>.
/// </summary>
public static class VoucherValidator
{
    /// <summary>Σ debit magnitudes and Σ credit magnitudes over the voucher's lines.</summary>
    public static (Money Debit, Money Credit) Totals(Voucher v) => (v.TotalDebit, v.TotalCredit);

    /// <summary>True iff Σ Dr == Σ Cr (in decimal).</summary>
    public static bool IsBalanced(Voucher v) => v.TotalDebit == v.TotalCredit;

    /// <summary>
    /// Enforces every §6 invariant relevant to posting; throws on the first violation
    /// (never persists a bad voucher). Checks, in order: ≥ 2 lines, positive line amounts,
    /// known-ledger references, date within books, and the balanced-voucher invariant.
    /// </summary>
    public static void EnsureValid(Voucher v, Company c)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(c);

        // §6.5 referential integrity: the voucher type must be known.
        var voucherType = c.FindVoucherType(v.TypeId);
        if (voucherType is null)
            throw new InvalidVoucherException($"Unknown voucher type {v.TypeId}.");

        // §11 zero-valued transactions (Phase 6 slice 4 RQ-21): "Allow zero-valued transactions" is a Sales/Purchase
        // feature only. A Journal / Stock-Journal (or any other base) type must never carry it — reject at post time
        // so the illegal configuration can never smuggle a ₹0 accounting entry onto a non-invoice voucher.
        if (voucherType.AllowZeroValuedTransactions &&
            voucherType.BaseType is not (VoucherBaseType.Purchase or VoucherBaseType.Sales))
            throw new InvalidVoucherException(
                $"'Allow zero-valued transactions' is only valid on a Purchase or Sales voucher type; " +
                $"'{voucherType.Name}' is a {voucherType.BaseType}.");

        // §6.2 at least two lines.
        if (v.Lines.Count < 2)
            throw new InvalidVoucherException("A voucher must have at least two entry lines.");

        // §6.3 positive line amounts + §6.5 known ledgers + §5 bill-wise integrity.
        foreach (var line in v.Lines)
        {
            if (line.Amount.Amount <= 0m)
                throw new InvalidVoucherException("Every entry line amount must be > 0.");
            var ledger = c.FindLedger(line.LedgerId)
                ?? throw new InvalidVoucherException($"Entry line references unknown ledger {line.LedgerId}.");

            if (line.HasBillAllocations)
                EnsureBillAllocationsValid(line, ledger);

            if (line.HasCostAllocations)
                EnsureCostAllocationsValid(line, ledger, c);

            if (line.HasBankAllocation)
                EnsureBankAllocationValid(line, ledger, c);

            if (line.HasForex)
                EnsureForexValid(line, ledger, c);
        }

        // §6.9 date within books.
        if (v.Date < c.BooksBeginFrom)
            throw new InvalidVoucherException(
                $"Voucher date {v.Date:yyyy-MM-dd} is before BooksBeginFrom {c.BooksBeginFrom:yyyy-MM-dd}.");

        // §6.1 the golden invariant: Σ Dr == Σ Cr.
        if (!IsBalanced(v))
            throw new UnbalancedVoucherException(v.TotalDebit, v.TotalCredit);

        // §10 item-invoice mode (slice 3.3b): the accounts↔inventory pairing invariant.
        if (v.HasInventoryLines)
            EnsureItemInvoiceValid(v, c);
    }

    /// <summary>
    /// The item-invoice pairing invariant (catalog §10; phase3-inventory-requirements RQ-16/RQ-17; slice 3.3b).
    /// Item lines are permitted only on a Purchase or Sales voucher whose type moves stock, every line must
    /// reference a known stock item and godown, and — critically — the item lines' <b>total value</b>
    /// (Σ qty × rate) must reconcile with the voucher's <b>stock accounting amount</b> so the inward/outward is
    /// always backed by an accounting posting (no unbacked stock, no phantom profit). The exact rule:
    /// <list type="bullet">
    ///   <item><b>Purchase</b>: Σ item-line value == Σ of the <b>debit</b>-line amounts posted to ledgers under
    ///     <b>Purchase Accounts</b> or <b>Stock-in-Hand</b> (the stock-in leg).</item>
    ///   <item><b>Sales</b>: Σ item-line value == Σ of the <b>credit</b>-line amounts posted to ledgers under
    ///     <b>Sales Accounts</b> (the sales leg).</item>
    /// </list>
    /// A mismatch, item lines on a non-Purchase/Sales (or non-stock-affecting) type, or an unknown item/godown
    /// reference all throw a clean <see cref="InvalidVoucherException"/>.
    /// </summary>
    public static void EnsureItemInvoiceValid(Voucher v, Company c)
    {
        var type = c.FindVoucherType(v.TypeId)!; // referential integrity already checked above
        var isPurchase = type.BaseType == VoucherBaseType.Purchase;
        var isSales = type.BaseType == VoucherBaseType.Sales;
        if (!isPurchase && !isSales)
            throw new InvalidVoucherException(
                $"Item-invoice stock lines are only valid on a Purchase or Sales voucher; '{type.Name}' is neither.");

        // The implied direction: Purchase ⇒ inward, Sales ⇒ outward. Every item line must already carry it
        // (the posting service stamps it), so the on-hand engine reads the direction directly.
        var expectedDir = isPurchase ? StockDirection.Inward : StockDirection.Outward;
        foreach (var line in v.InventoryLines)
        {
            if (c.FindStockItem(line.StockItemId) is null)
                throw new InvalidVoucherException($"Item-invoice line references unknown stock item {line.StockItemId}.");
            if (c.FindGodown(line.GodownId) is null)
                throw new InvalidVoucherException($"Item-invoice line references unknown godown {line.GodownId}.");
            if (line.Direction != expectedDir)
                throw new InvalidVoucherException(
                    $"Item-invoice line direction {line.Direction} does not match the '{type.Name}' nature " +
                    $"(expected {expectedDir}).");
            // Zero-value guard (Phase 6 slice 4 RQ-21, ER-7 surgical relaxation). A zero-rate / zero-value line
            // normally injects unbacked stock (phantom on-hand / phantom profit) that slips through the pairing
            // check, so it stays rejected — UNLESS this Sales/Purchase type has "Allow zero-valued transactions"
            // on, in which case a ₹0 free-goods line is a legitimate entry (it moves stock but posts ₹0, and the
            // pairing invariant still balances ₹0 against ₹0). The relaxation is scoped to zero-valued-enabled
            // types only; a normal invoice still rejects a fat-finger ₹0 line, and a positive-value line is never
            // affected.
            if (!type.AllowZeroValuedTransactions && (line.Rate.Amount <= 0m || line.Value.Amount <= 0m))
                throw new InvalidVoucherException(
                    "Item-invoice line rate must be greater than zero (a zero-rate line would move stock with no " +
                    "accounting backing).");
        }

        // Σ of the accounting stock leg: Purchase = debit lines to Purchase Accounts / Stock-in-Hand ledgers;
        // Sales = credit lines to Sales Accounts ledgers.
        var wantSide = isPurchase ? DrCr.Debit : DrCr.Credit;
        var accountingStockAmount = 0m;
        foreach (var line in v.Lines)
        {
            if (line.Side != wantSide) continue;
            var ledger = c.FindLedger(line.LedgerId);
            if (ledger is null) continue; // already validated above
            if (IsStockLegLedger(ledger, c, isPurchase))
                accountingStockAmount += line.Amount.Amount;
        }

        var itemLinesValue = v.InventoryLinesValue.Amount;
        if (accountingStockAmount != itemLinesValue)
        {
            var leg = isPurchase ? "Purchases / Stock-in-Hand (debit)" : "Sales (credit)";
            throw new InvalidVoucherException(
                $"Item-invoice pairing: the item lines total ₹{itemLinesValue:0.00} (Σ qty × rate) does not equal " +
                $"the voucher's {leg} accounting amount ₹{accountingStockAmount:0.00}. The stock leg must be backed " +
                "by an equal accounting posting so no unbacked stock is created.");
        }
    }

    /// <summary>
    /// Whether a ledger is the accounting "stock leg" for an item-invoice: for a Purchase, a ledger under
    /// <b>Purchase Accounts</b> (primary ancestor) or under <b>Stock-in-Hand</b>; for a Sales, a ledger under
    /// <b>Sales Accounts</b> (primary ancestor).
    /// </summary>
    private static bool IsStockLegLedger(Domain.Ledger ledger, Company c, bool isPurchase)
    {
        var group = c.FindGroup(ledger.GroupId);
        if (group is null) return false;
        if (isPurchase)
        {
            if (ClassificationRules.IsStockInHandLedger(ledger, c)) return true;
            return string.Equals(ClassificationRules.PrimaryAncestorOf(group, c).Name, "Purchase Accounts",
                StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(ClassificationRules.PrimaryAncestorOf(group, c).Name, "Sales Accounts",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// §5 bill-wise integrity for one line: allocations are only permitted on a bill-by-bill ledger,
    /// and their magnitudes must <b>sum exactly to the line amount</b> ("split"). Throws otherwise.
    /// </summary>
    public static void EnsureBillAllocationsValid(Domain.EntryLine line, Domain.Ledger ledger)
    {
        if (!ledger.MaintainBillByBill)
            throw new InvalidVoucherException(
                $"Ledger '{ledger.Name}' does not maintain balances bill-by-bill; it cannot carry bill allocations.");

        if (line.BillAllocationTotal != line.Amount)
            throw new InvalidVoucherException(
                $"Bill allocations on the line for '{ledger.Name}' sum to {line.BillAllocationTotal} " +
                $"but the line amount is {line.Amount}; they must be equal (split).");
    }

    /// <summary>
    /// §6 cost-centre integrity for one line: cost allocations are only permitted on a ledger with cost
    /// centres applicable, every allocation must reference a known category and a known centre that
    /// belongs to that category, and their magnitudes must <b>sum exactly to the line amount</b>
    /// ("split across centres"). Throws otherwise.
    /// </summary>
    public static void EnsureCostAllocationsValid(Domain.EntryLine line, Domain.Ledger ledger, Company c)
    {
        if (!ClassificationRules.CostCentresApplicableFor(ledger, c))
            throw new InvalidVoucherException(
                $"Ledger '{ledger.Name}' does not have cost centres applicable; it cannot carry cost allocations.");

        foreach (var a in line.CostAllocations)
        {
            var category = c.FindCostCategory(a.CategoryId)
                ?? throw new InvalidVoucherException(
                    $"Cost allocation on the line for '{ledger.Name}' references unknown cost category {a.CategoryId}.");
            var centre = c.FindCostCentre(a.CentreId)
                ?? throw new InvalidVoucherException(
                    $"Cost allocation on the line for '{ledger.Name}' references unknown cost centre {a.CentreId}.");
            if (centre.CategoryId != category.Id)
                throw new InvalidVoucherException(
                    $"Cost centre '{centre.Name}' does not belong to category '{category.Name}'.");
        }

        if (line.CostAllocationTotal != line.Amount)
            throw new InvalidVoucherException(
                $"Cost allocations on the line for '{ledger.Name}' sum to {line.CostAllocationTotal} " +
                $"but the line amount is {line.Amount}; they must be equal (split across centres).");
    }

    /// <summary>
    /// §8 banking integrity for one line: a bank allocation is only permitted on a bank ledger
    /// (a ledger under Bank Accounts / Bank OD A/c). The allocation carries no amount of its own — it
    /// annotates the whole line — so there is no split-sum check; it is enough that the ledger is a bank.
    /// Throws otherwise.
    /// </summary>
    public static void EnsureBankAllocationValid(Domain.EntryLine line, Domain.Ledger ledger, Company c)
    {
        if (!ClassificationRules.IsBankLedger(ledger, c))
            throw new InvalidVoucherException(
                $"Ledger '{ledger.Name}' is not a bank account; it cannot carry a bank allocation.");
    }

    /// <summary>One paisa, the coarsest base-currency unit — the tolerance a rounded forex base may differ by.</summary>
    private const decimal OnePaisa = 0.01m;

    /// <summary>
    /// Multi-currency integrity for one line (catalog §2/§20): the forex detail must reference a known
    /// currency, and the line's base <see cref="Domain.EntryLine.Amount"/> must equal the
    /// <b>paisa-rounded</b> <c>ForexAmount × Rate</c> (<see cref="ForexInfo.BaseValue"/>), so the base ledger
    /// math is unchanged. Because a non-round rate makes the raw product carry a sub-paisa tail, the base is
    /// the product snapped to the paisa; a base off by <b>more than a paisa</b> (or an unknown currency) is
    /// rejected. Throws otherwise.
    /// </summary>
    public static void EnsureForexValid(Domain.EntryLine line, Domain.Ledger ledger, Company c)
    {
        var forex = line.Forex!;
        if (c.FindCurrency(forex.CurrencyId) is null)
            throw new InvalidVoucherException(
                $"Forex on the line for '{ledger.Name}' references unknown currency {forex.CurrencyId}.");

        // BaseValue is the paisa-rounded forex × rate; the line's base must match it to within one paisa,
        // so a base that carries the unrounded sub-paisa tail (or the rounded value) both pass, but a base
        // that is genuinely wrong (off by more than a paisa) is rejected.
        var expected = forex.BaseValue; // paisa-exact
        if (Math.Abs((line.Amount - expected).Amount) > OnePaisa)
            throw new InvalidVoucherException(
                $"Forex on the line for '{ledger.Name}': {forex.ForexAmount} × {forex.Rate} ≈ {expected} " +
                $"(paisa-rounded) does not equal the base line amount {line.Amount}; the base amount must be " +
                $"forex × rate rounded to the paisa.");
    }
}
