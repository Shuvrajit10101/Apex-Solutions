using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// THE FOUR MENUS THE VENDOR PUTS ON A REPORT, and the disclosure of what each one withholds.
///
/// <para>Every builder here is a pure function over <see cref="MenuItemViewModel"/> returning a
/// <see cref="GatewayColumn"/>, exactly as <see cref="CompanyMenu"/> is, so the shell's own cascade renders it,
/// arrow-navigates it, assigns its bare-letter hotkeys and pops it on Escape. No new view code exists that could
/// quietly fail to render — the failure mode this project has filed twice
/// (<c>CostReports.BuildLedgerBreakup</c>, <c>MultiAccountPrintViewModel</c>).</para>
///
/// <para>🔴 <b>WHY FOUR MENUS LANDED IN ONE SLICE.</b> They are one defect, not four: the vendor's shortcut table
/// pairs a "current object" chord with a "menu" chord six times over
/// (help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/, <i>Across TallyPrime</i>), and this build
/// shipped three of the current-object halves and <b>none</b> of the menu halves. Measured before this slice:
/// <c>Alt+P</c> inert, <c>Alt+M</c> inert, <c>Alt+E</c> silently doing <c>Ctrl+E</c>'s job, and <c>Ctrl+E</c>
/// itself unreachable on a report. Fixing one without the others would have left the pairing half-true.</para>
///
/// <para>🔴 <b>WHAT EVERY ROW HERE OBEYS: pop, then act.</b> Each action first pops its own menu column and only
/// then runs the verb. That is load-bearing rather than tidy. <see cref="MainWindowViewModel.OpenWhatsAppShare"/>
/// and its e-mail twin branch on <c>CurrentScreen == Screen.VoucherDetail</c>; with the menu column still on top,
/// <c>CurrentScreen</c> is the MENU and those branches would miss — a share launched over a drilled voucher would
/// silently attach the <i>report</i> underneath it instead. Popping restores the screen id and re-binds the page
/// through the same <c>BackFromPage</c> path an F12 config column already uses.</para>
/// </summary>
public static class ChangeViewMenu
{
    /// <summary>
    /// The column header. Also the screen title while the menu is the active pane.
    /// <para>Attested in BOTH tiers of the vendor's own documentation, which matters because this is the one
    /// chord in the slice that lands beside a shipped incumbent (Change Mode, ruling 17). The product-wide
    /// shortcut table gives <c>Ctrl+H</c> as <i>"To change view – display report details in different views"</i>
    /// (help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/), and the feature page writes the menu by
    /// this exact name — <i>"press <b>Ctrl+H</b> (Change View), and select the view under <b>Saved Views</b>"</i>
    /// (help.tallysolutions.com/use-save-view-feature-in-tallyprime/). Both pages were opened by content for
    /// this slice. The shortcut table's own wording also settles the SCOPE question the guard encodes: it says
    /// <i>report</i> details, which is why <see cref="MainWindowViewModel.OpenChangeViewMenu"/> refuses off a
    /// live report and the voucher's Change Mode keeps the chord where it had it.</para>
    /// </summary>
    public const string ColumnTitle = "Change View";

    /// <summary>Vendor row label, verbatim (help.tallysolutions.com/use-save-view-feature-in-tallyprime/,
    /// <i>Open Saved Views</i>): <i>"press Ctrl+H (Change View), and select the view"</i>.</summary>
    public const string SavedViewsVerb = "Saved Views";

    /// <summary>Vendor row label, verbatim: <i>"Ctrl+H (Change View) &gt; Delete Saved Views"</i>.</summary>
    public const string DeleteSavedViewsVerb = "Delete Saved Views";

    /// <summary>Vendor row label, verbatim: <i>"Ctrl+H (Change View) &gt; Show Original View"</i>.</summary>
    public const string ShowOriginalViewVerb = "Show Original View";

    /// <summary>
    /// The verbs this application actually has under Change View, in the vendor's own order. Read by the
    /// shell tests, which assert the BUILT column matches this list exactly — so a row added to
    /// <see cref="BuildColumn"/> and not to this list (or the reverse) fails rather than drifting.
    /// </summary>
    public static readonly IReadOnlyList<string> OfferedVerbs =
        new[] { SavedViewsVerb, DeleteSavedViewsVerb, ShowOriginalViewVerb };

    /// <summary>
    /// 🔴 THE HONEST DISCLOSURE, and the vendor page was re-opened by content to confirm the gap is real rather
    /// than assumed. help.tallysolutions.com/use-save-view-feature-in-tallyprime/ lets the operator tick
    /// <i>"Set this as default view for the report"</i> when saving, and manage it afterwards through
    /// <i>"Set/Alter Default View"</i> — a saved view designated to open automatically. This build has no
    /// default-view concept at all: a view is applied when the operator chooses it and never otherwise, so
    /// neither the tick nor the management row has anything to act on. A row that opened a "not available" message
    /// would be worse than no row (the standing rule this menu inherits from <see cref="CompanyMenu"/>), so the
    /// capability is NAMED here instead. Length-budgeted: a cascade header is drawn uppercase at LetterSpacing
    /// 1.5, ~12.5px per character against ~350px of column, so this must stay short enough to wrap to two lines.
    /// </summary>
    public const string Disclosure = "No default view on this build";

    /// <summary>Builds the Ctrl+H column.</summary>
    public static GatewayColumn BuildColumn(Action savedViews, Action deleteSavedViews, Action showOriginalView)
    {
        if (savedViews is null) throw new ArgumentNullException(nameof(savedViews));
        if (deleteSavedViews is null) throw new ArgumentNullException(nameof(deleteSavedViews));
        if (showOriginalView is null) throw new ArgumentNullException(nameof(showOriginalView));

        var column = new GatewayColumn(ColumnTitle);

        // No row carries a chord hint: the vendor documents these as Ctrl+H ROWS, not as shortcuts of their own,
        // and an invented shortcut wearing an attested-looking hint is worse than no hint (CompanyMenu's rule).
        column.Add(new MenuItemViewModel(SavedViewsVerb, savedViews, string.Empty, kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel(
            DeleteSavedViewsVerb, deleteSavedViews, string.Empty, kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel(
            ShowOriginalViewVerb, showOriginalView, string.Empty, kind: MenuItemKind.Action));

        column.Add(MenuItemViewModel.Header(Disclosure));
        return column;
    }

    /// <summary>The verbs the built column actually offers — what the "only verbs we have" tests read.</summary>
    public static IReadOnlyList<string> VerbsOf(GatewayColumn column) =>
        column is null
            ? Array.Empty<string>()
            : column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
}

/// <summary>
/// <b>Alt+P — the print menu</b> (census rows 12.1 / 12.6). Vendor, verbatim
/// (help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/): <c>Alt+P</c> — <i>"To open the print menu for
/// printing transactions or reports."</i>, listed against <c>Ctrl+P</c> — <i>"To print the current voucher or
/// report"</i> as its pair.
///
/// <para>The row names come from the vendor's own print page
/// (help.tallysolutions.com/print-invoices-reports/), which reaches multi-account printing as <b>Alt+P &gt;
/// Others</b> and the print knobs as <b>Configuration</b>.</para>
/// </summary>
public static class ReportPrintMenu
{
    /// <summary>The column header.</summary>
    public const string ColumnTitle = "Print";

    /// <summary>
    /// <b>Current</b> — and this label is ATTESTED, not inferred. This doc-comment previously justified it only
    /// as "the vendor's pairing: Alt+P's first row is what Ctrl+P does on its own", which is an inference; the
    /// page was re-opened by content and it does better than that. help.tallysolutions.com/print-invoices-reports/
    /// writes the chord with this exact word attached, repeatedly: <i>"Open the invoice or report you want to
    /// print and press <b>Ctrl+P</b> (Current)."</i>, and again <i>"press <b>Ctrl+P</b> (Current) &gt; <b>C</b>
    /// (Configure)"</i>. So <b>Current</b> is the vendor's own name for this verb, exactly as it is on the export
    /// side — <i>"Open any voucher or report and press <b>Ctrl+E</b> (Current)."</i>, which is on
    /// help.tallysolutions.com/<b>export-data-in-tally</b>/ and <b>NOT</b> on the print page this paragraph is
    /// otherwise quoting (re-checked by content 2026-09-23; naming the page is the point, because the previous
    /// wording left a reader to assume the print page carried it) — and the hint this row carries is the chord the vendor attaches it
    /// to. <b>Do not weaken this back to an inference, and do not widen it into a claim that the vendor prints
    /// the word as a ROW of the Alt+P menu — it does not; what is attested is the verb's name.</b>
    /// </summary>
    public const string CurrentVerb = "Current";

    /// <summary>Vendor row label, verbatim (help.tallysolutions.com/print-invoices-reports/, <i>Print
    /// Multi-Account Reports</i>): <i>"Press <b>Alt+P</b> (Print) &gt; <b>Others</b>."</i></summary>
    public const string OthersVerb = "Others";

    /// <summary>The verbs this menu offers, in order. Read by the shell tests.</summary>
    public static readonly IReadOnlyList<string> OfferedVerbs = new[] { CurrentVerb, OthersVerb };

    /// <summary>
    /// 🔴 THE HONEST DISCLOSURE, and it names TWO withheld rows because the vendor's menu has three.
    /// help.tallysolutions.com/print-invoices-reports/ was re-opened by content for this slice and its Alt+P
    /// menu carries <b>Configuration</b>, <b>All Tiles</b> and <b>Others</b>; this build offers Current and
    /// Others, so two things are missing and only one of them used to be admitted here.
    /// <para>• <b>Configuration</b> opens the print knobs directly from the menu. In this build those knobs live
    /// on the preview's own F12 panel (<see cref="MainWindowViewModel.OpenPrintConfig"/>) and <b>cannot be opened
    /// before a preview exists</b> — the panel is constructed from a live <c>PrintPreviewViewModel</c>. The route
    /// exists, one step later; the disclosure says where.</para>
    /// <para>• <b>All Tiles</b> prints every tile of the dashboard in one job (<i>"To print a specific tile in
    /// Dashboard, press <b>Ctrl+P</b> (Current)"</i> is the single-tile half the same page documents). This build
    /// HAS a dashboard with tiles (<see cref="DashboardViewModel.Tiles"/>) and has NO print route to any of them:
    /// <see cref="MainWindowViewModel.IsPrintablePage"/> needs a report, a drilled voucher or a master list, and
    /// the dashboard is none of the three, so Alt+P does not even open over it. That is a real gap, it is not
    /// this slice's to close, and it is named rather than left for a later reader to discover.</para>
    /// </summary>
    public const string Disclosure = "Configuration is F12 on the preview - no All Tiles";

    /// <summary>Builds the Alt+P column.</summary>
    public static GatewayColumn BuildColumn(Action current, Action others)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (others is null) throw new ArgumentNullException(nameof(others));

        var column = new GatewayColumn(ColumnTitle);

        // "Current" carries Ctrl+P because the vendor DOES document that chord for exactly this verb — this is
        // the one hint in these four menus that is attested, and it is the pairing the shortcut table states.
        column.Add(new MenuItemViewModel(CurrentVerb, current, "Ctrl+P", kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel(OthersVerb, others, string.Empty, kind: MenuItemKind.Action));

        column.Add(MenuItemViewModel.Header(Disclosure));
        return column;
    }

    /// <summary>The verbs the built column actually offers.</summary>
    public static IReadOnlyList<string> VerbsOf(GatewayColumn column) =>
        column is null
            ? Array.Empty<string>()
            : column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
}

/// <summary>
/// <b>Alt+E — the export menu</b> (census row 13.5). Vendor, verbatim
/// (help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/): <c>Alt+E</c> — <i>"To open the export menu
/// for exporting masters, transactions, or reports"</i>, paired with <c>Ctrl+E</c> — <i>"To export the current
/// voucher or report"</i>.
///
/// <para>🔴 <b>THE VENDOR'S OWN PAGES DISAGREE ABOUT THIS PAIR, AND THE DISAGREEMENT IS RECORDED RATHER THAN
/// PAPERED OVER.</b> help.tallysolutions.com/tally-prime/reports/difference-btw-alte-and-ctrle-tally/ says the
/// opposite of the shortcut table: there, Alt+E is the general export "available in all the transactions and
/// reports" and Ctrl+E is the narrow statutory-return e-format export. That page is written for the VAT
/// statutory reports; the shortcut table is the product-wide reference and is the source this project's R7
/// grounding order reaches first for a chord question, so the table is what is implemented. A later agent who
/// finds the VAT page should not "fix" this back without a ruling.</para>
///
/// <para>🔴 <b>THE "Current" ROW LABEL IS ATTESTED, BUT NOT IN THE FORM THIS COMMENT ONCE CLAIMED — CORRECTED
/// AFTER RE-OPENING THE PAGE.</b> It said the label was the vendor's own <i>"Alt+E (Export) &gt; Current"</i>.
/// help.tallysolutions.com/export-data-in-tally/ was re-read by content and does not contain that string. What
/// it contains is <i>"Open any voucher or report and press <b>Ctrl+E</b> (Current)."</i> — so <b>Current</b> is
/// the vendor's own word for this verb, and it is the word the vendor attaches to <b>Ctrl+E</b>, which is
/// exactly the chord this row carries as its hint. The same page shows the menu form as
/// <i>"Press <b>Alt+E</b> (Export) &gt; <b>Configuration</b>"</i>. The row label stands; only the quotation
/// that justified it was wrong, and a misquote in a comment is how the E/P/M invariant next door shipped
/// false. Do not re-widen this back into a verbatim claim without re-opening the page.</para>
/// </summary>
public static class ReportExportMenu
{
    /// <summary>The column header.</summary>
    public const string ColumnTitle = "Export";

    /// <summary>The vendor's own word for this verb: <i>"press <b>Ctrl+E</b> (Current)"</i>
    /// (help.tallysolutions.com/export-data-in-tally/) — the chord this row carries as its hint.</summary>
    public const string CurrentVerb = "Current";

    /// <summary>The verbs this menu offers. Read by the shell tests.</summary>
    public static readonly IReadOnlyList<string> OfferedVerbs = new[] { CurrentVerb };

    /// <summary>
    /// 🔴 THE HONEST DISCLOSURE, and it is the whole reason this menu is one row long. It names THREE withheld
    /// rows, because the vendor's export menu has four and this build has one —
    /// help.tallysolutions.com/export-data-in-tally/ was re-opened by content for this correction and shows all
    /// three beside the <b>Current</b> row we do have: <i>"Press <b>Alt+E</b> (Export) &gt; select
    /// <b>Masters</b> or <b>Transactions</b>"</i> (the two bulk families) and <i>"Press <b>Alt+E</b> (Export)
    /// &gt; <b>Others</b> &gt; and select any of the report listed above"</i> (a report picker that does not
    /// require standing on the report). This build exports only what is ON SCREEN: <c>BuildExportPanel</c> reads
    /// the top cascade column or the live report and nothing else, so none of the three has anything to route
    /// to. Census row 13.5 keeps the gap; a row that opened an empty picker would hide it.
    ///
    /// <para>🔴 <b>The string previously named only the master half</b> ("Bulk master export is not on this
    /// build") — accurate as far as it went, and therefore the more dangerous kind of disclosure: a reader
    /// checking the menu against the vendor page would have concluded the other two rows were considered and
    /// found present. They were not considered. Two-line budget as for every cascade header.</para>
    /// </summary>
    public const string Disclosure = "No bulk Masters, Transactions or Others";

    /// <summary>Builds the Alt+E column.</summary>
    public static GatewayColumn BuildColumn(Action current)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));

        var column = new GatewayColumn(ColumnTitle);
        column.Add(new MenuItemViewModel(CurrentVerb, current, "Ctrl+E", kind: MenuItemKind.Action));
        column.Add(MenuItemViewModel.Header(Disclosure));
        return column;
    }

    /// <summary>The verbs the built column actually offers.</summary>
    public static IReadOnlyList<string> VerbsOf(GatewayColumn column) =>
        column is null
            ? Array.Empty<string>()
            : column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
}

/// <summary>
/// <b>Alt+M — the Share menu</b> (census row 14.10 / 13.7). Vendor, verbatim
/// (help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/): <c>Alt+M</c> — <i>"To open the Share menu for
/// sharing transactions or reports through e-mail or WhatsApp"</i>, paired with <c>Ctrl+M</c> — <i>"To e-mail the
/// current voucher or report"</i>.
///
/// <para>🔴 <b>THIS MENU IS ALSO THE ANSWER TO IV-64.</b> <c>docs/invented-vs-cloned.md</c> IV-64 records that
/// this application put WhatsApp on its own invented <c>W</c> chord, and that the vendor instead nests WhatsApp
/// UNDER this Alt+M access point beside e-mail — IV-64's own recommended route. Both channels now hang off the
/// vendor's chord. <b>The W chord is NOT removed</b>: it is a shipped door an operator may already use, removing
/// it would be a regression for a fidelity gain the menu already delivers, and IV-64 asks for the vendor route
/// to EXIST, not for the extra one to be deleted.</para>
/// </summary>
public static class ReportShareMenu
{
    /// <summary>The column header — the vendor's own word for this menu.</summary>
    public const string ColumnTitle = "Share";

    /// <summary>The e-mail channel. The vendor's Alt+M text names it as one of the two.</summary>
    public const string EmailVerb = "E-Mail";

    /// <summary>The WhatsApp channel. The vendor's Alt+M text names it as the other.</summary>
    public const string WhatsAppVerb = "WhatsApp";

    /// <summary>The verbs this menu offers, in the order the vendor's sentence names them.</summary>
    public static readonly IReadOnlyList<string> OfferedVerbs = new[] { EmailVerb, WhatsAppVerb };

    /// <summary>
    /// 🔴 THE HONEST DISCLOSURE, and it names TWO things because this menu falls short of the vendor's in two
    /// different ways.
    /// <para>• <b>Nothing is sent.</b> The same statement both panels already carry on their own status lines:
    /// E-Mail writes an <c>.eml</c> or opens a <c>mailto:</c> draft; WhatsApp hands over a prepared link. There
    /// is no SMTP socket and no WhatsApp Business API in this build — a SOURCED limit, not a shortcut:
    /// help.tallysolutions.com/share-documents-using-whatsapp-for-business-faq/ requires WhatsApp Business API
    /// on-boarding through its own BSP — <i>"It is mandatory for you to be onboarded to a WhatsApp Business API
    /// account with Interakt as Tally has tied up with the BSP"</i> — and states of the number itself: <i>"Using a
    /// number that is already in use for an existing WhatsApp Business Account (Business App) for WhatsApp Business
    /// API on-boarding is <b>not allowed by Meta</b>."</i> (re-opened by content 2026-09-24). 🔴 <b>QUOTED IN THE
    /// VENDOR'S OWN WORDS ON PURPOSE.</b> This sentence previously read "Meta forbids re-using a number …", which
    /// is one attribution hop loose: it turns a vendor's report of a third party's rule into a direct claim about
    /// that third party. The rule is cited as the vendor states it, and is not restated as ours. The statement is
    /// repeated at the menu because the menu is now the first thing the operator sees.</para>
    /// <para>• 🔴 <b>No <i>Others</i> row, and that was previously undisclosed.</b> The vendor's Share menu is
    /// not two rows: help.tallysolutions.com/configure-for-print-export-share/ documents
    /// <i>"For WhatsApp: <b>Alt+M</b> (Share) &gt; <b>Others</b> under <b>WHATSAPP</b> &gt; select a report from
    /// the <b>List of Reports</b>"</i> — a picker that shares a report WITHOUT standing on it, the share-side
    /// twin of <c>ReportPrintMenu.OthersVerb</c>. This build shares only what is on screen (see
    /// <see cref="MainWindowViewModel.IsShareablePage"/>: a drilled voucher or the live report, nothing else),
    /// so there is nothing to route a picker to. Census row 13.7 already records the row as owed; the menu now
    /// says so on its own face instead of leaving a reader to infer it was considered.</para>
    /// <para>Two-line budget as for every cascade header.</para>
    /// </summary>
    public const string Disclosure = "Prepares only - nothing sent, and no Others";

    /// <summary>Builds the Alt+M column.</summary>
    public static GatewayColumn BuildColumn(Action email, Action whatsApp)
    {
        if (email is null) throw new ArgumentNullException(nameof(email));
        if (whatsApp is null) throw new ArgumentNullException(nameof(whatsApp));

        var column = new GatewayColumn(ColumnTitle);
        column.Add(new MenuItemViewModel(EmailVerb, email, "Ctrl+M", kind: MenuItemKind.Action));
        column.Add(new MenuItemViewModel(WhatsAppVerb, whatsApp, string.Empty, kind: MenuItemKind.Action));
        column.Add(MenuItemViewModel.Header(Disclosure));
        return column;
    }

    /// <summary>The verbs the built column actually offers.</summary>
    public static IReadOnlyList<string> VerbsOf(GatewayColumn column) =>
        column is null
            ? Array.Empty<string>()
            : column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
}
