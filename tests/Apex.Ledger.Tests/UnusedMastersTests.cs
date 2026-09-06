using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 2.13 — the derived "Unused" master predicate (<see cref="UnusedMasters"/>).</b>
///
/// <para><b>🔴 R7 — FIDELITY, and the two categories are kept strictly apart.</b>
/// <list type="bullet">
///   <item><b>ATTESTED</b> (help.tallysolutions.com, <c>/tally-prime/charts-of-accounts-tally/</c>, section "View
///     and Delete Multiple Unused Masters"): that the vendor DERIVES this from having no recorded transactions
///     rather than storing an inactive flag — <i>"party ledgers with no recorded transactions"</i> — that the
///     chord is <c>Ctrl+J</c> (Exception Reports) &gt; "Show Unused", and that the resulting pane is captioned
///     <i>"List of Ledgers (Unused)"</i>.</item>
///   <item><b>OURS (ruling 9):</b> the WHOLE-BOOK scope (no period argument — see
///     <c>A_ledger_used_only_outside_a_period_is_still_used</c> for why a period-scoped answer would be a
///     data-loss bug), and the decision to treat "named by another master" as used as well as "transacted with".
///     Neither is claimed as fidelity.</item>
/// </list></para>
///
/// <para>Every test here FAILS on today's <c>main</c>: <see cref="UnusedMasters"/> does not exist there.</para>
/// </summary>
public class UnusedMastersTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static Company Seed(string name = "Unused Co") => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    private static DomainLedger AddParty(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
                                Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger AddSales(Company c, string name = "Sales")
    {
        var existing = c.FindLedgerByName(name);
        if (existing is not null) return existing;
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sales Accounts")!.Id,
                                Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A balanced Journal posted through the real engine, so the ledger is genuinely transacted with.</summary>
    private static Voucher PostJournalAgainst(Company c, DomainLedger party, DateOnly on, decimal rupees = 5000m)
    {
        var sales = AddSales(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var v = new Voucher(Guid.NewGuid(), journal.Id, on, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(rupees), DrCr.Credit),
        });
        new LedgerService(c).Post(v);
        return v;
    }

    // ------------------------------------------------------------------ the predicate

    [Fact]
    public void A_ledger_with_no_vouchers_is_unused()
    {
        var c = Seed();
        var party = AddParty(c, "Never Traded Ltd");

        Assert.True(UnusedMasters.IsLedgerUnused(c, party),
            "a freshly created party with no voucher anywhere must be reported unused — that IS the feature.");
    }

    [Fact]
    public void A_ledger_with_a_posted_voucher_is_used()
    {
        var c = Seed();
        var party = AddParty(c, "Acme Traders");
        PostJournalAgainst(c, party, new DateOnly(2024, 4, 10));

        Assert.False(UnusedMasters.IsLedgerUnused(c, party),
            "a ledger carrying a posted voucher is used; listing it as unused would offer it for deletion behind "
          + "a guard that is about to refuse.");
    }

    /// <summary>
    /// <b>🔴 THE SCOPE TEST — the one that pins the data-safety decision.</b> The predicate takes no date and must
    /// never grow one. A period-scoped "unused" would list a ledger whose only vouchers fall outside the period,
    /// i.e. it would invite the operator to delete a master that still carries history.
    /// </summary>
    [Fact]
    public void A_ledger_used_only_outside_a_period_is_still_used()
    {
        var c = Seed();
        var party = AddParty(c, "Old Customer Ltd");

        // The only voucher sits at the very end of the year — outside any "current month" a caller might scope to.
        PostJournalAgainst(c, party, new DateOnly(2025, 3, 28));

        Assert.False(UnusedMasters.IsLedgerUnused(c, party),
            "the ledger has a voucher in the book, so it is used no matter which period is on screen. If this "
          + "ever fails, someone has period-scoped the predicate and the Unused pane now offers ledgers that "
          + "carry history for deletion.");
    }

    /// <summary>
    /// A cancelled voucher still counts. The row and its <c>entry_lines.ledger_id</c> survive cancellation, so the
    /// ledger remains undeletable — and must therefore remain unlisted. This mirrors the deliberate decision
    /// recorded on <see cref="MasterDeletionRules.CountLedgerTransactions"/>.
    /// </summary>
    [Fact]
    public void A_ledger_whose_only_voucher_is_cancelled_is_still_used()
    {
        var c = Seed();
        var party = AddParty(c, "Cancelled Only Ltd");
        var v = PostJournalAgainst(c, party, new DateOnly(2024, 4, 10));

        new LedgerService(c).Cancel(v.Id);

        Assert.False(UnusedMasters.IsLedgerUnused(c, party),
            "cancelling sets a flag; the voucher row and its foreign keys survive, so the ledger is still used. "
          + "Excluding cancelled here would read like a tidy consistency fix and would be a defect.");
    }

    /// <summary>
    /// The predicate answers "has it been USED", not "may it be DELETED" — so a predefined ledger with no
    /// vouchers IS reported unused even though <see cref="MasterDeletionRules.EnsureLedgerDeletable"/> refuses it
    /// outright. Hiding it would misreport the book; the delete verb keeps its own guard.
    /// </summary>
    [Fact]
    public void A_predefined_ledger_with_no_vouchers_is_still_reported_unused()
    {
        var c = Seed();
        var cash = c.FindLedgerByName("Cash");
        Assert.NotNull(cash);
        Assert.True(cash!.IsPredefined, "the fixture's Cash ledger must be predefined or this test proves nothing.");

        Assert.True(UnusedMasters.IsLedgerUnused(c, cash),
            "usage is not deletability. A predefined ledger nothing has touched is genuinely unused.");
    }

    /// <summary>
    /// The stricter half, and it is OURS: a ledger named by another master — here a budget line — is NOT listed,
    /// because the operator following the feature's own purpose would meet the delete guard's second refusal.
    /// </summary>
    [Fact]
    public void A_ledger_named_by_another_master_is_not_listed_as_unused()
    {
        var c = Seed();
        var party = AddParty(c, "Budgeted Ltd");

        Assert.True(UnusedMasters.IsLedgerUnused(c, party), "premise: it starts unused.");

        c.AddBudget(new Budget(
            Guid.NewGuid(), "FY Budget", FyStart, FyStart.AddYears(1).AddDays(-1),
            lines: new[] { BudgetLine.ForLedger(party.Id, BudgetType.OnClosingBalance, Money.FromRupees(1000m)) }));

        Assert.False(UnusedMasters.IsLedgerUnused(c, party),
            "a budget line names it, so deleting it would leave that line pointing at nothing — the same second "
          + "refusal EnsureLedgerDeletable raises. Listing it as unused would set the operator up for it.");
    }

    /// <summary>
    /// <b>The no-second-counter proof.</b> Across every ledger in a book with real vouchers, "unused" and "the
    /// delete guard accepts it or refuses only for predefined/reserved reasons" must never disagree about whether
    /// the ledger carries transactions. If someone forks a private counter into <see cref="UnusedMasters"/>, this
    /// bites.
    /// </summary>
    [Fact]
    public void Unused_is_exactly_the_complement_of_the_deletion_counters()
    {
        var c = Seed();
        var traded = AddParty(c, "Traded Ltd");
        AddParty(c, "Idle Ltd");
        PostJournalAgainst(c, traded, new DateOnly(2024, 5, 2));

        foreach (var l in c.Ledgers)
        {
            var byCounters = MasterDeletionRules.CountLedgerTransactions(c, l) == 0
                          && !MasterDeletionRules.IsLedgerNamedByAnotherMaster(c, l);

            Assert.True(byCounters == UnusedMasters.IsLedgerUnused(c, l),
                $"'{l.Name}': the Unused predicate and the deletion counters disagree. There must be exactly one "
              + "counter — see the remarks on MasterDeletionRules.CountLedgerTransactions.");
        }

        // Non-vacuity: the loop must have seen both answers, or the assertion above proves nothing.
        Assert.Contains(c.Ledgers, l => UnusedMasters.IsLedgerUnused(c, l));
        Assert.Contains(c.Ledgers, l => !UnusedMasters.IsLedgerUnused(c, l));
    }

    /// <summary>The vendor's own caption is what the pane must show, so it is pinned rather than left as a
    /// literal someone can re-word by accident.</summary>
    [Fact]
    public void The_unused_pane_caption_is_the_vendors_own()
        => Assert.Equal("List of Ledgers (Unused)", UnusedMasters.UnusedLedgersCaption);
}
