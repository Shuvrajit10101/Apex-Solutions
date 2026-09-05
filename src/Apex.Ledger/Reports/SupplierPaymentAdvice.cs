using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One bill named on a supplier payment advice — the "Invoice numbers, Amounts paid" the vendor's own
/// configuration list asks the letter to carry.
/// </summary>
/// <param name="BillReference">The bill reference the payment is set against, or a synthetic caption for an
/// advance / on-account payment that names no bill.</param>
/// <param name="RefType">New / Agst Ref / Advance / On Account, as posted.</param>
/// <param name="Amount">The magnitude allocated to this bill.</param>
/// <param name="DueDate">The bill's due date where the allocation carries one; <c>null</c> otherwise.</param>
public sealed record SupplierPaymentAdviceBill(
    string BillReference,
    BillRefType RefType,
    Money Amount,
    DateOnly? DueDate);

/// <summary>
/// One advice — a single payment made to a single supplier.
/// </summary>
/// <param name="VoucherId">The Payment voucher paid by.</param>
/// <param name="VoucherNumber">Its raw number (for ordering).</param>
/// <param name="FormattedNumber">Its formatted voucher number.</param>
/// <param name="Date">The voucher date.</param>
/// <param name="PartyLedgerId">The supplier's ledger.</param>
/// <param name="PartyName">The supplier's name as the books hold it.</param>
/// <param name="AddresseeName">The supplier's mailing name where captured, else the ledger name.</param>
/// <param name="AddressLines">The supplier's mailing address lines; empty when none is captured.</param>
/// <param name="GrossAmount">What the bills add up to before deductions — the party debit as posted.</param>
/// <param name="TdsDeducted">The §194x withholding on the same voucher, or zero.</param>
/// <param name="NetPaid">What actually left the bank/cash for this supplier on this voucher.</param>
/// <param name="PaymentMode">Cheque/DD · NEFT · RTGS · Cash · Other, read from the bank line's allocation.
/// <c>null</c> when the payment carries no bank allocation at all (a cash payment out of a cash ledger).</param>
/// <param name="InstrumentNumber">The cheque / UTR number, or empty.</param>
/// <param name="InstrumentDate">The instrument date, or <c>null</c>.</param>
/// <param name="BankLedgerId">The bank the money left, or <see cref="Guid.Empty"/> for a cash payment.</param>
/// <param name="BankName">That bank ledger's name, or empty.</param>
/// <param name="BankDate">The date the bank statement cleared it; <c>null</c> ⇒ unreconciled.</param>
/// <param name="Bills">The bill-wise detail, in posted order.</param>
public sealed record SupplierPaymentAdviceRow(
    Guid VoucherId,
    int VoucherNumber,
    string FormattedNumber,
    DateOnly Date,
    Guid PartyLedgerId,
    string PartyName,
    string AddresseeName,
    IReadOnlyList<string> AddressLines,
    Money GrossAmount,
    Money TdsDeducted,
    Money NetPaid,
    BankTransactionType? PaymentMode,
    string InstrumentNumber,
    DateOnly? InstrumentDate,
    Guid BankLedgerId,
    string BankName,
    DateOnly? BankDate,
    IReadOnlyList<SupplierPaymentAdviceBill> Bills)
{
    /// <summary>True once the bank statement has cleared the payment — the vendor's "matched (reconciled)".</summary>
    public bool IsReconciled => BankDate is not null;
}

/// <summary>
/// The pure projection behind the <b>Payment Advice</b> banking report and its printed letter (catalog §8
/// Banking; census row 8.7).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/payment-advice/</c> — a report of "all payments made
/// to suppliers" showing whether each is "matched (reconciled) or not", from which a letter is printed carrying
/// (the page's own configuration list) "Invoice-wise details, such as: Invoice numbers, Amounts paid, Deductions
/// (if any), TDS details, Payment mode (NEFT, RTGS, cheque, etc.)", the party's contact details and address, and
/// bank transfer information.</para>
///
/// <para><b>🔴 THIS IS NOT THE PAYROLL PAYMENT ADVICE.</b> <see cref="PaymentAdvice"/> in this same namespace is
/// the payroll bank advice — employee, bank, IFSC, net pay — surfaced under Reports → Payroll Reports. This one
/// is the supplier advice: a different document, a different counterparty, a different menu. Overloading either
/// onto the other is the conflation census row 8.7 exists to record, so they are deliberately separate types
/// with separate report kinds.</para>
///
/// <para><b>Every figure is read off the POSTED legs.</b> The gross is the party debit as posted, the deduction is
/// the <see cref="TdsLineTax"/> that voucher actually carries, and the net is the bank/cash credit. Nothing is
/// re-derived from a live master at advice time, so an advice sent to a supplier states the debt the general
/// ledger recorded — the same rule the invoice projector keeps.</para>
///
/// <para>Pure: no UI, no DB, no clock, no RNG.</para>
/// </summary>
public static class SupplierPaymentAdvice
{
    /// <summary>The group every supplier ledger hangs under. Named once so the predicate cannot drift.</summary>
    public const string SupplierGroupName = "Sundry Creditors";

    /// <summary>
    /// The advices for payments made to suppliers in <paramref name="period"/>, oldest first.
    /// </summary>
    /// <param name="company">The books.</param>
    /// <param name="period">The window, inclusive of both ends.</param>
    /// <param name="partyLedgerId">Narrow to one supplier, or <c>null</c> for every supplier.</param>
    /// <param name="reconciledOnly">The vendor's reconciled-only filter (F8): drop payments the bank statement
    /// has not yet cleared.</param>
    public static IReadOnlyList<SupplierPaymentAdviceRow> Build(
        Company company,
        PeriodRange period,
        Guid? partyLedgerId = null,
        bool reconciledOnly = false)
    {
        ArgumentNullException.ThrowIfNull(company);

        var rows = new List<SupplierPaymentAdviceRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < period.From || v.Date > period.To) continue;

            var type = company.FindVoucherType(v.TypeId);
            if (type?.BaseType != VoucherBaseType.Payment) continue;
            if (!LedgerBalances.CountsAsOf(v, period.To, type.BaseType)) continue;

            // One advice per (party, voucher): the supplier legs of this payment, grouped by their ledger. A
            // payment settling two suppliers at once is two advices, because each supplier is sent its own letter.
            foreach (var group in SupplierLegs(company, v))
            {
                if (partyLedgerId is { } wantedParty && group.Key != wantedParty) continue;
                if (company.FindLedger(group.Key) is not { } party) continue;

                var bank = FindBankLine(company, v);
                var alloc = bank?.BankAllocation;
                if (reconciledOnly && alloc?.BankDate is null) continue;

                var gross = Money.Zero;
                var tds = Money.Zero;
                var bills = new List<SupplierPaymentAdviceBill>();

                foreach (var line in group.Lines)
                {
                    gross += line.Amount;
                    foreach (var b in line.BillAllocations)
                        bills.Add(new SupplierPaymentAdviceBill(
                            BillCaption(b),
                            b.RefType,
                            b.Amount,
                            b.DueDate));
                }

                // The withholding rides its OWN line on the same voucher (the "deduct in same voucher" shape), so
                // it is summed across the voucher rather than read off the party leg.
                foreach (var line in v.Lines)
                    if (line.Tds is { } t && t.DeducteeLedgerId == party.Id)
                        tds += t.TdsAmount;

                var bankLedger = bank is null ? null : company.FindLedger(bank.LedgerId);

                rows.Add(new SupplierPaymentAdviceRow(
                    v.Id,
                    v.Number,
                    company.FormatVoucherNumber(v),
                    v.Date,
                    party.Id,
                    party.Name,
                    AddresseeName(party),
                    AddressLines(party),
                    gross,
                    tds,
                    gross - tds,
                    alloc?.TransactionType,
                    alloc?.InstrumentNumber ?? string.Empty,
                    alloc?.InstrumentDate,
                    bankLedger?.Id ?? Guid.Empty,
                    bankLedger?.Name ?? string.Empty,
                    alloc?.BankDate,
                    bills));
            }
        }

        rows.Sort((a, b) =>
        {
            int byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            int byNumber = a.VoucherNumber.CompareTo(b.VoucherNumber);
            return byNumber != 0 ? byNumber : string.CompareOrdinal(a.PartyName, b.PartyName);
        });
        return rows;
    }

    /// <summary>True when this ledger is a supplier — under Sundry Creditors through the FULL ancestry, not just
    /// its direct parent, so a "Suppliers → Local" sub-group is still a supplier.</summary>
    public static bool IsSupplier(Domain.Ledger ledger, Company company)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(company);
        return ClassificationRules.GroupIsUnder(ledger.GroupId, SupplierGroupName, company);
    }

    /// <summary>The supplier debit legs of one payment voucher, grouped by supplier ledger and kept in posted order.</summary>
    private static List<(Guid Key, List<EntryLine> Lines)> SupplierLegs(Company company, Voucher voucher)
    {
        var groups = new List<(Guid Key, List<EntryLine> Lines)>();
        foreach (var line in voucher.Lines)
        {
            // A payment DEBITS the supplier — money going the other way is a receipt, not an advice.
            if (line.Side != DrCr.Debit) continue;
            if (company.FindLedger(line.LedgerId) is not { } led) continue;
            if (!IsSupplier(led, company)) continue;

            int at = groups.FindIndex(g => g.Key == line.LedgerId);
            if (at < 0) groups.Add((line.LedgerId, new List<EntryLine> { line }));
            else groups[at].Lines.Add(line);
        }
        return groups;
    }

    /// <summary>The bank line the money left by — the first credit on a bank ledger. <c>null</c> for a payment
    /// made out of cash, which is a legitimate advice with no bank-transfer block rather than a defect.</summary>
    private static EntryLine? FindBankLine(Company company, Voucher voucher)
    {
        foreach (var line in voucher.Lines)
        {
            if (line.Side != DrCr.Credit) continue;
            if (company.FindLedger(line.LedgerId) is not { } led) continue;
            if (ClassificationRules.IsBankLedger(led, company)) return line;
        }
        return null;
    }

    /// <summary>The letter's addressee — the party's captured mailing name, else the ledger name.</summary>
    public static string AddresseeName(Domain.Ledger party)
    {
        ArgumentNullException.ThrowIfNull(party);
        var mailing = party.Mailing?.MailingName;
        return string.IsNullOrWhiteSpace(mailing) ? party.Name : mailing.Trim();
    }

    /// <summary>The letter's address block, one entry per captured line. Empty when no address was captured —
    /// the letter then omits the block rather than printing an empty caption.</summary>
    public static IReadOnlyList<string> AddressLines(Domain.Ledger party)
    {
        ArgumentNullException.ThrowIfNull(party);
        var lines = new List<string>();
        var address = party.Mailing?.Address;
        if (!string.IsNullOrWhiteSpace(address))
            foreach (var l in address.Replace("\r\n", "\n").Split('\n'))
                if (!string.IsNullOrWhiteSpace(l)) lines.Add(l.Trim());

        var pin = party.Mailing?.Pincode;
        var country = party.Mailing?.Country;
        if (!string.IsNullOrWhiteSpace(pin)) lines.Add(pin.Trim());
        if (!string.IsNullOrWhiteSpace(country)) lines.Add(country.Trim());
        return lines;
    }

    /// <summary>How one bill allocation is named on the letter. An advance or on-account payment references no
    /// bill, so it is captioned as what it is rather than shown with a blank reference.</summary>
    public static string BillCaption(BillAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        if (!string.IsNullOrWhiteSpace(allocation.Name)) return allocation.Name;
        return allocation.RefType switch
        {
            BillRefType.Advance => "(advance)",
            BillRefType.OnAccount => "(on account)",
            _ => "(unreferenced)",
        };
    }

    /// <summary>The payment mode as it reads on the letter. A payment with no bank allocation names no mode.</summary>
    public static string PaymentModeText(BankTransactionType? mode) => mode switch
    {
        BankTransactionType.ChequeOrDD => "Cheque/DD",
        BankTransactionType.NEFT => "NEFT",
        BankTransactionType.RTGS => "RTGS",
        BankTransactionType.Cash => "Cash",
        BankTransactionType.Other => "Other",
        _ => string.Empty,
    };
}
