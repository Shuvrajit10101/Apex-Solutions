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

/// <summary>W2-20 — which master family a multi-create grid is entering.</summary>
public enum MultiMasterKind
{
    /// <summary>Multi Ledger Creation — Name of Ledger · Under · Opening Balance (+ Dr/Cr).</summary>
    Ledger,

    /// <summary>Multi Group Creation — Name of Group · Under. Groups carry no opening balance.</summary>
    AccountGroup,
}

/// <summary>
/// One option in the header <b>Under Group</b> picker. The FIRST option is the vendor's <b>"All Items"</b>
/// sentinel, whose <see cref="Group"/> is <c>null</c> — under it each grid row picks its own Under, which is
/// how a mixed list (debtors + creditors + expense heads) is entered in one pass.
/// </summary>
public sealed class MultiMasterUnderOption
{
    public MultiMasterUnderOption(string display, Group? group)
    {
        Display = display;
        Group = group;
    }

    /// <summary>The label shown in the picker ("All Items", or the group's own name).</summary>
    public string Display { get; }

    /// <summary>The group this option names, or <c>null</c> for the "All Items" sentinel.</summary>
    public Group? Group { get; }

    public override string ToString() => Display;
}

/// <summary>
/// One row of the multi-create grid — a single master being typed. Deliberately holds only the operator's raw
/// text: parsing and validation happen once, for the WHOLE batch, in
/// <see cref="MultiMasterCreateViewModel.Accept"/>, so no row can be half-committed by a per-row write.
/// </summary>
public sealed partial class MultiMasterRowViewModel : ViewModelBase
{
    /// <summary>
    /// The owner's group list, handed to each row so the per-row Under picker has an ItemsSource without the
    /// item template having to reach up the visual tree for its parent's DataContext — a lookup that compiled
    /// bindings cannot type-check and that silently yields an empty picker when it misses.
    /// </summary>
    public ObservableCollection<Group> UnderOptions { get; init; } = new();

    /// <summary>True only on the ledger grid — mirrors the owner's <c>ShowsOpeningBalance</c> for the template.</summary>
    public bool ShowsOpeningBalance { get; init; }

    /// <summary>
    /// True while this row's own Under is the one that counts, i.e. while the header names "All Items". Kept in
    /// sync by the owner, so the per-row picker greys out the moment a header group takes over.
    /// </summary>
    [ObservableProperty] private bool _underIsEditable = true;

    /// <summary>The 1-based row number shown in the leftmost gutter and quoted in every refusal message.</summary>
    [ObservableProperty] private int _number;

    /// <summary>"Name of Ledger" / "Name of Group" — the vendor's own column heading.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>This row's own Under group. Used ONLY under "All Items"; otherwise the header group wins.</summary>
    [ObservableProperty] private Group? _under;

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

    /// <summary>True when the operator has typed nothing at all into this row — a trailing blank is ignored.</summary>
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Name)
        && string.IsNullOrWhiteSpace(OpeningBalanceText)
        && Under is null;
}

/// <summary>
/// W2-20 (census 2.12) — <b>Multi Ledger / Multi Group Creation</b>: create MANY masters from one grid instead
/// of one screen per master.
///
/// <para><b>Grounding.</b> help.tallysolutions.com, "How to Use Chart of Accounts in TallyPrime" (fetched
/// 2026-09-05): Multi Masters is reached with <b>Alt+H</b> and offers <i>Multi Create</i> / <i>Multi Alter</i>;
/// the Multi Ledger Creation screen carries an <b>"Under Group"</b> header field that <b>defaults to "All
/// Items"</b> and a grid of <b>"Name of Ledger"</b>, <b>"Under"</b> and <b>"Opening Balance"</b>. The Multi
/// Stock Item screen is documented with the identical header-plus-grid shape, which is what establishes the
/// pattern as the screen family's rather than one screen's.</para>
///
/// <para><b>Two documented divergences, LABELLED AS OURS (RULING 9)</b> — the source is silent on both:
/// <list type="bullet">
///   <item>the per-row <b>Dr/Cr side</b> (see <see cref="MultiMasterRowViewModel.OpeningIsCredit"/>);</item>
///   <item><b>all-or-nothing Accept.</b> Every row is validated BEFORE anything is written, and one bad row
///     refuses the whole batch naming the offending row. A partial write leaves the operator guessing which
///     of twenty names landed; worse, the engine's own uniqueness guard cannot see a name entered twice in
///     the SAME batch (neither exists yet), so without a batch-level pass row 12 would silently create a
///     duplicate of row 3. The operator's typing survives a refusal so the bad row can simply be corrected.
///     </item>
/// </list></para>
///
/// <para><b>Keyboard-first.</b> The grid grows as it is typed — filling the last row appends a fresh blank
/// one — so there is no "add row" button to reach for, and Ctrl+A accepts the whole batch through the shell's
/// ordinary master-accept path. No affordance here requires a pointer.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable, exactly like every other master view model.</para>
/// </summary>
public sealed partial class MultiMasterCreateViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>Which master family this grid enters — fixed for the life of the screen.</summary>
    public MultiMasterKind Kind { get; }

    /// <summary>The screen title, and the cascade column's header ("Multi Ledger Creation").</summary>
    public string Title => Kind == MultiMasterKind.Ledger
        ? "Multi Ledger Creation"
        : "Multi Group Creation";

    /// <summary>The vendor's own name column heading for this grid.</summary>
    public string NameColumnHeader => Kind == MultiMasterKind.Ledger ? "Name of Ledger" : "Name of Group";

    /// <summary>True only for the ledger grid — a group has no opening balance.</summary>
    public bool ShowsOpeningBalance => Kind == MultiMasterKind.Ledger;

    /// <summary>The header "Under Group" options: the "All Items" sentinel first, then every company group.</summary>
    public ObservableCollection<MultiMasterUnderOption> UnderGroupOptions { get; } = new();

    /// <summary>The per-row Under picker's options — the company's groups, name-sorted.</summary>
    public ObservableCollection<Group> UnderOptions { get; } = new();

    /// <summary>The grid rows. Always ends in exactly one blank row.</summary>
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

    /// <summary>True while the header names the "All Items" sentinel, i.e. each row carries its own Under.</summary>
    public bool IsAllItems => SelectedUnderGroup?.Group is null;

    public MultiMasterCreateViewModel(
        MultiMasterKind kind, Company company, CompanyStorage storage, Action onChanged)
    {
        Kind = kind;
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshGroupOptions();
        SelectedUnderGroup = UnderGroupOptions.FirstOrDefault();   // "All Items" — the vendor's default
        AppendBlankRow();
    }

    partial void OnSelectedUnderGroupChanged(MultiMasterUnderOption? value)
    {
        OnPropertyChanged(nameof(IsAllItems));
        foreach (var row in Rows)
        {
            // The per-row Under only counts under "All Items"; otherwise the header group wins and the cell greys.
            row.UnderIsEditable = value?.Group is null;
            // Re-default every untouched row's side to the new header group's nature, so the operator sees the
            // conventional Dr/Cr before typing rather than after saving.
            if (string.IsNullOrWhiteSpace(row.OpeningBalanceText))
                row.OpeningIsCredit = DefaultsToCredit(value?.Group);
        }
    }

    /// <summary>Rebuilds both group pickers from the company (the "All Items" sentinel stays first).</summary>
    private void RefreshGroupOptions()
    {
        var previousId = SelectedUnderGroup?.Group?.Id;

        UnderOptions.Clear();
        UnderGroupOptions.Clear();
        UnderGroupOptions.Add(new MultiMasterUnderOption("All Items", null));
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            UnderOptions.Add(g);
            UnderGroupOptions.Add(new MultiMasterUnderOption(g.Name, g));
        }

        if (previousId is { } id)
            SelectedUnderGroup = UnderGroupOptions.FirstOrDefault(o => o.Group?.Id == id)
                                 ?? UnderGroupOptions.FirstOrDefault();
    }

    /// <summary>
    /// The conventional opening side for a group: Asset/Expense open Debit, Liability/Income open Credit —
    /// the same rule the single-ledger master applies.
    /// </summary>
    private bool DefaultsToCredit(Group? group)
        => group is not null && group.Nature is not (GroupNature.Asset or GroupNature.Expense);

    private void AppendBlankRow()
    {
        var row = new MultiMasterRowViewModel
        {
            UnderOptions = UnderOptions,
            ShowsOpeningBalance = ShowsOpeningBalance,
            Number = Rows.Count + 1,
            UnderIsEditable = IsAllItems,
            OpeningIsCredit = DefaultsToCredit(SelectedUnderGroup?.Group),
        };
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
    }

    /// <summary>
    /// Keeps exactly one trailing blank row, so the grid grows under the operator's fingers: filling the last
    /// row makes a new one appear beneath it and Down reaches it. There is deliberately no "add row" button —
    /// an affordance that only a mouse can drive would fail the keyboard-first contract.
    /// </summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MultiMasterRowViewModel.Number)) return;
        if (Rows.Count > 0 && !Rows[^1].IsBlank) AppendBlankRow();

        // Picking a row's own Under re-defaults its untouched side from that group's nature.
        if (e.PropertyName == nameof(MultiMasterRowViewModel.Under)
            && sender is MultiMasterRowViewModel r
            && string.IsNullOrWhiteSpace(r.OpeningBalanceText))
            r.OpeningIsCredit = DefaultsToCredit(r.Under);
    }

    private void ResetGrid()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        AppendBlankRow();
    }

    /// <summary>The parsed, validated shape of one row — built only after the WHOLE batch has passed.</summary>
    private sealed record PendingRow(string Name, Group Under, Money Opening, bool IsDebit);

    /// <summary>
    /// Ctrl+A — validate EVERY filled row, then (only if all pass) create them all and persist once.
    ///
    /// <para>The ordering is the whole point. Validation runs against the company as it stands PLUS the names
    /// already claimed earlier in this same batch, so a name typed twice is caught even though neither copy
    /// exists yet. Nothing is added to the company and <see cref="CompanyStorage.Save"/> is not called at all
    /// unless the batch is wholly valid — so a refused batch cannot leave a partial set of masters behind.</para>
    /// </summary>
    public bool Accept()
    {
        MessageIsError = false;
        Message = null;

        var filled = Rows.Where(r => !r.IsBlank).ToList();
        if (filled.Count == 0)
            return Refuse("Nothing to create — type at least one name.");

        var headerGroup = SelectedUnderGroup?.Group;
        var pending = new List<PendingRow>(filled.Count);
        // Names claimed earlier in THIS batch — the clash the engine's guard structurally cannot see.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in filled)
        {
            var label = $"Row {row.Number} ({(row.Name ?? string.Empty).Trim()})";

            // --- name: required, unique against the company AND against this batch.
            string name;
            try
            {
                name = MasterAlterationRules.EnsureNameAvailable(
                    _company, row.Name, Guid.Empty,
                    Kind == MultiMasterKind.Ledger ? MasterKind.Ledger : MasterKind.Group);
            }
            catch (InvalidOperationException ex)
            {
                return Refuse($"{label}: {Decapitalise(ex.Message)}");
            }

            if (!claimed.Add(name))
                return Refuse($"{label}: '{name}' is entered twice in this batch.");

            // --- Under: the header group when one is named, else this row's own pick.
            var under = headerGroup ?? row.Under;
            if (under is null)
                return Refuse($"{label}: pick an Under group.");

            // --- opening balance (ledger grid only): the single-ledger master's three refusals, by row.
            var opening = Money.Zero;
            var isDebit = !row.OpeningIsCredit;
            if (Kind == MultiMasterKind.Ledger)
            {
                var text = (row.OpeningBalanceText ?? string.Empty).Trim();
                if (text.Length > 0)
                {
                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                        return Refuse(
                            $"{label}: opening balance must be an amount (e.g. 41237.53), or blank for nil.");

                    if (amount < 0m)
                        return Refuse($"{label}: opening balance cannot be negative — enter the amount and " +
                                      "pick Cr for a credit balance.");

                    opening = Money.FromRupees(amount);
                    if (!opening.IsPaisaExact)
                        return Refuse($"{label}: opening balance cannot be finer than a paisa " +
                                      "(at most two decimal places).");
                }
            }

            pending.Add(new PendingRow(name, under, opening, isDebit));
        }

        // ------------------------------------------------------------- every row passed: write the batch.
        try
        {
            if (Kind == MultiMasterKind.Ledger)
            {
                foreach (var p in pending)
                    _company.AddLedger(new DomainLedger(Guid.NewGuid(), p.Name, p.Under.Id, p.Opening, p.IsDebit));
            }
            else
            {
                var service = new GroupService(_company);
                foreach (var p in pending)
                    service.CreateGroup(p.Name, p.Under.Id);
            }
        }
        catch (InvalidOperationException ex)
        {
            // Defensive: every case above is pre-validated, so this arm should be unreachable. If the engine
            // ever refuses anyway, say so plainly rather than leaving a half-written batch silently persisted.
            return Refuse(ex.Message);
        }

        _storage.Save(_company);

        var noun = Kind == MultiMasterKind.Ledger
            ? (pending.Count == 1 ? "ledger" : "ledgers")
            : (pending.Count == 1 ? "group" : "groups");
        MessageIsError = false;
        Message = headerGroup is { } hg
            ? $"{pending.Count} {noun} created under {hg.Name}."
            : $"{pending.Count} {noun} created.";

        RefreshGroupOptions();      // a group just created is immediately selectable as a parent
        ResetGrid();
        _onChanged();
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
