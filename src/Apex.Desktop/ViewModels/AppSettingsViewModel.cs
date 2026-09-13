using System;
using System.Collections.ObjectModel;
using Apex.Ledger;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// Census row 1.8 — the <b>F1 (Help) &gt; Settings</b> application-wide configuration surface, hosted as its own
/// cascading Miller column exactly like the F12 report-config panel beside it.
///
/// <para>🔴 <b>This is F1, and it is NOT a resurrected F12 tree (user ruling 21).</b> The reference product
/// REMOVED the global configuration menu — vendor verbatim, re-fetched for this slice from
/// <c>help.tallysolutions.com/developer-reference/release-notes-whats-new-in-tdl/working-of-tally-erp-9-customisations-with-tallyprime/</c>:
/// <i>"Button F12 has been removed from Menu context."</i>; <i>"The menu 'Configuration' has been removed. The
/// related configurations have been added under the respective features."</i>; <i>"With the enhanced Popup Menu
/// capability, the items in the General Configuration have now been placed in the Popup Menu, F1: Help invoked
/// from the top buttons (F1: Help &gt; Settings)."</i> Building the old tree would have invented a Tally.ERP 9
/// artefact into a TallyPrime clone, which is why two earlier tracks declined to build this row as it was then
/// titled. F12 stays CONTEXTUAL and is untouched by this slice.</para>
///
/// <para><b>The three-way distinction this page sits inside.</b> F11 = company features (what this book does,
/// already shipped on <see cref="GstConfigViewModel"/>); F12 = per-screen Configure, context-sensitive; F1 =
/// this, the application layer that is not scoped to a company at all.</para>
///
/// <para>🔴 <b>Every switch on this page gates real, rendered behaviour, and only switches that do are here.</b>
/// The vendor's Settings popup also carries <i>Startup</i> (load companies on startup, auto-login),
/// <i>Language</i> (display / data-entry language), <i>Connectivity</i> (client-server) and <i>Licence</i>
/// groups, and a <i>Display &gt; Colour &amp; Sound &gt; Display Mode</i> (Bright / Soft) and <i>Enable Stripe
/// View for Reports</i>. NONE of those is shipped here, deliberately: this application has no startup
/// company-loading step, no second language, no client/server mode, no licence, no theme and no report row
/// striping, so each of those switches could only ever be a knob with nothing behind it — which is worse than an
/// honestly absent row. They are reported as absent rather than mocked.</para>
/// </summary>
public sealed partial class AppSettingsViewModel : ViewModelBase
{
    private readonly Action<bool> _applyBottomBar;

    /// <summary>The column title / heading. "Settings" is the vendor's own caption for this popup item.</summary>
    public string Title => "Settings — F1 (Help)";

    /// <summary>
    /// The one-line honesty notice the page always shows. The setting store is IN-MEMORY for the session (this
    /// slice carries no schema budget), so saying nothing would let the operator believe a restart keeps it.
    /// </summary>
    public string Notice =>
        "These settings apply to the whole application, not to one company. They last for this session — "
      + "they are not saved when Apex Solutions is closed.";

    // ------------------------------------------------------------------ Display

    /// <summary>
    /// <b>Display &gt; Show bottom bar.</b> Vendor: <i>"You can set Show bottom bar to No if you need to disable
    /// the bottom bar to increase viewing space on your screen."</i>
    ///
    /// <para><b>What it gates HERE:</b> the window's status strip (the "Company: … Current Date: …" navy bar at
    /// the foot of <c>MainWindow.axaml</c>). It does NOT hide the notice line or the Accept? (Y/N) prompt that
    /// share that grid row — those are messages the operator must not be able to switch off, and the vendor's
    /// sentence is about reclaiming viewing space, not about silencing prompts.</para>
    /// </summary>
    [ObservableProperty] private bool _showBottomBar = true;

    // ------------------------------------------------------------------ Country → Date and Number Format

    /// <summary>
    /// <b>Country &gt; Date and Number Format &gt; Show Quantity and Number in millions.</b> Vendor: <i>"once the
    /// option is set to yes, you can see the amount as 1,000,000 instead of 10,00,000"</i>, applying <i>"in the
    /// book as well as on cheques"</i>.
    ///
    /// <para><b>What it gates HERE:</b> <see cref="IndianMoneyFormat.ActiveCulture"/> — the ONE grouping rule
    /// every rendered amount and quantity in the application formats through: the report grids and ledger books
    /// (via <c>IndianFormat</c>), the voucher and invoice entry screens, and the printed tax invoice, POS
    /// receipt, voucher, certificates and cheque. One switch, exactly the surfaces the vendor names.</para>
    /// </summary>
    [ObservableProperty] private bool _showAmountsInMillions;

    /// <summary>
    /// A live worked example of what the number switch does, rendered from the vendor's OWN example figure
    /// (1000000 → "1,000,000" vs "10,00,000") so the operator can see the effect before applying it. It is
    /// computed from the two frozen cultures rather than hard-coded, so it cannot drift from the rule it
    /// previews.
    /// </summary>
    public string NumberPreview =>
        "₹ " + 1000000m.ToString(
            "#,##0.00",
            ShowAmountsInMillions ? IndianMoneyFormat.MillionsCulture : IndianMoneyFormat.Culture);

    /// <summary>
    /// Only the PREVIEW follows the tick. 🔴 It must not touch <see cref="AmountDisplay.Grouping"/> — Apply is
    /// the commit point, and a tick that re-grouped the books on its way past would make Escape unable to
    /// abandon the change.
    /// </summary>
    partial void OnShowAmountsInMillionsChanged(bool value) => OnPropertyChanged(nameof(NumberPreview));

    /// <summary>A short status line shown after applying (feedback that the setting took effect).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>
    /// The vendor's Settings groups, listed so the page reads as the vendor's surface rather than as two loose
    /// checkboxes — and so the groups this build does NOT carry are named on screen instead of being silently
    /// missing. <see cref="AppSettingGroup.IsAvailable"/> is false for a group with no behaviour here.
    /// </summary>
    public ObservableCollection<AppSettingGroup> Groups { get; } = new();

    public AppSettingsViewModel(bool showBottomBar, Action<bool> applyBottomBar)
    {
        _applyBottomBar = applyBottomBar ?? throw new ArgumentNullException(nameof(applyBottomBar));

        // Seeded from the LIVE state, so opening the page and applying it with no edits changes nothing.
        ShowBottomBar = showBottomBar;
        ShowAmountsInMillions = AmountDisplay.Grouping == AmountDigitGrouping.Millions;

        Groups.Add(new AppSettingGroup("Display", "Show bottom bar", true));
        Groups.Add(new AppSettingGroup("Country", "Date and Number Format", true));
        Groups.Add(new AppSettingGroup("Startup", "Not available in this build — there is no startup company-loading step", false));
        Groups.Add(new AppSettingGroup("Language", "Not available in this build — the interface ships in one language", false));
        Groups.Add(new AppSettingGroup("Connectivity", "Not available in this build — there is no client/server mode", false));
        Groups.Add(new AppSettingGroup("Licence", "Not available in this build — the application is not licensed", false));
    }

    /// <summary>
    /// Ctrl+A / the Apply button: push both settings into the application and report what changed. Applying with
    /// no edits is a no-op that leaves every rendering exactly as it was.
    /// </summary>
    public void Apply()
    {
        AmountDisplay.Grouping = ShowAmountsInMillions ? AmountDigitGrouping.Millions : AmountDigitGrouping.Indian;
        _applyBottomBar(ShowBottomBar);

        // 🔴 The number half says "re-open" and that is not a hedge. A report projects its cell STRINGS once,
        // when it is built, so a report already on screen keeps the grouping it was built with until it is
        // re-opened. The reference product is blunter about the same constraint — its own Date-and-Number page
        // says the application must be RESTARTED for these to take effect — so saying nothing here would leave
        // the operator staring at an unchanged report believing the switch did nothing.
        Status = "Applied. The bottom bar is " + (ShowBottomBar ? "shown" : "hidden")
               + ". Amounts and quantities now show in "
               + (ShowAmountsInMillions ? "millions" : "lakhs")
               + " — re-open a report to see the change in figures already on screen.";
    }
}

/// <summary>
/// One row in the Settings group list: the vendor's group caption, what it carries here, and whether this build
/// actually has it. A group marked unavailable is shown DIMMED with its reason, never as an operable row —
/// a switch with nothing behind it is the defect this page exists to avoid.
/// </summary>
public sealed class AppSettingGroup
{
    public string Caption { get; }
    public string Detail { get; }
    public bool IsAvailable { get; }
    public bool IsUnavailable => !IsAvailable;

    public AppSettingGroup(string caption, string detail, bool isAvailable)
    {
        Caption = caption;
        Detail = detail;
        IsAvailable = isAvailable;
    }
}
