using System;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W2-32 / census row 12.6 — multi-account printing must be REACHABLE, not merely written.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> <c>MultiAccountPrintProjector</c> and
/// <c>MultiAccountPrintViewModel</c> landed as ~432 lines with <b>zero references</b>: no shell member, no
/// menu route, no XAML template, no test. The integrator refused row 12.6 on that basis and filed it as
/// <c>T2-40</c>. It is the third instance of this project's most repeated defect — careful, correct-looking,
/// unreachable code counted as delivered (<c>CompanyStorage.Rename()</c>,
/// <c>CostReports.BuildLedgerBreakup</c>).</para>
///
/// <para>These tests therefore never construct <c>MultiAccountPrintViewModel</c> themselves. Every one of them
/// starts at <see cref="MainWindowViewModel"/> and walks the route an operator walks: the menu entry, the
/// opener, the selection, the print. A test that news up the view model would pass over dead code, which is
/// exactly the failure being guarded against.</para>
/// </summary>
public sealed class MultiAccountPrintReachabilityTests
{
    private const string MenuLabel = "Multi-Account Printing";

    private static MainWindowViewModel Shell(out string tempDir)
    {
        tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiPrint_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.LoadRobertDemo();
        return vm;
    }

    /// <summary>
    /// Walks the operator's own route: open the hub, highlight the leaf, press Enter. Never calls the opener
    /// directly — a test that did would pass over an entry no menu carries.
    /// </summary>
    private static void OpenPanelThroughTheMenu(MainWindowViewModel vm)
    {
        vm.ShowStatementsOfAccountsMenu();
        var hub = vm.Columns[^1];
        bool found = false;
        for (var i = 0; i < hub.Items.Count; i++)
            if (hub.Items[i].IsSelectable && hub.Items[i].Label == MenuLabel)
            {
                hub.SetSelected(i);
                found = true;
                break;
            }
        Assert.True(found, $"Reports → Statements of Accounts carries no \"{MenuLabel}\" item — the panel is unreachable.");
        vm.DrillIn();
    }

    /// <summary>
    /// <b>THE MENU ASSERTION.</b> The entry is nested under Reports → Statements of Accounts — the UI contract
    /// forbids a flat dump — and it is NOT at the gateway root.
    /// </summary>
    [Fact]
    public void The_entry_is_nested_under_statements_of_accounts_and_not_at_the_root()
    {
        var vm = Shell(out _);

        vm.ShowStatementsOfAccountsMenu();
        var hub = vm.Columns[^1];
        Assert.True(hub.IsMenu);
        Assert.Contains(hub.Items, m => m.IsSelectable && m.Label == MenuLabel && m.IsPage);

        // The parent-section rule: it must not also appear as a root-level item.
        vm.ShowGateway();
        Assert.DoesNotContain(vm.Columns[0].Items, m => m.Label == MenuLabel);
    }

    /// <summary>
    /// <b>THE OPENER ASSERTION.</b> Selecting the menu entry — by its LABEL, through the same dispatch an
    /// operator's Enter key reaches — puts the panel on screen as a page column with the shell's own member set.
    /// </summary>
    [Fact]
    public void Selecting_the_menu_entry_opens_the_panel_as_a_page_column()
    {
        var vm = Shell(out _);

        OpenPanelThroughTheMenu(vm);

        Assert.Equal(Screen.MultiAccountPrint, vm.CurrentScreen);
        Assert.NotNull(vm.MultiAccountPrint);
        Assert.True(vm.Columns[^1].IsPage, "the panel must open as a page column to the right of the cascade");
        Assert.NotEmpty(vm.MultiAccountPrint!.Accounts);
    }

    /// <summary>
    /// <b>THE END-TO-END ASSERTION.</b> Select two accounts, print, and a print preview opens over a document
    /// SET. The preview's page count must exceed a single account's, because each document starts a fresh sheet
    /// — that is what makes this a multi-account job rather than one report with a longer title.
    /// </summary>
    [Fact]
    public void Printing_the_selected_accounts_opens_a_preview_over_the_whole_set()
    {
        var vm = Shell(out _);
        OpenPanelThroughTheMenu(vm);

        var panel = vm.MultiAccountPrint!;
        Assert.True(panel.Accounts.Count >= 2, "the Robert fixture must offer at least two accounts to print");
        panel.Accounts[0].IsSelected = true;
        panel.Accounts[1].IsSelected = true;
        Assert.Equal(2, panel.SelectedCount);

        vm.PrintMultiAccountJob();

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        var preview = vm.PrintPreview;
        Assert.NotNull(preview);
        Assert.NotEmpty(preview!.PdfBytes);
        // Two documents, each opening a fresh sheet ⇒ at least two preview pages.
        Assert.True(preview.PageCount >= 2,
            $"a two-account job previewed as {preview.PageCount} page(s); each document must start a fresh sheet");
    }

    /// <summary>
    /// <b>THE REFUSAL.</b> A job of nothing opens no preview and says why. Printing an empty selection as a
    /// blank sheet would be a mistake reported as output.
    /// </summary>
    [Fact]
    public void Printing_with_nothing_selected_opens_no_preview_and_states_the_reason()
    {
        var vm = Shell(out _);
        OpenPanelThroughTheMenu(vm);

        vm.PrintMultiAccountJob();

        Assert.Equal(Screen.MultiAccountPrint, vm.CurrentScreen);
        Assert.Null(vm.PrintPreview);
        Assert.Contains("Select at least one account", vm.MultiAccountPrint!.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>THE DOCUMENT-KIND ASSERTION (census 12.7).</b> The reminder letter and the confirmation of accounts
    /// are reached from this panel — they are multi-account OUTPUTS, not standalone documents (census §1.3
    /// item 22). Switching the kind changes the printed heading, so each is genuinely reachable.
    /// </summary>
    [Fact]
    public void The_panel_reaches_the_reminder_letter_and_the_confirmation_of_accounts()
    {
        var vm = Shell(out _);
        OpenPanelThroughTheMenu(vm);

        var panel = vm.MultiAccountPrint!;
        panel.Accounts[0].IsSelected = true;

        panel.DocumentKind = MultiAccountDocumentKind.ReminderLetter;
        var letters = panel.BuildJob();
        Assert.Single(letters);
        Assert.Equal("Reminder Letter", letters[0].Title);

        panel.DocumentKind = MultiAccountDocumentKind.ConfirmationOfAccounts;
        var confirmations = panel.BuildJob();
        Assert.Single(confirmations);
        Assert.Equal("Confirmation of Accounts", confirmations[0].Title);
    }
}
