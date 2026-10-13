using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// W2-20 / WBB-1 — which master family a multi-master grid is entering or altering.
///
/// <para><b>R7 — ATTESTED, AND THE CENSUS'S OWN TARGET NUMBER IS NOT.</b>
/// <c>help.tallysolutions.com/tally-prime/charts-of-accounts-tally/</c> (opened by content 2026-10-13) documents
/// the Multi Masters feature for <i>Ledgers</i> in full and then says, verbatim: "<i>Similarly, you can create
/// other masters such as stock items, groups, employees, <b>and so on</b></i>". <b>The page does not enumerate a
/// closed set, and it certainly does not enumerate SIX</b> — the "two of the vendor's six families" in census row
/// 2.12 is an internal count with no citation behind it, and it is corrected rather than built to. Families
/// attested family-by-family on their OWN vendor pages, each opened by content:</para>
/// <list type="bullet">
///   <item><b>Ledgers</b> and <b>Groups</b> — the Chart of Accounts page above.</item>
///   <item><b>Stock Items</b> — <c>help.tallysolutions.com/stock-items-faq/</c>: "<i>Alt+H (Multi-Masters) to open
///     the <b>Multi Stock Item Creation</b> or <b>Multi Stock Item Alteration</b> screens</i>".</item>
///   <item><b>Godowns</b> — <c>help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/</c>: "<i>In
///     the Chart of Accounts screen, press <b>Alt+H</b> (Multi-Masters) &gt; and select <b>Multi Create</b></i>",
///     and "<i>If you select <b>Under Godown &gt; All Items</b>, you will be prompted to select the parent godown
///     for each godown name that you specify under the <b>Name</b> column</i>".</item>
///   <item><b>Cost Centres</b> (and Cost Categories) — <c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c>:
///     "<i>You can create and alter multiple Cost or Profit Centres from one screen. Use the <b>multi-master
///     creation and alteration feature</b> … from the Chart of Accounts</i>".</item>
/// </list>
/// <para><b>Employees</b> and <b>Employee Groups</b> are attested too and are deliberately NOT built: payroll
/// (census Area 7) is out of scope by user rulings 29+30.</para>
/// </summary>
public enum MultiMasterKind
{
    /// <summary>Multi Ledger Creation — Name of Ledger · Under · Opening Balance (+ Dr/Cr).</summary>
    Ledger,

    /// <summary>Multi Group Creation — Name of Group · Under. Groups carry no opening balance.</summary>
    AccountGroup,

    /// <summary>Multi Stock Item Creation — Name of Item · Under · Units · Opening Qty · Rate · Amount.</summary>
    StockItem,

    /// <summary>Multi Godown Creation — Name · Under. The vendor's header here is "Under Godown".</summary>
    Godown,

    /// <summary>Multi Cost Centre Creation — Name · Category. The header is "Under Category".</summary>
    CostCentre,
}

/// <summary>
/// W2-20 / WBB-1 — which VERB a multi-master grid is running.
///
/// <para>🔴 <b>THIS ENUM IS THE WHOLE DEFENCE AGAINST THIS PROJECT'S MOST-REPEATED DEFECT.</b> Four Area-3 master
/// screens (T2-105) shipped an ALTERATION screen whose only button ran a bare unconditional <c>Create()</c>, so a
/// correction was either duplicated or silently lost. The remedy applied here is structural rather than careful:
/// <b>this screen has exactly ONE public commit method</b> — <see cref="MultiMasterCreateViewModel.Accept"/> —
/// which branches on this enum internally. There is no <c>Create()</c> for a pointer handler to call by reflex,
/// so the keyboard arm and the pointer arm <b>cannot</b> diverge; they are the same method. The button's caption
/// is likewise BOUND to <see cref="MultiMasterCreateViewModel.CommitLabel"/> rather than hard-coded, so the label
/// and the verb cannot drift apart either — which is the half of T2-105 that a verb-by-verb audit missed.</para>
///
/// <para><b>Ruling 25:</b> there is no <i>Display</i> verb. TallyPrime documents Create and Alter only.</para>
/// </summary>
public enum MultiMasterMode
{
    /// <summary>Multi Create — type new masters into a grid that grows as it is filled.</summary>
    Create,

    /// <summary>Multi Alter — the existing masters are LOADED into the grid and corrected in place.</summary>
    Alter,
}

/// <summary>
/// A parent/owner a grid row can sit under, flattened to <c>(Id, Name)</c> so ONE picker type serves all five
/// families. Without it the row template would need a different <c>x:DataType</c> per family (Group, StockGroup,
/// Godown, CostCategory), which compiled bindings cannot express on one shared template.
/// </summary>
public sealed record MultiMasterParent(Guid Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// One option in the header <b>Under …</b> picker. The FIRST option is the vendor's <b>"All Items"</b> sentinel,
/// whose <see cref="Parent"/> is <c>null</c>.
///
/// <para>On <see cref="MultiMasterMode.Create"/> "All Items" means each grid row picks its own parent — which is
/// how a mixed list (debtors + creditors + expense heads) is entered in one pass. On
/// <see cref="MultiMasterMode.Alter"/> the header is a <b>filter</b> over the masters loaded into the grid, and
/// "All Items" loads them all.</para>
/// </summary>
public sealed class MultiMasterUnderOption
{
    public MultiMasterUnderOption(string display, MultiMasterParent? parent)
    {
        Display = display;
        Parent = parent;
    }

    /// <summary>The label shown in the picker ("All Items", or the parent's own name).</summary>
    public string Display { get; }

    /// <summary>The parent this option names, or <c>null</c> for the "All Items" sentinel.</summary>
    public MultiMasterParent? Parent { get; }

    public override string ToString() => Display;
}

/// <summary>One option in a stock-item row's <b>Units</b> picker — the item's base unit.</summary>
public sealed record MultiMasterUnitOption(Guid Id, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// One row of a multi-master grid — a single master being typed (Create) or corrected (Alter). Deliberately holds
/// only the operator's raw text: parsing and validation happen once, for the WHOLE batch, in
/// <see cref="MultiMasterCreateViewModel.Accept"/>, so no row can be half-committed by a per-row write.
/// </summary>
public sealed partial class MultiMasterRowViewModel : ViewModelBase
{
    /// <summary>
    /// The owner's parent list, handed to each row so the per-row picker has an ItemsSource without the item
    /// template having to reach up the visual tree for its parent's DataContext — a lookup that compiled bindings
    /// cannot type-check and that silently yields an empty picker when it misses.
    /// </summary>
    public ObservableCollection<MultiMasterParent> UnderOptions { get; init; } = new();

    /// <summary>The owner's base-unit list, for the stock-item grid's Units column. Empty for every other family.</summary>
    public ObservableCollection<MultiMasterUnitOption> UnitOptions { get; init; } = new();

    /// <summary>True only on the ledger grid — mirrors the owner's <c>ShowsOpeningBalance</c> for the template.</summary>
    public bool ShowsOpeningBalance { get; init; }

    /// <summary>True only on the stock-item CREATE grid — mirrors the owner's <c>ShowsUnits</c>.</summary>
    public bool ShowsUnits { get; init; }

    /// <summary>True only on the stock-item CREATE grid — mirrors the owner's <c>ShowsOpeningQuantity</c>.</summary>
    public bool ShowsOpeningQuantity { get; init; }

    /// <summary>
    /// 🔴 The <b>stable Guid of the master this row is ALTERING</b>, or <see cref="Guid.Empty"/> on a create row.
    /// This field is what makes the alteration an alteration: every write in
    /// <see cref="MultiMasterCreateViewModel.Accept"/>'s alter arm resolves its target through this id, so a
    /// corrected row updates the ORIGINAL master and a rename applies retroactively to every voucher that
    /// references it. A row whose id is empty can only ever be created.
    /// </summary>
    public Guid ExistingId { get; init; }

    /// <summary>True when this row stands for a master that already exists, i.e. this is an alteration row.</summary>
    public bool IsExisting => ExistingId != Guid.Empty;

    /// <summary>The master's name as it was LOADED, so the alter arm can tell a changed row from an untouched one.</summary>
    public string OriginalName { get; init; } = string.Empty;

    /// <summary>The master's parent as it was LOADED (by id), for the same reason as <see cref="OriginalName"/>.</summary>
    public Guid? OriginalUnderId { get; init; }

    /// <summary>The opening balance as it was LOADED, rendered exactly as the box shows it.</summary>
    public string OriginalOpeningBalanceText { get; init; } = string.Empty;

    /// <summary>The opening side as it was LOADED.</summary>
    public bool OriginalOpeningIsCredit { get; init; }

    /// <summary>
    /// True while this row's own Under is the one that counts. On Create that is while the header names "All
    /// Items"; on Alter it is always true, because the header there is a filter and the row's parent is one of the
    /// things being corrected.
    /// </summary>
    [ObservableProperty] private bool _underIsEditable = true;

    /// <summary>The 1-based row number shown in the leftmost gutter and quoted in every refusal message.</summary>
    [ObservableProperty] private int _number;

    /// <summary>"Name of Ledger" / "Name of Group" / "Name of Item" / "Name" — the vendor's own column heading.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>This row's own parent. Used per row under "All Items", and always on an alteration row.</summary>
    [ObservableProperty] private MultiMasterParent? _under;

    /// <summary>"Opening Balance" as typed. Blank ⇒ nil, which is the norm. Ledger grid only.</summary>
    [ObservableProperty] private string _openingBalanceText = string.Empty;

    /// <summary>
    /// The Dr/Cr side of this row's opening balance. <b>Ours, labelled (RULING 9)</b> — the vendor help page
    /// names an "Opening Balance" column and does not speak to the side, but
    /// <see cref="DomainLedger.OpeningBalance"/> is an unsigned magnitude paired with
    /// <c>OpeningIsDebit</c>, so a grid that captured only a number could not express a credit opening at all.
    /// Defaulted from the chosen group's nature exactly as the single-ledger master defaults it.
    /// </summary>
    [ObservableProperty] private bool _openingIsCredit;

    /// <summary>The stock item's base unit. Required on the stock-item CREATE grid; never altered (see below).</summary>
    [ObservableProperty] private MultiMasterUnitOption? _unit;

    /// <summary>"Opening Qty" as typed. Stock-item CREATE grid only.</summary>
    [ObservableProperty] private string _openingQuantityText = string.Empty;

    /// <summary>"Rate" as typed (₹ per unit). Stock-item CREATE grid only.</summary>
    [ObservableProperty] private string _openingRateText = string.Empty;

    /// <summary>
    /// "Amount" — <b>computed</b>, Qty × Rate, and deliberately READ-ONLY.
    ///
    /// <para><b>Ours, labelled (RULING 9).</b> No vendor page this project could open by content lists the
    /// stock-item grid's columns, so the column SET here is ours. TallyPrime's own grids let an operator key the
    /// Amount and back-solve the Rate; we do not, because two editable boxes over one derived third are three
    /// ways to disagree about an opening valuation, and an opening valuation is Balance-Sheet money. Blank
    /// whenever either input is blank or unparseable, so it never shows a figure the batch would refuse.</para>
    /// </summary>
    public string AmountText
    {
        get
        {
            var qty = (OpeningQuantityText ?? string.Empty).Trim();
            var rate = (OpeningRateText ?? string.Empty).Trim();
            if (qty.Length == 0 || rate.Length == 0) return string.Empty;
            if (!decimal.TryParse(qty, NumberStyles.Number, CultureInfo.InvariantCulture, out var q)
                || !decimal.TryParse(rate, NumberStyles.Number, CultureInfo.InvariantCulture, out var r))
                return string.Empty;
            return (q * r).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    partial void OnOpeningQuantityTextChanged(string value) => OnPropertyChanged(nameof(AmountText));
    partial void OnOpeningRateTextChanged(string value) => OnPropertyChanged(nameof(AmountText));

    /// <summary>
    /// True when the operator has typed nothing at all into this row — a trailing blank is ignored. An ALTERATION
    /// row is never blank: it stands for a master that exists, and clearing its name must be a REFUSAL (a master
    /// needs a name) rather than a silently skipped row.
    /// </summary>
    public bool IsBlank =>
        !IsExisting
        && string.IsNullOrWhiteSpace(Name)
        && string.IsNullOrWhiteSpace(OpeningBalanceText)
        && string.IsNullOrWhiteSpace(OpeningQuantityText)
        && string.IsNullOrWhiteSpace(OpeningRateText)
        && Under is null
        && Unit is null;

    /// <summary>
    /// True when an ALTERATION row differs from what was loaded. The alter arm writes only these rows, so
    /// accepting a grid of 300 untouched ledgers rewrites none of them — and an untouched predefined master
    /// cannot be refused by a guard it was never going to trip.
    /// </summary>
    public bool IsChanged =>
        IsExisting
        && (!string.Equals((Name ?? string.Empty).Trim(), OriginalName, StringComparison.Ordinal)
            || Under?.Id != OriginalUnderId
            || !string.Equals((OpeningBalanceText ?? string.Empty).Trim(),
                              OriginalOpeningBalanceText, StringComparison.Ordinal)
            || OpeningIsCredit != OriginalOpeningIsCredit);
}

/// <summary>
/// W2-20 / WBB-1 (census 2.12, and the multi-stock-item clause of census 3.3) — <b>Multi Masters</b>: create or
/// alter MANY masters from one grid instead of one screen per master.
///
/// <para><b>Grounding.</b> Every family and both verbs are cited on <see cref="MultiMasterKind"/>, which also
/// records that the census's "two of the vendor's SIX families" is an uncited internal count: the vendor page
/// refuses to close the list ("<i>and so on</i>"). What the Chart of Accounts page states in full, and what this
/// screen implements, is <b>Alt+H (Multi-Masters) &gt; Multi Create | Multi Alter</b>, an <b>"Under Group"</b>
/// header field <b>defaulting to "All Items"</b>, a grid of <b>"Name of Ledger" · "Under" · "Opening
/// Balance"</b>, <b>Ctrl+A</b> to save, and — on the <b>"Multi Ledger Alteration"</b> screen — <b>Alt+B (Zero
/// Opening Balance)</b> to "<i>remove the opening balances from all the ledgers</i>".</para>
///
/// <para><b>Divergences, LABELLED AS OURS (RULING 9)</b> — the source is silent on each:
/// <list type="bullet">
///   <item>the per-row <b>Dr/Cr side</b> (see <see cref="MultiMasterRowViewModel.OpeningIsCredit"/>);</item>
///   <item><b>all-or-nothing Accept.</b> Every row is validated BEFORE anything is written, and one bad row
///     refuses the whole batch naming the offending row. A partial write leaves the operator guessing which
///     of twenty names landed; worse, the engine's own uniqueness guard cannot see a name entered twice in
///     the SAME batch (neither exists yet), so without a batch-level pass row 12 would silently create a
///     duplicate of row 3. The operator's typing survives a refusal so the bad row can simply be corrected.
///     </item>
///   <item>the <b>column set of every grid but the ledger one</b>, and the read-only computed <b>Amount</b>
///     (see <see cref="MultiMasterRowViewModel.AmountText"/>). The godown grid's <b>"Under Godown"</b> header and
///     its <b>"Name"</b> column ARE the vendor's, quoted on <see cref="MultiMasterKind"/>; the rest are ours.</item>
///   <item>on <b>Alter</b>, the header behaves as a <b>filter</b> over which masters load, and changing it
///     RELOADS the grid (discarding uncommitted edits). The page does not say what the header does on the
///     alteration screen.</item>
///   <item><b>what Alter may change: the name and the parent, plus the opening balance on the ledger grid.</b>
///     Three exclusions are deliberate and each prevents wrong money or a wrong quantity rather than saving
///     effort: a stock item's <b>base unit</b> is never re-assigned (it would silently reinterpret every
///     quantity already recorded against the item — the same exclusion the single-item master makes); a stock
///     item's <b>opening quantity</b> is never re-applied (it would ADD a second opening allocation on every
///     accept and multiply the opening stock); and a <b>primary</b> group is not loaded into the group grid at
///     all (it has no parent to show, and its Nature is not alterable here). The vendor's own FAQ records the
///     same shape of limit on its side — "<i>Currently, TallyPrime does not support viewing and editing all the
///     details related to stock items in the Multi Stock Item Alteration screen</i>".</item>
/// </list></para>
///
/// <para>🔴 <b>THE OPTIONAL-PARAMETER TRAP THIS SCREEN WAS WARNED ABOUT, AND WHAT IT DOES ABOUT IT.</b> Ticket
/// T2-77 predicted exactly this caller: <c>CostMasterService.AlterCostCentre</c> declared
/// <c>string? alias = null</c> and wrote <c>centre.Alias</c> unconditionally, so a Multi Alter that re-parented a
/// batch would null every alias in it. The parameter is now <b>required</b> on that method. Two further instances
/// of the same class were found while writing this screen and are handled here by passing the master's EXISTING
/// values through explicitly, never by relying on a default: <c>InventoryService.AlterGodown</c> would clear
/// <c>Alias</c>, <c>ThirdParty</c> and <c>JobCostCentreId</c>, and <c>GroupService.AlterGroup</c> would clear
/// <c>Alias</c> and — through <c>behaviour ?? GroupBehaviour.None</c> — <b>wipe all five of a group's
/// behavioural flags</b>. <b>A field this grid does not show is a field this grid must preserve.</b></para>
///
/// <para><b>Keyboard-first.</b> On Create the grid grows as it is typed — filling the last row appends a fresh
/// blank one — so there is no "add row" button to reach for. Ctrl+A accepts the whole batch through the shell's
/// ordinary master-accept path and Alt+B zeroes the opening balances. No affordance here requires a pointer.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable, exactly like every other master view model.</para>
///
/// <para>⚠️ <b>Naming debt, stated rather than hidden:</b> the type is still called
/// <c>MultiMasterCreateViewModel</c> because it began as a create-only screen. The name is now too narrow. It is
/// NOT dangerous — there is no <c>Create()</c> method to call by reflex, only <see cref="Accept"/> — but the
/// rename is owed.</para>
/// </summary>
public sealed partial class MultiMasterCreateViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>Which master family this grid enters — fixed for the life of the screen.</summary>
    public MultiMasterKind Kind { get; }

    /// <summary>Which verb this grid runs — fixed for the life of the screen.</summary>
    public MultiMasterMode Mode { get; }

    /// <summary>True on a Multi Alter screen. Drives the caption, the commit label and the grid's load.</summary>
    public bool IsAltering => Mode == MultiMasterMode.Alter;

    /// <summary>The screen title, and the cascade column's header — the vendor's own screen names.</summary>
    public string Title => Kind switch
    {
        MultiMasterKind.Ledger => IsAltering ? "Multi Ledger Alteration" : "Multi Ledger Creation",
        MultiMasterKind.AccountGroup => IsAltering ? "Multi Group Alteration" : "Multi Group Creation",
        MultiMasterKind.StockItem => IsAltering ? "Multi Stock Item Alteration" : "Multi Stock Item Creation",
        MultiMasterKind.Godown => IsAltering ? "Multi Godown Alteration" : "Multi Godown Creation",
        _ => IsAltering ? "Multi Cost Centre Alteration" : "Multi Cost Centre Creation",
    };

    /// <summary>
    /// 🔴 The commit button's caption, <b>bound</b> so the label and the verb cannot drift apart. The hard-coded
    /// <c>Content="Create (Ctrl+A)"</c> standing over an alteration screen is the half of T2-105 that survived a
    /// verb-by-verb audit on four other masters; it cannot happen here because there is no literal to forget.
    /// </summary>
    public string CommitLabel => IsAltering ? "Alter All (Ctrl+A)" : "Create All (Ctrl+A)";

    /// <summary>The vendor's own name column heading for this grid.</summary>
    public string NameColumnHeader => Kind switch
    {
        MultiMasterKind.Ledger => "Name of Ledger",
        MultiMasterKind.AccountGroup => "Name of Group",
        MultiMasterKind.StockItem => "Name of Item",
        MultiMasterKind.Godown => "Name",                 // the vendor's word on the godowns page
        _ => "Name of Cost Centre",
    };

    /// <summary>The parent column's heading. "Under" everywhere but the cost grid, whose parent IS the category.</summary>
    public string UnderColumnHeader => Kind == MultiMasterKind.CostCentre ? "Category" : "Under";

    /// <summary>The header field's label — the vendor's "Under Group" / "Under Godown", and ours for the cost grid.</summary>
    public string UnderHeaderLabel => Kind switch
    {
        MultiMasterKind.Godown => "Under Godown",
        MultiMasterKind.CostCentre => "Under Category",
        _ => "Under Group",
    };

    /// <summary>The italic hint beside the header picker — says what the header DOES, which differs by verb.</summary>
    public string UnderHeaderHint => IsAltering
        ? $"Pick a {ParentNoun} to load only its {PluralNoun}, or leave \"All Items\" to load them all."
        : $"Pick a {ParentNoun} to put every row under it, or leave \"All Items\" to choose per row.";

    /// <summary>True only for the ledger grid — a group, godown, item or cost centre has no opening balance.</summary>
    public bool ShowsOpeningBalance => Kind == MultiMasterKind.Ledger;

    /// <summary>
    /// True only on the stock-item CREATE grid. The base unit is never offered on an alteration: changing it would
    /// silently reinterpret every quantity already recorded against the item.
    /// </summary>
    public bool ShowsUnits => Kind == MultiMasterKind.StockItem && !IsAltering;

    /// <summary>
    /// True only on the stock-item CREATE grid. An alteration never re-applies an opening quantity — doing so
    /// would add a second opening allocation on every accept and multiply the item's opening stock.
    /// </summary>
    public bool ShowsOpeningQuantity => Kind == MultiMasterKind.StockItem && !IsAltering;

    /// <summary>
    /// True on the <b>Multi Ledger Alteration</b> screen only — the surface the vendor documents <b>Alt+B (Zero
    /// Opening Balance)</b> on.
    /// </summary>
    public bool ShowsZeroOpeningBalance => Kind == MultiMasterKind.Ledger && IsAltering;

    /// <summary>The header "Under …" options: the "All Items" sentinel first, then every candidate parent.</summary>
    public ObservableCollection<MultiMasterUnderOption> UnderGroupOptions { get; } = new();

    /// <summary>The per-row parent picker's options — name-sorted.</summary>
    public ObservableCollection<MultiMasterParent> UnderOptions { get; } = new();

    /// <summary>The stock-item grid's base-unit options. Empty for every other family.</summary>
    public ObservableCollection<MultiMasterUnitOption> UnitOptions { get; } = new();

    /// <summary>The grid rows. On Create this always ends in exactly one blank row; on Alter it never does.</summary>
    public ObservableCollection<MultiMasterRowViewModel> Rows { get; } = new();

    [ObservableProperty] private MultiMasterUnderOption? _selectedUnderGroup;
    [ObservableProperty] private string? _message;

    /// <summary>
    /// True when <see cref="Message"/> is a REFUSAL, false when it is a CONFIRMATION.
    ///
    /// <para>🔴 This screen shipped its first draft with one text block bound to <see cref="Message"/> in the
    /// alert colour, so "3 ledgers created under Sundry Debtors." printed in RED — a successful batch reported
    /// as a failure. That is the same defect <c>CompanyProfileViewModel</c> already carries a note about, so
    /// this screen uses that screen's remedy: the SEVERITY is decided here, where it is headlessly testable,
    /// and the view binds one text block per severity rather than converting a brush.</para>
    /// </summary>
    [ObservableProperty] private bool _messageIsError;

    /// <summary>The message line when it is a refusal, else <c>null</c>.</summary>
    public string? ErrorMessage => MessageIsError ? Message : null;

    /// <summary>The message line when it is a confirmation, else <c>null</c>.</summary>
    public string? ConfirmationMessage => MessageIsError ? null : Message;

    partial void OnMessageChanged(string? value) => RaiseMessageParts();
    partial void OnMessageIsErrorChanged(bool value) => RaiseMessageParts();

    private void RaiseMessageParts()
    {
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ConfirmationMessage));
    }

    /// <summary>Refuses the batch with <paramref name="text"/>, and always returns <c>false</c> so every
    /// refusal in <see cref="Accept"/> is a single <c>return Refuse(…)</c> that cannot forget the severity.</summary>
    private bool Refuse(string text)
    {
        MessageIsError = true;
        Message = text;
        return false;
    }

    /// <summary>True while the header names the "All Items" sentinel, i.e. each row carries its own parent.</summary>
    public bool IsAllItems => SelectedUnderGroup?.Parent is null;

    /// <summary>The singular noun for this family's parent, used in hints and refusals.</summary>
    private string ParentNoun => Kind switch
    {
        MultiMasterKind.Godown => "parent godown",
        MultiMasterKind.CostCentre => "cost category",
        MultiMasterKind.StockItem => "stock group",
        _ => "group",
    };

    /// <summary>The plural noun for this family's masters, used in hints and confirmations.</summary>
    private string PluralNoun => Kind switch
    {
        MultiMasterKind.Ledger => "ledgers",
        MultiMasterKind.AccountGroup => "groups",
        MultiMasterKind.StockItem => "stock items",
        MultiMasterKind.Godown => "godowns",
        _ => "cost centres",
    };

    /// <summary>The singular noun for this family's masters.</summary>
    private string SingularNoun => Kind switch
    {
        MultiMasterKind.Ledger => "ledger",
        MultiMasterKind.AccountGroup => "group",
        MultiMasterKind.StockItem => "stock item",
        MultiMasterKind.Godown => "godown",
        _ => "cost centre",
    };

    public MultiMasterCreateViewModel(
        MultiMasterKind kind, Company company, CompanyStorage storage, Action onChanged)
        : this(kind, MultiMasterMode.Create, company, storage, onChanged)
    {
    }

    public MultiMasterCreateViewModel(
        MultiMasterKind kind, MultiMasterMode mode, Company company, CompanyStorage storage, Action onChanged)
    {
        Kind = kind;
        Mode = mode;
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshUnitOptions();
        SelectedUnderGroup = UnderGroupOptions.FirstOrDefault();   // "All Items" — the vendor's default
        // Setting SelectedUnderGroup above already loaded the grid on the alter path (OnSelectedUnderGroupChanged
        // reloads), but only if it actually CHANGED — on a company with no groups it stays null. Load explicitly
        // so an empty-picker company still gets a grid rather than a silently blank screen.
        if (IsAltering) LoadExistingRows();
        else AppendBlankRow();
    }

    partial void OnSelectedUnderGroupChanged(MultiMasterUnderOption? value)
    {
        OnPropertyChanged(nameof(IsAllItems));

        if (IsAltering)
        {
            // The header is a FILTER on this verb: re-load the grid from the company. Uncommitted edits are
            // discarded, which is why the hint says "load" rather than "put under".
            LoadExistingRows();
            return;
        }

        foreach (var row in Rows)
        {
            // The per-row parent only counts under "All Items"; otherwise the header wins and the cell greys.
            row.UnderIsEditable = value?.Parent is null;
            // Re-default every untouched row's side to the new header group's nature, so the operator sees the
            // conventional Dr/Cr before typing rather than after saving.
            if (string.IsNullOrWhiteSpace(row.OpeningBalanceText))
                row.OpeningIsCredit = DefaultsToCredit(value?.Parent);
        }
    }

    /// <summary>Rebuilds both parent pickers from the company (the "All Items" sentinel stays first).</summary>
    private void RefreshParentOptions()
    {
        var previousId = SelectedUnderGroup?.Parent?.Id;

        UnderOptions.Clear();
        UnderGroupOptions.Clear();
        UnderGroupOptions.Add(new MultiMasterUnderOption("All Items", null));
        foreach (var p in CandidateParents().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            UnderOptions.Add(p);
            UnderGroupOptions.Add(new MultiMasterUnderOption(p.Name, p));
        }

        if (previousId is { } id)
            SelectedUnderGroup = UnderGroupOptions.FirstOrDefault(o => o.Parent?.Id == id)
                                 ?? UnderGroupOptions.FirstOrDefault();
    }

    /// <summary>The parents a row of this family may sit under, flattened to <see cref="MultiMasterParent"/>.</summary>
    private IEnumerable<MultiMasterParent> CandidateParents() => Kind switch
    {
        MultiMasterKind.Ledger or MultiMasterKind.AccountGroup =>
            _company.Groups.Select(g => new MultiMasterParent(g.Id, g.Name)),
        MultiMasterKind.StockItem =>
            _company.StockGroups.Select(g => new MultiMasterParent(g.Id, g.Name)),
        MultiMasterKind.Godown =>
            _company.Godowns.Select(g => new MultiMasterParent(g.Id, g.Name)),
        _ => _company.CostCategories.Select(c => new MultiMasterParent(c.Id, c.Name)),
    };

    /// <summary>Rebuilds the stock-item grid's base-unit options. A no-op for every other family.</summary>
    private void RefreshUnitOptions()
    {
        UnitOptions.Clear();
        if (Kind != MultiMasterKind.StockItem) return;
        foreach (var u in _company.Units.OrderBy(u => u.Symbol, StringComparer.OrdinalIgnoreCase))
            UnitOptions.Add(new MultiMasterUnitOption(u.Id, u.Symbol));
    }

    /// <summary>
    /// The conventional opening side for a group: Asset/Expense open Debit, Liability/Income open Credit —
    /// the same rule the single-ledger master applies. Only the accounting families have a nature to read.
    /// </summary>
    private bool DefaultsToCredit(MultiMasterParent? parent)
    {
        if (parent is null) return false;
        if (Kind is not (MultiMasterKind.Ledger or MultiMasterKind.AccountGroup)) return false;
        return _company.FindGroup(parent.Id) is { } g
               && g.Nature is not (GroupNature.Asset or GroupNature.Expense);
    }

    private void AppendBlankRow()
    {
        var row = new MultiMasterRowViewModel
        {
            UnderOptions = UnderOptions,
            UnitOptions = UnitOptions,
            ShowsOpeningBalance = ShowsOpeningBalance,
            ShowsUnits = ShowsUnits,
            ShowsOpeningQuantity = ShowsOpeningQuantity,
            Number = Rows.Count + 1,
            UnderIsEditable = IsAllItems,
            OpeningIsCredit = DefaultsToCredit(SelectedUnderGroup?.Parent),
        };
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
    }

    /// <summary>
    /// 🔴 <b>Multi Alter's load</b> — fills the grid from the masters that already exist, each row carrying the
    /// master's stable <see cref="MultiMasterRowViewModel.ExistingId"/> and the values it was loaded with. A row
    /// with no id could only ever be created, so this method is what makes the alter arm an alteration.
    ///
    /// <para>No trailing blank row is appended: Multi Alter corrects what exists and never creates.</para>
    /// </summary>
    private void LoadExistingRows()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        MessageIsError = false;
        Message = null;

        var filter = SelectedUnderGroup?.Parent;
        var n = 0;
        foreach (var seed in EnumerateExisting(filter))
        {
            var row = new MultiMasterRowViewModel
            {
                UnderOptions = UnderOptions,
                UnitOptions = UnitOptions,
                ShowsOpeningBalance = ShowsOpeningBalance,
                ShowsUnits = ShowsUnits,
                ShowsOpeningQuantity = ShowsOpeningQuantity,
                Number = ++n,
                UnderIsEditable = true,   // the parent is one of the things an alteration changes
                ExistingId = seed.Id,
                OriginalName = seed.Name,
                OriginalUnderId = seed.UnderId,
                OriginalOpeningBalanceText = seed.OpeningText,
                OriginalOpeningIsCredit = seed.OpeningIsCredit,
                Name = seed.Name,
                Under = seed.UnderId is { } uid ? UnderOptions.FirstOrDefault(p => p.Id == uid) : null,
                OpeningBalanceText = seed.OpeningText,
                OpeningIsCredit = seed.OpeningIsCredit,
            };
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        if (Rows.Count == 0)
            Message = filter is null
                ? $"This company has no {PluralNoun} to alter yet."
                : $"No {PluralNoun} sit under {filter.Name}.";
    }

    /// <summary>One existing master, flattened to exactly the fields a grid row shows.</summary>
    private sealed record ExistingSeed(
        Guid Id, string Name, Guid? UnderId, string OpeningText, bool OpeningIsCredit);

    /// <summary>
    /// The masters loaded into a Multi Alter grid, name-ordered, optionally restricted to one parent.
    ///
    /// <para><b>PRIMARY groups are deliberately excluded</b> from the group grid: a primary head has no parent to
    /// show in the Under column, and <c>GroupService.AlterGroup</c> requires a stated Nature of Group for one —
    /// which this grid has no column for and which, for a predefined head, it is not allowed to change at all.
    /// Loading them would put rows on screen that could only ever be refused.</para>
    /// </summary>
    private IEnumerable<ExistingSeed> EnumerateExisting(MultiMasterParent? filter)
    {
        switch (Kind)
        {
            case MultiMasterKind.Ledger:
                return _company.Ledgers
                    .Where(l => filter is null || l.GroupId == filter.Id)
                    .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(l => new ExistingSeed(
                        l.Id, l.Name, l.GroupId, FormatOpening(l.OpeningBalance), !l.OpeningIsDebit))
                    .ToList();

            case MultiMasterKind.AccountGroup:
                return _company.Groups
                    .Where(g => g.ParentId is not null)
                    .Where(g => filter is null || g.ParentId == filter.Id)
                    .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ExistingSeed(g.Id, g.Name, g.ParentId, string.Empty, false))
                    .ToList();

            case MultiMasterKind.StockItem:
                return _company.StockItems
                    .Where(i => filter is null || i.StockGroupId == filter.Id)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new ExistingSeed(i.Id, i.Name, i.StockGroupId, string.Empty, false))
                    .ToList();

            case MultiMasterKind.Godown:
                return _company.Godowns
                    .Where(g => filter is null || g.ParentId == filter.Id)
                    .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ExistingSeed(g.Id, g.Name, g.ParentId, string.Empty, false))
                    .ToList();

            default:
                return _company.CostCentres
                    .Where(c => filter is null || c.CategoryId == filter.Id)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(c => new ExistingSeed(c.Id, c.Name, c.CategoryId, string.Empty, false))
                    .ToList();
        }
    }

    /// <summary>
    /// Renders a stored opening balance the way the box shows it — blank for nil, so an untouched nil row reads as
    /// unchanged rather than as "0.00" typed over nothing.
    /// </summary>
    private static string FormatOpening(Money amount)
        => amount.Amount == 0m ? string.Empty : amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// 🔴 <b>Alt+B — Zero Opening Balance</b>, on the Multi Ledger Alteration screen.
    ///
    /// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/tally-prime/charts-of-accounts-tally/</c>: "<i>To
    /// remove the opening balances from all the ledgers, press <b>Alt+B</b> (Zero Opening Balance)</i>".</para>
    ///
    /// <para>It stages the change in the grid; nothing is written until Ctrl+A, exactly like every other edit on
    /// this screen. A no-op on any other grid.</para>
    /// </summary>
    /// <returns>True when the chord applies to this screen and the grid was changed.</returns>
    public bool ZeroOpeningBalances()
    {
        if (!ShowsZeroOpeningBalance) return false;

        var touched = 0;
        foreach (var row in Rows)
        {
            if (row.OpeningBalanceText.Length == 0 && !row.OpeningIsCredit) continue;
            row.OpeningBalanceText = string.Empty;
            row.OpeningIsCredit = false;
            touched++;
        }

        MessageIsError = false;
        Message = touched == 0
            ? "Every opening balance on this screen is already nil."
            : $"Opening balance cleared on {touched} {(touched == 1 ? "ledger" : "ledgers")} — "
              + "press Ctrl+A to apply.";
        return touched > 0;
    }

    /// <summary>
    /// Keeps exactly one trailing blank row on a CREATE grid, so it grows under the operator's fingers: filling
    /// the last row makes a new one appear beneath it and Down reaches it. There is deliberately no "add row"
    /// button — an affordance that only a mouse can drive would fail the keyboard-first contract.
    ///
    /// <para>On an ALTER grid nothing is appended: the row set is the masters that exist.</para>
    /// </summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MultiMasterRowViewModel.Number)) return;
        if (!IsAltering && Rows.Count > 0 && !Rows[^1].IsBlank) AppendBlankRow();

        // Picking a row's own parent re-defaults its untouched side from that group's nature.
        if (e.PropertyName == nameof(MultiMasterRowViewModel.Under)
            && sender is MultiMasterRowViewModel r
            && !r.IsExisting
            && string.IsNullOrWhiteSpace(r.OpeningBalanceText))
            r.OpeningIsCredit = DefaultsToCredit(r.Under);
    }

    private void ResetGrid()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        AppendBlankRow();
    }

    /// <summary>The parsed, validated shape of one create row — built only after the WHOLE batch has passed.</summary>
    private sealed record PendingRow(
        string Name, MultiMasterParent Under, Money Opening, bool IsDebit,
        Guid UnitId, decimal OpeningQuantity, Money OpeningRate);

    /// <summary>The parsed, validated shape of one alter row — built only after the WHOLE batch has passed.</summary>
    private sealed record PendingAlteration(
        Guid Id, string Name, MultiMasterParent Under, Money Opening, bool IsDebit);

    /// <summary>
    /// 🔴 <b>Ctrl+A — THE ONE AND ONLY COMMIT PATH, for both verbs.</b>
    ///
    /// <para>Both the keyboard arm (<c>MainWindowViewModel</c>'s master-accept case) and the pointer arm
    /// (<c>MainWindow.OnMultiMasterCreateClick</c>) call <b>this</b> method, which branches on <see cref="Mode"/>
    /// internally. That is deliberate and it is the structural fix for T2-105: when the two arms call two
    /// different methods, one of them eventually runs the wrong verb — on four Area-3 masters the pointer arm ran
    /// <c>Create()</c> over an alteration screen and either duplicated the master or silently lost the
    /// correction. Here there is no second method to get wrong.</para>
    ///
    /// <para>The ordering is the whole point of both arms. Validation runs against the company as it stands PLUS
    /// the names already claimed earlier in this same batch, so a name typed twice is caught even though neither
    /// copy exists yet. Nothing is written and <see cref="CompanyStorage.Save"/> is not called at all unless the
    /// batch is wholly valid — so a refused batch cannot leave a partial set of masters behind.</para>
    /// </summary>
    public bool Accept() => IsAltering ? AlterBatch() : CreateBatch();

    // ------------------------------------------------------------------------------- the CREATE arm

    private bool CreateBatch()
    {
        MessageIsError = false;
        Message = null;

        var filled = Rows.Where(r => !r.IsBlank).ToList();
        if (filled.Count == 0)
            return Refuse("Nothing to create — type at least one name.");

        var headerParent = SelectedUnderGroup?.Parent;
        var pending = new List<PendingRow>(filled.Count);
        // Names claimed earlier in THIS batch — the clash the engine's guard structurally cannot see.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in filled)
        {
            var label = $"Row {row.Number} ({(row.Name ?? string.Empty).Trim()})";

            // --- name: required, unique against the company AND against this batch.
            if (!TryValidateName(row.Name, Guid.Empty, label, out var name, out var nameRefusal))
                return Refuse(nameRefusal!);

            if (!claimed.Add(name))
                return Refuse($"{label}: '{name}' is entered twice in this batch.");

            // --- parent: the header when one is named, else this row's own pick.
            var under = headerParent ?? row.Under;
            if (under is null)
                return Refuse($"{label}: pick a{(ParentNoun.StartsWith('a') ? "n" : string.Empty)} {ParentNoun}.");

            // --- opening balance (ledger grid only): the single-ledger master's three refusals, by row.
            var opening = Money.Zero;
            var isDebit = !row.OpeningIsCredit;
            if (Kind == MultiMasterKind.Ledger)
            {
                if (!TryParseOpening(row.OpeningBalanceText, label, out opening, out var openingRefusal))
                    return Refuse(openingRefusal!);
            }

            // --- base unit + opening quantity (stock-item grid only).
            var unitId = Guid.Empty;
            var openingQty = 0m;
            var openingRate = Money.Zero;
            if (Kind == MultiMasterKind.StockItem)
            {
                if (row.Unit is null)
                    return Refuse($"{label}: pick a base unit (create a Unit first).");
                unitId = row.Unit.Id;

                if (!TryParseStockOpening(row, label, out openingQty, out openingRate, out var stockRefusal))
                    return Refuse(stockRefusal!);
            }

            pending.Add(new PendingRow(name, under, opening, isDebit, unitId, openingQty, openingRate));
        }

        // ------------------------------------------------------------- every row passed: write the batch.
        try
        {
            WriteCreateBatch(pending);
        }
        catch (InvalidOperationException ex)
        {
            // Defensive: every case above is pre-validated, so this arm should be unreachable. If the engine
            // ever refuses anyway, say so plainly rather than leaving a half-written batch silently persisted.
            return Refuse(ex.Message);
        }

        _storage.Save(_company);

        var noun = pending.Count == 1 ? SingularNoun : PluralNoun;
        MessageIsError = false;
        Message = headerParent is { } hp
            ? $"{pending.Count} {noun} created under {hp.Name}."
            : $"{pending.Count} {noun} created.";

        RefreshParentOptions();      // a group just created is immediately selectable as a parent
        RefreshUnitOptions();
        ResetGrid();
        _onChanged();
        return true;
    }

    private void WriteCreateBatch(List<PendingRow> pending)
    {
        switch (Kind)
        {
            case MultiMasterKind.Ledger:
                foreach (var p in pending)
                    _company.AddLedger(new DomainLedger(Guid.NewGuid(), p.Name, p.Under.Id, p.Opening, p.IsDebit));
                break;

            case MultiMasterKind.AccountGroup:
            {
                var groups = new GroupService(_company);
                foreach (var p in pending) groups.CreateGroup(p.Name, p.Under.Id);
                break;
            }

            case MultiMasterKind.StockItem:
            {
                var inventory = new InventoryService(_company);
                // The opening allocation needs a godown. Main Location is the one every company has, and it is
                // the same default the single-item master opens on.
                var godownId = DefaultGodownId();
                foreach (var p in pending)
                {
                    var item = inventory.CreateStockItem(p.Name, p.Under.Id, p.UnitId);
                    if (p.OpeningQuantity > 0m && godownId is { } gid)
                        inventory.AddOpeningBalance(item.Id, gid, p.OpeningQuantity, p.OpeningRate);
                }
                break;
            }

            case MultiMasterKind.Godown:
            {
                var inventory = new InventoryService(_company);
                foreach (var p in pending) inventory.CreateGodown(p.Name, p.Under.Id);
                break;
            }

            default:
                // 🔴 The grid captures no parent cost centre, so every centre it creates is top-level. That is a
                // labelled divergence, not an oversight: a per-row parent picker would have to be re-scoped to
                // each row's own category to keep the domain's "a parent must be in the SAME category" invariant,
                // and a picker that silently offers an invalid parent is worse than no picker. Re-parent from the
                // single Cost Centre master.
                //
                // There is no CreateCostCentre on CostMasterService (the service owns Alter and Delete only), so
                // this mirrors CostCentreMasterViewModel.Create exactly — including the normalised null alias,
                // which matters because the alias is a LOOKUP KEY and an empty string would make
                // FindCostCentreByName match a blank search against every centre created without one.
                foreach (var p in pending)
                    _company.AddCostCentre(
                        new CostCentre(Guid.NewGuid(), p.Name, p.Under.Id, parentId: null, alias: null));
                break;
        }
    }

    /// <summary>Main Location, the godown every company is seeded with, else the first godown, else none.</summary>
    private Guid? DefaultGodownId()
        => (_company.Godowns.FirstOrDefault(g => g.IsMainLocation) ?? _company.Godowns.FirstOrDefault())?.Id;

    // -------------------------------------------------------------------------------- the ALTER arm

    /// <summary>
    /// 🔴 <b>Ctrl+A on a Multi Alter screen — the ALTER verb.</b>
    ///
    /// <para>Every write resolves its target through the row's <see cref="MultiMasterRowViewModel.ExistingId"/>,
    /// so a corrected row <b>updates the ORIGINAL master</b> and a rename applies retroactively to every voucher
    /// that references it by id. Nothing is appended, nothing is duplicated, and no row without an id can be
    /// reached from here at all.</para>
    ///
    /// <para>Only rows that actually CHANGED are written (<see cref="MultiMasterRowViewModel.IsChanged"/>), for
    /// two reasons: accepting a grid of 300 untouched ledgers must not rewrite 300 masters, and an untouched
    /// predefined master must not be refused by a guard it was never going to trip.</para>
    ///
    /// <para>🔴 <b>And every service call below passes the master's EXISTING value for every field this grid does
    /// not show.</b> That is the T2-77 class, which predicted this exact caller: three alter methods in the engine
    /// take optional parameters and write them unconditionally, so <c>AlterGodown(id, name, parent)</c> would
    /// clear the godown's alias, its third-party flag and its job cost centre, and
    /// <c>AlterGroup(id, name, parent)</c> would clear the alias and wipe all five behavioural flags. A field this
    /// grid does not show is a field this grid must preserve.</para>
    /// </summary>
    private bool AlterBatch()
    {
        MessageIsError = false;
        Message = null;

        if (Rows.Count == 0)
            return Refuse($"There are no {PluralNoun} on this screen to alter.");

        // Every row is validated — not only the changed ones — because clearing an untouched row's name is a
        // refusal, and the batch-level duplicate check must see the names that are staying put too.
        var pending = new List<PendingAlteration>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
        {
            var label = $"Row {row.Number} ({(row.Name ?? string.Empty).Trim()})";

            if (!TryValidateName(row.Name, row.ExistingId, label, out var name, out var nameRefusal))
                return Refuse(nameRefusal!);

            if (!claimed.Add(name))
                return Refuse($"{label}: '{name}' is entered twice in this batch.");

            var under = row.Under;
            if (under is null)
                return Refuse($"{label}: pick a{(ParentNoun.StartsWith('a') ? "n" : string.Empty)} {ParentNoun}.");

            var opening = Money.Zero;
            var isDebit = !row.OpeningIsCredit;
            if (Kind == MultiMasterKind.Ledger
                && !TryParseOpening(row.OpeningBalanceText, label, out opening, out var openingRefusal))
                return Refuse(openingRefusal!);

            if (row.IsChanged)
                pending.Add(new PendingAlteration(row.ExistingId, name, under, opening, isDebit));
        }

        if (pending.Count == 0)
        {
            MessageIsError = false;
            Message = "Nothing changed — edit a row, then press Ctrl+A.";
            return false;
        }

        try
        {
            WriteAlterBatch(pending);
        }
        catch (InvalidOperationException ex)
        {
            // 🔴 A refusal HERE has already mutated some of the batch in memory, so the company object must not be
            // trusted afterwards — and it is NOT saved. The in-memory aggregate is reloaded from disk by the
            // caller's Escape/re-open, and the grid is reloaded below from whatever the company now holds so the
            // operator is never left reading stale text over a half-applied batch. The pre-validation above is
            // what makes this arm practically unreachable; it exists because "practically" is not "provably".
            Refuse(ex.Message);
            LoadExistingRowsPreservingMessage();
            return false;
        }

        _storage.Save(_company);

        var noun = pending.Count == 1 ? SingularNoun : PluralNoun;
        RefreshParentOptions();
        LoadExistingRows();   // re-read the company so the grid shows what was actually stored
        MessageIsError = false;
        Message = $"{pending.Count} {noun} altered.";
        _onChanged();
        return true;
    }

    private void LoadExistingRowsPreservingMessage()
    {
        var text = Message;
        var isError = MessageIsError;
        LoadExistingRows();
        MessageIsError = isError;
        Message = text;
    }

    private void WriteAlterBatch(List<PendingAlteration> pending)
    {
        switch (Kind)
        {
            case MultiMasterKind.Ledger:
                foreach (var p in pending)
                {
                    var ledger = _company.FindLedger(p.Id)
                        ?? throw new InvalidOperationException($"Ledger '{p.Name}' no longer exists.");
                    // The reserved-name guard matters most here: ~14 engine sites resolve a ledger by a hardcoded
                    // string and fail SILENTLY afterwards, so renaming "Round Off" in a batch of fifty would turn
                    // B2C-QR rounding into zero with no error anywhere.
                    MasterAlterationRules.EnsureLedgerRenameAllowed(ledger, p.Name);
                    MasterAlterationRules.EnsureLedgerGroupValid(_company, p.Under.Id);
                    ledger.Name = p.Name;
                    ledger.GroupId = p.Under.Id;
                    ledger.OpeningBalance = p.Opening;
                    ledger.OpeningIsDebit = p.IsDebit;
                }
                break;

            case MultiMasterKind.AccountGroup:
            {
                var groups = new GroupService(_company);
                foreach (var p in pending)
                {
                    var group = _company.FindGroup(p.Id)
                        ?? throw new InvalidOperationException($"Group '{p.Name}' no longer exists.");
                    // 🔴 T2-77 CLASS: AlterGroup's `alias`, `primaryNature` and `behaviour` are all optional and
                    // all written unconditionally — `behaviour ?? GroupBehaviour.None` would wipe all five of this
                    // group's behavioural flags. Pass what the group already holds.
                    groups.AlterGroup(
                        p.Id, p.Name, p.Under.Id, group.Alias,
                        primaryNature: null,   // never a primary group: EnumerateExisting excludes them
                        behaviour: new GroupBehaviour(
                            group.BehavesLikeSubLedger,
                            group.NettBalancesForReporting,
                            group.UsedForCalculation,
                            group.AffectsGrossProfits,
                            group.PurchaseAllocationMethod));
                }
                break;
            }

            case MultiMasterKind.StockItem:
                foreach (var p in pending)
                {
                    var item = _company.FindStockItem(p.Id)
                        ?? throw new InvalidOperationException($"Stock item '{p.Name}' no longer exists.");
                    if (_company.FindStockGroup(p.Under.Id) is null)
                        throw new InvalidOperationException($"Stock group {p.Under.Id} not found.");
                    // BaseUnitId is NOT re-assigned, and no opening allocation is added — the two exclusions the
                    // single-item master makes, for the same two reasons (reinterpreted quantities; doubled
                    // opening stock).
                    item.Name = p.Name;
                    item.StockGroupId = p.Under.Id;
                }
                break;

            case MultiMasterKind.Godown:
            {
                var inventory = new InventoryService(_company);
                foreach (var p in pending)
                {
                    var godown = _company.FindGodown(p.Id)
                        ?? throw new InvalidOperationException($"Godown '{p.Name}' no longer exists.");
                    // 🔴 T2-77 CLASS: `alias`, `thirdParty` and `jobCostCentreId` are optional and written
                    // unconditionally. Omitting them would clear a stored alias, un-flag a third-party godown and
                    // detach its job/project cost centre — none of which this grid shows.
                    inventory.AlterGodown(
                        p.Id, p.Name, p.Under.Id, godown.Alias, godown.ThirdParty, godown.JobCostCentreId);
                }
                break;
            }

            default:
            {
                var cost = new CostMasterService(_company);
                foreach (var p in pending)
                {
                    var centre = _company.FindCostCentre(p.Id)
                        ?? throw new InvalidOperationException($"Cost centre '{p.Name}' no longer exists.");
                    // 🔴 T2-77 ITSELF — the ticket named this exact call. `alias` is now a REQUIRED parameter on
                    // AlterCostCentre, so this cannot compile without a decision; the decision is to preserve.
                    cost.AlterCostCentre(p.Id, p.Name, p.Under.Id, centre.ParentId, centre.Alias);
                }
                break;
            }
        }
    }

    // ------------------------------------------------------------------------------- shared validation

    private bool TryValidateName(
        string? raw, Guid exceptId, string label, out string name, out string? refusal)
    {
        name = string.Empty;
        refusal = null;

        var kind = Kind switch
        {
            MultiMasterKind.Ledger => (MasterKind?)MasterKind.Ledger,
            MultiMasterKind.AccountGroup => MasterKind.Group,
            MultiMasterKind.StockItem => MasterKind.StockItem,
            _ => null,   // godowns and cost centres have no MasterKind; their services own uniqueness
        };

        var trimmed = (raw ?? string.Empty).Trim();

        if (kind is { } k)
        {
            try
            {
                name = MasterAlterationRules.EnsureNameAvailable(_company, trimmed, exceptId, k);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                refusal = $"{label}: {Decapitalise(ex.Message)}";
                return false;
            }
        }

        // Godown / cost centre: the same two checks the engine makes, run here so the refusal names the ROW.
        if (trimmed.Length == 0)
        {
            refusal = $"{label}: a {SingularNoun} name is required.";
            return false;
        }

        var clashId = Kind == MultiMasterKind.Godown
            ? _company.FindGodownByName(trimmed)?.Id
            : _company.FindCostCentreByName(trimmed)?.Id;
        if (clashId is { } id && id != exceptId)
        {
            refusal = $"{label}: a {SingularNoun} named '{trimmed}' already exists.";
            return false;
        }

        name = trimmed;
        return true;
    }

    private static bool TryParseOpening(string? raw, string label, out Money opening, out string? refusal)
    {
        opening = Money.Zero;
        refusal = null;

        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return true;

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            refusal = $"{label}: opening balance must be an amount (e.g. 41237.53), or blank for nil.";
            return false;
        }

        if (amount < 0m)
        {
            refusal = $"{label}: opening balance cannot be negative — enter the amount and pick Cr for a "
                      + "credit balance.";
            return false;
        }

        opening = Money.FromRupees(amount);
        if (!opening.IsPaisaExact)
        {
            refusal = $"{label}: opening balance cannot be finer than a paisa (at most two decimal places).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The stock-item grid's Opening Qty + Rate, with the single-item master's own refusals by row. A quantity
    /// with no rate is allowed (the item simply opens at nil value, exactly as the single-item screen allows);
    /// a RATE with no quantity is refused, because there is nothing for it to value and it would be silently
    /// discarded.
    /// </summary>
    private static bool TryParseStockOpening(
        MultiMasterRowViewModel row, string label, out decimal quantity, out Money rate, out string? refusal)
    {
        quantity = 0m;
        rate = Money.Zero;
        refusal = null;

        var qtyText = (row.OpeningQuantityText ?? string.Empty).Trim();
        var rateText = (row.OpeningRateText ?? string.Empty).Trim();

        if (qtyText.Length == 0)
        {
            if (rateText.Length == 0) return true;
            refusal = $"{label}: a rate needs an opening quantity to value — type a quantity, or clear the rate.";
            return false;
        }

        if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty < 0m)
        {
            refusal = $"{label}: opening quantity must be a number ≥ 0, or blank for none.";
            return false;
        }
        if (decimal.Round(qty, 6) != qty)
        {
            refusal = $"{label}: opening quantity cannot be finer than six decimal places.";
            return false;
        }
        quantity = qty;

        if (rateText.Length == 0) return true;

        if (!decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var r) || r < 0m)
        {
            refusal = $"{label}: rate must be a number ≥ 0 (₹ per unit), or blank.";
            return false;
        }
        rate = Money.FromRupees(r);
        if (!rate.IsPaisaExact)
        {
            refusal = $"{label}: rate cannot be finer than a paisa (at most two decimal places).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Lower-cases the first letter of an engine message so it reads as a clause after the "Row N (Name): "
    /// prefix. The engine's own wording is kept verbatim otherwise — the refusal the operator reads is the
    /// refusal the engine actually made, not a paraphrase that could drift from it.
    /// </summary>
    private static string Decapitalise(string message)
        => string.IsNullOrEmpty(message) ? message : char.ToLowerInvariant(message[0]) + message[1..];
}
