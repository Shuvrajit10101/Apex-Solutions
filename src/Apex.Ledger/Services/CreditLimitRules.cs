using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The outcome of measuring one voucher against one party's credit limit.
/// </summary>
/// <param name="Breached">True when <see cref="Exposure"/> exceeds <see cref="Limit"/>.</param>
/// <param name="Party">The party ledger the limit belongs to; <c>null</c> when no ledger on the voucher carries one.</param>
/// <param name="Limit">The limit that was measured against; <see cref="Money.Zero"/> when there was none.</param>
/// <param name="Exposure">What the party would owe (Receivable) or be owed (Payable) once this voucher is on the book.</param>
/// <param name="Excess"><see cref="Exposure"/> − <see cref="Limit"/>, floored at zero.</param>
public readonly record struct CreditLimitCheck(
    bool Breached, Domain.Ledger? Party, Money Limit, Money Exposure, Money Excess)
{
    /// <summary>The "no limit anywhere on this voucher" answer.</summary>
    public static readonly CreditLimitCheck Clear =
        new(false, null, Money.Zero, Money.Zero, Money.Zero);
}

/// <summary>
/// <b>Census 10.1 — Credit Limits.</b> The pure half: who carries a limit, what the limit is measured against, and
/// whether a voucher breaches it. Headless, clock-free and side-effect-free — <see cref="VoucherValidator"/> is the
/// only thing that turns a breach into a refusal, and it does so on ENTRY paths only.
///
/// <para><b>R7 — ATTESTED</b> (help.tallysolutions.com). <i>"Credit limits can be set for ledgers created under the
/// groups Sundry Debtors and Sundry Creditors"</i> (Credit_Limits.htm). On breach, <i>"an error message is
/// displayed"</i> when accepting the voucher, showing the defined limit and the amount exceeded, and the voucher
/// cannot be saved as it stands (Exceeding_Credit_Limits.htm), corroborated on the TallyPrime-era page
/// <c>/manage-receivables-outstanding-tally/</c>: <i>"an error message appears with the credit limit defined for the
/// party while saving the transaction. It also shows the amount that has been exceeded."</i> The escapes the vendor
/// names are exactly two: enter a lower value, or turn on <i>"Override credit limit using post-dated
/// transactions"</i> on the party ledger.</para>
///
/// <para>🔴 <b>THE AMOUNT LIMIT IS A BLOCK; THE CREDIT-PERIOD CHECK IS A WARNING.</b> They are different rules with
/// different severities, and this file implements only the first. Getting the pair backwards is the wrong-money
/// failure this row exists to avoid: blocking on days would refuse legitimate invoices, warning on the amount would
/// let bad ones through. The credit-days warning is <see cref="Ledger.CheckCreditDaysOnEntry"/>'s business and is
/// NOT built in this wave — named here rather than half-done.</para>
///
/// <para>🔴 <b>WHERE THE SOURCE IS SILENT — three decisions are OURS (ruling 9) and are labelled at their site:</b>
/// the arithmetic basis of the comparison (<see cref="ExposureAfter"/>), which voucher families the check applies to
/// (<see cref="AppliesTo"/>), and what the post-dated override actually overrides (<see cref="Check"/>).</para>
/// </summary>
public static class CreditLimitRules
{
    /// <summary>
    /// True when <paramref name="ledger"/> may carry a credit limit at all: it sits under <b>Sundry Debtors</b> or
    /// <b>Sundry Creditors</b> (by ancestry, so a sub-group like "Sundry Debtors &gt; North" qualifies and a rename
    /// of an unrelated group cannot). ATTESTED — the vendor names exactly these two groups.
    ///
    /// <para>The Ledger master shows the Credit Limits block on exactly this predicate, so a limit can never be
    /// captured on a ledger this returns false for.</para>
    /// </summary>
    public static bool CarriesCreditLimit(Company company, Domain.Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(ledger);

        return ClassificationRules.GroupIsUnder(ledger.GroupId, "Sundry Debtors", company)
            || ClassificationRules.GroupIsUnder(ledger.GroupId, "Sundry Creditors", company);
    }

    /// <summary>
    /// The effective limit on <paramref name="ledger"/>, or <c>null</c> for "no limit".
    ///
    /// <para>🔴 <see cref="Domain.Ledger.CreditLimit"/> of <c>null</c> and a limit of <see cref="Money.Zero"/> are
    /// DIFFERENT answers and this method keeps them apart: zero is a real limit that blocks any credit purchase. A
    /// limit stored on a ledger that is no longer under a party group is ignored rather than enforced — the master
    /// screen could not have shown the field, so enforcing it would refuse vouchers on a rule the operator has no
    /// way to see or clear.</para>
    /// </summary>
    public static Money? EffectiveLimit(Company company, Domain.Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!CarriesCreditLimit(company, ledger)) return null;
        return ledger.CreditLimit;
    }

    /// <summary>
    /// Whether the credit-limit block applies to a voucher of <paramref name="baseType"/> at all.
    ///
    /// <para>🔴 <b>OURS (ruling 9), and deliberately the NARROWEST reading of the one sentence there is.</b> The
    /// vendor says only <i>"During sales, purchase, and order transactions, the credit limit and current outstanding
    /// balance of the ledger will be displayed"</i>. Nothing retrievable says whether the check runs on a receipt, a
    /// journal or a credit note, so those are left alone. A Receipt reduces a receivable and could never breach; a
    /// Credit Note likewise; refusing a Journal on a rule the vendor never states would block legitimate entries,
    /// which is the exact half of the wrong-money risk this row must not land on.</para>
    ///
    /// <para><see cref="VoucherBaseType.SalesOrder"/> / <see cref="VoucherBaseType.PurchaseOrder"/> are listed
    /// because the sentence names orders, but they are <b>pure-inventory</b> aggregates in this application and do
    /// not reach <see cref="VoucherValidator"/>'s accounting path today. They are named, not claimed: only Sales and
    /// Purchase can actually be refused here, and no test asserts otherwise.</para>
    /// </summary>
    public static bool AppliesTo(VoucherBaseType baseType)
        => baseType is VoucherBaseType.Sales
                    or VoucherBaseType.Purchase
                    or VoucherBaseType.SalesOrder
                    or VoucherBaseType.PurchaseOrder;

    /// <summary>
    /// The party's exposure once <paramref name="voucher"/> is on the book: the party's balance as at the voucher's
    /// own date, <b>plus</b> this voucher's net effect on that ledger, expressed as a positive magnitude in the
    /// party's natural direction (what a Sundry Debtor owes us; what we owe a Sundry Creditor). A party in credit
    /// yields a negative number, which no non-negative limit can be exceeded by.
    ///
    /// <para>🔴 <b>OURS (ruling 9): "prior outstanding PLUS this voucher", not "prior outstanding alone".</b> No
    /// retrieved source states the arithmetic. This reading is forced by the vendor's own remedy — <i>"enter a lower
    /// value within the prescribed limit to complete the entry"</i> — which is only actionable if the voucher's OWN
    /// value is inside the sum. Measured against the prior balance alone, a voucher could never be refused BY its
    /// own value: the first over-limit invoice would post and only the NEXT one would be refused, which is not what
    /// the vendor's error message describes.</para>
    ///
    /// <para><b>As at the VOUCHER's date, never "today".</b> ER-12: this engine reads no clock. Back-dating an entry
    /// therefore measures it against the book as it stood then, which is the only answer that is stable when the
    /// same voucher is re-validated tomorrow.</para>
    ///
    /// <para><paramref name="replacing"/> is the voucher an Alter is swapping OUT. It is still on the book while
    /// <c>LedgerService.Replace</c> validates, so its effect is subtracted before the new voucher's is added —
    /// otherwise altering a voucher would measure the party's exposure as if BOTH copies existed and an in-place
    /// edit that changes nothing would breach. The subtraction is gated on the same
    /// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> the balance itself used, so a
    /// cancelled or not-yet-due original (which contributed nothing) is not subtracted twice.</para>
    /// </summary>
    public static Money ExposureAfter(
        Company company, Domain.Ledger party, Voucher voucher, Voucher? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(voucher);

        var signed = LedgerBalances.SignedClosing(company, party, voucher.Date);

        if (replacing is not null
            && LedgerBalances.CountsAsOf(
                replacing, voucher.Date, company.FindVoucherType(replacing.TypeId)?.BaseType))
            signed -= SignedEffectOn(replacing, party);

        signed += SignedEffectOn(voucher, party);

        // Signed is debit-positive. A Sundry Debtor's exposure IS the debit balance; a Sundry Creditor's is the
        // credit balance, i.e. the same figure with the sign flipped. Outstandings.KindOf answers which, walking
        // the group's primary nature, so a renamed sub-group cannot change the direction.
        return Outstandings.KindOf(company, party) == OutstandingKind.Payable
            ? new Money(-signed)
            : new Money(signed);
    }

    /// <summary>Σ signed movement of <paramref name="voucher"/>'s own lines on <paramref name="ledger"/>
    /// (debit-positive). Zero when the voucher does not name it.</summary>
    private static decimal SignedEffectOn(Voucher voucher, Domain.Ledger ledger)
    {
        var total = 0m;
        foreach (var line in voucher.Lines)
            if (line.LedgerId == ledger.Id)
                total += line.Signed;
        return total;
    }

    /// <summary>
    /// Measures <paramref name="voucher"/> against every credit limit it touches and returns the <b>first breach</b>
    /// in line order, or <see cref="CreditLimitCheck.Clear"/>.
    ///
    /// <para>🔴 <b>THE POST-DATED OVERRIDE — OURS (ruling 9), and it is a DIVERGENCE FROM THIS TRACK'S OWN DESIGN
    /// BRIEF, taken because the code contradicted the brief.</b> The brief said the flag "excludes post-dated
    /// vouchers from the exposure". It cannot mean that here, and probably cannot mean it anywhere:
    /// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> ALREADY drops a not-yet-due
    /// post-dated voucher from the balance (<c>if (v.PostDated &amp;&amp; v.Date &gt; asOf) return false;</c>), so on
    /// that reading the flag would be a no-op — and a no-op cannot be what the vendor offers as one of exactly two
    /// remedies for a breach. The reading that makes it a remedy is the one its caption states: the
    /// <b>transaction being saved</b>, when it is post-dated, overrides the limit. That is what is implemented, and
    /// it is exercised by its own test.</para>
    ///
    /// <para>Only ledgers this voucher actually names are measured. A limit on some other party is not this
    /// voucher's business, and re-checking the whole book on every save would make lowering one party's limit refuse
    /// unrelated entries.</para>
    /// </summary>
    public static CreditLimitCheck Check(Company company, Voucher voucher, Voucher? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var baseType = company.FindVoucherType(voucher.TypeId)?.BaseType;
        if (baseType is not { } bt || !AppliesTo(bt)) return CreditLimitCheck.Clear;

        var seen = new HashSet<Guid>();
        foreach (var id in LedgerIdsOn(voucher))
        {
            if (!seen.Add(id)) continue;

            var party = company.FindLedger(id);
            if (party is null) continue;

            if (EffectiveLimit(company, party) is not { } limit) continue;

            // The vendor's named escape: a post-dated transaction overrides the limit when the party allows it.
            if (voucher.PostDated && party.OverrideCreditLimitWithPostDated) continue;

            var exposure = ExposureAfter(company, party, voucher, replacing);
            if (exposure.Amount <= limit.Amount) continue;

            return new CreditLimitCheck(
                true, party, limit, exposure, new Money(exposure.Amount - limit.Amount));
        }

        return CreditLimitCheck.Clear;
    }

    /// <summary>The ledgers this voucher names, party first so a breach on the party is the one reported.</summary>
    private static IEnumerable<Guid> LedgerIdsOn(Voucher voucher)
    {
        if (voucher.PartyId is { } pid) yield return pid;
        foreach (var line in voucher.Lines) yield return line.LedgerId;
    }

    /// <summary>
    /// The refusal an operator reads. States the vendor's own two facts — the limit defined for the party, and the
    /// amount by which it is exceeded — plus the resulting exposure, so the entry can be corrected without leaving
    /// the screen. <b>Wording OURS (ruling 9)</b>: no retrievable source states the message text.
    ///
    /// <para>Culture-invariant on purpose: this string is asserted by tests and shown identically on every runner.
    /// <see cref="Money.ToString()"/> is the product's own formatter, so the figures read the same way here as they
    /// do everywhere else in the app.</para>
    /// </summary>
    public static string BreachMessage(CreditLimitCheck check)
    {
        var name = check.Party?.Name ?? "the party";
        return $"Credit limit for '{name}' is {check.Limit}. This voucher would take the outstanding to "
             + $"{check.Exposure}, exceeding the limit by {check.Excess}. Enter a lower value, or turn on "
             + "'Override credit limit using post-dated transactions' on the party ledger.";
    }
}
