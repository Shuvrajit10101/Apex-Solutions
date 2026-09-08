using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Which of the vendor's two Declaration Forms reports a projection is (census 15.6). The two are mirror
/// images across the counterparty, and keeping them one engine with this discriminator is what stops them
/// drifting into disagreeing about the same transaction.
/// </summary>
public enum CstFormsSide
{
    /// <summary>
    /// The vendor's <b>Forms Receivable</b>: forms this dealer is waiting to RECEIVE from a customer, against
    /// inter-State <b>sales</b>. <c>help.tallysolutions.com/tally-prime/reports/forms-receivables-tally/</c> —
    /// "<i>the date of transaction, CST Sales ledger used, gross amount inclusive of CST, form types</i>" plus
    /// the three fields "<i>which have to be filled when the prescribed forms are received from the purchasing
    /// dealer or the customer</i>".
    /// </summary>
    Receivable = 0,

    /// <summary>
    /// The vendor's <b>Forms Issuable</b>: forms this dealer must ISSUE to a supplier, against inter-State
    /// <b>purchases</b>. <c>help.tallysolutions.com/tally-prime/reports/forms-issuables-tally/</c> — "<i>the
    /// date of transaction, CST purchases ledger used, gross amount inclusive of CST, form types</i>" plus the
    /// three fields "<i>which can be filled when the prescribed forms are issued to the selling dealer or
    /// supplier</i>".
    /// </summary>
    Issuable = 1,
}

/// <summary>
/// Where one transaction stands with respect to its declaration form. Three states, not two, and the third is
/// the one that makes the feature reachable at all.
/// </summary>
public enum CstFormStatus
{
    /// <summary>
    /// 🔴 <b>No declaration form has been recorded against this transaction yet.</b> A candidate, not an
    /// omission — most transactions in a VAT dealer's sales register are local and will never need one.
    ///
    /// <para>These rows exist BECAUSE the operator has to be able to reach a transaction in order to record a
    /// form against it. An earlier draft of this report listed only vouchers that already carried a form type,
    /// which made the whole of census row 15.6 a capability no user could enter data into — the dead-feature
    /// shape this project has filed three times.</para>
    /// </summary>
    NotRecorded = 0,

    /// <summary>A form type is recorded but the physical form has not changed hands — no form NUMBER. This is
    /// the vendor's pending state and the exposure the report exists to surface.</summary>
    Pending = 1,

    /// <summary>The form has been received (or issued) and its number recorded.</summary>
    Completed = 2,
}

/// <summary>
/// One row of a <see cref="CstDeclarationFormsReport"/> — a single inter-State transaction covered by a CST
/// declaration form, and the state of that form.
/// </summary>
/// <param name="VoucherId">The voucher, so Enter drills to it.</param>
/// <param name="Date">Vendor column: <i>"the date of transaction"</i>.</param>
/// <param name="VoucherNumber">The voucher number, so a row can be matched to a paper document.</param>
/// <param name="PartyId">The counterparty ledger, when the voucher names one.</param>
/// <param name="PartyName">The counterparty's display name, or <c>null</c> when the voucher names no party.</param>
/// <param name="PartyCstNumber">The counterparty's <i>"CST No."</i> from their ledger, or <c>null</c>. The
/// operator needs it to chase the form, and it is the reason <c>ledgers.party_cst_number</c> exists.</param>
/// <param name="GrossAmount">Vendor column: <i>"gross amount inclusive of CST"</i> — the voucher's own party
/// amount, so the figure on this report and the figure in the books are the same number.</param>
/// <param name="FormType">Vendor column: <i>"form types"</i>; <c>null</c> when no form has been recorded
/// against the transaction yet (<see cref="CstFormStatus.NotRecorded"/>).</param>
/// <param name="FormSeriesNumber">Vendor field <i>"Form Series Number"</i>; <c>null</c> when not filled.</param>
/// <param name="FormNumber">Vendor field <i>"Form Number"</i>; <c>null</c> means the form is still PENDING.</param>
/// <param name="FormDate">Vendor field <i>"Form Date"</i>; <c>null</c> when not filled.</param>
public sealed record CstFormRow(
    Guid VoucherId,
    DateOnly Date,
    int VoucherNumber,
    Guid? PartyId,
    string? PartyName,
    string? PartyCstNumber,
    Money GrossAmount,
    CstDeclarationForm? FormType,
    string? FormSeriesNumber,
    string? FormNumber,
    DateOnly? FormDate)
{
    /// <summary>
    /// True iff the physical form has actually changed hands — i.e. a form NUMBER is recorded.
    ///
    /// <para>🔴 <b>The number alone decides.</b> Series was optional in practice and a date without a number
    /// records nothing, so filling either must not move a transaction out of the pending list. This is
    /// <see cref="Voucher.CstFormReceived"/> carried through unchanged, so the report and the voucher can never
    /// disagree about what "pending" means.</para>
    /// </summary>
    public bool Received => !string.IsNullOrWhiteSpace(FormNumber);

    /// <summary>Which of the three states this transaction is in. Derived, never stored, so it cannot drift
    /// from the two fields it reads.</summary>
    public CstFormStatus Status =>
        FormType is null ? CstFormStatus.NotRecorded
        : Received ? CstFormStatus.Completed
        : CstFormStatus.Pending;
}

/// <summary>
/// The vendor's <b>Forms Receivable</b> and <b>Forms Issuable</b> reports (census 15.6) — which CST
/// declaration forms this dealer is still waiting for, and which it still owes.
/// </summary>
/// <remarks>
/// <para>🔴 <b>WHY A REPEALED-LOOKING REPORT IS IN A 2026 PRODUCT.</b> The Central Sales Tax Act 1956 survives
/// for the goods GST never absorbed — alcoholic liquor for human consumption, which the Constitution keeps
/// outside GST permanently (Art. 366(12A); CGST Act s.9(1)), and the five petroleum products CGST Act s.9(2)
/// keeps out until the GST Council notifies otherwise. A fuel or liquor dealer trading across a State border
/// still issues and collects Form C, and an uncollected form is money: it is the difference between the
/// concessional inter-State rate and the full one. See <see cref="NonGstGoodsClass"/>.</para>
///
/// <para><b>Which side a transaction falls on.</b> A <b>Sales</b> voucher generates a form RECEIVABLE (the
/// customer owes this dealer the declaration); a <b>Purchase</b> voucher generates a form ISSUABLE (this
/// dealer owes the supplier one). That is the vendor's own split — its Receivable page names the "CST Sales
/// ledger" and its Issuable page the "CST purchases ledger". Credit and Debit Notes are deliberately NOT
/// included: neither vendor page mentions them, and guessing which side an inter-State return note falls on
/// would put invented rows on a statutory working paper.</para>
///
/// <para>🔴 <b>EVERY SALES (resp. PURCHASE) VOUCHER IN THE WINDOW IS LISTED, NOT ONLY THOSE ALREADY CARRYING A
/// FORM — AND THAT IS WHAT MAKES THE ROW REACHABLE.</b> The reference product derives the form from a <i>nature
/// of transaction</i> master this build does not have, so here the operator records it; and an operator can
/// only record it against a transaction they can reach. Listing only vouchers that already carried a form type
/// — which an earlier draft of this report did — left census row 15.6 with no way in at all: the report showed
/// what had been keyed and there was nowhere to key it. So the candidate set is the register, split three ways
/// by <see cref="CstFormStatus"/>, and the operator marks the inter-State ones.</para>
///
/// <para>The cost of that choice is stated rather than hidden: a dealer whose trade is mostly local sees mostly
/// <see cref="CstFormStatus.NotRecorded"/> rows. That is noise, but it is <b>honest</b> noise on a screen the
/// operator drives — far better than silently guessing that a transaction is covered by Form C and reporting a
/// concession the dealer cannot substantiate.</para>
///
/// <para><b>What never counts.</b> Cancelled and Optional (Ctrl+L) vouchers, and post-dated vouchers not yet
/// due at the window end — the same <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/>
/// rule every other report here uses. A cancelled sale owes nobody a declaration.</para>
///
/// <para><b>Ordering is by date, then voucher number, then voucher id</b> — total and deterministic, so two
/// runs over the same book produce byte-identical output and a printed working paper can be diffed. Falling
/// back to the id matters: two vouchers may share a date and a number across different types.</para>
/// </remarks>
public sealed record CstDeclarationFormsReport(
    CstFormsSide Side,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CstFormRow> Rows)
{
    /// <summary>The report's own title — the vendor's name for the side.</summary>
    public string Title => Side == CstFormsSide.Receivable ? "Forms Receivable" : "Forms Issuable";

    /// <summary>The transactions no declaration form has been recorded against yet — the candidates an
    /// operator marks. See <see cref="CstFormStatus.NotRecorded"/> for why they are listed.</summary>
    public IReadOnlyList<CstFormRow> NotRecorded { get; } =
        (Rows ?? Array.Empty<CstFormRow>()).Where(r => r.Status == CstFormStatus.NotRecorded).ToList();

    /// <summary>The rows whose form is recorded but has NOT yet changed hands — what the report exists to
    /// surface. 🔴 A row with no form type at all is NOT here: it is a candidate, not an outstanding form, and
    /// counting it as pending would inflate the exposure figure with local sales.</summary>
    public IReadOnlyList<CstFormRow> Pending { get; } =
        (Rows ?? Array.Empty<CstFormRow>()).Where(r => r.Status == CstFormStatus.Pending).ToList();

    /// <summary>The rows whose form has been received/issued.</summary>
    public IReadOnlyList<CstFormRow> Completed { get; } =
        (Rows ?? Array.Empty<CstFormRow>()).Where(r => r.Status == CstFormStatus.Completed).ToList();

    /// <summary>Σ gross amount of the <see cref="Pending"/> rows — the exposure an operator is chasing.</summary>
    public Money PendingValue
    {
        get
        {
            var sum = Money.Zero;
            foreach (var r in Pending) sum += r.GrossAmount;
            return sum;
        }
    }

    /// <summary>Σ gross amount of every row on the report, pending or not.</summary>
    public Money TotalValue
    {
        get
        {
            var sum = Money.Zero;
            foreach (var r in Rows) sum += r.GrossAmount;
            return sum;
        }
    }

    /// <summary>
    /// Builds one side of the Declaration Forms report over <c>[from, to]</c>.
    /// </summary>
    public static CstDeclarationFormsReport Build(
        Company company, CstFormsSide side, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var wanted = side == CstFormsSide.Receivable
            ? VoucherBaseType.Sales
            : VoucherBaseType.Purchase;

        var ledgersById = company.Ledgers.ToDictionary(l => l.Id);
        var rows = new List<CstFormRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != wanted) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

            Domain.Ledger? party = v.PartyId is { } pid && ledgersById.TryGetValue(pid, out var p) ? p : null;

            rows.Add(new CstFormRow(
                VoucherId: v.Id,
                Date: v.Date,
                VoucherNumber: v.Number,
                PartyId: party?.Id,
                PartyName: party?.Name,
                PartyCstNumber: party?.PartyCstNumber,
                GrossAmount: GrossAmountOf(v, party),
                FormType: v.CstFormType,
                FormSeriesNumber: v.CstFormSeriesNumber,
                FormNumber: v.CstFormNumber,
                FormDate: v.CstFormDate));
        }

        rows.Sort(static (a, b) =>
        {
            var c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            c = a.VoucherNumber.CompareTo(b.VoucherNumber);
            return c != 0 ? c : a.VoucherId.CompareTo(b.VoucherId);
        });

        return new CstDeclarationFormsReport(side, from, to, rows);
    }

    /// <summary>
    /// The vendor's <i>"gross amount inclusive of CST"</i> for one voucher.
    ///
    /// <para>🔴 <b>It is the PARTY LINE's own amount, taken from the books — never a figure this report
    /// computes.</b> The party leg of a Sales or Purchase voucher is by construction the whole invoice value,
    /// tax included, because that is what the customer owes or the supplier is owed. Re-deriving it by summing
    /// goods lines and adding a tax would produce a second number that could disagree with the ledger, on a
    /// working paper whose entire purpose is to be reconciled against the books.</para>
    ///
    /// <para>When the voucher names no party — legal, if unusual, for a hand-keyed Sales voucher — the report
    /// falls back to Σ of the debit side, which is the voucher's own total. It never returns a partial sum.</para>
    /// </summary>
    private static Money GrossAmountOf(Voucher voucher, Domain.Ledger? party)
    {
        if (party is not null)
        {
            var partyTotal = Money.Zero;
            var found = false;
            foreach (var line in voucher.Lines)
            {
                if (line.LedgerId != party.Id) continue;
                partyTotal += line.Amount;
                found = true;
            }
            if (found) return partyTotal;
        }

        var debits = Money.Zero;
        foreach (var line in voucher.Lines)
            if (line.Side == DrCr.Debit) debits += line.Amount;
        return debits;
    }
}
