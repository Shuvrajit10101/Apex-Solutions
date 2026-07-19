# CA Audit Backlog — decoded, verified, NOT yet actioned

**Date compiled:** 2026-07-17
**Status:** FINDINGS ONLY. Nothing in this document has been implemented. No code was changed to produce it.
**Sequencing:** to be actioned **AFTER the UI-defect campaign lands** (328 catalogued defects across 140 screens;
see `C:\Users\dkpho\OneDrive\Desktop\Apex-UI-Defect-Catalogue.md`). Almost every work item below touches
`src/Apex.Desktop/Views/MainWindow.axaml` — the same single 14,785-line file that campaign is rewriting.
Starting this work concurrently guarantees merge conflicts.

---

## What this is

A practising **Chartered Accountant** audited the running Apex Solutions app and returned **15 rough,
informally-worded points**. This document is the result of decoding those 15 points against the codebase and
against the Tally fidelity corpus, then **adversarially verifying every decode** (a second agent re-opened
every cited `file:line` and re-ran every search independently), then **web-verifying the statutory points**
against official Government of India sources.

It exists so that a future session with **no memory of that work** can pick up any item and act on it without
redoing the investigation.

### How to read it

- **Trust the verifier over the decoder.** Where the two disagreed, the verifier's finding is what is recorded
  here, and the disagreement is called out explicitly in a **⚠️ Verifier correction** block. Several decoder
  proposals contained wrong line numbers or wrong regression tests that would have sent an implementer at the
  wrong target — those are corrected inline. Do not resurrect the original.
- **`file:line` citations are real.** Every one reproduced here was established by the decoder AND confirmed by
  the verifier, or was found by the verifier. Nothing was invented. Where a citation could not be established,
  it says **NOT FOUND** rather than guessing.
- **Fidelity claims are separated from unverified ones.** Under R7, a Tally behaviour or a point of law may not
  be asserted from memory. Anything not grounded in `docs/tally-feature-catalog.md`, the git-ignored PDFs in
  `tally/`, or an official source is tagged **🟡 UNVERIFIED** and must be grounded before it is built.
- **"Already built" is a finding, not a failure.** This is a mature app (Phases 0–9 complete and merged, 98
  screens, 2481 tests). Several of the CA's points describe things that already work. Those are marked and
  evidenced. Building them again would waste a slice.

### ⚠️ Line-number drift warning

The decodes were read against **`git HEAD` = `0d1effd`**, where `src/Apex.Desktop/Views/MainWindow.axaml` is
**14,785 lines**. The working tree currently carries **uncommitted UI-defect-campaign changes** taking that file
to **14,807 lines (+22)**. Every `MainWindow.axaml` line number in this document is **HEAD-relative** and will be
off by roughly +22 (and more, once the campaign lands). `.cs` citations were unaffected. **Re-resolve every
`.axaml` line number against the tree you are working in.** Prefer the content anchors given alongside them.

---

## Traceability — all 15 raw points → work items

Every one of the CA's 15 points maps to at least one work item. Nothing was dropped.

| # | Point (short) | Work item(s) | State today |
|---|---|---|---|
| 1 | Party ledger has no address / PIN code | **WI-4** | NOT IMPLEMENTED |
| 2 | Dropdowns not keyboard-navigable (Up/Down) | **WI-2** | PARTIAL |
| 3 | Dropdowns should filter/shrink as you type | **WI-2** | PARTIAL (and silently misfiring) |
| 4 | Date format inconsistent; F2 everywhere | **WI-5** | PARTIAL |
| 5 | Ledger not editable after creation; CoA keyboard | **WI-3** | NOT IMPLEMENTED |
| 6 | TDS option in salary, per income tax law | **WI-6** (+ **WI-13**, **WI-14**) | PARTIAL — engine complete, unreachable |
| 7 | Gateway options need a red hotkey letter | **WI-9** | NOT IMPLEMENTED |
| 8 | Alt+C should create the right master for the field | **WI-1** | PARTIAL |
| 9 | Salary group under a payable head; employee ledger TDS | **WI-7** (group) + **WI-8** (ledger TDS) | NOT IMPLEMENTED |
| 10 | Alterations in all items, ledgers, groups | **WI-3** | NOT IMPLEMENTED |
| 11 | Alt+C on an item field → item creation, not ledger | **WI-1** | PARTIAL |
| 12 | Multiple units per item; 1 Dozen = 12, 1 kg = 1000 g | **WI-10** | PARTIAL |
| 13 | Y/N confirmation on accept; Ctrl+A accept-as-is | **WI-11** | PARTIAL — Ctrl+A done, Y/N absent |
| 14 | Alt+A to add a voucher from the Day Book | **WI-12** | PARTIAL |
| 15 | Alt+C / dropdown "Create" for group, ledger, stock, unit | **WI-1** | PARTIAL |

**Deliberate merges** (the CA described one feature from several angles — building them separately means
building the same machinery two or three times):

- **Points 8 + 11 + 15 → WI-1.** All three are *context-aware Alt+C create-on-the-fly*. 8 says "Alt+C should go
  to item creation, not ledger"; 11 says the same and adds "detect all the inputs"; 15 enumerates the target
  master types and adds the in-dropdown "Create" entry. One dispatch mechanism serves all three.
- **Points 2 + 3 → WI-2.** Both are *dropdown keyboard behaviour*, and they share a single root cause
  (`MainWindow.axaml.cs:548` `IsTyping`) and a single control. Point 2 is arrow navigation; point 3 is
  type-ahead filtering. Fixing 3 without 2 produces a filtering picker you still cannot arrow through.
- **Points 5 + 10 → WI-3.** Both are *master alteration*. Point 5 is the ledger-specific instance and pins the
  entry point (drill from the Chart of Accounts) and the keyboard requirement; point 10 generalises it to
  "all items, ledgers, and groups". Same scaffolding.
- **Point 9 splits into two** (WI-7, WI-8). Its two halves are unrelated: creating an accounting **group** is a
  missing master screen; enabling **TDS on an employee ledger** is a statutory-routing problem that is
  currently *dangerous* to grant naively. They must not ship as one slice.

### ⚠️ On the raw wording

The task brief asked that the CA's **raw wording be preserved verbatim**, because the rough phrasing is the
ground truth for intent. **The full original text of the 15 points was not supplied to this synthesis pass** —
only the decoders' analyses, which quote it in fragments. Each work item below therefore reproduces the
**verbatim fragments the decoders quoted**, clearly marked as fragments.

**ACTION FOR THE NEXT SESSION: paste the CA's original 15 points into this section verbatim.** Until then the
fragments are the best available record, and any ambiguity in them must be resolved with the user rather than
guessed. (The brief itself notes the points contain typos — e.g. "leisure creation" almost certainly means
"ledger creation" — which is exactly why the original matters.)

---

## Cross-cutting findings — NOT asked for by the CA, surfaced anyway

These emerged during the investigation. They are **not** part of the 15 points, and several are more serious
than what was asked. Each needs a user decision (R12) before it becomes work.

1. **🔴 The Income-tax Act, 2025 came into force on 1 April 2026 — mid-project.** Salary TDS is now **§392**,
   not §192. **Form 24Q → Form 138**, **Form 16 → Form 130**, **Form 12BB → Form 124**, **Form 16A → 131**.
   The shipped app names three forms that legally no longer exist. See **WI-13** and the Statutory section.
2. **🔴 `ImportPlan.cs:172` accepts a caller-supplied group `Nature` with no validation against the parent.**
   A canonical import declaring `Nature=Asset` under `Current Liabilities` silently puts a payable on the
   **assets** side of the Balance Sheet, which still "balances". This is a **live defect today**, reachable via
   Gateway → Import (`MainWindowViewModel.cs:2547-2564`). See WI-7.
3. **🔴 Credit Note / Debit Note voucher entry is unreachable from the UI**, although the engine and the
   entry screen are complete and tested (`tests/Apex.Desktop.Tests/CreditDebitNoteVoucherEntryViewModelTests.cs:94`,
   10 facts). `docs/tally-feature-catalog.md:471` already prescribes the route: "`Alt+F5` Debit Note ·
   `Alt+F6` Credit Note · `Alt+F7` Stock Journal". Those keys are unbound. This is a **grounded, specified,
   missing binding** — not an open question. Cheapest fix in this document; consider folding into WI-12.
4. **Go To (`Alt+G`) / Switch To (`Ctrl+G`)** — `docs/tally-feature-catalog.md:54` specifies "jump-anywhere
   navigation". Zero hits for `Key.G` / `GoTo` / `SwitchTo` in `src/Apex.Desktop`. Not implemented.
5. **`Alt+H` Multi-Master / Multi Alter is absent everywhere** (zero matches across `src/`), although
   `docs/tally-feature-catalog.md:125` specifies it as one of the three creation modes and it is Tally's real
   entry point for **group** alteration (Study Guide l.2111-2117, "Multiple Group Alteration").
6. **Salary-TDS rates are hardcoded `const` with no tax-year parameter** (`SalaryIncomeTax.cs`), unlike Phase-7
   TDS which deliberately made rates *seeded configuration* precisely so the FY table could change without a
   code change (`NatureOfPayment.cs:5-10`). See WI-13.
7. **Salary TDS cannot be deposited or challaned.** It accrues and reports correctly but has no path into the
   Phase-7 stat-payment machinery. Deliberately deferred, documented at `Form24Q.cs:102-115`. See **WI-14**.

---

# WI-1 — Context-aware Alt+C create-on-the-fly

**Covers points 8, 11, 15.** **State: PARTIAL.** **Effort: L.**

### Raw wording (verbatim fragments)

> *(8)* "While entering items, the option of creating a new item should also be allowed" · "If the window is
> about creating a ledger for the particular voucher then **only** it should redirect to ledger" · "Otherwise
> if an item is involved … it should redirect to item creation" · "Alt+C for creation of any item or ledger or
> group while entering the voucher"
>
> *(11)* "That should also lead to item creation rather than ledger creation" · "This should be true for all the
> units which the user is trying to enter" · "Automatically detect all the inputs that need to be redirected
> separately"
>
> *(15)* "group creation or ledger creation or stock creation or unit" · "shown in the dropdown"

### Decoded requirement

Alt+C must dispatch on the **master type of the field that has focus**, not on the screen. Ledger field →
Ledger; item field → **Stock Item**; Under field → Group (accounting or stock, per the form); Units → Unit;
Category → Stock Category; Godown → Godown. Three further requirements are implicit but essential:

1. **Non-destructive.** The in-progress voucher must survive; the creator opens over/beside it.
2. **Return-to-caller.** On save, return to the originating field with the new master **already selected**.
3. **Exhaustive.** Every master-reference input carries its kind as a declared property — no screen-level
   special cases, no silent fall-through to "always Ledger".

Point 15's "shown in the dropdown" is a **second, distinct affordance**: a `Create` entry inside the picker
list itself, alongside the shortcut.

### Tally fidelity target — ✅ GROUNDED (strongly)

The decisive evidence is that **the same Alt+C on the same screen creates three different master types
depending on the focused field** — which no screen-level rule can explain:

- `tally/664311548-Tally-Prime-Book.pdf:4658` — Stock Item "Under – Select Under group for stock item if you
  have no then create by Pressing `Alt+C'" → **Stock Group**
- `...:4659-4661` — "Category – … create by Pressing `Alt+C'" → **Stock Category**
- `...:4662` — "Units - Select units for stock item … create by Pressing `Alt+C'" → **Unit**
- `...:4958` — BOM "Location - Select Godown … (If you have no Godown then Create by Pressing `Alt+C')" → **Godown**
- `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:2046-2047` — "You can also create Ledger at the time of entering
  transaction by pressing Alt+C **in place of Ledger field** or select **Create option under List of Ledger
  Accounts**." *(Note: the phrase line-wraps across 2046-47; a naive single-line grep misses it.)* This one
  sentence grounds **both** the field-context rule **and** point 15's in-dropdown Create entry.
- **Return-to-field is proven:** `tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf:341-342` — "In Again Debit
  Field Press Alt+C to create ledger of RBI--Bank under Bank Accounts. 12. **In amount field amount appears
  automatically.**" The user is back in the voucher, continuing.
- Catalogue: `docs/tally-feature-catalog.md:124-125` (ledger inline `Alt+C`), `:238` (stock item inline
  `Alt+C`), `:469` ("`Alt+C` Create-on-the-fly / New Column").
- **The app's own unmet requirement:** `docs/phase3-inventory-requirements.md:216-217` **RQ-36** — "From an
  item/godown/unit field inside a voucher, `Alt+C` SHALL create the referenced master **inline without leaving
  the voucher** (RQ-7)." Also `:103` (RQ-7) and `:236`. **The CA independently rediscovered RQ-36.**

**🟡 UNVERIFIED — the item case specifically.** An exhaustive sweep of every `Alt+C` hit across all 10 corpus
PDFs found that each one is (a) a ledger field in a voucher, (b) a **master-screen** picker field, or (c) report
New Column. **No PDF shows Alt+C on a voucher's "Name of Item" field creating a stock item** — Book:1358/1457/
1548/1637 all read "Name of Item — Select `item name' from list" with no Alt+C nearby. The item case is
**catalogue-grounded (`:238`) and RQ-36-grounded, not PDF-quote-grounded.** The general field-context rule is
fully grounded. Do not overstate this to the CA.

**🟡 UNVERIFIED — does the typed text seed the new master's Name?** Tally is widely believed to carry the typed
prefix into the Name field. Not grounded in the corpus either way. Cheap if designed in; awkward to retrofit.

### Current state — file:line evidence

**Exists and works:**

- `src/Apex.Desktop/Views/MainWindow.axaml.cs:158-164` — the binding:
  `// Alt+C opens the Ledger-creation master whenever a company is open.` → `vm.CreateLedgerShortcut();`
- `src/Apex.Desktop/Views/MainWindow.axaml.cs:21` — `AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);`
  **Tunnelling**, so Alt+C fires *before* any focused TextBox sees it. The prerequisite for field-context
  dispatch already works.
- `src/Apex.Desktop/Views/MainWindow.axaml.cs:144-156` — the **report override**, correctly ahead of the global
  handler: `// Checked BEFORE the global Alt+C (Create Ledger) so on a report page Alt+C compares columns rather
  than creating a ledger.` This matches Tally (`catalog:469`, Book:1908 P&L → New Column) and **must be preserved**.

**The defect — dispatch is screen-level, two hardcoded cases:**

`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:4350-4360`:
```csharp
public void CreateLedgerShortcut()
{
    if (Company is null) return;
    if (CurrentScreen is Screen.ManufacturingJournalEntry or Screen.BomMaster)
    { ShowStockItemMaster(); return; }
    if (CurrentScreen != Screen.LedgerMaster) ShowLedgerMaster();
}
```
Every other screen — including Sales/Purchase **item invoice** entry — falls through to `ShowLedgerMaster()`.
Zero field awareness: Unit, Godown, Category, Stock Group and accounting Group are unreachable by Alt+C anywhere.

**🔴 Alt+C silently destroys the in-progress voucher.** `ShowLedgerMaster()` (`:2642-2649`) → `OpenPageColumn`
(`:4072-4098`), whose own contract at `:4086-4088` reads: *"Trim after the LAST MENU column — this removes any
page column that is already open … so a page is REPLACED, never stacked. There is therefore AT MOST ONE page
column, always the rightmost."* It calls `TrimColumnsAfter(LastMenuColumnIndex())` then `ClearSubScreens()`
(`:4119-4123`), which does `VoucherEntry = null;` / `InventoryVoucherEntry = null;`. No warning, no recovery,
no unsaved-changes guard anywhere (independent search for `IsDirty|unsaved|HasChanges|ConfirmDiscard` found
only unrelated rate/discount auto-fill flags). **This is data loss, and it directly violates RQ-36.**

**No return-to-caller:** `onChanged: () => { }` appears **32 times** in `MainWindowViewModel.cs` — including
`:2646` (ledger) **and `:2769` (stock item)**. The hook is live (`LedgerMasterViewModel.cs:630` invokes
`_onChanged()` on save) and wired to nothing.

**No accounting-Group master exists at all** — see WI-7. `case "Group": ShowLedgerMaster();`
(`MainWindowViewModel.cs:4897`) misroutes it.

**Zero tests:** `grep -rn "CreateLedgerShortcut" tests/` matches only the compiled `Apex.Desktop.dll`.

### ⚠️ Verifier corrections (carry these; do not use the decoders' originals)

1. **A second dispatch site was missed.** `MainWindowViewModel.cs:5265` binds the on-screen button **directly**,
   bypassing `CreateLedgerShortcut` entirely:
   `ButtonBar.Add(new ButtonBarItem("Alt+C", "Create Ledger", ShowLedgerMaster, hasCompany));`
   Consequences: **key and button already disagree today** (on ManufacturingJournalEntry/BomMaster the *key*
   opens Stock Item, the *button* opens Ledger); a fix that only replaces `CreateLedgerShortcut` leaves the
   button wrong; and the hardcoded label "Create Ledger" must become context-dependent. Also check the
   button-bar dispatcher `Fire(vm, key)` at `MainWindow.axaml.cs:569`.
2. **`ShowBatchAllocation` (`:2796-2811`) is NOT a "proven" non-destructive pattern.** Its comment ("The
   sub-screen sits to the RIGHT of the live voucher column (do NOT trim the voucher page)") is real and it never
   calls `TrimColumnsAfter` — **but it calls `ClearSubScreens()` itself**, which nulls the voucher VMs. Combined
   with the rehydrate gap below, it is **demonstrably broken on return today**. Copying it verbatim reproduces
   the orphaning. `ComparativeReportViewModelTests.cs:307-313` (report stays live beneath an extra column) is the
   genuinely regression-locked precedent to model on.
3. **The blast radius is wider than the decode said.** `Screen.InventoryVoucherEntry`, `JobWorkOrderEntry`,
   `MaterialMovementEntry` and `PosBilling` are all stock-centric screens where Alt+C opens the **Ledger**
   master — condemned by the existing docstring's own rationale at `:4347` ("opening the accounting Ledger
   master there is nonsensical").
4. **The report override is narrower than it looks.** `MainWindow.axaml.cs:148-149` gates on
   `vm.IsReportContext && vm.Reports is { SupportsComparative: true }`. So on a **non-comparative** report
   (e.g. the **Day Book**) Alt+C **falls through today and opens Ledger Creation**. Whether that matches Tally
   is **🟡 UNVERIFIED** — no corpus passage covers Alt+C on a non-comparative report. Anyone told to "preserve
   the report override" must know its true scope.

### Enabling defect that must be fixed in the same slice

**`RehydratePageFromRightmostColumn` (`MainWindowViewModel.cs:5114-5142`) cannot restore a voucher.** Its switch
has exactly four cases — `ReportsViewModel`, `LedgerVouchersViewModel`, `VoucherDetailViewModel`,
`PrintPreviewViewModel` — then `default: CurrentScreen = Screen.Gateway`. `VoucherEntryViewModel` appears
**0 times**. `BackFromPage()` (`:5081`) calls `ClearSubScreens()` then rehydrates, so popping *any* column above
a voucher drops the shell to the Gateway with `VoucherEntry` null.

**This is a latent bug in the existing Alt+B batch-allocation flow, today.** No test covers the return path —
`BatchInventoryViewModelTests.cs:287-317` drives `ShowBatchAllocation` and asserts only that the sub-screen
*opens*. "Return to the voucher" is impossible until this is fixed.

### Gap

1. Dispatch keys off `CurrentScreen`, not the focused field's master kind.
2. Five of seven targets unreachable by Alt+C: accounting Group, Stock Group, Stock Category, Unit, Godown.
3. Inline create is destructive (`ClearSubScreens` nulls the voucher).
4. No return-to-caller (32 × `onChanged: () => { }`).
5. `RehydratePageFromRightmostColumn` cannot restore a voucher page.
6. The accounting-Group master does not exist (WI-7 is a **prerequisite** for point 8's "group").
7. Button-bar label is static.
8. Point 15's in-dropdown "Create" entry: **zero equivalent anywhere** (searched `(create)`, `"Create new"`,
   `"< New"`, `CreateOption`, `NewItemPlaceholder`, `"+ New"`, `AddNewOption`).
9. No tests.

### Proposal

**No schema change. No engine change. v44 stays v44.** Pure `Apex.Desktop` + one new master VM (WI-7).

1. **Declare the mapping.** New `src/Apex.Desktop/Views/MasterField.cs`: a `MasterFieldKind` enum
   (`Ledger, Group, StockItem, StockGroup, StockCategory, Unit, Godown, …`) plus an Avalonia **attached
   property** `MasterField.Kind`. This makes the mapping a declared property of each control — which is what
   "automatically detect all the inputs" means.
2. **Resolve the focused field.** Add `FocusedMasterField(KeyEventArgs e)` to `MainWindow.axaml.cs`, modelled
   directly on the **shipped, working** `FocusedInventoryLine` at **`:555-561`** (walks `e.Source as
   StyledElement` up via `.Parent` — the exact technique needed, already proven for Alt+B).
3. **Tag the pickers** in `MainWindow.axaml` — the bulk of the work, and mechanical. **Do this after the
   UI-defect campaign lands.**
4. **Rewrite the dispatcher.** Replace `CreateLedgerShortcut()` with `CreateMasterInContext(MasterFieldKind?)`;
   `switch` to the existing factories (`ShowLedgerMaster` :2642, `ShowStockGroupMaster` :2725,
   `ShowStockCategoryMaster` :2735, `ShowUnitMaster` :2745, `ShowGodownMaster` :2755, `ShowStockItemMaster`
   :2765 — **all four of these line numbers verified exact**). Keep the screen-level fallback for a `null` kind
   so untagged fields degrade to today's behaviour rather than breaking. **Fix the ButtonBar site at `:5265`
   too.**
5. **Open beside, not over.** Add `OpenOverlayPageColumn(...)` next to `OpenPageColumn` that stacks an extra
   column and does **not** call `ClearSubScreens()`. **Do not loosen `OpenPageColumn` itself** — its
   "AT MOST ONE page column" invariant (`:4086-4088`) is depended on by `ClearSubScreens`, `TrimColumnsAfter`
   and `IsMenuScreen`.
6. **Fix rehydrate** — add `VoucherEntryViewModel` / `InventoryVoucherEntryViewModel` cases to `:5114`. Required,
   not optional; also repairs the existing Alt+B bug.
7. **Feed the result back** — real callbacks at `:2646` **and `:2769`** (and the other Alt+C-opened sites).
8. **Point 15's in-dropdown "Create" entry** — near-free once 1–7 land; shares the same field→kind map,
   non-destructive open and return-to-caller. Fold it in rather than re-cutting later.
9. **Tests** (`tests/Apex.Desktop.Tests/InlineCreateShortcutTests.cs`, new): item field → `StockItemMaster`,
   NOT LedgerMaster; ledger field → LedgerMaster; **after accept the voucher is still live** (`VoucherEntry`
   non-null, lines intact) and the new master is selected into the field; Alt+C on a comparative report still
   adds a column. **Drive real key events through `MainWindow.axaml.cs`** — a VM-level test would pass while the
   keybinding stayed wrong. Add an enumeration test asserting every master-reference picker carries a
   `MasterField.Kind`, so a tagging miss fails loudly instead of silently reverting to "always Ledger".

### Risk

- **🔴 The fix touches a live data-loss path.** Half-done, it turns silent voucher loss into a null-deref crash
  or a phantom half-saved master — the class of bug A10 caught in Phase 9 UI-3 (a refused advance left a
  dangling FK and made the company permanently unsaveable). The overlay path must be all-or-nothing.
- **🟠 Breaking the one-page-column invariant** breaks navigation across all 98 screens, far beyond Alt+C.
- **🟠 AXAML contention** with the in-flight UI-defect campaign. Sequence after it.
- **🟡 Silent-fallthrough regression** if a picker is missed — no error, just the old bug on one field. The
  enumeration test is the mitigation.
- **🟡 Regressing report Alt+C** (correct Tally, tested) if handler order changes.
- **🟢 No schema risk.** Nothing new is persisted.

### Open questions

1. **Field-level or voucher-level dispatch?** Recommend **field-level** (Tally's "in place of Ledger field") —
   a voucher-level rule is *undecidable* on an item invoice, which has both ledger and item fields
   (`VoucherEntryViewModel.cs:47` `Lines`/`SelectedLedger` **and** `:96` `InventoryLines`/`SelectedItem`).
   The CA's literal words ("is an item involved in this particular voucher") suggest voucher-level. Confirm.
2. **Does point 8 stop at the voucher, or is it one general rule?** Points 11/15 say "all". Building it once for
   every picker is barely more work; voucher-only guarantees a second pass.
3. **Nested Alt+C depth?** Point 8 names "group", but no voucher field takes a group directly — it is only
   reachable *inside* the ledger master's Under field, i.e. voucher → ledger → group. Tally allows nesting; our
   single-page-column shell makes depth >1 hard. **Recommend capping at depth 1 for this slice.**
4. **"group" = accounting or stock group?** Book:4658 grounds *stock* group on the item master's Under field;
   point 9 concerns an *accounting* group.
5. **Does the typed prefix seed the new master's Name?** 🟡 UNVERIFIED — check the corpus/live Tally.
6. **"all the units" (point 11)** — Unit-of-Measure masters specifically, or "every input field" loosely? Both
   readings converge on the same build, but confirm.

---

# WI-2 — Dropdown keyboard navigation + type-ahead filtering

**Covers points 2, 3.** **State: PARTIAL** (and the part that exists is **silently misfiring**). **Effort: L.**

### Raw wording (verbatim fragments)

> *(2)* "each and every drop down" · "selecting from a dropdown"
>
> *(3)* "If the first few characters of the word are typed" · "shrinking the drop down menu" · "the drop down
> option should automatically shrink to that particular word" · "this logic should also be true"

### Decoded requirement

Every value-selection dropdown must be fully operable from the keyboard: **Up/Down** moves within the
dropdown's list (not stolen by the shell), a gesture **opens** the list, **Enter** commits, **Escape**
dismisses — and typing characters **progressively filters the list** to matching entries, updating on each
keystroke, so the user can then arrow to the survivor and press Enter. Backspace widens again; Esc reverts.
"Each and every" ⇒ this is one systemic change to the shared key pipeline, not per-screen patches.

### Tally fidelity target — ✅ PARTLY GROUNDED, and honestly so

**Grounded:**

- `docs/tally-feature-catalog.md:52` — "**Keyboard-driven**: nearly every action has an `F`/`Alt`/`Ctrl`
  shortcut; mouse optional."
- **Arrow-to-highlight + Enter-to-commit is the standard Tally interaction for a list-backed field:**
  `tally/664311548-Tally-Prime-Book.pdf:14707` — "For Define work of user, select the user type by Pressing
  \"Down Arrow\" key."; `:14710` — "After selecting user Press Enter on \"User type\"."; also `:14696`.
- **Ledger lists are alias-searchable** — `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:2031` — "You can access
  the ledgers using the original name or the alias name." ⇒ **any filter MUST match Alias as well as Name.**
- **🔒 HARD CONSTRAINT: `F4` is Contra.** `docs/tally-feature-catalog-verification-report.md:63` — "Confirmed-
  correct (no change): Contra=F4, Payment=F5, …", sourced at `:64` to
  https://help.tallysolutions.com/keyboard-shortcuts-tally-prime/. So **F4 must not be repurposed as the
  open-dropdown key**, and Avalonia's built-in F4-opens-dropdown must be actively suppressed.

**🟡 UNVERIFIED — and these gate the design. Do not guess:**

- **Does Tally shrink the list, or only jump the highlight?** The catalogue, the verification report and **all
  10 PDFs are silent.** Two independent exhaustive sweeps (~15 search terms each: `type the first`, `first few
  char`, `start typing`, `as you type`, `narrow down`, `filter the list`, `incremental`, `autocomplete`,
  `drop.?down`, `begins with`, `starts with`, …) found **nothing**. The corpus only ever says "select SGST from
  the list of ledgers".
- **Prefix (StartsWith) or substring (Contains)?** Ungrounded. Widely believed to be substring; **not asserted
  here**. This single answer most shapes the slice and is expensive to reverse across ~194 sites.
- **Does Tally auto-open the list when the cursor enters a list-backed field?** If yes, there is no "open"
  gesture to design and the whole shape changes. **Resolve first.** *(The only near-hit, Book:14889-14895, is a
  pdftotext-scrambled table about F12-config and company-data **menus**, not field dropdowns — not a
  counterexample.)*
- **Wrap at the ends?** The shell's own lists **wrap** (`MainWindowViewModel.cs:4421`/`:4424` doc comments
  literally say "wraps"; `DemoLoadViewModelTests.cs:122` `vm.MoveUp(); // wraps to the last item`). Avalonia's
  ComboBox does not. So "consistent" is itself ambiguous.
- **Avalonia's own open keys.** The decode asserted F4 / Alt+Down are Avalonia's defaults without a citation.
  Plausible (WPF convention) but **load-bearing** for "Alt+Down is free today" — confirm against Avalonia
  12.0.5's `ComboBox.OnKeyDown` before designing around it.

**Do not use `tally/659947760-Tally-Prime-Short-Key.pdf` as a source.** `docs/tally-feature-catalog.md:479-481`
flags it as having "garbled shortcut mappings", and it is genuinely broken (it lists F7=Payment,
F8=Stock Journal, Alt+K=Go To).

### Current state — the root cause is one line

**🔴 A window-level TUNNELLING handler swallows the arrow keys before any dropdown can see them.**

- `src/Apex.Desktop/Views/MainWindow.axaml.cs:20-21`:
  ```csharp
  // Handle keys at the tunnelling stage so arrow/Enter/Esc work regardless of focus.
  AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
  ```
- **`src/Apex.Desktop/Views/MainWindow.axaml.cs:548` — THE BUG:**
  ```csharp
  private static bool IsTyping(KeyEventArgs e) => e.Source is TextBox;
  ```
  A ComboBox is not a TextBox, so `IsTyping` is **false** for every dropdown. **And it can never be true:**
  `grep -c IsEditable src/Apex.Desktop/Views/MainWindow.axaml` = **0** — no combo is editable, so no ComboBox
  template contains an inner TextBox. This closes the one hole a future implementer might argue.
- `MainWindow.axaml.cs:492-499` — arrows therefore drive the **cascade menu** and are marked handled:
  `case Key.Up when !IsTyping(e): vm.MoveUp(); e.Handled = true; break;` (same for Down) →
  `MainWindowViewModel.cs:4422`/`:4425` `MoveUp() => StepActive(-1)` / `MoveDown() => StepActive(+1)`.
  Because `Handled` is set during the **tunnel** phase, the ComboBox's own bubble-phase `OnKeyDown` never runs.
- **Enter and Escape are worse — no guard at all.** `:506-509` `case Key.Enter: vm.ActivateSelected();` and
  `:516-519` `case Key.Escape: vm.Back();`. Plus an even earlier unguarded intercept at `:66`
  `if (e.Key == Key.Enter && vm.DrillSelectedRow())`.
- **No keyboard way to open a dropdown.** F4 → `:525` `case Key.F4: Fire(vm, "F4");` (fires Contra). Alt+Down →
  the Alt block at `:435-463` handles **only F-keys**, so it falls through to `case Key.Down when !IsTyping(e)`
  at `:496` (whose `when` clause tests no modifiers).
- **Nothing custom mitigates it.** The only other KeyDown handlers in the whole Desktop project are
  `OnStockSummaryKeyDown` (`:1048`) and `OnAccountingReportKeyDown` (`:1071`), both wired to report ListBoxes
  and both early-returning unless `e.Key == Key.Enter`. Neither touches a ComboBox. `IsTextSearchEnabled` set in
  source: **0**. The only ComboBox style (`MainWindow.axaml:73`) is cosmetic. No `src/Apex.Desktop/Controls`
  directory. All dropdowns are stock Avalonia **12.0.5** (`Apex.Desktop.csproj:20-23`).

**🔴 Type-to-jump is already ON app-wide — and it silently selects the WRONG ledger.** This is the most
important finding in WI-2 and it is a **correctness bug independent of whether filtering is ever built.**

- Avalonia 12.0.5's `ComboBox` static ctor: `IsTextSearchEnabledProperty.OverrideDefaultValue<ComboBox>(true);`
  (verified against the 12.0.5 tag). Semantics: **jumps** the selection to the first `StartsWith` /
  `OrdinalIgnoreCase` match, accumulating a term reset by a 1-second `DispatcherTimer`. It does **not** filter.
- Match text comes from `TextSearch.GetEffectiveText`, whose chain is `TextSearch.Text` →
  `TextSearch.TextBinding` → `ItemsControl.DisplayMemberBinding` → `IContentControl.Content.ToString` →
  `object.ToString()`. **The repo sets none of the first three** (0 hits for `TextSearch`,
  `IsTextSearchEnabled`, `DisplayMemberBinding` across `src/` and `tests/`).
- **56 of ~194 pickers bind directly to domain entities** — `ledger:StockItem` ×13, `ledger:Godown` ×13,
  `ledger:Ledger` ×11, `ledger:Unit` ×3, `ledger:Employee` ×3, plus CostCategory/CostCentre/Group/StockGroup/
  PriceLevel/Currency/Budget/AttendanceType/EmployeeGroup/PayrollUnit/NatureOfPayment. **Every one is a plain
  `public sealed class` with no `ToString()` override** (verified: `Ledger.cs:9`, `StockItem.cs:18`,
  `Godown.cs:14`, `Unit.cs:17`, `Employee.cs:16`, `CostCategory.cs:16`, `CostCentre.cs:14`, `Group.cs:9`,
  `StockGroup.cs:15`, `PriceLevel.cs:11`, `Currency.cs:16`, `Budget.cs:14`, `AttendanceType.cs:17`,
  `EmployeeGroup.cs:16`, `PayrollUnit.cs:16`, `NatureOfPayment.cs:14` — records would have synthesised
  `ToString()`; these are not records).
- **Therefore in the Ledger picker every item's search text is the identical string
  `"Apex.Ledger.Domain.Ledger"`.** And `GetIndexFromTextSearch` iterates raw `Items[i]` **from index 0**, not
  from `SelectedIndex+1`. So **typing "A" selects the FIRST ledger**; typing any other letter does nothing.
  The test company is "**Apex** Automations" and the namespace begins "**Apex**". **A user typing a real party
  name on a voucher either gets nothing or silently gets the wrong ledger.** Same class as the silent
  under-reversals A10 caught on Phase-9 slices.
- ~32 VM option types (across 18 files) *do* override ToString and work — e.g. `ReportsViewModel.cs:3126-3131`
  `PayrollMonthOption` (`public override string ToString() => Label;`), `:3135-3140` `PayrollEmployeeOption`,
  `Cmp08ReportViewModel.cs:18`. **~64 do not** — e.g. `InventoryVoucherEntryViewModel.cs:601` `PartyOption`
  (has `Display`, no ToString; feeds exactly 5 pickers), `ReportsViewModel.cs:3069` `ScenarioOption`,
  IndianStateOption, GstRegistrationTypeOption, PayHeadPickerOption, CdnOriginalInvoiceOption, BatchPickOption…

**No filtering infrastructure at all.** `AutoCompleteBox` usage = 0 across `src/` (it exists in Avalonia 12.0.5
with `FilterMode`/`ItemFilter`/`TextFilter`/`MinimumPrefixLength`/`ValueMemberBinding`/`SelectionAdapter` —
280 mentions in `Avalonia.Controls.xml` — just unused). Only two `.axaml` files exist: `App.axaml`,
`MainWindow.axaml`.

**Why 2481 tests never caught any of this:** `grep` for `KeyEventArgs|RaiseEvent|KeyDownEvent|KeyboardDevice`
across `tests/` matches **only compiled `.dll` artifacts — zero `.cs` source hits. No test ever injects a real
key.** Every test calls the VM directly (`CascadingGatewayTests.cs:45` `vm.MoveDown();`,
`GatewayHierarchyTests.cs:90`/`:97`, `DemoLoadViewModelTests.cs:122`). The tunnel handler is effectively
untested. This is the project's known false-green shape ("a `ShowXMenu()` test proves nothing about
reachability").

### ⚠️ Verifier corrections

1. **🔴 MATERIAL — widening `IsTyping` does NOT fix Enter/Escape.** `case Key.Enter:` (`:506`) and
   `case Key.Escape:` (`:516`) carry **no `!IsTyping(e)` guard**, unlike Up/Down/Left/Right/F10. A proposal that
   calls widening `IsTyping` "the single shared fix" and then tests "Enter accepts the highlighted survivor;
   Esc reverts" **will fail** — those two need separate context guards.
2. **Wrong lines (decoder):** bare-O/bare-Y at Gateway are **`:381`/`:391`**, not 326/336. `FocusedInventoryLine`
   is **`:555`**, not 500. `PartyOption.IsNone` is **`:605`**, not 604.
3. **Overstated (decoder):** `Key.D` quick-jump **cannot** hijack a dropdown. `CanQuickJump` = `vm.IsMenuScreen
   && !IsTyping(e)` (`:564-565`), and `IsMenuScreen` (`MainWindowViewModel.cs:548-554`) explicitly **excludes**
   VoucherEntry / LedgerMaster / Reports — precisely the screens with pickers. B/P/T/D cannot reach a dropdown;
   O/Y are Gateway-only.
4. **Under-reported (decoder), and it widens the slice:** the bare-letter shortcuts **are** guarded only by
   `IsTyping` — `:358` **P = Print**, `:370` **E = Export**, `:404` **M = E-Mail**, `:412` **Space =
   Outstandings**. `IsPrintablePage`/`IsExportablePage` are true on report/master screens that *do* have
   pickers. **So on a focused ComboBox, pressing "P" fires PRINT.** The damage is not limited to
   arrows/Enter/Escape.
   - 🟡 **Nuance not resolved:** the hijack is on `KeyDown`; ComboBox text search runs on `TextInput`
     (`_textSearchTerm += e.Text`). Whether `e.Handled = true` on KeyDown suppresses the subsequent
     WM_CHAR/TextInput decides "**instead of**" vs "**in addition to**". Not asserted. The defect exists either way.
5. **The "380 ComboBoxes" vs "194 ComboBoxes" discrepancy is explained, not a contradiction.** `grep -c
   "<ComboBox"` = **380** counts control tags **plus** property-element tags like `<ComboBox.ItemTemplate>`;
   `grep -c "<ComboBox "` = **194** is the true control count, and 186 of those carry an ItemTemplate.
   194 + 186 = 380 exactly. **Use 194 as the control count.**
6. **Trivial:** `AutoCompleteBox` mentions are 280, not 260; ToString overrides are 32, not ~31.

### 🟡 Honest gap in the evidence

Nobody could verify (no builds permitted) whether **Avalonia 12.0.5 hosts the ComboBox popup in a separate
`PopupRoot`** and moves focus into it. If it does, a **mouse-opened** list might already respond to arrows,
because KeyDown would not route through MainWindow's tunnel handler. This does **not** rescue the requirement —
the keyboard-only path is provably broken end-to-end (cannot open; closed-combo arrows hijacked) — but it
determines whether the new guard must also match `ComboBoxItem`. **Resolve by running the app before designing.**

### Gap

**(A) No filtering** — nothing narrows as you type; Avalonia only jumps the highlight. *(Point 3's literal ask.)*
**(B) The existing jump is misconfigured** — search text is a constant type name on the 56 domain-bound pickers
⇒ **live silent-wrong-selection defect on the Ledger/StockItem/Godown/Party pickers.** Independent of (A);
should arguably jump the queue as a correctness bug.
**(C) The tunnel handler steals the keys** — `IsTyping` only knows TextBox; plus unguarded Enter/Escape.
Fixing (A) without (C) yields a filtering picker you cannot arrow through.

### Proposal

**No schema change — pure input routing. v44 untouched, no Io fold-in.** Do points 2 and 3 as **ONE slice**
(same control, same handler, same predicate; splitting means editing `MainWindow.axaml.cs:490-548` twice and
re-litigating the same guards). **Sequence after the UI-defect campaign** — it rewrites the same file.

1. **Step 1 — stop the silent misfire (cheap, high value, do first).** Give every picker a real search string:
   set `TextSearch.TextBinding="{Binding Name}"` (or `Display`/`Label`) on each `<ComboBox>`, **rather than**
   adding `ToString()` to `src/Apex.Ledger/Domain/*.cs` (that pushes a UI concern into the engine).
   `DisplayMemberBinding` is a viable alternative — **verified in the chain** via the call site
   `var textBinding = TextSearch.GetTextBinding(this) ?? DisplayMemberBinding;`. For the ~64 VM option types
   lacking ToString, either add `public override string ToString() => Display;` (consistent with the 32 that
   already do) or bind `TextSearch.TextBinding`.
2. **Step 2 — unblock the keys.** Add a **new, narrowly-scoped predicate** beside `IsTyping` — **do not widen
   `IsTyping` itself**, it has ~20 call sites governing unrelated letter/Alt shortcuts:
   ```csharp
   private static bool OwnsListKeys(KeyEventArgs e) => e.Source is ComboBox or ComboBoxItem;
   ```
   Apply it to `:492`/`:496` (Up/Down), `:502`/`:512` (Right/Left — so Left/Right cannot pop a cascade column
   out from under an open list), and **add explicit guards to the currently-unguarded `:506` Enter, `:516`
   Escape and the pre-emptive `:66` Enter drill**. Gate `:525` F4 only per the F4 decision; **suppress
   Avalonia's built-in F4-opens-dropdown** so F4 stays Contra even on a focused combo (Tally fidelity beats
   platform default). Put the resolution in the VM (e.g. `bool ActivateHotkey(char)`) so it is unit-testable.
3. **Step 3 — the open gesture. Decide first** (see UNVERIFIED). If Tally auto-opens on field focus, implement
   that via a single style/behaviour near the existing combo style at `MainWindow.axaml:73` (GotFocus →
   `IsDropDownOpen=true`) — **not 194 edits**. If not, adopt **Alt+Down** (free today) and handle it explicitly
   in the Alt block at `:435` instead of letting it fall through to `:496`.
4. **Step 4 — add the filtering.** Create `src/Apex.Desktop/Controls/ApexPicker.axaml(.cs)` — the project's first
   shared control — wrapping Avalonia 12's `AutoCompleteBox`, which natively shrinks its dropdown. Configure
   `ItemFilter` to match **Name + Alias** case-insensitively (alias is the one grounded requirement),
   `MinimumPrefixLength=0`, and a `SelectionAdapter` so Up/Down/Enter work. Expose a `SearchTextSelector`
   (`Func<object,string>`) so each site supplies its searchable text while keeping the rich ItemTemplate.
   **Reserve an Alt+C hook now for WI-1.** Migrate the highest-value pickers first (Ledger ×11, StockItem ×13,
   Godown ×13, Party ×5) and prove them before the long tail.
5. **Tests** — the load-bearing part. New `tests/Apex.Desktop.Tests/DropdownKeyboardNavigationTests.cs`, and
   they must **inject REAL `KeyEventArgs` through the Window** with a ComboBox as `Source` — a `vm.MoveDown()`
   call proves nothing, which is exactly why 2481 tests missed this. Assert: focused closed combo + Down →
   combo selection changes AND `vm.SelectedIndex` unchanged; open list + Down/Enter → committed, cascade column
   count unchanged; Escape → list closes, column NOT popped; typing a prefix shrinks the list; **alias matches**;
   backspace re-widens; a `(none)`/sentinel option survives filtering; and a **regression lock that typing "A"
   in the Ledger picker does NOT select an unrelated ledger** (locks defect B).

### Risk

- **🔴 Shell regression.** The tunnel handler exists precisely so "arrow/Enter/Esc work regardless of focus".
  Excluding ComboBox from Up/Down could dead-key cascade navigation on any screen where a ComboBox holds focus
  by default. **Audit which screens auto-focus a combo before merging.**
- **🔴 False green.** All 2481 tests exercise MoveUp/MoveDown at the VM level and will stay green whether or not
  the fix works — **and would stay green through a regression.** Any "tests pass" claim here is worthless
  unless real keys are injected.
- **🟠 Blast radius.** `MainWindow.axaml.cs:490-548` is the single key pipeline for all 98 screens. Enter (`:66`,
  `:506`) is especially delicate — it already has two competing consumers before the dropdown.
- **🟠 Widening `IsTyping` instead of adding a separate predicate** would silently alter ~20 unrelated shortcut
  guards. A future "simplification" may reintroduce this.
- **🟠 F4 collision is a fidelity trap.** Leaving both Avalonia's F4-opens-dropdown and Tally's F4=Contra live
  means F4 does different things depending on focus — worse than either.
- **🟡 Swapping ComboBox → AutoCompleteBox changes the control template and metrics** — the campaign's known
  traps apply (starved `*` columns; horizontal StackPanels killing TextWrapping; `IsEffectivelyVisible` stays
  TRUE when a parent clips, so only a **render** catches it). Render-verify headlessly, then **revert
  `TestAppBuilder`**.
- **🟡 Data integrity.** AutoCompleteBox is text-editable ⇒ it can accept free text matching no ledger. Must
  reject/blank non-existent entries or it becomes a phantom-master bug.
- **🟡 Sentinels must survive filtering** — `PartyOption.IsNone` (`InventoryVoucherEntryViewModel.cs:605`),
  `ScenarioOption.Actual` (`ReportsViewModel.cs:3084`). VMs depend on them.
- **🟢 No schema risk.** `Alias` — the one field a Tally-faithful search needs — already exists **and is already
  persisted**: `Ledger.cs:27`, `StockItem.cs:36`, `SqliteCompanyStore.cs:4706` binds `$alias`. Schema stays v44
  (`Schema.cs:99`). **If the design later adds a persisted per-picker preference, the full v45 obligation
  returns — avoid it.**

### Open questions

**For the user / CA:**
1. **Prefix or substring?** Should typing "ank" find "Bank of India"? *(The single answer that most shapes the
   slice.)*
2. **Should the filter search the Alias too?** Corpus says ledgers are alias-accessible ⇒ recommend yes. Confirm.
3. **No-match behaviour** — empty list, freeze on last match, or refuse the keystroke?
4. **Do tiny enum dropdowns (Yes/No, Dr/Cr) also filter, or only long data-driven lists?** "Each and every"
   suggests all — affects ~194 sites vs ~40.
5. **On a CLOSED dropdown, should Up/Down change the value in place, or open the list first?** Silent
   one-keypress mutation of a voucher field is a real hazard.
6. **Wrap at the ends?** The shell wraps; Avalonia does not.
7. **PageUp/PageDown/Home/End in a long list?** Note PgUp/PgDn are Tally voucher-navigation keys — may collide.
8. **Did "each and every drop down" also mean the Miller-column menu/report lists?** Those already move on
   Up/Down (`MainWindowViewModel.cs:4422`/`:4425`) — confirm they are out of scope. *(Note: this collides with
   WI-9 — see that item's cross-point conflict.)*
9. **When the typed text matches nothing, should the picker offer Alt+C create-on-the-fly?** (WI-1.)

**For A14 / R7 grounding — the catalogue and all 10 PDFs are silent; resolve against official Tally help or a
live install BEFORE implementing:**
10. Does Tally **shrink** the list or only **jump** the highlight? StartsWith or Contains? Name-only or
    Name+Alias? No-match behaviour?
11. **Does Tally auto-open the selection list on field focus?** The design-gating question.

**For the implementing agent (resolve by running the app):**
12. Does Avalonia 12.0.5 host the popup in a separate `PopupRoot`? (Decides whether the guard must match
    `ComboBoxItem`.)
13. Which screens auto-focus a ComboBox on entry? (That is where the shell-regression risk bites.)
14. Confirm Avalonia's actual default open keys before relying on "Alt+Down is free".

---

# WI-3 — Master alteration (the "Alter" verb)

**Covers points 5, 10.** **State: NOT IMPLEMENTED.** **Effort: XL** (L if scoped to ledger+group+item only —
see Q2; the engine half is much cheaper than it looks, see the verifier correction).

### Raw wording (verbatim fragments)

> *(5)* "Editing in Ledger should be allowed in the chart of accounts after the creation of Ledger" · "Make it
> fully editable" · "keyboard logic for the chart of accounts"
>
> *(10)* "alterations in all items, ledgers, and groups"

### Decoded requirement

Add Tally's **second universal action verb** — Alter — alongside Create. A user must be able to (a) pick an
existing master, (b) open the **same form pre-filled** with its current values, (c) change any field, (d) accept
with Ctrl+A, (e) have it saved against the master's **stable identity** — so a rename mutates in place and every
historical voucher follows automatically. Point 5 additionally pins the entry point (**drill down from the Chart
of Accounts**, with an arrow-key highlight and Enter-to-open) and says "**fully** editable"; point 10 generalises
to "all items, ledgers, and groups".

### Tally fidelity target — ✅ GROUNDED

- `docs/tally-feature-catalog.md:51` — "**Two universal action verbs**: **Create** / **Alter** (edit) applied to
  every master & voucher."
- **Single alter path** (two independent PDFs agree, so this is solid):
  `tally/664311548-Tally-Prime-Book.pdf` p.17 — "Step.1: GOT (Gateway of Tally) > Alter > Ledger > Select Ledger
  for Changes/Display & After Changes Press `Ctrl+A' for Accept."; `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:
  2050-2055` — "You can alter any information of your business Ledger master **except for the closing balance
  under the group Stock-in-Hand**. … Go to Gateway of Tally→Alter→Ledger→Enter. Or, Go To (Alt+G)→Alter
  Master→Ledger→Enter."
- **Group alter:** Book p.15 — "GOT > Alter > Group > Select `Group' for Alteration (Change)" then Ctrl+A.
- **Delete lives on the alteration screen:** Book p.17 — "GOT > Alter > Ledger > Select Ledger for Delete &
  Press Enter > Alt+D > Press Two times Enter"; Study Guide:2057-2067 — "Press Alt+D→supply Yes to confirm
  Deletion" + "**You cannot delete any ledger, if any transaction(s) has been already made with that ledger.**"
- **Delete guards:** `docs/tally-feature-catalog.md:126-127` — "cannot delete a ledger/group with transactions,
  sub-groups, or contained ledgers; cannot alter closing balance of Stock-in-Hand ledgers."
- **Multi-alter lives in the Chart of Accounts:** `docs/tally-feature-catalog.md:124-125` — "multi
  (`Chart of Accounts → Ledgers → Alt+H Multi-Master → Multi Create/Alter`)"; Study Guide:2111-2117
  "**Multiple Group Alteration** … Chart of Accounts → Groups → Enter → Press Alt+H for Multi-Masters → Multi
  Alter". **This is Tally's real entry point for group alteration** — see the verifier correction below.
- **Chart of Accounts is view-switchable:** Study Guide:1962 — "From Chart of Accounts, press **F5** to navigate
  between Ledger view and Group view." Not implemented anywhere.

**🟡 UNVERIFIED — and it matters:**

- **Identity-preserving retroactive rename.** `docs/tally-feature-catalog-verification-report.md:48` states it —
  but is explicitly tagged **[model-knowledge]**, i.e. **NOT corpus-grounded**. It matches our own domain design
  (`Ledger.cs:14` doc comment: "a rename does not change identity"; `Group.cs:6-7`: "The Id is the stable key —
  the Name is not, so an **Alter renames in place**") and comes free from the Guid + snapshot-save architecture,
  but per R7 it should be confirmed before being locked in as the fidelity target.
- **Does Enter on a ledger row inside Tally's Chart of Accounts open its Alter screen?** **NOT FOUND** in the
  catalogue or any of the 10 PDFs. The corpus only ever routes the CoA to Multi-Masters (Alt+H) and to views,
  and routes single-alter through the separate Gateway → Alter menu. **The CA asked for a CoA drill; Tally may
  not do that.** If Tally opens a *display* there, the CA is asking for a deliberate improvement over Tally —
  a decision to make consciously, not by accident.
- **`Alt+V` "Alter from drilldown"** (`catalog:469`) sits inside a section the catalogue itself header-flags at
  `:462` as "(reconciled — ⚠verify against current build)". Not settled.
- **Can a predefined group's name/parent be altered?** `catalog:116` only says predefined groups cannot be
  **deleted**; it is silent on alter. Confirm before adding a guard Tally does not have.
- **Stock-in-Hand closing balance:** the catalogue frames it as "cannot alter"; verification-report:46 (item 10,
  also [model-knowledge]) **reframes it as derived/computed, not a protected-but-editable field.** Both are
  [model-knowledge]; neither is corpus-grounded.

### Current state — NOT IMPLEMENTED, four independent lines of evidence

1. **No "Alter" verb exists in the UI.** A case-insensitive sweep for `alter` across `src/**/*.{cs,axaml}`
   returns **only SQL `ALTER TABLE`** in `Schema.cs`/`SqliteCompanyStore.cs` — **zero in `Apex.Desktop`**
   (the 75 apparent hits under `src/Apex.Desktop` are all `bin/Release` binaries). The Gateway builds
   Masters → Create only: `MainWindowViewModel.cs:830-831`
   (`col.Add(MenuItemViewModel.Header("Masters")); col.Add(new MenuItemViewModel("Create", …));`) and
   `:831`/`:832` are exactly "Create" and "Chart of Accounts". No Alter sibling. Searches for
   `IsAlterMode|AlterMode|LoadForAlter|EditingLedger|IsEditing|AlterLedger|EditLedger|UpdateLedger` → **0 hits.**
2. **Every master ViewModel is create-only.** All 22 `*MasterViewModel` ctors take `(Company, CompanyStorage,
   Action onChanged)` — no parameter for an existing master: `LedgerMasterViewModel.cs:330`,
   `StockItemMasterViewModel.cs:238`, `StockGroupMasterViewModel.cs:64`, `UnitMasterViewModel.cs:77`,
   `CostCentreMasterViewModel.cs:66`, `EmployeeMasterViewModel.cs:137`, `GodownMasterViewModel.cs:65`.
   `LedgerMasterViewModel` has exactly **one** public mutator, `public bool Create()` at **`:453`**, ending in
   `new DomainLedger(Guid.NewGuid(), …)` (`:568-569`) → `_company.AddLedger(ledger);` (`:605`). A grep for
   editing state across all of `src/Apex.Desktop/ViewModels/` matched only `PayrollVoucherEntryViewModel.cs:112`
   — a doc comment about discarding a stale preview.
3. **The Chart of Accounts is a read-only, ID-less, construction-time snapshot.**
   `ChartOfAccountsViewModel.cs:44` — "A **read-only** Chart-of-Accounts tree for the current company."
   `ChartRow` (`:29-41`) is `Name`/`Kind`/`Depth`/`Detail`, **all `{ get; init; }` — no Guid**, so a row cannot
   be resolved back to the Ledger or Group it represents. `Build()` (`:95`) runs once from the ctor (`:88`/`:92`);
   there is **no `Refresh()`**.
4. **No keyboard on the CoA.** `StepActive` (`MainWindowViewModel.cs:4433`) dispatches to per-screen
   `MoveHighlight(direction)` for Outstandings, Gstr2bRecon, ImsActions, PostItcReversal, GenerateEInvoice,
   GenerateEWayBill, ChallanReconciliation, Form26Q, Form24Q, Form16, TaxDeclaration, TcsChallanReconciliation,
   Form27EQ (`:4435-4522`) — **Chart of Accounts is absent**. `ActivateSelected()` (`:4556`) has no
   `Screen.ChartOfAccounts` case, so **Enter does nothing**. The XAML confirms it: the CoA rows render as a
   plain `<ItemsControl ItemsSource="{Binding Rows}">` (~`:3918`) with no selection brush — contrast the
   Outstandings template (`:3931`) binding `IsSelected`/`IsHighlighted` through `OutstandingRowBrushConverter`.

**Two fields are unreachable even at CREATE** (bears directly on "fully editable"):
- **Opening Balance:** `LedgerMasterViewModel.cs:566` comment "Opening balance defaults to 0"; `:569` hardcodes
  `Money.Zero`. **You cannot enter an opening balance in this app at all** — despite the corpus listing it as a
  create-screen field (Study Guide:2020-2030). Arguably a bigger gap than alter itself: an accounting app that
  cannot take opening balances cannot onboard an existing set of books.
- **Alias:** the string "Alias" appears **0 times** in `LedgerMasterViewModel.cs`, though `Ledger.Alias` exists
  and `catalog:116` lists it.

**The engine and storage are already sufficient — this is a UI-layer gap:**
- The domain aggregate is **fully mutable and was designed for this**: `Ledger.cs:15` `public string Name
  { get; set; }` (doc `:14` "a rename does not change identity"), `:18` `GroupId { get; set; }`,
  `OpeningBalance`, `OpeningIsDebit`, Alias/MaintainBillByBill/Interest/CurrencyId/PartyGst/Tds*/Tcs* all
  `{ get; set; }`. `Group.cs:6-7` says the same.
- **Persistence needs no change.** `SqliteCompanyStore.Save(Company)` (`:1569`) is a **full-snapshot replace**:
  `DeleteCompanyRows(tx, company.Id)` (`:1575`, defined `:4000`, "Child-first so foreign keys are satisfied")
  then re-inserts every table (`InsertGroups` :1581, `InsertLedgers` :1589 …). `CompanyStorage.cs:67` documents
  it: "Persists a company aggregate to its .db file (**create or replace**)." So mutate-in-memory +
  `_storage.Save(_company)` persists an alteration with **zero new SQL and no migration**. Because vouchers
  reference masters by stable Guid, a rename propagates to all history **for free**.
- **✅ SCHEMA-FREE CONFIRMED — this resolves the decoder's own blocking pre-flight question.** `Schema.cs`
  CreateV1 `CREATE TABLE ledgers` (`:669`) **already carries** `opening_balance_paisa` (`:674`),
  `opening_is_debit` (`:675`) and `alias` (`:676`). Opening Balance + Alias can be added at Create **and** Alter
  with **no v45, no CreateV1/Migrate parity work, no Io fold-in.**

### ⚠️ Verifier corrections — three of these change the plan materially

1. **🔴 "The domain aggregate is add-only" is FALSE, and so is "no Remove/Update counterpart for ANY master
   type".** `Company.cs` has a large `Remove*` API — **four of them inside the very range the decoder cited**:
   `:552 RemoveBatchMaster`, `:558 RemoveBillOfMaterials`, `:564 RemovePriceLevel`, `:570 RemovePriceList`; plus
   `:859 RemoveStockOpeningBalance`, `:871 RemoveStockGroup`, `:873 RemoveStockCategory`, `:875 RemoveUnit`,
   `:877 RemoveGodown`, `:879 RemoveStockItem`, `:889 RemoveGroup`, `:891 RemoveLedger`, `:893
   RemoveVoucherType`, `:897 RemoveCurrency`, `:899 RemoveExchangeRate`, `:901 RemoveCostCategory`,
   `:903 RemoveCostCentre`. Decisively, **`Company.cs:884-886` says: "Delete-guards for interactive Alter/Delete
   live in the services; these are the raw list removals the transactional importer needs…"** The codebase
   **anticipated this feature and named where the guards belong.**
2. **🔴 A guarded, TESTED mutation layer already exists for most masters — the effort estimate is inflated.**
   `InventoryService`: `SetStockGroupParent:44`, `DeleteStockGroup:55`, `SetStockCategoryParent:101`,
   `DeleteStockCategory:112`, `DeleteUnit:187`, `SetGodownParent:214`, `DeleteGodown:228`, `DeleteStockItem:295`
   — with **real referential guards** ("used by stock items" `:192`, "component of a compound unit" `:194`,
   "has child godowns" `:235`, "holds opening stock" `:237`/`:300`) and a predefined guard (`IsMainLocation`
   `:232`). **Four working cycle-checks exist to copy verbatim:** `EnsureStockGroupParentValid:66-83`,
   `EnsureStockCategoryParentValid:~123-134`, `EnsureGodownParentValid:241-257`,
   `PayrollService.EnsureEmployeeGroupParentValid:~330-341`, `EnsureAttendanceTypeParentValid:~495-506`.
   `PayrollService`: `DeleteEmployeeCategory:277`, `SetEmployeeGroupParent:308`, `DeleteEmployeeGroup:319`,
   `DeleteEmployee:392`, `DeletePayrollUnit:441`, `SetAttendanceTypeParent:475`, `DeleteAttendanceType:486`.
   **`PayHeadService`: `RenamePayHead:91`, `SetComputation:105`, `DeletePayHead:116` — an identity-preserving
   rename ALREADY EXISTS in the engine.** Plus `BomService.DeleteBom:74`, `BatchService.DeleteBatch:62`,
   `PriceListService.DeleteLevel:77`, `ReorderLevelsService.Delete:60`,
   `SalaryStructureService.DeleteSalaryStructure:87`. **All tested** (`InventoryMastersTests.cs`,
   `PayHeadServiceTests.cs`, `PayrollServiceTests.cs`, `Inventory/BatchTests.cs`, and a persistence round-trip in
   `tests/Apex.Persistence.Sqlite.Tests/InventoryRoundTripTests.cs`). **This whole surface is DEAD CODE from the
   UI** — the only Desktop call sites are `EmployeeMasterViewModel.cs:222`/`:227`, and those run inside the
   *create* flow. So the capability is genuinely NOT_IMPLEMENTED for a user, despite a rich engine.
3. **🔴 THE ASYMMETRY — the single most useful correction, and it reshapes the slice.** Engine coverage falls
   **exactly on the two masters the CA named**:
   - **Accounting Ledger + Group: NO service-layer mutation/delete/rename/re-parent exists.** There is **no
     `GroupService`**. `LedgerService` is the **voucher posting service** (`LedgerService.cs:5-10`: "The posting
     service (design §8.2)… Cancel (Alt+X) and Delete (Alt+D)") — its `Delete:106` removes a **voucher**, not a
     ledger master. For Ledger/Group the guards must genuinely be **written**.
   - **Everything else** (stock items/groups/categories/units/godowns, employees/groups/categories, pay heads,
     BOM, batches, price levels, reorder defs, salary structures): engine guards **exist and are tested** ⇒
     **UI WIRING ONLY.**
   **So the slice splits: engine work for Ledger/Group only; UI-only for ~15 other master types.**
4. **🔴 "Arrow keys on the CoA are actively destructive" is FALSE — they are INERT no-ops.** `OpenPageColumn`
   sets `ActiveColumnIndex = Columns.Count - 1` (`:4093`), and `ShowChartOfAccounts` (`:2662`) builds it via the
   **2-arg** `GatewayColumn` ctor (`GatewayColumn.cs:236`), so `IsMenu => Page is null` (`:34`) is **false**.
   `StepActive`'s `IsGatewayCascade` branch therefore hits `if (col is null || !col.IsMenu) return;`
   (`:4526-4527`) and returns immediately — `TrimColumnsAfter`/`ClearSubScreens` are **never reached**.
   **Consequence: the decoder's proposed "REGRESSION LOCK: Up/Down must not close the pane" locks a bug that
   does not exist and would pass on today's code. Do not write it.** The remediation (add a CoA arm to
   `StepActive` before `:4524`) is unaffected and still correct.
5. **`Ledger.IsPredefined` is NOT unread.** It **is** read and round-tripped: `SqliteCompanyStore.cs:4707`,
   `CanonicalMapper.cs:407`, `CanonicalXml.cs:257`; and `PayrollService.cs:281` (`if (category.IsPredefined)`)
   is a genuine delete-guard consumer. The **true, narrower** claim: it is never enforced as a **protection
   guard on ledgers**.
6. **🔴 Rename blast radius was MATERIALLY understated, and the proposed guard does not cover it.** ~14 engine
   sites resolve ledgers by **hardcoded well-known name**, and a rename breaks them **silently**. Worst:
   `B2cQrService.cs:143` — `if (_company.FindLedgerByName(GstService.RoundOffLedgerName) is not { } roundOff)
   return 0m;` ⇒ **renaming "Round Off" makes B2C-QR rounding silently return 0, with no error.** Also
   `OutstandingsViewModel.cs:127` `FindLedgerByName("Cash")`; `RunSetOffViewModel.cs:302`;
   `ElectronicLedgersView.cs:116`; `GstDepositService.cs:290`; `AdvanceReceiptService.cs:214`;
   `ForexReportViewModel.cs:233`; `GstConfigViewModel.cs:1401`. **An `IsPredefined`-only guard DOES NOT COVER
   THESE:** `Ledger`'s ctor defaults `bool isPredefined = false` (`Ledger.cs:169`), `GstService.cs:232` creates
   "Round Off" **without** passing it, and `SeedLedgers.cs:31-32` shows only **two** ledgers are ever predefined
   (Cash, P&L A/c). **The guard set must extend to the well-known `LedgerName` constants** (`GstService.cs:61`/
   `:236`/`:258`/`:280`/`:302`, `ForexGainLoss.cs:66`, `PayrollVoucherService.cs:31`/`:34`/`:37`/`:41`+).
   Also note `OpenAccountBook(item.Label)` (`MainWindowViewModel.cs:4886`) passes a ledger **name** string.
7. **Tally's real group-alter entry point is the Chart of Accounts**, not a Gateway → Alter menu (Study
   Guide:2111-2117, Multi Alter via Alt+H). Our `ChartOfAccountsViewModel.cs:43` is explicitly read-only. This
   **supports point 5's CoA framing** and should shape where Alter lives.
8. **Line drift (all still findable):** `ActivateSelected` is **`:4556`** (`:4554` is its doc comment);
   `case Screen.LedgerMaster: LedgerMaster?.Create();` is **`:4569-4570`**, not 4568; the CoA `ItemsControl` is
   ~**`:3918`** (its ContentControl opens `:3892`); `Ledger.cs` Name is `:15` (doc `:14`), GroupId `:18`;
   `Group.cs` doc `:6-7`; catalogue "Creation modes" starts `:124`; `Create()` is **`:453`** (not ~444).

### The prerequisite this exposes

**`case "Group": ShowLedgerMaster(); break;` (`MainWindowViewModel.cs:4897`) — CONFIRMED REAL BUG.**
Masters → Create → **Group** silently opens **Ledger Creation**. There is no accounting-Group master
(`Glob **/GroupMaster*.cs` → no files; no `Screen.GroupMaster` in the 98-value enum; only `StockGroupMaster`
`:49` and `EmployeeGroupMaster` `:109` exist). **WI-7 is a prerequisite for point 10's "groups".**

### Gap

1. No alteration entry point of any kind — no Gateway → Alter, no CoA Enter-drill, no Alt+H Multi-Alter.
2. `LedgerMasterViewModel` is one-directional: it **writes** a new ledger from its fields but never **loads** an
   existing one into them. All ~20 gated sub-forms (interest, currency, party GST, TDS/TCS, price level,
   appropriation, bill-by-bill + credit period) have write-side mapping only.
3. `Create()`'s uniqueness check (`:468`, `if (_company.FindLedgerByName(name) is not null)`) has **no
   except-self exclusion** — reused as-is on an Alter it **fails closed on the most common case of all**: open a
   ledger, change one unrelated field, accept without renaming → *"A ledger named 'X' already exists."*
4. The CoA has no selection model (no Id on `ChartRow`, no `IsSelected`), no `MoveHighlight`, no `StepActive`
   arm, no `ActivateSelected` case, and no `Refresh()`.
5. "Fully editable" is unattainable for two fields never capturable: Opening Balance + Dr/Cr, and Alias.
6. Alter-side guards unenforced: predefined Cash/P&L protection, the well-known-name set (correction 6), the
   Stock-in-Hand carve-out, and — if delete is in scope — the has-transactions block.
7. No engine mutation layer for **Ledger and Group specifically** (correction 3).
8. No accounting-Group master at all (WI-7).

**NOT a gap — do not spend the slice here:** schema, persistence, the Io canonical layer, and the domain model.

### Proposal

**No schema change — v44 stands** (confirmed: `Save` is delete + full re-insert; `alias`, `opening_balance_paisa`
and `opening_is_debit` already exist in CreateV1). **The ONLY thing that would force v45 is an alteration AUDIT
TRAIL** — which Tally does keep and which belongs to **Phase 10 (security/roles/audit)**. Confirm that exclusion.

**Do points 5 and 10 as ONE slice.** Engine-first, TDD per R8.

1. **Domain** (`Company.cs`, beside the Add*/Remove* block): add `UpdateLedger`/`UpdateGroup`/`UpdateStockItem`…
   resolving by stable Guid. Thin, because the objects are already mutable and Save is a snapshot replace. Add
   `RecomputeNatureFor(Guid groupId)` re-deriving `Group.Nature` from the primary ancestor **and cascading to
   descendants** — required by `Group.cs:18`'s own invariant.
2. **Engine services — write these for Ledger and Group only** (everything else already has them): a
   `GroupService` (**shared with `ImportPlan` — see WI-7**) and a ledger equivalent, following the
   `InventoryService.CreateStockGroup:31` template ("enforces unique name + valid, non-cyclic parent") and
   copying one of the four existing cycle-guards verbatim.
3. **Shared alter rules** (new `src/Apex.Ledger/Services/MasterAlterationRules.cs`) so 20+ screens don't
   re-implement them: name-uniqueness-**excluding-self** (fixes gap 3); predefined-master protection;
   **well-known-name protection** (correction 6); group re-parent cycle + nature check; Stock-in-Hand
   carve-out. **Unit-test this class directly — it is the risk surface.**
4. **Make `LedgerMasterViewModel` bidirectional.** Add `ForAlter(Company, CompanyStorage, Guid ledgerId,
   Action onChanged)` holding `_editingId`; add `LoadFrom(DomainLedger)` mirroring the write block at
   `:566-603`. **Refactor `Create()`'s validation + field-building into a shared `TryBuildInto(DomainLedger)`**
   so `Create()` = new + TryBuildInto + AddLedger and `Alter()` = FindLedger + TryBuildInto + Save.
   **Do NOT fork the validation or hand-write a second mapping — a copy will drift, and an omission silently
   wipes a field** (see risk 1). Fix `:468` to `FindLedgerByName(name) is { } other && other.Id != _editingId`.
   Add `IsAltering` for the title/caption.
5. **Make the CoA selectable + keyboard-driven.** Add `Guid? LedgerId` / `Guid? GroupId` to `ChartRow` (`:29`)
   — **additive**, so `MasterListTabularProjector.ProjectChartOfAccounts` (`:73`) and the export tests keep
   passing. Make `ChartRow` an `ObservableObject` with `IsSelected`. Add `SelectedRow` + `MoveHighlight(int)`
   copying the house pattern from `OutstandingsViewModel.cs:86` (or `ImsActionsViewModel.cs:163`). Add
   `Refresh()` and have `Build()` preserve the highlighted Id across a rebuild.
6. **Wire the shell.** Add `IsChartOfAccountsScreen`; add a `StepActive` arm **before** the `IsGatewayCascade`
   branch at `:4524`; add a `case Screen.ChartOfAccounts:` to `ActivateSelected()` (`:4556`) → `ShowLedgerAlter(
   row.LedgerId.Value)`; add `ShowLedgerAlter(Guid)` beside `ShowLedgerMaster()` (`:2642`) with
   `onChanged: () => ChartOfAccounts?.Refresh()`; branch `:4569-4570` on `IsAltering`. **If a Gateway → Alter
   menu is also wanted** (Q1): note the dispatch switch at `:4891` is keyed on **label strings**, so "Ledger"
   under Alter would collide with "Ledger" under Create — it needs a `CurrentGatewayMenu` guard exactly like the
   Cash Book/Bank Book one already at `:4883`.
7. **Tests.** Round-trip: create → alter **every** field → Save → reload via `CompanyStorage` → assert each field
   survived (**this is the test that catches an asymmetric `LoadFrom`** — diff every field before/after a no-op
   alter and assert equality). Rename keeps identity (post a voucher, rename, assert the Guid is unchanged, the
   voucher still resolves, and no duplicate master appears). Accept-without-rename succeeds (locks gap 3).
   Rename to a **different** ledger's name still rejected. Re-parent recomputes nature and cascades. Predefined
   + well-known-name guards. CoA: `MoveHighlight` skips group rows and wraps; Enter opens LedgerMaster with
   `IsAltering` true and the right ledger loaded. Regression-lock the **Robert** and **Bright** fixtures: an
   alter that *should* be neutral leaves every report byte-identical. Prove reachability **from the ROOT column**
   and render-verify headlessly, then **revert `TestAppBuilder`**. **Do not write tests that assert nothing** (a
   prior A10 pass found 4).

### Risk

- **🔴 Silent financial corruption — the big one.** Alter is the **first feature that mutates masters already
  referenced by posted vouchers.** Re-pointing a ledger's `GroupId` silently reclassifies every historical
  transaction between the Balance Sheet and the P&L; editing an opening balance silently restates prior
  periods; re-parenting a group with posted ledgers **retroactively rewrites history** (`plan.md:221` mandates
  exactly that: "Alter renames in place and **applies retroactively to all historical vouchers**"). Moving
  "Salary Payable" from Current Liabilities to Indirect Expenses would move every historical balance from the
  Balance Sheet to the P&L and restate prior-period profit — and `BalanceSheet.cs:84` already skips P&L groups,
  so the ledgers would simply **vanish** from the sheet. **No error. Just different, wrong financials.**
  Tally guards some of this; we guard none of it.
- **🔴 Asymmetric `LoadFrom` = silent data loss.** `LedgerMasterViewModel` carries ~20 conditionally-gated
  fields across 6 sub-forms. Miss one and altering a ledger **silently wipes** it — the screen shows a default,
  the user accepts, the data is gone with no error. Exactly the shape A10 caught on every prior slice.
  **Mitigation: derive both directions from one `TryBuildInto`; test the full round-trip field-by-field.**
- **🔴 Rename blast radius** (correction 6) — `B2cQrService.cs:143` is the worst, but audit **every** by-name
  lookup before shipping rename.
- **🟠 Cycles.** `Company.AddGroup` (`:521`) is a bare `_groups.Add` with zero validation. A cyclic parent throws
  deep inside `ClassificationRules.PrimaryAncestorOf` (guarded at 1024 iterations, `:24-25`) — i.e. surfaced as
  a raw `InvalidOperationException` **from a report**, not a friendly message at the master.
- **🟠 Name collision with the reserved P&L head.** `Company.FindGroupByName` (`:994`) deliberately **excludes**
  `ProfitAndLossHead`; `FindGroupOrHeadByName` (`:1005`) includes it. **Use the head-including variant for the
  duplicate check**, or a user can create a second "Profit & Loss A/c" and fork the classification.
- **🟠 Full-aggregate save.** `Save` deletes and reinserts **every** row for the company. Correct and
  transactional — but any half-built in-memory aggregate gets persisted wholesale. Prior art is nasty (Phase 9
  UI-3: a phantom record with a dangling FK made a company permanently unsaveable). Regrouping a ledger must not
  orphan rows that FK it (`SqliteCompanyStore.cs:4029`/`:4120`/`:4139`).
- **🟠 Stale-snapshot tree.** `Build()` runs once in the ctor with no `Refresh` — the user sees the OLD name,
  believes the save failed, re-alters, and confusion follows.
- **🟠 UI-defect campaign collision.** The CoA template (`MainWindow.axaml:3869`) is squarely in the 328-defect
  problem space (the ledger picker column pinned ~298px beside dead cream; names ellipsizing).
- **🟡 Reachability** — gate at the ROOT too; a `ShowXMenu()` test proves nothing.
- **🟢 No schema risk** — confirmed, including for Alias and Opening Balance.

### Open questions

1. **(BLOCKING — scope) Where does single-ledger alter live?** The corpus says `Gateway → Alter → Ledger`; the
   CA said "in the chart of accounts"; and Tally's real **group**-alter entry point is the **CoA + Alt+H Multi
   Alter**. Build the Gateway menu, the CoA Enter-drill, or both? **Recommend both.**
2. **(BLOCKING — the L-vs-XL fork) Does "all" mean only items/ledgers/groups, or every master type?** The app has
   ~22 create-only master screens. **Note the answer is cheaper than it looks:** ~15 of those already have a
   tested engine layer and need **UI wiring only** (correction 3). Points 12 and 15 imply **Units** are expected
   too.
3. **(BLOCKING — sizing) Does "fully editable" include Opening Balance + Dr/Cr and Alias?** Neither is capturable
   today even at Create. **Schema-free** (confirmed). Should opening-balance capture be split into its own
   point? Worth putting back to the CA — they may have meant to raise it separately.
4. **Is DELETE in scope?** Tally pairs Alter with `Alt+D` + Y/N confirm, guarded by "no transactions". The CA
   said "editable", not "deletable". WI-11 would supply the confirm infrastructure anyway.
5. **Should altering a master's classification (a ledger's group, a group's parent) be ALLOWED, WARNED, or
   BLOCKED once transactions exist?** A fidelity + financial-safety decision — **not** the implementing agent's
   call.
6. **How deep does "keyboard logic" go?** Up/Down + Enter only, or also the corpus-documented **F5** Ledger-view
   ↔ Group-view toggle (Study Guide:1962) and **Alt+H** Multi-Masters → Multi Alter? Both are entirely absent
   (zero matches across `src/`). Multi-alter is a substantial separate feature.
7. **Is an alteration audit trail required now?** The only thing that would force v45. **Recommend deferring to
   Phase 10.**
8. **(A14 / R7)** Confirm the identity-preserving retroactive-rename semantics — `verification-report:48` is
   tagged **[model-knowledge]**, not corpus-grounded.
9. **(A14 / R7)** Does Enter on a CoA ledger row in real Tally open Alter, or a Display? **NOT FOUND** in the
   corpus. If "Display", the CA is asking for a deliberate improvement over Tally.
10. **(A14 / R7)** Can a **predefined** group's name/parent be altered in Tally? The catalogue is silent — do not
    add a guard Tally does not have.

---

# WI-4 — Party ledger Mailing Details (address + PIN code)

**Covers point 1.** **State: NOT IMPLEMENTED.** **Effort: M.**

### Raw wording (verbatim fragments)

> "under ledger" · "input of address, i.e., filling of address should be present" · "along with PIN code"
>
> *(The brief notes this point contains the typo "leisure creation", almost certainly meaning "**ledger**
> creation".)*

### Decoded requirement

When creating (and altering) a ledger whose group resolves — **through the full ancestry, not just the direct
parent** — to **Sundry Debtors** or **Sundry Creditors** (i.e. a "party" ledger), the master must offer a
**Mailing Details** block that is captured, persisted, round-tripped through import/export, and **consumed by
invoice printing**. Minimum fields: **Mailing Name** (defaulted from Name, editable), **Address** (free-text,
multi-line), **Country**, **State**, and — called out explicitly by the CA — **PIN code**.

The CA singling out "along with PIN code" is the diagnostic detail: PIN is not decorative. It (a) completes a
legally printable tax-invoice recipient block and (b) is a mandatory element of the NIC INV-01 e-invoice
`BuyerDtls` and of e-Way Bill distance validation.

### Tally fidelity target — ✅ GROUNDED that the block exists; 🟡 PARTIAL on the exact field list

**Direct — a party ledger carries Mailing Details** (and, per the verifier, the corpus attests **both** halves
of the CA's debtor/creditor ask):

- `tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf:754` — "In the field of Party's, A/c Name with the help of
  (ALT+C) create a ledger of NARESH TRADERS **under Sundry Debtors with Mailing Details & Tax Information**."
  *(This also grounds WI-1: an Alt+C inline-creation form must expose this same block.)*
- `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:3926` (p.138, TDS chapter; ledger "Mohit Agarwal", **Under:
  Sundry Creditors**) — step 7: "**Enter Mailing Details** and PAN No. (PAN Number is mandatory)".
- `...:4142` — the same instruction for "**Godrej Interio**", whose Under is **Sundry Debtors** (`:4137`).
  **⚠️ The decoder called this merely "the same pattern"; the verifier established it is a Sundry *Debtor*.**
  So the corpus attests the block on **both** a Sundry Creditor and a Sundry Debtor. **Cite this explicitly.**

**Direct — a ledger screen carries Address/State/Pincode:**
- `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:6151` (p.233-234, POS chapter, **bank** ledger "ICICI
  Bank-Credit Card") — step 8: "**Provide other details like Address, State, Pincode etc., as required**".
  *(Note: this is a **bank** ledger, not a party — see openQuestion 3 on scope.)*

**Superset feature, almost certainly out of scope:** `tally/703679456-…:808-830` — "The Multi Address feature …
allows you to **maintain multiple mailing details for your company and ledgers**"; "F11: Features>Other>Enable
multiple Address"; "In Under select **Sundry Debtors** … In **Mailing details** enter the details of party's
THANE BRANCH"; "Address Type Screen of Arya Enterprises appears". **⚠️ Verifier note: line 808 literally reads
"The Multi Address feature in *Tally.ERP9* allows you to…" — the decoder elided the *predecessor product name*.
The passage sits in a Tally Prime book and gives Tally-Prime F11 steps, but the sentence is literally about
Tally.ERP9.** Immaterial while out of scope; note it if that feature is ever built.

**🟡 UNVERIFIED — "Mailing Name" and "Country" on a *ledger*.** The corpus references the block **by name**
("Enter Mailing Details and PAN No.") and separately enumerates "Address, State, Pincode" on a ledger screen,
but **never renders a fully labelled party-ledger Mailing Details screenshot**. Mailing Name and Country are
**inferred by symmetry from the Company block** (`docs/tally-feature-catalog.md:87`: "Name, Mailing Name (auto,
editable), **Address, Country (India), State, Pin**") and are **NOT directly attested for a ledger.** A14 must
confirm the exact list and order before build. **The verifier strengthens the case that this is warranted:
even the COMPANY block's field order varies by source** — `664311548-…:457-461` = Mailing Name, Address, State,
Country, Pin Code; `696054070-…:6733-6740` (Group Company) = Mailing Name, Address, State, Country, Pincode;
`catalog:87` = Address, Country (India), State, Pin.

### 🔴 Catalogue defect — likely the root cause of the gap (R7)

`docs/tally-feature-catalog.md:117-127` enumerates Ledger "**Fields:** Name, Alias, Under (group), Opening
Balance (Dr/Cr)" plus feature-gated bill-by-bill / interest / bank / GST-TDS-TCS / currency blocks — and
**omits address/mailing entirely**, while the Company section (`:87`) **does** list "Address, Country (India),
State, Pin". **An implementer working the catalogue faithfully would build exactly what exists today.**
The catalogue needs a sourced correction alongside this fix, or the gap will be re-litigated.

**⚠️ Verifier correction — the decoder's supporting evidence for this was FALSE, but the thesis survives.**
The decoder claimed: *"Cross-checked `docs/tally-feature-catalog-verification-report.md`: grep for
`pincode|pin code|postal code|mailing details|address` returned **no matches**."* That is wrong —
**`verification-report:139`** reads: "**Party multi-address (Additional Contact/Address Details)** — §3.
Multiple billing/shipping addresses per party, selectable at Sales/Purchase entry (Party Details screen)",
under the "### PHASE-2" heading. This matters twice: **(a) it partly answers openQuestion 4** — the project has
**already triaged party multi-address as a §3 enrichment at PHASE-2 priority**, i.e. already deferred it; and
**(b)** the verifier found further support the decoder missed — **`verification-report:123`**: "**Ledger
\"Credit Limit\" field + PAN/IT No. + MSME/Udyam fields** — §3 (ledger fields)", **independent evidence that §3
ledger fields are known-incomplete.** The narrow claim still stands: **neither doc adds a basic single
address/PIN to the Ledger field list.**

### Current state — no address/mailing/PIN field exists anywhere in the stack

**The codebase states this about itself, in a comment.** `src/Apex.Desktop/Services/VoucherPrintProjector.cs:
211-217` — the buyer block on **every printed invoice**:
```csharp
Name = ReportPrintProjector.Ascii(party?.Name ?? string.Empty),
// A party ledger has no address field in the current model; the State + GSTIN identify the recipient.
AddressLines = Array.Empty<string>(),
Gstin = ReportPrintProjector.Ascii(party?.PartyGst?.Gstin ?? string.Empty),
```
The comment at `:213` is a standing acknowledgement of exactly this gap, and `:214` hardcodes the empty result.

- `src/Apex.Ledger/Domain/Ledger.cs:9-224` — read in full. Fields are Id, Name, GroupId, OpeningBalance,
  OpeningIsDebit, Alias, IsPredefined, MaintainBillByBill, DefaultCreditPeriodDays, CostCentresApplicable,
  EnableChequePrinting, ChequePrintingBankName, Interest, CurrencyId, PartyGst, SalesPurchaseGst,
  GstClassification, MethodOfAppropriation, DefaultPriceLevelId, Tds*/Tcs*/PartyPan. **No Address, no Pin, no
  Mailing anything.** Ctor at `:162-181` confirms.
- `src/Apex.Persistence.Sqlite/Schema.cs:669-699+` — `CREATE TABLE ledgers` has no address columns.
- `src/Apex.Ledger.Io/CanonicalModel.cs:650+` — `LedgerDto` mirrors the domain; no address.
- `src/Apex.Ledger.Io/EInvoiceJson.cs:357-361` — `PartyDtlsDto { Gstin, StateCode }` only; consumed at `:140`
  `BuyerDtls = new PartyDtlsDto { Gstin = partyGst?.Gstin, StateCode = … }`. *(This emitter self-flags as
  offline/unconfirmed at `:20-30`, so it is not claimed "broken" — but it cannot carry a buyer PIN because no
  buyer PIN exists to carry.)*

**Searches run (all negative for a ledger):** `PinCode|Pincode|pincode` → **0 files repo-wide**;
`mailing_address|ledger_address|party_address|mailing_state|contact_person` → **0 hits in `src/` and `tests/`**;
`MailingName` → only `Company.cs:65`/`:514`. Every `pin` hit across `src/` is the **Company** pin
(`SqliteCompanyStore.cs:1090`/`:1131`/`:4239`/`:4262`/`:4290`; `Schema.cs:126`; `CanonicalXml.cs:55`/`:966`;
`CanonicalMapper.cs:67`; `CsvCanonicalBridge.cs:187`; `ImportPlan.cs:1157`; `CanonicalModel.cs:48`).
*(Verifier nit: `\bpin\b` also hits prose — `ManufacturingJournalService.cs:83`, `BatchStockService.cs:10` "pin a
specific batch" — and the distinct GST-challan "Cpin" — `GstChallan.cs:65`, `CanonicalXml.cs:1458`,
`RunSetOffViewModel.cs:99`. Immaterial: no party PIN field exists.)*

### What already exists — this is unusually cheap for its value

- **The end-to-end precedent:** `src/Apex.Ledger/Domain/Company.cs:67-70` — `Address` (`:67`), `Country` (`:68`),
  `State` (`:69`), `Pin` (`:70`). The Company already does exactly this, **including Io round-trip.**
- **The sink already exists:** `src/Apex.Ledger.Io/InvoicePrintData.cs:14-15` — `AddressLines` ("Address lines
  (each printed on its own line); may be empty").
- **⚠️ Verifier addition — the sink is not merely declared, it genuinely RENDERS:** `InvoicePdf.cs:398`
  `foreach (var line in party.AddressLines)` and `:135` counts non-blank lines for layout. **So replacing
  `VoucherPrintProjector.cs:214` flows straight to the PDF.** The "consumer is wired; only the source is
  missing" claim is **stronger** than the decoder stated.
- **The formatter already exists:** `VoucherPrintProjector.cs:236` `SplitAddress(string?)` splits free-text into
  lines; already used for the **seller** at `:205`. Directly reusable for the buyer.
- **The gate already exists and is already in use:** `src/Apex.Desktop/ViewModels/LedgerMasterViewModel.cs:
  421-433` `IsUnderParty(Group)` walks group ancestry (64-deep guard) to "Sundry Debtors"/"Sundry Creditors";
  surfaced as `IsPartyGroup` at `:402`. **Precisely the gate this point needs.**
- **The conditional-block pattern to copy:** `LedgerMasterViewModel.cs:236` `ShowPartyGst => GstEnabled &&
  IsPartyGroup` and `:292` `ShowPartyTdsTcs => ShowTdsTcs && IsPartyGroup`.
- **The insertion point:** the rendered "GST Details (party)" `<Border>` with `IsVisible="{Binding ShowPartyGst}"`.

### ⚠️ Verifier corrections

1. **🔴 WRONG LINE — and it would be a XAML compile error.** The decoder placed the party-GST border at
   **":3594-3638"** and said to insert the new block "immediately **before** the party-GST border at `:3594`".
   **The actual bounds are `3616-3660`** (comment `:3616`; `<Border Background="#FBF7EE"` opens `:3617`;
   `Padding="12,10" IsVisible="{Binding ShowPartyGst}">` `:3619`; `<TextBlock Text="GST Details (party)"`
   `:3621`; `</Border>` `:3660`). A consistent **+22-line shift** at both ends. **Line 3594 sits INSIDE the
   Method-of-Appropriation `<ComboBox.ItemTemplate>` — inserting a Border there is a XAML compile error.**
   **INSERT BEFORE `:3616`.** *(These are HEAD-relative; re-resolve against your tree — see the drift warning.)*
   Mitigation: the decoder also anchored by **content** (Border + `IsVisible=ShowPartyGst` + the
   `ColumnDefinitions="110,*"` row idiom, independently confirmed at `:3623` and `:3630`), so intent is
   recoverable — but **fix the number before handing this to an implementer.**
2. **The verification-report grep claim was FALSE** — see the Catalogue defect section above.
3. **`Create()` is at `:453`**, not "~:444". *(`new DomainLedger(` opens `:568` with `Guid.NewGuid()` literally
   on `:569`, so the `:569` cite is fine.)*
4. **CLEARED — do not chase:** `ApplyJournal.cs:324`/`:333`/`:347` threads the Company Pin and is **absent** from
   the decoder's Io fold-in list. Verified **correct to omit**: ApplyJournal's ledger path is wholesale
   (`RecordLedger :98` / `RemoveLedger :274`); only *openings* get field-level snapshots
   (`RecordLedgerOpeningSnapshot :148`). A new nullable `Ledger.Mailing` property needs **no** ApplyJournal
   change.

### Coupling that affects sequencing

**`LedgerMasterViewModel` is create-only** — see WI-3. Any field added here is **create-only until ledger
alteration exists.** A user who mistypes a PIN cannot fix it — **arguably worse than no field at all.**

### Gap

1. **Persistence/domain:** no `Ledger` field to hold a mailing address; no `ledgers` columns; no `LedgerDto`
   member ⇒ nothing to save, and nothing for JSON/XML/CSV to round-trip.
2. **UI:** no Mailing Details block. A user creating "Naresh Traders" under Sundry Debtors **physically cannot
   enter an address or PIN**.
3. **Consumption:** every printed invoice emits a blank recipient address (`VoucherPrintProjector.cs:214`), and
   the e-invoice `BuyerDtls` cannot carry Addr/Loc/Pin.

The gap is **shallow for its value**: the sink, the formatter, the gate, and the end-to-end precedent all exist.
What is missing is the **source field** and the **form that fills it**.

### Proposal

**Schema v44 → v45.** Follow the `PartyGstDetails` precedent exactly (a nullable value-object hung off `Ledger`),
which keeps every existing ledger **byte-identical** (the ER-13 discipline this project uses).

1. **NEW** `src/Apex.Ledger/Domain/PartyMailingDetails.cs` — mirror `PartyGstDetails.cs`: `MailingName`,
   `Address` (free text, newline-separated), `Country`, `State`, `Pincode`, plus `EnsureValid()` (fail-fast per
   ER-6; PIN rule per Q2).
2. `Ledger.cs` — add `public PartyMailingDetails? Mailing { get; set; }` as a **post-construction property**
   (like `TdsApplicable` at `:129`), **not** a new ctor param — avoids touching the 20-arg ctor at `:162-181`
   and every call site.
3. `Schema.cs` — nullable TEXT columns (`mailing_name`, `mailing_address`, `mailing_country`, `mailing_state`,
   `mailing_pincode`) in `CREATE TABLE ledgers` (~`:669`) **and** a new `MigrateV44ToV45` following the
   `MigrateV43ToV44` shape at `:3382`; bump `CurrentVersion` `:99` → 45.
   **⚠️ Read the `Schema.cs` structural warning in WI-10 before editing: CreateV1 spans lines 114-1679 and IS the
   full current v44 DDL; the migrations from `:1687` onward are FROZEN HISTORY. Edit CreateV1 + a NEW migration
   — never an existing one.**
4. `SqliteCompanyStore.cs` — migration dispatch (copy the `:1063` `MigrateV43ToV44` branch) + ledger
   INSERT/SELECT plumbing.
5. **Io fold-in (mandatory, or export silently drops the address):** `CanonicalModel.cs:650` `LedgerDto`
   (+ a `PartyMailingDto`), then `CanonicalMapper.cs`, `CanonicalXml.cs`, `CsvCanonicalBridge.cs`,
   `ImportPlan.cs`. **Trace how `Company.Pin` threads through** (`CanonicalXml.cs:55` write / `:966` read,
   `CanonicalMapper.cs:67`, `CsvCanonicalBridge.cs:187`, `ImportPlan.cs:1157`) **and copy that path.**
6. `LedgerMasterViewModel.cs` — add `ShowMailingDetails => IsPartyGroup` (**deliberately NOT feature-gated** —
   unlike `ShowPartyGst` at `:236`, Tally's mailing block is not behind F11); `[ObservableProperty]` fields;
   raise `OnPropertyChanged(nameof(ShowMailingDetails))` in `OnSelectedGroupChanged` (`:404-413`); build the
   block in `Create()` (~`:509`, alongside the `partyGst` build); clear it in the reset path (~`:618`).
7. `MainWindow.axaml` — a new "Mailing Details" `<Border>` **immediately before `:3616`** (see correction 1),
   copying its `ColumnDefinitions="110,*"` row idiom; Address as a multi-line `TextBox` (`AcceptsReturn`,
   `TextWrapping`).
8. **The payoff:** `VoucherPrintProjector.cs:211-217` — replace `AddressLines = Array.Empty<string>()` with
   `SplitAddress(party?.Mailing?.Address)` (reusing `:236`) and **delete the now-false comment at `:213`**.
9. **Build the block as a reusable unit, not inline XAML** — the corpus citation for the party mailing block is
   *literally an Alt+C inline-creation instruction* (`703679456:754`), so **WI-1's inline form must expose the
   same block.**
10. **Tests:** extend `tests/Apex.Persistence.Sqlite.Tests/SchemaMigrationEquivalenceTests.cs:31` (CreateV1 ≡
    migrated-v45 parity); a VM test asserting the block shows for a **nested** child of Sundry Debtors and hides
    for a non-party group; an Io **lossless round-trip** test; and a print test asserting a real buyer address
    renders — **that last one guards the exact regression the `:213` comment describes.**
11. **Also (R7):** patch `docs/tally-feature-catalog.md:117-127` to add the mailing block to the Ledger field
    list, citing the PDFs above — otherwise the catalogue keeps saying this feature shouldn't exist.

### Risk

- **🔴 The State collision is the real design risk.** `PartyGstDetails.StateCode`
  (`src/Apex.Ledger/Domain/PartyGstDetails.cs`) already exists and **drives place of supply → CGST/SGST vs
  IGST**. A second, independent mailing "State" that disagrees with it is a **tax-computation hazard**, not a
  cosmetic one: a user could set mailing State = Maharashtra while GST State stays Karnataka and get the **wrong
  tax head with no warning.** **Resolve Q1 before build.**
- **🔴 Io lossless round-trip.** An incomplete fold-in means export→import **silently drops the address** — no
  error, data just vanishes. This project has been bitten by silent-drop bugs repeatedly. **The round-trip test
  is not optional.**
- **🟠 Schema (v44→v45) — the first migration after Phase 9**, reopening a path dormant since S7. The classic
  failure is CreateV1/MigrateV44ToV45 divergence (column order, type, a forgotten default).
  `SchemaMigrationEquivalenceTests` exists to catch it and **must be extended, not just run.** All columns
  nullable TEXT with no default ⇒ existing rows unaffected.
- **🟠 Create-only surface.** Fields added now are unreachable for existing ledgers until WI-3 lands. **Sequence
  with WI-3.**
- **🟡 UI truncation.** Adding a multi-line Address box to an already-dense screen is exactly the shape the
  campaign warns about (starved `*` columns, horizontal StackPanels killing TextWrapping). Render-verify
  headlessly — `IsEffectivelyVisible` stays true when a parent clips.
- **🟢 Low:** no engine/valuation/tax computation changes; the Robert and Bright fixtures are untouched.

### Open questions

**For the user / CA:**
1. **🔴 Mailing State vs GST State — the blocking decision.** Options: **(a)** ONE State field in Mailing
   Details, with GST place-of-supply defaulting from it (closest to Tally, avoids contradiction); **(b)** two
   independent fields (real for a branch-delivery case, but invites silent tax errors); **(c)** two fields with
   a divergence warning. **Recommend (a); needs sign-off.**
2. **Is PIN mandatory, and for whom?** Free text (as `Company.Pin` is today), 6-digit-validated, or *required*
   for a Regular GST party? The CA's emphasis hints at mandatory — but a hard requirement blocks creating
   parties whose PIN isn't to hand. Should `Company.Pin` be tightened to match?
3. **Scope — party-only or all ledgers?** The CA said debtor/creditor; the corpus shows Address/State/Pincode on
   a **bank** ledger (`696054070:6151`). Gate on `IsUnderParty` (narrow, matches the ask) or offer on any ledger
   (matches the corpus more broadly)?
4. **Multi-address in or out?** Tally's F11 "Enable multiple Address" / Address Type feature (`703679456:
   808-830`) lets one party hold several branch addresses. **The project has already triaged this as a PHASE-2
   §3 enrichment (`verification-report:139`)**, so it is almost certainly a separate slice — but confirm the CA
   didn't mean it: "1 party, many branches" is a common CA complaint. **If it IS wanted, the data model must be
   a *collection* from day one and effort rises to L.**
5. **Sequencing vs WI-3** — ship create-only now, or hold until alteration exists so addresses are editable?

**For A14 (R7):**
6. **Confirm the exact party-ledger Mailing Details field list and order.** "Mailing Name" and "Country" on a
   *ledger* are inferred from the Company block by symmetry and are **not directly attested**. *(The verifier
   showed even the Company block's order varies by source — so this is genuinely warranted, not bureaucratic.)*
7. Does Tally auto-default a party ledger's Mailing Name from its Name (as it does for Company, `catalog:87`
   "Mailing Name (auto, editable)")?

**For the CA, if reachable:**
8. **Was the driver invoice printing** (the visible symptom — every invoice currently prints a blank recipient
   address), **e-invoice/e-Way readiness, or statutory recipient-details compliance?** The answer sets whether
   PIN validation is strict and whether this is urgent or cosmetic.

---

# WI-5 — Date handling: one canonical format, lenient input, F2 everywhere

**Covers point 4.** **State: PARTIAL.** **Effort: L.**

### Raw wording (verbatim fragments)

> "saved in a certain format" · "he may type the date in any order he wants but the date will be saved in that
> particular format only after his input" · "**F2 should decide the selection of date. In whatever window**" ·
> "all the app options"

### Decoded requirement

Three separable parts:

- **(4a) ONE canonical date format, app-wide.** Every date field renders in a single format, and every error
  message names that same format. Today **three** different contracts coexist.
- **(4b) Lenient input, canonical echo.** The user may type any reasonable shorthand (1/5/22, 1-5-2022,
  01.05.22); on commit the field **re-renders normalized**. "Saved in that format" = the value the user **sees**
  settles to the canonical form — a normalize-on-commit echo, **not a storage change** (storage is already
  canonical ISO). Corollary: unparseable input must be **rejected visibly**, not silently ignored.
- **(4c) F2 sets the working date EVERYWHERE** — "in whatever window" — voucher entry above all, not only
  reports.

"Harden" = remove the silent-wrong-date and silent-discard failure modes. That is the financial-correctness core.

### Tally fidelity target — ✅ GROUNDED (and the verifier found MORE than the decoder did)

- **F2 = Change date; Alt+F2 = Change period (distinct keys):** `docs/tally-feature-catalog.md:466` —
  "`Alt+F2` Change period · `F2` Change date"; `:404` — "period change (`Alt+F2`)";
  `tally/659947760-…:60` — "56. Alt+F2   Change Period".
- **F2 works on REPORTS:** `tally/664311548-Tally-Prime-Book.pdf:4045` — "Press `F2' for Change date > Enter";
  `:13075` — "first Press F2 and type last date".
- **F2 works on VOUCHER-ENTRY screens** — this is the grounding for 4c:
  `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:5749` — "Press F2 to set the Date of Attendance Entry like
  31.05.2022"; also `:5765`/`:5795`/`:5819` (Payroll Entry) and `:5846`/`:5880`/`:5913` (Payment Entry).
- **🔑 Verifier addition — F2 on PURCHASE/SALES voucher entry, which the decoder missed and which widens 4c well
  beyond payroll:** `tally/664311548-Tally-Prime-Book.pdf:6755`/`:6793`/`:6837`/`:6877` — "**Date - Type date of
  Purchase/Sale transactions by pressing F2, like I type `01/04/2020'**".
- **🔑 Verifier addition — lenient all-numeric input IS grounded after all, answering the decoder's own
  openQuestion.** The decoder marked "does Tally accept slash-separated all-numeric input?" as NOT FOUND, and was
  right to be sceptical about the payroll prose ("31.05.2022" is the author *naming* a date). But
  **`tally/664311548-Tally-Prime-Book.pdf:13078` is a field-entry instruction, not prose:** `Date (F2) - Type
  "31/01/2021"`. **"31" cannot be a month** — so this is genuine corpus evidence that **the first numeric field
  is the DAY** and that **slash-separated all-numeric input is accepted.** Corroborated at `:4727`/`:4728`
  ("I type 01/05/2020"), `:5399`, `:6240` ("Like I type \"01/01/2021\"").
- **Canonical display is `dd-MMM-yy`** (a month **abbreviation**, never an ambiguous all-numeric date — the
  strongest support for 4a): `696054070-…:5008` — "Period From: 1-Mar-23 To: 31-Mar-23"; `:5725` — "Effective
  From: 01-Apr-22"; `703679456-…:962` — "Mfg Date: 22-Aug-22". *(Only 4 such renderings corpus-wide — thin but
  consistent; no counter-example of an all-numeric Tally-rendered date was found.)*

**🟡 STILL UNVERIFIED (genuinely NOT FOUND — must be answered before 4b is specified, because the accepted-input
set IS the requirement):**
- Tally's **2-digit-year pivot rule** (does "23" mean 2023?). **A correctness trap in an accounting app** — a
  wrong pivot silently posts to the wrong FY.
- **Day-only / partial-date completion** — what does Tally do when only a day, or day+month, is typed?
- **Rejection UX** — does Tally block the field, beep, or revert?

### Current state

**STORAGE — already canonical and uniform ⇒ 4b needs NO schema work.** `src/Apex.Persistence.Sqlite/Schema.cs:7`
— "…`yyyy-MM-dd` TEXT (a `System.DateOnly`)". Confirmed across `:143`, `:266`, `:306-307`, `:324-325`,
`:344-345` (all "-- ISO yyyy-MM-dd"). Dates are `DateOnly` end-to-end; **there is no free-text date column to
migrate.**

**DISPLAY — `dd-MMM-yyyy` by convention only, enforced by nothing shared.**
`src/Apex.Desktop/ViewModels/AddComparisonColumnViewModel.cs:18` — "Dates use the app-wide `dd-MMM-yyyy` text
convention" (a comment asserting an app-wide rule **that no shared code enforces**); `:25`
`private const string DateFormat = "dd-MMM-yyyy";` — a **private** const, **re-declared per VM**.

**PARSING — fragmented. NO shared date helper exists.** Searched for `class *Date*(Helper|Util|Format|Parser|
Convention)`, `DateFormats`, `AppDateFormat` across `src/`, and for files named `*date*.cs` — **NOT FOUND**
(the only `*date*` file is a test, `tests/Apex.Desktop.Tests/GstDatedRateUiViewModelTests.cs`).

**~36 parse sites in `src/Apex.Desktop/ViewModels/` alone, with 4 competing format literals** — `"dd-MMM-yyyy"`
(×10), `"yyyy-MM-dd"` (×5), `"dd-MM-yyyy"` (×5), `"dd/MM/yyyy"` (×1). *(Of these, 21 are bare
`(DateOnly|DateTime).TryParse(`; the rest are `TryParseExact`.)*

**THREE different user-facing contracts, verified:**
- **strict `dd-MMM-yyyy`:** `CurrencyMasterViewModel.cs:167`, `ForexReportViewModel.cs:109`/`:180`,
  `AddComparisonColumnViewModel.cs:134`; error text at `AddComparisonColumnViewModel.cs:109` — "Unrecognized
  date. Use the dd-MMM-yyyy format (e.g. 01-Apr-2020)."
- **strict `dd-MM-yyyy`:** `GenerateEInvoiceViewModel.cs:379` `TryParseExact(…, "dd-MM-yyyy", …)`, seeded
  `:120`, error `:262` — "is not a valid acknowledgement date (dd-MM-yyyy)."
  **So on THIS screen the user must type `16-07-2026` while on Currency Master they must type `16-Jul-2026`.**
- **loose InvariantCulture:** `InventoryVoucherEntryViewModel.cs:117`, `BankReconciliationViewModel.cs:52`,
  `BillAllocationRowViewModel.cs:52`, `JobWorkOrderEntryViewModel.cs:124`,
  `ManufacturingJournalEntryViewModel.cs:150`, `MaterialMovementEntryViewModel.cs:124`,
  `EmployeeMasterViewModel.cs:290`, `BatchMasterViewModel.cs:266`, `GstConfigViewModel.cs:1378`.

**🔴 DEFECT 1 — dd/MM vs MM/dd SILENT MISREAD.** `InventoryVoucherEntryViewModel.cs:114` **renders**
`dd-MMM-yyyy` but `:117` parses via `DateOnly.TryParse(value, CultureInfo.InvariantCulture, …)`.
**InvariantCulture's short-date convention is MM/dd.** Silent, no error, wrong voucher date. Hides in testing
because it only bites when the day is ≤ 12.

**🔴 DEFECT 2 — SILENT DISCARD.** `InventoryVoucherEntryViewModel.cs:115-119` — the setter only assigns `Date`
**inside** `if (TryParse…)`. On unparseable input it no-ops.

**F2 — IMPLEMENTED ON REPORTS ONLY; A STUB EVERYWHERE ELSE (4c is the real gap):**
- **Reports (correct — leave alone):** `MainWindow.axaml.cs:482` `case Key.F2: vm.ReportSetAsOf();` gated by
  `vm.IsReportContext` (`:477`); Alt+F2 → `ReportSetPeriod()` (`:445`). Correctly mirrors Tally's F2/Alt+F2
  split. VM: `ReportsViewModel.cs:525` "F2 — sets the as-of date…", `:533` "Alt+F2 — sets the explicit period
  window".
- **Everywhere else:** `MainWindow.axaml.cs:523` `case Key.F2: Fire(vm, "F2");` → the **only** F2 button-bar
  registration, `MainWindowViewModel.cs:5242`:
  ```csharp
  ButtonBar.Add(new ButtonBarItem("F2", "Date", () => Message = StatusDate));
  ```
  It merely **prints a date to the status line**. `StatusDate` is set **once**, at `MainWindowViewModel.cs:796`
  — `StatusDate = company.FinancialYearStart.ToString("dd-MMM-yyyy");` — i.e. the **FY-start** date, not the
  working date, **and it is never updated.** Verified to be the sole `ButtonBarItem("F2"` registration in
  `src/`, so no screen re-binds it. **On a voucher-entry screen F2 does nothing useful** — precisely the case
  the corpus documents.

**UI surface:** `grep -c DatePicker src/Apex.Desktop/Views/MainWindow.axaml` = **0**. All date entry is TextBox +
string parsing (keyboard-first, consistent with Tally).

### ⚠️ Verifier corrections — three material, and one would have shipped a wrong test

1. **🔴 THE WORKED EXAMPLE IS WRONG IN BOTH HALVES — and it propagates into the proposed regression test.**
   The decoder wrote: *"an Indian-convention '05/06/2022' (6 June) parses as 5 May."* **Truth: dd/MM
   "05/06/2022" = 5 **June**; InvariantCulture MM/dd = 6 **May**. Swapped.** This propagated three times — the
   proposal and the touches list both specify a regression test *"pin 05/06/2022 → 6 June (NOT 5 May)"*, which
   **a correct dd-first parser would fail** (it yields 5 June). An implementer writing the test as dictated
   would see it fail and might "fix" the parser into a bug. **The DIRECTION of the defect is real and correctly
   identified.** **Use the corpus-grounded example instead:** `Book:6755` "I type `01/04/2020'" → intended
   **1-Apr-2020**; InvariantCulture reads **4-Jan-2020** — **wrong financial year, silently.**
2. **🔴 SCOPE UNDERSTATED ~2.3× — AND IT OMITS THE MOST IMPORTANT FIELD IN THE APP.** The decoder listed 9 loose
   sites. A fresh `grep -rnE "(DateOnly|DateTime)\.TryParse\(" src/Apex.Desktop/ViewModels/` returns **21**.
   **Missing from both the evidence and the touches list: `VoucherEntryViewModel.cs:443` — THE MAIN accounting
   voucher date (Payment/Receipt/Journal/Sales/Purchase)**, with the identical `get => dd-MMM-yyyy` /
   `set => DateOnly.TryParse(InvariantCulture)` shape ⇒ **identical MM/dd misread + silent discard.** The decode
   lists `VoucherEntryViewModel` **only** as an `ISetsWorkingDate` implementer for F2, never as a defect site —
   yet it is the highest-value date field in the app and **exactly the screen the corpus documents F2 driving**
   (Book:6755/6837). Also missing: `PosBillingViewModel.cs:190` (silent discard), `VoucherLineViewModel.cs:421`,
   `JobWorkComponentLineViewModel.cs:88`, `PriceListsViewModel.cs:143`, `SalaryStructureMasterViewModel.cs:476`,
   `TcsStatPaymentViewModel.cs:220`, `TdsStatPaymentViewModel.cs:218`. **So "silent discard at 3 sites" is really
   ≥ 5.** Effort **L still stands**, but the touches list as written **fixes 9 of 21 and leaves the main voucher
   screen broken.**
3. **🔴 THE SILENT-DISCARD MECHANISM IS WRONG — and the truth is WORSE.** The decoder said "the getter re-renders
   the OLD date; the user's typing vanishes with no message." Verified otherwise: `Date` is `[ObservableProperty]`
   and several VMs raise `OnDateChanged → OnPropertyChanged(nameof(DateText))`
   (`VoucherEntryViewModel.cs:544-546`, `MaterialMovementEntryViewModel.cs:191`,
   `JobWorkOrderEntryViewModel.cs:195`) — **but that fires only when `Date` CHANGES.** On unparseable input
   `Date` is never assigned, so **no notification fires** and the TwoWay binding (`MainWindow.axaml:1797`) never
   re-reads the getter. **The typed text STAYS ON SCREEN while `Date` silently retains the old value. Not
   "typing vanishes" — the screen shows the typed date and the voucher posts a different one.** Additionally
   **`InventoryVoucherEntryViewModel` has NO `OnDateChanged` at all**, so even a **successful** parse never
   echoes canonically — which independently **confirms 4b is a real gap** but refutes "only the getter/setter
   round-trip incidentally re-renders". **Consequence: the fix must explicitly notify; it cannot rely on the
   property-changed path.** *(Read-verified, not run-verified — no builds were permitted.)*
4. **Risk check cleared:** the decoder worried existing tests might pin the buggy behaviour. **Verified LOW** —
   the only numeric-date test assignments are `TdsStatPaymentViewModelTests.cs:221` "07-05-2026" (which hits that
   VM's strict `dd-MM-yyyy` ladder, not the loose fallback) and Io-layer PDF display models
   (`InvoicePdfTests`/`VoucherPdfTests` `DateText = "31-03-2025"`), a different, out-of-scope `DateText`.
5. **Minor:** the per-literal counts are lower than repo totals (95/15/7/1 in ViewModels) — evidently counted
   only at parse sites. The "4 competing literals" claim holds.

### Gap

1. **(4a)** No single canonical format — three user-facing contracts, ~36 parse sites, 4 literals, zero shared
   helper. A user who learns one screen is wrong on the next.
2. **(4b-input)** No lenient parsing. Strict sites reject everything but one exact spelling; loose sites accept
   shorthand but interpret it as **MM/dd — the opposite of Indian convention.** Neither is "type any order →
   canonical".
3. **(4b-echo)** No normalize-on-commit, and no path that reports a rejection.
4. **(4b-safety)** Two live silent failure modes: the dd/MM→MM/dd misread (**wrong date POSTED**) and the silent
   discard (**screen and stored value disagree**).
5. **(4c)** F2 does not set the working date on any non-report screen — it is a stub printing the FY-start.
   **The largest single gap, and the one the corpus most directly contradicts.**

**NOT a gap — do not spend a slice on it:** storage. Already uniform ISO `yyyy-MM-dd` `DateOnly`.

### Proposal

**Do NOT hand-edit ~36 call sites. Introduce a contract, then converge on it.**

1. **STEP 1 — canonical date contract.** New `src/Apex.Desktop/ApexDate.cs` (static; no new project):
   - `const string Canonical = "dd-MMM-yyyy";`
   - `static string Format(DateOnly d)` — the ONE renderer.
   - `static bool TryParse(string? text, DateOnly context, out DateOnly date)` — the ONE lenient parser. An
     ordered `TryParseExact` over an explicit **dd-first ladder** ("dd-MMM-yyyy", "d-MMM-yy", "dd-MM-yyyy",
     "d-M-yy", "dd/MM/yyyy", "d/M/yy", "dd.MM.yyyy", "yyyy-MM-dd", "ddMMyyyy", "ddMMyy"), separators normalized
     to '-' first. **CRITICAL: never fall through to bare `DateOnly.TryParse(…, InvariantCulture, …)` — that is
     the MM/dd misread.** `context` supplies the year/month when omitted, and the 2-digit-year pivot.
   - `static string ErrorFor(string input)` — the ONE error message.
   Unit-test it directly (`tests/Apex.Desktop.Tests/ApexDateTests.cs`) with **the corrected, corpus-grounded
   regression case: "01/04/2020" → 1-Apr-2020, NOT 4-Jan-2020** (see correction 1 — do **not** use the
   decoder's 05/06/2022 example).
2. **STEP 2 — converge the call sites.** Replace all ~21 `TryParse` + the `TryParseExact` sites in
   `src/Apex.Desktop/ViewModels/` — **including `VoucherEntryViewModel.cs:443` and `PosBillingViewModel.cs:190`
   and the other 10 the decode omitted** (correction 2). Delete the per-VM privates
   (`AddComparisonColumnViewModel.cs:25`, `AttendanceVoucherEntryViewModel.cs:256-263`,
   `PayrollVoucherEntryViewModel.cs:341-343`, `GenerateEInvoiceViewModel.cs:378-380`,
   `BudgetMasterViewModel.cs:237`, `GstRateSetupViewModel.cs:435-436`). **EXCLUDE the Io layer** (see risk 1).
   Fix the silent-discard setters to **surface a rejection**, and note they **must explicitly notify** rather
   than rely on the property-changed path (correction 3) — this needs a per-VM error/Message field, so budget
   for it.
3. **STEP 3 — F2 everywhere (the 4c gap; the highest-value part).**
   - **Leave the report path untouched** (`MainWindow.axaml.cs:477-488` already matches Tally).
   - Add an `ISetsWorkingDate` interface (`DateOnly WorkingDate { get; set; }`) implemented by the voucher-entry
     VMs (`VoucherEntryViewModel`, `InventoryVoucherEntryViewModel`, `AttendanceVoucherEntryViewModel`,
     `PayrollVoucherEntryViewModel`, `ManufacturingJournalEntryViewModel`, `MaterialMovementEntryViewModel`,
     `JobWorkOrderEntryViewModel`).
   - In `MainWindow.axaml.cs`, **before** the `Fire(vm, "F2")` fallthrough at `:523`, route F2 to the active VM
     when it implements the interface. Mirror how `ReportSetAsOf` uses the config panel rather than a modal
     (`MainWindowViewModel.cs:2069-2071`) so the keyboard-first, DatePicker-free design holds.
   - Fix `MainWindowViewModel.cs:5242` — the F2 button-bar item must **reflect and drive the WORKING date**, not
     print `StatusDate` (the never-updated FY-start from `:796`).
   - **Test by DRIVING the key handler**, not by asserting a VM method exists.

**SCHEMA: NONE.** Storage is already ISO `DateOnly` (`Schema.cs:7`) ⇒ no v45, no CreateV1/Migrate pair, no
`SchemaMigrationEquivalenceTests` change, no Io fold-in. **If STEP 3 later adds a persisted "last working date"
preference, that WOULD trigger the full v45 chain — keep it in memory-only state.**

### Risk

- **🔴 Sweeping the STATUTORY wire formats — the worst risk.** A naive "one date format everywhere" sweep would
  **corrupt GST/e-invoice/e-way output.** `src/Apex.Ledger.Io/Gstr2bJsonParser.cs:268-273` — "Parses a portal
  \"dd-MM-yyyy\" (or ISO \"yyyy-MM-dd\") date" — **that dd-MM-yyyy is the GSTN PORTAL's format, dictated
  externally, NOT a UI inconsistency.** The Io layer (`CanonicalMapper.cs`, `CsvCanonicalBridge.cs`,
  `CertificatePdfSupport.cs`, `CompanyImportService.cs`, `CanonicalValidation.cs`) must be **OUT OF SCOPE**.
  **The genuinely subtle case:** `GenerateEInvoiceViewModel.cs:379` is `dd-MM-yyyy` but is a **UI INPUT field**
  (an Ack Date the user types, `:120`/`:261`) — **that one IS in scope; its Io-side counterpart is not.**
  Getting this line wrong in either direction is the main way this slice breaks something.
- **🟠 dd-first ordering silently CHANGES the meaning of existing input.** "05/06/2022" means 5 May today and
  5 June after. That is the intended fix, but any existing test asserting today's (wrong) behaviour will flip —
  **such a test is asserting a bug and must be corrected, not accommodated.** *(Verified LOW — no such test
  found; see correction 4.)*
- **🟠 Tightening loose→canonical could REJECT input that currently "works"** (e.g. "2022-05-06"). **Keep
  "yyyy-MM-dd" in the ladder** — unambiguous, cannot collide with dd-first.
- **🟠 2-digit-year pivot is a correctness trap** ("23" → 2023 or 1923?). 🟡 UNVERIFIED against Tally. A wrong
  pivot silently posts to the wrong FY. **Prefer requiring 4-digit years, or pivot off the company's
  `FinancialYearStart`** rather than a fixed century window.
- **🟠 F2 routing touches the global key handler**, which already has a delicate precedence chain (report F2 at
  `:482` must still win over the button-bar `:523`; the Alt+F2 report/inventory ordering at `:437-450` is
  explicitly commented as collision-sensitive). A careless insert re-breaks report F2.
- **🟡 Blast radius:** ~20 VMs / ~36 sites. Interacts with the UI-defect campaign — sequence after it.
- **🟢 No schema risk** — v44 untouched.

### Open questions

**For the user:**
1. **Confirm the canonical format.** Recommend **`dd-MMM-yyyy`** (01-Apr-2026) — already dominant and
   unambiguous. **Note the Tally corpus actually shows 2-digit `dd-MMM-yy`** (01-Apr-22); adopting that would be
   closer to Tally but reintroduces year ambiguity. **Which wins — fidelity or safety?**
2. **Confirm "F2 in whatever window" = F2 sets the working/voucher date** (Tally behaviour), **NOT** "F2 opens a
   calendar picker". The app has **zero DatePicker controls by design**; a picker would be a deliberate reversal
   of the keyboard-first design.
3. **Confirm "saved in a certain format" means the on-screen value normalizes after input**, not a DB storage
   change (storage is already uniform ISO — a literal storage reading makes the point a **no-op**).
4. **Should the e-invoice Ack Date field** (`GenerateEInvoiceViewModel.cs:120`, currently dd-MM-yyyy) **be
   unified to the canonical UI format?** Recommend **yes** (it is a typed UI field), while its **Io wire format
   stays dd-MM-yyyy**. Confirm the CA is not relying on that field mirroring the portal.

**For A14 / R7 — must be answered before 4b is specified, because the accepted-input set IS the requirement:**
5. What date input forms does Tally **actually** accept? *(Partly answered by the verifier: slash-separated
   all-numeric, day-first, IS grounded — Book:13078. Remaining: dots? bare "15"? "150522"?)*
6. What does Tally do when only a day, or day+month, is typed — does it complete from the current period?
7. **Tally's 2-digit-year pivot rule?**
8. On rejection, does Tally block the field, beep, or revert? (Sets the STEP-2 rejection UX.)

**For the CA (via the user):**
9. **Was the CA reporting an OBSERVED wrong date** (i.e. did they hit the dd/MM→MM/dd misread live), **or a
   usability complaint?** **If observed, priority rises sharply** — it means wrong dates may already be posted in
   their test books, and **existing data may need auditing for day ≤ 12 dates.**

---

# WI-6 — Make the salary-TDS pay-head option reachable

**Covers point 6.** **State: PARTIAL — the engine is complete and law-current; the option is unreachable.**
**Effort: S.**

### Raw wording (verbatim fragment)

> "TDS option in salary should also be included as per income tax law"

### Decoded requirement

The Payroll module must expose a **salary-TDS (Income Tax) deduction option a user can actually reach and
configure**. Concretely: on the **Pay Head master**, when creating an `Employees' Statutory Deductions` pay head,
the user must be able to mark it as the **Income Tax / TDS-on-salary** head
(`IncomeTaxComponent.TaxDeductedAtSource`).

**The CA's complaint is accurate but its cause is not what the words suggest.** The §192 engine, regime
election, Form 12BB declaration, Form 24Q and Form 16 are **all fully built and correct**. The single missing
link is **one entry in one UI dropdown**, which makes the entire feature unreachable from the running app. The
CA looked for a "TDS option in salary", found none, and concluded it was absent. **It is present in the engine
and orphaned from the UI.**

### 🟢 ALREADY BUILT — do NOT rebuild this

**The §192 statutory calculator is complete.** `src/Apex.Ledger/Services/SalaryIncomeTax.cs` (284 lines, pure and
deterministic). Its own docstring (`:6-8`): "**§192 salary-TDS income-tax engine** (Phase 8 slice 7; RQ-12;
Finance Act 2025 / §115BAC(1A) / §87A; **A14-verified FY 2025-26 / AY 2026-27**)". It implements:

- New-regime slabs (`:91-97`) 0/5/10/15/20/25/30% at 4L/8L/12L/16L/20L/24L; old regime (`:101-111`) with the
  **age-banded** first nil band (2.5L / 3L senior / 5L super-senior) via `AgeBand` `:44-52`, `AgeBandFor()` `:64-70`.
- Standard deduction `:26-27` (₹75,000 new / ₹50,000 old); `StandardDeduction()` `:73-74`.
- **§87A with the correct asymmetry** — `Rebate87A()` `:138-150`: new regime = **marginal-relief band**
  (`max(0, slabTax − (taxable − 12L))`, cap ₹60,000, `:142-145`); old regime = **hard cliff** at ₹5L / ₹12,500
  (`:149`).
- **Surcharge + marginal relief** — `SurchargeBands()` `:155-158` (new capped at 25%, **no 37% band**; old adds
  37% above ₹5cr), `Surcharge()` `:175-203`.
- **4% cess applied last** — `CessRate` `:38`, in `ComputeAnnual()` `:218-228` (order: slab → rebate → surcharge
  → cess → nearest rupee).
- **§206AA no-PAN 20% floor** — `NoPanFloorRate` `:41`, `AnnualTaxNoPan()` `:234-239`.
- **Average-rate spread + true-up** — `MonthlyTds()` `:248-255`, `MonthsRemainingInFy()` `:259-264`.
- Itemised output for the return/certificate — `SalaryTaxComputation` `:272-284`.

**Payroll integration is complete.** `PayrollComputationService.cs:511-541` `EvaluateIncomeTax`, gated `:513`
`if (payHead.IncomeTaxComponent != IncomeTaxComponent.TaxDeductedAtSource) return Money.Zero;` and `:514`
`if (!_company.SalaryTdsEnabled) return Money.Zero;`. **The annual estimate is properly telescoped** (`:522-528`:
paid-to-date + monthly × months-remaining + additional income) — the comment at `:522-527` records that
annualising this-month×12 was a **real bug** (a mid-year raise/bonus/joiner diverged from Annexure II) and that
telescoping collapses in March to the actual FY gross "so the year trues up to Annexure II **by construction**".
**That is the correct §392(1)/§192(1) estimate mechanic.**

**Declaration, toggle, reports, screens — all present:**
- `src/Apex.Ledger/Domain/TaxDeclaration.cs:60-70` `AllowedDeductions(TaxRegime)` — new regime returns **only**
  `Section80CCD2Employer` (`:62-63`, statutorily correct); old regime sums capped 80C/80D/80CCD(1B)/80CCD(2)/
  HRA/24(b).
- `Company.SalaryTdsEnabled` (`Company.cs:305`); `PayrollService.cs:198-202` `EnableSalaryTds()` /`:205`
  `DisableSalaryTds()`. Its docstring `:190-197` notes it **reuses the Phase-7 `TdsConfig`** deductor/TAN — "no
  parallel deductor config".
- **F11 UI exists:** `MainWindow.axaml:8637-8641` "Enable Salary TDS for this establishment";
  `GstConfigViewModel.cs:278` `ShowSalaryTdsConfig => PayrollStatutoryEnabled`, `:1055`
  `if (SalaryTdsEnabled) service.EnableSalaryTds();`. **So "the TDS option in salary" is already a reachable,
  working toggle** — see Q1 on discoverability.
- **Reports:** `Reports/Form24Q.cs` (Annexure I every quarter + **Annexure II Q4-only driving Form 16 Part B**,
  `:96-97`; section codes 92B/92A/92C `:126-132`; control totals `:71-91`), `Reports/Form16.cs`,
  `PayrollRegister.cs:76` (TDS column). **Critically `Form24Q.cs:98-99`: it reads withheld tax off each posted
  line and never recomputes**, so the return reconciles to the payable credit by construction.
- **Screens:** `Screen.TaxDeclarationMaster`/`Form24Q`/`Form16` (`MainWindowViewModel.cs:135-137`), opened
  `:3677`/`:3695`/`:3720`, all gated on `SalaryTdsEnabled`; `Form16ViewModel.cs:55` and `Form24QViewModel.cs:75`
  gate too (ER-13).
- **Io round-trips it:** `CanonicalModel.cs:90`, `CanonicalMapper.cs:82`, `ApplyJournal.cs:328`/`:358`.
- **Employee regime is on the employee master:** `Employee.cs:92` `ApplicableTaxRegime { get; set; } =
  TaxRegime.New;`, set from the UI at `EmployeeMasterViewModel.cs:211`, pickable `:143-145`.

### Tally fidelity target — ✅ GROUNDED that the option exists; 🟡 UNVERIFIED on the exact label

Tally creates salary TDS as a **pay head**, not a special module. Two distinct pay-head income-tax surfaces:

- **(a) Earnings heads carry an income-tax classification.** `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf` p.198
  (Basic Pay), verbatim: "Set /Alter Income Tax Details: Yes / Income tax component: Basic Salary / **Tax
  Calculation Basis: On Projected Value** / **Deduct TDS Across Period: Yes**".
  `tally/664311548-Tally-Prime-Book.pdf` p.332 confirms the sub-window and enumerates "On Projected Value" vs
  "On Actual Value".
- **(b) Deduction heads carry a Statutory pay type.** Study Guide p.205 (Professional Tax): "Pay head Type:
  Employees' Statutory Deductions / Statutory pay type: Professional Tax / Under: Duties& Taxes". Same shape for
  PF and ESI (p.205-206). Book p.348: "Statutory pay type – In this section different type of Employee Statutory
  Deduction are listed."
- Catalogue: `docs/tally-feature-catalog.md:369` — "Employee Deductions (Professional Tax slab, **Income Tax**,
  PF/ESI/NPS employee)"; `:379` — statutory reports include "**Income Tax computation**"; `:361` — F11 payroll
  statutory details include "**Income Tax** (TAN, circle/ward, responsible person)".

**🟡 UNVERIFIED:** that Tally's Statutory-pay-type dropdown **literally contains an "Income Tax" entry**. The
corpus enumerates PF/EPS/ESI/PT **instances** but never prints the full dropdown. **This affects only the label
wording, not the design** — the app's per-statute-component architecture (`PfComponent`/`EsiComponent`/
`PtComponent`/`IncomeTaxComponent` on `PayHead.cs:71`/`:85`/`:106`/`:109`) deliberately differs from Tally's
single dropdown, is already established across three shipped statutes, and **should not be restructured for
this point.**

### The gap — one line of code

`src/Apex.Desktop/ViewModels/PayHeadMasterViewModel.cs:230-239` populates the income-tax-component picker with
exactly **ten** options, ending at `IncomeTaxComponent.FullyExempt` (`:239`). **`IncomeTaxComponent.
TaxDeductedAtSource` is absent.**

- `src/Apex.Ledger/Domain/IncomeTaxComponent.cs:50` — `TaxDeductedAtSource = 10`, documented `:41-50` as "the
  marker that a `PayHeadType.EmployeesStatutoryDeductions` pay head **is** the salary-TDS withholding head".
- **Exhaustive grep:** `grep -rn "TaxDeductedAtSource" src/` returns **10 hits, every one in
  `src/Apex.Ledger`**. **Zero hits anywhere in `src/Apex.Desktop`** (the only Desktop match is the compiled
  `Apex.Ledger.dll`).
- The **only writer** is `PayHeadMasterViewModel.cs:480` `incomeTaxComponent: (SelectedIncomeTaxComponent ??
  IncomeTaxComponents.First()).Value` — a value drawn **solely** from that ten-item list. **So
  `PayrollComputationService.cs:513` can never be satisfied by a UI-created pay head, and salary TDS is always
  ₹0 in the shipped app.** Form 24Q, Form 16 and the Payroll Register's income-tax column consequently render
  **zeros for every real company.**
- **No seed backdoor:** `src/Apex.Ledger/Seed/` contains no pay-head seed.
- **No alter backdoor:** `PayHeadService.cs` exposes only `CreatePayHead:37`, `RenamePayHead:91`,
  `SetComputation:105`, `DeletePayHead:116` — **nothing can change `IncomeTaxComponent` after creation.**
- **Not a backdoor:** `src/Apex.Desktop/ViewModels/SalaryTdsOptions.cs` is a **section-code** picker
  (92B private / 92A government / 92C union) feeding the F11 block and the Form 24Q/16 screens. It is **not**
  the pay-head component picker.

**Why 2481 tests stayed green:** `tests/Apex.Desktop.Tests/SalaryTdsUiViewModelTests.cs:77-79` builds the head
through the **engine service**, bypassing the picker:
```csharp
var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
    PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
    incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
```
The project's known trap: **the VM tests assert on state the VM itself cannot produce.**

### ⚠️ Verifier correction — the decoder's "HIGH" risk is FALSE; the guard already exists

The decoder proposed a "Fix 2" (add type/tag validation to `CreatePayHead`) and rated the absence of it HIGH,
claiming: *"`PayrollComputationService.cs:216` returns `PayHeadPostingRole.Deduction` for the TDS tag regardless
of the head's type, so a mis-tagged Earnings head would silently invert into a deduction… Fix 2 is not optional
garnish — it is the guard."*

**This is wrong.** `CreatePayHead:84` already calls `ValidatePayHead` → `ValidateStatutoryComponentRole`
(`PayHeadService.cs:179-189`):
```csharp
if (PayrollComputationService.RequiredStatutoryRole(payHead.PtComponent, payHead.PfComponent,
        payHead.EsiComponent, payHead.IncomeTaxComponent) is not { } required) return;
var actual = PayrollComputationService.RoleOf(payHead.Type);
if (actual != required) throw new InvalidOperationException(
    $"Pay head '{payHead.Name}' carries a statutory component that must post as {required}, " +
    $"but its pay-head type '{payHead.Type}' posts as {actual}.");
```
`RequiredStatutoryRole` returns `Deduction` for the §192 tag; `RoleOf(PayHeadType.Earnings)` = `Earning`;
mismatch **throws**. A mis-tagged Earnings head is **rejected loudly at the master-save boundary, not silently
inverted.** The Io direct-construction import path mirrors the same check (`CompanyImportService.cs:505`).

**Root of the misreading:** the decoder treated `RequiredStatutoryRole` (`PCS:212-217`) as the posting-role
**resolver**. It is a validation **oracle** (its own doc comment at `PCS:210` points at `RoleOf`). The actual
posting role is assigned from `RoleOf(payHead.Type)` at **`PCS:77`**.

**Residual truth worth keeping — downgrade to LOW, not HIGH:**
- The existing guard only requires `role == Deduction`. `RoleOf` maps **`Deductions` and `LoansAndAdvances`** to
  `Deduction` as well, so the §192 tag **would** be accepted on those two types, not strictly
  `EmployeesStatutoryDeductions`. A narrower check is defensible as a nicety.
- **No test covers the INCOME-TAX case of this guard** — the only guard test found is for Professional Tax
  (`tests/Apex.Ledger.Io.Tests/CanonicalPtRoundTripTests.cs:133`). **Adding a test that tagging Earnings with
  `TaxDeductedAtSource` throws is cheap and worthwhile.**

**Also:** the decoder's grep breakdown is off by one (it says `PayrollComputationService.cs ×5`; there are **six**
hits — 216, 298, 513, 612, 929, 938 — so the per-file tally sums to 9, not 10). **Conclusion unaffected.**

**XAML drift note:** the decode read **HEAD** (14,785 lines), where `:7408` **is**
`ItemsSource="{Binding IncomeTaxComponents}"` and `:8638` **is** `Content="Enable Salary TDS for this
establishment"` — **the citations are correct.** In the working tree they sit at `:7430` and `:8660` (+22).

### Two secondary Tally gaps (both NOT IMPLEMENTED, both optional)

- **`Tax Calculation Basis` (On Projected Value / On Actual Value) and `Deduct TDS Across Period`** — grounded
  (Study Guide p.198 + Book p.332). Searched `TaxCalculationBasis|ProjectedValue|OnActualValue|DeductTdsAcross`
  across `src/` → **zero hits.** The engine hardcodes one estimation strategy
  (`PayrollComputationService.cs:522-528`).
- **Income Tax Computation report** (`catalog:379`). Searched `IncomeTaxComputation|"Income Tax Computation"` →
  **zero hits.** `SalaryTaxComputation` (`SalaryIncomeTax.cs:272-284`) **already exposes every field such a
  report needs.**

### Proposal

**Fix 1 — the core. S, no schema change.** In `PayHeadMasterViewModel.cs`, after `:239`:
```csharp
IncomeTaxComponents.Add(new IncomeTaxComponentOption {
    Value = IncomeTaxComponent.TaxDeductedAtSource,
    Display = "Income Tax (TDS on Salary — Sec 192)" });   // see WI-13 re §392
```
Gate it to a statutory-deduction head on an enrolled establishment — rebuild the list when `SelectedType` changes,
admitting `TaxDeductedAtSource` **only** when `SelectedType.Value == PayHeadType.EmployeesStatutoryDeductions &&
_company.SalaryTdsEnabled`. Mirror the established pattern (`EmployeeMasterViewModel.cs:131` `ShowPfDetails =>
_company.PayrollStatutoryEnabled`) and the existing refresh idiom (`PayHeadMasterViewModel.cs:259-261`
`RefreshBasisOptions()`/`RefreshAttendanceOptions()`). The VM already takes `Company` in its ctor, so **no new
plumbing**. **No XAML change** — the ComboBox at `MainWindow.axaml:7408` already binds `IncomeTaxComponents`.

**Fix 2 — narrow the guard (optional, LOW). S.** Per the verifier correction, the guard exists; optionally
tighten `ValidateStatutoryComponentRole` from "role must be Deduction" to "type must be
`EmployeesStatutoryDeductions`" for the §192 tag. **And add the missing income-tax guard test.**

**Fix 3 — the test that would have caught this. S.** Add to `SalaryTdsUiViewModelTests.cs` a test that drives the
**ViewModel picker, never the service**: construct `PayHeadMasterViewModel`, set `SelectedType` = Employees'
Statutory Deductions, assert `IncomeTaxComponents` **contains** `TaxDeductedAtSource`, select it, `Save()`, then
assert the persisted `PayHead.IncomeTaxComponent == TaxDeductedAtSource` **and that a payroll run yields non-zero
TDS**. Also assert the **negative gate** (option absent when `SelectedType` is Earnings). The existing test at
`:77-79` must stay but is **not sufficient — it proves the engine, not reachability.**

**Fix 4 — optional, M, SCHEMA v44→v45.** Add `TaxCalculationBasis` (OnProjectedValue/OnActualValue) and
`DeductTdsAcrossPeriod` to `PayHead.cs` (alongside `IncomeTaxComponent:109`). **Requires the full drill:** new
columns in **both** `CreateV1` **and** a new `MigrateV44ToV45`, `SchemaMigrationEquivalenceTests` parity,
`SqliteCompanyStore` read/write, **and** an `Apex.Ledger.Io` fold-in (`CanonicalModel`/`CanonicalMapper`/
`ApplyJournal`/`CanonicalXml`). Defaults must keep a pre-v45 pay head byte-identical (ER-13).
**Recommend deferring — ask the user first; it is not needed to make point 6 work.**

**Fix 5 — optional, M, no schema.** Add an **Income Tax Computation** report:
`src/Apex.Ledger/Reports/IncomeTaxComputation.cs` + `Screen.IncomeTaxComputation` + a `Cmp08ReportViewModel`-
template VM, under Reports → Statutory Reports → Payroll beside Form24Q/Form16, gated on `SalaryTdsEnabled`.
`SalaryTaxComputation` (`SalaryIncomeTax.cs:272-284`) already carries every line item. Follow the UI conventions:
the `#Root` binding path (not the `GatewayColumn` `x:DataType` fallback — AVLN2000), the 9-step tree wiring
**with the gate at the ROOT too**, and headless-Skia render-verify then revert `TestAppBuilder`.

**Sequencing:** Fixes 1+2+3 together are **one small slice** that makes the CA's complaint go away and lights up
Form 24Q / Form 16 / the Payroll Register with real numbers. Fixes 4 and 5 are separate and user-gated.

**De-brand (hard rule):** the picker label and any report title must never contain "Tally".

### Risk

- **🟠 Two §192 heads on one structure.** Nothing prevents tagging two pay heads `TaxDeductedAtSource`.
  `PayrollComputationService.cs:927-938` **sums** all such lines ⇒ the employee is deducted **twice**. `:591-612`
  and `Form24Q.cs:265`/`:311` likewise iterate all tagged heads. **Decide and enforce: at most one §192 head per
  salary structure.** Regression-lock it.
- **🟠 No re-tag path** — `PayHeadService` has only Rename/SetComputation/Delete, so an existing untagged "Income
  Tax" head **cannot be fixed**; the user must delete and recreate, which may be blocked if a posted voucher
  references it. **Depends on WI-3.** Ship a note or the alter path with it, or users hit a second wall
  immediately.
- **🟠 Going live changes existing books.** Today every UI company computes ₹0 salary TDS. The moment a §192 head
  joins a structure, payroll vouchers post a new credit line and **net pay drops**. `PCS:528` telescopes
  paid-to-date + projected, so a head added **mid-year front-loads the whole year's residual tax into the
  remaining months** — correct per §192/§392, but it **will look alarming**. Verify against
  `tests/Apex.Ledger.Tests/Phase8GoldenPayrollTests.cs` and the worked example at
  `docs/phase8-payroll-requirements.md:521-543` (taxable ₹16,33,800 → TDS ₹10,986/mo). **Those goldens build the
  head via the service, so Fix 1 must not perturb them.**
- **🟢 Fixes 1-3 need NO schema change.** `IncomeTaxComponent.cs:47-48` states ordinal 10 already persists in the
  existing `income_tax_component` column and round-trips by name in Io. **This is why the core fix is S.**
- **🟡 UI-defect campaign collision** (`MainWindow.axaml:7408` is in the shared file). Sequence after.
- **🟡 De-branding** — new labels must never contain "Tally".

### Open questions

1. **🔴 (highest value — for the user / CA) What exact nav path did the CA take, and did they see a *wrong TDS
   number* or *no TDS option at all*?** The evidence says the option is **unselectable**, so "no option at all"
   — but if they saw a wrong figure, the target moves from the picker to the engine and this decode is wrong.
   **One sentence from the CA settles it.**
2. **(Discoverability — possibly the real answer)** The salary-TDS toggle is fully working but lives inside
   **`GstConfigViewModel`** (`:278-288`, `:1033-1048`) — a **GST-named screen** — and is hidden unless
   `PayrollStatutoryEnabled`. **A CA hunting for a salary-TDS option would plausibly never find it and conclude
   it is absent.** If so, the fix is navigation/labelling, not engine work. Confirm before scoping.
3. **(User decision) Does point 6 stop at "make the option reachable"** (S, no schema, ships this slice), **or
   extend to `Tax Calculation Basis` / `Deduct TDS Across Period`** (M, **schema v45**) **and the Income Tax
   Computation report** (M, no schema)? All three are genuinely absent and corpus/catalogue-grounded, but none is
   needed to make salary TDS work. **Recommend: fix the picker now; defer 4 and 5 to an explicit go.**
4. **(Design) Enforce at most one §192 head per salary structure?** Recommend yes, validated at create.
5. **(A14 / R7) Does Tally's `Statutory pay type` dropdown literally contain an "Income Tax" entry?**
   🟡 UNVERIFIED. Affects the label only — **do not restructure the per-component architecture for it.**
6. **(Sequencing) WI-3 blocks re-tagging an existing pay head.** Ship WI-6 before, with, or after WI-3?
   **Recommend: with** — otherwise WI-6 only helps users who have not yet created their Income Tax head.
7. **(Scope boundary with point 9)** Confirm points 6 and 9 are not the same wall, to avoid two slices fixing one
   dropdown. See **WI-8**, which explains why point 9's employee-ledger reading is a **different and dangerous**
   thing.
8. **🔴 See WI-13.** The label proposed above says "Sec 192". **As of 1 April 2026 the governing provision is
   §392 of the Income-tax Act, 2025.** Decide the naming before shipping a new user-visible label.

---

# WI-7 — Accounting Group master (Create + Alter)

**Covers point 9 (first half).** **State: NOT IMPLEMENTED.** **Effort: M.**

### Raw wording (verbatim fragments)

> "group of salary … in the balance sheet" · "head payable in the balance sheet" · "creation of ledger of an
> employee"

### Decoded requirement

Ship a real **accounting-Group creation screen**. The user must be able to create a custom accounting Group —
e.g. "Salary Payable" — with an **Under** parent of **Current Liabilities** (a Balance-Sheet liability head), and
then create a ledger per employee under it. Today `Create → Group` exists as a menu item but is **mis-wired to
open Ledger Creation**, so the ledger master's Under-picker only ever offers the 28 seeded groups.

### Tally fidelity target — ✅ GROUNDED

- **Group creation:** `tally/664311548-Tally-Prime-Book.pdf` p.14 — "*How to create Group in Tally Prime?*
  Step.1: GOT (Gateway of Tally) > Create > Group … Name: Type Name of Group … **Under: Select Under from 28
  lists of groups** … After filling all details Press **Ctrl+A** to save". **Its own practice table lists
  "Bill Payable → Current liabilities"** — the exact shape point 9 asks for.
- **Group alteration:** Book p.15 — "*How to Alter (Changes) in Group in Tally Prime?* GOT > Alter > Group >
  Select `Group' for Alteration (Change). Step.2: After Changes … Press `Ctrl+A' for Accept changes".
- **Custom groups are permitted:** `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:2073` — "Groups in Tally Prime
  have already set 28 pre-defined Groups. **However, you can create groups as required**"; `:2085` — "One can
  also create groups **under the Primary group category**, if required"; `:2130` notes predefined groups cannot
  be deleted.
- **Catalogue:** `docs/tally-feature-catalog.md:106-115` — "28 predefined groups = 15 Primary + 13 Sub-groups …
  **Custom groups nest under any of these (or a custom parent)**"; `:116` — "**Fields:** Name, Alias, Under
  (parent). Predefined groups cannot be deleted."

**⚠️ Verifier correction — one fidelity citation was mis-attributed, and the miss matters.** The decoder cited
Study Guide `l.2124-2125` ("Go to Gateway of Tally → Alter → Group → Enter; Or, Go To (Alt+G) → Alter Master →
Group → Enter") as independent cross-confirmation of group **alteration**. The lines are verbatim accurate **but
sit under the heading "Group Deletion"** (`l.2120-2126`: "You can also delete any group … Press Alt+D"). **The
Alter claim survives fully on the primary citation (Book p.15, verified verbatim)** — but the sourcing was
sloppy. **Worse, the decoder missed the guide's genuine alteration section:** `l.2111-2117` **"Multiple Group
Alteration"** — "Go To (Alt+G) → Chart of Accounts → Groups → Enter → Press **Alt+H** for Multi-Masters →
**Multi Alter**", and `l.2100-2105` "Multi Group Creation". **This matters for the design: Tally's real
alter entry point for groups is the CHART OF ACCOUNTS + Alt+H, and our `ChartOfAccountsViewModel.cs:43` is
explicitly "A read-only Chart-of-Accounts tree". The decoder's proposal routes Alter through the new Group
screen and never mentions the CoA.** This also bears on **WI-3** and strengthens point 5's CoA framing.

### Is the CA's accounting sound? — reasoned, NOT cited law

Accrued-but-unpaid salary is an **employee benefit payable**: a present obligation settled within 12 months ⇒ a
**current liability**. Grouping it under Current Liabilities is standard, and a **"Salary Payable" group with one
ledger per employee** is a legitimate sub-ledger pattern — it gives a per-employee payable balance readable
straight off the Balance Sheet, which is exactly what a CA reconciling unpaid wages wants. **Caveat:** if salary
is *provisioned* at period-end rather than merely accrued, Tally's convention would put it under **Provisions**
(which we already seed — `SeedGroups.cs:53`). "Under the head payable" most naturally means **Current
Liabilities**. **⚠️ This is professional-consensus reasoning, not a cited accounting standard — treat it as
such.**

### 🟢 What Tally actually does by default — and the app is ALREADY FAITHFUL

- **Tally ships NO "Salary" group.** 28 predefined groups, none of them "Salary": Study Guide `~1964-65` —
  "Tally Prime provides **28 pre-defined groups** … **15** are Primary Groups and **13** are sub-groups";
  Book `~539` says the same. **The app matches exactly:** `src/Apex.Ledger/Seed/SeedGroups.cs` — `Count = 28`
  (`:64`), 15 primary (`:29-45`) + 13 sub-groups (`:48-60`), docstring `:6-8` citing "verification §A6/A7".
  Relevant seeded sub-groups: **Duties & Taxes** (`:52`), **Provisions** (`:53`). **This is not a defect.**
- **Tally's taught payroll pattern is a LEDGER, not a group.** Book `~13145-13152`, "**Process 1: Create Payable
  (Dues) Pay Heads**": *Name* → "**Salary Payable**"; *Pay Head Type* → "**Not Applicable**"; *Under* →
  "**Current Liabilities**". Study Guide `~5779`/`~5859` — "Payroll Ledger: **Salary Payable**", with Tally
  "automatically captur[ing] the Net Amount of Salary payable for **each Employee** as well the Total" (`~5864`)
  — **i.e. per-employee visibility comes from the Employee dimension, NOT from one ledger per employee.**
- **🟢 THE APP ALREADY REPLICATES THIS EXACTLY.** `PayrollVoucherService.cs:31`
  `SalaryPayableLedgerName = "Salary Payable"` → created under `CurrentLiabilitiesGroupName` (`:48`) at **`:355`**
  (`EnsureLedger(SalaryPayableLedgerName, () => GroupIdByName(CurrentLiabilitiesGroupName), openingIsDebit:
  false, scope)`). And `PayHeadType.NotApplicable = 9` (`PayHeadType.cs:39-40`, "a payable/round-off head that
  neither earns nor deducts by default") is the precise counterpart of Tally's "Not Applicable" payable pay head.
  **Fidelity confirmed on both the ledger name and the group placement.**

**So what is point 9 really asking for?** A **user-created "Salary" group under Current Liabilities holding one
ledger per employee** — a pattern Tally **permits** but does **not ship** and does **not teach** for payroll.
**The "allow it to be created" half is therefore ordinary master data — and it is genuinely blocked only by the
missing Group screen.**

### Current state — the UI cannot create an accounting group; everything beneath it can

**🔴 THE HEADLINE BUG — `Create → Group` is a mis-wired stub that silently opens Ledger Creation.**

- `MainWindowViewModel.cs:1060` — the menu item exists, under `MenuItemViewModel.Header("Accounting Masters")`
  (`:1058`):
  `col.Add(new MenuItemViewModel("Group", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));`
- `MainWindowViewModel.cs:4896-4897` — the dispatch:
  ```csharp
  case "Ledger": ShowLedgerMaster(); break;
  case "Group":  ShowLedgerMaster(); break;
  ```
  **Both arms call the same method.** *(Verifier note: the `() => { }` lambda at `:1060` is **not** the bug —
  "Ledger" at `:1059` uses the identical no-op and works, because dispatch is the string switch at `:4889`.
  **The only fix site is `:4897`.**)*
- **No `Screen.GroupMaster`** in the 98-value enum (`MainWindowViewModel.cs:14-141`) — only `StockGroupMaster`
  (`:49`) and `EmployeeGroupMaster` (`:109`), which are the inventory and payroll trees. `Glob
  **/GroupMaster*.cs` → **no files**.
- **No engine service creates a group.** `grep -rn "AddGroup(" src/` returns exactly **four** call sites, none
  user-driven from the UI: `Company.cs:521` (the bare adder), `CompanyFactory.cs:29` (seeds the 28),
  `ImportPlan.cs:174` (import), `SqliteCompanyStore.cs:1314` (reload). **There is no `CreateGroup` /
  `AlterGroup` / `RenameGroup` / `UpdateGroup` anywhere in `src/Apex.Ledger/`, and no `GroupService`.**
- **Consequence for the ledger master:** `LedgerMasterViewModel.cs:115` — "The 28 groups (excluding the reserved
  P&L head) the Under-picker offers"; `:336` `Groups = company.Groups.OrderBy(g => g.Name, …)`. **The picker is
  generic over `company.Groups`, so it would show a custom group the moment one can exist** — the block is
  purely the missing creation screen.

### ⚠️ Verifier correction — 🔴 MATERIAL, and it turns a hypothetical risk into a LIVE DEFECT

**"No accounting group can ever be created from the UI" is FALSE. The IMPORT path is a live, user-reachable
escape hatch.** Gateway → **Import** (`O` / `Alt+O`; `MainWindowViewModel.cs:2547-2564`, `Screen.ImportData`,
RQ-20..24) reaches `ImportPlan.cs:171-174`:
```csharp
new Group(Guid.NewGuid(), g.Name, ParseEnum<GroupNature>(g.Nature), parent, g.Alias, isPredefined: false)
… t.AddGroup(domain);
```
**A user CAN create a custom group today by importing canonical JSON/XML.** This does not change the verdict
(it is not a master screen and not what the CA asked for), but it matters **twice**:

1. **It is a zero-code way to END-TO-END VERIFY the no-schema / no-Io / no-migration claim before committing to
   the slice.**
2. **🔴 The nature-mis-derivation risk is NOT hypothetical — IT IS ALREADY LIVE IN PRODUCTION.**
   **`ImportPlan.cs:172` accepts the caller-supplied `Nature` verbatim, with NO derivation from — and NO
   validation against — the parent.** So a canonical file declaring `Nature=Asset` with `parent=Current
   Liabilities` is **silently accepted**, and that "Salary Payable" lands on the **ASSETS** side of
   `BalanceSheet.cs:115`. **The sheet still balances. Nothing fails loudly.** The decoder framed this as a risk
   to avoid in new code; **it is an existing defect.** ⇒ **The proposed `AccountGroupService.CreateGroup` must be
   SHARED WITH (or at minimum mirrored by, with a regression test) `ImportPlan`.** The decoder's proposal does
   not mention `ImportPlan` at all.

### 🟢 What already works and needs no new work (this de-risks the slice materially)

- **Domain:** `src/Apex.Ledger/Domain/Group.cs:1-48` — "The 28 predefined groups form the backbone; **custom
  groups nest under any of them**"; carries `Name`, `Nature`, `ParentId`, `Alias`, `IsPredefined`, `IsPrimary`.
  *(`SeedGroups.cs:75` constructs with `isPredefined: true`, implying the `false` path exists — and `ImportPlan`
  proves it.)*
- **✅ Schema v44 already persists custom groups — NO MIGRATION NEEDED.** `Schema.cs:657-668`:
  `CREATE TABLE groups (id, company_id, name, nature, parent_id TEXT NULL REFERENCES groups(id), alias,
  is_predefined INTEGER NOT NULL, is_pl_head …)`. `SqliteCompanyStore.cs:1307-1314` comment: "the 28 (**and any
  custom**) groups go into Company.Groups".
- **✅ Io already round-trips custom groups losslessly — NO FOLD-IN NEEDED.** `CanonicalModel.cs:640` `GroupDto`;
  `CanonicalMapper.cs:397` `MapGroup`; `CanonicalXml.cs:77` `List("groups", "group", p.Groups, BuildGroup)`;
  `ImportPlan.cs:174`.
- **✅ Reports already place custom groups correctly at any depth.** `ClassificationRules.cs:14-27`
  `PrimaryAncestorOf` walks `ParentId` to the primary head (with a **1024-iteration cycle guard**, `:24-25`);
  `:30` `PrimaryNatureOf`. `BalanceSheet.cs:113` `var primaryNature = ClassificationRules.PrimaryNatureOf(group,
  company);` and `:125` `liabilities.Add(new BalanceSheetLine(ledger.Name, group.Name, …))` — **a ledger under a
  custom "Salary Payable" (child of Current Liabilities) resolves to Liability nature and prints on the
  liabilities side, grouped under its immediate group name. Exactly what point 9 asks for.**
- `ChartOfAccountsViewModel.cs:100-110` already nests arbitrary child groups by `ParentId`.
- **The VM template to copy:** `StockGroupMasterViewModel.cs:45-120`. **The validation template:**
  `InventoryService.cs:31` `CreateStockGroup(string name, Guid? parentId, string? alias, bool addQuantities)`,
  which already "enforces unique name + valid, non-cyclic parent".

### Gap

1. **No accounting-Group creation screen.** No `Screen.AccountGroupMaster`, no VM, no AXAML page column, no
   engine `CreateGroup`. `Create → Group` opens Ledger Creation (`:4897`).
2. **No Alter path for Groups** (WI-3). Tally ships `GOT > Alter > Group` (Book p.15) **and** CoA + Alt+H Multi
   Alter (Study Guide:2111-2117); we ship neither.
3. **`ImportPlan.cs:172` does not derive or validate `Nature` against the parent** — a live Balance-Sheet
   corruption path.

**NOT a gap:** schema, persistence, Io, domain, report classification. All ready.

### Proposal

**No schema change, no migration, no Io fold-in — verified.** Mirror `StockGroupMasterViewModel`; it is the
proven template.

1. **Engine** — new `src/Apex.Ledger/Services/AccountGroupService.cs` (or `GroupService`), mirroring
   `InventoryService.cs:31`:
   - `Group CreateGroup(string name, Guid? parentId, string? alias = null)` — trim + require name; reject
     duplicates via **`Company.FindGroupOrHeadByName` (`:1005`) — the head-INCLUDING variant**, so a group cannot
     collide with the reserved P&L head; require the parent to exist; **derive `Nature` from
     `ClassificationRules.PrimaryAncestorOf(parent).Nature` — do NOT let the caller pass a nature** (that is
     exactly how a "Salary Payable" under Current Liabilities silently lands on the asset side); reject a cyclic
     parent (copy one of the four existing guards, e.g. `InventoryService.EnsureStockGroupParentValid:66-83`);
     `isPredefined: false`; then `company.AddGroup(...)`.
   - `void AlterGroup(Guid id, string name, Guid? parentId, string? alias)` — rename in place per `plan.md:221`;
     reject re-parenting/renaming a predefined group (**pending Q6 — the catalogue only forbids *deleting***) and
     reject a re-parent that would create a cycle or flip nature under a group that already has posted ledgers.
   - **🔴 SHARE THIS VALIDATOR WITH `ImportPlan.cs:171-174`, or mirror it with a regression test** — the import
     path is the live defect (see the verifier correction).
2. **VM** — new `src/Apex.Desktop/ViewModels/AccountGroupMasterViewModel.cs`, a near-copy of
   `StockGroupMasterViewModel.cs:45-120`: `Name`, `Alias`, `SelectedParent` (every `company.Groups`, sorted;
   **no "Primary" option** — Book p.14 says "Select Under from 28 lists of groups"; see Q3), `Message`,
   `Create()`, `Alter()`, `Existing`, `ToMasterListSnapshot()`. Keep it Avalonia-free so it is headlessly
   testable.
3. **Shell** (`MainWindowViewModel.cs`): add `AccountGroupMaster` to the `Screen` enum (`:14-141`); add
   `[ObservableProperty] private AccountGroupMasterViewModel? _accountGroupMaster;` + its `partial void
   On…Changed` → `OnPropertyChanged(nameof(IsMenuScreen))`; add it to the `IsMenuScreen` null-chain (`:555-566`)
   and the reset path (~`:4198`); add `ShowAccountGroupMaster()` mirroring `ShowStockGroupMaster()` (`:2725-2731`)
   → `OpenPageColumn(new GatewayColumn("Group Creation", master), Screen.AccountGroupMaster, "Group Creation",
   () => AccountGroupMaster = master);`; **fix `:4897` → `case "Group": ShowAccountGroupMaster(); break;`**; add
   arrow-key nav (mirror `:4504-4506`).
4. **View** — a new page column in `MainWindow.axaml`. Use the `Cmp08ReportViewModel` template + the **`#Root`
   binding path** (NOT the `GatewayColumn` `x:DataType` fallback → AVLN2000). Watch starved `*` columns and
   horizontal `StackPanel`s.
5. **Tests.** Engine: create under Current Liabilities → `ClassificationRules.PrimaryNatureOf` == **Liability**;
   duplicate name rejected; unknown/cyclic parent rejected; predefined group not alterable; **and an
   `ImportPlan` test that a nature contradicting the parent is rejected** (locks the live defect). Desktop:
   **`Create → Group` opens `Screen.AccountGroupMaster`, NOT `Screen.LedgerMaster`** (the regression lock on the
   `:4897` bug); the new group appears in `LedgerMasterViewModel.Groups`; a ledger under it prints on the
   **liabilities** side of `BalanceSheet` grouped under "Salary Payable". Io: a custom group survives a JSON+XML
   round-trip (extend `CanonicalRoundTripTests`) — **should pass unchanged, proving the no-fold-in claim.**
   Persistence: custom group survives save/reload — **should pass unchanged.**
6. **Reachability** — gate/wire at the **ROOT** too, prove it with a nav test that walks from the Gateway root,
   plus a **headless-Skia render-verify then REVERT `TestAppBuilder`** (`IsEffectivelyVisible` stays TRUE when a
   parent clips, so only a real render catches an off-pane control).

### Risk

- **🔴 Nature mis-derivation silently corrupts the Balance Sheet — and it is ALREADY LIVE via import.** If
  `CreateGroup` lets a caller pass `Nature` instead of deriving it, a "Salary Payable" under Current Liabilities
  can carry `Asset` nature. `BalanceSheet.cs:113` branches on `PrimaryNatureOf` — **a wrong nature puts the
  salary payable on the ASSETS side and the sheet still "balances", so nothing fails loudly.** Same class as the
  catalogued Trial-Balance overprint defect: a **financial-misread** risk. **Derive, never accept.
  Regression-lock it. Fix `ImportPlan.cs:172` in the same slice.**
- **🔴 Re-parenting a group with posted ledgers retroactively rewrites history** — `plan.md:221` mandates
  "applies **retroactively to all historical vouchers**". Moving "Salary Payable" to Indirect Expenses silently
  moves every historical balance from the Balance Sheet to the P&L and restates prior-period profit — and
  `BalanceSheet.cs:84` already skips P&L groups, so the ledgers would simply **vanish** from the sheet.
  **Guard or confirm-prompt a nature-changing re-parent when the group has posted ledgers.**
- **🟠 Cycles.** `Company.AddGroup` (`:521`) is a bare `_groups.Add` with **zero validation**. A self/cyclic
  parent would surface as a raw `InvalidOperationException` **from a report** (`ClassificationRules.cs:24-25`
  guard), not a friendly message at the master. Validate at create/alter.
- **🟠 Name collision with the reserved P&L head.** `FindGroupByName` (`:994`) deliberately **excludes**
  `ProfitAndLossHead`; `FindGroupOrHeadByName` (`:1005`) includes it. **Use the head-including variant.**
- **🟠 Reachability regression — the project's most-repeated bug.** ("root gate omitted `IsRegularGstDealer` →
  ALL 10 screens unreachable"; "a `ShowXMenu()` test proves nothing".)
- **🟠 UI-defect campaign collision** — a new page column in the shared 14,785-line file, mid-sweep. **Sequence
  after**, and build the column to whatever layout contract that campaign settles on (its whole finding was that
  there **isn't** one).
- **🟡 Two "Group" menu items, one word.** "Group" now legitimately means three things (accounting / stock /
  employee). WI-1 and WI-9 must disambiguate — coordinate naming before this lands.
- **🟢 SCHEMA: NONE.** Verified — but **verify it with the round-trip test rather than trusting it** (expect green
  with zero production change).

### Open questions

1. **Q3 (blocking design) — should the new Group screen allow creating a group under "Primary"** (a 16th
   top-level head, which would need an explicit **Nature** pick)? **The two sources disagree:** Study Guide:2085
   says "One can also create groups under the Primary group category, if required"; Book p.14's create screen
   says "Under: Select **Under from 28 lists of groups**". The proposal picks the **conservative** reading
   (28 groups only, nature always derived). **Confirm — allowing Primary means the user must pick a Nature, which
   reopens the top risk.**
2. **(A14 / R7) Can a predefined group's name/parent be altered in Tally?** The catalogue (`:116`) only says
   predefined groups cannot be **deleted**; it is silent on alter. **Confirm before adding a guard Tally does not
   have.**
3. **Where does group Alter live** — a Gateway → Alter menu (Book p.15), or the **Chart of Accounts + Alt+H Multi
   Alter** (Study Guide:2111-2117, Tally's real entry point)? See WI-3 Q1/Q6.
4. **Sequencing:** WI-7 is a **prerequisite** for WI-1 (point 8's "group") and for WI-3 (point 10's "groups").
   **Schedule it early in that cluster.**

---

# WI-8 — TDS on a non-party (employee) ledger

**Covers point 9 (second half).** **State: NOT IMPLEMENTED.** **Effort: M.**
**🔴 BLOCKED — do not build until Q1/Q2/Q4/Q5 are answered. Granting this naively is an ACTIVE CORRECTNESS
REGRESSION, not a feature.**

### Raw wording (verbatim fragment)

> "TDS of employee should be enabled in that ledger creation or alteration"

### Decoded requirement

When creating (or later altering) a per-employee ledger under a custom payable group, the **TDS deductee
sub-screen** (Is TDS Deductable / Deductee Type / PAN / Deduct TDS in same voucher) must be reachable. Today it
is **hard-gated to ledgers under Sundry Debtors/Creditors only**, so a ledger under a custom "Salary Payable"
group gets **no TDS block at all**. And there is **no Alter path for any master**, so "or alteration" is
unreachable by construction (WI-3).

This is the **non-payroll salary workflow**: post salary by Journal against a per-employee payable ledger and
deduct TDS there — as opposed to the Payroll module (which the app already implements) where employee identity
rides on the voucher **line** and the net credits **one shared** "Salary Payable" ledger.

### 🔴 Why granting this naively would be WRONG — the decisive finding

**The per-ledger TDS mechanism already exists — but it is wired for §194x contractors/professionals, NOT for
salary.** `src/Apex.Ledger/Domain/Ledger.cs` carries: `TdsApplicable` `:129`, `TdsNatureOfPaymentId` `:132`,
`DeducteeType` `:135`, `PartyPan` `:139`, `DeductTdsInSameVoucher` `:143` (+ TCS siblings `:147-153`,
`TdsTcsClassification` `:160`).

**`grep -rn '"192' --include=*.cs src/` returns NOTHING. There is no §192/§392 nature of payment anywhere in the
codebase.** `NatureOfPayment.cs:19-20` requires a `SectionCode`, and `:37-38` requires an `FvuSectionCode`
"(e.g. \"94J-B\", \"4IA\", \"94Q\")" — i.e. **the master is structurally a Form-26Q section master.** The only
salary section codes that exist are `Form24Q.cs:126-132` — "92B"/"92A"/"92C" — and those are **24Q/138
*statement* section codes, a different thing from an Act section**; they live on the **return**, not on any
master, and nothing links them to `NatureOfPayment`.

**So if a user ticked `TdsApplicable` on an employee ledger today, they would have to point
`TdsNatureOfPaymentId` at some §194x nature — and the withholding would flow into `TdsLineTax`, be picked up by
`TdsDepositService` / `ChallanReconciliation`, and land in Form 26Q.** That is **precisely the "pollute Form 26Q
with §192 rows" failure the Phase-8 author deliberately refused** (`Form24Q.cs:106-110`). It would also
**mis-deduct**: §194x is flat-rate-on-payment; §392/§192 is **average-rate on estimated annual income** with
rebate/surcharge/cess/true-up.

**And §192 salary TDS is a PAYROLL feature in Tally, not a ledger flag** (Book p.332 "Set/Alter Income tax
details" on a pay head; Book p.353 "Pay head type — Employees' Statutory Deductions / **Statutory pay type —
Income Tax** / Under — Duties & Taxes / Calculation Type — As Per Income Tax Slab"). §192 appears in Book p.281's
TDS rate chart ("192 | 92A & 92B | Salaries | Normal Slab rate") **as tax-law background, not as a TDS
Nature-of-Payment master entry.** **A faithful clone should not put §392/§192 on a ledger's deductee flags.**

### Current state

- **The gate:** `LedgerMasterViewModel.cs:290-292` — "should render: a TDS/TCS feature is on AND the chosen group
  is a party group (Sundry Debtors/Creditors)"; `public bool ShowPartyTdsTcs => ShowTdsTcs && IsPartyGroup;`
- `:402` `public bool IsPartyGroup => SelectedGroup is not null && IsUnderParty(SelectedGroup);`
- `:422-434` `IsUnderParty` walks the parent chain and returns true **only** for "Sundry Debtors" / "Sundry
  Creditors" (`g.Name.Equals("Sundry Debtors", StringComparison.OrdinalIgnoreCase) || … "Sundry Creditors" …`).
  The gate also drives the **write path** (`:587-603`), so even a pre-set value would not persist.
- **Bill-by-bill is gated by the same predicate** (`:406`) — an employee payable ledger could not maintain bill
  references either, which the manual salary workflow wants (Tally's deductee ledger sets "Maintain Balances Bill
  by Bill: Yes").

### ⚠️ Verifier correction — the decode CONFLATES two different fields, and it may reopen schema

**There is NO field named "Is TDS Deductable" in our model.** The decode's gap statement ("a ledger under a
custom Salary Payable gets no `Is TDS Deductable` / `Deductee Type` / `PAN` / `Deduct TDS in same voucher`") is
imprecise in a way that matters:

- `Ledger.cs:129-143` has `TdsApplicable`, `TdsNatureOfPaymentId`, `DeducteeType` (nullable), `PartyPan`,
  `DeductTdsInSameVoucher` — **there is no deductee-side boolean.**
- **`TdsApplicable` ("Is TDS Applicable", the EXPENSE-side flag) is gated ONLY by `TdsEnabled`
  (`LedgerMasterViewModel.cs:583-586`) — so it DOES render for a custom group today.** Only `DeducteeType` /
  `DeductTdsInSameVoucher` (`:587-591`), `CollecteeType` (`:597-598`) and `PartyPan` (`:601-602`) are gated by
  `ShowPartyTdsTcs`. *(The decode correctly did **not** list `TdsApplicable` in the write-path gate — the
  substance is right; the naming is not.)*
- **This SHARPENS the decode's own open question:** Tally has a **distinct per-ledger "Is TDS Deductable: Yes/No"**
  (Study Guide p.138 field 4; Book's practice-table column header "Is TDS Deductable") **which our domain LACKS
  ENTIRELY** — deductee-ness is currently **implied** by (group is party) + DeducteeType set.
  **⇒ If Q5 resolves to "Tally gates by an explicit per-ledger opt-in, not by group", then WI-8 needs a NEW
  DOMAIN FIELD and therefore possibly SCHEMA work.** The decode's confident "no schema for 9b either" is **only
  safe under the group-gate reading.**

### Tally fidelity target — ✅ GROUNDED for party ledgers; 🟡 UNVERIFIED for a custom liability group

- **Deductee ledger, Book p.290:** "*How to Create Deductee Ledger in Tally Prime?* GOT > Create > Ledger …
  **Under — Select \"Sundry creditor\"** … Maintain Balance bill by bill — Yes … **Deductee type** … **Deduct TDS
  in Same Voucher — Yes** … **PAN/IT No**".
- **Cross-confirmed, Study Guide p.138** (`l.3915-3925`, ledger "Mohit Agarwal"): "2. **Under: Sundry Creditors**
  … 4. **Is TDS Deductable: Yes** 5. **Deductee Type: Individual / HUF / Resident** 6. Deduct TDS in Same
  voucher: Yes 7. Enter Mailing Details and PAN No. (PAN Number is mandatory)".
- **🟡 Both sources show the deductee ledger under Sundry Creditors, and NEITHER shows a deductee ledger under
  Current Liabilities or a custom liability group.** **Whether Tally renders the deductee block for a ledger under
  "Salary Payable" is UNVERIFIED and must be checked in the real product before we widen our gate.**
- **Non-payroll salary journal is a real, documented Tally pattern:** Book p.47 — "Salary — Expense — Dr /
  **Salary Payable — Liabilities — Cr**". So the CA's workflow is legitimate.

### Gap

1. The deductee block is hard-gated to Sundry Debtors/Creditors (`LedgerMasterViewModel.cs:422-434`).
2. Bill-by-bill is gated by the same predicate (`:406`).
3. **No §392/§192 nature of payment exists** — and the existing `NatureOfPayment` master is structurally a 26Q
   section master, so reusing it would route salary TDS into **Form 26Q** and apply the **wrong rate mechanic**.
4. **No Alter path** (WI-3) ⇒ "or alteration" is unreachable regardless.
5. **Possibly a missing domain field** — Tally's explicit per-ledger "Is TDS Deductable" (see the verifier
   correction).

### Proposal — DO NOT BUILD UNTIL Q1/Q2 ARE ANSWERED

**If the answer is "the §194x deductee reading, and Tally does show the block there":**
Replace the name-match in `LedgerMasterViewModel.cs:422-434` with a **classification-based** predicate rather
than adding `|| g.Name.Equals("Salary Payable")` — **name-matching a user-created group is exactly the fragility
the project already flagged** (`docs/phase8-payroll-requirements.md:292` insists ledgers be "classification-
tagged so reports map ledger→head **without name parsing**"). **Cleanest: split the one `IsPartyGroup` predicate
into `IsPartyGroup` (bill-by-bill / party-GST — keep as-is) and a separate `CanBeTdsDeductee`** (any
Liability-nature group, or an explicit per-ledger "Is TDS Deductable" opt-in à la Tally). Then relax
`ShowPartyTdsTcs` (`:292`) and the write path (`:587-603`) to the **new** predicate. **S** once decided —
*unless* Q5 requires a new domain field, in which case **schema is back on the table**.

**If the answer is "the §392/§192 salary reading": BUILD NOTHING.** It exists (`TaxDeclarationViewModel`,
`SalaryIncomeTax`, `Form24Q`, `Form16`, `Employee.ApplicableTaxRegime`). **Fix discoverability instead** and show
the CA the Payroll path. See WI-6 Q2.

### Risk

- **🔴 Anti-fidelity + double-path.** Bolting §392/§192 onto ledger deductee flags would contradict Tally (Book
  pp.332/353: §192 = Pay Heads) **and** duplicate a working engine, creating **two divergent salary-TDS paths and
  a TDS figure that disagrees with Form 24Q/138.**
- **🔴 Form 26Q pollution + wrong deduction mechanic** if the §194x plumbing is reused (see the decisive finding).
- **🟠 Widening `IsUnderParty` over-broadly.** `LedgerMasterViewModel.cs:406` uses the **same** predicate to
  default `MaintainBillByBill`. **Relaxing `IsUnderParty` itself (rather than splitting out a separate
  `CanBeTdsDeductee`) would silently flip bill-by-bill ON for a swathe of existing non-party groups, changing
  Outstandings for existing companies.** **Split the predicate; don't widen the shared one.**
- **🟡 Schema may return** if Q5 resolves to an explicit per-ledger opt-in.

### Open questions — ALL BLOCKING

1. **🔴 Q1 (blocks everything) — "TDS of employee": do you mean (a) §392/§192 salary TDS** (slab-based, Form
   16/130, Form 24Q/138) — which **already exists** in the Payroll module and needs only discoverability — **or
   (b) the §194x deductee block** (Deductee Type / PAN) on the employee's ledger — a small gate change?
   **Please also ask the CA whether they were aware of the existing Payroll §192 module.** Point 6 suggests
   possibly not — **and if so, several of these 15 points may be discoverability, not gaps.**
2. **🔴 Q2 — is the intended workflow the manual/non-payroll one** (Journal: `Dr Salary / Cr <Employee>` against
   a per-employee ledger under Salary Payable, TDS deducted in that journal), **or should the Payroll module be
   used** (which already auto-creates one shared "Salary Payable" under Current Liabilities and tags each line
   with the employee)? **Tally supports both.** If the CA wants both to coexist, **confirm we are NOT replacing
   the payroll design.** *(Reading (a)-non-payroll is what point 9's wording most naturally suggests — "creation
   of **ledger of an employee**" only makes sense if employees are *ledgers*, which in Phase-8 payroll they are
   **not**. Cost: **large** — a whole parallel salary path. Do NOT pick this without the user.)*
3. **🟡 Q4 (A14 / live Tally, blocking) — does Tally show the "Is TDS Deductable / Deductee Type / PAN" block for
   a ledger under a CUSTOM group whose parent is Current Liabilities**, or only under Sundry Debtors/Creditors?
   **Both corpus sources show Sundry Creditors and neither shows any other group. NOT FOUND. Must be checked in
   the real product or against official TallyHelp — do not infer.**
4. **🟡 Q5 (A14, blocking, and it decides whether schema moves) — does Tally gate the deductee block by GROUP at
   all, or by an explicit per-ledger "Is TDS Deductable? Yes/No" the user simply answers on any ledger?** The
   Study Guide's field ordering (`4. Is TDS Deductable: Yes`) **hints at the latter** — in which case **our whole
   `IsUnderParty`-driven design is wrong in SHAPE, not just in scope**, the fix is cleaner than proposed, **and a
   new domain field (+ possible v45) is required.**
5. **Sequencing:** WI-3 (alteration) is required for the "or alteration" half.

---

# WI-9 — Bare-letter menu hotkeys, with the letter shown in red

**Covers point 7.** **State: NOT IMPLEMENTED.** **Effort: L.**
**🟡 The FIDELITY TARGET IS UNVERIFIED — see below. Ground it before building.**

### Raw wording (verbatim fragments)

> "any type of option in the gateway" · "or any other letter"

### Decoded requirement

Every selectable option in the Gateway menu cascade must be activatable by a **single bare letter** keystroke
(no Alt/Ctrl), and that letter must be **rendered in red inside the option's own label**. The letter is normally
the label's first character, but where the first character collides with another option **in the same menu**, a
different character of that label is designated instead ("or any other letter"). So: **exactly one designated
hotkey character per selectable item, unique within its menu column, drawn in red inside the label text**, and
pressing it selects/activates that item directly instead of arrowing to it.

Four pieces: (a) a per-item designated-hotkey character; (b) render that character red within the existing label;
(c) handle a bare letter keypress on a menu column; (d) **guarantee per-column uniqueness across every F11-feature
permutation**, since menu contents are conditional.

### 🟡 Tally fidelity target — UNVERIFIED. This is the honest weak point of this item.

**Grounded (the adjacent facts only):**
- `docs/tally-feature-catalog.md:50` — "**Gateway of Tally (GOT)** is the home hub. Everything is reached by
  menus or shortcuts from here."
- `:52` — "**Keyboard-driven**: nearly every action has an `F`/`Alt`/`Ctrl` shortcut; mouse optional."
- `:53` — "**Right-hand button bar** exposing context actions (the shortcuts)."

**NOT GROUNDED — the specific mechanic the CA describes.** A **single BARE letter per menu item, drawn in RED
inside the label, first-letter-by-default-with-fallback**, is:
- **NOT** in `docs/tally-feature-catalog.md` (including §21, the keyboard reference at `:462-481`, which lists
  only F/Alt/Ctrl shortcuts — **no bare-letter menu keys at all**);
- **NOT** in `docs/tally-feature-catalog-verification-report.md` (zero hits for `red|highlight|first letter|
  underlin|mnemonic`);
- **NOT** in any of the 10 corpus PDFs. **Two agents searched independently with different term sets**
  (`letter (is|in) red`, `red letter`, `shown in red`, `appears in red`, `colou?red letter`, `type the letter`,
  `press the (first )?letter`, `initial letter`, `bold letter`, `hot key`, `hotkey`, `selecting an option`,
  `mnemonic`, `underlined letter`, …). **`in red` / `red colour` / `red color` = ZERO hits corpus-wide.** The
  only `highlighted` hit is `tally/664311548-Tally-Prime-Book.pdf:14773` — "This option is highlighted only when
  Tally.Net user option is activated" — which is about **field enablement, not menu letters.**

**Do NOT use `tally/659947760-Tally-Prime-Short-Key.pdf`** — `docs/tally-feature-catalog.md:479-481` flags it as
having "garbled shortcut mappings", and it genuinely is (line 3 "Alt+K → Go To", line 30 "F7 → Payment").

**MUST BE CHECKED against a real TallyPrime build or official Tally help before design is frozen:**
(a) does TallyPrime select menu items by a **bare** single letter, or Alt+letter? (b) is the highlight colour
genuinely **RED** in the default theme, or a theme accent the CA is describing loosely? (c) is the designated
letter always the first character, and what is Tally's documented collision rule? (d) does the letter **activate**
immediately or only move the highlight? **Until answered, the fidelity target is an assumption.** `docs/tally-
feature-catalog.md` should gain a subsection under §0/§21 recording the answer — **it currently documents no
bare-letter menu keys at all.**

*(Note: the CA is a domain authority and the user reports this as a real gap, so the requirement is probably
right. It is the **exact colour, activation semantics and collision rule** that are ungrounded.)*

### Current state — no per-item hotkey exists, and no letter selects a menu item

1. **The menu-item model has no hotkey field.** `src/Apex.Desktop/ViewModels/MenuItemViewModel.cs:34-96` — the
   complete public surface is `IsSelected`, `IsActiveColumn`, `Label`, `Hint`, `Activate`, `Kind`, `IsHeader`,
   `IsSelectable`, `IsGroup`, `IsPage`, `IsSubItem`. **No hotkey char, no index, no accelerator.** The ctor
   (`:69-82`) takes only `(label, activate, hint, isSubItem, kind)`.
2. **The label renders as one flat, uncoloured string.** `MainWindow.axaml:240-242` — `<TextBlock Grid.Column="0"
   Text="{Binding Label}" FontSize="14" TextTrimming="CharacterEllipsis" Foreground="{Binding IsSelected,
   Converter={StaticResource SelFore}}"/>`. Same shape at `:142-144` (pre-company centred menu). **No `Inlines`,
   no `<Run>`, no `AccessText`, no per-character brush.**
3. **No key handler matches a letter against menu items.** `MainWindow.axaml.cs:490-545` is the whole navigation
   switch: Up/Down, Right/Enter, Left/Esc, F1–F12, then exactly four hardcoded letters
   `case Key.B/P/T/D when CanQuickJump(vm, e): Fire(vm, "B"/"P"/"T"/"D")`. **`Fire` (`:567-575`) walks
   `vm.ButtonBar` matching `b.Key` — the RIGHT-HAND BUTTON BAR, never the menu items. Nothing reads
   `MenuItemViewModel.Label`.**
4. **Grep sweep — zero hits.** `hotkey|accelerator|mnemonic|accesskey|AccessText` (plus `HotkeyIndex`,
   `HotkeyChar`, `LabelPrefix`, `LabelRuns`, `DisplayLabel`, `KeyLetter`) across `src/**/*.cs` and `src/**/*.axaml`
   returns **only prose in XML doc-comments** ("the public entry a hotkey/test uses") — never a type, property, or
   handler. `src/Apex.Desktop/Converters/Converters.cs` (387 lines, 21 converters) has **no** label-splitting /
   accelerator converter. `tests/` contains no letter-keystroke test — the word "hotkey" there means the
   F-key/button-bar path (`CascadingGatewayTests.cs:237` "via its hotkey path (the 'B' button / OpenReport)") and
   calls the VM directly.

**Adjacent infrastructure that already exists:**
- **The red-key convention is already established — but only in the button bar, and as a separate badge, not a
  coloured character inside a label.** `MainWindow.axaml:14776-14786` (block starts `:14769`):
  `<Grid ColumnDefinitions="52,*">` at `:14777`, then `<TextBlock Grid.Column="0" Text="{Binding Key}"
  Foreground="{StaticResource AlertRed}" FontWeight="Bold"` at `:14778-14779`. `AlertRed` = **#B00020**
  (`App.axaml:22` `<Color x:Key="AlertRedColor">#B00020</Color>`, `:34` the brush).
- **Menu items DO advertise accelerators — as a grey right-aligned `Hint`** (`MainWindow.axaml:243-245`,
  `Foreground="#6A6A6A"`), and **only F/Alt/Ctrl combos**: `"F11"` (`MainWindowViewModel.cs:836`), `"Ctrl+R"`
  (`:839`), `"F4–F9  ▸"` (`:843`), `"F3"` (`:877`). **Never a bare letter.**
- **Two bare letters ARE live on the Gateway:** `Key.O` → Import (`MainWindow.axaml.cs:381-389`) and `Key.Y` →
  Export Data (`:391-402`), both gated `vm.CurrentScreen == Screen.Gateway`.
- **The RQ-28 precedent is already recorded in code** — `MainWindowViewModel.cs:5269-5271`: *"\"Outs\" (not
  \"O\") — the bare-O key is bound to Import on the Gateway (**RQ-28: a hint's letter must map to the action that
  key actually triggers**), so the Outstandings quick-button uses a non-key mnemonic badge and is reached by
  click, never by a colliding \"O\" keystroke."* **The project has already ruled that an advertised letter must
  not be a dead key.**

### ⚠️ Verifier corrections

1. **🔴 B/P/T/D are NOT globally dead — do not delete them.** The decoder said they are "live only on Company
   Select / Create Company, **where their actions are disabled** (`hasCompany` false)". **The second half is
   wrong.** `Company` is **never** nulled (`grep "Company = null"` → **zero hits**); once a company is opened it
   stays non-null for the session. `ShowCompanySelect()` (`:708-728`) calls `LeaveCascade()` (`:714` →
   `IsGatewayCascade=false`) and `ClearSubScreens()` (`:713`) ⇒ **`IsMenuScreen` (`:548`) is TRUE**; then
   `BuildButtonBar()` (`:727`) rebuilds with `var hasCompany = Company is not null` (`:5245`) = **TRUE**.
   **So on Company Select reached via "Quit — Change Company" (root item `:877`), pressing B really does
   `Fire(vm,"B")` → `OpenReport(BalanceSheet)`.** B/P/T/D are dead **only on a cold start before any company is
   opened.** ⇒ **The decoder's openQuestion "re-enable, delete, or re-letter B/P/T/D?" is framed on a false
   premise; deleting them would silently regress working Company-Select quick-jumps. The question is "re-letter
   or keep".** *(The decoder's HEADLINE claim — "dead **on the Gateway**", the load-bearing part for the collision
   analysis — is CORRECT and survives: `ShowGateway()` → `EnterCascade()` (`:814`) sets `IsGatewayCascade = true`
   (`:4219-4223`), and `IsMenuScreen` = `!IsGatewayCascade && …`, so `CanQuickJump` (`:564-565`) is false there.)*
2. **Wrong line — RQ-28.** The comment is at **`MainWindowViewModel.cs:5269-5271`**, NOT `:1269-1272` (which is
   the unrelated `ShowInventoryBatchReportsMenu()` doc-comment). The wrong number appeared **four times** in the
   decode. *(Mitigation: `touches` already included `:5236-5292` (BuildButtonBar), so an implementer would
   stumble on it anyway.)*
3. **Wrong line — the red badge.** It is at **`MainWindow.axaml:14776-14786`**, NOT `:14756-14759` (which are
   closing tags). The markup itself was described accurately.
4. **Verifier addition (an existing RQ-28-shaped wart to reconcile):** `new ButtonBarItem("C", "Cost Centres",
   …)` (`:5275`) renders a bare **"C"** in the red key badge, **but no bare `Key.C` handler exists** (only Alt+C
   at `:159` and Alt+C-in-report at `:153`). A bare-letter scheme should reconcile this.
5. **Adversarial check that SURVIVED — "only two live bare letters" is right.** There are three more bare-letter
   handlers the decoder didn't list — `Key.P`→Print (`:358`, `IsPrintablePage`), `Key.E`→Export (`:370`,
   `IsExportablePage`), `Key.M`→E-Mail (`:404`) — **but all three are inert while a menu column is active**:
   `IsPrintablePage`/`IsExportablePage` (`:2065`, `:2360`) require `IsReportContext` (`:2060`, `Reports is not
   null`), and `BackFromPage()` (`:5081-5105`) calls `ClearSubScreens()` (`:5096`) while `OpenGroupOf` calls it
   at `:4797`, so **no page VM survives with a menu column focused.**
6. **Minor:** `OpenGroupOf`'s range is `:4794-4844+`, not `:4794-4839` (the method extends past the cited end).
   `MainWindow.axaml:142-144` has **no** `TextTrimming`, unlike `:240-242` (the decoder said "same at" —
   harmless).

### Gap

1. **DATA:** `MenuItemViewModel` (`:34-96`) carries no designated hotkey character. Needs a nullable hotkey char
   **+ its index within `Label`**.
2. **RENDER:** the label is one flat `Text="{Binding Label}"` TextBlock (`:240-242`, `:142-144`). Needs three
   inline runs (before / hotkey char in red / after) **inside ONE TextBlock** so `TextTrimming` and the `*`
   column keep working.
3. **INPUT:** `OnKeyDown` (`:490-545`) has no branch matching a typed letter against `ActiveColumn.Items`.
4. **POLICY — the real work:** assign a **unique** letter to every selectable item in every fixed menu column and
   **prove uniqueness holds for every F11-feature permutation.** The root column alone
   (`MainWindowViewModel.cs:825-880`) has **19 selectable items with SIX first-letter collisions** (independently
   re-counted and confirmed): **C** — "Create" (`:831`) vs "Chart of Accounts" (`:832`); **G** — "GST" (`:836`)
   vs "GST Rate Setup" (`:839`) vs "GST Reports" (`:856`); **B** — "Banking" (`:844`) vs "Balance Sheet"
   (`:849`); **S** — "Statements" (`:853`) vs "Statements of Accounts" (`:854`) vs "Statutory Reports" (`:874`);
   **P** — "Profit & Loss A/c" (`:850`) vs "Payroll Reports" (`:863`); plus **D** — "Day Book" (`:845`) vs the D
   button-bar letter. **This is exactly why the CA wrote "or any other letter" — the fallback is mandatory, not
   optional.**

**NOT a gap:** no engine, no persistence, no report logic. **100% `Apex.Desktop`.**

### Proposal

**UI-only slice. No schema.** *(Assumes broad scope + activate-immediately + authored letters — **CONFIRM FIRST**,
see Q1/Q2/Q7.)*

1. **`MenuItemViewModel.cs`** — add `public int HotkeyIndex { get; }` (−1 = none) and `public char? Hotkey =>
   HotkeyIndex >= 0 ? Label[HotkeyIndex] : null`, plus derived `HotkeyPrefix`/`HotkeyChar`/`HotkeySuffix` for the
   view. Add an optional `hotkeyIndex` ctor param (default −1) so **all 176 existing call sites still compile.**
   **🔴 CRITICAL: do NOT encode the letter in `Label`** (no `&Vouchers` / `_Vouchers` markup) — see risk 1.
2. **`MainWindowViewModel.cs`** — pass the authored index at each `new MenuItemViewModel(...)`. Start with
   `BuildRootColumn()` (`:825-880`) and `BuildVouchersColumn()` (`:886-932`), then the other 25 `Build*Column`
   methods (**27 builders / 176 call sites — both counts verified exact**). **SKIP `BuildLedgerBookPickerColumn`
   (`:1390-1408`)** — it is data-driven (`new MenuItemViewModel(ledger.Name, …)` at `:1405`), so no authored
   letter is possible; that column is **WI-2's type-ahead territory** (see the cross-point conflict).
3. **`Converters.cs`** — add an **attached property** `HotkeyText.Item` building `TextBlock.Inlines` =
   [Run(prefix), Run(char){Foreground=AlertRed, FontWeight=Bold}, Run(suffix)]. **Attached property, not a
   StackPanel of three TextBlocks** — see risk 4.
4. **`MainWindow.axaml`** — apply at `:240-242` and `:142-144`. **Keep `TextTrimming="CharacterEllipsis"` and the
   `*` column exactly as-is.**
5. **`MainWindow.axaml.cs`** — in `OnKeyDown` (`:490-545`), **before** the `Key.B/P/T/D` arm (`:541-544`) and
   coordinated with `Key.O` (`:381`) / `Key.Y` (`:391`): if `!IsTyping(e)`, no Ctrl/Alt, `vm.IsGatewayCascade`,
   and the active column is a menu column → ask the VM to resolve the letter; if it resolves, activate and set
   `e.Handled = true`. **Put the resolution in the VM** (e.g. `public bool ActivateHotkey(char c)`) so it is
   unit-testable without a window.
6. **Decide the O/Y precedence explicitly** (risk 2) and **record it in `memory.md` as an RQ-28-style ruling.**
7. **Tests** (`tests/Apex.Desktop.Tests/GatewayHotkeyTests.cs`, new): for **EVERY** `Build*Column`, over **EVERY**
   relevant F11 permutation (GstEnabled / TdsEnabled / TcsEnabled / PayrollEnabled / PayrollStatutoryEnabled /
   composition-vs-regular), assert (a) every selectable item has a hotkey, (b) hotkeys are **unique within the
   column** (case-insensitive), (c) `Label[HotkeyIndex]` **equals** the hotkey (guards a stale hand-authored index
   after a label rename), (d) no column hotkey collides with a live global letter on that screen.
   Extend `HeadlessMainWindowTests.cs` using the existing **real-key** pattern at `:523-545`
   (`window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control)`) — **press the letter on the REAL window
   and assert the screen changed;** a VM-only test proves nothing about reachability. Render-verify the red run
   via headless Skia, then **REVERT `TestAppBuilder`**. Regression: hotkey activation must go through the same
   `DrillIn()` path so page columns **replace rather than stack** — the invariant asserted at
   `CascadingGatewayTests.cs:204` (`Opening_a_page_via_a_hotkey_while_a_page_is_open_replaces_it_never_stacks`).

### Risk

- **🔴 LABEL-STRING DISPATCH WILL BREAK SILENTLY IF THE LETTER IS ENCODED IN THE LABEL.** Group and page routing
  is a `switch (item.Label)` on the **raw string** — `OpenGroupOf` (`:4794-4844+`, switch from `:4800`) with
  context-sensitive arms `"Batch" when CurrentGatewayMenu == GatewayMenu.InventoryReports` (`:4820`) and
  `"Ledger" when CurrentGatewayMenu == GatewayMenu.AccountBooks` (`:4837`); `OpenPageOf` (`:4879+`) likewise.
  **Any Tally/WPF-style `&Vouchers` or `_Vouchers` markup in `Label` makes every arm fall through to the default
  with NO compile error — dozens of menu items would go dead.** The hotkey **MUST** be a separate index/char
  field. **This is the single costliest mistake available here.**
- **🟠 Collision with the two live bare-letter Gateway keys.** `Key.O` → Import (`:381-389`) and `Key.Y` → Export
  (`:391-402`) are gated **only** on `vm.CurrentScreen == Screen.Gateway` — and **`CurrentScreen` STAYS
  `Screen.Gateway` inside every submenu column** (`OpenGroupOf` sets it at `:4798`). **So O fires Import even
  while the Vouchers column is active** — colliding head-on with "Order Vouchers" (`:900`) and "Other Vouchers"
  (`:905`), both natural O candidates. Needs an explicit precedence ruling consistent with RQ-28 (`:5269-5271`).
  **One of the two advertised meanings of O must change or be re-lettered.**
- **🟠 Conditional menu items ⇒ uniqueness is PER-PERMUTATION, not per-column.** "GST Rate Setup" appears only
  when `Company is { GstEnabled: true }` (`:838`), "Payroll Reports" only when PayrollEnabled (`:862`),
  "Statutory Reports" on a 5-way condition (`:872-874`), and `BuildVouchersColumn`'s TDS/TCS/Payroll blocks at
  `:910-930`. **A letter set unique for a plain company can COLLIDE once a feature is switched on. A test over
  the default company only would pass and ship the bug.** *(An auto-assigner makes this **worse** — it silently
  reshuffles letters when a feature is toggled, breaking muscle memory.)*
- **🟡 Rendering regression — the truncation trap.** The label lives in a `*` column with
  `TextTrimming="CharacterEllipsis"` (`:238-242`). **Splitting it into three TextBlocks in a horizontal
  StackPanel would destroy trimming** (infinite-width measure) — the exact defect class the campaign is cleaning
  up (110 truncated-text findings). **Use inline `<Run>`s inside ONE TextBlock. Render-verify.**
- **🟡 Data-driven columns have no authorable letter.** `BuildLedgerBookPickerColumn` (`:1390-1408`) emits one
  item per DB ledger name. N ledgers > 26 letters, and names are user data. Under a broad reading these columns
  will look inconsistent unless the answer is explicitly "type-ahead there instead" — **which is WI-2.**
- **🟢 Colour overload.** `AlertRed` #B00020 currently means both "error message" (`MainWindow.axaml:177`) and
  "shortcut key badge" (`:14778`). A third meaning is defensible (matches the button bar) but worth a conscious
  call.
- **🟢 NO SCHEMA RISK.** v44 untouched — nothing persists. **The cheapest risk profile of any item here.**

### Open questions

**For the user / CA (blocking scope):**
1. **Does "the gateway" mean the root Gateway column only (19 items), or every menu column in the cascade
   (27 builders, 176 items)?** **~9× effort difference.** *(Leaning broad: in this app the Gateway IS the whole
   cascading shell — `CurrentScreen` stays `Screen.Gateway` inside submenus, `:4798`.)*
2. **Does the letter ACTIVATE immediately, or only move the highlight (still needing Enter)?** *(Recommend
   activate — Tally-style.)*
3. **Does this extend to the right-hand button bar** (which already shows red key badges) **and to in-page
   buttons**, or strictly to the menu options?

**For the CA / A14 (🟡 R7 — currently UNVERIFIED; needs a real TallyPrime build or official Tally help):**
4. **Does TallyPrime select menu items by a BARE letter, or Alt+letter?**
5. **Is the highlight colour genuinely RED in the default theme**, or a theme accent described loosely?
6. **What is Tally's actual collision rule** when two options in one menu share a first letter — **and is the
   chosen letter stable across releases?**

**For the implementing agent (design decisions):**
7. **Authored letters** (stable, hand-picked — **recommended**) **or auto-assigned** (reshuffles when an F11
   feature toggles)?
8. **Precedence for bare O and Y on the Gateway** (`:381`/`:391`) vs a menu item's O — which wins, and does the
   loser get re-lettered or re-bound? **Must honour RQ-28 (`:5269-5271`).**
9. **Fate of B/P/T/D** (`:541-544`) — **note the verifier correction: they are NOT dead code; they fire on
   Company Select once a company has been opened. Re-letter or keep — do not delete.** Decide inside this slice,
   or a future slice that "fixes" the quick letters creates four collisions at once (B=Banking/Balance Sheet,
   P=Profit & Loss/Payroll Reports, T=Trial Balance, D=Day Book). Reconcile the `"C"` Cost Centres badge
   (`:5275`) too.

**🔴 CROSS-POINT CONFLICT — WI-9 vs WI-2.** WI-2 (point 3) wants typing a letter to **FILTER a dropdown**; WI-9
(point 7) wants typing a letter to **ACTIVATE a menu option**. **These are the same keystroke on the same widget
wherever a cascade column doubles as a picker** — `BuildLedgerBookPickerColumn` (`:1390-1408`) is *literally a
menu column of `MenuItemViewModel` built from DB ledger names*. **A single rule must cover both**, e.g. "fixed
authored menus → hotkey activate; data-driven picker columns → type-ahead filter". **WI-2 and WI-9 should be
designed together.** Doing WI-9 first and WI-2 later risks re-litigating the keystroke contract.

---

# WI-10 — Multiple units per item + conversion (Dozen = 12, kg = 1000 g)

**Covers point 12.** **State: PARTIAL.** **Effort: XL** (slices A+B alone are ~M and carry no schema).

> ## ✅ STATUS UPDATE — Slices A, B and C SHIPPED (CA slice S8). Slice D DEFERRED.
>
> Everything below this box is the original find-only analysis and is preserved verbatim. This box
> records what actually landed, so a future session does not redo it.
>
> **A — direction bug FIXED.** `Unit.cs` `BaseMeasureUnitId` now returns `TailUnitId ?? Id` (was
> `FirstUnitId`). Grounded in TWO independent corpus PDFs (Tally Prime Book "Doz (Dozen) of 12 Nos",
> First = "Dozen" / Second = "Numbers"; Study Guide First/Factor/Second table Dozen-12-Pcs,
> Kg-1000-Grams, Box-20-Pcs) ⇒ First is the LARGER unit, so scaling by the factor lands in the TAIL.
> **Both backwards tests rewritten** (`StockMovementTests.cs` built "Dozen" as first=Nos/tail=Box;
> `InventoryMastersViewModelTests.cs` as first=Nos/tail=Pcs). **Gap 7 guard added** at
> `InventoryPostingService.EnsureReferencesResolve`: a line unit whose `BaseMeasureUnitId` ≠ the item's
> `BaseUnitId` is rejected. That guard is what proved the old test encoded the wrong model — with the
> guard in and the test unchanged it failed with *"Inventory line for 'Egg' states its quantity in
> 'Dozen', which does not reduce to the item's base unit 'Nos'."*
>
> **B — line unit REACHABLE.** `InventoryVoucherLineViewModel` gained `UnitOptions` (the item's base unit
> + every compound reducing to it), `SelectedUnit`, `ShowUnit`, `UnitId` and `ParsedQuantityInBaseUnit`;
> `InventoryVoucherEntryViewModel` passes `company.Units` and stamps `l.UnitId` at all four
> `InventoryAllocation` sites. **NO SCHEMA CHANGE WAS NEEDED** — `inventory_allocations.unit_id` already
> exists at CreateV1 and `InventoryAllocationDto.UnitId` already round-trips (`CanonicalMapper.cs:1003`);
> proven, not assumed, by a reload-from-.db assertion. Store stays **v45**.
> **Two stale un-normalised sums fixed:** `SumBase` (the live balance strip) and the Accept-time
> Stock-Journal balance pre-check both summed raw quantities while *labelling them "(base unit)"*.
>
> **C — 🔴 RATE SEMANTICS SETTLED: the rate is PER THE LINE UNIT DISPLAYED.** "2 Doz-Nos @ ₹10" = **₹20**,
> not ₹240. Corpus support (the corpus does NOT state it for a compound line, but nothing contradicts it
> and two things point the same way): Tally's invoice line carries an explicit **"per"** column naming the
> rate's unit (`719244897-Tally-Book.pdf`, "Quantity | Rate per | Amount"), and its worked example reads
> Quantity 2 · Rate 10,000 · Amount 20,000 — `Amount = Quantity × Rate` with the quantity in the unit
> shown; likewise `703679456`: "purchased 10 nos … for **6000 per piece**".
> **This required a real engine fix, not just a decision.** Valuation paired the NORMALISED base quantity
> with the RAW rate, so "2 Doz @ ₹10" would have valued at 24 × ₹10 = **₹240** — exactly the 12× error this
> document warned about. New `Unit.RateInBaseMeasure(rate)` = `rate × den / num` (the exact inverse of
> `QuantityInBaseMeasure`, so `qty × rate` is invariant) is now applied at every site that multiplies a
> normalised quantity by a rate: `StockValuationService` (both allocation loops), `InventoryRegisters`,
> `JobWorkReports`, `AdditionalCostApportionment`.
>
> **D — DEFERRED (not dropped).** Per-item Tally **Alternate Units** (`StockItem.AlternateUnitId` + factor,
> `Company.UseAlternateUnits` F12 flag, schema **v45→v46** with CreateV1 + `MigrateV45ToV46` parity +
> downgrade + `StockItemDto` fold-in + ER-13, and the gated item-master sub-form). Reasons, in order:
> 1. **R7 gate.** Alternate Units is **absent from `docs/tally-feature-catalog.md`** (see the 🔴 CATALOGUE
>    GAP above). The catalogue must be corrected first, and that is a requirements change → R12 user gate.
> 2. **The CA's ask is already answered functionally.** An item measured in Nos can now be transacted in
>    Nos *or* Doz-Nos, and one in grams in g *or* Kg-g — i.e. "multiple units for the same item", with
>    exactly the CA's two examples (1 dozen = 12, 1 kg = 1000 g) working end to end. This document's own
>    inference ("the CA most likely hit the fact that he could not ENTER a voucher line in dozens") is
>    now satisfied, and its own recommendation was "Ship A+B, demo to the CA, and let their reaction
>    decide … that avoids a migration on a guess."
> 3. **An unsettled interaction.** If an item carries BOTH an alternate unit and a compound unit reducing
>    to its base, which appears in the picker, and does the alternate unit follow the same per-line-unit
>    rate rule? The corpus has **3 hits total** on the feature (one PDF) and shows **no invoice arithmetic**
>    for it — so this must be grounded (A14) before it is built, not guessed at the end of a slice.
>
> **Still open after this slice:** **Gap 2** (Sales/Purchase INVOICE lines still have no `UnitId` —
> `VoucherInventoryLine`; that is the schema-bearing half) and the **GSTR-1 UQC** risk at `Gstr1.cs:584`,
> which stays latent only because invoice lines remain base-unit by construction. **Ship those together.**

### Raw wording (verbatim fragments)

> "Multiple units should be allowed for same item" · "1 dozen should be equal to 12, 1kg should be equal to
> 1000g" · "and all same logics of unit conversion"

### Decoded requirement

A stock item must be transactable in **more than one unit of measure**, with the app converting **automatically
and exactly**, so a user can buy/sell/transfer in whichever unit is natural while on-hand, valuation and reports
stay in one canonical unit. Three separable parts:

- **12-a — compound units:** define a compound unit as a relation between two existing simple units with a
  conversion factor ("Doz of 12 Nos"). **This already exists.**
- **12-b — transact in a chosen unit:** a voucher line must let the user **pick the unit its quantity is stated
  in** and have the engine normalise it. **This is the operative half — and it is genuinely missing from the UI.**
- **12-c — per-item Alternate Unit:** Tally's F12-gated "Alternate Units" on the Stock Item master (Units: Nos,
  Alternate Units: Box, "1 box = 10 nos"). **Absent.**

### ⚠️ GENUINE AMBIGUITY — the phrase and the examples point at TWO DIFFERENT Tally features

- **"for the SAME ITEM"** maps to Tally's **Alternate Units** — per-item, F12-gated. Literally "multiple units for
  one item".
- **The examples ("1 dozen = 12", "1 kg = 1000 g")** are **verbatim the Compound Unit examples**
  (`docs/tally-feature-catalog.md:232`) — a **company-wide unit MASTER, not an item property. Compound units
  already exist in this codebase.**

**These are distinct features with distinct masters, schema and UI.** A CA saying "1 dozen should be 12" most
likely hit the fact that **he could not ENTER a voucher line in dozens** (12-b, genuinely missing) rather than
that the unit master lacked compound support (it has it). **But that is inference, not fact — ask.**

### Tally fidelity target — ✅ BOTH GROUNDED

**A. Compound Unit** (`tally/664311548-Tally-Prime-Book.pdf`, author-marked **Page 20**, verified verbatim):
> "A Compound Unit is a relation between two Simple Units. Hence, before you create a Compound Unit, ensure that
> you have already created two Simple units. For example, To Create Compound unit — Doz (Dozen) of 12 Nos
> (Numbers), you have to create two simple units, Doz (Dozen) and Nos (Numbers) and set the conversion factor as
> 12."

Fields: **Type** = Compound; **First Unit** = "Dozen"; **Conversion** = "12"; **Second Unit** = "Numbers" — "This
unit is also called **Tail Unit**". Accepted with **Ctrl+A**.
**⇒ Tally's invariant is: `1 × FirstUnit = Conversion × TailUnit` (1 Dozen = 12 Nos).** The FIRST unit is the
LARGER unit; the compound is a **derived relation between two pre-existing simple units**.

**B. Alternate Units** (`tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf`, verified verbatim): "Press **F12:
Configure : Uses Alternate Units for Stock Items** Set to Yes"; then on the item: "3. Units : Nos / 4. **Alternate
Units : Box** / 5. In Where field type (**1 box = 10 nos**)". Default elsewhere: "4. Alternate Units : Not
Applicable". *(Independently confirmed: this is the **only** one of the 10 corpus PDFs that covers the feature —
3 hits; the other 9 have zero.)*

**Catalogue:** `docs/tally-feature-catalog.md:231-232` — "**Units of Measure:** **Simple** (Symbol, Formal Name,
UQC for GST, decimals 0–4) or **Compound** (First Unit × Conversion Factor + Second/Tail Unit), e.g. Dozen = 12
Pcs, Kg = 1000 g, Box = 20 Pcs."
**Requirements:** `docs/phase3-inventory-requirements.md:90-92` — RQ-4 Compound Unit: "Conversion SHALL be exact
(integer-factor arithmetic, no float) and reversible for display in either unit."

**🔴 CATALOGUE GAP (R7).** `grep -i alternate` over `docs/tally-feature-catalog.md` **and**
`docs/tally-feature-catalog-verification-report.md` returns **nothing relevant** (only an unrelated "alternate
label" note about Bank OD A/c at verification-report:25). **Alternate Units is a real Tally feature present in the
PDF corpus but ABSENT from the requirements catalogue — so it was never planned or built. Per R7 the catalogue
must be corrected before this is implemented.**

**🟡 UNVERIFIED:** whether Tally permits a **decimal** conversion factor; whether the compound's **name is always
derived** ("Doz of 12 Nos") or user-typed; how Tally reports a line entered in an alternate/compound unit in
**GSTR-1 UQC** terms.

### 🟢 ALREADY BUILT — do NOT rebuild

**The compound unit MASTER, end-to-end:**
- `src/Apex.Ledger/Domain/Unit.cs:17-172` — `Unit.Simple(...)` and `Unit.Compound(id, symbol, formalName,
  firstUnitId, tailUnitId, conversionNumerator, conversionDenominator = 1)`; validates first ≠ tail (`:118-119`),
  factor > 0 (`:120-123`). Doc: "a first (base) unit × an exact integer conversion factor + a tail unit (e.g.
  \"Dozen = 12 Nos\", \"Box = 20 Nos\", \"Kg = 1000 g\")".
- `src/Apex.Ledger/Services/InventoryService.cs:158-180` — `CreateCompoundUnit`; components "must exist and be
  **simple** units".
- **Schema already has every column:** `Schema.cs:1034-1046` (CreateV1) — `units(… is_compound, uqc,
  decimal_places, first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator)`.
- **Io lossless:** `CanonicalModel.cs:791-803` `UnitDto` carries all four compound fields.
- **Persistence:** `SqliteCompanyStore.cs:2777-2780` read / `:5430-5433` write.
- **UI exists and is reachable:** `UnitMasterViewModel.cs:62` `_isCompound`, `:71-73` FirstUnit/TailUnit/
  ConversionFactorText, `:157-209` `CreateCompound()`; rendered at `MainWindow.axaml:4835-4914` (Simple/Compound
  radio, First unit / Conversion / Tail unit), wired at `MainWindowViewModel.cs:2749-2751` via `Screen.UnitMaster`.

**Engine conversion, on inventory vouchers:**
- `src/Apex.Ledger/Domain/InventoryAllocation.cs:43` `public Guid? UnitId { get; }`; `:40-42` — "The unit the
  Quantity is expressed in (a simple or compound unit). null ⇒ the item's base unit. **The engine converts to the
  base unit before accumulating on-hand.**"
- `Unit.cs:137-142` `QuantityInBaseMeasure` → `quantity * ConversionNumerator / ConversionDenominator`.
- **7 call sites normalise** (list verified exact and complete): `InventoryLedger.cs:222`,
  `StockValuationService.cs:431`, `AdditionalCostApportionment.cs:221`, `JobWorkService.cs:287`,
  `Reports/InventoryMovements.cs:159`, `Reports/InventoryRegisters.cs:197`, `Reports/JobWorkReports.cs:210`.
- **Persisted + Io:** `Schema.cs:1143` (CreateV1) `inventory_allocations.unit_id TEXT NULL REFERENCES units(id)`;
  `CanonicalModel.cs:1706` `InventoryAllocationDto.UnitId`.

### The gaps

**🔴 GAP 1 — the conversion path is UNREACHABLE from the app.** `unitId` is the 7th, optional, defaulted param
(`InventoryAllocation.cs:45-52`, `Guid? unitId = null`). **Only TWO Desktop files reference `InventoryAllocation`
at all, and EVERY call site omits it**, stopping at `l.Batch`: `InventoryVoucherEntryViewModel.cs:365-366`,
`:522-523`, `:535-536`, `:539-540`; `MaterialMovementEntryViewModel.cs:325-326`, `:329-330`.
⇒ **`UnitId` is ALWAYS null from the UI; every line is interpreted as the item's base unit. Compound conversion
is live engine code with no user-facing entry point.**
**🔑 Verifier addition — the codebase self-documents this, and it is the strongest single citation:**
`InventoryVoucherEntryViewModel.cs:437` — `sum += l.ParsedQuantity; // lines are entered in the item's base unit
(no compound-unit UI yet)`. *(Related latent doc defect: `:432` claims "Σ of the complete lines' base-unit
quantities (compound units normalised via the engine)" — but `SumBase` does **no** normalisation. Stale/wrong.)*

**🔴 GAP 2 — sales/purchase INVOICE lines have no unit concept at all** (where "bill in dozens" matters most):
`VoucherInventoryLine.cs:24-101` has **no** `UnitId`; `:33` — "The **Actual** movement quantity (> 0), **in the
item's base unit** (6-dp)". No `unit_id` column. `CanonicalModel.cs:1612-1624` `VoucherInventoryLineDto` — no
UnitId.

**🔴 GAP 3 — Alternate Units NOT IMPLEMENTED.** `grep -Ei "alternate\s*unit|AlternateUnit|AltUnit|dual.?quantity|
second.?unit"` over the whole worktree → **zero matches** (only `Gstin.cs:65` "alternate positions" and
verification-report:25). An item carries exactly ONE unit: `StockItem.cs:33` `public Guid BaseUnitId { get; set; }`
("The base Unit of measure; required"); `StockItemMasterViewModel.cs:121` `Units` + `:619` `SelectedUnit` = one
pick.

**🔴 GAP 4 — LATENT SEMANTICS BUG.** `Unit.cs:146` `public Guid BaseMeasureUnitId => FirstUnitId ?? Id;`
**contradicts `QuantityInBaseMeasure`**, which scales **by** the numerator and therefore yields **TAIL** units.
Per Tally (1 Dozen = 12 Nos; first=Dozen, tail=Nos), ×12 produces Nos = the **TAIL** unit ⇒ **this must be
`TailUnitId`.** Currently **no call sites** (only the definition), so it is dormant — but any future consumer
inherits it.
**🔑 Verifier corroboration the decoder missed (this settles the direction):** `Unit.cs:64-65` — the
`ConversionNumerator` doc says "**how many tail units are in one first unit — the '12' in '1 Dozen = 12 Nos**'";
and `Schema.cs:1044` says "compound: **tail-per-first factor** numerator". **Both independently confirm ×numerator
yields TAIL units.**

**🔴 GAP 5 — the compound engine test encodes the WRONG model.**
`tests/Apex.Ledger.Tests/Inventory/StockMovementTests.cs:221-222`:
```csharp
// Compound "Dozen": first = Nos (base measure), tail = Box (distinct), 1 Dozen = 12 Nos.
var dozen = masters.CreateCompoundUnit("Dozen", "Dozen", nos.Id, box.Id, 12);
```
first=**Nos**, tail=**Box** — under Tally this reads "1 Nos = 12 Box", which is nonsense; "Box" is arbitrary
filler chosen only to satisfy first ≠ tail and **plays no part in the arithmetic.** It passes only because ×12
coincides and `BaseMeasureUnitId` is never consulted. **So the regression lock protects the wrong invariant.**

**GAP 6 — the UI contradicts the test's own model.** `UnitMasterViewModel.cs:204` — `Message = $"Compound unit
'{symbol}' created (1 {symbol} = {factor} {tailSymbol})."` reports the **TAIL** as the target (Tally-correct),
while `:174` ("Pick a first (base) unit and a tail unit") and the test treat **FIRST** as the base. **Built the
test's way, the app would print "1 Dozen = 12 Box".**

**GAP 7 — no compatibility validation.** `InventoryPostingService.cs:245-246` checks only **existence**:
`if (a.UnitId is { } uid && _company.FindUnit(uid) is null) throw … "Inventory line references unknown unit"`.
**Nothing checks the unit reduces to the ITEM's base unit** — "1 Kg" of a Nos-based item would silently scale.
Unreachable today (Gap 1); **becomes live the moment a unit picker ships.**

**GAP 8 — fractional factors** are structurally supported (`ConversionDenominator`) but UI-inaccessible —
`UnitMasterViewModel.cs:182-183` parses `NumberStyles.Integer` only.

### ⚠️ Verifier corrections — one is ACTIONABLE and would CORRUPT THE SCHEMA

1. **🔴 THE DECODE INVERTS `CreateV1` AND THE FROZEN MIGRATIONS. This is the most dangerous error in this
   document.** `Schema.cs:109-112` self-documents: **CreateV1 (lines 114 → 1679) IS the full current v44 DDL** —
   "a fresh DB is stamped straight to the current version, so this DDL already includes every column/table added
   by later migrations". **Migrations follow from `:1687`.** Therefore:
   - `":1908-1920 (units, current v44)"` is **WRONG** — that is `MigrateV8ToV9` (1890-1978), **frozen history**.
   - `":2002 (inventory_allocations.unit_id, current)"` is **WRONG** — that is `MigrateV9ToV10` (1979-2042).
   - **`":2056-2066 (voucher_inventory_lines)"` labelled CreateV1 in BOTH the touches list AND the proposal is
     WRONG — that is `MigrateV11ToV12` (2055-2079).** The **REAL** CreateV1 definition is **`Schema.cs:1180-1198`**
     and carries **MORE** columns than the quoted list (`batch_id`, `actual_qty_micro`, `billed_qty_micro`).
   **⇒ Slice C's instruction "add `unit_id` to CreateV1 (`Schema.cs:2056-2066`)" would send an implementer to
   edit a FROZEN v11→v12 migration — corrupting migration replay and `SchemaMigrationEquivalenceTests` — while
   leaving the real CreateV1 untouched. CORRECT TARGETS: `Schema.cs:1180-1198` (CreateV1) + a NEW
   `MigrateV44ToV45`.** *(The substance was right: `units` has every compound column at CreateV1 `:1034-1046`;
   `inventory_allocations.unit_id` is persisted at CreateV1 `:1143`; `voucher_inventory_lines` has **no**
   `unit_id`; no v45 needed for the master.)*
   **⚠️ This warning applies to WI-4 and WI-6's optional Fix 4 as well.**
2. **A SECOND backwards test was missed.** `tests/Apex.Desktop.Tests/InventoryMastersViewModelTests.cs:186-198`
   wires the **same** inversion (Symbol "Dozen", FirstUnit=nos, TailUnit=Pcs, factor 12 ⇒ "1 Nos = 12 Pcs"; the VM
   would print "1 Dozen = 12 Pcs"). **Slice A must rewrite BOTH.**
3. **🔑 A counter-example STRENGTHENS the case.** `tests/Apex.Ledger.Tests/Inventory/InventoryMastersTests.cs:238`
   builds the compound the **TALLY-CORRECT** way: `CreateCompoundUnit("Kg-g", "Kilogram in grams", kg.Id, g.Id,
   1000)` — first=Kg, tail=g. **So the suite encodes BOTH models simultaneously.** This is harder proof of genuine
   confusion than the decoder presented, and it confirms the direction: **fixing `:146` to `TailUnitId` makes this
   test right and `StockMovementTests` wrong.**
4. **MISCHARACTERISATION — do not act on it.** The decode calls Apex's master "a 3-unit shape for a 2-unit
   relation" that "forces the user to pick an **irrelevant** tail". **Per the verified PDF, Tally's compound
   genuinely IS a third record relating two PRE-EXISTING simple units, so first+tail is Tally-FAITHFUL and the
   tail is NOT irrelevant — it is the essential smaller unit.** It only *looks* like filler because
   `StockMovementTests` used it as filler. **The real deviation is narrower:** the PDF's compound screen shows
   only Type / First Unit / Conversion / Second Unit — **no Symbol/Formal Name field** — implying a **derived
   name**, whereas Apex requires a typed Symbol + FormalName. *(The decoder correctly routed this to A14 as an
   open question rather than asserting.)*
5. **Minor provenance slip:** "Type = Compound (default Simple; Shift+Tab moves back a field)" — the fact IS
   grounded, but it sits in the **SIMPLE**-unit section (~pdftotext line 848-850), not the compound Type bullet.
6. **🔑 The GSTR-1 risk is real and now precisely located** (the decoder honestly said "must be re-checked"):
   **`Gstr1.cs:584` — `var uqc = company.FindUnit(item?.BaseUnitId ?? Guid.Empty)?.UnitQuantityCode;`** — UQC
   comes from the item's **BASE** unit while quantity accumulates the **line** quantity. **Safe today only because
   `VoucherInventoryLine.Quantity` is base-unit by construction; ship a line-unit without normalising and GSTR-1
   emits UQC=NOS against a Dozen-denominated quantity.**
7. **A second consumer of the confused model was unmentioned:** `PayrollUnitMasterViewModel` has its own
   Simple/Compound toggle (`MainWindow.axaml:7118-7136`; `MainWindowViewModel.cs:3388-3390`).

### Proposal — slice it; do NOT do it all at once

**A/B deliver the CA's likely intent with NO schema risk. Ship A+B, demo to the CA, and let their reaction decide
whether C and/or D are actually wanted — that avoids a migration on a guess.**

**Slice A — settle the model + fix the latent bug (no schema, S).**
- **Declare Tally's invariant canonical: `1 × FirstUnit = Conversion × TailUnit`** (first = larger, tail =
  smaller/base measure), per the Class-5 PDF **and** corroborated by `Unit.cs:64-65` + `Schema.cs:1044`.
- `Unit.cs:146` → `BaseMeasureUnitId => TailUnitId ?? Id`; correct the `QuantityInBaseMeasure` doc (`:129-136`),
  which currently names `FirstUnitId` as the base measure.
- **Rewrite BOTH backwards tests** — `StockMovementTests.cs:211-238` (build Dozen the Tally way: first=Dozen,
  tail=Nos; drop the filler "Box") **and `InventoryMastersViewModelTests.cs:186-198`**. Add a test asserting
  `BaseMeasureUnitId == Nos`. *(`InventoryMastersTests.cs:238` is already correct and should stay green.)*
- Fix the `UnitMasterViewModel.cs:174` vs `:204` contradiction; reshape the master toward Tally **pending Q5**.
- **Add the compatibility guard at `InventoryPostingService.cs:245-246`:** the line unit's `BaseMeasureUnitId`
  must equal the item's `BaseUnitId`, else reject. **This must land in the SAME slice as any picker, not after.**

**Slice B — expose the unit on inventory-voucher lines (no schema, M).** Schema (`inventory_allocations.unit_id`)
and Io (`InventoryAllocationDto.UnitId`) **already support this; only the UI is missing.**
- Add a Unit picker per line in `InventoryVoucherEntryViewModel` and `MaterialMovementEntryViewModel` (default =
  item base unit; choices = base unit + compounds reducing to it), pass it as the 7th arg at the six sites listed
  in Gap 1.
- Add the column to `MainWindow.axaml`. **UI-defect warning:** starved `*` columns and horizontal StackPanels
  killing TextWrapping in this 14,785-line / 736-ColumnDefinition file — **render-verify headlessly**, not just
  unit-test.
- Fix the stale doc at `InventoryVoucherEntryViewModel.cs:432` and the self-documenting comment at `:437`.

**Slice C — unit on INVOICE lines (SCHEMA v45, L).**
- `VoucherInventoryLine.cs`: add `Guid? UnitId`; normalise via `QuantityInBaseMeasure` wherever stock accumulates.
- **Schema: `voucher_inventory_lines.unit_id TEXT NULL REFERENCES units(id)` in BOTH the REAL CreateV1
  (`Schema.cs:1180-1198` — NOT `:2056-2066`, see correction 1) AND a new `MigrateV44ToV45`**, with
  `SchemaMigrationEquivalenceTests` parity.
- Io fold-in: `CanonicalModel.cs:1612-1624` + `CanonicalMapper` + `CanonicalXml`, keeping null ⇒ byte-identical
  round-trip (the **ER-13 pattern `BilledQuantity` already uses** at `CanonicalModel.cs:1621-1623`).
- **🔴 RATE SEMANTICS DECISION:** `VoucherInventoryLine.Value => Rate × BilledQuantity` (`:103-106`). **If qty is
  in Dozen the rate must be per Dozen — otherwise every invoice total is wrong by the factor.** Must be settled
  and regression-locked **before** any picker ships.
- **🔴 GST/UQC:** GSTR-1 must report the item's UQC quantity, not the entered unit — **`Gstr1.cs:584`** (see
  correction 6).

**Slice D — Alternate Units (SCHEMA v45, L; CATALOGUE FIX FIRST).**
- **Per R7, first correct `docs/tally-feature-catalog.md` §9** to document Alternate Units from
  `703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf` (currently absent).
- `Company.UseAlternateUnits` F12 flag — follow the existing precedent `Company.cs:216
  UseSeparateActualBilledQuantity`.
- `StockItem.cs:33`: add `AlternateUnitId` + per-item conversion numerator/denominator; `stock_items` columns in
  **the real CreateV1** + `MigrateV44ToV45` + `StockItemDto` fold-in; dual-quantity columns in the item master and
  invoice UI.

### Risk

- **🔴 Financial-correctness (highest).** A line unit on an invoice without settled rate semantics **silently
  misprices by the conversion factor** — "2 Dozen @ ₹10" is ₹20 or ₹240 depending on the reading. **A 12× error
  on every invoice total, flowing straight into GST.**
- **🔴 GST/UQC.** `Gstr1.cs:584` takes UQC from the item's base unit while quantity accumulates the line quantity.
  **A line billed in Dozen could report "2" where "24" is due — a filed-return defect.**
- **🔴 Direction risk — LIVE NOW.** Gaps 4-6: `BaseMeasureUnitId => FirstUnitId` is inconsistent with the
  arithmetic, and the suite **encodes both models**. **Anyone building a unit picker on today's code has a 50%
  chance of wiring it backwards, and the existing test will NOT catch it — it currently passes on the wrong
  model.** Same shape as the "3 silent under-reversals" and inverted-CDN bugs A10 caught on prior slices.
- **🔴 Schema-edit-target error** (correction 1) — would corrupt migration replay.
- **🟠 Unguarded scaling.** `InventoryPostingService.cs:245-246` validates existence only. **The moment a picker
  ships, "1 Kg" of a Nos item scales silently.**
- **🟠 Schema (v44 → v45) for C and D.** Every new column in **BOTH** the real CreateV1 **AND**
  `MigrateV44ToV45`, with parity tests **and** an Io fold-in or JSON/XML goes lossy. Null-default so an existing
  company round-trips byte-identically (mirror `BilledQuantity`). **`units.first_unit_id`/`tail_unit_id` are
  self-referential FKs into `units(id)`** — a Phase-9 CRITICAL showed a dangling FK can make a company permanently
  unsaveable; **insert ordering matters.**
- **🟠 UI risk** — a new grid column in the shared file, mid-campaign.
- **🟠 Catalogue/R7 risk** — building Slice D against a PDF read alone violates R7. **Correct the catalogue first
  (A14).**
- **🟠 Scope risk** — the three readings differ by ~5× in cost. **Building D when the CA only meant B wastes a
  slice.**
- **🟢 De-brand (hard rule):** corpus text quoted here ("Gateway of Tally", "Tally Prime") must **never** reach
  shipped UI/code. Labels must read "Units", "Compound", "First Unit", "Conversion", "Second Unit", "Alternate
  Units".

### Open questions

**For the USER / CA (blocking — these change cost ~5×):**
1. **🔴 Which failure did you actually hit?** (a) **couldn't create "Dozen = 12 Nos" as a unit** — it **EXISTS**
   today, so this would mean the form is confusing/unusable rather than absent; (b) **couldn't type a voucher line
   in Dozen** — **CONFIRMED missing (Gap 1), cheap to fix**; (c) **need ONE item to carry two units at once**
   (Nos + Box) with dual quantity columns — **Tally's Alternate Units, confirmed missing, needs schema.**
   **The answer decides Slice B vs C vs D.**
2. **Must this work on Sales/Purchase invoices** (needs schema v45 + rate semantics), **or only on stock/inventory
   vouchers** (no schema)?
3. **🔴 If a line is entered as "2 Dozen @ ₹10", is ₹10 per Dozen or per Nos?** *(Tally's answer is per the unit
   entered — **but this was NOT grounded in the corpus, so it must be confirmed, not assumed. It is the single
   highest-value answer here.**)*
4. **Do you need FRACTIONAL conversion factors** (1 Box = 2.5 Kg)? Structurally supported
   (`ConversionDenominator`), UI-blocked today (`UnitMasterViewModel.cs:182-183`).

**For A14 / R7:**
5. **Confirm Tally's compound-unit NAMING** — is the name always derived ("Doz of 12 Nos"), or can the user type
   an arbitrary symbol? **This decides whether `UnitMasterViewModel`'s typed Symbol + First + Tail shape is a
   defect or an accepted deviation — and it is the root of the backwards test.**
6. **Does Tally permit a DECIMAL conversion factor?** 🟡 UNVERIFIED.
7. **How does Tally report a line entered in an alternate/compound unit in GSTR-1 UQC terms?**
8. **Should `docs/tally-feature-catalog.md` §9 be amended to document Alternate Units** (present in the PDF
   corpus, absent from the catalogue) **before any build?** **Recommend yes, per R7.**

**Engineering (decide before code):**
9. **Adopt `1 × First = Conversion × Tail` as canonical and fix `Unit.cs:146` + BOTH backwards tests?**
   **Recommend yes — but note this changes a GREEN test, so it needs an explicit decision, not a silent edit.**

---

# WI-11 — Y/N Accept confirmation (Ctrl+A accept-as-is already works)

**Covers point 13.** **State: PARTIAL — Ctrl+A is comprehensively implemented; the Y/N confirm is absent.**
**Effort: M.**

### Raw wording (verbatim fragments)

> "while creating" · "ledger, item, group etc" · "a confirmation should be incorporated" · "if true y or else n
> should be short cut" · "accepting the entry made without further details"

### Decoded requirement — two DISTINCT things bundled in one point

- **(13-A) CONFIRMATION STEP.** On master creation (and by implication alteration), saving must present a
  terminal **"Accept?"** confirmation rather than committing silently, answered by **Y** = yes/save, **N** =
  no/don't save.
- **(13-B) ACCEPT-AS-IS.** While creating a ledger/item/unit, the user must be able to accept **without filling
  in the remaining optional fields**, via **Ctrl+A**, from whatever field the cursor is in.

**Read against Tally: Ctrl+A is the BYPASS (immediate save, no prompt), and the Y/N Accept prompt is the TERMINUS
of the Enter path. They are two routes to the same save, not a sequence.** The naive reading ("Ctrl+A → then
confirm Y/N") is **both non-Tally and test-breaking** — see risk 1.

### Tally fidelity target — ✅ GROUNDED (both mechanisms real, and ALTERNATIVES)

- **🔑 The decisive sentence** — `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:2035`: "After providing all required
  details, **choose Yes option under Accept OR press Ctrl+A** to save the screen." **The "or" establishes that
  Ctrl+A BYPASSES the prompt rather than raising it.** This settles the point's central ambiguity.
- **An Accept prompt terminates the Enter path** — `...:1847-1848`: "If you have entered all the details and after
  verifying it seems to be right, then you have to accept the screen by pressing Enter and again Enter to accept
  and save the details." *(⚠️ **Verifier scoping note:** this sits in a **COMPANY-creation** walkthrough —
  `:1849` continues "After saving the company, takes you to the Company Features screen". It grounds the
  Enter-walk/terminal-confirm shape, **not its universality across masters.** The decoder self-flagged this as
  openQuestion 7, so it is honest rather than unsupported — but label it company-specific.)*
- **Ctrl+A applies to ALTERATION too** — `tally/664311548-Tally-Prime-Book.pdf:731`: "After Changes information in
  Group Press `Ctrl+A' for Accept changes". *(Bears on the creation-only-vs-alteration question.)*
- **Enter and Ctrl+A are both accepted on masters** — `...:508`: "After Filling all Option Press `Ctrl+A' or
  Enter"; `:756`: "After filling all details in ledger screen Press Ctrl+A to save."
- **Catalogue:** `docs/tally-feature-catalog.md:168` — "**Universal save:** **`Ctrl+A`**. Delete voucher:
  `Alt+D`. Cancel voucher: `Alt+X`."; `:467` (§21) — "`Ctrl+A` Accept/Save". *(§21 is self-marked "(reconciled —
  ⚠verify against current build)" at **`:462`** — so it is not authoritative alone; the PDFs corroborate it.)*
  `docs/tally-feature-catalog-verification-report.md` has **no** correction touching Ctrl+A or the Accept prompt.

**🔑 Two corroborating sources the decoder MISSED (both strengthen 13-A):**
- **`tally/719244897-Tally-Book.pdf:3003` — `ACCEPT ? YES`** — a **verbatim on-screen rendering** of the Accept
  prompt with YES as the supplied answer. Stronger than the study guide's prose. *(Caveat: context is the company
  **backup** flow, `:2996-3001` — it grounds the prompt's wording/form, not universality on masters.)*
- **`tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf:6881-6883`** — "Tally Prime will ask you to \"**Delete Yes or
  No?**\" / Supply Yes to Delete / Tally Prime will ask your confirmation \"**Are you sure Yes or No?**\" /
  Supply Yes to Confirm it". **This GROUNDS a Yes/No confirm on DELETE** — bearing directly on Q5, which the
  decoder scoped deletion OUT of.

**🔑 The "source conflict" the decoder flagged actually DISSOLVES INTO CORROBORATION.** The decoder discounted
`tally/659947760-Tally-Prime-Short-Key.pdf:17` ("Ctrl+A → Zoom") and `:19` ("Alt+A → Save") as scrambled — and it
correctly grounded the contradiction in the catalogue (`:464-465`) rather than from memory. **But the scramble is
not random: it is a DETERMINISTIC +2 label shift** (the key at row N pairs with the label at row N+2). Verified
against five knowns: row 1 Alt+G → row 3 "Go To"; row 2 Ctrl+G → row 4 "Switch To"; row 3 Alt+K → row 5 "Company
Menu"; row 10 F3 → row 12 "Change Company"; row 12 Ctrl+F3 → row 14 "Shut Company" — **all five match catalogue
`:464-465`.** Apply the same shift to row 17 (Ctrl+A) → **row 19 label = "Save"**. **De-scrambled, the file says
Ctrl+A = Save.** The conflict is an extraction artifact; **the decoder's conclusion is right and BETTER supported
than it claimed.** Its warning to future greppers stands.

**🟡 UNVERIFIED — the single-letter Y/N KEYSTROKE.** The prompt is grounded as a **Yes/No field you "choose Yes
option under"**. **No corpus text states that bare `Y` accepts and bare `N` declines as keystrokes.** The Y/N
*prompt* is grounded; the Y/N *accelerator* is not. **Must be checked against a live TallyPrime build before
implementing, since the user explicitly asks for Y/N shortcuts.** *(If live Tally uses a highlighted Yes/No
**field** rather than single keystrokes, the design changes.)*

### 🟢 (13-B) Ctrl+A — ALREADY IMPLEMENTED, comprehensively. Do NOT re-derive this.

- `MainWindow.axaml.cs:20-21` registers the handler at the **tunnelling** stage — "Handle keys at the tunnelling
  stage so arrow/Enter/Esc work regardless of focus." **This is exactly the "accept from any field without
  further details" semantic** — the shortcut fires before the focused TextBox sees the key.
- `MainWindow.axaml.cs:72-74` — `// Ctrl+A saves/accepts (accept shortcut)` then `if (e.Key == Key.A &&
  e.KeyModifiers.HasFlag(KeyModifiers.Control))`. Special-cases ~15 screens, then falls through at `:111` to
  `else vm.ActivateSelected();`.
- `MainWindowViewModel.cs:4556` `public void ActivateSelected()` — a switch routing ~40 screens to their commit:
  `case Screen.LedgerMaster: LedgerMaster?.Create();` (`:4569-4570`), `Screen.StockItemMaster` (`:4590`),
  `Screen.UnitMaster` (`:4584`), `Screen.StockGroupMaster` (`:4578`), `Screen.VoucherEntry: VoucherEntry?.Accept()`
  (`:4563-4564`), etc. Also `:4235` `public void AcceptCurrent() => ActivateSelected();`.
- **Proven live:** `tests/Apex.Desktop.Tests/HeadlessMainWindowTests.cs:121` —
  `window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // Ctrl+A creates the ledger`, with
  `:122-123` asserting the ledger exists; and `:141`/`:142` asserting a voucher posts and the screen returns to
  Gateway.
- **Advertised across the UI:** the committed `MainWindow.axaml` has **71 "Ctrl+A" occurrences across 52+ distinct
  buttons** — "Create (Ctrl+A)" ×22 (e.g. `:168`), "Accept (Ctrl+A)" ×7 (e.g. `:2758`), "Save (Ctrl+A)" ×5 (e.g.
  `:6505`), "Export PDF"/"Apply" ×4 each, … *(The decoder said "~30+" — conservative, not wrong.)*

### (13-A) Y/N confirmation — NOT IMPLEMENTED. Searched and found nothing.

- **`Key.Y|Key.N` across `src/`** → exactly **2 hits, both unrelated**: `MainWindow.axaml.cs:391`
  (`if (e.Key == Key.Y && vm.CurrentScreen == Screen.Gateway` — Y opens Export Data) and `:154`
  (`case Key.N: vm.OpenAutoColumns();`, under an Alt guard).
- **`ConfirmScreen|PendingConfirm|ShowConfirm|IsConfirm|ConfirmPrompt|MessageBox|Dialog|Window.Show` across
  `src/`** → **zero matches. There is no confirmation/dialog infrastructure of any kind in the application.**
  *(Independently re-run with fresh terms — `confirm|prompt` returns only doc-comment noise and "A14-CONFIRMED"
  tags; `ShowDialog|new Window|Popup|Flyout|ContentDialog|OverlayPanel` → zero.)*
- **`Accept\?|Are you sure|"Yes"` in `MainWindow.axaml`** → zero. *(The 5 `"Yes"`/`"No"` hits across
  `src/Apex.Desktop` are all **report cell VALUES**, not prompts — e.g. `BonusRegisterViewModel.cs:121`
  `Eligible = r.Eligible ? "Yes" : "No"`; `ScenarioMasterViewModel.cs:160`.)*
- **Only TWO `.axaml.cs` files exist in the whole app** (`App.axaml.cs`, `MainWindow.axaml.cs`) — **there is
  nowhere else for a prompt to hide.**
- **Commits go straight through:** `LedgerMasterViewModel.cs:453` `public bool Create()` validates, sets `Message`
  on failure (`:460` "A ledger name is required.", `:465` "Pick an Under group.", `:470` duplicate-name), and on
  success sets `:611` `Message = $"Ledger '{name}' created under {SelectedGroup.Name}{currencyNote}.";` —
  **no confirmation gate anywhere in the path.**
- **No Y/N or confirm tests exist** in `tests/Apex.Desktop.Tests/`.

**Also relevant — Enter == Ctrl+A today (no field-walk).** `MainWindow.axaml.cs:506-509` —
`case Key.Enter: vm.ActivateSelected(); e.Handled = true; break;`. **So Enter from ANY field commits
immediately; Tally's Enter-walk-then-Accept-prompt does not exist.** This is why the scope question below matters.

### ⚠️ Verifier corrections

1. **🔴 MATERIAL — "group" has no screen to attach a confirm to, AND the existing menu path is a LANDMINE for this
   very slice.** The decoder concluded "no accounting-group creation screen exists" — **the conclusion is correct**
   (no `Screen` value, no VM; searched `AccountGroup|AccountingGroup|LedgerGroup` → zero files; the engine
   supports it — `Group.cs:9`, `Company.cs:521` `AddGroup`, `SeedGroups.cs:10` — but groups are seed-only). **But
   it MISSED that a reachable "Group" menu item exists and IS mis-wired:** `MainWindowViewModel.cs:1060`
   (under `MenuItemViewModel.Header("Accounting Masters")` at `:1058`) → dispatched at **`:4897`
   `case "Group": ShowLedgerMaster(); break;`**. **⇒ Masters → Create → Group SILENTLY OPENS LEDGER CREATION.**
   Two consequences: (a) a future agent told "no Group screen exists" will be confused on finding the menu entry;
   (b) **🔴 a confirm gate keyed to `Screen.LedgerMaster` WILL fire "Accept?" for a LEDGER while the user believes
   they are creating a GROUP.** **The gate design must account for `:4897`.** See **WI-7**.
2. **⚠️ CRITICAL OPERATIONAL CONTEXT (not the decoder's error).** The `MainWindow.axaml` citations `:2758`
   ("Accept (Ctrl+A)") and `:6505` ("Save (Ctrl+A)") appear **WRONG in the live working copy** (they resolve to a
   `<Run Text="TCS Rs "/>` and an `<ItemsControl.ItemTemplate>`, off by exactly **+22** lines). **They are CORRECT
   against the committed file** — `git show HEAD:src/Apex.Desktop/Views/MainWindow.axaml` is **14,785 lines** and
   `:168`/`:2758`/`:6505` match verbatim. **Cause: the worktree carries uncommitted UI-defect-campaign changes**
   (` M src/Apex.Desktop/Views/MainWindow.axaml`, +35/−13; ` M tests/Apex.Desktop.Tests/TestAppBuilder.cs`;
   `?? tests/Apex.Desktop.Tests/ZZRenderProbe.cs` — the render-probe convention), taking the file to 14,807 lines.
   **HEAD = `0d1effd`. Re-resolve axaml line numbers after that work lands.**
3. **Off-by-one:** the §21 "⚠verify" header is at **`docs/tally-feature-catalog.md:462`**, not `:461`. And
   `HeadlessMainWindowTests.cs` "140-141" → the Ctrl+A press is at **`:141`**, the assert
   `Assert.Equal(Screen.Gateway, vm.CurrentScreen);` at **`:142`** (`:140` is a comment). Both point at the right
   code.

### Gap

**Only 13-A is missing.** Specifically:
1. **No confirm state on the VM** — `MainWindowViewModel.cs:4556` commits synchronously inside
   `ActivateSelected()`. No `IsAcceptPromptOpen`, no pending-action indirection.
2. **No Y/N key handling gated to a prompt** — and `Key.Y` (`:391`) / `Key.N` (`:154`) are bound to unrelated
   features and **would COLLIDE**.
3. **No prompt UI** — no Yes/No affordance in `MainWindow.axaml`.
4. **No decision on which accept gesture raises the prompt.** Per Tally: **Enter (`:506`) should raise it;
   Ctrl+A (`:74`) should bypass it.** Today **both call the same `ActivateSelected()` and both commit
   immediately.**

**13-B needs NO work** — Ctrl+A already accepts from any field, on ~40 screens, via a tunnel handler,
regression-locked by `HeadlessMainWindowTests.cs:121`. **Anyone reading point 13 as "Ctrl+A is missing" would
waste a slice.**

**Out of scope for a pure confirm gate but blocking full coverage of the word "group":** WI-7.

### Proposal

**NO SCHEMA CHANGE.** Pure presentation state — nothing persists. v44 untouched; no CreateV1/Migrate pair, no
`SchemaMigrationEquivalenceTests` parity, no `Apex.Ledger.Io` fold-in. **That keeps this slice cheap.**

*(Tally-faithful shape; settles the central ambiguity as "Ctrl+A bypasses" per Study Guide:2035.)*

1. **VM confirm state** — `MainWindowViewModel.cs`, beside `ActivateSelected()` (`:4556`):
   - `[ObservableProperty] private bool _isAcceptPromptOpen;` and `private Action? _pendingAccept;`
   - `public void RequestAccept()` — capture the screen's commit as `_pendingAccept` and set the flag
     (**do NOT commit**).
   - `public void ConfirmAccept()` (Y) — run `_pendingAccept`, clear state. `public void DeclineAccept()` (N) —
     clear state, **leave the form populated for further editing** (per Tally; confirm per Q2).
   - **Reuse the existing switch rather than duplicating it:** refactor `ActivateSelected()`'s body into an
     `Action? ResolveAcceptAction()` so both the prompt path and the Ctrl+A path share **ONE** dispatch table.
     **Duplicating that ~140-line switch is how screens get silently missed.**
   - **Gate the prompt to master/entry screens only.** A ready-made list exists at
     `MainWindowViewModel.cs:4252-4263` (the `CancelVoucher()` screen set) — **reuse it; don't hand-roll a second
     one that drifts.** Read-only reports (`:4666-4681`) must stay a **safe no-op**.
2. **Key wiring** — `MainWindow.axaml.cs`:
   - Handle Y/N **early in `OnKeyDown`, BEFORE `:154` (Alt+N AutoColumns) and BEFORE `:391` (Y = Export Data)**,
     and guard strictly: `if (vm.IsAcceptPromptOpen && !IsTyping(e))`. **Ordering is load-bearing** — the tunnel
     chain is ~450 lines of sequential ifs and **first match wins**.
   - Change `case Key.Enter:` (`:506`) to **raise the prompt on master screens**; leave it as-is elsewhere (it
     also drives cascade navigation via `ActivateSelected`, and the Enter drill at `:66` must keep priority).
   - **Leave Ctrl+A (`:74`) committing immediately** — Tally's bypass, and it preserves the existing tests.
3. **Prompt UI** — `MainWindow.axaml`: an "Accept? Yes / No" bar bound to `IsAcceptPromptOpen`, following the
   `Cmp08ReportViewModel` / **`#Root` binding-path** convention (NOT the `GatewayColumn` `x:DataType` fallback —
   AVLN2000).
4. **Tests** — new `tests/Apex.Desktop.Tests/AcceptPromptTests.cs`: prompt raised; **Y commits**; **N does not
   commit AND preserves field values**; prompt suppressed on read-only reports; **typing "y" into a ledger Name is
   NOT swallowed** (risk 2). Extend `HeadlessMainWindowTests.cs` for **real key dispatch through the window**.
   Render-verify the prompt bar headlessly, then **REVERT `TestAppBuilder`**.

**Before building:** resolve Q1–Q4 with the user, and **verify the bare-Y/bare-N accelerator against a live
TallyPrime build.**

### Risk

- **🔴 Gating Ctrl+A behind the prompt BREAKS EXISTING TESTS *and* TALLY FIDELITY — the trap in this point.**
  `HeadlessMainWindowTests.cs:121-123` presses Ctrl+A and immediately asserts the ledger exists; `:141-142`
  asserts the voucher posted and the screen returned to Gateway. **If Ctrl+A raises a prompt, both fail.** And the
  corpus independently says it should not (`Study Guide:2035`, "choose Yes … **or** press Ctrl+A").
  **The naive reading of point 13 is wrong on both counts.**
- **🔴 Y/N interception vs typing — the highest-probability real bug.** Every master screen has a Name TextBox.
  **Because the handler is a TUNNEL handler (`:21`) it fires BEFORE the focused TextBox**, so an ungated
  `case Key.Y` would make it **impossible to type "y" into a ledger name** ("Yamuna Traders" → "amuna Traders"),
  **silently, on every master.** Must be gated `IsAcceptPromptOpen && !IsTyping(e)`. *(The existing Y handler at
  `:391` already models this: `Screen.Gateway && !IsTyping(e)`.)*
- **🔴 The `:4897` landmine** — a gate keyed to `Screen.LedgerMaster` fires "Accept?" for a **ledger** on the
  **Group** menu path (verifier correction 1).
- **🟠 Handler-ordering fragility.** `OnKeyDown` is a ~450-line sequential if-chain, first-match-wins. The Y/N
  block must precede `:154` and `:391` and must not shadow the Enter drill at `:66`.
- **🟠 Changing Enter's meaning is a ~40-screen blast radius.** Enter (`:506`) currently commits on every screen
  `ActivateSelected()` handles. Gate the prompt to the master-screen set at `:4252-4263` and run the full Desktop
  suite (840 tests).
- **🟠 Duplicating the dispatch switch** ⇒ screens WILL drift out of sync (one silently commits without a prompt,
  or the prompt fires a stale action). **Refactor to one shared table.**
- **🟡 UI-defect campaign collision** — a prompt bar added to panes already catalogued for truncation/overlap
  risks new clipping. **Render-verify; it must not overprint** (cf. the Trial Balance overprint defect).
- **🟡 UNVERIFIED premise** — the bare-Y/bare-N accelerator.
- **🟡 "group" is unbuildable today** — coverage of that word cannot ship until WI-7 does.
- **🟢 NO schema risk.**

### Open questions

**For the user:**
1. **🔴 Does Ctrl+A raise the Y/N prompt, or bypass it and save immediately?** **Tally says BYPASS
   (Study Guide:2035). Recommend bypass. Confirm — this is the point's central fork.**
2. **What does N do?** Return to the form with values preserved (Tally), or discard? **Recommend return-to-form**
   — Esc/Alt+X already abandons (`MainWindowViewModel.cs:4238` `CancelVoucher`).
3. **🔴 Full Tally Enter field-walk, or just a confirm gate on the existing accept?** Apex's Enter commits
   immediately from any field today (`:506`); Tally's Enter walks field-to-field and only prompts at the end.
   **Confirm gate = M. Full field-walk across ~40 screens = XL and is really a separate campaign.
   The M estimate assumes the confirm gate.**
4. **Creation only, or alteration too?** The corpus shows Ctrl+A accepting alterations identically (Book:731), and
   WI-3 implies alteration parity.
5. **Should DELETION get a confirm too?** Point 13 says only "creating", so deletion is scoped **OUT** — **but the
   verifier found the corpus grounds exactly this** (`Study Guide:6881-6883`, "Delete Yes or No?" / "Are you sure
   Yes or No?"). Confirm.

**For the CA / A14 (fidelity):**
6. **🟡 Verify the single-letter Y/N accelerator against a live TallyPrime build.** The corpus grounds a Yes/No
   Accept **prompt** but NOT bare-Y/bare-N as **keystrokes**. **The one UNVERIFIED premise in this point.**
7. **Does the Accept prompt appear on EVERY master, or only some?** The corpus samples ledger, group, company and
   vouchers — **universality could not be established**, and the strongest citations are company-scoped.

**NOTE FOR WHOEVER IMPLEMENTS: do not re-derive Ctrl+A. It is already implemented on ~40 screens and
regression-locked. ONLY the confirmation step is missing.**

---

# WI-12 — Alt+A to add a voucher from the Day Book

**Covers point 14.** **State: PARTIAL.** **Effort: M.**

### Raw wording (verbatim fragments)

> "day book" · "alt + a option for addition of a voucher" · "Entering any type of voucher" · "from the day book"

### Decoded requirement

From the Day Book report, **Alt+A** must open a voucher-creation screen for a **user-chosen voucher type**,
**without leaving/destroying the Day Book**, and on save return to the Day Book **with the new voucher visible in
it**. "Entering any type of voucher" ⇒ not limited to the fixed F4–F9 accounting hotkeys; the Day Book lists
*every* voucher type and implies no single type, so this needs a **voucher-type picker**.

The CA is naming a **real Tally binding**, not inventing one. Scope: **Alt+A (Add) only** — Alt+I (Insert),
Alt+2 (Duplicate), Ctrl+T (Post-dated), Ctrl+R (Remove) are siblings in the same Tally table but are **NOT** in
scope.

### Tally fidelity target — ✅ GROUNDED (with an extraction caveat that was independently resolved)

**`tally/664311548-Tally-Prime-Book.pdf`, p.431, §"7. Tally Prime Important Keyboard Shortcuts"** — key cell
**"Alt+I/ Alt+A"**; function cell **"To insert or Add a voucher in a report"**; "Where does it work" column =
**Reports**. **The Day Book is a report, so Alt+A there raises voucher creation from within the report.**

**⚠️ Extraction caveat, stated honestly and then RESOLVED.** This table's three columns are emitted as separate
blocks by `pdftotext -layout`, so key↔function alignment is **not literal** in the text dump — line 14887
literally reads "Alt+I/ Alt+A  To open Company Features", **a column-offset artefact, NOT the real mapping.**
**The decoder reconstructed the alignment and the verifier independently re-derived it without relying on the
decode: both columns have exactly 15 entries; position 11 = `Alt+I/ Alt+A` ↔ "To insert or Add a voucher in a
report" (lines 14905-14906) ↔ "Reports". Independently-known-correct rows bracket it on BOTH sides** — F11 →
Company Features (5), F12 → configurations (6), Alt+Z → exchange data (8), Alt+2 → duplicate (12), Ctrl+T → Post
Dated (13), Alt+D → delete (14), Ctrl+R → remove (15) — and the third column corroborates (position 13 (Ctrl+T)
→ "Vouchers", matching real Tally). **The alignment is sound.** *(Flagging the artefact rather than quoting it as
the mapping is exemplary R7 discipline. The "p.431" anchor is the **printed** page number — physical PDF page 435;
a 30-second visual check removes all doubt.)*

**🔴 CATALOGUE GAP (R7).** `docs/tally-feature-catalog.md:476-477` — the **Reports** shortcut row lists only
`Alt+F1`/`Alt+F2`/`Alt+C`/`Alt+N`/`Alt+F12`/`Enter`/`F5`. **It does NOT document report-context Alt+A Add-voucher
(nor Alt+I / Alt+2 / Ctrl+T / Ctrl+R).** And `:474` lists `Alt+A` **only under Vouchers** as "Tax Analysis / Add
column". **The catalogue should be corrected as part of this work — otherwise a future agent reading only the
catalogue will "know" Alt+A is Tax Analysis and reject this point.** `docs/tally-feature-catalog-verification-
report.md` is silent (its only Day Book entry is item 14 at `:68`, Alt+X vs Alt+D).

**NOT AUTHORITATIVE:** `tally/659947760-Tally-Prime-Short-Key.pdf:19` says "Alt+A → Save". **Disregard** —
`docs/tally-feature-catalog.md:479-481` flags this handout as garbled, and its own list is self-evidently broken
(`:17` "Ctrl+A → Zoom", `:18` "Alt+D Insert", `:21` "Ctrl+U Add"). *(See WI-11 for the +2-shift explanation.)*

**🟡 UNVERIFIED — must be checked before building:**
- **The exact difference between Add (Alt+A) and Insert (Alt+I).** Conventionally about *where in the
  date/number sequence* the voucher lands, but the corpus states only "To insert or Add a voucher in a report"
  **without distinguishing them.** **"Add" is only well-defined relative to "Insert".** Not asserted from memory.
- **The DATE the added voucher defaults to.** Candidates: the highlighted row's date; the last voucher date
  (today's default, `VoucherEntryViewModel.cs:529`); the report period end; today. **The single most likely thing
  to get silently wrong.**
- **Whether Tally prompts for a voucher TYPE on Alt+A in the Day Book**, or opens a default type.
- **Whether Alt+A returns to the report on accept** (near-certain given "in a report", but not stated).

### Current state — Alt+A does nothing on the Day Book, but F5 already opens a voucher there

**Alt+A is FREE in report context — no collision to resolve.** There are exactly **two** `Key.A` handlers in the
whole shell (`MainWindow.axaml.cs`): **Ctrl+A** accept at `:74`, and **Alt+A** at `:327`, **hard-scoped to POS**:
```csharp
// Alt+A surfaces the POS bill's per-rate tax analysis (RQ-53). Scoped to the POS Billing screen; ...
if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
    && vm.CurrentScreen == Screen.PosBilling)
```
*(Every earlier Alt handler was checked for a generic catch — `:129` ReorderLevelsMaster (S/V only), `:148`
report-context (Key.C/Key.N only), `:347` Alt+K. **None catches A.**)*
The button bar agrees — `MainWindowViewModel.cs:5260-5262`: `var onPos = CurrentScreen == Screen.PosBilling;` …
`new ButtonBarItem("Alt+A", "Tax Analysis", ShowPosTaxAnalysis, onPos)`.

**🔑 But voucher entry from the Day Book PARTLY works already — by accident of gating. This is the PARTIAL
hinge.** `MainWindowViewModel.cs:5247-5252` enables F4–F9 on **`hasCompany`, NOT on menu context**:
```csharp
ButtonBar.Add(new ButtonBarItem("F5", "Payment", () => OpenVoucher(VoucherBaseType.Payment), hasCompany));
```
and bare F-keys dispatch through `Fire(vm,"F5")` (`MainWindow.axaml.cs:526`; `Fire` at `:567-575`, which runs the
action if `b.Enabled`). The report-context interceptor at `MainWindow.axaml.cs:477-488` **only claims F2/F12
(+F8 on Reorder)**. **⇒ F5 pressed on the Day Book today opens a Payment voucher.**
*(Verified end-to-end rather than assumed: the whole of `BuildButtonBar` (`:5236-5292`) was read — **there is NO
report-context branch or early return**, so F4–F9 stay gated on `hasCompany` only. **A NOT_IMPLEMENTED verdict
here would have been wrong.**)*

**Why that partial route does NOT satisfy the point — three cited defects:**
1. **The Day Book is destroyed.** `OpenVoucher` (`MainWindowViewModel.cs:2579-2598`) uses `OpenPageColumn`, whose
   contract (`:4086-4089`) is: "Trim after the LAST MENU column — this removes any page column that is already
   open … so a page is **REPLACED, never stacked**. There is therefore **AT MOST ONE page column**". **The Day
   Book IS that page column** (`OpenReport` `:1906` uses `OpenPageColumn`).
2. **Save lands on the Gateway, not the Day Book** — `:2593` `onSaved: ShowGateway`.
3. **Not "any type".** Reachable types are only the 6 accounting F-keys (`:5247-5252`), the inventory Alt/Ctrl
   F-keys (`MainWindow.axaml.cs:429-432`, `:454-461`), and the menu dispatch (`:5021-5036`).

### 🔴 Adjacent confirmed gap — Credit/Debit Note are UNREACHABLE, and the catalogue already specifies the fix

`VoucherBaseType` (`src/Apex.Ledger/Domain/VoucherBaseType.cs:15-16`) has `CreditNote`/`DebitNote`, and
`VoucherEntryViewModel` **fully implements them** (`:1164` `_type.BaseType is VoucherBaseType.CreditNote or
VoucherBaseType.DebitNote`, `:1176` CdnType selection, §34 RQ-24) — **yet no UI route opens them.** Menus are
**hardcoded string literals**, not built from `Company.VoucherTypes`: absent from `BuildVouchersColumn`
(`:886-905`), `BuildOtherVouchersColumn` (`:1017-1038`), the menu dispatch (`:5021-5036`, a hardcoded switch with
no CN/DN case), and every F-key (the Alt block at `:435-462` binds only F9/F8/F7 — **Alt+F5/Alt+F6 unbound**).
A repo-wide `OpenVoucher(` sweep found **ZERO production CN/DN callers**.

**⚠️ VERIFIER CORRECTION — SUBSTANTIVE, fix before handing to an implementer.**
**`docs/tally-feature-catalog.md:471` reads: "`Alt+F5` Debit Note · `Alt+F6` Credit Note · `Alt+F7` Stock
Journal". The catalogue ALREADY PRESCRIBES the canonical CN/DN route.** The decoder correctly observed
"Alt+F5/Alt+F6 unbound" but **never connected it to `:471`**, and instead filed CN/DN as an **open question**
requiring CA/user adjudication ("Is that part of what 'any type' means? … deserves its own line item").
**It needs no adjudication — it is a grounded, catalogue-specified binding the code is simply missing.**
Two consequences: (a) **an implementer following the decode builds the picker and may ship WITHOUT Alt+F5/Alt+F6,
leaving the catalogue-specified route still absent;** (b) the decode's R7 framing is half-right — the catalogue
**is** silent on report-context Alt+A (a real gap worth correcting) but is **NOT** silent on the CN/DN route.
**Drop that open question; treat it as a specified requirement.**

**⚠️ Second verifier correction — the test gap was OVERSTATED, which deflates a risk.** The decode says "The only
caller is a test reaching past the UI: `GstVoucherEntryUiBindingTests.cs:294`". **FALSE.**
`tests/Apex.Desktop.Tests/CreditDebitNoteVoucherEntryViewModelTests.cs:94` is a **dedicated CN/DN test file**
(10 `[Fact]`/`[Theory]`) whose helper `OpenNote(… VoucherBaseType type = VoucherBaseType.CreditNote …)` calls
`vm.OpenVoucher(type)`. **CN/DN entry is well covered at VM level** ⇒ surfacing CN/DN in a picker is **cheaper and
safer** than the decode implies.

### 🟢 Enablers already present — this is cheaper than it looks

- **`VoucherEntryViewModel` already accepts a date:** `:462-468` `DateOnly? date = null`, applied at `:529`
  `Date = date ?? last ?? company.BooksBeginFrom;`. **No production call site passes it** (`:2591-2594`).
- **The exact "column to the right, report stays live beneath" pattern exists:** `OpenReportConfig`
  (`MainWindowViewModel.cs:2007-2020`) / `OpenReportSortFilter` (`:2031-2044`) — "Unlike the other page-openers it
  does **NOT** trim the report page column: the report stays live".
- **`IsReportContext` (`:2060-2061`) is the ready-made gate.**
- **Report re-projection hook:** `ReportsViewModel.Show(ReportKind)` (`ReportsViewModel.cs:606`) — "Switches the
  displayed report and rebuilds its rows".
- **Day Book drill-to-voucher already wired** (`:1905`, `OpenVoucherDetail` `:1971-1979`) — but read-only.
- **`Reports.SelectedRow` is real** — `[ObservableProperty] private ReportRow? _selectedRow;` at
  `ReportsViewModel.cs:142` (source-generated public property). *(A naive grep misses it; the verifier nearly
  flagged the proposal as broken before finding it.)*
- **`Company.FindVoucher`** exists at `src/Apex.Ledger/Domain/Company.cs:918`.
- **🔑 Missed precedent the proposal should use:** `MainWindow.axaml.cs:347` —
  `if (e.Key == Key.K && Alt && vm.IsReportContext)` (Alt+K Saved Views) — **exactly the report-context
  Alt+letter pattern needed**, already sitting after the POS block.

### Gap

1. **No Alt+A binding in report context** — Alt+A is POS-only (`:327-328`).
2. **No voucher-type picker.** Type selection is hardcoded into F-keys/menu labels (`:5021-5036`, `:5247-5252`).
   **Required by "any type".**
3. **Column discipline is wrong for this flow** — `OpenVoucher` → `OpenPageColumn` replaces the Day Book
   (`:4086-4089`) and `onSaved: ShowGateway` (`:2593`) abandons it.
4. **No date carry-over and no post-save refresh.** `OpenVoucher` never passes the `date` the entry VM already
   accepts (`:468`/`:529`); and **`ReportRow` carries `DrillVoucherId` but NO Date field** — `BuildDayBook` bakes
   the date into a display string (`ReportsViewModel.cs:1360`: `Particulars = $"{FormatDate(r.Date)}
   {DayBookParticulars(r)}"`), so a highlighted row's date must be recovered via
   `Company.FindVoucher(DrillVoucherId).Date` or by adding a structural field. **After save the Day Book must be
   re-run (`Show(Kind)`) or it shows stale rows.**
5. **Credit/Debit Note unreachable** — and `catalog:471` already specifies the route.

### Proposal

**UI-only slice. No schema change — v44 untouched** (vouchers already persist; nothing new is stored).

1. **`MainWindowViewModel.cs`** — add `OpenAddVoucherFromReport()`:
   - Guard on `IsReportContext && Reports?.Kind == ReportKind.DayBook && Company is not null` (widen only if Q4
     resolves that way).
   - Open a **voucher-type picker column** modelled exactly on `OpenReportConfig` (`:2007-2020`) — append to
     `Columns` **WITHOUT** `OpenPageColumn`/`TrimColumnsAfter`, so the Day Book stays live beneath and Esc pops
     back. Add `Screen.AddVoucherPicker` to the enum (`:14`).
   - **Populate from `Company.VoucherTypes.Where(t => t.IsActive)`** — this is what makes "any type" true and, as
     a side effect, **gives Credit/Debit Note their first real UI route.**
   - On pick, route by base type reusing the existing table at `:5021-5036`:
     `VoucherEffects.IsInventoryBaseType(baseType)` (already used at `:2613`) → `OpenInventoryVoucher`, else
     `OpenVoucher`.
2. **Same file** — overload `OpenVoucher(VoucherBaseType baseType, DateOnly? date = null, Action? onSaved = null)`
   (`:2579`): pass `date` through to the ctor's existing `DateOnly? date` param
   (`VoucherEntryViewModel.cs:468`), and **default `onSaved` to today's `ShowGateway` so ALL existing call sites
   are byte-identical.** From the Day Book pass `onSaved: () => { BackFromPage(); Reports?.Show(Reports.Kind); }`
   (`ReportsViewModel.cs:606`) to pop back to a **refreshed** Day Book.
3. **Date source** — resolve the highlighted row via `Reports.SelectedRow?.DrillVoucherId` →
   `Company.FindVoucher(id)?.Date`, falling back to the entry VM's existing default (`:529`). **Prefer this over
   widening `ReportRow`;** add a `Date` field only if the picked semantics need a date on non-drillable rows
   (empty Day Book). **BLOCKED on Q2 — do not guess.**
4. **`MainWindow.axaml.cs`** — bind Alt+A near the existing POS Alt+A (`:327`), **ordered AFTER it** and gated on
   `IsReportContext` (**copy the `Alt+K` pattern at `:347`**), so the POS binding keeps priority and a future
   voucher-context Tax Analysis (phase-4 RQ-29) can slot in ahead of it. **Name it distinctly
   (`OpenAddVoucherFromReport`, NOT `AddVoucher*`)** — `AddVoucherLine()` at `:4363` already means "add a line to
   the open voucher".
5. **`BuildButtonBar` (`:5236`)** — add `new ButtonBarItem("Alt+A", "Add Voucher", OpenAddVoucherFromReport,
   isDayBook)`. **⚠️ The bar already has an `Alt+A` item (`:5262`); `Fire()` (`MainWindow.axaml.cs:567-575`)
   returns on the FIRST key match, so two `Alt+A` entries would make the POS one shadow this. Emit only the
   context-appropriate one.**
6. **Bind the catalogue-specified CN/DN keys** — `Alt+F5` Debit Note / `Alt+F6` Credit Note (`catalog:471`) in the
   Alt block at `:435-462`. **Cheapest fix in this document; the VM path is already tested.**
7. **`MainWindow.axaml`** — a picker column DataTemplate. **`#Root` binding path** (not the `GatewayColumn`
   `x:DataType` fallback → AVLN2000); watch starved `*` columns; **render-verify headlessly then revert
   `TestAppBuilder`.** *(Voucher-type names are long — do not pin a narrow fixed-width column beside dead space,
   the campaign's #1 root cause.)*
8. **`docs/tally-feature-catalog.md:476-477`** — correct the Reports shortcut row to include `Alt+A` Add voucher /
   `Alt+I` Insert voucher (cite Book p.431). **Leaving this stale will cause a future agent to "fix" this back
   out.**
9. **Tests** (`tests/Apex.Desktop.Tests/`, near `ReportDrillViewModelTests.cs`): Alt+A on Day Book opens the
   picker; **the Day Book column SURVIVES** (assert `Columns` still contains the report **AND** `Reports is not
   null`); picking a type opens the right screen (incl. an inventory type → `InventoryVoucherEntry`);
   **accept returns to the Day Book AND the new voucher appears in `Reports.Rows`** — **this is the assertion that
   actually proves the point** (per the "a `ShowXMenu()` test proves nothing about reachability" lesson, assert
   reachability + refresh, not just that a method ran). Assert **Alt+A on POS still shows Tax Analysis** (no
   regression).

### Risk

- **🔴 Stale report after save (silent wrong data).** `ReportsViewModel` builds `Rows` on construction/`Show`. If
  the Day Book is left live beneath and **not re-run**, the user adds a voucher and **does not see it — looking
  exactly like a lost posting.** Mitigation: `Reports.Show(Reports.Kind)` in `onSaved`. **Must be an explicit test
  assertion.**
- **🔴 Back-dated voucher numbering.** Adding a voucher on a past date under Automatic numbering can
  **renumber/gap the sequence** — the very concern `docs/tally-feature-catalog-verification-report.md:68` and
  `plan.md:255` raise for Alt+X/Alt+D. Engine-side (`LedgerService`), untouched by this UI slice, **but Alt+A
  makes back-dated insertion EASY for the first time, so it will get exercised.** Verify before shipping — and
  **this is exactly where the UNVERIFIED Add-vs-Insert distinction bites.**
- **🟠 Alt+A collision with Tax Analysis.** Safe today (POS-scoped, `:327`), **but phase-4 RQ-29 says Alt+A SHALL
  be Tax Analysis on an open GST voucher.** Order the handlers and scope by context **now**, or a later slice
  silently shadows one of them. Same hazard in `BuildButtonBar` via `Fire`'s first-match-wins.
- **🟠 Regressing the shared `OpenVoucher`** — 10+ call sites (`:5021-5036`, `:5247-5252`). **Add an overload with
  defaulted params; do not change the existing signature's behaviour.**
- **🟠 Cascade/column invariant.** `OpenPageColumn` documents "AT MOST ONE page column, always the rightmost"
  (`:4086-4089`). Stacking an entry screen beside a live report **bends** that invariant; `OpenReportConfig`
  already does exactly this, **so follow it precisely rather than inventing a third discipline.** Esc / `Back` /
  `RehydratePageFromRightmostColumn` must be tested. **⚠️ Note the WI-1 finding: `RehydratePageFromRightmostColumn`
  (`:5114-5142`) cannot restore a voucher page** — that gap interacts with this flow.
- **🟡 Scope creep via CN/DN** — a "list all active types" picker makes CN/DN reachable for the first time. **That
  is a FIX**, and per the verifier the VM path is already well tested, so this risk is **lower than the decode
  implied.**
- **🟡 UI truncation** — a new column in the file with the 328-defect catalogue against it.
- **🟢 No schema change.** *(If Q2 is resolved by persisting a per-report "last added type" preference, the full
  v45 parity chain applies. Avoid it.)*

### Open questions

**Must be answered before building:**
1. **Type selection** — does Alt+A open a **type picker** (proposed), or a **default type switchable by F4–F9**?
   **If the latter, it is a much larger change:** `VoucherEntryViewModel` is constructed **per `VoucherType`** and
   **cannot currently switch type in-screen.**
2. **🔴 Date of the added voucher** — highlighted row's date / last voucher date / period end / today?
   **Not grounded in the corpus; MUST be verified against real Tally Prime** (open Day Book, Alt+A, observe the
   date). **The single most likely thing to get silently wrong.**
3. **Add vs Insert** — the Book lists both (Alt+I/Alt+A) but **does not distinguish them.** **Verify in real Tally
   before implementing Add, because "Add" is only well-defined relative to "Insert".** The CA asked only for
   Alt+A; confirm Alt+I is out of scope for now.
4. **Day Book only, or all reports?** The Book says "**Reports**"; the CA said "day book".
5. **Catalogue correction** — `docs/tally-feature-catalog.md:476-477` omits report-context Alt+A/Alt+I. Confirm the
   catalogue may be amended (R7 reference) as part of this fix.
6. **Verify Book p.431 visually** — the key↔function alignment is reconstructed from a column-scrambled
   `pdftotext -layout` dump. **High confidence** (two independent reconstructions; four+ known rows corroborate),
   but a 30-second look at the page removes all doubt. Note "p.431" is the **printed** page number (physical PDF
   page 435).

**~~7. Are Credit/Debit Notes part of "any type"?~~ — WITHDRAWN.** The verifier established this is **not** an
open question: `docs/tally-feature-catalog.md:471` already specifies `Alt+F5` Debit Note / `Alt+F6` Credit Note.
**Treat as a grounded, specified, missing binding and just build it.**

---

# STATUTORY SECTION — A14 web-verified law (points 6 and 9)

**Verified 2026-07-17 against official Government of India sources.** Under R7, nothing here is asserted from
memory. **Where an official page could not be fetched, it says so and the claim is marked UNVERIFIED.**

## 🔴 THE HEADLINE: the governing Act changed on 1 April 2026, mid-project

**The Income-tax Act, 2025 (Act 30 of 2025) came into force 1 April 2026, replacing the Income-tax Act, 1961.
Salary TDS is now section 392, NOT section 192.**

This is confirmed on an **official page that was actually fetched** — the incometax.gov.in TDS-compliance page
expressly "discusses salary obligations under **Section 192 (old Act) and Section 392(1) (new Act)**", with
**1 April 2026 as the transition date**.

**§392(1) mechanism** (per the official section text as indexed; wording consistent with old §192(1)): any person
responsible for paying income chargeable under the head "Salaries" shall deduct income-tax **at the time of
payment**, at the **average rate of income-tax** computed on the basis of the **rates in force for the tax year**,
on the **estimated income** of the assessee under that head for that year.
- **§392(2):** the employer may, at its option, bear the tax on non-monetary perquisites without deducting it.
- **§392(4):** the employer shall take into account particulars furnished by the employee at his option —
  including salary from **another employer** during the tax year — and the tax deductible shall **not be reduced
  except on account of loss under the head "Income from house property"** and tax deducted under other provisions.
- **§392 also absorbs old §192A** (accumulated PF balance).
- **§392(5)(b) r/w Rule 205** of the Income-tax Rules, 2026 is the employee-declaration power.

**Terminology:** the 2025 Act uses "**tax year**". "Previous year" / "assessment year" are **gone**. So
"FY 2026-27 / AY 2027-28" is now simply **tax year 2026-27**.

## 🔴 The forms were all renumbered (Income-tax Rules, 2026)

| Old | New | Status of verification |
|---|---|---|
| **Form 24Q** (quarterly salary-TDS statement) | **Form 138** | **✅ OFFICIALLY CONFIRMED — the Form 138 user manual on incometax.gov.in was FETCHED: "It replaced Form 24Q". Rule 219.** Due dates from that page: **Q1 31 Jul · Q2 31 Oct · Q3 31 Jan · Q4 31 May** (following year). |
| **Form 16** (annual salary TDS certificate) | **Form 130** | Official-domain index only (403). Due **15 June** following the tax year; download from TRACES; corrections require a revised **Form 138**. |
| **Form 12BB** (employee declaration) | **Form 124** | Official-domain index only (403) — page titled "Form No. 124 (Earlier Form No. 12BB)". |
| **Form 16A** | **Form 131** | Official-domain index only (403) — "Form No. 130_131_132_133 (Earlier Form No. 16/16A/16B/16C/16D/16E/27D)". |
| **Form 27D** | within the **130/131/132/133** family | Same index page. |
| **26QB / 26QC / 26QD / 26QE** | common **Form 141** | ✅ Fetched (the TDS-compliance page). |

**⚠️ This is a wider blast radius than salary alone — it also touches Phase 7 (Form 26Q, Form 16A → 131).**

## 🟡 RATES — split verification. DO NOT touch rate code until this is settled.

**Officially verified (fetched incometax.gov.in "Salaried Individuals for AY 2026-27") — the AY 2026-27 =
FY 2025-26 figures ONLY:**
- New regime **0 / 5 / 10 / 15 / 20 / 25 / 30%** at **4L / 8L / 12L / 16L / 20L / 24L**
- Old regime **2.5L / 5L / 10L** at **0 / 5 / 20 / 30%**
- **§87A rebate ₹60,000 @ taxable ≤ ₹12L (new)**; **₹12,500 @ ≤ ₹5L (old)**
- **Surcharge nil → 37%** (37% **old regime only**, above ₹5cr); **cess 4%**
- *(That page still uses AY terminology and the 1961 Act.)*
- **Standard deduction ₹75,000 new / ₹50,000 old** is widely reported **but was NOT obtained from an official
  page that was fetched.**

**🔴 UNVERIFIED — that these rates carry UNCHANGED into tax year 2026-27** (i.e. that Finance Act 2026 made no
slab/SD/rebate/surcharge change). Asserted by multiple secondary sources; **could NOT be confirmed officially —
`indiabudget.gov.in` and `incometaxindia.gov.in` both returned 403 to the fetcher** (retried with a browser UA
via curl: also 403). **The two documents that would settle it:** the consolidated "Income-tax Act 2025 as amended
by Finance Act 2026" PDF and the Finance Bill 2026 explanatory memorandum. **Must be checked — via a different
network path, or by the user opening them — BEFORE anyone touches `SalaryIncomeTax.cs`.** Until then the rate
constants are "**correct for FY 2025-26, PRESUMED-unchanged for TY 2026-27**" — presumed, not verified.

## Sources (exactly as obtained — fetched vs index-only is stated)

**FETCHED (official, content read):**
- https://www.incometax.gov.in/iec/foportal/help/all-topics/e-filing-services/tds-compliance — the §192 (old Act)
  → §392(1) (new Act) transition on 1 April 2026; 26QB/QC/QD/QE → common Form 141.
- https://www.incometax.gov.in/iec/foportal/newformpage/forms/form138-um — official Form 138 user manual:
  "It replaced Form 24Q"; quarterly due dates Q1 31 Jul / Q2 31 Oct / Q3 31 Jan / Q4 31 May.
- https://www.incometax.gov.in/iec/foportal/help/individual/return-applicable-1 — official AY 2026-27 =
  FY 2025-26 slabs, 87A rebate 60k/12.5k, surcharge, 4% cess.

**OFFICIAL-DOMAIN INDEX ONLY — 403, NOT directly fetched (titles/snippets surfaced via search):**
- https://www.incometaxindia.gov.in/w/section-392-6 — the §392 text.
- https://www.incometaxindia.gov.in/documents/d/guest/fn-138 — "Form No. 138 (Earlier Form No. 24Q)".
- https://www.incometaxindia.gov.in/documents/d/guest/fn-130-131-132-133 — "Form No. 130_131_132_133 (Earlier
  Form No. 16/16A/16B/16C/16D/16E/27D)".
- https://www.incometaxindia.gov.in/documents/d/guest/fn-124 — "Form No. 124 (Earlier Form No. 12BB)".
- https://www.incometaxindia.gov.in/w/faqs-on-forms-as-per-income-tax-rules-2026-1
- https://www.incometaxindia.gov.in/documents/d/guest/income_tax_act_2025_as_amended_by_fa_act_2026-pdf —
  **the doc that would settle the TY 2026-27 rates.**
- https://www.indiabudget.gov.in/doc/memo.pdf — Finance Bill 2026 explanatory memorandum. **403.**

**LOCAL CORPUS (`pdftotext -layout`):**
- `tally/664311548-Tally-Prime-Book.pdf` — "Process 1: Create Payable (Dues) Pay Heads": Name "Salary Payable",
  Pay Head Type "Not Applicable", Under "Current Liabilities".
- `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf` — `~1964-65` "28 pre-defined groups … 15 Primary and 13
  sub-groups"; `~2073` "you can create groups as required"; `~5779`/`~5859` "Payroll Ledger: Salary Payable".

---

# WI-13 — Income-tax Act 2025 renumbering (§392, Forms 138 / 130 / 124) + rate effective-dating

**Extends point 6 (A14-discovered — the CA did NOT raise this).** **State: NOT IMPLEMENTED.** **Effort: L.**
**🔴 Requires a user decision (R12) — this is scope the user has not approved.**

### The gap

**As of 1 April 2026 the entire salary-TDS surface is named after a repealed Act and superseded forms.** The
shipped app today:

- a screen literally titled **"Form 24Q"** (`MainWindowViewModel.cs:136`) — now **Form 138**;
- a screen **"Form 16"** (`:137`) — now **Form 130**;
- a column titled **"Income Tax Declaration (Form 12BB)"** (`:3683`, opened by `ShowTaxDeclarationMaster()`
  `:3677-3683`) — now **Form 124**;
- **~every docstring citing §192** — `SalaryIncomeTax.cs:6`, `PayrollComputationService.cs:500`,
  `PayrollService.cs:191`, `Form24Q.cs:94` — now **§392**;
- **Phase 7 is hit too** — Form 26Q and Form 16A (→ 131).

**A CA opening "Form 24Q" in July 2026 sees a form that no longer exists.** **The arithmetic is unaffected** (rates
*appear* unchanged — but see the UNVERIFIED flag); this is **nomenclature, statutory citation, and filing-artifact
identity.**

**Note this collides with WI-6:** the new picker label proposed there says "Income Tax (TDS on Salary — Sec 192)".
**Decide the naming before shipping a new user-visible label.**

### 🔴 Related gap — rates are hardcoded with no effective-dating

`SalaryIncomeTax.cs` holds **every rate as a `const` / inline literal** (`:26-41`, `:91-97`, `:101-111`,
`:155-158`) **with no tax-year parameter anywhere.** The class is **pinned to FY 2025-26 by its own docstring**
(`:6-8`). **Today is TY 2026-27.**

**Contrast the deliberate Phase-7 decision:** `NatureOfPayment.cs:5-10` made TDS rates "**seeded configuration,
not a hard-coded constant, so the FY-specific rate/threshold table can be maintained without a code change**".
**Salary TDS did the opposite.** ⇒ **If Finance Act 2026 changed any figure, the app is silently wrong and only a
code change fixes it.** Since the TY 2026-27 rates **could not be officially confirmed** (both hosts 403'd),
**this is UNVERIFIED and must be settled before anyone declares the numbers correct.**

### Minor rider

`GstConfigViewModel.cs:284` computes `SalaryTdsAssessmentYearLabel` from `FinancialYearStart.Year + 1`. **Under the
2025 Act "assessment year" no longer exists** (superseded by "tax year"). Cosmetic, but it is a correctness wart
that rides along with this decision.

### Proposal (sketch — do not start without a user go)

1. **Confirm the TY 2026-27 rates** against the consolidated Act / Finance Bill 2026 memo (see Sources — both
   403'd the fetcher). **Blocking for any rate change.**
2. **Rename the user-visible surface:** Form 24Q → **Form 138**; Form 16 → **Form 130**; Form 12BB → **Form 124**;
   Form 16A → **131**; "assessment year" → "tax year". Decide whether to keep the old numbers as a parenthetical
   during transition (a CA who has filed 24Q for 20 years may want "Form 138 (formerly 24Q)").
3. **Update statutory citations** §192 → §392 across docstrings and any user-visible text.
4. **Give `SalaryIncomeTax` a tax-year parameter** and move the rate table to seeded configuration, following the
   `NatureOfPayment.cs:5-10` precedent. **This is the change that stops this recurring every Finance Act.**
5. **De-brand check:** no shipped label may contain "Tally".

### Risk

- **🔴 The rates are UNVERIFIED for TY 2026-27.** Changing them on secondary-source belief would be worse than
  leaving them. **Verify first.**
- **🟠 Blast radius reaches Phase 7** (26Q, 16A) — this is not a payroll-only rename.
- **🟠 Effective-dating is a real refactor,** not a rename: `SalaryIncomeTax` is pure and deterministic today, and
  the Phase-8 golden fixtures (`tests/Apex.Ledger.Tests/Phase8GoldenPayrollTests.cs`,
  `docs/phase8-payroll-requirements.md:521-543`) pin FY 2025-26 numbers. A tax-year parameter must keep those
  green **by construction** (pass the FY explicitly rather than defaulting to "today").
- **🟡 Naming churn confuses users mid-transition** — hence the parenthetical question.

### Open questions

1. **🔴 (User, R12) Does the app move to the Income-tax Act 2025 / Rules 2026 vocabulary now?** The shipped UI
   names three forms that legally no longer exist. **Arguably more urgent than either CA point — but it is scope
   the user has not approved. Surface at a gate.**
2. **🔴 (Blocking any rate work) Did Finance Act 2026 change the TY 2026-27 slabs / standard deduction / 87A /
   surcharge?** Secondary sources say no; **could not be confirmed officially.** Needs a different network path or
   the user opening the consolidated Act / Budget memo.
3. **Keep the old form numbers as a parenthetical during transition?**
4. **Should the rate table move to seeded configuration now** (the `NatureOfPayment` precedent), or is that a
   separate slice? **Recommend now — it is the only thing that stops this recurring.**

---

# WI-14 — Salary-TDS deposit / challan path

**Extends point 6 (A14-discovered — the CA did NOT raise this).** **State: NOT IMPLEMENTED (deliberately
deferred).** **Effort: L.**

### The gap

**Salary TDS accrues correctly and reports correctly, but CANNOT BE DEPOSITED OR CHALLANED through the app.**

**The carry-forward is documented in the code itself.** `src/Apex.Ledger/Reports/Form24Q.cs:102-115` states that
salary-TDS "is credited to its own auto-created payable ledger (e.g. \"TDS on Salary\" under Current
Liabilities), **separate** from the Phase-7 \"TDS Payable\" (Duties & Taxes) ledger that `TdsDepositService` /
`ChallanReconciliation` / `Form26Q` operate on." Routing was "**deliberately deferred**" because the Phase-7
machinery "keys on `TdsLineTax` (section 194x) details", so wiring salary-TDS (which carries
**`PayrollLineDetail`, not `TdsLineTax`**) "would either **pollute Form 26Q with §192 rows** or require invasive
changes".

**The other end verified:** `src/Apex.Ledger/Services/TdsDepositService.cs:6-18` — the deposit engine "pays the
accrued **\"TDS Payable\"** liability into the bank via a **Stat Payment**"; `BuildStatPayment()` `:53-60` builds
exactly `Dr "TDS Payable" / Cr bank`. **It has no knowledge of payroll lines.**

**Consequence:** salary TDS accrues and reports correctly, but **the 24Q/138 FVU file carries no challan block**
(`Form24Q.cs:111-113`).

### ⚠️ PRECISION CORRECTION to the project memory — read this before hunting for a constant

**There is NO hardcoded "TDS on Salary" ledger.** `grep -rn "TDS on Salary" src/` matches **only the comment at
`Form24Q.cs:104` — and that comment says "e.g."**. The real ledger name is **whatever the user names the §192 pay
head** (`PayHead.cs:56` auto-creates `LedgerId` from it), under a **user-chosen** group (`PayHead.cs:49`).
**A fix agent hunting for a `TdsOnSalaryLedgerName` constant will not find one.**

### Proposal (sketch — needs an architecture decision first)

Route salary-TDS into the deposit/challan machinery **without** polluting Form 26Q. The shape depends entirely on
Q1 below. Either:
- **share** the Phase-7 "TDS Payable" ledger and **discriminate at the RETURN level** (26Q vs 24Q/138) — eases
  challan/stat-payment reuse, but risks the exact reconciliation the Phase-8 author protected; or
- **keep a separate** salary-TDS payable ledger and build a **parallel challan path**.

### Risk

- **🔴 Form 26Q pollution** — the failure the Phase-8 author explicitly refused (`Form24Q.cs:106-110`).
- **🔴 Challan reconciliation** is the thing most likely to break — it currently keys on `TdsLineTax`.
- **🟠 `Form24Q.cs:98-99` reads withheld tax off each posted line and never recomputes**, so the return reconciles
  to the payable credit **by construction**. **Any deposit path must preserve that invariant.**

### Open questions

1. **🔴 (User, R12 — architecture) If salary TDS gets a deposit path, does it SHARE the Phase-7 "TDS Payable"
   (Duties & Taxes) ledger, or keep its own?** Sharing eases challan/stat-payment reuse but risks the 26Q/challan
   reconciliation the Phase-8 author protected; separating needs a parallel challan path.
   **⚠️ Note: §392/§192 and §194x deposits DO share a single ITNS-281 challan in reality, which argues for sharing
   the ledger and discriminating at the RETURN level — but that is REASONING, not a verified statutory or
   Tally-fidelity claim. Ground it before relying on it.**
2. **Is this in scope at all**, or does it stay a documented carry-forward until Phase 11? *(The CA did not raise
   it. But "TDS option in salary … as per income tax law" is arguably incomplete without the ability to deposit
   it.)*

---

# ALREADY BUILT — findings that need NO work

Reported here because **not finding work is a valuable result**, and because building any of these again would
waste a slice. Each is evidenced above.

| Thing | Evidence | Bearing |
|---|---|---|
| **The §392/§192 salary-TDS engine — complete and law-current for FY 2025-26** | `SalaryIncomeTax.cs` (284 lines): slabs `:91-111`, standard deduction `:26-27`, §87A with correct new/old asymmetry `:138-150`, surcharge + marginal relief `:155-203`, 4% cess last `:218-228`, §206AA floor `:234-239`, average-rate spread + true-up `:248-264` | **Point 6 is a missing dropdown entry, NOT a missing feature.** |
| **§192 payroll integration, incl. the correct telescoped annual estimate** | `PayrollComputationService.cs:511-541`, esp. the `:522-527` comment recording that annualising this-month×12 was a real bug | Do not "fix" the estimate. |
| **Form 12BB declaration with correct regime gating** | `TaxDeclaration.cs:60-70` — new regime returns **only** `Section80CCD2Employer` | Statutorily correct. |
| **Form 24Q (Annexure I + Q4 Annexure II → Form 16 Part B), Form 16, Payroll Register TDS column** | `Reports/Form24Q.cs:96-99`/`:71-91`/`:126-132`, `Reports/Form16.cs`, `PayrollRegister.cs:76` | Renders zeros only because no head can be tagged. |
| **The salary-TDS F11 toggle — working and reachable** | `MainWindow.axaml:8637-8641`, `GstConfigViewModel.cs:278`/`:1055` | **Possibly a discoverability problem** — it lives in a **GST-named** screen. |
| **The statutory-component role guard** | `PayHeadService.cs:179-189` + `CreatePayHead:84`; Io mirror `CompanyImportService.cs:505` | **The decoder's "HIGH" risk was FALSE — this guard exists.** |
| **Ctrl+A accept-as-is, from any field, on ~40 screens** | tunnel handler `MainWindow.axaml.cs:20-21`/`:74`, `ActivateSelected` `MainWindowViewModel.cs:4556`, live test `HeadlessMainWindowTests.cs:121-123`, 71 label occurrences | **Point 13-B needs NO work.** |
| **The compound-unit master, end-to-end** | `Unit.cs:17-172`, `InventoryService.cs:158-180`, schema `Schema.cs:1034-1046`, Io `CanonicalModel.cs:791-803`, UI `MainWindow.axaml:4835-4914` | **"1 Dozen = 12 Nos" is already creatable.** Point 12 is about *transacting* in it. |
| **Engine unit conversion + its persistence and Io** | `InventoryAllocation.cs:43`, `Unit.cs:137-142`, **7** normalising call sites, `Schema.cs:1143`, `CanonicalModel.cs:1706` | Live engine code with **no UI entry point**. |
| **Guarded, TESTED master delete / re-parent for ~15 master types** | `InventoryService` `:44`–`:295`, `PayrollService` `:277`–`:486`, `PayHeadService` `:91`–`:116` (incl. an **identity-preserving rename**), BomService/BatchService/PriceListService/ReorderLevelsService/SalaryStructureService | **Dead code from the UI.** WI-3's engine half is much cheaper than "NOT_IMPLEMENTED" suggests — **only Ledger and Group need new engine work.** |
| **Custom accounting groups: schema, Io, and report classification all ready** | `Schema.cs:657-668`, `SqliteCompanyStore.cs:1307-1314`, `CanonicalModel.cs:640`/`CanonicalMapper.cs:397`/`CanonicalXml.cs:77`, `ClassificationRules.cs:14-31`, `BalanceSheet.cs:113`/`:125` | **WI-7 needs NO schema and NO Io fold-in.** |
| **The invoice print address SINK — declared AND rendering** | `InvoicePrintData.cs:14-15`, `InvoicePdf.cs:398`/`:135`, formatter `VoucherPrintProjector.cs:236` | **WI-4's consumer is wired; only the source is missing.** |
| **The party-group ancestry gate + conditional-block pattern** | `LedgerMasterViewModel.cs:421-433`/`:402`/`:236`/`:292` | Exactly the gate WI-4 needs, already written. |
| **"Salary Payable" under Current Liabilities — Tally-faithful, already replicated** | `PayrollVoucherService.cs:31`/`:48`/`:355`, `PayHeadType.NotApplicable = 9` (`PayHeadType.cs:39-40`); Tally: Book "Process 1: Create Payable (Dues) Pay Heads" | **Not a defect.** Tally ships no "Salary" group either — we match its 28 (`SeedGroups.cs:64`). |
| **Report-context Alt+C = New Column** | `MainWindow.axaml.cs:144-156` | **Correct Tally — preserve it.** *(But note its narrow gate: comparative reports only.)* |
| **Report F2 / Alt+F2 split** | `MainWindow.axaml.cs:482`/`:445`, `ReportsViewModel.cs:525`/`:533` | **Correct Tally — leave alone.** WI-5 is about the *other* 97 screens. |
| **Date STORAGE — already uniform ISO `DateOnly`** | `Schema.cs:7` and throughout | **WI-5 needs no migration.** A literal "saved in a format" reading makes point 4 a no-op. |
| **Credit/Debit Note engine + entry screen + tests** | `VoucherBaseType.cs:15-16`, `VoucherEntryViewModel.cs:1164`/`:1176`, `CreditDebitNoteVoucherEntryViewModelTests.cs:94` (10 facts) | Only the **binding** is missing (`catalog:471`). |

---

# OPEN QUESTIONS — consolidated

Per-item questions live in each section. **These are the ones that block work or change cost materially.**

## 🔴 For the user / CA — blocking

1. **(WI-8 / WI-6) "TDS of employee" — §392/§192 salary TDS, or the §194x deductee block on a ledger?** The first
   **already exists** in Payroll; the second is a small gate change. **And please ask the CA whether they were
   aware of the existing Payroll §192 module — if not, SEVERAL of these 15 points may be discoverability, not
   gaps.** *(The salary-TDS toggle works but lives inside a GST-named screen.)*
2. **(WI-8) Is the intended salary workflow the manual/non-payroll one** (Journal: `Dr Salary / Cr <Employee>`
   against a per-employee ledger), **or the Payroll module** (one shared Salary Payable, employee on the line)?
   **Tally supports both. If both, confirm we are NOT replacing the payroll design.** *(Reading (a) is what point
   9's wording most naturally suggests, and it is the **large** option.)*
3. **(WI-10) Which unit failure did the CA actually hit?** (a) couldn't create "Dozen = 12 Nos" — **it exists**;
   (b) couldn't type a line in Dozen — **confirmed missing, cheap**; (c) needs one item with two units at once —
   **Alternate Units, needs schema.** **~5× cost spread.**
4. **(WI-10) "2 Dozen @ ₹10" — is ₹10 per Dozen or per Nos?** **Ungrounded. Get it wrong and every invoice total
   is out by the conversion factor.**
5. **(WI-4) Mailing State vs GST State.** `PartyGstDetails.StateCode` already drives place of supply. One field,
   two, or one-defaults-the-other? **A second, contradicting State is a tax-computation hazard.** *(Recommend one
   field, GST defaults from it.)*
6. **(WI-11) Does Ctrl+A raise the Y/N prompt, or bypass it?** **Tally says bypass (Study Guide:2035); gating it
   would break `HeadlessMainWindowTests.cs:121-123`.** **The point's central fork.**
7. **(WI-3) Does "all" mean items/ledgers/groups, or every master type?** The **L-vs-XL fork** — though cheaper
   than it looks, since ~15 master types already have a tested engine layer.
8. **(WI-3) Does "fully editable" include Opening Balance + Dr/Cr and Alias?** **Neither is capturable today even
   at CREATE** — an accounting app that cannot take opening balances cannot onboard an existing set of books.
   **Arguably a bigger gap than alter itself. Should it be its own point?**
9. **(WI-9) Does "the gateway" mean the root column (19 items) or the whole cascade (176 items)?** **~9× effort.**
10. **(WI-1) Field-level or voucher-level Alt+C dispatch?** **Voucher-level is undecidable on an item invoice.**
11. **(WI-2) Prefix or substring filtering?** The single answer that most shapes that slice.
12. **(WI-5) Canonical date format — `dd-MMM-yyyy` (dominant, unambiguous) or Tally's `dd-MMM-yy` (2-digit)?**
    **Fidelity vs safety.**
13. **(WI-13, R12) Does the app move to the Income-tax Act 2025 vocabulary (§392, Forms 138/130/124, "tax
    year")?** **Not raised by the CA; arguably more urgent than what was.**
14. **(WI-14, R12) If salary TDS gets a deposit path, does it share the Phase-7 "TDS Payable" ledger or keep its
    own?**

## 🟡 For A14 / R7 — must be grounded BEFORE the relevant item is built

15. **(WI-9) Does TallyPrime select menu items by a BARE letter? Is the colour genuinely RED? What is the
    collision rule?** **NOT FOUND in the catalogue, the verification report, or any of the 10 PDFs — two agents
    searched independently. The entire fidelity target for WI-9 is currently an assumption.**
16. **(WI-2) Does Tally SHRINK the list or only JUMP the highlight? StartsWith or Contains? Does the list
    auto-open on field focus?** **All NOT FOUND. Auto-open is design-gating.**
17. **(WI-11) Is bare-Y / bare-N a real Tally KEYSTROKE?** The Yes/No **prompt** is grounded (incl. a verbatim
    `ACCEPT ? YES` at `719244897:3003`); the **accelerator** is not.
18. **(WI-12) Add (Alt+A) vs Insert (Alt+I) — what is the difference? What DATE does an added voucher take? Does
    Tally prompt for a type?** **"Add" is only well-defined relative to "Insert".**
19. **(WI-8) Does Tally show the deductee block for a ledger under a CUSTOM liability group, or only under Sundry
    Debtors/Creditors?** **Both corpus sources show Sundry Creditors and neither shows any other group.**
20. **(WI-8) Does Tally gate the deductee block by GROUP, or by an explicit per-ledger "Is TDS Deductable?"**
    **If the latter, our design is wrong in SHAPE, and a new domain field (+ possible v45) is needed.**
21. **(WI-3) Confirm identity-preserving retroactive rename** — `verification-report:48` is **[model-knowledge]**,
    not corpus-grounded. **(WI-3) Does Enter on a CoA ledger row open Alter or a Display?** **NOT FOUND.**
    **(WI-3 / WI-7) Can a predefined group's name/parent be altered?** The catalogue only forbids **deleting**.
22. **(WI-4) Confirm the exact party-ledger Mailing Details field list and order** — "Mailing Name" and "Country"
    on a *ledger* are inferred by symmetry and NOT directly attested; **even the Company block's order varies by
    source.**
23. **(WI-10) Is the compound unit's NAME derived ("Doz of 12 Nos") or user-typed?** **This is the root of the
    backwards test.** **Decimal conversion factors?** **UQC under an alternate unit?**
24. **(WI-5) Tally's 2-digit-year pivot? Partial-date completion? Rejection UX?** **A wrong pivot silently posts
    to the wrong FY.**
25. **(WI-13) 🔴 Did Finance Act 2026 change the TY 2026-27 rates?** **Could NOT be confirmed — both official
    hosts 403'd. BLOCKING for any rate work.**

## 📋 Catalogue corrections owed (R7) — each will be re-litigated if skipped

26. **`docs/tally-feature-catalog.md:117-127`** — the Ledger field list **omits address/mailing/PIN entirely**
    while the Company section (`:87`) lists them. **This is likely the root cause of WI-4's gap.**
27. **`docs/tally-feature-catalog.md` §9** — **Alternate Units is absent** from the catalogue despite being in the
    PDF corpus. **Building WI-10 Slice D against a PDF read alone violates R7.**
28. **`docs/tally-feature-catalog.md:476-477`** — the Reports shortcut row **omits report-context `Alt+A` Add /
    `Alt+I` Insert**, while `:474` lists `Alt+A` under Vouchers as Tax Analysis. **Leave it stale and a future
    agent will "fix" WI-12 back out.**
29. **`docs/tally-feature-catalog.md` §0/§21** — documents **no bare-letter menu keys at all** (WI-9). Add a
    subsection once Q15 is answered.

---

# SUGGESTED PHASING

**Nothing here starts until the UI-defect campaign lands.** Almost every item touches
`src/Apex.Desktop/Views/MainWindow.axaml`, and the campaign is rewriting it now. The user has already sequenced
this work as fix-later for exactly that reason.

This backlog does **not** fit inside Phase 10 (security/roles/audit + data management) or Phase 11 (hardening +
release v1.0) as `plan.md:418-434` defines them. **Per R6, `plan.md` must be updated before any of it is
executed.** The natural shape is a **new phase between 10 and 11** — call it **Phase 10.5 — CA-audit remediation**
— because:

- it is **feature/fidelity work**, not administration (Phase 10) and not hardening (Phase 11);
- **WI-3's audit-trail question** is the one thing that genuinely belongs to **Phase 10** (it is the only trigger
  that would force schema v45 for that item) — so **Phase 10 should land first** and answer it;
- shipping v1.0 (Phase 11) with a CA-visible backlog of this size is the wrong order.

**Suggested ordering within Phase 10.5** (dependency-driven, cheapest-first where dependencies allow):

| Wave | Items | Rationale |
|---|---|---|
| **0 — decisions** | Q1–Q14 to the user; Q15–Q25 to A14; catalogue corrections 26–29 | **Several items cannot be scoped until these land.** Q1 alone may collapse WI-8. |
| **1 — correctness bugs, cheap, no schema** | **WI-6** (picker entry, **S**) · **WI-2 Step 1** (the silent-wrong-ledger misfire) · **WI-12's CN/DN bindings** (`catalog:471`, already specified) · **`ImportPlan.cs:172`** nature validation (a live Balance-Sheet corruption path) | **These are live defects, not features.** All are small and independently shippable. **The `:172` fix and the type-search misfire arguably jump the queue entirely.** |
| **2 — the prerequisite** | **WI-7** (accounting Group master) | **Blocks WI-1 (point 8's "group"), WI-3 (point 10's "groups") and WI-11's gate design.** Also fixes the `:4897` mis-wire. Schema-free. |
| **3 — the master-editing spine** | **WI-3** (points 5+10) — **then WI-4** (v45) rides on it | WI-3 unblocks re-tagging (WI-6), makes WI-4's fields editable, and is where the well-known-name rename guards must land. **WI-4 create-only without WI-3 is arguably worse than no field.** |
| **4 — the keyboard cluster (design together)** | **WI-2** + **WI-9** + **WI-11** | **WI-2 and WI-9 have a direct cross-point conflict** (same keystroke, same widget on picker columns) and **must share one rule.** WI-11 edits the same ~450-line handler. **Doing these separately means re-litigating the same guards three times.** |
| **5 — the Alt+C cluster** | **WI-1** (points 8+11+15) | Needs WI-7 (group target) and the WI-2 picker contract (point 15's in-dropdown Create). **Carries the rehydrate fix that also repairs the existing Alt+B bug.** |
| **6 — the rest** | **WI-5** (dates) · **WI-12** (Alt+A) · **WI-10 Slices A+B** (settle the unit model, expose the line unit) | Independent; WI-10 A+B are schema-free. |
| **7 — user-gated, schema/scope-heavy** | **WI-10 Slices C+D** · **WI-13** · **WI-14** · **WI-8** | **All blocked on decisions.** WI-13/WI-14 are A14 findings the user has not approved. WI-8 is dangerous until Q1/Q2/Q4/Q5 resolve. |

**Schema forecast.** Only three items move the schema, and **each needs the full drill** (new columns in the
**real** CreateV1 **and** a new `MigrateV44ToV45`, `SchemaMigrationEquivalenceTests` parity, **and** an
`Apex.Ledger.Io` fold-in or export goes silently lossy): **WI-4** (party mailing, v45) · **WI-10 Slices C+D**
(line unit / alternate units, v45) · **WI-6 Fix 4** *if taken* (pay-head income-tax fields, v45).
**Everything else is v44-clean.** **⚠️ Read the `Schema.cs` structural warning in WI-10 correction 1 first —
CreateV1 spans `:114-1679` and IS the current v44 DDL; the migrations from `:1687` are FROZEN HISTORY.**

**Standing conventions that apply to every item here** (hard-won; see `memory.md`):
- The `Cmp08ReportViewModel` template and the **`#Root` binding path** — **not** the `GatewayColumn` `x:DataType`
  fallback (AVLN2000).
- The **9-step tree wiring with the gate at the ROOT too** — *"a `ShowXMenu()` test proves nothing about
  reachability."*
- **Render-verify via headless Skia, then REVERT `TestAppBuilder`.** `IsEffectivelyVisible` stays **TRUE** when a
  parent clips, so **only a real render catches an off-pane control.**
- Watch **starved `*` columns** and **horizontal StackPanels** (infinite-width measure kills `TextWrapping`).
- **A10 has caught a real bug on EVERY slice to date, engine and UI alike.** Budget for the adversarial pass.
- **De-brand (hard rule): no shipped label, message or report title may contain "Tally".**

---

*End of document. Compiled 2026-07-17 from 15 decoded CA points, 15 adversarial verification passes, and one
A14 statutory web-verification pass. Read-only investigation — no code was changed.*

