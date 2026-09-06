using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// <b>Census 2.13 — the derived "Unused" master view.</b> A single predicate answering one question: has anything
/// in this book ever named this ledger?
///
/// <para><b>🔴 THE FINDING THAT SHAPED THIS FILE: the vendor DERIVES this, it does not STORE it.</b> The census row
/// is titled "Show Inactive / hidden masters", and the obvious implementation — an <c>is_active</c> column on every
/// master — would have been an invention. help.tallysolutions.com's Chart of Accounts page
/// (<c>/tally-prime/charts-of-accounts-tally/</c>) carries no "Show Inactive" and no inactive master status; the
/// word "inactive" appears there once, as UI jargon about <c>Ctrl+H</c> being greyed out. The retrievable feature
/// is the section <b>"View and Delete Multiple Unused Masters"</b>: <i>"If there are certain masters such as party
/// ledgers with no recorded transactions … then you can delete those ledgers"</i>, reached by
/// <b><c>Ctrl+J</c> (Exception Reports) &gt; "Show Unused"</b>, producing the <b>"List of Ledgers (Unused)"</b>.
/// So the answer is computed from the vouchers, and <b>this row takes no schema column</b> — v54 carries nothing
/// for it.</para>
///
/// <para><b>🔴 WHOLE-BOOK SCOPE, NOT THE CURRENT PERIOD — and that is a data-safety decision, not a shortcut.</b>
/// The feature exists so the listed masters can be deleted. A period-scoped answer would offer for deletion a
/// ledger that has vouchers outside the period, i.e. it would present a ledger the delete guard is about to refuse
/// — or, worse, invite an operator to clear a period first and then delete a master that still carries history.
/// <c>IsLedgerUnused</c> therefore looks at <c>company.Vouchers</c> entire, with no date argument anywhere in the
/// signature so no caller can accidentally narrow it.</para>
///
/// <para><b>🔴 "UNUSED" MEANS NOTHING NAMES IT — stricter than "no transactions", deliberately, and this
/// difference is OURS (ruling 9).</b> The vendor's sentence says "no recorded transactions". Taken literally, a
/// ledger named only by a pay head, a budget line or a POS till default would be listed as unused, and the
/// operator following the feature's own purpose to its end would meet
/// <see cref="MasterDeletionRules.EnsureLedgerDeletable"/>'s SECOND refusal ("other masters and settings name
/// it"). A pane that lists masters it is about to refuse to delete is worse than no pane. So the predicate is the
/// exact complement of BOTH deletion counters — <see cref="MasterDeletionRules.CountLedgerTransactions"/> and
/// <see cref="MasterDeletionRules.IsLedgerNamedByAnotherMaster"/> — and there is no second counter anywhere.</para>
///
/// <para><b>Predefined and reserved ledgers ARE reported unused when nothing names them</b>, even though
/// <see cref="MasterDeletionRules.EnsureLedgerDeletable"/> refuses them outright. That is not an inconsistency:
/// this predicate answers "has it been used", not "may it be deleted". Cash is genuinely unused in a book that has
/// never touched it, and hiding it would misreport the book. The delete verb keeps its own guard; in this wave the
/// Unused pane is a VIEW and deletes nothing.</para>
///
/// <para><b>Deliberately NOT here: stock items.</b> The vendor page documents "Delete Unused Stock Items at once"
/// beside the ledger arm, and the same complement would be trivial to write against
/// <see cref="MasterDeletionRules.EnsureStockItemDeletable"/>'s counters. It is absent because this application has
/// <b>no stock-item list screen</b> to host it — <c>ChartOfAccountsViewModel</c> is accounts-only (it walks
/// <c>Groups</c> and <c>Ledgers</c> and nothing else), and <c>StockItemMasterViewModel</c> is a create/alter form,
/// not a browsable tree. A predicate with no screen to display it is exactly the dead capability this project has
/// filed twice already (<c>CostReports.BuildLedgerBreakup</c>, <c>MultiAccountPrintViewModel</c>). The stock-item
/// arm needs a stock-item list screen FIRST; it is named debt, not an oversight.</para>
/// </summary>
public static class UnusedMasters
{
    /// <summary>The heading the filtered pane carries — the vendor's own caption, verbatim.</summary>
    public const string UnusedLedgersCaption = "List of Ledgers (Unused)";

    /// <summary>
    /// True when nothing in <paramref name="company"/> names <paramref name="ledger"/>: no voucher has been posted
    /// against it (as party, entry line, or POS tender) and no other master or setting points at it. Whole-book
    /// scope — see the type remarks for why there is no date parameter.
    /// </summary>
    public static bool IsLedgerUnused(Company company, Domain.Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(ledger);

        return MasterDeletionRules.CountLedgerTransactions(company, ledger) == 0
            && !MasterDeletionRules.IsLedgerNamedByAnotherMaster(company, ledger);
    }
}
