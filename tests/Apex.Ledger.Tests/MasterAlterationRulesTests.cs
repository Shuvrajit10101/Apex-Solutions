using System;
using System.Linq;
using System.Reflection;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// WI-3 — the <b>Alter</b> verb's shared engine guards (<see cref="MasterAlterationRules"/>) and
/// <see cref="GroupService.AlterGroup"/>, plus the WI-4 single-State invariant.
///
/// <para>The centrepiece is <see cref="A_rename_propagates_retroactively_to_every_posted_voucher"/>: altering a
/// master must be identity-preserving, so a renamed ledger keeps resolving in vouchers posted BEFORE the rename.
/// The rest lock the failure modes the CA-audit backlog identified as silent: an except-self uniqueness check that
/// fails closed on the commonest alteration of all; a rename of a ledger the engine resolves by hardcoded name
/// (which fails with no error at all — <c>B2cQrService</c> just returns 0); a cyclic re-parent that only surfaces
/// much later as an exception thrown out of a report; and a re-parented sub-tree that keeps its old ancestry's
/// nature and lands on the wrong side of the Balance Sheet.</para>
/// </summary>
public class MasterAlterationRulesTests
{
    private static readonly DateOnly From = new(2024, 4, 1);

    private static Company Seed(string name = "Alter Co") => CompanyFactory.CreateSeeded(name, From, From);

    // ================================================================= identity: a rename propagates

    [Fact]
    public void A_rename_propagates_retroactively_to_every_posted_voucher()
    {
        var c = Seed();
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var sales = AddAndReturn(c, new Ledger.Domain.Ledger(
            Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false));
        var party = new Ledger.Domain.Ledger(
            Guid.NewGuid(), "Acme Traders", debtors.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(party);

        var journal = c.FindVoucherTypeByName("Journal")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(party.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        var voucherId = c.Vouchers.Single().Id;
        var partyId = party.Id;

        // The alteration: rename in place, against the stable Guid.
        var newName = MasterAlterationRules.EnsureNameAvailable(c, "Acme Traders Pvt Ltd", partyId, MasterKind.Ledger);
        MasterAlterationRules.EnsureLedgerRenameAllowed(party, newName);
        party.Name = newName;

        // Identity is unchanged — no new master appeared, and the id did not move.
        Assert.Equal(partyId, party.Id);
        Assert.Single(c.Ledgers, l => l.Id == partyId);
        Assert.Null(c.FindLedgerByName("Acme Traders"));
        Assert.Equal(partyId, c.FindLedgerByName("Acme Traders Pvt Ltd")!.Id);

        // The PRE-EXISTING voucher still resolves to that ledger, and now reports the NEW name — retroactively.
        var voucher = c.FindVoucher(voucherId)!;
        var line = voucher.Lines.Single(l => l.LedgerId == partyId);
        Assert.Equal("Acme Traders Pvt Ltd", c.FindLedger(line.LedgerId)!.Name);

        // And the ledger balance the report engine computes is untouched by the rename.
        Assert.Equal(5000m, LedgerBalances.SignedClosing(c, party, new DateOnly(2024, 4, 30)));
    }

    // ================================================================= except-self uniqueness

    [Fact]
    public void Accepting_an_alteration_without_renaming_is_allowed()
    {
        // The commonest alteration of all: open a master, change something unrelated, accept. A create-time
        // uniqueness check reused verbatim rejects this, because the master collides with ITSELF.
        var c = Seed();
        var ledger = c.Ledgers.First();

        var name = MasterAlterationRules.EnsureNameAvailable(c, ledger.Name, ledger.Id, MasterKind.Ledger);
        Assert.Equal(ledger.Name, name);
    }

    [Fact]
    public void Renaming_onto_a_DIFFERENT_masters_name_is_still_rejected()
    {
        var c = Seed();
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var a = new Ledger.Domain.Ledger(Guid.NewGuid(), "Alpha", debtors.Id, Money.Zero, true);
        var b = new Ledger.Domain.Ledger(Guid.NewGuid(), "Beta", debtors.Id, Money.Zero, true);
        c.AddLedger(a);
        c.AddLedger(b);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterAlterationRules.EnsureNameAvailable(c, "Beta", a.Id, MasterKind.Ledger));
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_group_may_not_take_the_reserved_profit_and_loss_head_name()
    {
        // FindGroupByName deliberately EXCLUDES the reserved P&L head, so a naive check would let a second
        // "Profit & Loss A/c" group be created and fork the report classification. The rule uses the
        // head-INCLUDING lookup.
        var c = Seed();
        var plHead = c.ProfitAndLossHead!;
        var custom = c.Groups.First(g => !g.IsPredefined || g.ParentId is not null);

        Assert.Null(c.FindGroupByName(plHead.Name));          // the reason the naive check would pass
        Assert.NotNull(c.FindGroupOrHeadByName(plHead.Name));  // the reason the rule catches it

        Assert.Throws<InvalidOperationException>(
            () => MasterAlterationRules.EnsureNameAvailable(c, plHead.Name, custom.Id, MasterKind.Group));
    }

    // ================================================================= protected ledgers

    [Theory]
    [InlineData("Cash")]
    [InlineData("Round Off")]
    public void A_ledger_the_engine_resolves_by_name_cannot_be_renamed(string reserved)
    {
        // Renaming one of these breaks a code path SILENTLY — B2cQrService returns 0 rounding with no error at all
        // when "Round Off" is missing. IsPredefined alone does not cover it: only Cash and P&L carry that flag.
        var c = Seed();
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var ledger = c.FindLedgerByName(reserved)
            ?? AddAndReturn(c, new Ledger.Domain.Ledger(Guid.NewGuid(), reserved, debtors.Id, Money.Zero, true));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterAlterationRules.EnsureLedgerRenameAllowed(ledger, "Something Else"));
        Assert.Contains(reserved, ex.Message);

        // But every OTHER field on that ledger stays alterable — the guard is a rename guard, not a freeze.
        MasterAlterationRules.EnsureLedgerRenameAllowed(ledger, ledger.Name);
    }

    [Fact]
    public void An_ordinary_ledger_cannot_be_renamed_ONTO_a_reserved_name()
    {
        var c = Seed();
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var ordinary = AddAndReturn(c, new Ledger.Domain.Ledger(
            Guid.NewGuid(), "Acme", debtors.Id, Money.Zero, true));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterAlterationRules.EnsureLedgerRenameAllowed(ordinary, "Round Off"));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================= group re-parent: cycles + nature cascade

    [Fact]
    public void A_group_cannot_be_re_parented_under_its_own_descendant()
    {
        var c = Seed();
        var svc = new GroupService(c);
        var liabilities = c.FindGroupByName("Current Liabilities")!;
        var parent = svc.CreateGroup("Payables", liabilities.Id);
        var child = svc.CreateGroup("Salary Payable", parent.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.AlterGroup(parent.Id, "Payables", child.Id));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing moved — the guard validates before mutating.
        Assert.Equal(liabilities.Id, parent.ParentId);
    }

    [Fact]
    public void A_group_cannot_become_its_own_parent()
    {
        var c = Seed();
        var svc = new GroupService(c);
        var g = svc.CreateGroup("Payables", c.FindGroupByName("Current Liabilities")!.Id);

        Assert.Throws<InvalidOperationException>(() => svc.AlterGroup(g.Id, "Payables", g.Id));
    }

    [Fact]
    public void Re_parenting_a_group_re_derives_its_nature_AND_cascades_to_every_descendant()
    {
        // The silent-misclassification case: a two-deep sub-tree built under a LIABILITY head, then moved under an
        // EXPENSE head. Without the cascade the descendants keep Nature=Liability and print on the wrong side of
        // the Balance Sheet — the sheet still balances, so nothing fails loudly.
        var c = Seed();
        var svc = new GroupService(c);
        var liabilities = c.FindGroupByName("Current Liabilities")!;
        var expenses = c.FindGroupByName("Indirect Expenses")!;

        var parent = svc.CreateGroup("Staff Costs", liabilities.Id);
        var child = svc.CreateGroup("Salary Payable", parent.Id);
        var grandchild = svc.CreateGroup("Bonus Payable", child.Id);
        Assert.Equal(GroupNature.Liability, parent.Nature);
        Assert.Equal(GroupNature.Liability, grandchild.Nature);

        svc.AlterGroup(parent.Id, "Staff Costs", expenses.Id);

        Assert.Equal(GroupNature.Expense, parent.Nature);
        Assert.Equal(GroupNature.Expense, child.Nature);
        Assert.Equal(GroupNature.Expense, grandchild.Nature);
        // And the classification the reports actually use agrees at every depth.
        Assert.Equal(GroupNature.Expense, ClassificationRules.PrimaryNatureOf(grandchild, c));
    }

    [Fact]
    public void A_predefined_group_cannot_be_renamed_or_moved()
    {
        var c = Seed();
        var svc = new GroupService(c);
        var liabilities = c.FindGroupByName("Current Liabilities")!;
        Assert.True(liabilities.IsPredefined);

        Assert.Throws<InvalidOperationException>(
            () => svc.AlterGroup(liabilities.Id, "My Liabilities", liabilities.ParentId));
        Assert.Equal("Current Liabilities", liabilities.Name);
    }

    [Fact]
    public void Altering_a_group_renames_it_in_place_and_its_ledgers_follow()
    {
        var c = Seed();
        var svc = new GroupService(c);
        var group = svc.CreateGroup("Payables", c.FindGroupByName("Current Liabilities")!.Id);
        var ledger = AddAndReturn(c, new Ledger.Domain.Ledger(
            Guid.NewGuid(), "Vendor A", group.Id, Money.Zero, openingIsDebit: false));
        var groupId = group.Id;

        svc.AlterGroup(groupId, "Trade Payables", c.FindGroupByName("Current Liabilities")!.Id, alias: "TP");

        Assert.Equal(groupId, group.Id);                         // identity preserved
        Assert.Equal("Trade Payables", c.FindGroup(groupId)!.Name);
        Assert.Equal("TP", c.FindGroup(groupId)!.Alias);
        Assert.Equal(groupId, ledger.GroupId);                    // the ledger followed automatically
        Assert.Equal("Trade Payables", c.FindGroup(ledger.GroupId)!.Name);
    }

    // ================================================================= reclassification detection

    [Fact]
    public void Moving_a_ledger_across_the_balance_sheet_PL_divide_is_reported_as_a_reclassification()
    {
        var c = Seed();
        var liabilities = c.FindGroupByName("Current Liabilities")!;   // Balance Sheet
        var expenses = c.FindGroupByName("Indirect Expenses")!;        // P&L
        var otherLiability = c.FindGroupByName("Current Assets")!;     // Balance Sheet

        Assert.True(MasterAlterationRules.DescribesReclassification(c, liabilities, expenses));
        Assert.False(MasterAlterationRules.DescribesReclassification(c, liabilities, otherLiability));
    }

    // ================================================================= WI-4: ONE State field

    [Fact]
    public void The_mailing_details_block_declares_no_State_member_at_all()
    {
        // The structural half of the single-State guarantee. If someone later adds a State/StateCode property to
        // PartyMailingDetails, this fails immediately — before it can silently contradict the place of supply.
        var members = typeof(PartyMailingDetails)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain(members, n => n.Contains("State", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_mailing_State_and_the_GST_place_of_supply_State_cannot_diverge()
    {
        var c = Seed();
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var party = AddAndReturn(c, new Ledger.Domain.Ledger(
            Guid.NewGuid(), "Acme", debtors.Id, Money.Zero, true));
        party.Mailing = new PartyMailingDetails { Address = "1 High St", Country = "India", Pincode = "700001" };

        // Writing the mailing State creates/updates the ONE stored value that drives place of supply.
        party.MailingStateCode = "27";                       // Maharashtra
        Assert.Equal("27", party.PartyGst!.StateCode);
        Assert.Equal("27", party.MailingStateCode);
        Assert.Equal("Maharashtra", party.PartyGst!.State!.Name);

        // Writing the GST State is read back by the mailing block — there is no second value to fall out of step.
        party.PartyGst!.StateCode = "19";                     // West Bengal
        Assert.Equal("19", party.MailingStateCode);

        // Clearing it clears both views, and never fabricates a GST block.
        party.MailingStateCode = null;
        Assert.Null(party.PartyGst!.StateCode);
        Assert.Null(party.MailingStateCode);
    }

    [Fact]
    public void Clearing_the_mailing_State_on_a_ledger_with_no_GST_block_does_not_create_one()
    {
        var c = Seed();
        var party = AddAndReturn(c, new Ledger.Domain.Ledger(
            Guid.NewGuid(), "Plain", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true));
        Assert.Null(party.PartyGst);

        party.MailingStateCode = null;
        Assert.Null(party.PartyGst);   // ER-13: an untouched ledger stays byte-identical

        party.MailingStateCode = "19";
        Assert.NotNull(party.PartyGst);
    }

    // ================================================================= PIN validation

    [Theory]
    [InlineData("700001", true)]
    [InlineData("110001", true)]
    [InlineData("", true)]        // blank is legal — a party whose PIN is not to hand must still be creatable
    [InlineData("70001", false)]  // five digits
    [InlineData("7000011", false)]
    [InlineData("012345", false)] // an Indian PIN never starts with 0
    [InlineData("70A001", false)]
    public void A_PIN_code_is_validated_as_a_six_digit_Indian_PIN(string pin, bool valid)
    {
        var block = new PartyMailingDetails { Pincode = pin };
        if (valid) block.EnsureValid();
        else Assert.Throws<ArgumentException>(() => block.EnsureValid());
    }

    [Fact]
    public void An_all_blank_mailing_block_reports_itself_empty_so_it_can_be_dropped_to_null()
    {
        Assert.True(new PartyMailingDetails().IsEmpty);
        Assert.True(new PartyMailingDetails { Address = "   " }.IsEmpty);
        Assert.False(new PartyMailingDetails { Address = "1 High St" }.IsEmpty);
    }

    [Fact]
    public void The_address_splits_into_printable_lines()
    {
        var block = new PartyMailingDetails { Address = "12 Park Street\n\nKolkata" };
        Assert.Equal(new[] { "12 Park Street", "Kolkata" }, block.AddressLines);
    }

    private static Ledger.Domain.Ledger AddAndReturn(Company c, Ledger.Domain.Ledger l)
    {
        c.AddLedger(l);
        return l;
    }
}
