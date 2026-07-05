using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Memorandum Register row (catalog §16 Exception Reports — "Memo"; RQ-5 part 2): a Memorandum voucher
/// (a non-accounting suspense memo) within the reporting period.
/// </summary>
public sealed record MemorandumRegisterRow(
    Guid VoucherId,
    DateOnly Date,
    int Number,
    string? PartyOrParticulars,
    Money Amount);

/// <summary>
/// The Memorandum Register exception report (catalog §16; RQ-5 part 2). Lists every voucher whose type derives
/// from <see cref="VoucherBaseType.Memorandum"/> dated within <c>[from, to]</c> — the suspense memos that never
/// touch the real books (<see cref="LedgerBalances.IsProvisionalBaseType"/>). Cancelled memos are excluded.
/// Each row's amount is the voucher's debit total (= credit total for a balanced memo). Rows are chronological
/// (then by number within a date); <see cref="Total"/> sums the row amounts. A <b>pure</b> projection — no UI,
/// no DB.
/// </summary>
public sealed record MemorandumRegister(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MemorandumRegisterRow> Rows,
    Money Total)
{
    /// <summary>Builds the Memorandum Register for the period <c>[from, to]</c>.</summary>
    public static MemorandumRegister Build(Company company, DateOnly from, DateOnly to)
    {
        var rows = new List<MemorandumRegisterRow>();
        var total = Money.Zero;

        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (company.FindVoucherType(v.TypeId)?.BaseType != VoucherBaseType.Memorandum) continue;

            string? particulars = v.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;
            particulars ??= v.Narration;

            rows.Add(new MemorandumRegisterRow(v.Id, v.Date, v.Number, particulars, v.TotalDebit));
            total += v.TotalDebit;
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : a.Number.CompareTo(b.Number);
        });
        return new MemorandumRegister(from, to, rows, total);
    }
}
