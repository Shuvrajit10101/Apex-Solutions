using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One month of a ledger's monthly summary: the month's debit and credit movement, how many vouchers
/// produced it, and the ledger's closing balance at the end of that month.
/// </summary>
public sealed record LedgerMonthlySummaryRow(
    MonthWindow Month,
    Money Debit,
    Money Credit,
    int VoucherCount,
    DrCr ClosingSide,
    Money ClosingAmount);

/// <summary>
/// The <b>Ledger Monthly Summary</b> — a row per month of the window, opening balance above and the
/// running closing carried down the column.
///
/// <para>This is census <b>T1-32</b>, the missing primitive: the wave-3 pass established from the vendor's
/// Cash/Bank Book page that picking a ledger opens the <i>monthly summary</i>, and that the voucher list
/// (our <see cref="LedgerBook"/>) is only what a month row drills into. Group Summary's documented drill
/// path terminates here too, which is why it is built once, here, rather than inside either caller.</para>
///
/// <para><b>What counts.</b> Movement is summed from vouchers that count under
/// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/>, so the final
/// <see cref="ClosingAmount"/> equals <see cref="LedgerBalances.Closing(Company, Domain.Ledger, DateOnly)"/>
/// as at the window end — the same figure the Trial Balance shows. A month with no postings still gets a
/// row, carrying the previous month's closing unchanged.</para>
/// </summary>
public sealed record LedgerMonthlySummary(
    Guid LedgerId,
    string LedgerName,
    DrCr OpeningSide,
    Money OpeningAmount,
    IReadOnlyList<LedgerMonthlySummaryRow> Rows,
    DrCr ClosingSide,
    Money ClosingAmount)
{
    /// <summary>Builds the ledger's monthly summary over <c>[from, to]</c>.</summary>
    public static LedgerMonthlySummary Build(Company company, Guid ledgerId, DateOnly from, DateOnly to)
    {
        // Defensive, like LedgerBook.Build: a drill launched from a synthetic row must never throw.
        if (ledgerId == Guid.Empty)
            return new LedgerMonthlySummary(ledgerId, string.Empty, DrCr.Debit, Money.Zero, [], DrCr.Debit, Money.Zero);

        var ledger = company.FindLedger(ledgerId)
            ?? throw new InvalidOperationException($"Ledger {ledgerId} not found.");

        // The opening shown at the top is the balance carried into the window — the ledger's own opening
        // plus everything posted before the window starts. With from == books-begin this is the plain
        // opening balance.
        var running = LedgerBalances.SignedClosing(company, ledger, from.AddDays(-1));
        var openingBalance = LedgerBalance.FromSigned(running);

        var rows = new List<LedgerMonthlySummaryRow>();
        foreach (var month in MonthAxis.Months(from, to))
        {
            var debit = 0m;
            var credit = 0m;
            var count = 0;

            foreach (var v in company.Vouchers)
            {
                if (v.Date < month.From || v.Date > month.To) continue;
                if (!LedgerBalances.CountsAsOf(v, month.To, company.FindVoucherType(v.TypeId)?.BaseType)) continue;

                var touched = false;
                foreach (var line in v.Lines)
                {
                    if (line.LedgerId != ledgerId) continue;
                    touched = true;
                    if (line.Side == DrCr.Debit) debit += line.Amount.Amount;
                    else credit += line.Amount.Amount;
                    running += line.Signed;
                }

                if (touched) count++;
            }

            var closing = LedgerBalance.FromSigned(running);
            rows.Add(new LedgerMonthlySummaryRow(
                month, new Money(debit), new Money(credit), count, closing.Side, closing.Amount));
        }

        var final = LedgerBalance.FromSigned(running);
        return new LedgerMonthlySummary(
            ledgerId, ledger.Name, openingBalance.Side, openingBalance.Amount, rows, final.Side, final.Amount);
    }
}
