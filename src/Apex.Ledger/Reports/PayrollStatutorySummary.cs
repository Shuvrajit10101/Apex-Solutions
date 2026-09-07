using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>The statutory pay-head <b>types</b> the summary rolls up — the categories the vendor's page names,
/// less the one this book does not maintain (see <see cref="PayrollStatutorySummary.UnsupportedTypeNote"/>).</summary>
public enum PayrollStatutoryHeadType
{
    /// <summary>Provident Fund — every pay head whose <see cref="PayHead.PfComponent"/> is set.</summary>
    ProvidentFund = 0,

    /// <summary>Employee State Insurance — every pay head whose <see cref="PayHead.EsiComponent"/> is set.</summary>
    EmployeeStateInsurance = 1,

    /// <summary>Professional Tax — every pay head whose <see cref="PayHead.PtComponent"/> is set.</summary>
    ProfessionalTax = 2,

    /// <summary>Income Tax — the §192 salary-TDS head
    /// (<see cref="IncomeTaxComponent.TaxDeductedAtSource"/>).</summary>
    IncomeTax = 3,
}

/// <summary>One pay head inside a <see cref="PayrollStatutorySummaryRow"/> — the vendor's <i>Statutory Pay Head
/// Details</i> level, carrying the same payable/paid pair as its parent type.</summary>
public sealed record PayrollStatutoryPayHeadDetail(
    Guid PayHeadId,
    string PayHeadName,
    string? LedgerName,
    Money Payable,
    Money Paid)
{
    /// <summary>Payable − Paid: what is still owed for the period on this head.</summary>
    public Money Balance => Payable - Paid;
}

/// <summary>One statutory pay-head type of a <see cref="PayrollStatutorySummary"/>, with the payable and paid
/// amounts for the period and the per-pay-head details beneath it.</summary>
public sealed record PayrollStatutorySummaryRow(
    PayrollStatutoryHeadType HeadType,
    string Caption,
    Money Payable,
    Money Paid,
    IReadOnlyList<PayrollStatutoryPayHeadDetail> Details)
{
    /// <summary>Payable − Paid: what is still owed for the period on this statutory type.</summary>
    public Money Balance => Payable - Paid;
}

/// <summary>
/// The <b>Payroll Statutory Summary</b> (census row 7.25) — <i>"statutory pay head types such as PF, ESI, NPS, and
/// PT, along with the payable and paid amounts for the selected period"</i>
/// (help.tallysolutions.com/tally-prime/payroll-statutory-reports/payroll-statutory-summary-tally/), together with
/// the vendor's <i>Statutory Pay Head Details</i> level carried inline on each row.
///
/// <para>🔴 <b>THIS IS THE ROLL-UP OVER THE PF / ESI / PT COMPUTATIONS, NOT A SUBSTITUTE FOR THEM.</b> Census rows
/// 7.10 / 7.11 / 7.12 already ship those computations and their registers, and this report recomputes none of
/// them: every figure here is read straight off the <b>posted, non-cancelled</b> payroll vouchers and the
/// payments made against the statutory payable ledgers. Nothing statutory is calculated in this file.</para>
///
/// <para><b>Payable and Paid, defined so the pair cannot be misread.</b> <b>Payable</b> is the liability the
/// period's payroll RAISED — Σ of the posted <see cref="PayrollLineCategory.Deduction"/> and
/// <see cref="PayrollLineCategory.EmployerContributionPayable"/> lines on the statutory heads. <b>Paid</b> is what
/// was SETTLED against those same payable ledgers in the period — Σ of the debits on them from lines that carry no
/// payroll detail, which is exactly a payment/journal voucher discharging the liability rather than the payroll
/// run creating it. The two are therefore measured over the same window and their difference is meaningful.</para>
///
/// <para>⚠️ <b>NPS IS NAMED BY THE VENDOR AND IS NOT MAINTAINED BY THIS BOOK.</b> There is no NPS statutory
/// component on a pay head anywhere in this product, so there is no NPS figure to roll up and none is invented.
/// The row is omitted and <see cref="UnsupportedTypeNote"/> says so on the report itself — a documented divergence
/// labelled as ours, not a silent gap.</para>
///
/// <para>A pure, deterministic, culture-invariant projection — no clock, no RNG.</para>
/// </summary>
public sealed record PayrollStatutorySummary(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<PayrollStatutorySummaryRow> Rows,
    Money TotalPayable,
    Money TotalPaid)
{
    /// <summary>The divergence this report declares on its own face (see the type doc).</summary>
    public const string UnsupportedTypeNote =
        "The vendor's summary also lists NPS. This book carries no NPS statutory component on a pay head, so "
        + "there is no NPS amount to report and no row is shown for it.";

    /// <summary>How the "Paid" column is derived, printed beneath the grid so the figure is never guessed at.</summary>
    public const string PaidDerivationNote =
        "\"Payable\" is the liability this period's payroll raised on the statutory heads. \"Paid\" is the "
        + "settlement debited to those same statutory payable ledgers in the period by a voucher other than the "
        + "payroll run itself.";

    /// <summary>Total still owed across every statutory type for the period.</summary>
    public Money TotalBalance => TotalPayable - TotalPaid;

    /// <summary>True when no statutory head posted anything and nothing was settled in the period.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Builds the statutory summary over <c>[from, to]</c>. Rows appear only for statutory types that
    /// have at least one configured pay head, and are ordered by <see cref="PayrollStatutoryHeadType"/> so the
    /// report is byte-stable.</summary>
    public static PayrollStatutorySummary Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        // 1. Classify every statutory pay head. A head can only be one statutory type in this book (the PF, ESI
        //    and PT engines each own their component), so the first match wins and nothing is double-counted.
        var byType = new Dictionary<PayrollStatutoryHeadType, List<PayHead>>();
        foreach (var payHead in company.PayHeads)
        {
            if (ClassifyHead(payHead) is not { } type) continue;
            if (!byType.TryGetValue(type, out var heads)) byType[type] = heads = new List<PayHead>();
            heads.Add(payHead);
        }
        if (byType.Count == 0)
            return new PayrollStatutorySummary(from, to, Array.Empty<PayrollStatutorySummaryRow>(), Money.Zero, Money.Zero);

        // 2. Σ the payable raised per pay head, and the settlement debited per statutory payable ledger.
        var payableByHead = new Dictionary<Guid, decimal>();
        var paidByLedger = new Dictionary<Guid, decimal>();
        var statutoryLedgers = new HashSet<Guid>();
        foreach (var heads in byType.Values)
            foreach (var head in heads)
                if (head.LedgerId is { } ledgerId) statutoryLedgers.Add(ledgerId);

        foreach (var voucher in company.Vouchers)
        {
            if (voucher.Cancelled) continue;
            if (voucher.Date < from || voucher.Date > to) continue;
            foreach (var line in voucher.Lines)
            {
                if (line.Payroll is { } pd)
                {
                    // The payroll run RAISING the liability.
                    if (pd.PayHeadId is not { } phId) continue;
                    if (pd.Category is not (PayrollLineCategory.Deduction
                        or PayrollLineCategory.EmployerContributionPayable)) continue;
                    payableByHead.TryGetValue(phId, out var running);
                    payableByHead[phId] = running + pd.Amount.Amount;
                    continue;
                }

                // Anything else DEBITING a statutory payable ledger is a settlement of it.
                if (line.Side != DrCr.Debit) continue;
                if (!statutoryLedgers.Contains(line.LedgerId)) continue;
                paidByLedger.TryGetValue(line.LedgerId, out var paid);
                paidByLedger[line.LedgerId] = paid + line.Amount.Amount;
            }
        }

        // 3. Fold into type rows. A payable ledger shared by two heads of the same type would double-count its
        //    settlement, so each ledger's paid amount is consumed exactly once.
        var consumedLedgers = new HashSet<Guid>();
        var rows = new List<PayrollStatutorySummaryRow>();
        foreach (var type in Enum.GetValues<PayrollStatutoryHeadType>())
        {
            if (!byType.TryGetValue(type, out var heads)) continue;

            var details = new List<PayrollStatutoryPayHeadDetail>();
            foreach (var head in heads.OrderBy(h => PayrollReportSupport.Label(h), StringComparer.Ordinal).ThenBy(h => h.Id))
            {
                payableByHead.TryGetValue(head.Id, out var payable);

                decimal paid = 0m;
                if (head.LedgerId is { } ledgerId && consumedLedgers.Add(ledgerId))
                    paidByLedger.TryGetValue(ledgerId, out paid);

                var ledgerName = head.LedgerId is { } lid ? company.FindLedger(lid)?.Name : null;
                details.Add(new PayrollStatutoryPayHeadDetail(
                    head.Id, PayrollReportSupport.Label(head), ledgerName, new Money(payable), new Money(paid)));
            }

            rows.Add(new PayrollStatutorySummaryRow(
                type,
                CaptionFor(type),
                new Money(details.Sum(d => d.Payable.Amount)),
                new Money(details.Sum(d => d.Paid.Amount)),
                details));
        }

        return new PayrollStatutorySummary(
            from, to, rows,
            new Money(rows.Sum(r => r.Payable.Amount)),
            new Money(rows.Sum(r => r.Paid.Amount)));
    }

    /// <summary>The statutory type a pay head belongs to, or <c>null</c> when it is not a statutory head. Public so
    /// a test pins the classification rather than restating it.</summary>
    public static PayrollStatutoryHeadType? ClassifyHead(PayHead payHead)
    {
        ArgumentNullException.ThrowIfNull(payHead);
        if (payHead.PfComponent != PfStatutoryComponent.None) return PayrollStatutoryHeadType.ProvidentFund;
        if (payHead.EsiComponent != EsiStatutoryComponent.None) return PayrollStatutoryHeadType.EmployeeStateInsurance;
        if (payHead.PtComponent != PtStatutoryComponent.None) return PayrollStatutoryHeadType.ProfessionalTax;
        if (payHead.IncomeTaxComponent == IncomeTaxComponent.TaxDeductedAtSource) return PayrollStatutoryHeadType.IncomeTax;
        return null;
    }

    /// <summary>The row caption for a statutory type.</summary>
    public static string CaptionFor(PayrollStatutoryHeadType type) => type switch
    {
        PayrollStatutoryHeadType.ProvidentFund => "Provident Fund",
        PayrollStatutoryHeadType.EmployeeStateInsurance => "Employee State Insurance",
        PayrollStatutoryHeadType.ProfessionalTax => "Professional Tax",
        PayrollStatutoryHeadType.IncomeTax => "Income Tax",
        _ => type.ToString(),
    };
}
