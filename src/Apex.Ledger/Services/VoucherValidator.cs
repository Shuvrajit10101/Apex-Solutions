using Apex.Ledger.Domain;

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
        if (c.FindVoucherType(v.TypeId) is null)
            throw new InvalidVoucherException($"Unknown voucher type {v.TypeId}.");

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
        }

        // §6.9 date within books.
        if (v.Date < c.BooksBeginFrom)
            throw new InvalidVoucherException(
                $"Voucher date {v.Date:yyyy-MM-dd} is before BooksBeginFrom {c.BooksBeginFrom:yyyy-MM-dd}.");

        // §6.1 the golden invariant: Σ Dr == Σ Cr.
        if (!IsBalanced(v))
            throw new UnbalancedVoucherException(v.TotalDebit, v.TotalCredit);
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
}
