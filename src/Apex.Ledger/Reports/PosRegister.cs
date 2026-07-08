using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One POS Register row (catalog §11; Phase 6 slice 7 RQ-44; Study Guide pp.240–242; DP-6): a single POS
/// (POS-flagged Sales) voucher within the reporting period, decomposed into its tender split. This is the
/// day-close view of the till <b>without</b> any persisted session object (DP-6) — it is derived purely from
/// the ordinary Sales vouchers that carry <see cref="Voucher.PosTenders"/>. Per-tender columns show the
/// <b>posted</b> payable share for each kind (Cash carries the residual, never the tendered amount — the change
/// never hits the books, subtlety b/d/e), and <see cref="BillTotal"/> is their sum (= the voucher debit total).
/// </summary>
public sealed record PosRegisterRow(
    Guid VoucherId,
    DateOnly Date,
    int Number,
    string Party,
    Money Gift,
    Money Card,
    Money Cheque,
    Money Cash,
    Money BillTotal);

/// <summary>
/// The POS Register / Summary report (catalog §11; RQ-44; Study Guide pp.240–242; DP-6). Lists every non-cancelled
/// POS voucher — a Sales voucher whose type is <see cref="VoucherType.IsPosSales"/> — dated within <c>[from, to]</c>,
/// decomposed into its Gift / Card / Cheque / Cash tender shares plus the bill total, and foots per-tender +
/// grand totals. Rows are chronological (then by number within a date). A <b>pure</b> projection (no UI, no DB,
/// deterministic). Because a POS bill is an ordinary Sales voucher, these same vouchers ALSO flow unchanged into
/// the Sales Register / GSTR-1 / GSTR-3B (RQ-43) — this register is simply the tender-oriented cut of them.
/// </summary>
public sealed record PosRegister(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PosRegisterRow> Rows,
    Money TotalGift,
    Money TotalCard,
    Money TotalCheque,
    Money TotalCash,
    Money TotalBill)
{
    /// <summary>Builds the POS Register for the period <c>[from, to]</c>.</summary>
    public static PosRegister Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var rows = new List<PosRegisterRow>();
        Money tGift = Money.Zero, tCard = Money.Zero, tCheque = Money.Zero, tCash = Money.Zero, tBill = Money.Zero;

        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            if (!v.HasPosTenders) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !type.IsPosSales) continue;

            Money gift = Money.Zero, card = Money.Zero, cheque = Money.Zero, cash = Money.Zero;
            foreach (var t in v.PosTenders)
            {
                switch (t.Type)
                {
                    case PosTenderType.GiftVoucher: gift += t.Amount; break;
                    case PosTenderType.Card: card += t.Amount; break;
                    case PosTenderType.Cheque: cheque += t.Amount; break;
                    case PosTenderType.Cash: cash += t.Amount; break;
                }
            }

            var bill = v.PosTendersValue;
            // The party is informational on a POS bill (B2C walk-in when none) — surface its name or "(cash)".
            var party = v.PartyId is Guid pid ? company.FindLedger(pid)?.Name ?? "(cash)" : "(cash)";

            rows.Add(new PosRegisterRow(v.Id, v.Date, v.Number, party, gift, card, cheque, cash, bill));
            tGift += gift; tCard += card; tCheque += cheque; tCash += cash; tBill += bill;
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : a.Number.CompareTo(b.Number);
        });

        return new PosRegister(from, to, rows, tGift, tCard, tCheque, tCash, tBill);
    }
}
