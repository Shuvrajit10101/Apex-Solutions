using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>The five accounting registers of census row 11.6, each selecting one voucher base type.</summary>
public enum VoucherRegisterKind
{
    Sales,
    Purchase,
    Journal,
    CreditNote,
    DebitNote,
}

/// <summary>
/// One month row of a register's <b>top</b> (summary) level: the month, how many vouchers of the register's
/// kind were posted in it, and their footed value. A month with no vouchers still gets a row carrying zeroes,
/// so the axis reads continuously and a gap in trading is visible rather than invisible.
/// </summary>
public sealed record VoucherRegisterMonthRow(MonthWindow Month, int VoucherCount, Money Value);

/// <summary>
/// One row of a register's <b>drilled</b> (voucher-wise) level: a single voucher of the register's kind.
/// <see cref="VoucherId"/> is the RQ-7 drill key — Enter opens that voucher's detail.
/// </summary>
public sealed record VoucherRegisterVoucherRow(
    Guid VoucherId,
    DateOnly Date,
    int Number,
    string FormattedNumber,
    string? Particulars,
    Money Value);

/// <summary>
/// An accounting register (census row 11.6): <b>Sales</b>, <b>Purchase</b>, <b>Journal</b>,
/// <b>Credit Note</b> and <b>Debit Note</b>.
///
/// <para>🔴 <b>THE SHAPE IS THE POINT.</b> The wave-2 verification pass read the vendor's published Sales
/// Register page and recorded that the register is a <b>month-wise view</b> from which "you can drill down
/// from the selected month to view the voucher-wise listing". That is two levels, and it is why the census
/// row could not be closed by filtering the <see cref="DayBook"/> — the Day Book is a flat chronological
/// list and has no month level at all. <see cref="Build"/> is the month level; <see cref="Vouchers"/> is
/// the level a month row drills into (call it with that month's <c>[From, To]</c>).</para>
///
/// <para><b>What counts.</b> A voucher enters a register when its type's
/// <see cref="VoucherType.BaseType"/> is the register's base type, it is dated inside the window, and it
/// counts under <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> — so
/// cancelled, optional and not-yet-due post-dated vouchers contribute nothing to a figure, exactly as they
/// contribute nothing to a balance. Its <see cref="Value"/> is the voucher's debit total (= its credit
/// total for a balanced voucher), the same money figure the Day Book shows.</para>
///
/// <para>⚠️ <b>DIVERGENCE, LABELLED AS OURS (R7 / RULING 9).</b> No admissible source was reachable for
/// this register's <i>column set</i> — only for its two-level shape. The columns chosen here (month ·
/// voucher count · value at the summary level; date · number · particulars · value at the voucher level)
/// and the exclusion of cancelled vouchers from the figures are <b>ours</b>, not a compared clone
/// behaviour, and this projection may never be recorded as corpus-verified on the strength of them.</para>
/// </summary>
public sealed record VoucherRegister(
    VoucherRegisterKind Kind,
    string Title,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<VoucherRegisterMonthRow> Months,
    int TotalCount,
    Money Total)
{
    /// <summary>The voucher base type a register selects.</summary>
    public static VoucherBaseType BaseTypeOf(VoucherRegisterKind kind) => kind switch
    {
        VoucherRegisterKind.Sales => VoucherBaseType.Sales,
        VoucherRegisterKind.Purchase => VoucherBaseType.Purchase,
        VoucherRegisterKind.Journal => VoucherBaseType.Journal,
        VoucherRegisterKind.CreditNote => VoucherBaseType.CreditNote,
        VoucherRegisterKind.DebitNote => VoucherBaseType.DebitNote,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown register kind."),
    };

    /// <summary>The register's report title, as it reads on the screen and in the menu.</summary>
    public static string TitleOf(VoucherRegisterKind kind) => kind switch
    {
        VoucherRegisterKind.Sales => "Sales Register",
        VoucherRegisterKind.Purchase => "Purchase Register",
        VoucherRegisterKind.Journal => "Journal Register",
        VoucherRegisterKind.CreditNote => "Credit Note Register",
        VoucherRegisterKind.DebitNote => "Debit Note Register",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown register kind."),
    };

    /// <summary>Builds the register's <b>month-wise</b> (top) level over <c>[from, to]</c>.</summary>
    public static VoucherRegister Build(Company company, VoucherRegisterKind kind, DateOnly from, DateOnly to)
    {
        var baseType = BaseTypeOf(kind);
        var months = new List<VoucherRegisterMonthRow>();
        var totalCount = 0;
        var total = 0m;

        foreach (var month in MonthAxis.Months(from, to))
        {
            var count = 0;
            var value = 0m;

            foreach (var v in company.Vouchers)
            {
                if (!Selects(company, v, baseType, month.From, month.To)) continue;
                count++;
                value += v.TotalDebit.Amount;
            }

            months.Add(new VoucherRegisterMonthRow(month, count, new Money(value)));
            totalCount += count;
            total += value;
        }

        return new VoucherRegister(kind, TitleOf(kind), from, to, months, totalCount, new Money(total));
    }

    /// <summary>
    /// The register's <b>voucher-wise</b> (drilled) level over <c>[from, to]</c> — pass a month row's own
    /// window to get exactly the vouchers footed into that month row. Chronological, then by number.
    /// </summary>
    public static IReadOnlyList<VoucherRegisterVoucherRow> Vouchers(
        Company company, VoucherRegisterKind kind, DateOnly from, DateOnly to)
    {
        var baseType = BaseTypeOf(kind);
        var rows = new List<(Voucher Voucher, VoucherRegisterVoucherRow Row)>();

        foreach (var v in company.Vouchers)
        {
            if (!Selects(company, v, baseType, from, to)) continue;

            string? particulars = v.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;
            particulars ??= CounterParticulars(company, v);
            particulars ??= v.Narration;

            rows.Add((v, new VoucherRegisterVoucherRow(
                v.Id, v.Date, v.Number, company.FormatVoucherNumber(v), particulars, v.TotalDebit)));
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Voucher.Date.CompareTo(b.Voucher.Date);
            return byDate != 0 ? byDate : a.Voucher.Number.CompareTo(b.Voucher.Number);
        });

        return rows.Select(r => r.Row).ToList();
    }

    /// <summary>Whether a voucher belongs in this register for the window <c>[from, to]</c>.</summary>
    private static bool Selects(Company company, Voucher v, VoucherBaseType baseType, DateOnly from, DateOnly to)
    {
        if (v.Date < from || v.Date > to) return false;
        var type = company.FindVoucherType(v.TypeId);
        if (type?.BaseType != baseType) return false;
        // Cancelled / optional / not-yet-due post-dated vouchers contribute nothing to a figure.
        return LedgerBalances.CountsAsOf(v, to, type.BaseType);
    }

    /// <summary>
    /// The "other side" label for a voucher with no explicit party — the single non-first ledger's name, or
    /// "(multiple)" when the voucher touches several. A Sales voucher paid straight into Cash has no party
    /// id, and reading "(no party)" there would be less useful than reading "Cash".
    /// </summary>
    private static string? CounterParticulars(Company company, Voucher v)
    {
        // The register's amount is the DEBIT total, so the informative counter-name is the debit side's
        // ledger for a Sales voucher and the credit side's for a Purchase — in both cases the side that is
        // NOT the register's own nominal account. Take the distinct ledger names and collapse.
        var names = v.Lines
            .Select(l => company.FindLedger(l.LedgerId))
            .Where(l => l is not null)
            .Where(l => !ClassificationRules.IsProfitAndLossLedger(l!, company))
            .Select(l => l!.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count switch
        {
            0 => null,
            1 => names[0],
            _ => "(multiple)",
        };
    }
}
