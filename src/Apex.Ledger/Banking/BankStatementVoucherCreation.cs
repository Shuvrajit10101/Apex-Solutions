using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Banking;

/// <summary>
/// How many vouchers a selection of bank-statement lines turns into (census row 8.13) —
/// <c>help.tallysolutions.com/auto-create-vouchers/</c>, which offers exactly these two shapes on two chords:
/// <b>F7</b> "Create Vch/Multi-Vch" and <b>Alt+F7</b> "Create Voucher(Consolidate)".
/// </summary>
public enum BankStatementVoucherMode
{
    /// <summary>
    /// <b>F7 — "Create Vch/Multi-Vch".</b> One voucher per selected statement line. Selecting a single line is
    /// the vendor's third case ("single voucher from a bank entry"); it is not a separate mode, it is this mode
    /// with one row selected, which is why only two members exist here.
    /// </summary>
    Multiple = 0,

    /// <summary>
    /// <b>Alt+F7 — "Create Voucher(Consolidate)".</b> ONE voucher for the whole selection, whose
    /// <i>"Amount is the combined amount of the bank entries selected"</i> (vendor, verbatim).
    /// </summary>
    Consolidate = 1,
}

/// <summary>
/// One selected statement line plus the ledger the operator typed against it. The vendor's screen calls that
/// column <b>Ledger Name</b> and requires it per transaction before F7 will create anything — a bank line on its
/// own says only that money moved, never what it was for, so there is no default to guess and none is guessed.
/// </summary>
/// <param name="Row">The imported statement line.</param>
/// <param name="ContraLedgerId">The NON-bank side of the entry (the expense/income/party ledger).</param>
public sealed record BankStatementVoucherRequest(BankStatementRow Row, Guid ContraLedgerId);

/// <summary>
/// A voucher auto-created from the bank statement, as the <b>"Bank Reconciliation – Optional Vouchers"</b> screen
/// shows it. Every one of these is <see cref="Voucher.Optional"/> until a human presses <b>R</b>.
/// </summary>
/// <param name="VoucherId">The created voucher — the drill target.</param>
/// <param name="Date">Its date (the bank's value date for the line, or the latest of them when consolidated).</param>
/// <param name="FormattedNumber">Its formatted voucher number.</param>
/// <param name="VoucherTypeName">Receipt or Payment, by the direction the money moved.</param>
/// <param name="ContraLedgerName">The Ledger Name the operator supplied.</param>
/// <param name="Amount">The magnitude posted.</param>
/// <param name="BankSide">Debit when money came into the bank, Credit when it left.</param>
/// <param name="InstrumentNumber">The statement's reference, or blank.</param>
/// <param name="SourceRowCount">How many statement lines this voucher came from (>1 only when consolidated).</param>
public sealed record AutoCreatedVoucherRow(
    Guid VoucherId,
    DateOnly Date,
    string FormattedNumber,
    string VoucherTypeName,
    string ContraLedgerName,
    Money Amount,
    DrCr BankSide,
    string InstrumentNumber,
    int SourceRowCount)
{
    /// <summary>"Receipt" rows brought money in; "Payment" rows took it out.</summary>
    public bool IsMoneyIn => BankSide == DrCr.Debit;
}

/// <summary>
/// <b>Auto-creation of vouchers from an imported bank statement</b> (census row 8.13) —
/// <c>help.tallysolutions.com/auto-create-vouchers/</c>. The operator ticks unmatched statement lines on the
/// Import Bank Statement page, names a ledger against each, and presses <b>F7</b> (one voucher per line) or
/// <b>Alt+F7</b> (one consolidated voucher). The vouchers created land in the
/// <b>"Bank Reconciliation – Optional Vouchers"</b> section and are reviewed there; <b>R</b> —
/// <i>"Mark as Regular &amp; Reconcile"</i> — is what finally puts them on the books.
///
/// <para>🔴 <b>THE OPTIONAL MARKING IS THE SAFETY PROPERTY, AND IT IS STRUCTURAL HERE, NOT COSMETIC.</b>
/// <see cref="Reports.LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> excludes an Optional
/// voucher from every balance, and <see cref="BankReconciliation.Transactions"/> is built on that same predicate —
/// so a voucher created here contributes NOTHING to the Trial Balance, the Balance Sheet, the bank ledger's
/// closing balance or the BRS until <see cref="MarkAsRegularAndReconcile"/> runs. That is deliberate and it is the
/// whole point: an import of two hundred bank lines that posted silently would be a wrong-money defect scaled by
/// the size of the file. <c>AutoCreatedVouchersDoNotTouchTheBooks</c> in the engine tests pins it.</para>
///
/// <para><b>The order inside <see cref="MarkAsRegularAndReconcile"/> is load-bearing.</b> Regularise FIRST, then
/// stamp the Bank Date. While the voucher is Optional it is not in <see cref="BankReconciliation.Transactions"/>
/// at all, so a reconcile attempted first would be writing a clearance onto a row the BRS cannot see.</para>
///
/// <para>🔴 <b>WHERE THE VENDOR IS SILENT, STATED RATHER THAN INVENTED (ruling 14).</b>
/// <list type="bullet">
///   <item><b>The bank transaction type.</b> A statement CSV carries a date, a narration, an amount and a
///     reference; it does not say whether the reference is a cheque number, a UTR or a UPI handle. The allocation
///     is therefore written as <see cref="BankTransactionType.Other"/> — the enum's own
///     "any other electronic/manual transfer" member — carrying the statement's reference as the instrument
///     number. Guessing ChequeOrDD from a numeric reference would be inventing an instrument.</item>
///   <item><b>Instrument date</b> is left <c>null</c>. The statement's date is the bank's VALUE date, which is not
///     the date written on an instrument, and nothing in the file supplies the latter.</item>
///   <item><b>The date of a CONSOLIDATED voucher</b> is the LATEST of its constituent lines. The vendor states the
///     amount rule ("the combined amount") and says nothing about the date. The latest is chosen because a single
///     entry standing for several movements cannot honestly be dated before the last of them had happened; it is
///     ours, and it is labelled as ours.</item>
///   <item><b>The consolidated instrument number</b> is blank: several lines carry several references and no one
///     of them describes the combined entry.</item>
/// </list></para>
///
/// <para>Pure engine: no UI, no file IO, no clock, no RNG beyond the voucher's own <see cref="Guid"/>.</para>
/// </summary>
public static class BankStatementVoucherCreation
{
    /// <summary>
    /// Creates the <b>Optional</b> vouchers for <paramref name="requests"/> against
    /// <paramref name="bankLedger"/>, in the requested <paramref name="mode"/>, and returns them in the shape the
    /// "Bank Reconciliation – Optional Vouchers" screen lists.
    ///
    /// <para>Direction is read from the statement, not asked for: a line that brought money IN debits the bank and
    /// credits the named ledger (a <b>Receipt</b>); a line that took money OUT credits the bank and debits the
    /// named ledger (a <b>Payment</b>). A zero-amount line is refused — there is nothing to post and the product
    /// only accepts a zero-valued voucher where its type opts in.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The selection is empty, a request names a ledger this company does not
    /// have, a line's amount is zero, or (in <see cref="BankStatementVoucherMode.Consolidate"/>) the selection
    /// mixes directions or ledgers.</exception>
    /// <exception cref="InvalidOperationException">The company has no active Receipt / Payment voucher type.</exception>
    public static IReadOnlyList<AutoCreatedVoucherRow> CreateOptionalVouchers(
        LedgerService service,
        Company company,
        Domain.Ledger bankLedger,
        IReadOnlyList<BankStatementVoucherRequest> requests,
        BankStatementVoucherMode mode)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(bankLedger);
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
            throw new ArgumentException(
                "Select at least one statement line before creating vouchers.", nameof(requests));

        foreach (var r in requests)
        {
            if (r.Row is null)
                throw new ArgumentException("A selected statement line is missing.", nameof(requests));
            if (r.Row.Magnitude.Amount == 0m)
                throw new ArgumentException(
                    $"The statement line dated {r.Row.Date:yyyy-MM-dd} has a zero amount; there is nothing to post.",
                    nameof(requests));
            if (company.FindLedger(r.ContraLedgerId) is null)
                throw new ArgumentException(
                    $"Ledger {r.ContraLedgerId} is not a ledger of this company. Fill the Ledger Name column "
                    + "against every selected line before creating vouchers.",
                    nameof(requests));
            if (r.ContraLedgerId == bankLedger.Id)
                throw new ArgumentException(
                    $"The statement line dated {r.Row.Date:yyyy-MM-dd} names the bank account itself as its "
                    + "Ledger Name. A bank line's other side must be a different ledger.",
                    nameof(requests));

            // 🔴 §6.9 "date within books", checked HERE rather than left to the validator, and this is not
            // belt-and-braces. `Post` mutates the company one voucher at a time, so in Multiple mode a line that
            // the validator refused HALFWAY through a selection would leave the earlier vouchers posted and throw
            // — a half-created batch out of an operation the operator issued once. Measured: importing a
            // statement whose lines predate BooksBeginFrom threw InvalidVoucherException (which derives from
            // Exception, not from InvalidOperationException) straight out of the page. Pre-checking every line
            // before posting any of them makes the refusal all-or-nothing, and makes it an ArgumentException the
            // caller already handles.
            if (r.Row.Date < company.BooksBeginFrom)
                throw new ArgumentException(
                    $"The statement line dated {r.Row.Date:yyyy-MM-dd} is before this company's books begin "
                    + $"({company.BooksBeginFrom:yyyy-MM-dd}), so no voucher can be dated to it. Nothing was "
                    + "created; narrow the statement file or change the books-begin date.",
                    nameof(requests));
        }

        return mode == BankStatementVoucherMode.Consolidate
            ? new[] { CreateConsolidated(service, company, bankLedger, requests) }
            : requests
                .Select(r => CreateOne(service, company, bankLedger, new[] { r }, r.Row.Date, r.Row.InstrumentNumber))
                .ToList();
    }

    private static AutoCreatedVoucherRow CreateConsolidated(
        LedgerService service,
        Company company,
        Domain.Ledger bankLedger,
        IReadOnlyList<BankStatementVoucherRequest> requests)
    {
        var first = requests[0];
        var moneyIn = first.Row.Signed > 0m;

        foreach (var r in requests)
        {
            if ((r.Row.Signed > 0m) != moneyIn)
                throw new ArgumentException(
                    "A consolidated voucher cannot mix money coming in with money going out. Select lines that "
                    + "move in one direction, or press F7 to create them one voucher each.",
                    nameof(requests));
            if (r.ContraLedgerId != first.ContraLedgerId)
                throw new ArgumentException(
                    "A consolidated voucher carries ONE Ledger Name for the whole selection. Give every selected "
                    + "line the same ledger, or press F7 to create them one voucher each.",
                    nameof(requests));
        }

        // The latest constituent date — see the type doc for why this is ours and not the vendor's.
        var date = requests.Max(r => r.Row.Date);
        return CreateOne(service, company, bankLedger, requests, date, instrumentNumber: string.Empty);
    }

    private static AutoCreatedVoucherRow CreateOne(
        LedgerService service,
        Company company,
        Domain.Ledger bankLedger,
        IReadOnlyList<BankStatementVoucherRequest> requests,
        DateOnly date,
        string instrumentNumber)
    {
        var moneyIn = requests[0].Row.Signed > 0m;
        var total = new Money(requests.Sum(r => r.Row.Magnitude.Amount));
        var contraId = requests[0].ContraLedgerId;
        var contra = company.FindLedger(contraId)!;

        var wanted = moneyIn ? VoucherBaseType.Receipt : VoucherBaseType.Payment;
        var type = ResolveType(company, wanted);

        var bankSide = moneyIn ? DrCr.Debit : DrCr.Credit;
        var contraSide = moneyIn ? DrCr.Credit : DrCr.Debit;

        var allocation = new BankAllocation(
            BankTransactionType.Other,
            instrumentNumber: instrumentNumber,
            instrumentDate: null,
            bankDate: null);            // NOT reconciled — R does that, after regularising.

        var narration = BuildNarration(requests);

        var voucher = new Voucher(
            Guid.NewGuid(),
            type.Id,
            date,
            new[]
            {
                new EntryLine(bankLedger.Id, total, bankSide, bankAllocation: allocation),
                new EntryLine(contraId, total, contraSide),
            },
            number: 0,
            narration: narration,
            partyId: null,
            cancelled: false,
            optional: true,             // 🔴 THE SAFETY PROPERTY. Reviewed by a human before it reaches the books.
            postDated: false,
            applicableUpto: null);

        service.Post(voucher);

        return new AutoCreatedVoucherRow(
            voucher.Id,
            voucher.Date,
            company.FormatVoucherNumber(voucher),
            type.Name,
            contra.Name,
            total,
            bankSide,
            instrumentNumber ?? string.Empty,
            requests.Count);
    }

    /// <summary>
    /// The narration written onto an auto-created voucher: the bank's own narration for the line, so the reviewer
    /// on the Optional Vouchers screen can see what the statement actually said. A consolidation joins them, and a
    /// blank statement description yields a blank narration rather than a fabricated one.
    /// </summary>
    private static string BuildNarration(IReadOnlyList<BankStatementVoucherRequest> requests)
    {
        var parts = requests
            .Select(r => r.Row.Description?.Trim() ?? string.Empty)
            .Where(d => d.Length > 0)
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
    }

    /// <summary>
    /// The company's Receipt / Payment voucher type: the predefined active one, else any active one of that base
    /// type. Refuses rather than inventing a type — a company whose Receipt type has been switched off has said
    /// it does not record receipts, and silently creating one would overrule that.
    /// </summary>
    private static VoucherType ResolveType(Company company, VoucherBaseType baseType)
    {
        var candidates = company.VoucherTypes.Where(t => t.BaseType == baseType && t.IsActive).ToList();
        var type = candidates.FirstOrDefault(t => t.IsPredefined) ?? candidates.FirstOrDefault();
        return type ?? throw new InvalidOperationException(
            $"This company has no active {baseType} voucher type, so a bank line that moves money "
            + $"{(baseType == VoucherBaseType.Receipt ? "in" : "out")} cannot be turned into a voucher. "
            + "Switch the type on under Masters > Create > Voucher Type.");
    }

    /// <summary>
    /// The contents of the <b>"Bank Reconciliation – Optional Vouchers"</b> screen for
    /// <paramref name="bankLedger"/>: every Optional voucher on the book that touches this bank account, oldest
    /// first. Derived — nothing is stored to mark a voucher as "auto-created", because the flag that matters
    /// (<see cref="Voucher.Optional"/>) is already persisted and an Optional bank voucher keyed by hand deserves
    /// exactly the same review.
    /// </summary>
    public static IReadOnlyList<AutoCreatedVoucherRow> OptionalVouchersFor(Company company, Domain.Ledger bankLedger)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(bankLedger);

        var rows = new List<AutoCreatedVoucherRow>();
        foreach (var v in company.Vouchers)
        {
            if (!v.Optional || v.Cancelled) continue;
            foreach (var line in v.Lines)
            {
                if (line.LedgerId != bankLedger.Id) continue;

                var contra = v.Lines.FirstOrDefault(l => l.LedgerId != bankLedger.Id);
                var contraName = contra is null
                    ? string.Empty
                    : company.FindLedger(contra.LedgerId)?.Name ?? string.Empty;

                rows.Add(new AutoCreatedVoucherRow(
                    v.Id,
                    v.Date,
                    company.FormatVoucherNumber(v),
                    company.FindVoucherType(v.TypeId)?.Name ?? string.Empty,
                    contraName,
                    line.Amount,
                    line.Side,
                    line.BankAllocation?.InstrumentNumber ?? string.Empty,
                    SourceRowCount: 1));
                break;                  // one bank line per voucher — see BankAllocation's remarks.
            }
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : string.CompareOrdinal(a.FormattedNumber, b.FormattedNumber);
        });
        return rows;
    }

    /// <summary>
    /// <b>R — "Mark as Regular &amp; Reconcile"</b> (vendor, verbatim). Clears
    /// <see cref="Voucher.Optional"/> on <paramref name="voucherId"/> — which is the moment the entry reaches the
    /// books — and then stamps the voucher's own date as the Bank Date on its <paramref name="bankLedgerId"/>
    /// line, because that date came from the bank statement in the first place.
    ///
    /// <para>Returns <c>false</c> when the voucher is unknown, already regular, cancelled, or carries no line for
    /// this bank account; it never half-applies.</para>
    ///
    /// <para>🔴 <b>WHERE THE BANK DATE COMES FROM, AND THE ONE CASE WHERE IT IS AN ASSUMPTION.</b> For a voucher
    /// auto-created here the voucher's date IS the statement's date, so stamping it as the Bank Date is exact —
    /// the bank itself said the money moved then. The queue this runs from
    /// (<see cref="OptionalVouchersFor"/>) also lists an Optional voucher keyed by HAND, because an unreviewed
    /// bank entry deserves the same review whoever made it; for one of those the voucher date is the operator's
    /// own, not the bank's, and stamping it asserts a clearance date nothing has attested. It is not silently
    /// permanent: the Bank Reconciliation screen owns the Bank Date and can correct or clear it
    /// (<see cref="BankReconciliation.SetBankDate"/> takes <c>null</c>). Distinguishing the two cases properly
    /// needs a stored "created from statement" marker, which is a schema column this track does not have.</para>
    /// </summary>
    public static bool MarkAsRegularAndReconcile(
        LedgerService service, Company company, Guid voucherId, Guid bankLedgerId)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(company);

        var voucher = company.FindVoucher(voucherId);
        if (voucher is null || !voucher.Optional || voucher.Cancelled) return false;

        // The bank line must exist AND carry an allocation before anything is changed: SetBankDate throws on a
        // bank line without one, and a throw AFTER the regularise would leave the voucher on the books carrying
        // no clearance — the half-applied state this method promises not to produce.
        var bankLine = voucher.Lines.FirstOrDefault(l => l.LedgerId == bankLedgerId);
        if (bankLine?.BankAllocation is null) return false;

        service.MarkOptionalAsRegular(voucherId);
        BankReconciliation.SetBankDate(company, voucherId, bankLedgerId, voucher.Date);
        return true;
    }
}
