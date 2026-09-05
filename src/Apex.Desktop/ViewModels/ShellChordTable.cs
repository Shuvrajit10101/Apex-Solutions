using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// ONE shell chord: the keystroke, the context it is allowed to fire in, and what it does.
///
/// <para><b>Why a record and not another arm in the if-chain.</b> <c>MainWindow.OnKeyDown</c> is a ~55-arm
/// first-match-wins chain whose ORDERING IS LOAD-BEARING and is documented in ~40 comment blocks. Adding a
/// chord there means choosing an index correctly; re-pointing one means moving an arm past other arms that
/// may shadow it. Neither is reviewable, and neither is a one-line change. Every chord in
/// <see cref="ShellChordTable.Table"/> is re-pointable by editing exactly one literal, which is the property
/// the open chord ruling needs in order to be cheap to apply.</para>
///
/// <para>🔴 <b><see cref="Modifiers"/> is matched EXACTLY (<c>==</c>), never with <c>HasFlag</c>.</b> This is
/// the keystroke file's own hard-won convention. <c>HasFlag</c> matching is what makes
/// <c>Ctrl+Alt+I</c> fire the <c>Ctrl+I</c> arm, and — measured on <c>main</c> before this table — it is what
/// silently aliased <c>Alt+F3</c> and <c>Ctrl+F3</c> onto the bare-<c>F3</c> arm, because that arm
/// (<c>case Key.F3:</c> in the trailing switch) carries no modifier guard at all and no <c>F3</c> case exists
/// in either the Control or the Alt F-key block above it. Exact matching is therefore also what makes
/// claiming <c>Alt+F3</c> and <c>Ctrl+F3</c> SAFE: bare <c>F3</c> still falls through to its own arm.</para>
/// </summary>
/// <param name="Id">Canonical, human-readable chord id — e.g. <c>"Ctrl+I"</c>. Used in tests and messages.</param>
/// <param name="Key">The physical key.</param>
/// <param name="Modifiers">The modifier set, matched exactly.</param>
/// <param name="CanFire">The context predicate. False ⇒ the table does not claim the keystroke and it falls
/// through to the legacy chain, so an incumbent arm on the same chord in another context keeps working.</param>
/// <param name="Fire">What the chord does. Only ever called when <paramref name="CanFire"/> is true.</param>
public sealed record ShellChord(
    string Id,
    Key Key,
    KeyModifiers Modifiers,
    Func<MainWindowViewModel, bool> CanFire,
    Action<MainWindowViewModel> Fire);

/// <summary>
/// THE SHELL CHORD TABLE — the single, ordered, testable list of top-level navigation chords.
///
/// <para><b>Where it is consulted.</b> <c>MainWindow.OnKeyDown</c> walks this table ONCE, immediately after the
/// master-accept-prompt arm and before the first legacy arm. First match wins; a match sets
/// <c>e.Handled</c> and returns. Nothing already in the chain moves, so the ~40 documented ordering decisions
/// below the insertion point are untouched.</para>
///
/// <para><b>Why the insertion point is safe for exactly these chords.</b> The two guards the chain relies on
/// above the legacy arms are the accept prompt (answered before this table runs) and the open-dropdown guard
/// (<c>IsPickerOpen</c>), which protects Up / Down / Enter / Left / Escape. None of the chords here is one of
/// those five keys, so none can steal a keystroke from an open picker. The chords are also all
/// modifier chords, so they do not compete with type-ahead or with a focused <c>TextBox</c> — the incumbent
/// <c>Ctrl+I</c> arm this table replaces had no typing guard either, so this is not a behaviour change.</para>
///
/// <para><b>Fidelity (Ruling 14 tier 1).</b> Every chord below is quoted verbatim from the vendor's own
/// shortcut documentation at <c>help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/</c> ("How to Use
/// Keyboard Shortcuts in TallyPrime"). Nothing here is invented; where the vendor is silent the chord is
/// absent.</para>
/// </summary>
public static class ShellChordTable
{
    /// <summary>
    /// The chords, in match order.
    ///
    /// <para>🔴 <b>Each entry's <c>CanFire</c> is the whole of its arbitration.</b> A chord that another
    /// feature already owns in some context (today: <c>Alt+K</c>, held by Saved Views inside a report) is
    /// scoped OUT here rather than the incumbent being deleted, so no shipped feature loses its only door.
    /// Handing such a chord over completely is then a one-line edit to that predicate.</para>
    /// </summary>
    public static IReadOnlyList<ShellChord> Table { get; } = new List<ShellChord>
    {
        // ── Ctrl+G — Switch To ────────────────────────────────────────────────────────────────────────────
        // Vendor, verbatim: "To switch to a different report, and create masters and vouchers in the flow of
        // work." It is NOT a multi-company chord (that is F3 / Alt+F3 / Ctrl+F3 below); it is the jump-anywhere
        // sibling of Go To, and its ONE documented difference from Go To is that it does not leave a return
        // path. Key.G returned zero hits in the whole of src/Apex.Desktop before this entry, so nothing is
        // narrowed by taking it.
        new("Ctrl+G", Key.G, KeyModifiers.Control,
            vm => vm.Company is not null,
            vm => vm.OpenSwitchTo()),

        // ── Alt+K — Company menu ──────────────────────────────────────────────────────────────────────────
        // Vendor, verbatim: "To open the company menu with the list of actions related to managing your
        // company."
        // 🔴 SCOPED OUT OF REPORT CONTEXT ON PURPOSE. Saved Views (census 14.7) is bound to Alt+K on a report
        // and has no menu row anywhere, so Alt+K is its ONLY door: claiming the chord there would delete a
        // shipped feature rather than move it. Outside report context the chord is unbound on main and the
        // vendor takes it. Handing it over entirely, once Saved Views has a menu row, is deleting
        // "&& !vm.IsReportContext" from this line.
        new("Alt+K", Key.K, KeyModifiers.Alt,
            vm => vm.Company is not null && !vm.IsReportContext,
            vm => vm.OpenCompanyMenu()),

        // ── Alt+F3 — Select Company ───────────────────────────────────────────────────────────────────────
        // Vendor, verbatim: "To select and open another company located in the same folder or other data
        // paths."
        // 🔴 THIS IS A NARROWING, NOT AN ADDITION, and that is a finding this table records rather than
        // hides. On main, Alt+F3 fell through to `case Key.F3:` in the trailing switch (no modifier guard,
        // and no F3 case in the Alt F-key block above it) and fired the button bar's F3 action. Nothing
        // documented or advertised that alias, but it existed.
        new("Alt+F3", Key.F3, KeyModifiers.Alt,
            _ => true,
            vm => vm.ShowCompanySelect()),

        // ── Ctrl+F3 — Shut Company ────────────────────────────────────────────────────────────────────────
        // Vendor, verbatim: "To shut the currently loaded companies."
        // Same narrowing as Alt+F3 above (no F3 case in the Control F-key block either). The plural in the
        // vendor's text is not reachable here: this application holds exactly one company open
        // (MainWindowViewModel.Company is a single nullable field), so Shut is the degenerate singular and
        // the company menu's own row says so.
        new("Ctrl+F3", Key.F3, KeyModifiers.Control,
            vm => vm.Company is not null,
            vm => vm.ShutCompany()),

        // ── Ctrl+I — More Details ─────────────────────────────────────────────────────────────────────────
        // Vendor, verbatim: "To add more details to a master or voucher for the current instance", and on the
        // Contra Register page: "press Ctrl+I (More Details) to enter any of the values WITHOUT ACTIVATING THE
        // OPTIONS IN F12 (Configure)."
        // 🔴 RELEASED FROM THE ITEM-INVOICE TOGGLE, AND NO CAPABILITY IS LOST. On main this chord ran
        // vm.ToggleItemInvoice() with NO context guard whatsoever, i.e. it was swallowed app-wide on every
        // screen including the ones where the toggle is a no-op. The verb keeps Ctrl+H, which is its
        // vendor-attested chord ("To change mode – open vouchers in different modes", Vouchers & Masters
        // section) and which is already bound and already tested. See MoreDetailsViewModel.
        new("Ctrl+I", Key.I, KeyModifiers.Control,
            vm => vm.CanOpenMoreDetails,
            vm => vm.OpenMoreDetails()),
    };

    /// <summary>
    /// The first chord in <see cref="Table"/> that matches this keystroke AND whose context predicate is
    /// satisfied, or null. Null means the keystroke is NOT claimed and must fall through to the legacy chain.
    /// </summary>
    public static ShellChord? Match(MainWindowViewModel vm, Key key, KeyModifiers modifiers) =>
        vm is null
            ? null
            : Table.FirstOrDefault(c => c.Key == key && c.Modifiers == modifiers && c.CanFire(vm));
}
