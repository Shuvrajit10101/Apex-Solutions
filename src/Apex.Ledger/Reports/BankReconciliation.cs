using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One bank transaction in the Bank Reconciliation projection (catalog §8): a single posting to a bank
/// ledger, with its voucher context, its signed movement, the instrument details, and the
/// <see cref="BankDate"/> (set once the transaction has cleared, else <c>null</c> = unreconciled).
/// </summary>
public sealed record BankTransactionRow(
    Guid VoucherId,
    int VoucherNumber,
    DateOnly Date,
    Guid LedgerId,
    DrCr Side,
    Money Amount,
    BankTransactionType TransactionType,
    string InstrumentNumber,
    DateOnly? InstrumentDate,
    DateOnly? BankDate)
{
    /// <summary>Signed contribution to the bank balance: +amount for a debit, −amount for a credit.</summary>
    public decimal Signed => Side == DrCr.Debit ? Amount.Amount : -Amount.Amount;

    /// <summary>True once a <see cref="BankDate"/> has been recorded (cleared the bank).</summary>
    public bool IsReconciled => BankDate is not null;
}

/// <summary>
/// The Bank Reconciliation Statement (BRS) for one bank ledger as of a date (catalog §8): the bank's
/// transactions, the <b>Balance as per Company Books</b>, and the <b>Balance as per Bank</b> (the book
/// balance adjusted for amounts that have not yet cleared the bank as of the reconciliation date).
/// </summary>
public sealed record BankReconciliationReport(
    Guid BankLedgerId,
    string BankLedgerName,
    DateOnly AsOf,
    IReadOnlyList<BankTransactionRow> Transactions,
    LedgerBalance BalanceAsPerBooks,
    LedgerBalance BalanceAsPerBank)
{
    /// <summary>The transactions still awaiting a Bank Date (uncleared as of <see cref="AsOf"/>).</summary>
    public IReadOnlyList<BankTransactionRow> Unreconciled =>
        Transactions.Where(t => t.BankDate is null || t.BankDate > AsOf).ToList();

    /// <summary>The transactions that have cleared on/before <see cref="AsOf"/>.</summary>
    public IReadOnlyList<BankTransactionRow> Reconciled =>
        Transactions.Where(t => t.BankDate is not null && t.BankDate <= AsOf).ToList();

    /// <summary>
    /// The net signed amount not yet reflected in the bank (Σ signed of the uncleared transactions):
    /// <c>BalanceAsPerBooks − BalanceAsPerBank</c> in signed terms.
    /// </summary>
    public Money AmountNotReflectedInBank =>
        new(BalanceAsPerBooks.Signed - BalanceAsPerBank.Signed);
}

/// <summary>
/// Pure Bank Reconciliation projection + reconcile API over the posted voucher set (catalog §8;
/// plan.md §5, §8). No UI, no DB. It lists the bank ledger's transactions (each with its Bank Date or
/// blank), computes the Balance-as-per-Books vs Balance-as-per-Bank pair, and exposes
/// <see cref="SetBankDate"/> to record (or clear) a transaction's Bank Date.
/// </summary>
public static class BankReconciliation
{
    /// <summary>
    /// Builds the BRS for <paramref name="bankLedger"/> as of <paramref name="asOf"/>. Transactions are
    /// the bank ledger's postings from counted vouchers dated ≤ <paramref name="asOf"/> (Cancelled/
    /// Optional and not-yet-due PostDated excluded, per <see cref="LedgerBalances.CountsAsOf"/>), in
    /// voucher-date then voucher-number order.
    /// </summary>
    /// <remarks>
    /// <b>Balance as per Company Books</b> = the bank ledger's closing balance as of the date (opening +
    /// all counted movements). <b>Balance as per Bank</b> = the book balance MINUS the signed movements
    /// that have not cleared the bank by <paramref name="asOf"/> (Bank Date null, or later than the
    /// reconciliation date). So an uncleared issued cheque (a credit to the bank) still sits in the bank
    /// balance until it is presented — exactly the BRS reconciliation behaviour.
    /// </remarks>
    public static BankReconciliationReport Build(Company company, Domain.Ledger bankLedger, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(bankLedger);

        var rows = Transactions(company, bankLedger, asOf);

        var booksSigned = LedgerBalances.SignedClosing(company, bankLedger, asOf);

        // Subtract the movements that have not yet cleared the bank as of the reconciliation date.
        var unclearedSigned = 0m;
        foreach (var t in rows)
            if (t.BankDate is null || t.BankDate > asOf)
                unclearedSigned += t.Signed;

        var bankSigned = booksSigned - unclearedSigned;

        return new BankReconciliationReport(
            bankLedger.Id,
            bankLedger.Name,
            asOf,
            rows,
            LedgerBalance.FromSigned(booksSigned),
            LedgerBalance.FromSigned(bankSigned));
    }

    /// <summary>
    /// The bank ledger's transactions as of <paramref name="asOf"/> — the rows the BRS screen binds to.
    /// Each carries the line's bank allocation (transaction type / instrument / bank date) when present;
    /// a bank line entered without an allocation defaults to an <see cref="BankTransactionType.Other"/>,
    /// blank-instrument, unreconciled row.
    /// </summary>
    public static IReadOnlyList<BankTransactionRow> Transactions(Company company, Domain.Ledger bankLedger, DateOnly asOf)
    {
        var rows = new List<BankTransactionRow>();
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, asOf)) continue;
            foreach (var line in v.Lines)
            {
                if (line.LedgerId != bankLedger.Id) continue;
                var alloc = line.BankAllocation;
                rows.Add(new BankTransactionRow(
                    v.Id,
                    v.Number,
                    v.Date,
                    line.LedgerId,
                    line.Side,
                    line.Amount,
                    alloc?.TransactionType ?? BankTransactionType.Other,
                    alloc?.InstrumentNumber ?? string.Empty,
                    alloc?.InstrumentDate,
                    alloc?.BankDate));
            }
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : a.VoucherNumber.CompareTo(b.VoucherNumber);
        });
        return rows;
    }

    /// <summary>
    /// Records (or clears) the <b>Bank Date</b> on the bank allocation of the <paramref name="bankLedger"/>
    /// line of voucher <paramref name="voucherId"/> — the reconcile action (catalog §8 "enter the Bank
    /// Date per line"). Pass <c>null</c> to un-reconcile. Returns true if a matching bank line was found
    /// and updated; false otherwise. Throws if the voucher/line exists but carries no bank allocation.
    /// </summary>
    public static bool SetBankDate(Company company, Guid voucherId, Guid bankLedgerId, DateOnly? bankDate)
    {
        ArgumentNullException.ThrowIfNull(company);
        var voucher = company.FindVoucher(voucherId);
        if (voucher is null) return false;

        var found = false;
        foreach (var line in voucher.Lines)
        {
            if (line.LedgerId != bankLedgerId) continue;
            if (line.BankAllocation is null)
                throw new InvalidOperationException(
                    $"Voucher {voucherId} line for ledger {bankLedgerId} has no bank allocation to reconcile.");
            line.BankAllocation.BankDate = bankDate;
            found = true;
        }
        return found;
    }
}
