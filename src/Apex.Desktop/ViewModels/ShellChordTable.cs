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

        // ── Ctrl+I — More Details (census 14.4) ──────────────────────────────────────────────────────────
        // Vendor, verbatim: "To add more details to a master or voucher for the current instance", listed in
        // the Right button area at help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/.
        //
        // 🔴 CLAIMED UNDER USER RULING 17 (2026-09-06), WHICH CLOSED THE OPEN U-6 CHORD RULING. Everything
        // below this entry is the RECORD OF WHY THIS ROW WAITED — it is deliberately preserved rather than
        // deleted, because it is also the map of what the re-homing had to answer for.
        //
        // WHAT THE RULING DECIDED: Ctrl+I is More Details. The item-invoice toggle moves to Ctrl+H and answers
        // to Ctrl+H ONLY — the user declined a Ctrl+I alias explicitly, so nothing is ambiguous.
        //
        // 🔴 AND THE COLLISION THAT RE-HOMING WALKED INTO, MEASURED IN THIS TREE. Ctrl+H WAS NOT A FREE CHORD:
        // it is the incumbent "Change Mode" arm in MainWindow.OnKeyDown, and the vendor's own shortcut page
        // attests it ("Change mode - open vouchers in different modes"). NOTHING WAS DISPLACED TO MAKE ROOM,
        // and nothing may be: the incumbent turned out to be a STRICT SUPERSET of the verb that moved.
        //   • ChangeMode() cycles As Voucher -> Item Invoice -> Accounting Invoice, so it ALREADY reaches
        //     item-invoice mode on exactly the Purchase/Sales screens the old two-way toggle worked on,
        //     under a WIDER gate (IsChangeModeEntry, which also admits Contra/Payment/Receipt).
        //   • So "the toggle answers to Ctrl+H" is satisfied by the key that was already there, and the
        //     capability keeps a keyboard door. What is gone is only the two-way toggle's distinct cycling
        //     SHAPE — the part the vendor never attested and census T2-14 graded as wrong.
        // 🔴 IF A LATER AGENT IS TEMPTED TO "FREE" Ctrl+H BY MOVING Change Mode ELSEWHERE: don't. That would
        // break a shipped, vendor-attested binding in order to re-seat an Apex invention.
        //
        // ── the record of why this row waited, kept verbatim ────────────────────────────────────────────
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
        //
        // ── how the ruling answered that last paragraph ─────────────────────────────────────────────────
        // 🔴 THE MEASUREMENT ABOVE STILL HOLDS AND IS STILL THE CONSTRAINT ON THE PANEL'S CONTENT: this build
        // has exactly TWO option-gated field groups (bill-wise, batch), both reachable only in invoice mode on
        // a Purchase/Sales. What the ruling changed is that Ctrl+I is no longer PARTITIONED — it is the whole
        // chord — so the surface with the rows is included rather than excluded, and the "always-empty panel"
        // failure mode does not arise. Where a voucher genuinely hides nothing (a Journal), the panel still
        // opens and says so; see MainWindowViewModel.CanOpenMoreDetails for why an honest empty answer was
        // chosen over a silently dead key.
        //
        // The gate is CanOpenMoreDetails (a live voucher-entry screen), NOT IsReportContext: the vendor's
        // "master or voucher" wording does not extend to reports, and this build has no option-gated field
        // group on a master screen to offer either. Where we have nothing, we claim nothing.
        //
        // ── 🔴 WHAT THE RE-HOMING COST, DISCLOSED RATHER THAN LEFT TO BE REDISCOVERED ───────────────────
        // The measurement above ("they are different verbs") did not stop being true when the ruling landed —
        // it stopped being a reason to WAIT. The price is real and it is this, exactly:
        //
        //   THERE IS NO LONGER A ONE-PRESS KEYBOARD ROUTE FROM ITEM INVOICE BACK TO AS VOUCHER.
        //   It now takes TWO presses of Ctrl+H, ON EVERY VOUCHER THAT HAS ITEM-INVOICE MODE AT ALL.
        //
        // VoucherEntryViewModel.ChangeMode cycles AsVoucher -> ItemInvoice -> AccountingInvoice -> AsVoucher, so
        // the next stop out of Item Invoice is Accounting Invoice, not As Voucher. The retired
        // Ctrl+I -> ToggleItemInvoice was a TWO-WAY toggle and did that return in one keystroke.
        //
        // 🔴 AND THE SCOPE IS WIDER THAN "SALES", WHICH IS WORTH STATING BECAUSE THE OBVIOUS READING IS WRONG.
        // ChangeMode's ItemInvoice arm falls through to `_ => AsVoucher` only when CanBeAccountingInvoice is
        // false, which invites the conclusion that Purchase keeps its one-press return. It does not:
        // CanBeItemInvoice (:67) and CanBeAccountingInvoice (:85) are the SAME predicate — `Sales or Purchase`
        // — so wherever the ItemInvoice arm is reachable the third mode exists, and that `_` arm is UNREACHABLE
        // FROM ITEM INVOICE. The cost applies to Purchase exactly as it does to Sales; there is no exempt
        // family. (CtrlH_on_a_purchase_cycles_all_three_modes already pins the Purchase cycle's period at 3.)
        //
        // This is a CONSEQUENCE OF USER RULING 17, not a defect to be fixed behind the ruling's back: the user
        // was asked for the chord and declined a Ctrl+I alias explicitly. It is legitimate, it is one direction
        // only, and it costs a keystroke rather than a capability — every mode including As Voucher remains
        // keyboard-reachable, and the mouse checkbox (MainWindow.axaml, "Item Invoice (Ctrl+H)") still flips it
        // directly. It is written down HERE, and pinned by ServiceAccountingInvoiceKeyboardTests, so that it is
        // a KNOWN divergence with a named cause rather than a silent regression an operator meets first. Should
        // the user later want the one-press return back, the honest fix is a NEW chord for ToggleItemInvoice —
        // not a Ctrl+I alias, which the ruling closed.
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
