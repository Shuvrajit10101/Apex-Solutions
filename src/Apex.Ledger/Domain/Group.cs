namespace Apex.Ledger.Domain;

/// <summary>
/// A classification node in the chart of accounts: a nature plus a parent
/// (catalog §3; plan.md §4.1). The 28 predefined groups form the backbone;
/// custom groups nest under any of them. The <see cref="Id"/> is the stable
/// key — the <see cref="Name"/> is not, so an Alter renames in place.
/// </summary>
public sealed class Group
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Asset / Liability / Income / Expense — equal to the primary ancestor's nature.</summary>
    public GroupNature Nature { get; set; }

    /// <summary>Parent group; <c>null</c> ⇒ this is one of the 15 primary heads.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>True for the 28 seeded groups — they cannot be deleted (§6).</summary>
    public bool IsPredefined { get; }

    /// <summary>
    /// The accounting group's GST details block — the "Group" level of the five-level GST hierarchy, sitting
    /// directly below the Sales/Purchase ledger in the shipped <see cref="GstDetailSource.LedgerFirst"/> walk
    /// (plan.md Phase 10.10 WF-1; register IV-1). <c>null</c> ⇒ this group declares no GST details and contributes
    /// nothing to a lookup, which is how every pre-v51 group reads. <b>Persisted-but-inert in slice S4</b>: no
    /// resolver reads it yet, so no existing figure moves.
    ///
    /// <para>🔴 <b>R7 — THIS LEVEL HAS ZERO CORPUS SUPPORT (owed-review lens 3 finding 1).</b> It comes only from
    /// TallyHelp's published hierarchy strings (<c>[web]</c>, <c>docs/invented-vs-cloned.md</c> IV-1). The corpus's
    /// own list of five GST methods does <b>not</b> contain an accounting Group, and the corpus's accounting-Group
    /// creation screen carries <b>no GST field</b> — see <see cref="MasterGstDetails"/> for both extracts with their
    /// PDF and line numbers. Treat it as web-sourced and A14-unverified, not as clone fidelity.</para>
    /// </summary>
    public MasterGstDetails? Gst { get; set; }

    /// <summary>A primary group has no parent.</summary>
    public bool IsPrimary => ParentId is null;

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Census 2.2 — the five behavioural flags and the allocation method the vendor's Group master carries
    // (schema v60). R7 SOURCE: help.tallysolutions.com/groups-in-tallyprime/ ("How to Create, Alter and Delete
    // Groups in TallyPrime"), whose Group Creation / Alteration screen names every caption transcribed below
    // VERBATIM. Nothing here is invented; where the vendor is silent about how a flag changes a report, this
    // build says so on the property rather than guessing a behaviour (see each remark).
    //
    // 🔴 READ THIS BEFORE WIRING ANY OF THEM INTO A FIGURE. Only ONE of these is read by a report in this build
    // — <see cref="AffectsGrossProfits"/>, and only for a CUSTOM primary group, which is a group no book could
    // even contain before v60 (see GroupService.CreateGroup). The other three are captured master data with no
    // reader, stated plainly here rather than left for a grep to discover. That is deliberate: each of them
    // changes how a report GROUPS or NETS figures, the vendor pages reached do not specify the resulting
    // arithmetic, and this project has had to strip out tax and reporting behaviour it could not source.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vendor caption: <i>"Group behaves like a sub-ledger"</i> — "to treat group as ledger under parent".
    /// <b>Captured, not read.</b> No report in this build collapses a group's ledgers into a single line on the
    /// strength of it; the vendor page reached names the switch and its intent but not the resulting report
    /// arithmetic, so nothing is inferred. Defaults <c>false</c>, which is what every pre-v60 group was.
    /// </summary>
    public bool BehavesLikeSubLedger { get; set; }

    /// <summary>
    /// Vendor caption: <i>"Nett Debit/Credit Balances for Reporting"</i> — "to display the net balance, either
    /// debit or credit, whichever is higher". <b>Captured, not read</b> — same reason as
    /// <see cref="BehavesLikeSubLedger"/>: netting a group's Dr and Cr sides changes printed figures, and the
    /// exact rule (which totals net, and at which level) is not stated on the page reached. Defaults
    /// <c>false</c>.
    /// </summary>
    public bool NettBalancesForReporting { get; set; }

    /// <summary>
    /// Vendor caption: <i>"Used for calculation (for example: taxes, discounts)"</i> — "to use percentage-based
    /// calculations for ledgers under this Group". <b>Captured, not read.</b> Percentage-based automatic
    /// calculation on an invoice is the general <b>Voucher Class</b> machinery (census 2.6), which this build
    /// does not have; a flag that switched on a calculation with nothing to perform it would be worse than an
    /// honest gap. Defaults <c>false</c>.
    /// </summary>
    public bool UsedForCalculation { get; set; }

    /// <summary>
    /// Vendor caption: <i>"Does it affect Gross Profits"</i> — set Yes to treat the head as a <b>Direct</b>
    /// expense/income (it then lands above the Gross Profit line), No for an <b>Indirect</b> one.
    ///
    /// <para>🔴 <b>THE ONE FLAG HERE THAT MOVES A FIGURE, AND ITS REACH IS DELIBERATELY BOUNDED.</b>
    /// <c>ProfitAndLoss.ComputeGrossProfit</c> reads it <b>only</b> for a group whose primary ancestor is a
    /// CUSTOM primary group — i.e. one the operator created under <i>Primary</i>, which was impossible in this
    /// product before v60 (defect T1-31). The four seeded trading heads — Sales Accounts, Direct Incomes,
    /// Purchase Accounts, Direct Expenses — keep being matched by NAME exactly as before, so <b>no figure on
    /// any existing book moves</b> and the Robert/Bright regression fixtures are untouched. Back-filling the
    /// seeded heads to <c>true</c> would have been a silent data change to shipped books for no gain.</para>
    ///
    /// <para>The vendor shows this field <b>only under a Primary group</b>; it is meaningless on a child, whose
    /// placement is decided by its ancestor. <see cref="Services.GroupService"/> refuses it on a child rather
    /// than storing a value nothing will ever read.</para>
    /// </summary>
    public bool AffectsGrossProfits { get; set; }

    /// <summary>
    /// Vendor caption: <i>"Method to allocate when used in purchase invoice"</i>, options <i>Not Applicable</i>
    /// / <i>Appropriate by Qty</i> / <i>Appropriate by Value</i>. <c>null</c> = <b>Not Applicable</b>, which is
    /// what every pre-v60 group is.
    ///
    /// <para>🔴 <b>MEASURED DIVERGENCE, REPORTED RATHER THAN QUIETLY "FIXED".</b> This product already carries
    /// <see cref="MethodOfAppropriation"/> — the same three-valued choice, with the same two named options — on
    /// the <b>Ledger</b> master (<c>Ledger.MethodOfAppropriation</c>), which is where the additional-cost
    /// apportionment actually reads it. The vendor puts the field on the <b>Group</b>. Both now exist; the
    /// ledger one is still the only one an apportionment reads, so this is <b>captured, not read</b>, and the
    /// same enum is reused so the two can never drift into different option sets.</para>
    /// </summary>
    public MethodOfAppropriation? PurchaseAllocationMethod { get; set; }

    public Group(
        Guid id,
        string name,
        GroupNature nature,
        Guid? parentId = null,
        string? alias = null,
        bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));

        Id = id;
        Name = name;
        Nature = nature;
        ParentId = parentId;
        Alias = alias;
        IsPredefined = isPredefined;
    }
}
