using System.Globalization;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// 🔴 <b>CENSUS 6.28 (WAVE 12, TRACK hf) — THE REFERENTIAL RULES FOR ALTERING AND DELETING A NATURE OF PAYMENT
/// (TDS SECTION) MASTER.</b> Until this wave the master was create-only and its predefined rows immutable, so a
/// user could not correct a seeded rate or threshold in-app (T1-21). The alteration and deletion verbs are now on
/// the master's screen; this class is what they ask before touching the book.
///
/// <para><b>Its own file, deliberately not <c>MasterDeletionRules</c>.</b> That file belongs to a sibling track
/// this wave; a nature of payment is not an accounting master in the sense its doctrine describes, and the
/// alteration half below has no counterpart there at all.</para>
///
/// <para><b>FIDELITY (R7, ruling 14) — what the vendor says, opened by content 2026-11-22:</b>
/// <list type="bullet">
///   <item><c>help.tallysolutions.com/tds-faq-tally/</c>, Q <i>"Can I update Section and Payment code in existing
///     Nature of Payment master?"</i> — updating them <i>"will affect the Payment Entries and return filing
///     process"</i>, and the page recommends creating a new Nature of Payment with a new Applicable-from date
///     instead. That is the ground for refusing a section-code or payment-code change on a master posted TDS lines
///     already use (<see cref="EnsureAlterable"/>).</item>
///   <item>The same page tells the user to <i>"Manually change the No PAN rate to 5%"</i> on the predefined
///     "Purchase of Any Goods" nature, and that the §194Q threshold is set <i>"in the Nature of Payment
///     master"</i> — so a predefined nature's rates and threshold ARE operator-editable. That is the ground for
///     allowing them to be altered at all.</item>
///   <item><c>help.tallysolutions.com/create-tds-masters/</c>: on creation the user selects a Name and <i>"The
///     Section and Payment code are displayed automatically"</i>.</item>
/// </list></para>
///
/// <para><b>WHAT IS OURS, labelled so (docs/invented-vs-cloned.md §2AG):</b> no vendor page says anything about
/// DELETING a Nature of Payment, so every deletion rule here is ours — refusing a predefined section outright,
/// refusing one a ledger names, refusing one a posted TDS line carries, and every message string. Locking the
/// section and payment codes of a PREDEFINED section on alteration is also ours (the vendor screen derives them
/// from the chosen name, which suggests but does not say they are fixed).</para>
/// </summary>
public static class NatureOfPaymentRules
{
    /// <summary>How many ledgers and vouchers a refusal names before it summarises the rest as a count.</summary>
    private const int NamesShown = 3;

    /// <summary>
    /// The posted entry lines whose TDS detail was computed under <paramref name="natureId"/>, cancelled vouchers
    /// included — a cancelled voucher keeps its lines in the book, so its <see cref="TdsLineTax.NatureId"/> would
    /// be orphaned by a deletion exactly as a live one's would.
    /// </summary>
    public static IReadOnlyList<Voucher> VouchersCarrying(Company company, Guid natureId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Vouchers
            .Where(v => v.Lines.Any(l => l.Tds is { } t && t.NatureId == natureId))
            .ToList();
    }

    /// <summary>
    /// T2-181 (wave 13, track ic) — the <b>live</b> vouchers whose withholding the alteration from
    /// <paramref name="existing"/> to <paramref name="replacement"/> would MOVE: for every posted TDS line carrying
    /// this nature on a voucher that is <b>not cancelled</b>, the withholding is computed twice by the one engine
    /// (<see cref="TdsService.ComputeWithholding"/>) — under the old terms and under the new — over the line's own
    /// posted assessable value, deductee and date, with the voucher itself excluded from its prior aggregate and with
    /// the line's posted rate, assessable and TDS handed in as the grandfathering carriers, exactly as the re-carve
    /// hands them in. A
    /// voucher is counted when the rate or the amount differs between the two. That is exactly the set whose next
    /// alteration <c>VoucherEntryViewModel.ApplyReCarve</c> would refuse because of THIS change; the old count
    /// (<see cref="VouchersCarrying"/>) also took in cancelled vouchers (which cannot be altered) and vouchers the
    /// new figures leave untouched.
    ///
    /// <para>A deductee ledger that no longer resolves is counted (conservative: the warning over-states rather
    /// than hides). Both computations see the same book, so drift that has nothing to do with this alteration (a
    /// sibling cancelled since posting) cancels out and is not attributed to it.</para>
    /// </summary>
    public static IReadOnlyList<Voucher> VouchersMovedBy(Company company, NatureOfPayment existing, NatureOfPayment replacement)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);
        var engine = new TdsService(company);
        var moved = new List<Voucher>();
        foreach (var v in company.Vouchers.Where(v => !v.Cancelled))
        {
            foreach (var t in v.Lines.Select(l => l.Tds).OfType<TdsLineTax>().Where(t => t.NatureId == existing.Id))
            {
                if (company.FindLedger(t.DeducteeLedgerId) is not { } deductee) { moved.Add(v); break; }
                // 🔴 Review fix (wave 13 ic): the SAME three posted carriers ApplyReCarve hands the engine — the
                // voucher's own posted rate (§194C grandfathering), assessable and TDS (§194-I outcome
                // grandfathering). Without them this count disagreed with the refusal in BOTH directions: it missed a
                // grandfathered §194C voucher a rate alteration makes unalterable, and counted a §194-I voucher whose
                // pinned outcome keeps it alterable (NatureOfPaymentMovedByGrandfatheringTests).
                var before = engine.ComputeWithholding(t.AssessableValue, existing, deductee, v.Date, asPostedBefore: v.Id,
                    postedRateBasisPoints: t.RateBasisPoints, postedAssessableValue: t.AssessableValue,
                    postedTdsAmount: t.TdsAmount);
                var after = engine.ComputeWithholding(t.AssessableValue, replacement, deductee, v.Date, asPostedBefore: v.Id,
                    postedRateBasisPoints: t.RateBasisPoints, postedAssessableValue: t.AssessableValue,
                    postedTdsAmount: t.TdsAmount);
                if (before.RateBasisPoints != after.RateBasisPoints || before.TdsAmount != after.TdsAmount)
                {
                    moved.Add(v);
                    break;
                }
            }
        }
        return moved;
    }

    /// <summary>The ledgers whose default Nature of Payment (<see cref="Domain.Ledger.TdsNatureOfPaymentId"/>) is
    /// <paramref name="natureId"/>.</summary>
    public static IReadOnlyList<Domain.Ledger> LedgersNaming(Company company, Guid natureId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Ledgers.Where(l => l.TdsNatureOfPaymentId == natureId).ToList();
    }

    /// <summary>
    /// Refuses deleting <paramref name="nature"/>, by name, when it is a <b>predefined</b> statutory section, when
    /// any <b>ledger</b> names it as its default nature, or when any <b>posted TDS line</b> carries it. Neither
    /// reference has a foreign key in the store (<c>ledgers.tds_nature_id</c> and the voucher-line TDS columns are
    /// bare ids), so this guard is the only thing standing between a deletion and an orphaned id that Form 26Q,
    /// the TDS exception reports and the cumulative-threshold projection would then fail to resolve.
    /// </summary>
    /// <exception cref="InvalidOperationException">The nature is predefined or still in use.</exception>
    public static void EnsureDeletable(Company company, NatureOfPayment nature)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(nature);
        var what = $"Nature of Payment '{nature.SectionCode}' ({nature.Name})";

        if (nature.IsPredefined)
            throw new InvalidOperationException(
                $"Cannot delete {what}: it is a predefined statutory section. Alter its rates or threshold "
                + "instead (Ctrl+Enter on the row).");

        var vouchers = VouchersCarrying(company, nature.Id);
        if (vouchers.Count > 0)
            throw new InvalidOperationException(
                $"Cannot delete {what}: {Count(vouchers.Count, "posted voucher deducts", "posted vouchers deduct")} "
                + $"TDS under it ({Names(vouchers.Select(v => DescribeVoucher(company, v)).ToList())}). "
                + "Those deductions are filed in Form 26Q under this section, so it must stay.");

        var ledgers = LedgersNaming(company, nature.Id);
        if (ledgers.Count > 0)
            throw new InvalidOperationException(
                $"Cannot delete {what}: {Count(ledgers.Count, "ledger names", "ledgers name")} it as the "
                + $"default Nature of Payment ({Names(ledgers.Select(l => $"'{l.Name}'").ToList())}). "
                + "Change those ledgers first.");
    }

    /// <summary>
    /// Refuses altering <paramref name="existing"/> into <paramref name="replacement"/> when the change would
    /// re-file deductions already made: a <b>section code</b> or <b>Form-26Q payment code</b> change on a master
    /// that posted TDS lines carry (vendor FAQ: such a change "will affect the Payment Entries and return filing
    /// process" — create a new Nature of Payment instead), and either change at all on a <b>predefined</b> section
    /// (ours). Rates, thresholds and the name may always change; a posted voucher keeps the rate and amount it was
    /// posted with, because <see cref="TdsLineTax"/> stores both.
    /// </summary>
    /// <exception cref="InvalidOperationException">The alteration is refused.</exception>
    public static void EnsureAlterable(Company company, NatureOfPayment existing, NatureOfPayment replacement)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);

        var sectionChanged = !string.Equals(existing.SectionCode, replacement.SectionCode, StringComparison.Ordinal);
        var codeChanged = !string.Equals(existing.FvuSectionCode, replacement.FvuSectionCode, StringComparison.Ordinal);
        if (!sectionChanged && !codeChanged) return;

        var what = $"Nature of Payment '{existing.SectionCode}' ({existing.Name})";
        var field = sectionChanged && codeChanged ? "section code and Form-26Q code"
                  : sectionChanged ? "section code" : "Form-26Q code";

        if (existing.IsPredefined)
            throw new InvalidOperationException(
                $"Cannot change the {field} of {what}: it is a predefined statutory section. Only its name, rates "
                + "and thresholds can be altered. For a different section, create a new Nature of Payment.");

        var vouchers = VouchersCarrying(company, existing.Id);
        if (vouchers.Count > 0)
            throw new InvalidOperationException(
                $"Cannot change the {field} of {what}: "
                + $"{Count(vouchers.Count, "posted voucher deducts", "posted vouchers deduct")} TDS under it "
                + $"({Names(vouchers.Select(v => DescribeVoucher(company, v)).ToList())}), and the change would "
                + "re-file those deductions. Create a new Nature of Payment for the new code instead.");
    }

    private static string Names(IReadOnlyList<string> names) =>
        names.Count <= NamesShown
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(NamesShown)) + $" and {names.Count - NamesShown} more";

    private static string DescribeVoucher(Company company, Voucher voucher)
    {
        var typeName = company.FindVoucherType(voucher.TypeId)?.Name ?? "Voucher";
        var number = company.FormatVoucherNumber(voucher);
        var numberPart = string.IsNullOrWhiteSpace(number) ? string.Empty : $" No. {number}";
        return $"{typeName}{numberPart} dated "
             + voucher.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
    }

    private static string Count(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";
}
