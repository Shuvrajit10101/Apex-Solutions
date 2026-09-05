using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Views;

public partial class MainWindow : Window
{
    private INotifyCollectionChanged? _watchedColumns;

    /// <summary>
    /// The file/folder chooser this window uses for every data path (census row 13.10 / T1-20). Defaults to the
    /// real OS dialogs; the headless tests swap in a fake, because a real dialog cannot open in a test and a
    /// chooser no test can reach is a chooser nobody can prove is reachable.
    /// </summary>
    internal IFilePathPicker FilePathPicker { get; set; }

    /// <summary>The size declared in XAML, captured before anything can override it — see
    /// <see cref="OnOpened"/>, which only fits the window to the screen if this is still the size in force.</summary>
    private readonly double _xamlWidth;
    private readonly double _xamlHeight;

    public MainWindow()
    {
        InitializeComponent();
        FilePathPicker = new StorageProviderFilePathPicker(this);
        _xamlWidth = Width;
        _xamlHeight = Height;
        // Handle keys at the tunnelling stage so arrow/Enter/Esc work regardless of focus.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => { HookCascadeAutoScroll(); HookWorkingDateFocus(); };
        HookCascadeAutoScroll();
        HookWorkingDateFocus();
    }

    /// <summary>
    /// Fits the DEFAULT window size to the screen's work area on first open.
    /// <para><b>The defect.</b> The XAML opens the shell at 1440x900 DIP with no startup fitting. A
    /// 1366x768 panel has a work area of roughly 1366x720 DIP at 100% scaling, so the app opened ~74 DIP
    /// wider and ~180 DIP taller than the screen could show — the button bar along the bottom edge sat
    /// off-screen on first launch, before DPI scaling enters the picture at all. A window edge past the
    /// screen edge is not scrollable content; it is simply unreachable, so there was no cue and no gesture
    /// that recovered it. (Measured: at 1366x768 @125% the DIP work area is 1092.8x576, which is why
    /// <c>MinWidth</c> also had to come down from 1120 to 1024 — at 1120 the app's own minimum exceeded
    /// the entire desktop width and Avalonia clamped the window LARGER than the screen.)</para>
    /// <para><b>Why it is gated on the XAML default.</b> Any caller that chose an explicit size — every
    /// headless layout test does, at sizes up to 1920x1080 — must keep exactly the size it asked for, or
    /// the whole measurement suite would silently be re-sized by the screen the runner happens to have.
    /// So the fit applies only while <see cref="Window.Width"/>/<see cref="Window.Height"/> still hold the
    /// values captured from XAML in the constructor.</para>
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitDefaultSizeToWorkArea();
    }

    private void FitDefaultSizeToWorkArea()
    {
        try
        {
            var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
            var work = screen?.WorkingArea;
            if (work is not { Width: > 0, Height: > 0 }) return;

            var fitted = FitToWorkArea(
                new Size(Width, Height),
                new Size(_xamlWidth, _xamlHeight),
                // WorkingArea is in PHYSICAL pixels; the window is sized in DIPs. DIP = physical / scale.
                new Size(work.Value.Width, work.Value.Height),
                screen!.Scaling,
                new Size(MinWidth, MinHeight));

            if (fitted.Width < Width) Width = fitted.Width;
            if (fitted.Height < Height) Height = fitted.Height;
        }
        catch
        {
            // No screen information (headless, or a platform that does not report a work area) — keep the
            // declared size. Fitting is an improvement, never a precondition.
        }
    }

    /// <summary>
    /// The whole decision behind <see cref="FitDefaultSizeToWorkArea"/>, as a pure function so it can be
    /// locked by tests. A headless test platform reports no screen work area, so the calling method returns
    /// early there and CANNOT demonstrate this logic — testing the effect through a real window would be a
    /// vacuous test that passes whether or not the guard exists.
    /// </summary>
    /// <param name="current">The window's current size.</param>
    /// <param name="xamlDefault">The size declared in XAML, captured in the constructor.</param>
    /// <param name="workArea">The screen work area, in PHYSICAL pixels.</param>
    /// <param name="scaling">The screen's DIP scale factor (physical pixels per DIP).</param>
    /// <param name="minimum">The window's declared <c>MinWidth</c>/<c>MinHeight</c>, in DIPs.</param>
    /// <returns>The size the window should take — never larger than <paramref name="current"/>.</returns>
    internal static Size FitToWorkArea(Size current, Size xamlDefault, Size workArea, double scaling, Size minimum)
    {
        // A caller picked its own size (every headless layout test does, at widths up to 1920) — never
        // second-guess it. Without this the whole measurement suite would silently be re-sized by whatever
        // screen the runner happens to have.
        if (current.Width != xamlDefault.Width || current.Height != xamlDefault.Height) return current;

        var scale = scaling > 0 ? scaling : 1.0;

        // Only ever SHRINK to fit, and never below the declared minimum — MinWidth/MinHeight stay the floor,
        // so a desktop smaller than the minimum still clamps up (and is out of support scope).
        return new Size(
            Math.Max(minimum.Width, Math.Min(current.Width, workArea.Width / scale)),
            Math.Max(minimum.Height, Math.Min(current.Height, workArea.Height / scale)));
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private MainWindowViewModel? _watchedVm;

    /// <summary>
    /// Subscribes to the shell's "F2 — set the date" request (WI-5 4c). The view's only job is to move the
    /// caret into the open entry screen's working-date box; parsing and canonical echo belong to the view model.
    /// </summary>
    private void HookWorkingDateFocus()
    {
        if (_watchedVm is not null)
            _watchedVm.WorkingDateEditRequested -= OnWorkingDateEditRequested;

        _watchedVm = Vm;
        if (_watchedVm is not null)
            _watchedVm.WorkingDateEditRequested += OnWorkingDateEditRequested;
    }

    private void OnWorkingDateEditRequested(object? sender, EventArgs e)
        // Defer to the next layout pass: F2 may have opened/switched the page, so the target box can still be
        // materialising when the request is raised.
        => Dispatcher.UIThread.Post(FocusWorkingDateBox, DispatcherPriority.Loaded);

    /// <summary>
    /// Focuses (and selects) the visible working-date TextBox of the open entry screen — the boxes tagged
    /// <c>Classes="working-date"</c> in the XAML. Selecting the text means the operator can simply type the new
    /// date over it, which is what F2 does in the reference product. No calendar is opened: the app has zero
    /// DatePicker controls by design and this keeps F2 keyboard-only.
    /// </summary>
    private void FocusWorkingDateBox()
    {
        foreach (var box in this.GetVisualDescendants().OfType<TextBox>())
        {
            if (!box.Classes.Contains("working-date")) continue;
            if (!box.IsEffectivelyVisible || !box.IsEffectivelyEnabled) continue;

            box.Focus();
            box.SelectAll();
            return;
        }
    }

    /// <summary>
    /// Keeps the newly-active (rightmost) cascade column in view: whenever a column is added/removed we
    /// scroll the horizontal cascade viewport to its far right so the focused column is never left
    /// clipped behind the viewport edge (macOS-Finder column-view behaviour).
    /// </summary>
    private void HookCascadeAutoScroll()
    {
        if (_watchedColumns is not null)
            _watchedColumns.CollectionChanged -= OnColumnsChanged;

        _watchedColumns = Vm?.Columns;
        if (_watchedColumns is not null)
            _watchedColumns.CollectionChanged += OnColumnsChanged;
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        // Defer to the next layout pass so the new column has a measured width to scroll to, then move
        // the horizontal offset to the far right (the active column is always the rightmost).
        => Dispatcher.UIThread.Post(ScrollCascadeToActiveColumn, DispatcherPriority.Loaded);

    private void ScrollCascadeToActiveColumn()
    {
        var scroller = CascadeScroller;
        if (scroller is null) return;
        var maxX = System.Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        scroller.Offset = new Avalonia.Vector(maxX, scroller.Offset.Y);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ W2-14 (census 14.1) — GO TO (Alt+G), and the keys that belong to it while it is up.              │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // Grounding: help.tallysolutions.com's shortcut table — Alt+G, "To primarily open a report, and create
        // masters and vouchers in the flow of work". Alt+G was measured FREE before this arm was written (zero
        // `Key.G` hits anywhere in src/Apex.Desktop), so unlike the Insert-Voucher / Company-menu / More-Details
        // chords it needed no ruling and displaces nothing.
        //
        // This block sits at the VERY TOP of the chain deliberately, and in two halves:
        //   • Alt+G opens the overlay from ANYWHERE — that is the whole feature ("without having to move out of
        //     the screen you have already opened"), so it must not be filtered by any screen guard below.
        //   • While the overlay IS up it OWNS Up / Down / Enter / Escape. Without that, Down would arrow the
        //     cascade column hidden behind the overlay and Enter would drill it — the operator would be driving
        //     a menu they cannot see. Every other key (the letters they are typing) falls through to the search
        //     box, which is focused.
        if (e.Key == Key.G && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (vm.IsGoToOpen) vm.CloseGoTo(); else vm.OpenGoTo();
            e.Handled = true;
            return;
        }

        if (vm.IsGoToOpen)
        {
            switch (e.Key)
            {
                case Key.Down: vm.GoTo!.MoveDown(); e.Handled = true; return;
                case Key.Up: vm.GoTo!.MoveUp(); e.Handled = true; return;
                case Key.Enter: vm.ActivateGoTo(); e.Handled = true; return;
                case Key.Escape: vm.CloseGoTo(); e.Handled = true; return;
            }
        }

        // WI-3: Ctrl+Enter on a master LIST row opens that master for ALTERATION. This must sit ahead of every
        // other Enter arm below — the plain-Enter drill immediately after ignores modifiers, and the
        // IsMasterAcceptScreen arm in the switch would otherwise raise "Accept Stock Item? (Y/N)" instead. The VM
        // returns false on any screen without a highlighted alterable row, so Ctrl+Enter is untouched elsewhere.
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.AlterHighlightedStockItemRow())
        {
            e.Handled = true;
            return;
        }

        // 7.16 — THE SAME CHORD, THE SAME RULE, on the payroll masters' existing-lists. ONE arm for every kind
        // that has the capability: the VM resolves which payroll master is open and returns false on every other
        // screen, so this is inert everywhere else and cannot diverge kind-by-kind (which is exactly how the
        // row's defect was shaped — a capability missing across all eight master kinds rather than eight
        // separate oversights). NOTE: four of the eight kinds are wired today; this arm is one arm for however
        // many the VM resolves, not a claim that all eight are done. See MainWindowViewModel.PayrollMasterScreen.
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.AlterHighlightedPayrollMasterRow())
        {
            e.Handled = true;
            return;
        }

        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ Ctrl+Enter OPENS THE HIGHLIGHTED POSTED VOUCHER FOR ALTERATION. (Phase 10.11 S5d / VL-1.)        │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // WHAT THIS CLOSES. `VoucherEntryViewModel.ForAlter` shipped with ZERO production callers — every caller
        // lived in tests/Apex.Desktop.Tests — so no operator could reach voucher alteration by any sequence of
        // keys. That is the SAME defect `StockItemAlterReachabilityTests` was written for one file away, and it
        // shipped a second time in a codebase that already contained the test proving the shape. The standing
        // lock against a third is `ViewModelAlterEntryPointReachabilityTests`, which derives its set rather than
        // listing it.
        //
        // FIDELITY (R7) — TWO RECORDS, KEPT APART because conflating them is the defect lenses caught on S3 AND
        // S5a. (A) The chord is a DELIBERATE WIDENING OF AN ATTESTED BEHAVIOUR: the corpus gives Ctrl+Enter as
        // "To alter a master during voucher entry or from drilldown of a report" (Book PDF p.436 [printed p.432],
        // `-raw`) — an alteration key, from a drill-down, for a MASTER; we widen it to a VOUCHER from the same
        // place. (B) NOT using plain Enter is a DELIBERATE DIVERGENCE FROM AN ATTESTED BEHAVIOUR: the corpus
        // reaches voucher alteration with plain Enter on a register row ("Select Month & Show/Edit Entry", Book
        // PDF pp.32, 34, 37, 42, 47, 49, 64, 71) and has no separate read-only voucher screen — we keep plain
        // Enter for the read-only VoucherDetail column, USER DECISION 1 / VL-1. Neither is corpus silence.
        // 🔴 Ctrl+B STAYS RESERVED AND UNBOUND. Nothing here claims it.
        //
        // ORDER IS LOAD-BEARING, in both directions:
        //   • BELOW the stock-item arm above, which owns Ctrl+Enter on Screen.StockItemMaster and returns false
        //     everywhere else. The two surfaces are disjoint, so the order is a reading convenience — but it also
        //     means master alteration keeps the chord it already had if a screen ever carries both.
        //   • ABOVE the `vm.DrillSelectedRow()` arm immediately below, and THAT is a real behaviour change: that
        //     arm tests `e.Key == Key.Enter` with NO modifier test at all, so before this block Ctrl+Enter on a
        //     Day-Book row DRILLED, identically to plain Enter. Plain Enter still drills — the read-only column
        //     is USER DECISION 1's half — and Ctrl+Enter now alters.
        //
        // EVERY GUARD, inherited from the S3/S4 reviews rather than re-derived:
        //   • `vm.IsVoucherAlterTargetPage` — the live report page, the register drill, the voucher-detail
        //     column: EXACTLY the three voucher arms of `IsDeleteTargetPage`, so Alt+D and Ctrl+Enter can never
        //     disagree about which voucher the highlight means. It uses `IsLiveReportPage`, NOT
        //     `IsReportContext`: the latter is deliberately TRUE while an F12 config, an Alt+F12 sort/filter, an
        //     Alt+A picker, an Alt+K saved-views panel or a Print Preview column is stacked over the report with
        //     the row still highlighted behind it. S3 measured that hole on five screens; it is not re-opened.
        //     🔴 HONESTLY LABELLED, because the mutation was run and it did NOT say what this comment first
        //     claimed: swapping this clause for `vm.IsReportContext` does NOT let a stacked column alter the row
        //     behind it — `RequestAlterHighlightedVoucher`'s own `CurrentScreen` switch has no `ReportConfig`
        //     arm and refuses it a second time. For THAT case the two guards are redundant and the view model is
        //     what decides. This clause is still load-bearing, measurably, in the other direction: under that
        //     same mutation the register drill and the voucher-detail column stop working entirely (both are
        //     excluded from `IsReportContext` by construction), so the chord loses two of its three surfaces.
        //     Read it as the readable statement of scope plus a cheap pre-filter — not as the thing standing
        //     between an operator and S3's hole.
        //   • `e.KeyModifiers == KeyModifiers.Control` — an EXACT match, not `HasFlag`. Ctrl+Alt+Enter,
        //     Ctrl+Shift+Enter and Ctrl+Win+Enter are different chords. Same doctrine as the Alt+X and Alt+D arms
        //     below and the bare-letter quick-jumps far beneath them. (The stock-item arm above keeps its own
        //     `HasFlag` spelling; tightening it is not this slice's change to make.)
        //   • `!IsTyping(e)` / `!IsPickerOpen(e)` — DEFENCE IN DEPTH, and labelled honestly. The three surfaces
        //     this arm is scoped to carry no TextBox that takes focus today, and the report page's three
        //     ComboBoxes are invisible on the report kinds that have voucher rows — so on the surfaces reachable
        //     at this commit neither clause can change the outcome, and neither is independently pinnable. They
        //     are kept, not deleted, for the same reason the Alt+X pair is: this arm puts a POSTED voucher into
        //     an editable form, and the report page is one inline filter box away from making them load-bearing.
        //     Do NOT write a test claiming to pin them — it would be pinning the screen gate.
        // The view model does the REST of the gating (a row must be highlighted, it must resolve to a real
        // voucher, no confirmation may already be up, and `ForAlter`'s eligibility predicate must accept the
        // shape — 13 of 33 enumerated shapes REFUSE, and the refusal is shown on the notice bar, never swallowed).
        //
        // 🔴 `e.Handled` IS NOT UNCONDITIONAL HERE, and that is the one place this arm deliberately departs from
        // Alt+X and Alt+D. Its outcome is three-valued (see `VoucherAlterationRequest`):
        //   • Opened / Refused → CONSUMED. A refusal is terminal because the sentence has just been written to
        //     the notice bar and `OnCurrentScreenChanged` clears that bar on any change of screen — falling
        //     through to the drill below would open the voucher-detail column and wipe the explanation on the way
        //     past, which is exactly the "invisible failed operation" defect S3's review found, by another route.
        //   • NoVoucherHere → NOT consumed, so the keystroke continues to `DrillSelectedRow` below. That arm
        //     tests `e.Key == Key.Enter` with no modifier test, so Ctrl+Enter on a Trial Balance ledger row (or
        //     any header/total row) drills TODAY; swallowing it would take a working behaviour away in exchange
        //     for a dead key.
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control
            && vm.IsVoucherAlterTargetPage && !IsTyping(e) && !IsPickerOpen(e)
            && vm.RequestAlterHighlightedVoucher() is not VoucherAlterationRequest.NoVoucherHere)
        {
            e.Handled = true;
            return;
        }

        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ W2-15 (census row 5.4) — Alt+2 DUPLICATES the highlighted posted voucher.                        │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // FIDELITY (R7; RULING 14 — the tally/ corpus is gone, so this is grounded on the vendor's own help).
        // help.tallysolutions.com/day-book-tally/ names the Day-Book verb and its chord verbatim: "Press Alt+2
        // (Duplicate Vch)". This is a FREE ADDITION, not a re-assignment: `Key.D2` — indeed every `Key.D<digit>`
        // — had ZERO hits anywhere in src/ before this line, so nothing in the open U-6 chord ruling is disturbed
        // and no existing binding is displaced.
        //
        // 🔴 ITS SIBLING IS NOT BUILT, and that is stated here rather than left as a gap for someone to "fix".
        // The vendor's Insert Voucher ("Select the entry above which you want to insert the transaction, press
        // Alt+I (Insert Vch)") is NOT bound, for two independent reasons: Alt+I is already spent on the POS
        // tender-mode toggle further down this file, and the only behaviour that distinguishes Insert from the
        // shipped Alt+A "Add voucher in a report" — which already seeds the new voucher with the highlighted
        // row's date — is that inserting between two vouchers renumbers every later voucher of that type. That
        // rewrites document numbers on already-issued documents, which is precisely what the IRN and challan
        // freezes in `VoucherAlterationEligibility` exist to prevent. It needs a user ruling, not a keystroke.
        //
        // SCOPE, ORDER AND CONSUMPTION are the Ctrl+Enter arm's, deliberately and for its reasons:
        //   • `vm.IsVoucherAlterTargetPage` — the SAME three surfaces (live report page, register drill,
        //     voucher-detail column), so Duplicate and Alter can never disagree about which document the
        //     highlight means. `RequestDuplicateHighlightedVoucher`'s own `CurrentScreen` switch is the thing
        //     that actually decides, exactly as it is for Ctrl+Enter.
        //   • `e.KeyModifiers == KeyModifiers.Alt` — an EXACT match, not `HasFlag`: Ctrl+Alt+2 and Alt+Shift+2
        //     are different chords, and on several layouts Alt+Shift+2 is the at-sign. (Spelled out rather than
        //     quoted: CompanyCaptureReachTests.BlankComments scans this file for a verbatim-string opener, and a
        //     quoted at-sign directly after a double quote reads as exactly that.)
        //   • `!IsTyping(e)` / `!IsPickerOpen(e)` — defence in depth, labelled as honestly as the arm above
        //     labels its own: on the three surfaces reachable at this commit neither clause can change the
        //     outcome, and neither is independently pinnable. Do not write a test claiming to pin them.
        //   • `e.Handled` is NOT unconditional, for the identical three-valued reason: Opened/Refused are
        //     consumed (a refusal has just been written to the notice bar, which `OnCurrentScreenChanged` would
        //     wipe), NoVoucherHere is not (nothing was chosen, so nothing is claimed).
        // Placed ABOVE the bare-`Key.D2` region of the file — there is none — and above the Alt-letter block far
        // below, which switches on letters only and would never see a digit.
        if (e.Key == Key.D2 && e.KeyModifiers == KeyModifiers.Alt
            && vm.IsVoucherAlterTargetPage && !IsTyping(e) && !IsPickerOpen(e)
            && vm.RequestDuplicateHighlightedVoucher() is not VoucherAlterationRequest.NoVoucherHere)
        {
            e.Handled = true;
            return;
        }

        // RQ-7 keyboard drill (defect-1): Enter must drill the highlighted drillable report/drill row BEFORE
        // the Window's generic Enter handling (which drives cascade navigation via ActivateSelected) consumes
        // it. This tunnel handler is on the Window, so it fires ahead of the report ListBox's own bubble
        // KeyDown; the VM drills the ACTIVE pane's two-way-bound SelectedRow (focus-independent). A no-op on a
        // non-drillable row / non-report screen, so Enter stays a safe no-op there. Double-click still drills.
        if (e.Key == Key.Enter && vm.DrillSelectedRow())
        {
            e.Handled = true;
            return;
        }

        // Ctrl+A saves/accepts (accept shortcut) — apply the F12 report config, else create company /
        // accept voucher / create ledger.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (vm.CurrentScreen == Screen.ReportConfig)
                vm.ApplyReportConfig();
            else if (vm.CurrentScreen == Screen.ReportSortFilter)
                vm.ApplyReportSortFilter();
            else if (vm.CurrentScreen == Screen.AddComparisonColumn)
                vm.ApplyAddComparisonColumn();
            else if (vm.CurrentScreen == Screen.AutoColumns)
                vm.ApplyAutoColumns();
            else if (vm.CurrentScreen == Screen.SaveView)
                vm.ApplySaveView();
            else if (vm.CurrentScreen == Screen.SavedViews)
                vm.OpenSelectedSavedView();
            else if (vm.CurrentScreen == Screen.PrintConfig)
                vm.ApplyPrintConfig();
            else if (vm.CurrentScreen == Screen.Export)
                vm.ApplyExport();
            else if (vm.CurrentScreen == Screen.ExportData)
                vm.ApplyExportData();
            else if (vm.CurrentScreen == Screen.ImportData)
                vm.ApplyImport();
            // Data -> Backup / Restore (the R-7 carve-out). On the Restore panel Ctrl+A is the DESTRUCTIVE step,
            // and the VM refuses it unless the archive has been examined AND the confirmation is ticked (NFR-8).
            else if (vm.CurrentScreen == Screen.BackupCompany)
                vm.ApplyBackup();
            else if (vm.CurrentScreen == Screen.RestoreCompany)
                vm.ApplyRestore();
            else if (vm.CurrentScreen == Screen.PrintPreview)
                SavePrintPreviewToDocuments(vm);
            else if (vm.CurrentScreen == Screen.EmailCompose)
                SaveEmailToDocuments(vm);
            else if (vm.CurrentScreen == Screen.SmtpSettings)
                vm.SaveSmtpSettings();
            // Phase 7 slice 7: Ctrl+A on a TDS/TCS certificate / control-chart page EXPORTS the deterministic,
            // de-branded PDF (the accelerator every one of those pages advertises) — no dead shortcut.
            else if (vm.CurrentScreen == Screen.Form16A)
                vm.Form16A?.ExportPdf();
            else if (vm.CurrentScreen == Screen.Form27D)
                vm.Form27D?.ExportPdf();
            else if (vm.CurrentScreen == Screen.Form27A)
                vm.Form27A?.ExportPdf();
            else
                vm.ActivateSelected();
            e.Handled = true;
            return;
        }

        // WI-11 — the "Accept? (Y/N)" confirmation. ORDER IS LOAD-BEARING, in both directions:
        //   • It sits AFTER the Ctrl+A arm above, so the accept-as-is shortcut still reaches its own handler and
        //     saves WITHOUT the prompt (the ~40 Ctrl+A screens are untouched, and Ctrl+A while the prompt happens
        //     to be up simply accepts, as the reference product does).
        //   • It sits BEFORE the bare-Y (Gateway → Export Data), Alt+Y (Data → Backup / Restore, :633) and
        //     Alt+N (Auto Columns) arms further down, and it CONSUMES all four shapes while the confirmation
        //     is up: bare Y/N answer it, Alt+Y/Alt+N are inert (see the S1 block below). Nothing reaches a
        //     backup panel, an Export-Data panel or an Auto-Columns chooser over a live confirmation.
        // The whole arm is SCOPED to vm.IsAcceptPromptOpen, which is false everywhere else — so Y and N keep
        // their existing meanings across the rest of the app.
        //
        // V8 — USER DECISION: the !IsPickerOpen(e) guard fixes the stray-Y-saves bug. With the accept prompt AND
        // a dropdown BOTH open, a bare Y used to reach ConfirmMasterAccept here and SAVE the master (measured:
        // promptOpen=True dropdownOpen=True, ledgers 38 -> 39) — a Y the operator meant as type-ahead into the
        // dropdown silently committed the ledger. This arm now YIELDS while a dropdown is open, so Y/N/Escape
        // reach the dropdown, not the confirm. The same guard is on the navigation Escape arm below, so with
        // both open the FIRST Escape closes the DROPDOWN (both arms yield; the ComboBox closes itself) and only a
        // SECOND Escape — no dropdown left — reaches DismissMasterAccept. The prompt is never stranded: once the
        // dropdown closes, IsPickerOpen is false and Y/N/Escape answer the prompt again exactly as before
        // (Y_with_prompt_and_dropdown_open_does_not_save_but_saves_once_the_dropdown_closes,
        // Escape_with_prompt_and_dropdown_open_closes_dropdown_first_then_dismisses_the_prompt).
        //
        // S1 (Phase 10.11) — THE SECOND MODIFIER HOLE, and the more dangerous of the two. This arm excluded
        // Control but not Alt, so with the prompt up a stray Alt+Y — the Data / Backup-Restore accelerator,
        // live on every screen a company is open on (its arm is further down this chain, so this one won) —
        // reached ConfirmMasterAccept and SAVED the master. Measured: prompt open on Ledger Creation, Alt+Y,
        // and "Bharat Motors" was in company.Ledgers. The operator asked for a menu and silently committed a
        // record. It matters far beyond WI-11: this one prompt is the confirmation channel a later slice in
        // this phase hangs DELETE on, so the same hole would have let Alt+Y confirm a deletion nobody answered.
        //
        // SCOPE — exactly Y and N, exactly Alt, and CONSUMED RATHER THAN YIELDED. Alt+Y/Alt+N must not answer
        // the confirmation; they must also not be handed onward, and the difference is the whole review fix.
        //
        // WHY NOT YIELD (measured, and the first cut of this slice got it wrong). Narrowing with
        // `case Key.Y when !altHeld` made Alt+Y fall through to its owner at :633 →
        // ShowDataMenu → SelectRootItem → TrimColumnsAfter(0) → OpenSubmenuColumn → ClearSubScreens, which
        // NULLS LedgerMaster/StockItemMaster/VoucherEntry. So the fix stopped the unconfirmed SAVE and bought
        // an unconfirmed DESTROY in its place: prompt up on Ledger Creation, Alt+Y, and the ledger was indeed
        // not created — but the typed name, the chosen group and the opening balance were gone, with the
        // operator dumped on Backup / Restore and no message. Worse under Alt+C create-on-the-fly, where the
        // create column sits OVER a live voucher: TrimColumnsAfter(0) takes the VOUCHER column too, so a
        // half-keyed invoice dies to a menu chord. That is the D2 work-loss class this arm already exempts
        // Escape for — the reasoning was applied to Escape and, first time round, not to Y.
        //
        // So while a confirmation is up the prompt is MODAL against Alt+letter chords: Alt+Y and Alt+N change
        // nothing at all and leave the question on screen. Two presses, exactly the doctrine already settled
        // for Escape — answer N/Esc, then press Alt+Y. Nothing is saved, nothing is discarded, and the
        // outcome no longer depends on where the caret happens to be (the :633 owner requires !IsTyping(e),
        // so a yield gave one answer with focus in the Name box and another with focus anywhere else).
        // The prompt is never stranded: it is answerable immediately, and any real navigation resets it via
        // OnCurrentScreenChanged → ResetMasterAcceptPrompt.
        //
        // ESCAPE IS DELIBERATELY NOT NARROWED. It is not a letter and owns no Alt accelerator, and the arm it
        // would fall through to is `case Key.Escape when !IsPickerOpen(e)` → Back(), which POPS the column and
        // discards the half-typed master — the same D2 work-loss class. Alt+Escape therefore still ANSWERS
        // the prompt (dismiss, master intact), and a test pins that.
        if (vm.IsAcceptPromptOpen && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsPickerOpen(e))
        {
            var altHeld = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            switch (e.Key)
            {
                // Alt+Y / Alt+N: consumed and INERT. Guarded cases must precede their unguarded twins below.
                case Key.Y when altHeld:
                case Key.N when altHeld: e.Handled = true; return;
                case Key.Y: vm.ConfirmMasterAccept(); e.Handled = true; return;
                case Key.N: vm.DismissMasterAccept(); e.Handled = true; return;
                case Key.Escape: vm.DismissMasterAccept(); e.Handled = true; return;
            }
        }

        // Ctrl+R opens the GST Rate Setup (dated GST 2.0 rate + cess bulk maintenance; Phase 9 slice 1) — the
        // advertised accelerator for that screen. Scoped to a GST-enabled company so it never fires otherwise.
        if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && vm.Company is { GstEnabled: true })
        {
            vm.ShowGstRateSetup();
            e.Handled = true;
            return;
        }

        // Reorder Levels master (RQ-53): Alt+S toggles the reorder level Simple⇄Advanced; Alt+V toggles the
        // minimum-order-qty Simple⇄Advanced. Scoped to that screen so they never collide elsewhere.
        if (vm.CurrentScreen == Screen.ReorderLevelsMaster && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S) { vm.ReorderLevels?.ToggleReorderAdvanced(); e.Handled = true; return; }
            if (e.Key == Key.V) { vm.ReorderLevels?.ToggleMinQtyAdvanced(); e.Handled = true; return; }
        }

        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ Alt+X IS VOUCHER CANCELLATION — it is NOT "abandon the screen I am on". (Phase 10.11 S3 / VL-3.)  │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // WHAT STOOD HERE BEFORE, and why it went. An arm bound Alt+X app-wide to `vm.CancelVoucher()` — the
        // ABANDON-THE-ENTRY-SCREEN verb (now `vm.AbandonEntry()`), which discards a half-keyed voucher or master
        // and pops its column. Two things were wrong with it:
        //   • It squatted the accelerator the reference product spends on CANCELLING A POSTED VOUCHER, so the one
        //     key an operator would reach for to cancel a posted entry instead threw away whatever was on screen.
        //   • It was UNDER-GUARDED: it tested only for the Alt modifier — no `!IsPickerOpen`, no `!IsTyping`, no
        //     Ctrl exclusion and no screen scope — so it fired from inside an open dropdown and from a text field,
        //     and Ctrl+Alt+X abandoned the screen too.
        // Nothing is orphaned by its removal: Escape reaches `vm.Back()` at the switch far below (which pops the
        // page column and tears the entry VM down through `ClearSubScreens`), Left does the same, and the six
        // on-screen "Cancel" buttons still call `vm.AbandonEntry()` directly.
        //
        // THE NEW ARM, and every guard on it:
        //   • `vm.IsLiveReportPage` — THE REPORT MUST BE THE ACTIVE COLUMN, not merely bound. Cancel acts on a
        //     POSTED voucher highlighted in a report (the Day Book is the one that carries voucher rows today),
        //     never on an entry screen and never from a column STACKED OVER the report.
        //     🔴 THIS GUARD WAS `vm.IsReportContext` AND THAT WAS A HOLE, measured: `IsReportContext` is
        //     `Reports is not null && CurrentScreen is not (LedgerVouchers or VoucherDetail)`, and its own
        //     doc-comment says it is built to STAY TRUE while an F12 config panel is open — it was written for
        //     report-PARAMETER shortcuts that must survive a config column, not for a destructive verb. It stayed
        //     true, with the Day Book row still highlighted underneath, on FIVE screens the operator is actually
        //     standing in: `ReportConfig` (F12), `ReportSortFilter` (Alt+F12), `AddVoucherPicker` (Alt+A),
        //     `SavedViews` (Alt+K) and `PrintPreview` (P) — every one of which leaves `Reports` deliberately bound
        //     beneath it so Esc returns to the same live report. Alt+X from inside any of them raised the
        //     confirmation for the voucher BEHIND the column, and one Y killed it. `IsPickerOpen` cannot see that:
        //     it looks for an open ComboBox popup, not for a Miller column. `IsReportContext` keeps its own job
        //     (the parameter shortcuts below); this arm asks the narrower question it actually needs.
        //     Note the SCOPE is OUR decision, not fidelity: the corpus scopes Alt+X to "Vouchers & Reports"
        //     (Book p.437), and we ship the narrower half deliberately because no alteration/entry-screen cancel
        //     exists yet (S5).
        //   • `!IsTyping(e)` / `!IsPickerOpen(e)` — DEFENCE IN DEPTH, and honestly labelled as such. Every text
        //     field and every picker that today sits over a report lives in one of the five columns the screen
        //     gate above now refuses, so neither predicate can change the outcome on any surface reachable at
        //     this commit, and neither is independently pinnable — the report page template itself carries no
        //     TextBox, and its three ComboBoxes (scenario, payroll month, payroll employee) are invisible on the
        //     only report kind that has voucher rows. MEASURED, not assumed: dropping BOTH clauses leaves the whole
        //     Desktop suite green at 2231/2231. They are kept, not deleted, because the report page is one inline
        //     filter box away from making them load-bearing again and this is the one destructive accelerator in
        //     the app. Do NOT write a test claiming to pin them: it would be pinning the screen gate. (Same
        //     category as `RequestCancelHighlightedVoucher`'s null gate — legitimate defence with an honest label,
        //     as opposed to a guard whose comment claims a mechanism it cannot deliver.)
        //   • `e.KeyModifiers == KeyModifiers.Alt` — an EXACT match, not `HasFlag`. Ctrl+Alt+X is a different
        //     chord, and so are Alt+Shift+X and Alt+Win+X: `HasFlag(Alt)` + a Ctrl exclusion admitted both, which
        //     made the one destructive accelerator in the app the loosest match in this chain. The doctrine is
        //     already written ~740 lines below for the bare-letter quick-jumps ("It deliberately excludes Shift as
        //     well … admitting Shift would leave the same class of hole open on the next chord anyone binds") and
        //     it applies here with more force, not less.
        // The view model does the REST of the gating (a row must be highlighted, it must carry a voucher, the
        // voucher must not already be cancelled, no confirmation may already be up, and no live IRN/EWB may be
        // stranded by it); this arm only decides that the keystroke is ours.
        // `e.Handled` marks the keystroke CONSUMED so it does not bubble past this window handler to any control
        // beneath — the `return` on the next line is what stops the later arms in this chain, so the two are not
        // the same thing and the flag is not redundant. It is set unconditionally once the guards pass, so a
        // report with no highlighted voucher row is a quiet no-op rather than a live key. Pinned by
        // `AltX_on_a_report_row_comes_back_Handled`.
        if (e.Key == Key.X && e.KeyModifiers == KeyModifiers.Alt
            && vm.IsLiveReportPage && !IsTyping(e) && !IsPickerOpen(e))
        {
            vm.RequestCancelHighlightedVoucher();
            e.Handled = true;
            return;
        }

        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ Alt+D DELETES THE HIGHLIGHTED VOUCHER OR MASTER. (Phase 10.11 S4 / VL-2.)                        │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // This is the arm that makes `LedgerService.Delete` REACHABLE for the first time in the project's life —
        // it has existed since Phase 1 with no caller — so every consequence of removing a posted voucher arrives
        // with this eight-line block. Both of them are guarded in `MasterDeletionRules`, which the view model calls
        // BEFORE it puts the question up: the referential guard (refuse with the COUNT of documents that point at
        // the voucher by Guid) and the numbering guard (refuse a FILED statutory document and offer Cancel,
        // because `NextNumber` is `max+1` BY SCAN and deleting the highest number hands it to the next entry).
        //
        // ORDER: it sits directly beside the Alt+X arm because the two verbs are siblings and must be read
        // together, and ABOVE the RQ-4 comparative Alt-letter arm (which claims C and N only) and the bare-letter
        // quick-jump switch ~600 lines below (`Key.D` → Day Book). The quick-jump cannot collide: slice S1
        // narrowed `CanQuickJump` to `e.KeyModifiers == KeyModifiers.None`, which is the guard that made Alt+D
        // available to bind at all. Do not loosen it.
        //
        // EVERY GUARD ON THIS ARM, and each one is inherited from the S3 review rather than re-derived:
        //   • `vm.IsDeleteTargetPage` — the five surfaces §6.4 item 6 names, and it uses `IsLiveReportPage`
        //     (NOT `IsReportContext`) for the report clause. `IsReportContext` is deliberately TRUE while an F12
        //     config, an Alt+F12 sort/filter, an Alt+A picker, an Alt+K saved-views panel or a Print Preview
        //     column is stacked over the report, with the Day Book row still highlighted behind it — a
        //     destructive verb written on it fires for the row BEHIND the column the operator is standing in.
        //     That was a measured hole in S3's first cut; it is not re-opened here.
        //   • `e.KeyModifiers == KeyModifiers.Alt` — an EXACT match, not `HasFlag`. Ctrl+Alt+D, Alt+Shift+D and
        //     Alt+Win+D are different chords, and admitting them would make the app's SECOND destructive
        //     accelerator its loosest match. Same doctrine as Alt+X above and the bare-letter jumps below.
        //   • `!IsTyping(e)` / `!IsPickerOpen(e)` — DEFENCE IN DEPTH, labelled honestly. On the report page and
        //     the two drill columns nothing focusable takes text today, so on those three surfaces neither clause
        //     can change the outcome. On the OTHER TWO they are load-bearing and not merely defensive: the Chart
        //     of Accounts and the Stock Item master are master screens with real TextBoxes and real pickers, and
        //     the Stock Item master's Name/Alias fields are where an operator's caret sits while they type. A
        //     bare `Alt+D` from inside a half-typed item name must not delete the item highlighted in the list
        //     behind the form. `Alt_D_while_typing_in_the_stock_item_name_does_not_delete` pins exactly that, so
        //     unlike S3's pair these clauses ARE independently falsifiable and are tested as such.
        // The view model does the rest of the gating (a row must be highlighted, it must resolve to a real
        // voucher/master, no confirmation may already be up, and the S4 guards must accept it). `e.Handled` is set
        // unconditionally once the guards pass, so a surface with nothing highlighted is a quiet no-op rather than
        // a live key that falls through to the Day Book jump.
        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ Census row 1.4 — Alt+D on the COMPANY ALTERATION screen DELETES THE OPEN COMPANY.                │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // FIDELITY (R7; RULING 14 — the tally/ corpus is gone, so this is the vendor's own help).
        // help.tallysolutions.com/…/set-up-company-tally/ gives screen and chord together: "Alt+K (Company) >
        // Alter. In the Company Alteration screen, press Alt+D." Only the route to the screen is ours
        // (Gateway → Masters → Alter Company) — the Alt+K top menu is not built and its chord is inside open
        // ruling U-6. `CompanyStorage.Delete` had ZERO callers before this arm: it was written, careful and
        // unreachable, which is why row 1.4 could not be claimed until a key reached it.
        //
        // IT SITS ABOVE THE MASTER Alt+D ARM AND IS DISJOINT FROM IT. `vm.IsDeleteTargetPage` excludes
        // Screen.AlterCompany, so neither arm can ever swallow the other's surface; they are adjacent because the
        // two must be read together, and `AltD_elsewhere_still_deletes_the_master_not_the_company` pins that the
        // master meaning is untouched. The bare-letter D quick-jump (Day Book) cannot collide either — slice S1
        // narrowed `CanQuickJump` to `KeyModifiers.None`.
        //
        // 🔴 NO `IsTyping` GUARD, AND THAT DIFFERS FROM THE ARM BELOW ON PURPOSE. The master arm guards it because
        // its caret sits in a form standing OVER A LIST, so a bare Alt+D mid-word would delete the row behind. The
        // Company Alteration screen has no list behind it — its subject IS the company — so the chord can only
        // mean one thing wherever the caret is, while guarding it would make an attested chord dead in ordinary
        // use, since the operator is on this screen precisely to type in its fields. `IsPickerOpen` is omitted for
        // the same reason and one more: the State picker claims no Alt chord, so the clause could not change an
        // outcome and would be a guard no test can fail. The Y/N confirmation is the guard here, and the view
        // model re-checks the screen and refuses while a question is already up.
        //
        // `e.Handled` is set unconditionally once the surface matches, so an Alt+D on this screen never falls
        // through to the Day Book jump — the same doctrine as the arm below.
        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Alt
            && vm.CurrentScreen == Screen.AlterCompany)
        {
            vm.RequestDeleteOpenCompany();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Alt
            && vm.IsDeleteTargetPage && !IsTyping(e) && !IsPickerOpen(e))
        {
            vm.RequestDeleteHighlighted();
            e.Handled = true;
            return;
        }

        // RQ-4 comparative shortcuts take priority while a report is the active page: Alt+C opens the "Add
        // Comparison Column" panel, Alt+N opens the "Auto Columns" chooser. Checked BEFORE the global Alt+C
        // (Create Ledger) so on a report page Alt+C compares columns rather than creating a ledger. Only fires
        // on a comparative-capable report (TB / BS / P&L / Stock Summary); otherwise it falls through.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.IsReportContext && vm.Reports is { SupportsComparative: true })
        {
            switch (e.Key)
            {
                case Key.C: vm.OpenAddComparisonColumn(); e.Handled = true; return;
                case Key.N: vm.OpenAutoColumns(); e.Handled = true; return;
            }
        }

        // Alt+C opens the Ledger-creation master whenever a company is open.
        // WI-1 — on a voucher-entry screen it is CONTEXT-AWARE: the focused picker's tagged field id (resolved by
        // walking up from the key source; see CreateField) selects WHICH master screen opens, and its DataContext
        // is the row the new master is written back into. An untagged field yields (null, null), which the view
        // model treats as inert on a voucher and as the historic Ledger/Stock-Item behaviour elsewhere.
        // ORDER: this stays BELOW the RQ-4 comparative arm above (a report's Alt+C still adds a comparison
        // column) and ABOVE nothing that claims Alt+C — no other arm in this chain matches C with Alt.
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var (fieldId, caller) = CreateField.Focused(e);
            vm.CreateLedgerShortcut(fieldId, caller);
            e.Handled = true;
            return;
        }

        // ┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ Ctrl+B IS FREE AND RESERVED — DO NOT BIND IT. (Phase 10.11 S2 / VL-4 / register row IV-5.)        │
        // └──────────────────────────────────────────────────────────────────────────────────────────────────┘
        // A "Bill Settlement" arm stood HERE, bound app-wide and unconditionally — it handled and returned
        // REGARDLESS OF SCREEN — and called vm.SettleBills(), which POSTED a real Receipt or Payment for every
        // spacebar-selected bill: always the bill's FULL pending amount, always through a ledger literally named
        // "Cash", dated at the report's as-of, with no preview, no confirmation and no undo.
        //
        // In TallyPrime Ctrl+B is "BASIS OF VALUES" — a report option that re-bases how figures are COMPUTED AND
        // PRESENTED, and it WRITES NOTHING TO THE BOOKS [TallyHelp keyboard-shortcuts, Reports: "Ctrl+B — To view
        // values in different ways in a report — Right button"]. TallyPrime's Bills Outstanding has no settlement
        // action of any kind; a bill is settled by keying a Receipt/Payment and choosing Against Reference from
        // the List of Pending Bills [CORPUS-SG p.92 §5.5]. So an operator pressing Ctrl+B on Bills Receivable to
        // change how figures DISPLAY instead posted a batch of receipt vouchers against their debtors — and the
        // trap was armed by a correct Tally reflex (Spacebar = select line in report) and sprung by a second one.
        //
        // Settlement now lives on Alt+A, scoped to the Outstandings screen (see that arm further down).
        //
        // BASIS OF VALUES ITSELF IS NOT BUILT — it is named debt, not an oversight. A later slice needs
        // ReportsViewModel to grow a re-basis (scale factor, stock valuation method, type of voucher entries) on
        // the OpenReportConfig cascade pattern; THAT slice reclaims Ctrl+B from this reservation. Until then the
        // key must reach nothing, so it stays unbound rather than being squatted by an unrelated feature.
        // NOTE for whoever binds it: with no arm here, Ctrl+B falls through to the bare-letter report quick-jump
        // switch far below (Key.B → Balance Sheet). That is harmless ONLY because slice S1 narrowed CanQuickJump
        // to `e.KeyModifiers == KeyModifiers.None`; do not loosen that guard.

        // Alt+R opens the Challan Reconciliation report (Phase 7 slice 3) — deposits vs deductions per section.
        // Gated internally on TDS being enabled (a no-op otherwise), so a non-TDS company is unaffected (ER-13).
        // Not while typing in a field, and not with Ctrl held.
        if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenChallanReconciliation();
            e.Handled = true;
            return;
        }

        // Ctrl+F opens the TDS Stat Payment (deposit) page (the Payment "Ctrl+F"; Phase 7 slice 3) — the accelerator
        // the menu item advertises. Gated internally on TDS being enabled (a no-op otherwise), and not while typing in
        // a field, so a non-TDS company is unaffected (ER-13).
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !IsTyping(e))
        {
            vm.ShowTdsStatPayment();
            e.Handled = true;
            return;
        }

        // Ctrl+F4 opens the Payroll voucher (Transactions → Vouchers → Payroll → Payroll; Phase 8 slice 3; RQ-7) —
        // the advertised Payroll accelerator. Intercepted here (before the bare F4 = Contra case) so Ctrl+F4 never
        // misfires as Contra; a no-op unless Payroll is enabled, so a non-payroll company is unaffected (ER-13).
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && vm.Company is { PayrollEnabled: true } && !IsTyping(e))
        {
            vm.ShowPayrollVoucher();
            e.Handled = true;
            return;
        }

        // Alt+B on a screen that carries a DATA PATH opens the OS file/folder chooser for it (census row 13.10 /
        // T1-20 — every path used to be a typed string or a silent default to Documents, so restoring from a
        // backup meant typing the archive path from memory).
        //
        // 🔴 WHY NOT Ctrl+B: Ctrl+B is the vendor's "Basis of Values" and is RESERVED UNBOUND above. Do not move
        // this here. Alt+B is already this codebase's screen-scoped convention (six arms below, each scoped to its
        // own screen), and the browse screens are disjoint from every one of them, so nothing collides.
        //
        // 🔴 WHY NO !IsTyping GUARD: the caret is normally IN the path TextBox when the operator wants the
        // chooser — that is the whole point of the feature. Alt+letter emits no character, so allowing it while
        // typing costs nothing. Scoped by BrowseRequest() returning null on every screen with no path, so this is
        // a safe no-op app-wide.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.BrowseRequest() is not null)
        {
            _ = BrowseForPathAsync(vm);
            e.Handled = true;
            return;
        }

        // Alt+B on the Form 26Q return (Phase 7 slice 4) SAVES the FVU flat file and RETURNS to the menu. Scoped to
        // the Form 26Q screen so it never collides with the inventory-voucher Alt+B (batch allocation) below; not
        // while typing in a field.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.Form26Q && !IsTyping(e))
        {
            vm.SaveReturnForm26Q();
            e.Handled = true;
            return;
        }

        // Alt+B on the Form 27EQ return (Phase 7 slice 6) SAVES the FVU flat file and RETURNS to the menu — the TCS
        // mirror of the Form 26Q Alt+B above. Scoped to the Form 27EQ screen so it never collides with the
        // inventory-voucher Alt+B (batch allocation) below; not while typing in a field.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.Form27EQ && !IsTyping(e))
        {
            vm.SaveReturnForm27EQ();
            e.Handled = true;
            return;
        }

        // Alt+B on the PF ECR / Challan report (Phase 8 slice 4) SAVES the ECR 2.0 flat file and RETURNS to the
        // menu — the PF mirror of the Form 26Q / 27EQ Alt+B above. Scoped to the PF ECR screen so it never collides
        // with the inventory-voucher Alt+B (batch allocation) below; not while typing in a field.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.PfEcrReport && !IsTyping(e))
        {
            vm.SaveReturnPfEcr();
            e.Handled = true;
            return;
        }

        // Alt+B on the PT Deduction Register report (Phase 8 slice 6) SAVES the register CSV and RETURNS to the menu —
        // the PT mirror of the PF ECR / ESI Alt+B above. Scoped to the PT register screen so it never collides with
        // the inventory-voucher Alt+B (batch allocation) below; not while typing in a field.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.ProfessionalTaxRegister && !IsTyping(e))
        {
            vm.SaveReturnProfessionalTax();
            e.Handled = true;
            return;
        }

        // Alt+B on a TDS/TCS certificate / control-chart page (Phase 7 slice 7) SAVES the PDF and RETURNS to the
        // menu — the mirror of the Form 26Q / 27EQ Alt+B above. Scoped to each certificate screen so it never
        // collides with the inventory-voucher Alt+B (batch allocation) below; not while typing in a field.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e)
            && vm.CurrentScreen is Screen.Form16A or Screen.Form27D or Screen.Form27A)
        {
            switch (vm.CurrentScreen)
            {
                case Screen.Form16A: vm.SaveReturnForm16A(); break;
                case Screen.Form27D: vm.SaveReturnForm27D(); break;
                case Screen.Form27A: vm.SaveReturnForm27A(); break;
            }
            e.Handled = true;
            return;
        }

        // Alt+B (NFR-2 / RQ-3) opens the batch-allocation sub-screen for a batch-tracked inventory-voucher line —
        // the keyboard equivalent of the "⧉" affordance the tooltip advertises. Resolves the focused line from the
        // key source (so Alt+B on a specific row targets it), falling back to the first eligible line on the
        // screen. Placed before the general Alt letter shortcuts so it never falls through; a safe no-op when no
        // line currently qualifies (company flag off / non-batch item / no godown / qty 0).
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.InventoryVoucherEntry
            && vm.InventoryVoucherEntry is { } entry)
        {
            var focused = FocusedInventoryLine(e);
            if (focused is not null && entry.LineWantsBatchAllocation(focused))
                entry.RequestBatchAllocation(focused);
            else
                entry.RequestBatchAllocationForFirstEligibleLine();
            e.Handled = true;
            return;
        }

        // G-5 — the SAME Alt+B on a Purchase/Sales ITEM INVOICE (BOOK pp.130-132 walks batch entry through F9
        // then F8). One key, one meaning, on every screen that carries batch-tracked item lines. Same resolution
        // rule: the focused row if it qualifies, else the first eligible line; a safe no-op when none does.
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.VoucherEntry
            && vm.VoucherEntry is { } invoice)
        {
            var focused = FocusedInventoryLine(e);
            if (focused is not null && invoice.LineWantsBatchAllocation(focused))
                invoice.RequestBatchAllocation(focused);
            else
                invoice.RequestBatchAllocationForFirstEligibleLine();
            e.Handled = true;
            return;
        }

        // Ctrl+T toggles the in-progress voucher as post-dated (post-dated cheque handling).
        if (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.TogglePostDated();
            e.Handled = true;
            return;
        }

        // Ctrl+L toggles the in-progress voucher as Optional (a provisional, scenario-only entry).
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleOptional();
            e.Handled = true;
            return;
        }

        // Ctrl+I toggles a Purchase/Sales voucher between plain accounting and item-invoice ("as invoice") mode.
        if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleItemInvoice();
            e.Handled = true;
            return;
        }

        // Ctrl+H "Change Mode" cycles a Purchase/Sales voucher through the three entry modes
        // As Voucher → Item Invoice → Accounting Invoice → As Voucher. Consumed (e.Handled) ONLY on an invoiceable
        // entry (Purchase/Sales) so the key is not swallowed app-wide; a no-op — and unhandled, falling through —
        // everywhere else. Additive: it consumes a key the keystroke arbiter does not otherwise route, and touches no
        // dropdown/Tab/arrow ownership, so the b8c617e arbitration and the numbering config are untouched.
        // G-6 widened the gate from IsInvoiceableEntry (Purchase/Sales only) to IsChangeModeEntry, which also admits
        // Contra/Payment/Receipt so Ctrl+H reaches Single Entry on the three vouchers that have it.
        if (e.Key == Key.H && e.KeyModifiers.HasFlag(KeyModifiers.Control) && vm.IsChangeModeEntry)
        {
            vm.ChangeMode();
            e.Handled = true;
            return;
        }

        // Alt+I toggles the in-progress POS bill between Single and Multi tender mode (both ways, RQ-42). Scoped to
        // the POS Billing screen so it never collides elsewhere; the item-invoice toggle stays on Ctrl+I.
        if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.PosBilling)
        {
            vm.TogglePosPaymentMode();
            e.Handled = true;
            return;
        }

        // Alt+A surfaces the POS bill's per-rate tax analysis (RQ-53). Scoped to the POS Billing screen; Ctrl+A
        // (accept) is a separate binding (Control) so this does not shadow it.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.PosBilling)
        {
            vm.ShowPosTaxAnalysis();
            e.Handled = true;
            return;
        }

        // Alt+A on the Outstandings report SETTLES the spacebar-selected bills — by opening a Single-Entry
        // Receipt/Payment PRE-LOADED with them as Against-Reference allocations, which the operator confirms
        // (date, cash/bank ledger, per-bill amounts) and Accepts. It POSTS NOTHING itself. This is the
        // replacement for the deleted Ctrl+B settlement arm; see the RESERVED block where that arm used to be.
        //
        // WHY Alt+A: TallyPrime's Reports bottom bar carries "Alt+A — Add voucher in report", which is precisely
        // the semantic needed (create a voucher FROM this report), and it is already the meaning this app gives
        // Alt+A on the Day Book — one key, one meaning. It squats nothing, so it does not repeat the IV-28
        // mistake of picking the first letter of our own feature name.
        //
        // ORDER: BELOW the POS Alt+A immediately above, ABOVE the Day-Book Alt+A immediately below. The three
        // guards are disjoint today — OpenPageColumn calls ClearSubScreens (which nulls Reports) before setting
        // the page, so IsDayBookReport cannot hold while Screen.Outstandings is current, and Screen.PosBilling
        // excludes both. The position is nevertheless deliberate, because this chain is FIRST-MATCH-WINS: if that
        // invariant ever changes, the screen the operator is actually STANDING ON must win, and Outstandings sits
        // above the Day Book for exactly that reason. Ctrl+A (Accept) is a separate Control-modified arm much
        // further up, and !Control here keeps it that way.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.IsOutstandingsScreen)
        {
            vm.OpenSettlementVoucherFromOutstandings();
            e.Handled = true;
            return;
        }

        // Alt+A on the Day Book ADDS a voucher (WI-12; Book p.431 "Add a voucher in a report"): it opens a
        // voucher-type picker beside the live Day Book (the report is NOT destroyed) and refreshes it on save.
        // Ordered AFTER the POS Alt+A so POS keeps priority, and scoped to the Day Book (IsDayBookReport) — copying
        // the Alt+K report-context pattern below — so it never hijacks Alt+A elsewhere. A no-op off the Day Book.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.IsDayBookReport)
        {
            vm.OpenAddVoucherFromReport();
            e.Handled = true;
            return;
        }

        // Ctrl+S (RQ-8) opens the "Save View" panel over an open report — name and store the report's current
        // configuration (kind + period/as-of + detail + F12 options + sort/filter + comparative columns). Report
        // context only, so it never fires while a drill column is the active pane. Ctrl+A on the panel saves it.
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control) && vm.IsReportContext)
        {
            vm.OpenSaveView();
            e.Handled = true;
            return;
        }

        // Alt+K (RQ-8) opens the "Saved Views" list — the company's saved report views (open/apply or delete one).
        // Available over any report page; needs a company. Checked before the global Alt shortcuts.
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && vm.IsReportContext)
        {
            vm.OpenSavedViews();
            e.Handled = true;
            return;
        }

        // P / Ctrl+P (RQ-9) opens the Print Preview of the CURRENT report — renders it to a de-branded PDF and
        // shows the paginated layout; "Save PDF" writes the bytes. Report context only (so the bare P never
        // fires while a drill column is active). Checked before the bare-P menu quick-jump (Profit & Loss),
        // which is guarded to menu screens, and before the Ctrl+P falls through to anything else.
        if (e.Key == Key.P && vm.IsPrintablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !IsTyping(e))
        {
            vm.OpenPrintPreview();
            e.Handled = true;
            return;
        }

        // E / Alt+E (RQ-14/16) opens the Export panel for the CURRENT report OR master list (Chart of Accounts,
        // ledgers, stock items) — choose CSV/XLSX/PDF, folder, filename and an optional timestamp; applying
        // writes the file via Apex.Ledger.Io. Exportable-page context only (a report or a master list), and not
        // while typing in a field (so a name-entry keystroke on a master screen goes to the field, not the
        // export jump). Accepts both the bare E and Alt+E (the header hint reads "E: Export"). No Ctrl.
        if (e.Key == Key.E && vm.IsExportablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenExport();
            e.Handled = true;
            return;
        }

        // O / Alt+O (Gateway → Import; RQ-20..24) opens the "Import" panel: read a canonical JSON/XML backup (or a
        // flat CSV) + choose the duplicate policy, then engine-routed apply into the open company. Only on the bare
        // Gateway cascade (a company is open, no page/voucher/master column on top, not typing) — the header hint
        // reads "O: Import". Accepts the bare O and Alt+O; never fires inside a voucher/ledger field.
        if (e.Key == Key.O && vm.CurrentScreen == Screen.Gateway
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenImport();
            e.Handled = true;
            return;
        }

        // Alt+Y (Data → Backup / Restore; the R-7 carve-out) opens the data-safety submenu column from anywhere a
        // company is open. This MUST be tested BEFORE the bare-Y Export-Data branch below, which only excludes
        // Ctrl — an Alt+Y would otherwise fall into it and open the wrong screen.
        if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && vm.Company is not null && !IsTyping(e))
        {
            vm.ShowDataMenu();
            e.Handled = true;
            return;
        }

        // Ctrl+E on the Restore panel EXAMINES the chosen backup (reads its manifest; touches nothing). The
        // destructive step stays on Ctrl+A, and the VM refuses that until this has passed and the tick is on.
        if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.RestoreCompany)
        {
            vm.ExamineRestore();
            e.Handled = true;
            return;
        }

        // Y (Gateway → Export Data; RQ-19/DP-4) opens the "Export Data" panel: a canonical JSON/XML backup of the
        // whole company. Same Gateway-root guard as Import — the header hint reads "Y: Data".
        if (e.Key == Key.Y && vm.CurrentScreen == Screen.Gateway
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenExportData();
            e.Handled = true;
            return;
        }

        // M / Ctrl+M (RQ-25/26) opens the "E-Mail" compose panel for the CURRENT report or the drilled voucher /
        // tax invoice — the attachment defaults to its exported PDF. The hand-off is OFFLINE: Save writes a
        // byte-stable .eml (with the attachment) or a mailto opens the OS mail client — nothing is sent. Printable
        // page context only (a report, or a voucher-detail drill), and not while typing. The header hint reads
        // "M: E-Mail". Accepts the bare M and Ctrl+M.
        if (e.Key == Key.M && vm.IsPrintablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !IsTyping(e))
        {
            vm.OpenEmailCompose();
            e.Handled = true;
            return;
        }

        // Spacebar toggles the highlighted bill's multi-select on the Outstandings page (not while typing).
        if (e.Key == Key.Space && vm.IsOutstandingsScreen && !IsTyping(e))
        {
            vm.ToggleOutstandingSelection();
            e.Handled = true;
            return;
        }

        // Inventory/order voucher shortcuts (modifier + F-key). Checked before the plain F-key switch so a
        // modified F-key never falls through to its bare-key report/voucher action.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                // Ctrl+F9 on the Reorder Status report raises a Purchase Order pre-filled from the selected row
                // (item + main location + Order-to-be-Placed qty; RQ-53). Checked before the blank-PO shortcut.
                case Key.F9 when vm.IsReorderStatusReport: vm.RaisePurchaseOrderFromReorder(); e.Handled = true; return;
                case Key.F9: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.PurchaseOrder); e.Handled = true; return;
                case Key.F8: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.SalesOrder); e.Handled = true; return;
                case Key.F6: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.RejectionIn); e.Handled = true; return;
                case Key.F5: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.RejectionOut); e.Handled = true; return;
                // Ctrl+F7 Physical Stock — TallyPrime's official key ("To open Physical Stock | Ctrl+F7"). The type
                // was seeded and rendered as "F10", which in this app opens the Other Vouchers menu, while Ctrl+F7
                // was bound to nothing: the UI advertised a route that did not exist. Ctrl+F7 was free — this block
                // previously handled only F5/F6/F8/F9, and Key.F7 appears nowhere else under Control — so nothing is
                // shadowed. (F10 deliberately stays Apex's Other Vouchers menu: it is the only route to Memorandum,
                // Reversing Journal and the four Job Work types, and re-cutting it to TallyPrime's voucher/master
                // list would break a working, discoverable route to chase a label. Decision D7 option A / X6.)
                case Key.F7: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.PhysicalStock); e.Handled = true; return;
            }
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Report parameter shortcuts (RQ-1/RQ-2) take priority while a report is the active page: Alt+F1
            // toggles detailed↔summary, Alt+F2 sets the period window. Checked before the inventory Alt+F
            // shortcuts so they never fire on a report page.
            if (vm.IsReportContext)
            {
                switch (e.Key)
                {
                    case Key.F1: vm.ReportToggleDetailed(); e.Handled = true; return;
                    case Key.F2: vm.ReportSetPeriod(); e.Handled = true; return;
                    // Alt+F12 opens the RQ-3 Sort/Filter panel. Placed with the report shortcuts and before
                    // the inventory Alt+F block so it never collides with the inventory voucher hotkeys.
                    case Key.F12: vm.OpenReportSortFilter(); e.Handled = true; return;
                }
            }

            switch (e.Key)
            {
                // Alt+F5 Debit Note / Alt+F6 Credit Note (WI-12; catalog §"Alt+F5 Debit Note · Alt+F6 Credit Note").
                // The §34 CN/DN entry screens are fully implemented but had no key route; these bind the advertised
                // accelerators to the existing accounting voucher entry. Checked before the inventory Alt+F kinds and
                // after the report-context Alt+F block above so a report page's Alt+F1/F2/F12 still win.
                case Key.F5: vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.DebitNote); e.Handled = true; return;
                case Key.F6: vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.CreditNote); e.Handled = true; return;
                case Key.F9: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.ReceiptNote); e.Handled = true; return;
                case Key.F8: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.DeliveryNote); e.Handled = true; return;
                // Alt+F7 (RQ-53): a Manufacturing Journal is a Stock-Journal-derived type, so once the BOM feature
                // is on (F12 "Set Components (BOM)") Alt+F7 opens the Manufacturing Journal; otherwise it stays the
                // plain Stock Journal, so a non-BOM company is unaffected.
                case Key.F7 when vm.Company is { SetComponentsBom: true }:
                    vm.OpenManufacturingJournal(); e.Handled = true; return;
                case Key.F7: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.StockJournal); e.Handled = true; return;
            }
        }

        // F12 on an open voucher/invoice print-preview opens the RQ-12 print-config panel (title override,
        // narration on/off, copy marking). Checked before the report F12 so it never re-opens report config.
        if (e.Key == Key.F12 && vm.CurrentScreen == Screen.PrintPreview
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.OpenPrintConfig();
            e.Handled = true;
            return;
        }

        // Bare F2 / F12 on a report page act on the report (RQ-1 as-of, RQ-6 configuration) rather than the
        // global button bar. Checked before the general switch. Ctrl+A on the open F12 panel applies it.
        if (vm.IsReportContext && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F2: vm.ReportSetAsOf(); e.Handled = true; return;
                case Key.F12: vm.OpenReportConfig(); e.Handled = true; return;
                // F8 on the Reorder Status report toggles the "reorder only" filter (RQ-53). Checked before the
                // bare-F8 global button-bar action so it never falls through on that report.
                case Key.F8 when vm.IsReorderStatusReport: vm.ReportToggleReorderOnly(); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            // The !IsPickerOpen guard is the USER CONTRACT: arrows must work on every screen INCLUDING inside a
            // dropdown. Without it this tunnel consumed both keys before the open popup could see them — measured
            // on Ledger Creation with a 26-row picker open: `BEFORE down: pickerSel=25 open=True` ->
            // `AFTER down: pickerSel=25 open=True`, with `bubble: Down handled=True src=ComboBoxItem`. The
            // highlight could not be moved by keyboard AT ALL, which made the Enter/Escape yields below very
            // nearly pointless — an operator could reach a dropdown but never navigate it.
            //
            // !IsTyping alone does not cover this: it tests `e.Source is TextBox`, and with a dropdown open
            // e.Source is a ComboBoxItem. Same blind spot that left Left (below) unguarded.
            //
            // NARROWNESS: with NO dropdown open these arms are untouched, and they are the Miller-column
            // navigation — the most-used key pair in the app. Arrows_with_no_picker_open_still_move_the_cascade_selection
            // locks that direction (measured Gateway: selIdx 1 -> 2 on Down, back to 1 on Up).
            case Key.Up when !IsTyping(e) && !IsPickerOpen(e):
                vm.MoveUp();
                e.Handled = true;
                break;
            case Key.Down when !IsTyping(e) && !IsPickerOpen(e):
                vm.MoveDown();
                e.Handled = true;
                break;
            // Right / Enter drills into the highlighted item (adds the column to the right and moves
            // focus there). Right is a navigation key only when not editing a text field.
            case Key.Right when !IsTyping(e):
                vm.DrillIn();
                e.Handled = true;
                break;
            // V3 — USER DECISION: "Enter opens it." Enter on a FOCUSED, CLOSED picker OPENS its dropdown. This
            // arm MUST precede BOTH Enter arms below, because on a focused closed picker each of them would
            // otherwise claim the key: on a master screen the next arm raises "Accept …? (Y/N)" (measured), and
            // on any screen the ActivateSelected arm drills the cascade. Placing it here makes Enter open the
            // picker instead — and only then; the guards below still stand for every other Enter.
            //
            // NARROWNESS — three ways it is scoped so nothing else moves:
            //   • IsPickerFocusedClosed matches ONLY a ComboBox that holds focus AND is closed. A menu/cascade
            //     column focuses a menu row (not a ComboBox), so Enter still drills there
            //     (Enter_on_a_menu_column_still_drills_the_cascade); with nothing focused e.Source is the
            //     MainWindow, so the no-picker master prompt is untouched
            //     (Enter_with_no_picker_open_still_raises_the_master_accept_prompt).
            //   • It requires the dropdown CLOSED, so an already-OPEN picker is excluded — the !IsPickerOpen
            //     yields below keep owning Enter there (commit the highlighted row + close), which is what lets
            //     open→Down→commit compose (Enter_opens_then_Down_highlights_then_Enter_commits_and_closes).
            //   • !IsAcceptPromptOpen keeps the :261 confirmation arm the owner of Enter while a prompt is up.
            // e.Handled stops fall-through to the arms below AND stops the ComboBox toggling the dropdown shut.
            case Key.Enter when !vm.IsAcceptPromptOpen && IsPickerFocusedClosed(e):
                OpenFocusedPicker(e);
                e.Handled = true;
                break;
            // WI-11: Enter on a master screen ASKS before saving ("Accept Ledger? (Y/N)") instead of committing
            // silently. Ctrl+A is unaffected — it has its own arm far above and still saves outright. Guarded on
            // the prompt not already being up so Enter cannot stack a second confirmation; every other Enter
            // (cascade navigation, drill-in, non-master pages) falls through to ActivateSelected exactly as before.
            // The guard is deliberately SIDE-EFFECT-FREE — it only reads state. RequestMasterAccept (which
            // RAISES the prompt) is called in the body, never in the `when` clause: a pattern-match guard that
            // mutates would raise a confirmation as a side effect of merely testing this arm, and would silently
            // change meaning the moment anyone appended another condition after it.
            // The !IsPickerOpen guard is the D1 fix. With a dropdown OPEN this arm used to fire, so an operator
            // who picked a row and pressed Enter got "Accept Ledger? (Y/N)" over a still-open dropdown instead
            // of their selection (measured on Ledger Creation:
            // `AFTER enter: open=True promptOpen=True promptText='Accept Ledger? (Y/N)'`). Live on all 24
            // IsMasterAcceptScreen screens. Enter now falls through to the dropdown, which owns it.
            case Key.Enter when vm.IsMasterAcceptScreen && !vm.IsAcceptPromptOpen && !IsPickerOpen(e):
                vm.RequestMasterAccept();
                e.Handled = true;
                break;
            // The SAME guard is load-bearing here, and not decoration: without it the arm above merely hands the
            // stolen Enter to this one — the prompt stops appearing but the key is still consumed and the
            // dropdown still never sees it. Both arms must yield for Enter to actually reach the picker.
            case Key.Enter when !IsPickerOpen(e):
                vm.ActivateSelected();
                e.Handled = true;
                break;
            // Left / Esc removes the rightmost column (focus returns to the previous column). Left is a
            // navigation key only when not editing a text field (there it moves the caret).
            //
            // The !IsPickerOpen guard is the SAME D2 fix as on Escape below, and it belongs here for the same
            // reason: this comment calls Left and Esc one pair, and they must be guarded as one pair. Guarding
            // only Escape left this arm reachable with a dropdown open, because !IsTyping tests
            // `e.Source is TextBox` and an open dropdown makes e.Source a ComboBoxItem. Measured, and
            // byte-identical to the D2 work-loss: `BEFORE left: columns=2 screen=LedgerMaster
            // ledgerMasterNull=False dropDownOpen=True` -> `AFTER left: columns=1 screen=Gateway
            // ledgerMasterNull=True` — one Left aimed at the dropdown discarded the half-typed ledger.
            //
            // As with Escape the guard is OPEN and not "focused": a closed picker must still let Left pop the
            // column in one press, or ~157 form screens lose a keyboard exit
            // (Left_on_a_CLOSED_picker_still_pops_the_column_in_one_press locks it).
            case Key.Left when !IsTyping(e) && !IsPickerOpen(e):
                vm.Back();
                e.Handled = true;
                break;
            // The !IsPickerOpen guard is the D2 fix. This arm was completely unguarded, so ONE Escape aimed at
            // closing a dropdown also popped the Miller column and destroyed the in-progress master (measured on
            // Ledger Creation: `BEFORE esc: columns=2 screen=LedgerMaster` -> `AFTER esc: columns=1
            // screen=Gateway ledgerMasterNull=True` — a half-typed ledger discarded by a keystroke the operator
            // aimed at the dropdown). Escape is TWO presses by settled contract: the first closes the dropdown
            // (the ComboBox does that itself once this arm yields), the second reaches Back() and pops.
            //
            // The guard is IsPickerOpen and NOT IsTyping / "a picker is focused": a CLOSED picker leaves Escape
            // unhandled (measured), so if this arm yielded on mere focus there would be no keyboard way out of a
            // form column on ~157 screens.
            case Key.Escape when !IsPickerOpen(e):
                vm.Back();
                e.Handled = true;
                break;

            // F-key button bar (mirrors the right panel).
            case Key.F1: Fire(vm, "F1"); e.Handled = true; break;
            case Key.F2: Fire(vm, "F2"); e.Handled = true; break;
            case Key.F3: Fire(vm, "F3"); e.Handled = true; break;
            case Key.F4: Fire(vm, "F4"); e.Handled = true; break;
            case Key.F5: Fire(vm, "F5"); e.Handled = true; break;
            case Key.F6: Fire(vm, "F6"); e.Handled = true; break;
            case Key.F7: Fire(vm, "F7"); e.Handled = true; break;
            case Key.F8: Fire(vm, "F8"); e.Handled = true; break;
            case Key.F9: Fire(vm, "F9"); e.Handled = true; break;
            // F10 opens the "Other Vouchers" menu (Transactions → Vouchers → Other Vouchers) — the route to the
            // Job Work In/Out Order + Material In/Out screens (Phase 6 slice 8; RQ-45/RQ-53). Menu context only,
            // never while typing in a field.
            case Key.F10 when vm.Company is not null && !IsTyping(e):
                vm.ShowOtherVouchersMenu(); e.Handled = true; break;
            case Key.F11: Fire(vm, "F11"); e.Handled = true; break;
            case Key.F12: Fire(vm, "F12"); e.Handled = true; break;

            // Report quick letters — only on the menu screens (never while entering a voucher /
            // ledger, where the letter is meant for the field, not a report jump).
            case Key.B when CanQuickJump(vm, e): Fire(vm, "B"); e.Handled = true; break;
            case Key.P when CanQuickJump(vm, e): Fire(vm, "P"); e.Handled = true; break;
            case Key.T when CanQuickJump(vm, e): Fire(vm, "T"); e.Handled = true; break;
            case Key.D when CanQuickJump(vm, e): Fire(vm, "D"); e.Handled = true; break;
        }

        // WI-9 / WI-2 — the bare-letter menu arm, LAST on purpose.
        //
        // Placed at the very end of the first-match-wins chain so it cannot shadow a single accelerator that
        // already shipped: every earlier arm (Ctrl+A, Alt+X, the report Alt+F1/F2/F12, the E/O/Y/M/P panels, the
        // B/P/T/D quick-jumps, the F-key bar) gets the keystroke first and only an UNCLAIMED letter reaches here.
        //
        // What it then does depends on the focused column's KIND, which is how WI-2 and WI-9 stop fighting over
        // the same keystroke: on an AUTHORED menu column the letter activates the row that owns it as a hotkey
        // (the letter painted red); on a DATA-DRIVEN picker column it filters the list (type-ahead) instead.
        // MainWindowViewModel.HandleMenuLetter owns that decision and returns false when nothing claimed the
        // letter, leaving the event unhandled so behaviour elsewhere is unchanged.
        if (!e.Handled && !IsTyping(e)
            && e.KeyModifiers == KeyModifiers.None
            && TryGetLetter(e.Key, out var letter)
            && vm.HandleMenuLetter(letter))
        {
            e.Handled = true;
        }
    }

    /// <summary>Maps a bare A–Z key to its letter; false for anything else (digits, F-keys, navigation).</summary>
    private static bool TryGetLetter(Key key, out char letter)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            letter = (char)('A' + (key - Key.A));
            return true;
        }
        letter = '\0';
        return false;
    }

    private static bool IsTyping(KeyEventArgs e) => e.Source is TextBox;

    /// <summary>
    /// True when the keystroke originated inside a picker whose dropdown is currently OPEN — the state in which
    /// Up, Down, Enter, Left and Escape belong to the dropdown, not to this window.
    ///
    /// <para><b>The five arms that consult it, and why they are exactly these five.</b> Up/Down move the
    /// highlight INSIDE the popup (the settled contract that arrows work on every screen, dropdowns included);
    /// Enter takes the highlighted row; Left and Escape are the two documented keyboard exits from a form column
    /// and must not pop it while a popup is up. Every one of those was measured being stolen by this tunnel. The
    /// F-key bar is deliberately NOT guarded — F4 stays Contra with a dropdown open, and a test pins it.</para>
    ///
    /// <para><b>Why the parent walk.</b> Once a dropdown opens, focus moves into the popup, so
    /// <c>e.Source</c> is a <c>ComboBoxItem</c> (measured) and never the <c>ComboBox</c> itself — a plain
    /// <c>e.Source is ComboBox</c> test would miss every case this guard exists for.</para>
    ///
    /// <para><b>Why "open" and not "focused".</b> Left and Escape are the only two keyboard exits from a form
    /// column. A picker that is merely focused but CLOSED must still let both reach <c>Back()</c>, or ~157
    /// screens lose their keyboard route out. Requiring <c>IsDropDownOpen</c> keeps the guard to exactly the
    /// state where the popup has something to do with the key. This is deliberately NOT a widening of
    /// <see cref="IsTyping"/>: that predicate answers a different question (is the operator typing into a text
    /// field) and reaches far more arms.</para>
    ///
    /// <para><b>Scope note.</b> Under Avalonia's headless platform the popup is hosted in the same top-level, so
    /// this window tunnel sees the keystroke and the guard is what stops it. On Win32 the popup may live in a
    /// separate <c>PopupRoot</c>, in which case the tunnel never runs and the guard is simply inert — it cannot
    /// make that case worse.</para>
    /// </summary>
    private static bool IsPickerOpen(KeyEventArgs e)
    {
        for (var c = e.Source as StyledElement; c is not null; c = c.Parent)
            if (c is ComboBox { IsDropDownOpen: true }) return true;
        return false;
    }

    /// <summary>
    /// True when the keystroke originated on a picker that IS focused but whose dropdown is CLOSED — the state
    /// in which V3 ("Enter opens it.") turns Enter into an OPEN gesture. This is the exact complement of
    /// <see cref="IsPickerOpen"/> on the open/closed axis: that guard yields keys to an OPEN popup; this one
    /// arms Enter for a CLOSED, focused picker. Kept a sibling predicate deliberately — it must NOT widen
    /// <see cref="IsTyping"/> (which answers a different question and reaches ~157 screens).
    /// <para><b>Why the focus requirement.</b> A keyboard event's <c>Source</c> is the focused element, so a
    /// closed ComboBox on its logical-parent chain already contains focus; requiring
    /// <c>IsKeyboardFocusWithin</c> as well pins that intent and refuses to fire on a closed picker that merely
    /// happens to be an ancestor of some other focused control. Measured on the real window: a focused closed
    /// picker gives <c>e.Source = ComboBox</c> with <c>IsFocused = IsKeyboardFocusWithin = true</c>.</para>
    /// </summary>
    private static bool IsPickerFocusedClosed(KeyEventArgs e) => FocusedClosedPicker(e) is not null;

    /// <summary>The focused, CLOSED <see cref="ComboBox"/> the key originated on, or null — the single walk
    /// that backs both <see cref="IsPickerFocusedClosed"/> and <see cref="OpenFocusedPicker"/>.</summary>
    private static ComboBox? FocusedClosedPicker(KeyEventArgs e)
    {
        for (var c = e.Source as StyledElement; c is not null; c = c.Parent)
            if (c is ComboBox { IsDropDownOpen: false } cb && cb.IsKeyboardFocusWithin) return cb;
        return null;
    }

    /// <summary>Opens the focused, closed picker the key originated on (the V3 "Enter opens it." action).</summary>
    private static void OpenFocusedPicker(KeyEventArgs e)
    {
        if (FocusedClosedPicker(e) is { } picker) picker.IsDropDownOpen = true;
    }

    /// <summary>
    /// Resolves the inventory-voucher line the key event originated on by walking the control tree up from the
    /// key source to the first element whose DataContext is an <see cref="InventoryVoucherLineViewModel"/> — so
    /// Alt+B on a specific row targets that row. Returns null when the key came from outside any line row.
    /// </summary>
    private static InventoryVoucherLineViewModel? FocusedInventoryLine(KeyEventArgs e)
    {
        for (var c = e.Source as StyledElement; c is not null; c = c.Parent)
            if (c.DataContext is InventoryVoucherLineViewModel line)
                return line;
        return null;
    }

    /// <summary>
    /// Report quick-letters fire only on menu screens, never while typing in a field, and only for a
    /// keystroke carrying NO modifier at all.
    ///
    /// <para><b>The modifier hole this closes (Phase 10.11 S1).</b> This predicate tested only
    /// <c>IsMenuScreen &amp;&amp; !IsTyping(e)</c>, and the <c>switch (e.Key)</c> that consults it tests no
    /// modifiers either — so all four quick-jumps fired for EVERY chord no earlier arm had already claimed.
    /// Measured on Company Select (the one screen they are reachable on: once a company is open
    /// <c>IsGatewayCascade</c> is true and <see cref="MainWindowViewModel.IsMenuScreen"/> is false):
    /// <b>Alt+D opened the Day Book</b>, and so did Ctrl+D and Shift+D; Alt+B/Alt+P/Alt+T opened their
    /// reports too. The fix belongs HERE and not on the four arms — a per-arm fix on D would have left three
    /// survivors.</para>
    ///
    /// <para><b>Why it had to be its own change, ahead of everything else in the phase.</b> A later slice
    /// binds <b>Alt+D to DELETE</b>. Binding a destructive verb on top of a chord that already fires a
    /// navigation would make a stray Alt+D both destructive and ambiguous — which one won would depend on
    /// the screen. The hole is closed first so Alt+D is genuinely unclaimed when delete arrives.</para>
    ///
    /// <para><b>Why <c>== KeyModifiers.None</c> and not merely "no Alt".</b> It is the identical predicate
    /// the WI-2/WI-9 bare-letter menu arm at the end of this handler already uses, so "bare letter" means one
    /// thing in both places. It deliberately excludes Shift as well: a quick-jump is a bare-letter
    /// accelerator, and admitting Shift would leave the same class of hole open on the next chord anyone
    /// binds. Nothing else changes — with no modifier held these four arms behave exactly as before, which
    /// <c>Bare_D_on_company_select_still_opens_the_day_book</c> and its B/P/T sibling pin.</para>
    /// </summary>
    private static bool CanQuickJump(MainWindowViewModel vm, KeyEventArgs e)
        => vm.IsMenuScreen && !IsTyping(e) && e.KeyModifiers == KeyModifiers.None;

    private static void Fire(MainWindowViewModel vm, string key)
    {
        foreach (var b in vm.ButtonBar)
            if (b.Key == key)
            {
                if (b.Enabled) b.Action();
                return;
            }
    }

    private void OnCreateCompanyClick(object? sender, RoutedEventArgs e)
        => Vm?.CreateCompany();

    private void OnAcceptCompanyProfileClick(object? sender, RoutedEventArgs e)
        => Vm?.AlterCompany?.Accept();

    /// <summary>
    /// The voucher-entry screen's on-screen <b>Accept</b> button. Phase 10.11 S5d routed it through the shell's
    /// single accept decision instead of calling <c>VoucherEntry.Accept()</c> directly: the same screen now serves
    /// entry AND alteration, and <c>Accept</c> HARD-REFUSES on an altering one, so the button and Ctrl+A would
    /// have disagreed the moment alteration became reachable.
    /// </summary>
    private void OnAcceptVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.AcceptVoucherEntryOrAlteration();

    /// <summary>
    /// The voucher-entry screen's "Cancel (Esc)" button — it <b>ABANDONS THE ENTRY SCREEN</b> (discards a
    /// half-keyed voucher and pops its column), which is <see cref="MainWindowViewModel.AbandonEntry"/>.
    ///
    /// <para>🔴 <b>It was called <c>OnCancelVoucherClick</c> and that name is now actively wrong.</b> Phase 10.11
    /// S3 renamed the view-model verb <c>CancelVoucher</c> → <c>AbandonEntry</c> because <b>Alt+X now means
    /// CANCEL A POSTED VOUCHER</b> — a different, destructive act on a document that is already on the books — and
    /// S4 adds a real <b>DELETE</b> verb beside it. Leaving a handler named "cancel voucher" that actually abandons
    /// an entry screen put three different meanings on two names, in the one slice where the difference between
    /// "throw away what I am typing", "void a posted document" and "remove a posted document" has to be exact.</para>
    ///
    /// <para><b>Renaming this is not cosmetic: a XAML <c>Click=</c> binds by NAME at RUNTIME</b>, so renaming the
    /// method without renaming the binding (or the reverse) compiles clean, ships, and fails only when a user
    /// clicks the button. Both were changed together, and
    /// <c>XamlClickHandlerBindingTests</c> now proves every <c>Click=</c> in the window resolves to a declared
    /// handler, so the next half-rename is caught by a test instead of by an operator.</para>
    /// </summary>
    private void OnAbandonVoucherEntryClick(object? sender, RoutedEventArgs e)
        => Vm?.AbandonEntry();

    private void OnAddVoucherLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddVoucherLine();

    private void OnAddItemInvoiceLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddItemInvoiceLine();

    private void OnAddAccountingInvoiceLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddAccountingInvoiceLine();

    /// <summary>Removes the Particulars row the clicked "✕" belongs to (the button's own DataContext IS that row).</summary>
    private void OnRemoveAccountingInvoiceLineClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.VoucherEntry is { } entry && (sender as Control)?.DataContext is AccountingInvoiceLineViewModel row)
            entry.RemoveAccountingInvoiceLine(row);
    }

    private void OnToggleItemInvoiceClick(object? sender, RoutedEventArgs e)
        => Vm?.ToggleItemInvoice();

    private void OnToggleAccountingInvoiceClick(object? sender, RoutedEventArgs e)
        => Vm?.ToggleAccountingInvoice();

    private void OnAddAdditionalCostClick(object? sender, RoutedEventArgs e)
        => Vm?.VoucherEntry?.AddAdditionalCostRow();

    private void OnAddTransferAdditionalCostClick(object? sender, RoutedEventArgs e)
        => Vm?.InventoryVoucherEntry?.AddAdditionalCostRow();

    private void OnAcceptInventoryVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.InventoryVoucherEntry?.Accept();

    private void OnCancelInventoryVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.AbandonEntry();

    private void OnAddInventoryLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddInventoryLine();

    private void OnAddInventoryDestinationLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddInventoryDestinationLine();

    private void OnAddBillAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VoucherLineViewModel line })
            Vm?.AddBillAllocation(line);
    }

    private void OnAddCostAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VoucherLineViewModel line })
            Vm?.AddCostAllocation(line);
    }

    /// <summary>
    /// G-1 — "+ Add bill" on the INVOICE-mode Bill-wise panel. Unlike the plain-grid sibling above, the allocations
    /// belong to the screen (the party leg is derived at Accept), so the row is added to the entry view model itself.
    /// </summary>
    private void OnAddInvoiceBillAllocationClick(object? sender, RoutedEventArgs e) =>
        Vm?.VoucherEntry?.AddInvoiceBillAllocation();

    /// <summary>G-1 — "Remove" on an invoice-mode Bill-wise row (keeps at least one while the panel is on).</summary>
    private void OnRemoveInvoiceBillAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BillAllocationRowViewModel row })
            Vm?.VoucherEntry?.RemoveInvoiceBillAllocation(row);
    }

    /// <summary>G-6 — "+ Add particular" on the Single-Entry grid (the many side).</summary>
    private void OnAddSingleEntryParticularClick(object? sender, RoutedEventArgs e) =>
        Vm?.VoucherEntry?.AddSingleEntryParticular();

    private void OnCreateCostCategoryClick(object? sender, RoutedEventArgs e)
        => Vm?.CostCategoryMaster?.Create();

    private void OnCreateCostCentreClick(object? sender, RoutedEventArgs e)
        => Vm?.CostCentreMaster?.Create();

    /// <summary>
    /// The Outstandings "Settle Bills (Alt+A)" button — the same route the Alt+A key takes, so the button and the
    /// accelerator can never do two different things. It OPENS a pre-loaded settlement voucher; it posts nothing.
    /// </summary>
    private void OnSettleBillsClick(object? sender, RoutedEventArgs e)
        => Vm?.OpenSettlementVoucherFromOutstandings();

    private void OnCreateLedgerClick(object? sender, RoutedEventArgs e)
        => Vm?.LedgerMaster?.Create();

    private void OnCreateAccountGroupClick(object? sender, RoutedEventArgs e)
        => Vm?.AccountGroupMaster?.Create();

    /// <summary>W2-20 — the pointer equivalent of Ctrl+A on the multi-master grid (same all-or-nothing Accept).</summary>
    private void OnMultiMasterCreateClick(object? sender, RoutedEventArgs e)
        => Vm?.MultiMasterCreate?.Accept();

    /// <summary>
    /// W2-14 — puts the caret in the Go To search box the instant the overlay is realised, so Alt+G is followed
    /// by typing and nothing else. Without this the keystrokes after Alt+G would go to whatever held focus
    /// behind the overlay, which is the screen the operator is trying to leave.
    /// </summary>
    private void OnGoToSearchBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box) box.Focus();
    }

    private void OnAddBudgetLineClick(object? sender, RoutedEventArgs e)
        => Vm?.BudgetMaster?.AddLine();

    private void OnCreateBudgetClick(object? sender, RoutedEventArgs e)
        => Vm?.BudgetMaster?.Create();

    private void OnReconcileBankClick(object? sender, RoutedEventArgs e)
        => Vm?.BankReconciliation?.Reconcile();

    private void OnImportBankStatementClick(object? sender, RoutedEventArgs e)
        => Vm?.BankStatementImport?.Import();

    private void OnCreateScenarioClick(object? sender, RoutedEventArgs e)
        => Vm?.ScenarioMaster?.Create();

    private void OnCreateCurrencyClick(object? sender, RoutedEventArgs e)
        => Vm?.CurrencyMaster?.CreateCurrency();

    private void OnCreateExchangeRateClick(object? sender, RoutedEventArgs e)
        => Vm?.CurrencyMaster?.CreateRate();

    private void OnCreateStockGroupClick(object? sender, RoutedEventArgs e)
        => Vm?.StockGroupMaster?.Create();

    private void OnCreateStockCategoryClick(object? sender, RoutedEventArgs e)
        => Vm?.StockCategoryMaster?.Create();

    private void OnCreateUnitClick(object? sender, RoutedEventArgs e)
        => Vm?.UnitMaster?.Create();

    private void OnCreateGodownClick(object? sender, RoutedEventArgs e)
        => Vm?.GodownMaster?.Create();

    private void OnCreateStockItemClick(object? sender, RoutedEventArgs e)
        => Vm?.StockItemMaster?.Create();

    private void OnCreateBatchClick(object? sender, RoutedEventArgs e)
        => Vm?.BatchMaster?.Create();

    private void OnAcceptBatchAllocationClick(object? sender, RoutedEventArgs e)
        => Vm?.AcceptCurrent();

    private void OnCreateBomClick(object? sender, RoutedEventArgs e)
        => Vm?.BomMaster?.Create();

    private void OnCreatePriceLevelClick(object? sender, RoutedEventArgs e)
        => Vm?.PriceLevels?.Create();

    private void OnCreateReorderLevelClick(object? sender, RoutedEventArgs e)
        => Vm?.ReorderLevels?.Create();

    private void OnSavePriceListClick(object? sender, RoutedEventArgs e)
        => Vm?.PriceLists?.Save();

    private void OnAddPriceListSlabClick(object? sender, RoutedEventArgs e)
        => Vm?.PriceLists?.AddSlabRow();

    private void OnAddBomLineClick(object? sender, RoutedEventArgs e)
        => Vm?.BomMaster?.AddBlankLine();

    private void OnAcceptManufacturingJournalClick(object? sender, RoutedEventArgs e)
        => Vm?.ManufacturingJournalEntry?.Accept();

    private void OnCancelManufacturingJournalClick(object? sender, RoutedEventArgs e)
        => Vm?.AbandonEntry();

    private void OnAddManufacturingCostClick(object? sender, RoutedEventArgs e)
        => Vm?.ManufacturingJournalEntry?.AddBlankAdditionalCost();

    // POS Billing (Phase 6 slice 7; RQ-38..RQ-44) — accept / cancel / add line / toggle payment mode / tax analysis.
    private void OnAcceptPosClick(object? sender, RoutedEventArgs e)
        => Vm?.AcceptPosBillingOrAlteration();

    private void OnCancelPosClick(object? sender, RoutedEventArgs e)
        => Vm?.AbandonEntry();

    private void OnAddPosItemLineClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.AddItemLine();

    private void OnTogglePosPaymentModeClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.TogglePaymentMode();

    private void OnShowPosTaxAnalysisClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.ShowTaxAnalysis();

    // Job Work In/Out Order (Phase 6 slice 8; RQ-47) — accept / cancel / add component line.
    private void OnAcceptJobWorkOrderClick(object? sender, RoutedEventArgs e)
        => Vm?.JobWorkOrderEntry?.Accept();

    private void OnCancelJobWorkOrderClick(object? sender, RoutedEventArgs e)
        => Vm?.AbandonEntry();

    private void OnAddJobWorkLineClick(object? sender, RoutedEventArgs e)
        => Vm?.JobWorkOrderEntry?.AddBlankLine();

    // Material In/Out movement (Phase 6 slice 8; RQ-48) — accept / cancel / add source & destination lines.
    private void OnAcceptMaterialClick(object? sender, RoutedEventArgs e)
        => Vm?.MaterialMovementEntry?.Accept();

    private void OnCancelMaterialClick(object? sender, RoutedEventArgs e)
        => Vm?.AbandonEntry();

    private void OnAddMaterialSourceLineClick(object? sender, RoutedEventArgs e)
        => Vm?.MaterialMovementEntry?.AddSourceLine();

    private void OnAddMaterialDestinationLineClick(object? sender, RoutedEventArgs e)
        => Vm?.MaterialMovementEntry?.AddDestinationLine();

    /// <summary>Opens the batch-allocation sub-screen (RQ-3) for the inventory-voucher line the button sits on.</summary>
    private void OnOpenBatchAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.InventoryVoucherLineViewModel line })
            Vm?.InventoryVoucherEntry?.RequestBatchAllocation(line);
    }

    /// <summary>
    /// G-5 — opens the batch-allocation sub-screen for the ITEM-INVOICE (Purchase F9 / Sales F8) line the button
    /// sits on. A separate handler from the stock-screen one because the two grids are hosted by different entry
    /// view models; the line type is shared.
    /// </summary>
    private void OnOpenItemInvoiceBatchAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.InventoryVoucherLineViewModel line })
            Vm?.VoucherEntry?.RequestBatchAllocation(line);
    }

    private void OnApplyGstClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.Apply();

    // GST Rate Setup (Phase 9 slice 1) — seed the GST 2.0 defaults, append a dated rate window, append a cess window.
    private void OnSeedAdvancedGstClick(object? sender, RoutedEventArgs e)
        => Vm?.GstRateSetup?.SeedDefaults();

    private void OnAddRateHistoryClick(object? sender, RoutedEventArgs e)
        => Vm?.GstRateSetup?.AddRateHistory();

    private void OnAddCessRateClick(object? sender, RoutedEventArgs e)
        => Vm?.GstRateSetup?.AddCess();

    // TDS/TCS (Phase 7 slice 1) — F11 Enable TDS / Enable TCS + the two Statutory-master Create actions.
    private void OnApplyTdsClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyTds();

    private void OnApplyTcsClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyTcs();

    // Provident Fund (Phase 8 slice 4) — F11 Enable Provident Fund + the PF ECR export / save-return actions.
    private void OnApplyPfClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyPf();

    private void OnExportEcrClick(object? sender, RoutedEventArgs e)
        => Vm?.PfEcrReport?.ExportEcr();

    private void OnSaveReturnPfEcrClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnPfEcr();

    // Employees' State Insurance (Phase 8 slice 5) — F11 Enable ESI + the ESI contribution export / save-return.
    private void OnApplyEsiClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyEsi();

    private void OnExportEsiContributionClick(object? sender, RoutedEventArgs e)
        => Vm?.EsiContributionReport?.ExportReturn();

    private void OnSaveReturnEsiContributionClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnEsiContribution();

    // Professional Tax (Phase 8 slice 6) — F11 Enable Professional Tax + the PT register export / save-return actions.
    private void OnApplyPtClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyPt();

    private void OnExportProfessionalTaxClick(object? sender, RoutedEventArgs e)
        => Vm?.ProfessionalTaxRegister?.ExportRegister();

    private void OnSaveReturnProfessionalTaxClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnProfessionalTax();

    // Gratuity + statutory Bonus (Phase 8 slice 9) — F11 Enable Gratuity / Enable Bonus + the Gratuity provision post.
    private void OnApplyGratuityClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyGratuity();

    private void OnApplyBonusClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplyBonus();

    private void OnPostGratuityProvisionClick(object? sender, RoutedEventArgs e)
        => Vm?.GratuityProvisionRegister?.PostProvision();

    // §192 salary TDS (Phase 8 slice 7) — F11 Enable Salary TDS, the Form-12BB declaration save, and the
    // Form 24Q / Form 16 export + save-return actions.
    private void OnApplySalaryTdsClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.ApplySalaryTds();

    private void OnSaveTaxDeclarationClick(object? sender, RoutedEventArgs e)
        => Vm?.TaxDeclarationMaster?.Save();

    private void OnExportForm24QClick(object? sender, RoutedEventArgs e)
        => Vm?.Form24Q?.ExportFvu();

    private void OnSaveReturnForm24QClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm24Q();

    private void OnExportForm16Click(object? sender, RoutedEventArgs e)
        => Vm?.Form16?.ExportPdf();

    private void OnSaveReturnForm16Click(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm16();

    private void OnCreateNatureOfPaymentClick(object? sender, RoutedEventArgs e)
        => Vm?.NatureOfPaymentMaster?.Create();

    private void OnCreateNatureOfGoodsClick(object? sender, RoutedEventArgs e)
        => Vm?.NatureOfGoodsMaster?.Create();

    // Payroll masters (Phase 8 slice 1) — the five Create actions + the Payroll-Unit Simple/Compound toggle.
    private void OnCreateEmployeeCategoryClick(object? sender, RoutedEventArgs e)
        => Vm?.EmployeeCategoryMaster?.Create();

    private void OnCreateEmployeeGroupClick(object? sender, RoutedEventArgs e)
        => Vm?.EmployeeGroupMaster?.Create();

    private void OnCreateEmployeeClick(object? sender, RoutedEventArgs e)
        => Vm?.EmployeeMaster?.Create();

    private void OnCreatePayrollUnitClick(object? sender, RoutedEventArgs e)
        => Vm?.PayrollUnitMaster?.Create();

    private void OnPayrollUnitSimpleClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.PayrollUnitMaster is { } m) m.IsCompound = false;
    }

    private void OnPayrollUnitCompoundClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.PayrollUnitMaster is { } m) m.IsCompound = true;
    }

    private void OnCreateAttendanceTypeClick(object? sender, RoutedEventArgs e)
        => Vm?.AttendanceTypeMaster?.Create();

    // Pay Head master (Phase 8 slice 2) — the Create action + the computation basis/slab editor add/remove.
    private void OnCreatePayHeadClick(object? sender, RoutedEventArgs e)
        => Vm?.PayHeadMaster?.Create();

    private void OnAddBasisComponentClick(object? sender, RoutedEventArgs e)
        => Vm?.PayHeadMaster?.AddBasisComponent();

    private void OnRemoveBasisComponentClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.PayHeadMaster is { } m && (sender as Control)?.DataContext is PayHeadBasisRow row)
            m.RemoveBasisComponent(row);
    }

    private void OnAddSlabClick(object? sender, RoutedEventArgs e)
        => Vm?.PayHeadMaster?.AddSlab();

    private void OnRemoveSlabClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.PayHeadMaster is { } m && (sender as Control)?.DataContext is PayHeadSlabRow row)
            m.RemoveSlab(row);
    }

    // Salary Details / structure master (Phase 8 slice 2) — the Save action.
    private void OnSaveSalaryStructureClick(object? sender, RoutedEventArgs e)
        => Vm?.SalaryDetails?.Save();

    // Attendance / Production voucher (Phase 8 slice 3) — add a line + record the entries.
    private void OnAddAttendanceLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AttendanceVoucher?.AddBlankRow();

    private void OnRecordAttendanceClick(object? sender, RoutedEventArgs e)
        => Vm?.AttendanceVoucher?.Accept();

    // Payroll voucher (Phase 8 slice 3) — compute the salary breakdown, then post the balanced voucher.
    private void OnComputePayrollClick(object? sender, RoutedEventArgs e)
        => Vm?.PayrollVoucher?.Compute();

    private void OnPostPayrollClick(object? sender, RoutedEventArgs e)
        => Vm?.PayrollVoucher?.Accept();

    private void OnDepositTdsStatPaymentClick(object? sender, RoutedEventArgs e)
        => Vm?.TdsStatPayment?.Deposit();

    private void OnExportFvuForm26QClick(object? sender, RoutedEventArgs e)
        => Vm?.Form26Q?.ExportFvu();

    private void OnSaveReturnForm26QClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm26Q();

    private void OnDepositTcsStatPaymentClick(object? sender, RoutedEventArgs e)
        => Vm?.TcsStatPayment?.Deposit();

    private void OnExportFvuForm27EQClick(object? sender, RoutedEventArgs e)
        => Vm?.Form27EQ?.ExportFvu();

    private void OnSaveReturnForm27EQClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm27EQ();

    // Phase 7 slice 7 — TDS/TCS certificates + control chart: Export PDF (Ctrl+A) / Save & Return (Alt+B).
    private void OnExportPdfForm16AClick(object? sender, RoutedEventArgs e)
        => Vm?.Form16A?.ExportPdf();

    private void OnSaveReturnForm16AClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm16A();

    private void OnExportPdfForm27DClick(object? sender, RoutedEventArgs e)
        => Vm?.Form27D?.ExportPdf();

    private void OnSaveReturnForm27DClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm27D();

    private void OnExportPdfForm27AClick(object? sender, RoutedEventArgs e)
        => Vm?.Form27A?.ExportPdf();

    private void OnSaveReturnForm27AClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveReturnForm27A();

    private void OnApplyReportConfigClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyReportConfig();

    private void OnApplyPrintConfigClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyPrintConfig();

    private void OnApplyExportClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyExport();

    private void OnApplyExportDataClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyExportData();

    private void OnApplyImportDataClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyImport();

    // ---- The file / folder chooser (census row 13.10 / T1-20) ----

    /// <summary>
    /// The "Browse…" button on every path-carrying panel. The same entry point as Alt+B, so the button and the
    /// chord can never drift apart.
    /// </summary>
    private void OnBrowseForPathClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) _ = BrowseForPathAsync(vm);
    }

    /// <summary>
    /// Asks the view model WHAT path the open screen needs, asks the operating system FOR it, and hands the answer
    /// back to the view model. Every product decision is on the view-model side of this method; the only thing
    /// that happens here is the dialog.
    ///
    /// <para>A cancelled dialog returns null and <see cref="MainWindowViewModel.ApplyBrowsedPath"/> changes
    /// nothing — the typed path stays exactly as it was, which matters most on the Restore panel, where that path
    /// is the archive about to overwrite a whole company.</para>
    ///
    /// <para>The screen is re-checked after the await: a dialog is modal to the OS, not to us, and the operator
    /// could have been moved on by anything that ran meanwhile. Writing a chosen path into whatever screen
    /// happens to be open by then is exactly the class of defect this feature exists to remove.</para>
    /// </summary>
    private async System.Threading.Tasks.Task BrowseForPathAsync(MainWindowViewModel vm)
    {
        if (vm.BrowseRequest() is not { } request) return;

        var screenAsked = vm.CurrentScreen;
        string? picked;
        try
        {
            picked = await FilePathPicker.PickAsync(request);
        }
        catch (Exception)
        {
            // A platform whose dialog fails is a reason to leave the typed path alone, not to crash the shell:
            // the TextBox is still there and still works, so the feature degrades to exactly what shipped before.
            return;
        }

        if (picked is null) return;
        if (vm.CurrentScreen != screenAsked) return;

        vm.ApplyBrowsedPath(picked);
    }

    // ---- Data -> Backup / Restore (the R-7 carve-out) ----

    private void OnApplyBackupClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyBackup();

    private void OnExamineRestoreClick(object? sender, RoutedEventArgs e)
        => Vm?.ExamineRestore();

    private void OnApplyRestoreClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyRestore();

    private void OnSaveEmailClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) SaveEmailToDocuments(vm);
    }

    private void OnSaveSmtpClick(object? sender, RoutedEventArgs e)
        => Vm?.SaveSmtpSettings();

    private void OnApplyReportSortFilterClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyReportSortFilter();

    private void OnClearReportSortFilterClick(object? sender, RoutedEventArgs e)
        => Vm?.ClearReportSortFilter();

    private void OnApplyAddComparisonColumnClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyAddComparisonColumn();

    private void OnApplyAutoColumnsClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyAutoColumns();

    private void OnClearComparativeClick(object? sender, RoutedEventArgs e)
        => Vm?.ClearComparative();

    private void OnApplySaveViewClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplySaveView();

    private void OnOpenSavedViewClick(object? sender, RoutedEventArgs e)
        => Vm?.OpenSelectedSavedView();

    /// <summary>
    /// "Save PDF" on the Print-Preview panel: writes the rendered bytes to a file. The renderer is disk-free;
    /// this thin layer just picks a path (the user's Documents folder with a report-derived file name) and calls
    /// the VM, which writes the stream. A full save-file dialog can replace the path choice in a later slice.
    /// </summary>
    private void OnSavePrintPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) SavePrintPreviewToDocuments(vm);
    }

    /// <summary>Picks a Documents-folder path from the report title and asks the VM to write the rendered PDF bytes.</summary>
    private static void SavePrintPreviewToDocuments(MainWindowViewModel vm)
    {
        if (vm.PrintPreview is not { } preview) return;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = SafeFileName(preview.ReportTitle) + ".pdf";
        vm.SavePrintPreview(Path.Combine(dir, name));
    }

    /// <summary>
    /// "Save .eml" on the E-Mail compose panel: writes the byte-stable message (with the exported-PDF attachment)
    /// to a Documents-folder path derived from the document title. The composer is disk-free; this thin layer just
    /// picks the path and calls the VM. A full save-file dialog can replace the path choice in a later slice.
    /// Nothing is sent — the .eml is handed to the OS mail client by the user.
    /// </summary>
    private static void SaveEmailToDocuments(MainWindowViewModel vm)
    {
        if (vm.EmailCompose is not { } compose) return;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = SafeFileName(compose.DocumentTitle) + ".eml";
        vm.SaveEmail(Path.Combine(dir, name));
    }

    /// <summary>Turns a report title into a safe file-name stem (invalid path chars → '_'; blank → "Report").</summary>
    private static string SafeFileName(string title)
    {
        var stem = string.IsNullOrWhiteSpace(title) ? "Report" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }

    private void OnDeleteSavedViewClick(object? sender, RoutedEventArgs e)
        => Vm?.DeleteSelectedSavedView();

    private void OnUnitSimpleClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.UnitMaster is { } m) m.IsCompound = false;
    }

    private void OnUnitCompoundClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.UnitMaster is { } m) m.IsCompound = true;
    }

    private void OnRecomputeForexClick(object? sender, RoutedEventArgs e)
        => Vm?.ForexReport?.Recompute();

    private void OnBookForexAdjustmentClick(object? sender, RoutedEventArgs e)
        => Vm?.ForexReport?.BookAdjustment();

    // ---------------------------------------------------------------- Stock-Summary drill → Stock Item Movement

    /// <summary>Double-click a Stock-Summary item row → open that item's Stock Item Movement report.</summary>
    private void OnStockSummaryDrill(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ReportRow row })
            Vm?.DrillReport(row);
    }

    /// <summary>
    /// Enter on the highlighted Stock-Summary row drills into that item's Stock Item Movement report
    /// (keyboard-first). Handled here (and marked handled) so it does not bubble to the cascade driver.
    /// </summary>
    private void OnStockSummaryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ListBox { SelectedItem: ReportRow row } && row.CanDrill)
        {
            Vm?.DrillReport(row);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- RQ-7 accounting-report drill (TB/BS/P&L/Day Book)

    /// <summary>Double-click an accounting-report row → drill (TB/BS/P&amp;L ledger → its vouchers; Day Book → the voucher).</summary>
    private void OnAccountingReportDrill(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ReportRow row })
            Vm?.DrillReport(row);
    }

    /// <summary>
    /// Enter on the highlighted accounting-report row drills into the report's per-kind target (keyboard-first).
    /// A no-op on a non-drillable row. Marked handled so it does not bubble to the cascade driver.
    /// </summary>
    private void OnAccountingReportKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ListBox { SelectedItem: ReportRow row } && row.CanDrill)
        {
            Vm?.DrillReport(row);
            e.Handled = true;
        }
    }
}
