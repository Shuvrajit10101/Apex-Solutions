using Apex.Ledger.Domain;
using Apex.Ledger.Seed;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Seed-verification tests (design §5; plan.md §4.4): a fresh company must contain exactly
/// 28 groups, 2 ledgers, 24 voucher types, Primary Cost Category, Main Location, ₹/INR 2-dp
/// "Paisa", FY 1-Apr→31-Mar. Guards the historical 28-vs-29 group drift.
/// </summary>
public class SeedTests
{
    private static Company Fresh() => CompanyFactory.CreateSeeded("Acme Test Co", new DateOnly(2024, 4, 1));

    [Fact]
    public void Fresh_company_has_exactly_28_groups()
    {
        var c = Fresh();
        Assert.Equal(28, c.Groups.Count);
    }

    [Fact]
    public void Fresh_company_has_exactly_2_ledgers()
    {
        var c = Fresh();
        Assert.Equal(2, c.Ledgers.Count);
        Assert.NotNull(c.FindLedgerByName("Cash"));
        Assert.NotNull(c.FindLedgerByName("Profit & Loss A/c"));
    }

    [Fact]
    public void Fresh_company_has_exactly_24_voucher_types()
    {
        var c = Fresh();
        Assert.Equal(24, c.VoucherTypes.Count);
    }

    [Fact]
    public void Fresh_company_seeds_primary_cost_category_and_main_location()
    {
        var c = Fresh();
        Assert.Equal("Primary Cost Category", c.PrimaryCostCategoryName);
        Assert.Equal("Main Location", c.MainLocationName);
    }

    [Fact]
    public void Fresh_company_has_rupee_paisa_currency_and_april_financial_year()
    {
        var c = Fresh();
        Assert.Equal("₹", c.BaseCurrencySymbol);
        Assert.Equal("INR", c.BaseCurrencyName);
        Assert.Equal(2, c.DecimalPlaces);
        Assert.Equal("Paisa", c.DecimalUnitName);
        Assert.Equal(new DateOnly(2024, 4, 1), c.FinancialYearStart);
    }

    [Fact]
    public void The_15_primary_groups_have_no_parent_and_correct_natures()
    {
        var c = Fresh();
        var primaries = c.Groups.Where(g => g.IsPrimary).ToList();
        Assert.Equal(15, primaries.Count);

        Assert.Equal(GroupNature.Liability, c.FindGroupByName("Capital Account")!.Nature);
        Assert.Equal(GroupNature.Asset, c.FindGroupByName("Fixed Assets")!.Nature);
        Assert.Equal(GroupNature.Income, c.FindGroupByName("Sales Accounts")!.Nature);
        Assert.Equal(GroupNature.Expense, c.FindGroupByName("Purchase Accounts")!.Nature);
        // Misc. Expenses (Asset) is an ASSET despite the word "Expenses" (verification §A6/A7).
        Assert.Equal(GroupNature.Asset, c.FindGroupByName("Misc. Expenses (Asset)")!.Nature);
    }

    [Fact]
    public void The_13_subgroups_nest_under_their_canonical_parents()
    {
        var c = Fresh();
        var subs = c.Groups.Where(g => !g.IsPrimary).ToList();
        Assert.Equal(13, subs.Count);

        void AssertUnder(string child, string parent)
        {
            var cg = c.FindGroupByName(child)!;
            var pg = c.FindGroupByName(parent)!;
            Assert.Equal(pg.Id, cg.ParentId);
        }

        AssertUnder("Sundry Debtors", "Current Assets");
        AssertUnder("Sundry Creditors", "Current Liabilities");
        AssertUnder("Bank Accounts", "Current Assets");
        AssertUnder("Cash-in-Hand", "Current Assets");
        AssertUnder("Stock-in-Hand", "Current Assets");
        AssertUnder("Reserves & Surplus", "Capital Account");
        AssertUnder("Secured Loans", "Loans (Liability)");
    }

    [Fact]
    public void Bank_OCC_is_an_alias_of_Bank_OD_not_a_29th_group()
    {
        var c = Fresh();
        var byOd = c.FindGroupByName("Bank OD A/c");
        var byOcc = c.FindGroupByName("Bank OCC A/c");
        Assert.NotNull(byOd);
        Assert.NotNull(byOcc);
        Assert.Equal(byOd!.Id, byOcc!.Id); // same group, resolved via alias
        Assert.Equal(28, c.Groups.Count);
    }

    [Fact]
    public void Core_accounting_shortcuts_resolve_to_the_right_base_types()
    {
        var c = Fresh();

        (string Name, VoucherBaseType Base, string Shortcut)[] expected =
        {
            ("Contra", VoucherBaseType.Contra, "F4"),
            ("Payment", VoucherBaseType.Payment, "F5"),
            ("Receipt", VoucherBaseType.Receipt, "F6"),
            ("Journal", VoucherBaseType.Journal, "F7"),
            ("Sales", VoucherBaseType.Sales, "F8"),
            ("Purchase", VoucherBaseType.Purchase, "F9"),
            ("Credit Note", VoucherBaseType.CreditNote, "Alt+F6"),
            ("Debit Note", VoucherBaseType.DebitNote, "Alt+F5"),
        };

        foreach (var (name, baseType, shortcut) in expected)
        {
            var t = c.FindVoucherTypeByName(name);
            Assert.NotNull(t);
            Assert.Equal(baseType, t!.BaseType);
            Assert.Equal(shortcut, t.DefaultShortcut);
        }
    }

    [Fact]
    public void Payroll_and_jobwork_types_are_inactive_by_default()
    {
        var c = Fresh();
        Assert.False(c.FindVoucherTypeByName("Payroll")!.IsActive);
        Assert.False(c.FindVoucherTypeByName("Attendance")!.IsActive);
        Assert.False(c.FindVoucherTypeByName("Material In")!.IsActive);
        Assert.True(c.FindVoucherTypeByName("Payment")!.IsActive);
        Assert.True(c.FindVoucherTypeByName("Memorandum")!.IsActive);
    }

    [Fact]
    public void Seed_count_constants_agree_with_the_built_sets()
    {
        Assert.Equal(SeedGroups.Count, CompanyFactory.SeedGroupSet().Count);
        Assert.Equal(SeedVoucherTypes.Count, CompanyFactory.SeedVoucherTypeSet().Count);
        Assert.Equal(28, SeedGroups.Count);
        Assert.Equal(24, SeedVoucherTypes.Count);
        Assert.Equal(2, SeedLedgers.Count);
    }
}
