using System.Globalization;
using System.Text;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The section of the <b>e-Payments report</b> a transaction sits in (census row 8.10) —
/// <c>help.tallysolutions.com/e-payments-report/</c>, whose bucket names these mirror verbatim.
/// </summary>
public enum EPaymentStatus
{
    /// <summary>
    /// <b>"Ready for Sending to Bank"</b> — everything the instruction file needs is present on both masters.
    /// </summary>
    ReadyForSendingToBank = 0,

    /// <summary>
    /// <b>"Incomplete/Incorrect Bank Ledger Master Details"</b> — the vendor's own words for a bank ledger
    /// <i>"missing mandatory fields like Account Number or IFS Code"</i>. Listed FIRST in the vendor's report and
    /// resolved first here: one bad bank master invalidates every payment drawn on it, so telling the operator
    /// about the payee instead would send them to the wrong screen n times over.
    /// </summary>
    IncompleteBankLedgerMasterDetails = 1,

    /// <summary>
    /// <b>"Incomplete/Incorrect Transaction Details"</b> — the vendor's <i>"Missing or incorrect party's bank
    /// details such as Account No. or IFS Code"</i>.
    /// </summary>
    IncompleteTransactionDetails = 2,
}

/// <summary>One transaction on the e-Payments report — a payment leaving a bank account by electronic transfer,
/// with both sides' bank details and the reason it is or is not exportable.</summary>
/// <param name="VoucherId">The payment voucher — the drill.</param>
/// <param name="Date">The voucher date.</param>
/// <param name="FormattedNumber">Its formatted voucher number.</param>
/// <param name="BankLedgerId">The remitting bank account.</param>
/// <param name="BankLedgerName">Its name.</param>
/// <param name="BankAccountNumber">The remitter's account number, blank when the master does not carry one.</param>
/// <param name="BankIfsc">The remitter's IFSC, blank when the master does not carry one.</param>
/// <param name="PayeeLedgerId">The beneficiary ledger, or <c>null</c> when the entry has no single other side.</param>
/// <param name="PayeeName">The beneficiary's ledger name.</param>
/// <param name="PayeeAccountNumber">The beneficiary's account number, blank when not captured.</param>
/// <param name="PayeeIfsc">The beneficiary's IFSC, blank when not captured.</param>
/// <param name="TransactionType">NEFT or RTGS — the payment mode the instruction carries.</param>
/// <param name="InstrumentNumber">The operator's own reference for the transfer, or blank.</param>
/// <param name="Amount">The amount leaving the account.</param>
/// <param name="Status">Which section of the report this row is in.</param>
/// <param name="Reason">What to fix, in the operator's terms; empty when the row is ready.</param>
public sealed record EPaymentRow(
    Guid VoucherId,
    DateOnly Date,
    string FormattedNumber,
    Guid BankLedgerId,
    string BankLedgerName,
    string BankAccountNumber,
    string BankIfsc,
    Guid? PayeeLedgerId,
    string PayeeName,
    string PayeeAccountNumber,
    string PayeeIfsc,
    BankTransactionType TransactionType,
    string InstrumentNumber,
    Money Amount,
    EPaymentStatus Status,
    string Reason)
{
    /// <summary>True when this row can go into a payment-instruction file.</summary>
    public bool IsReady => Status == EPaymentStatus.ReadyForSendingToBank;
}

/// <summary>The e-Payments report over a period: every electronic payment, bucketed.</summary>
public sealed record EPaymentsReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<EPaymentRow> Rows)
{
    /// <summary>The vendor's <b>"Ready for Sending to Bank"</b> section — the only rows a file is built from.</summary>
    public IReadOnlyList<EPaymentRow> ReadyForSendingToBank =>
        Rows.Where(r => r.Status == EPaymentStatus.ReadyForSendingToBank).ToList();

    /// <summary>The vendor's <b>"Incomplete/Incorrect Bank Ledger Master Details"</b> section.</summary>
    public IReadOnlyList<EPaymentRow> IncompleteBankLedgerMaster =>
        Rows.Where(r => r.Status == EPaymentStatus.IncompleteBankLedgerMasterDetails).ToList();

    /// <summary>The vendor's <b>"Incomplete/Incorrect Transaction Details"</b> section.</summary>
    public IReadOnlyList<EPaymentRow> IncompleteTransactionDetails =>
        Rows.Where(r => r.Status == EPaymentStatus.IncompleteTransactionDetails).ToList();

    /// <summary>The value that would go to the bank if the file were exported now.</summary>
    public Money ReadyTotal =>
        ReadyForSendingToBank.Aggregate(Money.Zero, (acc, r) => acc + r.Amount);
}

/// <summary>
/// <b>e-Payments</b> (census row 8.10) — <c>help.tallysolutions.com/tally-prime/banking-utilities/e-payments-tally/</c>,
/// <c>help.tallysolutions.com/e-payments-report/</c> and <c>help.tallysolutions.com/export-upload-e-payments/</c>.
/// The report tells an operator which electronic payments are exportable and, for the rest, exactly what is
/// missing; <see cref="BuildPaymentInstructionFile"/> renders the exportable ones as the file the bank ingests.
///
/// <para>🔴 <b>THE FILE LAYOUT IS OURS, AND THAT IS STATED HERE BECAUSE THE VENDOR DOES NOT PUBLISH ONE.</b>
/// The vendor's own pages say a payment instruction <i>"contains all the details required to process the payments
/// on the bank portal"</i> and route the export through <b>Alt+E (Export) &gt; Payment Instructions</b>, but they
/// name <b>no</b> file format, <b>no</b> field list and <b>no</b> individual bank's layout — they refer the reader
/// to their bank. There is therefore no bank format attested well enough to clone, and inventing one would be
/// worse than useless: a malformed instruction file either fails at the bank or pays the wrong party. So this
/// exports a <b>documented, self-describing CSV</b> carrying the fields a credit transfer actually needs —
/// beneficiary name, account number and IFSC, amount, value date, payment mode and reference, plus the remitter's
/// account — under a header row that names each one. It is labelled in the file itself as Apex's own layout, NOT
/// as any bank's. Cloning a real bank's fixed-width or XML layout is a separate piece of work that needs that
/// bank's published specification in hand.</para>
///
/// <para>🔴 <b>WHAT THE VENDOR'S REPORT HAS THAT THIS ONE DOES NOT, named rather than quietly dropped.</b>
/// <list type="bullet">
///   <item><b>"Sent to Bank (Unreconciled)"</b> and its <i>In Progress / Successful / Unsuccessful</i>
///     sub-sections. Every one of those is a fact about what happened AFTER an export — which file a payment went
///     out in, and what the bank's reverse file said about it. Nothing in this schema records either, and this
///     track carries no schema budget, so the sections are absent rather than faked. A "Successful" column derived
///     from anything currently stored would be a guess printed next to money.</item>
///   <item><b>"Mismatch in Bank Details (With Masters)"</b> — the bucket for a transaction whose captured bank
///     details differ from the party master's. It presupposes that a transaction carries its OWN copy of the
///     payee's account number; here a <see cref="BankAllocation"/> carries a transaction type, an instrument
///     number and dates, and the payee's account lives only on the master. With one copy there is nothing to
///     mismatch, so the bucket cannot exist yet — it arrives with a per-transaction payee-details schema change.</item>
///   <item><b>Sending payments directly from the product</b> (the vendor's "Send Payments" over a bank
///     connection). That is a subscription/connected-banking feature — census rows 8.11 / 8.12 — and is out of
///     scope by §1.1 rule 4. Only the export half is built.</item>
/// </list></para>
///
/// <para>Pure: no UI, no file IO, no clock, no RNG. The caller writes the returned text wherever it likes.</para>
/// </summary>
public static class EPayments
{
    /// <summary>
    /// The electronic payment modes an e-payment can be. <b>NEFT and RTGS only</b>, and the exclusion of
    /// <see cref="BankTransactionType.Other"/> is deliberate: that member's own definition is
    /// <i>"any other electronic/manual transfer (IMPS, UPI, inter-bank, adjustment, …)"</i>, so it conflates a
    /// real IMPS payment with a book adjustment and nothing stored can tell them apart. Sweeping it in would put
    /// adjustments in front of a bank; leaving it out costs IMPS/UPI, which is the smaller and the honest error.
    /// Widening this is a <see cref="BankTransactionType"/> change, not an e-Payments change.
    /// </summary>
    private static bool IsElectronic(BankTransactionType t) =>
        t is BankTransactionType.NEFT or BankTransactionType.RTGS;

    /// <summary>
    /// Builds the report for <paramref name="period"/>, over <paramref name="bankLedger"/> or — when that is
    /// <c>null</c> — every bank account the company has.
    ///
    /// <para><b>Payments only, and that is the definition.</b> An e-payment moves money OUT, so only CREDITS to a
    /// bank ledger are collected. A receipt into the same account is somebody else's instruction file.</para>
    /// </summary>
    public static EPaymentsReport Build(
        Company company, Domain.Ledger? bankLedger, PeriodRange period)
    {
        ArgumentNullException.ThrowIfNull(company);

        var rows = new List<EPaymentRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < period.From || v.Date > period.To) continue;
            // Base type passed, so a Memorandum or a Reversing Journal — neither of which reaches the books —
            // cannot put an instruction in front of a bank. Optional vouchers are excluded by the same predicate,
            // which is what keeps an unreviewed auto-created entry (census 8.13) out of a payment file.
            if (!LedgerBalances.CountsAsOf(v, period.To, company.FindVoucherType(v.TypeId)?.BaseType)) continue;

            foreach (var line in v.Lines)
            {
                if (bankLedger is not null && line.LedgerId != bankLedger.Id) continue;
                if (line.Side != DrCr.Credit) continue;                     // money OUT of the bank

                var bank = company.FindLedger(line.LedgerId);
                if (bank is null || !ClassificationRules.IsBankLedger(bank, company)) continue;

                var alloc = line.BankAllocation;
                if (alloc is null || !IsElectronic(alloc.TransactionType)) continue;

                rows.Add(Classify(company, v, line, bank, alloc));
            }
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            return string.CompareOrdinal(a.FormattedNumber, b.FormattedNumber);
        });

        return new EPaymentsReport(period.From, period.To, rows);
    }

    private static EPaymentRow Classify(
        Company company, Voucher voucher, EntryLine bankLine, Domain.Ledger bank, BankAllocation alloc)
    {
        // The beneficiary: the voucher's declared party where there is one, else the single non-bank ledger on
        // the entry. An entry that splits across several ledgers has no ONE beneficiary and is reported as such
        // rather than having one of them picked for it.
        var payee = voucher.PartyId is { } pid ? company.FindLedger(pid) : null;
        if (payee is null)
        {
            var others = voucher.Lines
                .Where(l => l.LedgerId != bankLine.LedgerId)
                .Select(l => l.LedgerId)
                .Distinct()
                .ToList();
            if (others.Count == 1) payee = company.FindLedger(others[0]);
        }

        var bankAcct = Trim(bank.BankAccountNumber);
        var bankIfsc = Trim(bank.BankIfsc);
        var payeeAcct = Trim(payee?.BankAccountNumber);
        var payeeIfsc = Trim(payee?.BankIfsc);

        var (status, reason) = Verdict(bank, bankAcct, bankIfsc, payee, payeeAcct, payeeIfsc);

        return new EPaymentRow(
            voucher.Id,
            voucher.Date,
            company.FormatVoucherNumber(voucher),
            bank.Id,
            bank.Name,
            bankAcct,
            bankIfsc,
            payee?.Id,
            payee?.Name ?? string.Empty,
            payeeAcct,
            payeeIfsc,
            alloc.TransactionType,
            alloc.InstrumentNumber,
            bankLine.Amount,
            status,
            reason);
    }

    /// <summary>
    /// The bucket and the sentence that goes with it. The bank master is judged FIRST, matching the vendor's own
    /// ordering: one incomplete bank ledger blocks every payment drawn on it, so naming the payee instead would
    /// send the operator to the wrong screen once per row.
    /// </summary>
    private static (EPaymentStatus Status, string Reason) Verdict(
        Domain.Ledger bank, string bankAcct, string bankIfsc,
        Domain.Ledger? payee, string payeeAcct, string payeeIfsc)
    {
        var bankMissing = Missing(bankAcct, bankIfsc);
        if (bankMissing.Length > 0)
            return (EPaymentStatus.IncompleteBankLedgerMasterDetails,
                $"The bank ledger '{bank.Name}' has no {bankMissing}. Record it on Masters > Ledgers > "
                + bank.Name + " (Bank Identity).");

        if (payee is null)
            return (EPaymentStatus.IncompleteTransactionDetails,
                "This payment has no single beneficiary ledger, so there is nobody to pay. Record it against one "
                + "party, or split it into one payment per beneficiary.");

        var payeeMissing = Missing(payeeAcct, payeeIfsc);
        if (payeeMissing.Length > 0)
            return (EPaymentStatus.IncompleteTransactionDetails,
                $"The beneficiary '{payee.Name}' has no {payeeMissing}. Record it on Masters > Ledgers > "
                + payee.Name + " (Beneficiary Bank Details).");

        return (EPaymentStatus.ReadyForSendingToBank, string.Empty);
    }

    /// <summary>Names which of the two mandatory identifiers are absent, in the vendor's own vocabulary
    /// ("Account Number or IFS Code"). Empty when both are present.</summary>
    private static string Missing(string account, string ifsc) =>
        (account.Length == 0, ifsc.Length == 0) switch
        {
            (true, true) => "account number or IFS code",
            (true, false) => "account number",
            (false, true) => "IFS code",
            _ => string.Empty,
        };

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    /// <summary>
    /// Renders the <b>payment instruction file</b> for the report's ready rows — the vendor's
    /// <b>Alt+E (Export) &gt; Payment Instructions</b>.
    ///
    /// <para>🔴 <b>This is Apex's own documented CSV, not any bank's layout</b>, and it says so on its first line.
    /// See the type doc for why: the vendor publishes no format and no bank's specification is in hand, so the
    /// honest thing is a self-describing file whose every column is named, rather than a fixed-width guess that a
    /// bank would either reject or misread. Rows that are not ready are NOT written — a file that quietly carried
    /// a payment with no beneficiary account is the failure this whole report exists to prevent.</para>
    ///
    /// <para><b>Cross-platform, deliberately.</b> Dates are ISO and amounts are rendered with the invariant
    /// culture, so a machine whose locale writes <c>1.234,56</c> cannot ship a file the bank reads as a different
    /// number. Line endings are <c>\n</c> for the same reason.</para>
    /// </summary>
    public static string BuildPaymentInstructionFile(Company company, EPaymentsReport report)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.Append("# Apex Solutions payment instruction file (Apex CSV layout v1 - not a bank-specific format)\n");
        sb.Append($"# Remitter,{Csv(company.Name)}\n");
        sb.Append($"# Period,{Iso(report.From)},{Iso(report.To)}\n");
        sb.Append("Beneficiary Name,Beneficiary Account Number,Beneficiary IFSC,Amount,Value Date,"
                  + "Payment Mode,Reference,Remitter Account Number,Remitter IFSC,Voucher Number\n");

        foreach (var r in report.ReadyForSendingToBank)
            sb.Append(string.Join(",",
                Csv(r.PayeeName),
                Csv(r.PayeeAccountNumber),
                Csv(r.PayeeIfsc),
                Csv(r.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(Iso(r.Date)),
                Csv(r.TransactionType.ToString()),
                Csv(r.InstrumentNumber),
                Csv(r.BankAccountNumber),
                Csv(r.BankIfsc),
                Csv(r.FormattedNumber))).Append('\n');

        return sb.ToString();
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Quotes a CSV field, and neutralises a spreadsheet FORMULA before it is quoted.
    ///
    /// <para>🔴 <b>Why the formula guard is here and not only the quoting.</b> Every field in this file comes from
    /// a ledger NAME or a bank reference that a human typed, and a payment instruction file is opened in a
    /// spreadsheet by an accounts clerk before it is uploaded. A ledger called <c>=cmd|'/c calc'!A1</c> would
    /// execute on open. A leading <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is therefore prefixed with an
    /// apostrophe, which spreadsheets read as "this is text" and which leaves the visible value intact.</para>
    ///
    /// <para><b>It matches <c>Apex.Ledger.Io.DelimitedText.Quote</c> deliberately, character for character,
    /// leading-space skip included</b> — that is this product's established guard, and two guards that disagree
    /// are worse than one. It is duplicated rather than shared only because <c>Apex.Ledger</c> does not (and
    /// should not) reference <c>Apex.Ledger.Io</c>; the dependency points the other way. The guard PREFIXES and
    /// never rewrites, so the field's own bytes survive verbatim after the quote.</para>
    /// </summary>
    internal static string Csv(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0)
        {
            var i = 0;
            while (i < v.Length && v[i] == ' ') i++;
            var c = i < v.Length ? v[i] : v[0];
            if (c is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        }
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}
