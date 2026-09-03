using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Reversing Journal Register row (catalog §16 Exception Reports — "Reversing"; RQ-5 part 2): a Reversing
/// Journal voucher within the reporting period, carrying its "Applicable upto" (effective) date.
/// </summary>
public sealed record ReversingJournalRegisterRow(
    Guid VoucherId,
    DateOnly Date,
    DateOnly? ApplicableUpto,
    int Number,
    string? Particulars,
    Money Amount,
    string FormattedNumber = "");

/// <summary>
/// The Reversing Journal Register exception report (catalog §16; RQ-5 part 2). Lists every voucher whose type
/// derives from <see cref="VoucherBaseType.ReversingJournal"/> dated within <c>[from, to]</c> — the what-if
/// accrual entries that reverse out after their <see cref="Voucher.ApplicableUpto"/> and never touch the real
/// books (<see cref="LedgerBalances.IsProvisionalBaseType"/>). Each row shows the voucher date, its
/// "Applicable upto" (effective) date, number, particulars and amount (the debit total). Cancelled reversing
/// journals are excluded. Rows are chronological (then by number within a date); <see cref="Total"/> sums the
/// row amounts. A <b>pure</b> projection — no UI, no DB.
/// </summary>
public sealed record ReversingJournalRegister(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ReversingJournalRegisterRow> Rows,
    Money Total)
{
    /// <summary>Builds the Reversing Journal Register for the period <c>[from, to]</c>.</summary>
    public static ReversingJournalRegister Build(Company company, DateOnly from, DateOnly to)
    {
        var rows = new List<ReversingJournalRegisterRow>();
        var total = Money.Zero;

        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (company.FindVoucherType(v.TypeId)?.BaseType != VoucherBaseType.ReversingJournal) continue;

            string? particulars = v.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;
            particulars ??= v.Narration;

            rows.Add(new ReversingJournalRegisterRow(
                v.Id, v.Date, v.ApplicableUpto, v.Number, particulars, v.TotalDebit,
                company.FormatVoucherNumber(v)));
            total += v.TotalDebit;
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : a.Number.CompareTo(b.Number);
        });
        return new ReversingJournalRegister(from, to, rows, total);
    }
}
