using System;
using Apex.Ledger.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// <b>Ctrl+J — Exception Reports</b> over the open Chart of Accounts (census 2.13). A Miller-column page column to
/// the RIGHT of the tree it filters, never a stacked overlay, copying <see cref="BasisOfValuesViewModel"/>'s shape
/// exactly: seed from the live screen, offer the choice, apply, pop back onto the re-projected page.
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com).</b> The Chart of Accounts page
/// (<c>/tally-prime/charts-of-accounts-tally/</c>), section <b>"View and Delete Multiple Unused Masters"</b>, gives
/// the route verbatim: <b><c>Alt+G</c> (Go To) &gt; Chart of Accounts &gt; Ledgers</b>, then
/// <b><c>Ctrl+J</c> (Exception Reports) &gt; "Show Unused"</b>, after which <i>"The List of Ledgers (Unused)
/// appears"</i>. <c>Ctrl+J</c> was verified unbound in this application before it was taken (zero <c>Key.J</c> arms
/// in the window's tunnel handler), so the vendor's own chord was free.</para>
///
/// <para><b>🔴 WHAT THIS PANEL DELIBERATELY DOES NOT CARRY.</b> Three things, each named rather than half-built:
/// <list type="number">
///   <item><b>The bulk delete.</b> The vendor's pane continues Spacebar-to-select &gt; <c>Alt+D</c> &gt; <c>Y</c>.
///     Single-master <c>Alt+D</c> already exists on this screen; a bulk delete over a filtered set is its own
///     slice with its own referential-integrity surface (<c>MasterDeletionRules.GuardedForeignKeyColumns</c>).
///     <b>In this wave the pane is a VIEW.</b></item>
///   <item><b>The stock-item arm.</b> "Delete Unused Stock Items at once" is on the same vendor page, but this
///     application has no stock-item list screen to host it — see the remarks on <see cref="UnusedMasters"/>.</item>
///   <item><b>A third "Ledgers already used" view.</b> The vendor sentence <i>"If you want to view Ledgers Already
///     used in transactions, then press Ctrl+J"</i> can be read as a toggle back to the full tree or as a third,
///     complementary list. The page does not disambiguate it and nothing else retrievable does, so this panel
///     offers only the two views it can source and does not invent the third.</item>
/// </list></para>
/// </summary>
public sealed partial class ExceptionReportsViewModel : ViewModelBase
{
    private readonly ChartOfAccountsViewModel _chart;

    /// <summary>The column title / heading for the panel.</summary>
    public string Title => "Exception Reports — Ctrl+J";

    /// <summary>The screen this panel filters, for the heading line beneath the title.</summary>
    public string ScreenTitle => _chart.Title;

    /// <summary>Radio: show every group and ledger — the screen's normal state.</summary>
    [ObservableProperty] private bool _isAllMasters;

    /// <summary>Radio: show only ledgers with no recorded transactions — the vendor's "Show Unused".</summary>
    [ObservableProperty] private bool _isShowUnused;

    /// <summary>A short status line shown after applying (feedback that the tree re-projected).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public ExceptionReportsViewModel(ChartOfAccountsViewModel chart)
    {
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));

        // Seed from the live screen so opening the panel and applying it unchanged is a no-op.
        _isShowUnused = chart.ShowUnusedOnly;
        _isAllMasters = !chart.ShowUnusedOnly;
    }

    partial void OnIsShowUnusedChanged(bool value)
    {
        if (value) IsAllMasters = false;
    }

    partial void OnIsAllMastersChanged(bool value)
    {
        if (value) IsShowUnused = false;
    }

    /// <summary>
    /// Applies the chosen view to the live Chart of Accounts and re-projects it. Nothing is stored and no master is
    /// edited — "unused" is derived from the vouchers on every rebuild (see <see cref="UnusedMasters"/>), so this
    /// is a display change in the strictest sense.
    /// </summary>
    public void Apply()
    {
        _chart.ShowUnusedOnly = IsShowUnused;

        Status = IsShowUnused
            ? $"Showing {_chart.UnusedLedgerCount} unused ledger(s) — no transaction has been posted against them."
            : "Showing every group and ledger.";

        OnPropertyChanged(nameof(ScreenTitle));
    }
}
