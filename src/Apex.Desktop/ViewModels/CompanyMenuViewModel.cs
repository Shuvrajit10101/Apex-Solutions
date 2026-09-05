using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 14.9 — <b>THE COMPANY MENU (Alt+K)</b>: the verbs, the chords the vendor gives them, and the disclosure of
/// what this build does not have. A pure builder over <see cref="MenuItemViewModel"/>, so the shell's own
/// cascade renders it, arrow-navigates it, assigns its bare-letter hotkeys and pops it on Escape.
///
/// <para><b>Fidelity (Ruling 14 tier 1).</b> help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/,
/// verbatim: <c>Alt+K</c> — <i>"To open the company menu with the list of actions related to managing your
/// company"</i>. The menu's contents are enumerated on
/// help.tallysolutions.com/tally-prime/company-management/set-up-company-tally/ ("Set Up Company"), which
/// reaches two of them as "press Alt+K (Company) &gt; Create" and "&gt; Alter":
/// <b>Create · Alter · Select · TallyVault · Change User · Edit Log</b>.</para>
///
/// <para>🔴 <b>A TOP-MENU OVERLAY, NEVER A GATEWAY SECTION — and that is a shipped test's ruling, not a
/// preference.</b> A "Company" section on the Gateway root column was built and removed TWICE (W0-2b, then
/// W2-18), against <c>docs/invented-vs-cloned.md</c> IV-29's finding that this menu's standing fault is having
/// grown a section per phase. <c>GatewayHierarchyTests</c> pins the root column at exactly
/// Masters / Statutory / Transactions / Reports / Data. This column is pushed onto the cascade and touches no
/// column builder, so that test stays green and unmodified — which is the point.</para>
///
/// <para>🔴 <b>WHAT THIS MENU DELIBERATELY DOES NOT OFFER, and why the omission is the honest answer.</b>
/// <list type="bullet">
/// <item><b>TallyVault · Change User · Edit Log</b> — the last three rows of the vendor's list. All three are
/// census area 16 (security &amp; audit), which is outside this build. A row that opens a "not available"
/// message is worse than no row: it advertises a capability the product does not have. The column discloses
/// them in a header line instead of pretending the list is complete.</item>
/// <item><b>"Shut Company" is NOT on the vendor's Alt+K list.</b> Census row 14.9's own title puts it there;
/// the vendor page does not. Shut is attested on the SHORTCUT page as <c>Ctrl+F3</c> — "To shut the currently
/// loaded companies" — so it is offered here carrying that chord, which is where the vendor documents it, and
/// nothing invents an Alt+K row the source does not show.</item>
/// </list></para>
/// </summary>
public static class CompanyMenu
{
    /// <summary>The column header. Also the screen title while the menu is the active pane.</summary>
    public const string ColumnTitle = "Company";

    /// <summary>
    /// The verbs this application actually has, in the vendor's order (Create · Alter · Select), with Shut
    /// last on the chord the vendor gives it. Named as a constant so the test that asserts "only verbs this
    /// application has" DERIVES its expectation instead of restating it.
    /// </summary>
    public static readonly IReadOnlyList<string> OfferedVerbs = new[] { "Create", "Alter", "Select", "Shut" };

    /// <summary>
    /// The three vendor rows this build cannot honour. Named for the same reason: the disclosure line that
    /// names them and the test that locks the disclosure read the same list.
    /// </summary>
    public static readonly IReadOnlyList<string> WithheldVerbs = new[] { "TallyVault", "Change User", "Edit Log" };

    /// <summary>
    /// 🔴 THE HONEST DISCLOSURE. It names the vendor rows this build does not have, so nobody — operator or
    /// later agent — reads a four-row menu as the whole feature. A test locks it.
    /// </summary>
    public static string Disclosure =>
        "Not in this build: " + string.Join(", ", WithheldVerbs) + " (security & audit)";

    /// <summary>
    /// Builds the Alt+K column for the company named <paramref name="companyName"/>.
    /// </summary>
    /// <param name="companyName">The open company, shown so the operator can see what Alter and Shut act on.</param>
    public static GatewayColumn BuildColumn(
        string companyName, Action create, Action alter, Action select, Action shut)
    {
        if (create is null) throw new ArgumentNullException(nameof(create));
        if (alter is null) throw new ArgumentNullException(nameof(alter));
        if (select is null) throw new ArgumentNullException(nameof(select));
        if (shut is null) throw new ArgumentNullException(nameof(shut));

        var column = new GatewayColumn(ColumnTitle);

        // The open company is named on a header row rather than in the column title: Alter and Shut act on it,
        // and "Shut" with no statement of WHAT is being shut is the kind of unlabelled destructive verb this
        // project has had to correct before.
        column.Add(MenuItemViewModel.Header(
            string.IsNullOrWhiteSpace(companyName) ? "COMPANY" : companyName.ToUpperInvariant()));

        // Vendor order: Create · Alter · Select. The vendor gives Create and Alter no chord, so neither carries
        // one here — an invented shortcut wearing an attested-looking hint is worse than no hint.
        column.Add(new MenuItemViewModel("Create", create, string.Empty, kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel("Alter", alter, string.Empty, kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel("Select", select, "Alt+F3", kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel("Shut", shut, "Ctrl+F3", kind: MenuItemKind.Action));

        // A HEADER row, so arrows skip it and Enter can never fire it — the disclosure is a statement, not an
        // affordance.
        column.Add(MenuItemViewModel.Header(Disclosure));

        return column;
    }

    /// <summary>The verbs the built column actually offers — what the "only verbs we have" test reads.</summary>
    public static IReadOnlyList<string> VerbsOf(GatewayColumn column) =>
        column is null
            ? Array.Empty<string>()
            : column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
}
