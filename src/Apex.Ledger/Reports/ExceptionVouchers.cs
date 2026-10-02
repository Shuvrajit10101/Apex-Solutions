using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Which of the Day Book's three <b>Exception Reports</b> to build (census rows 5.2, 5.7, 5.8).
///
/// <para>Fidelity (R7 / ruling 14): the vendor states the set outright — "The Exception Reports available in the
/// Day Book in TallyPrime are of <b>Optional Vouchers</b>, <b>Cancelled Vouchers</b>, and <b>Post-Dated
/// Vouchers</b>" (<c>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</c>),
/// reached with <b>Ctrl+J (Exception Reports)</b>. Exactly three, and no fourth is invented here.</para>
/// </summary>
public enum ExceptionVoucherKind
{
    /// <summary>Vouchers marked Optional (Ctrl+L) — entered but not affecting the books until regularised.</summary>
    Optional,

    /// <summary>Vouchers cancelled (Alt+X) — retained as evidence, contributing nothing to any balance.</summary>
    Cancelled,

    /// <summary>Vouchers marked Post-Dated (Ctrl+T) — entered now, effective on a later date.</summary>
    PostDated,
}

/// <summary>
/// The Day Book's three <b>Exception Reports</b>: the registers of Optional, Cancelled and Post-Dated vouchers
/// (census 5.2(c), 5.7(c), 5.8).
///
/// <para><b>The gap these close.</b> All three flags were already settable and already persisted, and all three
/// were <i>invisible</i>: the only surface that listed a flagged voucher was the Day Book, mixed in with every
/// ordinary voucher of the same day. An operator who marked a voucher Optional and moved on had no screen that
/// would ever tell them it was still Optional — i.e. still outside the books. A flag with no register is a
/// liability the book cannot show you.</para>
///
/// <para><b>Deliberately built on <see cref="DayBookRow"/> rather than on a new row type.</b> These registers
/// are the Day Book filtered by one flag — same columns, same drill, same two-aggregate hazard — so reusing the
/// row means the <see cref="DayBookRow.IsInventory"/> discipline, the formatted voucher number and the drill id
/// come across already correct. A parallel row type is how one of the three would quietly acquire a different
/// idea of which aggregate an id addresses.</para>
/// </summary>
public static class ExceptionVouchers
{
    /// <summary>
    /// 🔴 <b>The Optional register reads the ACCOUNTING aggregate only, and that is a real limit rather than an
    /// oversight to be papered over.</b> <see cref="Voucher"/> carries <c>Optional</c>;
    /// <see cref="InventoryVoucher"/> has <b>no Optional member at all</b>, so a pure-stock voucher cannot be
    /// marked Optional anywhere in this product and there is correspondingly nothing for this register to list.
    /// Callers surface this as a footnote instead of letting the register read as "no stock voucher is Optional",
    /// which would be a different and untrue statement. Census row 5.7(a) records the underlying gap.
    /// </summary>
    public const string OptionalScopeNote =
        "Optional applies to accounting vouchers only — a stock/order voucher cannot be marked Optional in this "
        + "application, so none can appear here.";

    /// <summary>
    /// Builds one exception register over <paramref name="from"/>…<paramref name="to"/>, in the Day Book's own
    /// order. Returns an empty list when nothing in the period carries the flag.
    /// </summary>
    public static IReadOnlyList<DayBookRow> Build(
        Company company, ExceptionVoucherKind kind, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        // Built by FILTERING the Day Book rather than by re-walking the aggregates. The Day Book already resolves
        // the voucher type name, the party-or-narration particulars, the formatted number, the movement value of a
        // stock voucher and the total order — so a register built here cannot disagree with the Day Book the
        // operator pressed Ctrl+J on, which is the one screen they will compare it against.
        var book = DayBook.Build(company, from, to);
        var rows = new List<DayBookRow>();

        foreach (var row in book)
        {
            if (!Matches(company, kind, row)) continue;
            rows.Add(row);
        }

        return rows;
    }

    private static bool Matches(Company company, ExceptionVoucherKind kind, DayBookRow row) => kind switch
    {
        // IsCancelled is carried on the row itself for BOTH aggregates, so this needs no lookup and cannot miss a
        // cancelled stock voucher the way a FindVoucher-only test would.
        ExceptionVoucherKind.Cancelled => row.IsCancelled,

        // See OptionalScopeNote: only the accounting aggregate has the flag. An inventory row is skipped rather
        // than resolved, because FindVoucher would return null for it and a null-tolerant test would read as
        // "not Optional" — true here by accident, and silently wrong the day InventoryVoucher gains the member.
        ExceptionVoucherKind.Optional =>
            !row.IsInventory && company.FindVoucher(row.VoucherId) is { Optional: true },

        // Both aggregates carry PostDated, so both are asked — each through its OWN finder, because the two
        // return null for each other's ids (the DayBookRow.IsInventory discipline).
        ExceptionVoucherKind.PostDated => row.IsInventory
            ? company.FindInventoryVoucher(row.VoucherId) is { PostDated: true }
            : company.FindVoucher(row.VoucherId) is { PostDated: true },

        _ => false,
    };

    /// <summary>The register's operator-facing title, matching the vendor's own option names.</summary>
    public static string TitleFor(ExceptionVoucherKind kind) => kind switch
    {
        ExceptionVoucherKind.Optional => "Optional Vouchers",
        ExceptionVoucherKind.Cancelled => "Cancelled Vouchers",
        ExceptionVoucherKind.PostDated => "Post-Dated Vouchers",
        _ => "Exception Vouchers",
    };

    /// <summary>The empty-state sentence for a register with no rows in the period — phrased per kind so it says
    /// what is absent rather than the generic "no data".</summary>
    public static string EmptyNoteFor(ExceptionVoucherKind kind) => kind switch
    {
        ExceptionVoucherKind.Optional =>
            "No voucher in this period is marked Optional. Ctrl+L on a voucher marks it Optional.",
        ExceptionVoucherKind.Cancelled =>
            "No voucher in this period has been cancelled. Alt+X on a Day Book row cancels one.",
        ExceptionVoucherKind.PostDated =>
            "No voucher in this period is post-dated. Ctrl+T on a voucher marks it Post-Dated.",
        _ => "Nothing to show for this period.",
    };
}
