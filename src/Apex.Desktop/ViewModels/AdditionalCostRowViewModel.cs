using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One <b>additional-cost</b> entry row on a Purchase item-invoice or a Stock-Journal transfer (Book pp.133–141;
/// catalog §11; Phase 6 slice 3 RQ-16..RQ-20): the picked <b>additional-cost ledger</b> (a Direct-Expenses ledger
/// carrying a non-null <c>MethodOfAppropriation</c>) plus the <b>amount</b> to apportion across the item / destination
/// lines to raise their landed stock rate. The ledger's method (by Quantity / by Value) decides the apportionment.
/// Parsing/validation is deferred to the parent VM; this class holds the editable state and raises change
/// notifications so the parent's landed-rate recompute + Accept gate refresh as the user types.
///
/// <para>MVVM boundary: references only the domain, no Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class AdditionalCostRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The additional-cost ledgers this row's picker chooses from (filtered to ledgers whose
    /// <c>MethodOfAppropriation</c> is non-null — an ordinary Direct-Expenses ledger stays out, RQ-19).</summary>
    public IReadOnlyList<DomainLedger> AdditionalCostLedgers { get; }

    [ObservableProperty] private DomainLedger? _selectedLedger;
    [ObservableProperty] private string _amountText = string.Empty;

    /// <summary>Census 5.11, wave 8 (IV-235): this cost leg's own narration, offered when the voucher type has
    /// <i>Provide narration for each ledger in voucher</i> on (help.tallysolutions.com/voucher-types-tally/).</summary>
    [ObservableProperty] private string _lineNarration = string.Empty;

    /// <summary>Whether the narration field under this row is shown (the type's flag, or a posted narration loaded on
    /// alteration).</summary>
    [ObservableProperty] private bool _showLineNarration;

    /// <summary>The narration as it posts: trimmed, blank ⇒ <c>null</c> (IV-198).</summary>
    public string? ToLineNarration() => string.IsNullOrWhiteSpace(LineNarration) ? null : LineNarration.Trim();

    public AdditionalCostRowViewModel(IReadOnlyList<DomainLedger> additionalCostLedgers, Action onChanged)
    {
        AdditionalCostLedgers = additionalCostLedgers ?? throw new ArgumentNullException(nameof(additionalCostLedgers));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    partial void OnSelectedLedgerChanged(DomainLedger? value) => _onChanged();
    partial void OnAmountTextChanged(string value) => _onChanged();
    partial void OnLineNarrationChanged(string value) => _onChanged();   // keeps one trailing blank row

    /// <summary>The parsed amount (null when blank/unparsable).</summary>
    public decimal? ParsedAmount =>
        string.IsNullOrWhiteSpace(AmountText)
            ? null
            : decimal.TryParse(AmountText.Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var a) ? a : null;

    /// <summary>True once the row has been touched at all — a wholly blank trailing row is ignored by the parent.
    /// <para>A row carrying only a narration is NOT blank (census 5.11 wave 8): it is refused as incomplete rather
    /// than its words being dropped silently.</para></summary>
    public bool IsBlank => SelectedLedger is null && string.IsNullOrWhiteSpace(AmountText)
                           && string.IsNullOrWhiteSpace(LineNarration);

    /// <summary>True when the row is a valid, complete additional cost: a ledger picked and a paisa-exact
    /// amount &gt; 0. The parent apportions only complete rows.</summary>
    public bool IsComplete =>
        SelectedLedger is not null
        && ParsedAmount is { } a && a > 0m
        && new Apex.Ledger.Money(a).IsPaisaExact;
}
