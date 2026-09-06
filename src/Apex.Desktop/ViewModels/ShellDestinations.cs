using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// ONE place the shell can jump to: a page or menu hub reachable from the Gateway cascade, named by the exact
/// menu route that reaches it.
/// </summary>
/// <param name="Section">The breadcrumb of everything ABOVE the row — the root section header plus any
/// submenu groups, e.g. <c>"Reports → Statements of Accounts"</c>. Never empty: a destination with no parent
/// section is a flat dump, which this project does not ship.</param>
/// <param name="Label">The row's own label, exactly as the menu spells it.</param>
/// <param name="Path">The Group row labels to drill through, in order, before selecting
/// <paramref name="Label"/>. Empty for a row that sits directly on the Gateway root column.</param>
/// <param name="OpensSubmenu">True when the destination is a menu GROUP (a hub such as "Cash Book") rather
/// than a page, so the replay must drill a Group row and not look for a Page row that does not exist.</param>
public sealed record ShellDestination(
    string Section, string Label, IReadOnlyList<string> Path, bool OpensSubmenu)
{
    /// <summary>How the destination reads in a list: <c>"Reports → Balance Sheet"</c>.</summary>
    public string Display => $"{Section} → {Label}";
}

/// <summary>
/// THE DESTINATION REGISTRY for Switch To (census 14.2).
///
/// <para>🔴 <b>It is a PROJECTION of the shipped Go To index, not a second walk of the menu tree — and that
/// correction is the whole content of this file.</b> An earlier draft walked the Gateway itself, which made
/// this the SECOND independent statement of the navigation tree in one code base: <c>MainWindowViewModel</c>
/// already ships <c>BuildGoToIndex</c>/<c>WalkGoTo</c> for Go To (W2-14, census 14.1), which landed on
/// <c>main</c> after this track's design was written. Two walks is exactly the failure this file's own remarks
/// warned about, and the duplicate had drifted in three measurable ways before it was ever run:</para>
/// <list type="number">
/// <item>🔴 <b>It could not reach a menu HUB at all.</b> The duplicate indexed Page rows only. Several report
/// families — the Account-Books pickers among them — are GROUPS, so Switch To could not reach the Cash Book,
/// the Bank Book or the all-ledgers book picker, while Go To could. That is a user-visible hole, not a
/// stylistic difference.</item>
/// <item>🔴 <b>Its cycle guard admitted phantom routes.</b> It resolved a submenu through the non-nullable
/// <c>BuildGroupColumn</c>, whose historical <c>_</c> fallback returns the CREATE column for any label it does
/// not know — so an unrecognised Group row would have been walked as Create and its rows advertised under a
/// breadcrumb that reaches something else. <c>WalkGoTo</c> resolves through the nullable <c>SubmenuFor</c> and
/// simply stops, which is why the shipped one is the safe one to keep.</item>
/// <item>It spelled the breadcrumb with <c>›</c> where the shipped index spells it <c>→</c>, so the same route
/// read two ways on two screens.</item>
/// </list>
///
/// <para><b>What Switch To still owns.</b> The ONE documented difference from Go To is the return path —
/// vendor: Go To <i>"takes you back to where you left"</i>, Switch To does not — and that lives in
/// <c>MainWindowViewModel.NavigateTo</c>, which clears the cascade before replaying. Sharing the index is what
/// guarantees the two overlays can never come to disagree about what the menus contain.</para>
/// </summary>
public static class ShellDestinations
{
    /// <summary>
    /// Every destination reachable from the Gateway root for the company currently open on
    /// <paramref name="vm"/>, projected from the shipped Go To index. Empty when no company is open (the
    /// Gateway, and so every destination, does not exist without one).
    ///
    /// <para>The underlying walk is PURE — it builds fresh columns and never touches the live cascade — so
    /// this is safe to call while the operator is standing anywhere. The projection copies each row into this
    /// file's own immutable record rather than handing out the Go To view models, whose <c>IsSelected</c> flag
    /// is live highlight state that two overlays must never share.</para>
    /// </summary>
    public static IReadOnlyList<ShellDestination> Build(MainWindowViewModel vm)
    {
        if (vm?.Company is null) return Array.Empty<ShellDestination>();

        return vm.BuildGoToIndex()
            .Select(d => new ShellDestination(d.Section, d.Label, d.Path, d.OpensSubmenu))
            .ToArray();
    }
}
