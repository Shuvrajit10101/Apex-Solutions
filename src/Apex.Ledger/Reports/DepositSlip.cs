using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The two deposit slips the vendor ships (census row 8.6) — <c>help.tallysolutions.com/deposit-slips/</c>.
/// <b>There are two, not one</b>, and they carry different columns, which is why this is a mode rather than a
/// filter on one report.
/// </summary>
public enum DepositSlipKind
{
    /// <summary>"Cash Deposit Slip" — cash paid in over the counter (vendor chord <b>F5</b>).</summary>
    Cash = 0,

    /// <summary>"Cheque Deposit Slip" — instruments banked for collection, one line per instrument.</summary>
    Cheque = 1,
}

/// <summary>One line of a deposit slip.</summary>
/// <param name="VoucherId">The receipt voucher — the drill.</param>
/// <param name="Date">The voucher date.</param>
/// <param name="FormattedNumber">Its formatted voucher number.</param>
/// <param name="ReceivedFrom">"Company/person name (cheque source)" — the party, or the contra ledger.</param>
/// <param name="InstrumentNumber">The cheque number; blank on a cash slip.</param>
/// <param name="InstrumentDate">The cheque date, or <c>null</c>.</param>
/// <param name="Amount">The amount banked.</param>
public sealed record DepositSlipLine(
    Guid VoucherId,
    DateOnly Date,
    string FormattedNumber,
    string ReceivedFrom,
    string InstrumentNumber,
    DateOnly? InstrumentDate,
    Money Amount);

/// <summary>
/// A whole deposit slip: the bank's header block plus its lines.
/// </summary>
/// <param name="Kind">Cash or Cheque.</param>
/// <param name="BankLedgerId">The bank account being paid into.</param>
/// <param name="BankName">The bank's name as it should read on the slip.</param>
/// <param name="AccountNumber">"Account Number", or blank when the bank ledger has not captured one.</param>
/// <param name="AccountHolderName">"Account Holder Name" — the COMPANY's mailing name; the account is in its name.</param>
/// <param name="BranchName">"Branch Name", or blank when not captured.</param>
/// <param name="From">Period start.</param>
/// <param name="To">Period end.</param>
/// <param name="Lines">The receipts being banked.</param>
public sealed record DepositSlipReport(
    DepositSlipKind Kind,
    Guid BankLedgerId,
    string BankName,
    string AccountNumber,
    string AccountHolderName,
    string BranchName,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DepositSlipLine> Lines)
{
    /// <summary>The slip total — what the counter clerk checks the paper against.</summary>
    public Money Total => Lines.Aggregate(Money.Zero, (acc, l) => acc + l.Amount);
}

/// <summary>
/// The pure projection behind the <b>Deposit Slip</b> (census row 8.6) — the pay-in slip an operator carries to
/// the bank with the cash or the cheques.
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/deposit-slips/</c>. The <b>Cash Deposit Slip</b>
/// prints <i>Account Number · Account Holder Name · Bank Name · Branch Name · Company's Telephone Number · Cash
/// Denomination Details</i>; the <b>Cheque Deposit Slip</b> prints, per instrument, <i>Company/person name
/// (cheque source) · Account Holder's Name · Bank Name · Branch Name · Cheque Date · Cheque No. · Amount</i>.
/// The two modes are switched with <b>F5</b> and the bank with <b>F4</b>.</para>
///
/// <para><b>🔴 SOURCE SILENCE, STATED RATHER THAN INVENTED (ruling 14 / "clone, never invent").</b>
/// <list type="bullet">
///   <item><b>Company's Telephone Number</b> — the vendor's slip carries it and <c>companies</c> has <b>no</b>
///     telephone column anywhere in this schema. It is therefore NOT printed and NOT captioned. Adding a company
///     telephone is a Company-master change and belongs to whoever owns that master.</item>
///   <item><b>Cash Denomination Details</b> — a tally of the notes being carried to the bank. It is not in the
///     books and cannot be derived from them, so this engine does not fabricate one.</item>
///   <item><b>Account Holder Name</b> is deliberately NOT a new stored field: it is the company's mailing name,
///     because that is whose account it is.</item>
/// </list></para>
///
/// <para>Pure: no UI, no DB, no clock, no RNG.</para>
/// </summary>
public static class DepositSlip
{
    /// <summary>
    /// The receipts banked into <paramref name="bankLedger"/> in <paramref name="period"/>, in the requested mode.
    ///
    /// <para><b>Receipt side only, and that is the definition of the document.</b> A deposit slip is what you hand
    /// the teller when you PAY IN; a payment out of the same account has no place on it. So only DEBITS to the
    /// bank ledger are collected — the mirror of the Cheque Printing report's credit-only rule.</para>
    /// </summary>
    public static DepositSlipReport Build(
        Company company, Domain.Ledger bankLedger, PeriodRange period, DepositSlipKind kind)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(bankLedger);

        var wanted = kind == DepositSlipKind.Cash ? BankTransactionType.Cash : BankTransactionType.ChequeOrDD;
        var lines = new List<DepositSlipLine>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < period.From || v.Date > period.To) continue;
            // Base type passed, so Memorandum and Reversing-Journal vouchers — which never reach the books —
            // cannot put money on a slip the operator is about to hand a teller.
            if (!LedgerBalances.CountsAsOf(v, period.To, company.FindVoucherType(v.TypeId)?.BaseType)) continue;

            foreach (var line in v.Lines)
            {
                if (line.LedgerId != bankLedger.Id) continue;
                if (line.Side != DrCr.Debit) continue;             // money INTO the bank
                var alloc = line.BankAllocation;
                if ((alloc?.TransactionType ?? BankTransactionType.Other) != wanted) continue;

                lines.Add(new DepositSlipLine(
                    v.Id,
                    v.Date,
                    company.FormatVoucherNumber(v),
                    ReceivedFrom(company, v, bankLedger.Id),
                    kind == DepositSlipKind.Cheque ? (alloc?.InstrumentNumber ?? string.Empty) : string.Empty,
                    kind == DepositSlipKind.Cheque ? alloc?.InstrumentDate : null,
                    line.Amount));
            }
        }

        lines.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            var byNumber = string.CompareOrdinal(a.InstrumentNumber, b.InstrumentNumber);
            return byNumber != 0 ? byNumber : string.CompareOrdinal(a.FormattedNumber, b.FormattedNumber);
        });

        return new DepositSlipReport(
            kind,
            bankLedger.Id,
            // "Bank Name": the cheque-stationery name when one was captured, else the ledger's own name.
            string.IsNullOrWhiteSpace(bankLedger.ChequePrintingBankName)
                ? bankLedger.Name
                : bankLedger.ChequePrintingBankName!,
            bankLedger.BankAccountNumber ?? string.Empty,
            AccountHolder(company),
            bankLedger.BankBranch ?? string.Empty,
            period.From,
            period.To,
            lines);
    }

    /// <summary>The account holder — the company's mailing name where it has one, else its name. Never a stored
    /// field of its own: a second copy of "whose account is this" could contradict the company master.</summary>
    private static string AccountHolder(Company company) =>
        string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName!;

    /// <summary>
    /// "Company/person name (cheque source)": the voucher's party when one is recorded, otherwise the first
    /// non-bank ledger on the voucher — a cash sale banked direct still has to name where the money came from.
    /// </summary>
    private static string ReceivedFrom(Company company, Voucher voucher, Guid bankLedgerId)
    {
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
