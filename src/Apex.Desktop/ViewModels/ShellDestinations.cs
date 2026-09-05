using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// ONE place the shell can jump to: a page (report / master / voucher screen) reachable from the Gateway
/// cascade, named by the exact menu route that reaches it.
/// </summary>
/// <param name="Section">The breadcrumb of everything ABOVE the page — the root section header plus any
/// submenu groups, e.g. <c>"Reports › GST Reports"</c>. Never empty: a destination with no parent section is
/// a flat dump, which this project does not ship.</param>
/// <param name="Label">The page row's own label, exactly as the menu spells it.</param>
/// <param name="Path">The Group row labels to drill through, in order, before selecting
/// <paramref name="Label"/>. Empty for a page that sits directly on the Gateway root column.</param>
public sealed record ShellDestination(string Section, string Label, IReadOnlyList<string> Path)
{
    /// <summary>How the destination reads in a list: <c>"Reports › Balance Sheet"</c>.</summary>
    public string Display => $"{Section} › {Label}";
}

/// <summary>
/// THE DESTINATION REGISTRY — every page the Gateway cascade can reach, DERIVED FROM THE MENUS THEMSELVES.
///
/// <para>🔴 <b>Derived, never listed.</b> A hand-written list of destinations is a second statement of the
/// navigation tree, and a second statement goes stale in both directions: it advertises screens the menus no
/// longer carry, and — the failure this project has already filed twice, as
/// <c>CostReports.BuildLedgerBreakup</c> and <c>MultiAccountPrintViewModel</c> — it silently stops mentioning
/// screens that exist. This walk asks the real column builders what the menus contain, so a destination
/// exists here if and ONLY if a user can arrow to it, and its <see cref="ShellDestination.Path"/> is the
/// literal keystroke route.</para>
///
/// <para><b>What is deliberately NOT a destination.</b>
/// <list type="bullet">
/// <item><b>Data-driven picker columns</b> (Cash Book / Bank Book / Ledger / the group pickers). Their rows
/// are the company's own ledgers and groups, not screens — listing them would put every ledger name in the
/// jump list and drown the ~120 real destinations.</item>
/// <item><b>Action rows</b> (today only "Quit — Change Company"). They are not places; the vendor describes
/// this feature as switching to a different report and creating masters and vouchers.</item>
/// </list></para>
/// </summary>
public static class ShellDestinations
{
    /// <summary>
    /// Enumerates every destination reachable from the Gateway root for the company currently open on
    /// <paramref name="vm"/>. Empty when no company is open (the Gateway does not exist without one).
    ///
    /// <para>The walk is PURE: it asks for freshly built columns and never touches the live cascade, so it is
    /// safe to run while the operator is standing anywhere.</para>
    /// </summary>
    public static IReadOnlyList<ShellDestination> Build(MainWindowViewModel vm)
    {
        if (vm?.Company is null) return Array.Empty<ShellDestination>();

        var found = new List<ShellDestination>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(vm, vm.BuildRootColumnForWalk(), GatewayMenu.Root, section: string.Empty,
             path: Array.Empty<string>(), onPath: new HashSet<GatewayMenu> { GatewayMenu.Root },
             found, seen);
        return found;
    }

    private static void Walk(
        MainWindowViewModel vm, GatewayColumn column, GatewayMenu menu, string section,
        IReadOnlyList<string> path, HashSet<GatewayMenu> onPath,
        List<ShellDestination> found, HashSet<string> seen)
    {
        // A picker over company data is not a menu of screens — see the class remarks.
        if (column.Kind == GatewayColumnKind.DataDriven) return;

        // The root column's section HEADERS (MASTERS / STATUTORY / …) are the top of the breadcrumb; a submenu
        // column's own header rows are its title, so a submenu keeps the breadcrumb it arrived with.
        var currentSection = section;

        foreach (var item in column.Items)
        {
            if (item.IsHeader)
            {
                if (menu == GatewayMenu.Root) currentSection = item.Label;
                continue;
            }

            if (!item.IsSelectable) continue;

            switch (item.Kind)
            {
                case MenuItemKind.Page:
                {
                    // Never a flat dump: a page always reads under the section (and any submenus) above it.
                    var parent = string.IsNullOrEmpty(currentSection) ? "Gateway" : currentSection;
                    var d = new ShellDestination(parent, item.Label, path);
                    // The same page can hang off two routes ("Price List" is a master under Create and a report
                    // under Inventory Reports). Both are real routes, so both are listed; only an identical
                    // route is a duplicate.
                    if (seen.Add(d.Display + "|" + string.Join("/", path))) found.Add(d);
                    break;
                }

                case MenuItemKind.Group:
                {
                    var (child, childMenu, _) = vm.BuildGroupColumnForWalk(item.Label, menu);

                    // 🔴 CYCLE GUARD, and it is load-bearing rather than defensive. The group switch's default
                    // arm returns the CREATE column for any label it does not know, so an unrecognised group row
                    // inside Create would hand back Create again and recurse for ever. Refusing a menu already
                    // on this path also stops any future genuine cycle in the tree.
                    if (!onPath.Add(childMenu)) break;

                    var childSection = string.IsNullOrEmpty(currentSection)
                        ? item.Label
                        : currentSection + " › " + item.Label;
                    Walk(vm, child, childMenu, childSection, path.Append(item.Label).ToArray(), onPath,
                         found, seen);
                    onPath.Remove(childMenu);
                    break;
                }

                // Action rows are not places. See the class remarks.
                case MenuItemKind.Action:
                default:
                    break;
            }
        }
    }
}
