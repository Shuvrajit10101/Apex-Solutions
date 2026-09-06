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
        //
        // 🔴 THE PREDICATE IS `HasLiveCompanyShell`, NOT `Company is not null`, AND THE DIFFERENCE WAS A BLANK
        // WINDOW. On Company Select (bare F3, the button bar's Company action, or the Alt+F3 entry below) the
        // company stays LOADED while ShowCompanySelect's LeaveCascade() empties and hides the cascade region.
        // `Company is not null` is true there, so Ctrl+G pushed this panel into a region nothing draws and set
        // CurrentScreen to it: two keystrokes from the Gateway to a blank window that owned the keyboard.
        new("Ctrl+G", Key.G, KeyModifiers.Control,
            vm => vm.HasLiveCompanyShell,
            vm => vm.OpenSwitchTo()),

        // ── Alt+K — Company menu ──────────────────────────────────────────────────────────────────────────
        // Vendor, verbatim: "To open the company menu with the list of actions related to managing your
        // company."
        // 🔴 SCOPED OUT OF REPORT CONTEXT ON PURPOSE. Saved Views (census 14.7) is bound to Alt+K on a report
        // and has no menu row anywhere, so Alt+K is its ONLY door: claiming the chord there would delete a
        // shipped feature rather than move it. Outside report context the chord is unbound on main and the
        // vendor takes it. Handing it over entirely, once Saved Views has a menu row, is deleting
        // "&& !vm.IsReportContext" from this line.
        // 🔴 And the same `HasLiveCompanyShell` narrowing as Ctrl+G above, for the same measured reason: Alt+F3
        // then Alt+K put this MENU COLUMN into the hidden cascade region and left the shell blank.
        new("Alt+K", Key.K, KeyModifiers.Alt,
            vm => vm.HasLiveCompanyShell && !vm.IsReportContext,
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
        //
        // 🔴 THIS ENTRY IS DELIBERATELY THE ONE THAT KEEPS THE WEAK `Company is not null` PREDICATE, and the
        // reason is the opposite of laziness. Shut is DESTRUCTIVE — it reaches ClearSubScreens — and a review
        // found it firing on a half-keyed voucher with no guard at all. The guard for that lives in the VERB
        // (MainWindowViewModel.ShutCompany, which refuses over MainWindowViewModel.HasUnsavedEntryWork and says
        // why), NOT here, because a false CanFire does not make the keystroke safe: it makes the table not CLAIM
        // it, and an unclaimed Ctrl+F3 falls through to `case Key.F3:` in the window's trailing switch — no
        // modifier guard — which runs ShowCompanySelect and destroys the same voucher by a longer route. Claimed
        // + refused is the only shape that consumes the keystroke AND keeps the work.
        new("Ctrl+F3", Key.F3, KeyModifiers.Control,
            vm => vm.Company is not null,
            vm => vm.ShutCompany()),

        // ── Ctrl+I — More Details — DELIBERATELY NOT IN THIS TABLE ───────────────────────────────────────
        // 🔴 Census 14.4 (More Details) is the vendor's Ctrl+I: "To add more details to a master or voucher for
        // the current instance." It is NOT claimed here, and the omission is a finding rather than an
        // oversight — the reasoning belongs beside the table so the next agent does not "fix" it.
        //
        // Taking Ctrl+I means taking it FROM vm.ToggleItemInvoice(), and the case for that was argued on the
        // premise that it "costs nothing, because Ctrl+H already carries mode switching". MEASURED IN THIS
        // TREE, THAT PREMISE IS FALSE:
        //   • Ctrl+H -> ChangeMode() is a THREE-WAY CYCLE: As Voucher -> Item Invoice -> Accounting Invoice.
        //   • Ctrl+I -> ToggleItemInvoice() is a TWO-WAY toggle that never lands in Accounting mode, and
        //     tests/Apex.Desktop.Tests/ServiceAccountingInvoiceKeyboardTests.CtrlI_stays_a_two_way_item_toggle
        //     locks exactly that, deliberately, alongside a sibling test that locks Ctrl+H's three-way cycle.
        // They are different verbs. Releasing Ctrl+I therefore does not re-home a capability, it DELETES the
        // only keyboard door to one: ToggleItemInvoice's sole surviving caller would be a mouse Click handler
        // (MainWindow.axaml:2202) in a keyboard-first product.
        //
        // That trade may well be right on fidelity grounds — our two-way toggle is an Apex invention the vendor
        // does not attest, and docs/full-clone-census.md's T2-14 already grades the binding as wrong. But that
        // same cell says "Chord ruling required — see U-6. OPEN.", and the wave-7 design lists it as decision
        // D-1, OWED TO THE USER. An open ruling is not a build agent's to take, least of all by deleting the
        // shipped test that guards the incumbent. 14.4 waits for the ruling.
        //
        // 🔴 AND THE OBVIOUS ESCAPE HATCH IS CLOSED — MEASURED, so the next agent does not spend a session
        // re-deriving it. The tempting compromise is to CONTEXT-PARTITION Ctrl+I: leave it with the toggle on
        // an invoiceable Purchase/Sales voucher (where ToggleItemInvoice is meaningful) and give it to More
        // Details everywhere else, where the incumbent arm is a dead swallow anyway. That does not work here,
        // because More Details would have NOTHING TO SHOW on the surface it would be given:
        //   • This application has exactly TWO screen-option knobs that gate an optional field group —
        //     VoucherEntryViewModel.UseDefaultBillWiseAllocation (:304) and .UseBatchWiseDetails (:640).
        //     Every other bool on that screen is computed panel state, not an operator knob.
        //   • BOTH are reachable only in invoice mode on a Purchase/Sales: the bill-wise row needs
        //     InvoiceBillWiseApplies, which requires ShowInvoiceOverlay; the batch row needs
        //     CanUseBatchWiseDetails, which is `CanBeItemInvoice && company.MaintainBatchwiseDetails`.
        //   • So the ONLY surface where More Details has a row to offer is EXACTLY the surface the incumbent
        //     occupies. A partitioned Ctrl+I would open an always-empty panel — a dead feature dressed as a
        //     live one, which is worse than not shipping the row.
        // The vendor's own attested example (the Contra register) has no F12-gated field group in this build
        // at all: our Single/Double-entry switch is Ctrl+H, deliberately NOT an F12 flag (see the remarks at
        // VoucherEntryViewModel:113-133). There is therefore no honest half-measure — 14.4 needs the ruling,
        // and it must ship WITH a keyboard door for whatever the item-invoice toggle becomes.
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
