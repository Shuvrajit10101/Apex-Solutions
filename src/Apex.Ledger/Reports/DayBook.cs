using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Day Book row (design §7.5). <see cref="VoucherId"/> is the underlying voucher's stable id
/// (RQ-7 universal drill-down): Enter on the row opens that voucher's detail. It is always a real id here
/// (every Day Book row IS a voucher), so <see cref="IsDrillable"/> is always true for Day Book rows.
/// </summary>
public sealed record DayBookRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    string? PartyOrParticulars,
    Money Amount,
    bool IsCancelled,
    Guid VoucherId = default)
{
    /// <summary>True iff Enter should drill this row into the underlying voucher's detail.</summary>
    public bool IsDrillable => VoucherId != Guid.Empty;
}

/// <summary>
/// The Day Book (design §7.5): all vouchers within a date range in chronological order
/// (then by number within a date). Cancelled vouchers are included but flagged (shown
/// greyed with zero effect); optional/post-dated vouchers still list. The amount is the
/// voucher's debit total (= credit total for a balanced voucher).
/// </summary>
public static class DayBook
{
    public static IReadOnlyList<DayBookRow> Build(Company company, DateOnly from, DateOnly to)
    {
        var rows = new List<DayBookRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            var typeName = type?.Name ?? "(unknown)";

            string? particulars = null;
            if (v.PartyId is Guid partyId)
                particulars = company.FindLedger(partyId)?.Name;
            particulars ??= v.Narration;

            rows.Add(new DayBookRow(v.Date, typeName, v.Number, particulars, v.TotalDebit, v.Cancelled, v.Id));
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : a.Number.CompareTo(b.Number);
        });

        return rows;
    }
}
