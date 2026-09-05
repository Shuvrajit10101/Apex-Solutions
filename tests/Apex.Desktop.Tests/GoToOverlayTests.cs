using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-14 (census 14.1) — <b>Go To (Alt+G)</b>, the jump-anywhere overlay.
///
/// <para><b>Vendor grounding (help.tallysolutions.com, fetched 2026-09-05).</b> The shortcut table gives
/// <b>Alt+G</b> — <i>"To primarily open a report, and create masters and vouchers in the flow of work."</i>
/// The feature page adds that Go To <i>"lists all the reports by default in different groups under a common
/// selection table"</i>, that you <i>"simply type a report name and search the report, without having to move
/// out of the screen you have already opened"</i>, and that it also reaches master creation
/// (<i>"Alt+G (Go To) &gt; Create Master &gt; Voucher Type"</i>).</para>
///
/// <para><b>Explicitly NOT built here:</b> Ctrl+G ("Switch To"). That is census row 14.2, which sits behind an
/// open ruling on the multi-company shell; this slice takes Alt+G only.</para>
///
/// <para><b>The anti-drift property this suite exists to pin.</b> The index is not a hand-written list of
/// destinations — it is built by WALKING the real Gateway menu, and Enter REPLAYS the real cascade navigation.
/// A hand-written list would be a second copy of a ~180-case dispatch and would rot the first time a menu row
/// moved. The tests below assert the walk and the replay, not a literal table.</para>
/// </summary>
public sealed class GoToOverlayTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GoToOverlayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGoToTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    /// <summary>Highlights the first result whose label is exactly <paramref name="label"/>.</summary>
    private static void Highlight(GoToViewModel g, string label)
    {
        for (var i = 0; i < g.Results.Count; i++)
            if (g.Results[i].Label == label) { g.SelectedIndex = i; return; }
        Assert.Fail($"'{label}' is not in the Go To results (have: " +
                    string.Join(", ", g.Results.Take(15).Select(r => r.Label)) + ")");
    }

    // ------------------------------------------------------------------ (1) the overlay opens and is populated

    [Fact]
    public void Go_To_opens_with_the_whole_menu_indexed()
    {
        var vm = NewSeededCompany("GoTo Open Co");

        Assert.False(vm.IsGoToOpen);
        vm.OpenGoTo();

        Assert.True(vm.IsGoToOpen);
        Assert.NotNull(vm.GoTo);
        var g = vm.GoTo!;

        // Destinations the operator would otherwise have to walk three columns to reach.
        Assert.Contains(g.Results, r => r.Label == "Balance Sheet");
        Assert.Contains(g.Results, r => r.Label == "Receivables");
        Assert.Contains(g.Results, r => r.Label == "Godown");
        // W2-20's own grid is reachable through it too, which is the point of building the index from the menu.
        Assert.Contains(g.Results, r => r.Label == "Multi Ledger");
    }

    /// <summary>
    /// Every destination is filed under its parent section — never a flat dump (the UI contract). "Receivables"
    /// lives three columns deep, so its breadcrumb must name every column above it.
    /// </summary>
    [Fact]
    public void Every_destination_carries_its_parent_section_breadcrumb()
    {
        var vm = NewSeededCompany("GoTo Section Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;

        Assert.All(g.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.Section)));

        Assert.Equal("Reports", g.Results.First(r => r.Label == "Balance Sheet").Section);
        Assert.Equal("Reports → Statements of Accounts → Outstandings",
                     g.Results.First(r => r.Label == "Receivables").Section);
        Assert.Equal("Masters → Create → Inventory Masters",
                     g.Results.First(r => r.Label == "Godown").Section);
    }

    /// <summary>
    /// 🔴 The index must NOT enumerate company data. The Account-Books pickers are data-driven columns whose
    /// every row is a LEDGER NAME; walking into them would fill Go To with one entry per ledger and bury the
    /// reports the feature exists to find. "Cash" is a seeded ledger, so its presence would prove the leak.
    /// </summary>
    [Fact]
    public void The_index_stops_at_data_driven_pickers_and_never_lists_ledgers()
    {
        var vm = NewSeededCompany("GoTo Data Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;

        Assert.NotNull(vm.Company!.FindLedgerByName("Cash"));      // the leak's marker really is seeded
        Assert.DoesNotContain(g.Results, r => r.Label == "Cash");

        // The picker itself is still a destination — you can jump TO the Cash Book, you just do not get one
        // Go To row per ledger inside it.
        Assert.Contains(g.Results, r => r.Label == "Cash Book");
    }

    // ------------------------------------------------------------------ (2) typing filters and ranks

    [Fact]
    public void Typing_filters_the_list_and_a_prefix_match_ranks_above_a_mid_word_one()
    {
        var vm = NewSeededCompany("GoTo Filter Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;
        var all = g.Results.Count;

        g.SearchText = "bal";
        Assert.True(g.Results.Count < all);
        Assert.Contains(g.Results, r => r.Label == "Balance Sheet");
        Assert.DoesNotContain(g.Results, r => r.Label == "Godown");

        // "Balance Sheet" starts with the text; "Trial Balance" merely contains it. Prefix wins.
        g.SearchText = "balance";
        var labels = g.Results.Select(r => r.Label).ToList();
        Assert.Contains("Balance Sheet", labels);
        Assert.Contains("Trial Balance", labels);
        Assert.True(labels.IndexOf("Balance Sheet") < labels.IndexOf("Trial Balance"),
                    $"prefix match must rank first; got [{string.Join(", ", labels)}]");

        // Clearing the box restores the whole index.
        g.SearchText = string.Empty;
        Assert.Equal(all, g.Results.Count);
    }

    /// <summary>The section is searchable too, so "outstand" finds the two reports filed under it.</summary>
    [Fact]
    public void The_section_breadcrumb_is_searchable()
    {
        var vm = NewSeededCompany("GoTo Breadcrumb Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;

        g.SearchText = "outstandings";
        Assert.Contains(g.Results, r => r.Label == "Receivables");
        Assert.Contains(g.Results, r => r.Label == "Payables");
    }

    /// <summary>A search that matches nothing leaves an empty list and says so, rather than showing everything.</summary>
    [Fact]
    public void A_search_that_matches_nothing_yields_an_empty_list()
    {
        var vm = NewSeededCompany("GoTo Nomatch Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;

        g.SearchText = "zzzz-no-such-report";
        Assert.Empty(g.Results);
        Assert.Equal(-1, g.SelectedIndex);
    }

    // ------------------------------------------------------------------ (3) Enter actually jumps

    /// <summary>
    /// The reachability standard: Alt+G → type → Enter must land on the real screen. "Receivables" sits three
    /// columns deep (Reports → Statements of Accounts → Outstandings), so a jump that works proves the replay
    /// drives the whole cascade, not just the root.
    /// </summary>
    [Fact]
    public void Enter_jumps_to_a_report_three_columns_deep()
    {
        var vm = NewSeededCompany("GoTo Jump Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;

        g.SearchText = "receiv";
        Highlight(g, "Receivables");
        Assert.True(vm.ActivateGoTo());

        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        Assert.NotNull(vm.Outstandings);
        Assert.False(vm.IsGoToOpen);                                // the overlay closes behind the jump

        // The cascade really was rebuilt down the path — the operator can walk back up it.
        Assert.Equal(4, vm.Columns.Count);                          // root · SoA · Outstandings · page
        Assert.Contains(vm.Columns, c => c.Title == "Outstandings");
    }

    /// <summary>Go To reaches master CREATION too, exactly as the vendor's "Go To &gt; Create Master" flow does.</summary>
    [Fact]
    public void Enter_jumps_to_a_master_creation_screen()
    {
        var vm = NewSeededCompany("GoTo Master Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;

        g.SearchText = "godown";
        Highlight(g, "Godown");
        Assert.True(vm.ActivateGoTo());

        Assert.Equal(Screen.GodownMaster, vm.CurrentScreen);
        Assert.NotNull(vm.GodownMaster);
    }

    /// <summary>
    /// 🔴 The ambiguous-label case. "Batch" is a PAGE (the batch master) under Create and a GROUP (the batch
    /// report family) under Inventory Reports. The index stores an ancestor PATH, not a bare label, so the two
    /// resolve independently — and the reports under the group flavour are indexed under it.
    /// </summary>
    [Fact]
    public void Ambiguous_labels_resolve_by_their_stored_path()
    {
        var vm = NewSeededCompany("GoTo Ambiguous Co");
        vm.Company!.MaintainBatchwiseDetails = true;                // surfaces both flavours
        vm.ShowGateway();
        vm.OpenGoTo();
        var g = vm.GoTo!;

        // The Create-column Batch MASTER.
        var master = g.Results.Single(r => r.Label == "Batch" && r.Section.Contains("Create"));
        // The Inventory-Reports Batch GROUP's children, which only an ancestor-aware walk can reach.
        Assert.Contains(g.Results, r => r.Label == "Batch-wise" && r.Section.Contains("Inventory Reports"));
        Assert.Contains(g.Results, r => r.Label == "Age Analysis" && r.Section.Contains("Inventory Reports"));

        Highlight(g, master.Label);
        // Highlight() takes the FIRST "Batch"; pin the intended one explicitly so the assertion is honest.
        g.SelectedIndex = g.Results.IndexOf(master);
        Assert.True(vm.ActivateGoTo());
        Assert.Equal(Screen.BatchMaster, vm.CurrentScreen);
    }

    /// <summary>Go To works from a page column, which is the whole point — "without moving out of the screen".</summary>
    [Fact]
    public void Go_To_jumps_from_one_open_report_to_another()
    {
        var vm = NewSeededCompany("GoTo FromPage Co");

        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        vm.OpenGoTo();
        var g = vm.GoTo!;
        g.SearchText = "trial";
        Highlight(g, "Trial Balance");
        Assert.True(vm.ActivateGoTo());

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind);
    }

    // ------------------------------------------------------------------ (4) Escape, and no accidental jumps

    [Fact]
    public void Escape_closes_the_overlay_without_navigating()
    {
        var vm = NewSeededCompany("GoTo Escape Co");
        vm.OpenReport(ReportKind.BalanceSheet);

        vm.OpenGoTo();
        vm.GoTo!.SearchText = "trial";
        vm.CloseGoTo();

        Assert.False(vm.IsGoToOpen);
        Assert.Null(vm.GoTo);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);   // did NOT jump
    }

    /// <summary>Enter with nothing highlighted is a quiet no-op, not a jump to whatever happened to be first.</summary>
    [Fact]
    public void Enter_with_no_match_does_not_navigate()
    {
        var vm = NewSeededCompany("GoTo NoJump Co");
        vm.OpenGoTo();
        vm.GoTo!.SearchText = "zzzz-no-such-report";

        Assert.False(vm.ActivateGoTo());
        Assert.True(vm.IsGoToOpen);                                 // stays open so the operator can retype
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    /// <summary>Arrowing moves the highlight and wraps, so the list is drivable with no pointer at all.</summary>
    [Fact]
    public void The_result_list_is_arrow_navigable_and_wraps()
    {
        var vm = NewSeededCompany("GoTo Arrows Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;
        g.SearchText = "balance";
        var n = g.Results.Count;
        Assert.True(n >= 2);

        Assert.Equal(0, g.SelectedIndex);                           // first result pre-highlighted
        g.MoveDown();
        Assert.Equal(1, g.SelectedIndex);
        for (var i = 1; i < n; i++) g.MoveDown();
        Assert.Equal(0, g.SelectedIndex);                           // wrapped
        g.MoveUp();
        Assert.Equal(n - 1, g.SelectedIndex);                       // wrapped the other way
    }

    /// <summary>Retyping resets the highlight to the top, so Enter can never fire on a stale row.</summary>
    [Fact]
    public void Changing_the_search_resets_the_highlight_to_the_first_result()
    {
        var vm = NewSeededCompany("GoTo Reset Co");
        vm.OpenGoTo();
        var g = vm.GoTo!;
        g.SearchText = "balance";
        g.MoveDown();
        Assert.Equal(1, g.SelectedIndex);

        g.SearchText = "godown";
        Assert.Equal(0, g.SelectedIndex);
    }

    /// <summary>Go To is a company-scoped feature — there is nothing to jump to before a company is open.</summary>
    [Fact]
    public void Go_To_does_not_open_before_a_company_is_open()
    {
        var vm = new MainWindowViewModel(_storage);
        Assert.NotEqual(Screen.Gateway, vm.CurrentScreen);

        vm.OpenGoTo();
        Assert.False(vm.IsGoToOpen);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }
}
