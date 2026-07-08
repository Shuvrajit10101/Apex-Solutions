using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Negative Cash/Bank row (catalog §16 Exception Reports — "negative cash"; RQ-5 part 2): a cash or bank
/// ledger whose closing balance is negative (i.e. its natural-asset debit balance has gone credit) as of the
/// report date.
/// </summary>
public sealed record NegativeCashBankRow(
    Guid LedgerId,
    string LedgerName,
    DateOnly AsOf,
    LedgerBalance Balance);

/// <summary>
/// The Negative Cash / Bank exception report (catalog §16; RQ-5 part 2). Cash and normal bank are natural
/// <b>asset</b> (debit) balances: a cash account cannot truly be negative, and a normal bank goes negative only
/// via an unintended overdraft. This report lists every <b>asset-nature</b> cash / bank ledger — a Cash-in-Hand
/// ledger, or a Bank Account whose primary ancestor is an asset — whose closing balance
/// (<see cref="LedgerBalances.Closing(Company, Domain.Ledger, DateOnly)"/>) is on the <b>credit</b> side as of
/// <paramref name="asOf"/>, so the user can spot an impossible cash balance or an unintended overdraft.
/// <para><b>Bank OD / OCC are excluded by design.</b> A Bank Overdraft (Bank OD A/c) or Cash-Credit
/// (Bank OCC A/c) ledger is a <b>liability</b>-nature account (it sits under <i>Loans (Liability)</i>): a credit
/// balance there is the amount drawn on the facility and is entirely normal, <b>not</b> an exception. Such
/// ledgers are filtered out via their primary-ancestor nature (<see cref="ClassificationRules.PrimaryNatureOf"/>),
/// so only genuine asset-nature cash/bank credit balances are flagged. This also correctly keeps flagging a
/// normal Bank Account (asset) that a payment has driven credit (overdrawn).</para>
/// Provisional vouchers (Optional / Memorandum / Reversing) are excluded, matching the real books. A <b>pure</b>
/// projection — no UI, no DB. Rows sorted by ledger name.
/// </summary>
public sealed record NegativeCashBank(
    DateOnly AsOf,
    IReadOnlyList<NegativeCashBankRow> Rows)
{
    /// <summary>Builds the Negative Cash / Bank report for the whole company as of <paramref name="asOf"/>.</summary>
    public static NegativeCashBank Build(Company company, DateOnly asOf)
    {
        var rows = new List<NegativeCashBankRow>();

        foreach (var ledger in company.Ledgers)
        {
            if (!ClassificationRules.IsCashOrBankLedger(ledger, company)) continue;

            // A credit balance is only an EXCEPTION for an asset-nature cash/bank account (Cash-in-Hand or a
            // normal Bank Account). A Bank OD / OCC ledger is a LIABILITY-nature facility whose credit balance
            // is the drawn amount — by design, never an exception — so exclude it by its primary-ancestor nature.
            var group = company.FindGroup(ledger.GroupId);
            if (group is null || ClassificationRules.PrimaryNatureOf(group, company) != GroupNature.Asset)
                continue;

            var signed = LedgerBalances.SignedClosing(company, ledger, asOf);
            if (signed >= 0m) continue; // only a credit (negative) balance is an exception

            rows.Add(new NegativeCashBankRow(
                ledger.Id, ledger.Name, asOf, LedgerBalance.FromSigned(signed)));
        }

        rows.Sort((a, b) => string.Compare(a.LedgerName, b.LedgerName, StringComparison.OrdinalIgnoreCase));
        return new NegativeCashBank(asOf, rows);
    }
}
