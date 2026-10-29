using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One party's overdue bills on the Overdue Receivables / Payables exception report (census 11.13).</summary>
public sealed record OverdueParty(
    Guid LedgerId,
    string LedgerName,
    IReadOnlyList<OutstandingBill> Bills,
    Money Total);

/// <summary>
/// Census 11.13 — the <b>Overdue Receivables</b> and <b>Overdue Payables</b> exception reports: the open bills
/// of one side of <see cref="Outstandings"/> whose due date has PASSED as of the report date, grouped by party.
///
/// <para><b>Vendor.</b> help.tallysolutions.com/manage-receivables-outstanding-tally/ (TallyPrime, opened by
/// content 2026-10-26): <i>"Alternatively, you can use Alt+G (Go To) &gt; Overdue Receivables."</i>, the same
/// sentence for <i>Overdue Payables</i>, <i>"To view only overdue bills, use Basis of Values to select Overdue
/// Bills in Type of Bills."</i> and <i>"Review the outstanding bills, party names, pending amounts, due dates,
/// overdue days, and other details."</i></para>
///
/// <para><b>One engine, not a second netting.</b> This filters <see cref="Outstandings.Build"/> rather than
/// re-walking the vouchers, so an overdue bill's pending amount can never disagree with the same bill on Bills
/// Receivable / Bills Payable.</para>
///
/// <para><b>Non-posting vouchers never make a bill overdue.</b> A Memorandum or Reversing Journal carrying a bill
/// allocation neither opens nor settles a bill outside a scenario — the projection's voucher rule carries the base
/// type (see <c>Outstandings.CountsUnder</c>). Vendor (help.tallysolutions.com/accounting-entry-tally/, opened by
/// content 2026-10-27): <i>"Memorandum Vouchers are those vouchers that do not impact your books of
/// accounts."</i></para>
///
/// <para><b>The boundary is ours (IV-245).</b> A bill is overdue when <see cref="OutstandingBill.IsOverdue"/>
/// — its due date is strictly before the as-of date. A bill falling due ON the as-of date is not listed. The
/// TallyPrime page names "overdue days" and does not say which way the due day itself falls.</para>
/// </summary>
public sealed record OverdueBills(
    DateOnly AsOf,
    OutstandingKind Kind,
    IReadOnlyList<OverdueParty> Parties,
    Money Total)
{
    /// <summary>The F6 basis <see cref="Ageing"/> was bucketed on (census 11.13 / 11.9, T2-24).</summary>
    public AgeingMethod AgeingMethod { get; init; } = AgeingMethod.ByDueDate;

    /// <summary>The overdue bills' pending amounts aged into the windows of <see cref="AgeingMethod"/> — the same
    /// buckets, over the same bills, that Bills Receivable / Payable shows under Type of Bills = Overdue Bills.</summary>
    public IReadOnlyList<AgeingBucket> Ageing { get; init; } = Array.Empty<AgeingBucket>();

    /// <summary>Builds one side of the overdue report as of <paramref name="asOf"/>. Parties keep the
    /// Outstandings emission order (ledger order, then each ledger's first-opened bill order).
    /// <para>🔴 The overdue filter is NOT applied here. It is <see cref="TypeOfBills.OverdueBills"/> on
    /// <see cref="Outstandings.Build"/> — the same call Bills Receivable / Payable makes under Basis of Values &gt;
    /// Type of Bills &gt; Overdue Bills — so the two surfaces cannot list different bills or different
    /// pending amounts. Membership is always by DUE date (a bill is overdue when its due date has passed);
    /// <paramref name="ageingMethod"/> moves only the ageing buckets.</para></summary>
    public static OverdueBills Build(
        Company company,
        DateOnly asOf,
        OutstandingKind kind,
        Scenario? scenario = null,
        AgeingMethod ageingMethod = AgeingMethod.ByDueDate)
    {
        ArgumentNullException.ThrowIfNull(company);
        var report = Outstandings.Build(company, asOf, scenario, ageingMethod, TypeOfBills.OverdueBills);
        var source = kind == OutstandingKind.Receivable ? report.Receivables : report.Payables;

        var parties = new List<OverdueParty>();
        var byLedger = new Dictionary<Guid, List<OutstandingBill>>();
        var order = new List<(Guid Id, string Name)>();
        foreach (var bill in source)
        {
            if (!byLedger.TryGetValue(bill.LedgerId, out var list))
            {
                list = new List<OutstandingBill>();
                byLedger[bill.LedgerId] = list;
                order.Add((bill.LedgerId, bill.LedgerName));
            }
            list.Add(bill);
        }

        var grand = 0m;
        foreach (var (id, name) in order)
        {
            var bills = byLedger[id];
            var total = 0m;
            foreach (var b in bills) total += b.Pending.Amount;
            grand += total;
            parties.Add(new OverdueParty(id, name, bills, new Money(total)));
        }

        return new OverdueBills(asOf, kind, parties, new Money(grand))
        {
            AgeingMethod = ageingMethod,
            Ageing = kind == OutstandingKind.Receivable ? report.ReceivableAgeing : report.PayableAgeing,
        };
    }
}
