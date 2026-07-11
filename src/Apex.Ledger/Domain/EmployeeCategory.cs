namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>Employee Category</b> (Phase 8 slice 1; Study Guide pp.193–195) — the parallel classification axis for
/// employees (mirrors <see cref="CostCategory"/>). It partitions the workforce independently of the
/// <see cref="EmployeeGroup"/> department tree (e.g. Direct vs Indirect, on-roll vs contract), so the same
/// employee can be reported along two axes, and — like its cost-category counterpart — it declares whether it may
/// allocate revenue and/or non-revenue cost items (RQ-2; requirements §3.2: "allocate revenue/non-revenue"). It
/// carries no balance of its own.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place. At least
/// one of <see cref="AllocateRevenueItems"/> / <see cref="AllocateNonRevenueItems"/> must be true (mirror of
/// <see cref="CostCategory"/>'s "≥1 must be Yes"). Not seeded on company creation — a company that never turns
/// Payroll on carries no employee categories (ER-13 byte-identical). Framework- and DB-agnostic.
/// </remarks>
public sealed class EmployeeCategory
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company (case-insensitive); a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>"Allocate Revenue Items" (requirements §3.2): may allocate P&amp;L (income/expense) lines.</summary>
    public bool AllocateRevenueItems { get; set; }

    /// <summary>"Allocate Non-Revenue Items" (requirements §3.2): may allocate balance-sheet lines.</summary>
    public bool AllocateNonRevenueItems { get; set; }

    /// <summary>True for a predefined category that cannot be deleted (none are seeded in this slice).</summary>
    public bool IsPredefined { get; }

    public EmployeeCategory(
        Guid id,
        string name,
        bool allocateRevenueItems = true,
        bool allocateNonRevenueItems = false,
        bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Employee category name is required.", nameof(name));
        if (!allocateRevenueItems && !allocateNonRevenueItems)
            throw new ArgumentException(
                "An employee category must allocate revenue and/or non-revenue items (at least one must be Yes).",
                nameof(allocateRevenueItems));

        Id = id;
        Name = name.Trim();
        AllocateRevenueItems = allocateRevenueItems;
        AllocateNonRevenueItems = allocateNonRevenueItems;
        IsPredefined = isPredefined;
    }
}
