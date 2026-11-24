using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>2.9</b>, schema v70 — the budget tree's engine rules: which parent may be chosen
/// (<see cref="BudgetHierarchy.ParentRefusal"/>), the List of Budgets order (<see cref="BudgetHierarchy.TreeOrder"/>),
/// the delete refusal for a budget with budgets under it (<see cref="MasterDeletionRules.EnsureBudgetDeletable"/>),
/// and the group guard no longer counting budgets.
///
/// <para><b>Mutations (run 2026-11-22, each reverted with git checkout and confirmed empty):</b>
/// (1) <c>IsSelfOrDescendant</c> returning <c>false</c> unconditionally reddens
/// <see cref="Filing_a_budget_under_itself_or_under_its_own_descendant_is_refused_by_name"/>;
/// (2) <c>EnsureBudgetDeletable</c> returning at once reddens
/// <see cref="A_budget_with_budgets_under_it_is_refused_with_the_count_and_the_names_and_a_leaf_is_not"/>;
/// (3) re-adding <c>company.Budgets.Count(b =&gt; b.UnderId == group.Id)</c> to the group guard reddens
/// <see cref="The_group_guard_no_longer_counts_budgets_because_a_budget_is_never_filed_under_a_group"/>.</para>
/// </summary>
public sealed class BudgetHierarchyTests
{
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2025, 3, 31);

    private static (Company C, Budget Dept, Budget Sales, Budget North, Budget Ops) Tree()
    {
        var c = CompanyFactory.CreateSeeded("Budget Tree Co", From);
        var dept = new Budget(Guid.NewGuid(), "Department Budget", From, To);
        var sales = new Budget(Guid.NewGuid(), "Sales Department Budget", From, To, underId: dept.Id);
        var north = new Budget(Guid.NewGuid(), "North Sales Budget", From, To, underId: sales.Id);
        var ops = new Budget(Guid.NewGuid(), "Ops Budget", From, To);
        // Insertion order deliberately NOT tree order.
        c.AddBudget(north);
        c.AddBudget(ops);
        c.AddBudget(sales);
        c.AddBudget(dept);
        return (c, dept, sales, north, ops);
    }

    [Fact]
    public void Filing_a_budget_under_itself_or_under_its_own_descendant_is_refused_by_name()
    {
        var (c, dept, sales, north, ops) = Tree();

        Assert.Equal("A budget cannot be filed under itself.", BudgetHierarchy.ParentRefusal(c, dept.Id, dept.Id));
        Assert.Equal("A budget cannot be filed under 'North Sales Budget', which is filed under it.",
            BudgetHierarchy.ParentRefusal(c, dept.Id, north.Id));
        Assert.Equal("A budget cannot be filed under 'Sales Department Budget', which is filed under it.",
            BudgetHierarchy.ParentRefusal(c, dept.Id, sales.Id));

        // Allowed: Primary, a sibling tree, and moving a leaf up a level.
        Assert.Null(BudgetHierarchy.ParentRefusal(c, dept.Id, null));
        Assert.Null(BudgetHierarchy.ParentRefusal(c, dept.Id, ops.Id));
        Assert.Null(BudgetHierarchy.ParentRefusal(c, north.Id, dept.Id));
        // A budget not yet created can go under anything that exists.
        Assert.Null(BudgetHierarchy.ParentRefusal(c, null, north.Id));
        Assert.Equal("The budget chosen under 'Under' no longer exists.",
            BudgetHierarchy.ParentRefusal(c, null, Guid.NewGuid()));
    }

    [Fact]
    public void An_existing_loop_is_treated_as_would_loop_and_never_walked_forever()
    {
        var c = CompanyFactory.CreateSeeded("Loop Co", From);
        var a = new Budget(Guid.NewGuid(), "A", From, To);
        var b = new Budget(Guid.NewGuid(), "B", From, To, underId: a.Id);
        a.UnderId = b.Id;   // only an imported or hand-edited book could carry this
        var x = new Budget(Guid.NewGuid(), "X", From, To);
        c.AddBudget(a);
        c.AddBudget(b);
        c.AddBudget(x);

        Assert.True(BudgetHierarchy.IsSelfOrDescendant(c, a.Id, x.Id));   // the walk hits the loop: refuse
        var order = BudgetHierarchy.TreeOrder(c);
        Assert.Equal(3, order.Count);   // nothing drops out of the list
        Assert.Equal(new[] { "A", "B", "X" }.OrderBy(n => n), order.Select(o => o.Budget.Name).OrderBy(n => n));
    }

    [Fact]
    public void The_List_of_Budgets_order_is_depth_first_with_name_sorted_siblings()
    {
        var (c, _, _, _, _) = Tree();
        var order = BudgetHierarchy.TreeOrder(c).Select(o => (o.Budget.Name, o.Depth)).ToList();
        Assert.Equal(
            new[]
            {
                ("Department Budget", 0),
                ("Sales Department Budget", 1),
                ("North Sales Budget", 2),
                ("Ops Budget", 0),
            },
            order);
    }

    [Fact]
    public void A_budget_with_budgets_under_it_is_refused_with_the_count_and_the_names_and_a_leaf_is_not()
    {
        var (c, dept, sales, north, _) = Tree();
        var extra = new Budget(Guid.NewGuid(), "Marketing Department Budget", From, To, underId: dept.Id);
        c.AddBudget(extra);

        var ex = Assert.Throws<InvalidOperationException>(() => MasterDeletionRules.EnsureBudgetDeletable(c, dept));
        Assert.Equal(
            "Cannot delete budget 'Department Budget': 2 budgets are filed under it "
            + "(Marketing Department Budget, Sales Department Budget). Move or delete them first.",
            ex.Message);

        var one = Assert.Throws<InvalidOperationException>(() => MasterDeletionRules.EnsureBudgetDeletable(c, sales));
        Assert.Contains("1 budget is filed under it (North Sales Budget)", one.Message);

        Assert.Null(Record.Exception(() => MasterDeletionRules.EnsureBudgetDeletable(c, north)));
        Assert.Equal(4 + 1, c.Budgets.Count);   // a guard never mutates
    }

    [Fact]
    public void The_group_guard_no_longer_counts_budgets_because_a_budget_is_never_filed_under_a_group()
    {
        var c = CompanyFactory.CreateSeeded("Group Guard Co", From);
        var indirect = c.Groups.Single(g => g.Name == "Indirect Expenses");
        var custom = new GroupService(c).CreateGroup("Hc Projects", indirect.Id);
        // An in-memory budget whose UnderId happens to equal the group's id (a v69-shaped value the v70 migration
        // clears). It is not "filed under" the group, so the group is deletable.
        c.AddBudget(new Budget(Guid.NewGuid(), "Legacy Shape Budget", From, To, underId: custom.Id));

        Assert.Null(Record.Exception(() => MasterDeletionRules.EnsureGroupDeletable(c, custom)));
    }
}
