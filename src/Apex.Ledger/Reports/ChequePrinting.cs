using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One cheque waiting to be printed (or already printed) — a row of the <b>Cheque Printing</b> report
/// (catalog §8 Banking; census row 8.4).
/// </summary>
/// <param name="VoucherId">The payment voucher this cheque pays.</param>
/// <param name="VoucherNumber">Its raw number (for ordering).</param>
/// <param name="FormattedNumber">Its formatted voucher number (for display).</param>
/// <param name="Date">The voucher date.</param>
/// <param name="BankLedgerId">The bank ledger the cheque is drawn on.</param>
/// <param name="BankName">That ledger's name.</param>
/// <param name="FavouringName">Who the cheque is drawn in favour of.</param>
/// <param name="InstrumentNumber">The cheque number ("Inst. no.").</param>
/// <param name="InstrumentDate">The cheque date ("Inst. date"), or <c>null</c>.</param>
/// <param name="Amount">The cheque amount.</param>
/// <param name="Printed">Whether this cheque has already been marked printed.</param>
public sealed record ChequePrintingRow(
    Guid VoucherId,
    int VoucherNumber,
    string FormattedNumber,
    DateOnly Date,
    Guid BankLedgerId,
    string BankName,
    string FavouringName,
    string InstrumentNumber,
    DateOnly? InstrumentDate,
    Money Amount,
    bool Printed);

/// <summary>
/// The pure projection behind the <b>Cheque Printing</b> report (census row 8.4).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/print-cheques/</c>, section "Cheque Printing
/// Report": a report of the cheques <i>pending for printing</i>, scoped by a List of Banks, showing the
/// favouring name with the instrument number and date; <b>F8 "Include Printed"</b> widens it to the cheques
/// already printed; the selected cheques are printed in bulk. The companion section "Print Cheque from Payment
/// Voucher" is the single-cheque route, which is why row 8.4 needs both surfaces and not just one.</para>
///
/// <para><b>What qualifies as a cheque here — all four conditions, and each of them matters.</b> A row exists
/// only for a posted line that is (1) on a ledger whose <see cref="Domain.Ledger.EnableChequePrinting"/> is on —
/// the two v5 columns that had seventeen references and not one of them in the UI, which is precisely what this
/// report exists to make live; (2) a <b>credit</b> to that bank ledger, because a cheque you print is money
/// leaving your account and a receipt is somebody else's cheque; (3) carrying a
/// <see cref="BankTransactionType.ChequeOrDD"/> allocation — you cannot print a cheque for an RTGS; and (4)
/// carrying a non-blank instrument number, because the leaf is pre-numbered and a cheque with no number cannot
/// be matched to one.</para>
///
/// <para>Pure: no UI, no DB, no clock, no RNG.</para>
/// </summary>
public static class ChequePrinting
{
    /// <summary>
    /// The cheques drawn in <paramref name="period"/>, newest-last, optionally narrowed to one bank ledger.
    ///
    /// <para><paramref name="isPrinted"/> answers "has this bank's cheque number already been printed?". Until
    /// the printed flag persists it is left <c>null</c> and every row reads unprinted, which is exactly the
    /// pending-for-printing list the vendor describes. <paramref name="includePrinted"/> is F8.</para>
    /// </summary>
    public static IReadOnlyList<ChequePrintingRow> Build(
        Company company,
        PeriodRange period,
        Guid? bankLedgerId = null,
        bool includePrinted = false,
        Func<Guid, string, bool>? isPrinted = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var rows = new List<ChequePrintingRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < period.From || v.Date > period.To) continue;
            if (!LedgerBalances.CountsAsOf(v, period.To)) continue;

            foreach (var line in v.Lines)
            {
                if (line.BankAllocation is not { } alloc) continue;
                if (alloc.TransactionType != BankTransactionType.ChequeOrDD) continue;
                if (line.Side != DrCr.Credit) continue;               // money out of the bank
                if (string.IsNullOrWhiteSpace(alloc.InstrumentNumber)) continue;

                if (company.FindLedger(line.LedgerId) is not { } bank) continue;
                if (!bank.EnableChequePrinting) continue;
                if (bankLedgerId is { } wanted && bank.Id != wanted) continue;

                bool printed = isPrinted?.Invoke(bank.Id, alloc.InstrumentNumber) ?? false;
                if (printed && !includePrinted) continue;

                rows.Add(new ChequePrintingRow(
                    v.Id,
                    v.Number,
                    company.FormatVoucherNumber(v),
                    v.Date,
                    bank.Id,
                    bank.Name,
                    FavouringName(company, v, bank.Id),
                    alloc.InstrumentNumber,
                    alloc.InstrumentDate,
                    line.Amount,
                    printed));
            }
        }

        rows.Sort((a, b) =>
        {
            int byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            int byNumber = a.VoucherNumber.CompareTo(b.VoucherNumber);
            return byNumber != 0
                ? byNumber
                : string.CompareOrdinal(a.InstrumentNumber, b.InstrumentNumber);
        });
        return rows;
    }

    /// <summary>
    /// Who the cheque is drawn in favour of: the voucher's party ledger when one is recorded, otherwise the
    /// first non-bank ledger on the voucher (a cheque paying an expense direct still has to name someone).
    /// Blank only when neither exists, and a blank favouring name refuses to print (<c>ChequePdf.Validate</c>).
    /// </summary>
    public static string FavouringName(Company company, Voucher voucher, Guid bankLedgerId)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        if (voucher.PartyId is { } partyId && company.FindLedger(partyId) is { } party)
            return party.Name;

        foreach (var line in voucher.Lines)
        {
            if (line.LedgerId == bankLedgerId) continue;
            if (company.FindLedger(line.LedgerId) is { } other) return other.Name;
        }
        return string.Empty;
    }
}
