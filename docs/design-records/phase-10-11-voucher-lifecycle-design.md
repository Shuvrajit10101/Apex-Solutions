> **HISTORICAL DESIGN RECORD — A SNAPSHOT, NOT A LIVE DOCUMENT.**
> The COMPLETE Phase 10.11 design (12 sections), captured 2026-08-17 and preserved here because the session
> scratchpad it was written in does not survive the session. It records what was true when written.
>
> **CITATION POLICY.** Every `file.ext:NN` pointer has been rewritten to `file.ext line NN` so the repository's
> citation invariant (`DocumentCodeAgreementTests`) does not read them as live pointers. These line numbers were
> accurate when captured and are NOT maintained — re-derive before relying on any of them.
>
> **NOTE ON A CONFUSION IN ITS OWN CLOSING REPORT:** the author flagged that "another agent" wrote a
> `…-PARTIAL.md` file into `docs/design-records/` during its run and inferred a second, concurrent Phase 10.11
> design was being produced. There was no second design. That file was the MAIN LOOP snapshotting THIS agent's
> own partial output mid-write, to survive a session close. It has been replaced by this complete version.
# Phase 10.11 — Voucher lifecycle (alter · delete · cancel) — DESIGN

**Author:** design agent, 2026-08-17.
**Target tree:** `C:\Users\dkpho\OneDrive\Desktop\Apex Solutions(end)\.claude\worktrees\recursing-swirles-3138c6`
branch `claude/apex-wrong-figures-bc45f4`, HEAD `3e968b3`. Read-only throughout: no repo file was created or
modified, no git write command was run.

**Gate at HEAD (given, not re-measured):** build 0W/0E · Ledger 1668 · Io 414 · Sqlite 231 · Desktop 2195 · schema **v51**.

> **HOW TO READ THIS FILE.** Sections are written in the task's numbering but were **appended in completion
> order** so that a crash loses at most one section. Every claim is either (a) a file:line I opened myself, (b) a
> corpus quotation with a PDF page, or (c) explicitly labelled **UNVERIFIED** / **NOT SETTLED BY THE CORPUS**.
> Nothing here is asserted from memory of TallyPrime.

## Section index

| § | Topic | State |
|---|---|---|
| 0 | Preamble — method, and one methodological finding that matters | done |
| 2 | R7 — what the corpus actually settles about Alter / Delete / Cancel | done |
| 1 | Ground truth — what exists in the tree today | done |
| 3 | The hard part — unwinding derived state | done |
| 4 | Numbering | done |
| 5 | Audit — the excluded Edit Log, and what its absence costs | done |
| 6 | Slice shape | done |
| 7 | Tests, including the RED-PROOF | done |
| 8 | Risk, and the ER-13 byte-identity question | done |
| 9 | Schema | done |
| 10 | RULING 5 — the fidelity rows for `docs/full-clone-census.md` §1.3 | done |
| — | APPENDIX — the ten decisions this design asks for (R12) | done |
| **11** | **LATE FINDINGS — read these; they correct §6.3, §10 and `plan.md`** | done |

> ## 🔴 READ FIRST — four things that change what you were about to do
>
> 1. **TWO OF THE FIVE PLANNED SLICES ARE ALREADY MERGED.** 10.11 **S1** (`6a28d15`) and **S2** (`f2abdbb`),
>    both 2026-08-07, both verified in the code. `plan.md`'s slice list and two of its sentences are stale.
>    **This phase is three slices, not five.** §1.1
> 2. **THE "CANCELLED VOUCHER KEEPS ITS NUMBER AND IS GREYED" CLAIM IS MODEL-KNOWLEDGE.** The project's own
>    verification report tags it `[model-knowledge]` and lists it as needing a spot-check; the corpus is silent.
>    `plan.md line 320` cites it as if sourced, via a section id that does not exist. §11.1
> 3. **DELETING THE HIGHEST-NUMBERED VOUCHER REUSES ITS NUMBER.** `NextNumber` is `max+1` by scan. The engine's
>    own doc says only *"may leave a gap"*. VL-2 is the slice that makes this reachable. §4.1
> 4. **THERE IS NO SECOND PHASE 10.11 DESIGN — THIS FILE IS THE WHOLE OF IT.** A part-written
>    `…-design-PARTIAL.md` did appear in `docs/design-records/` during the run, and §11.3 below records it as a
>    rival agent's work. That inference was wrong: it was the **main loop snapshotting THIS design's own partial
>    output mid-write**, so a session close would not lose it. It has been replaced by this complete version and
>    no longer exists. **Nothing needs reconciling — execute from this file.** §11.3

---

# §0 — Preamble: method, and one methodological finding

## 0.1 What I did

- Converted the nine admissible corpus PDFs with `pdftotext -layout` into a scratchpad `corpus/` directory
  (`659947760-Tally-Prime-Short-Key.pdf` **excluded** — REJECTED as a source per the standing rule).
- Grepped with a page-aware helper so every quotation carries a **PDF page number**.
- Opened every source file I cite in `src/` and `tests/` directly.

## 0.2 🔴 METHODOLOGICAL FINDING — `-layout` SILENTLY SCRAMBLES THE BOOK'S SHORTCUT TABLES; `-raw` FIXES THEM

This is the most reusable thing in this document and it invalidates at least one R7 claim already written into
`plan.md`.

`664311548-Tally-Prime-Book.pdf` pages 435–437 carry TallyPrime's own three-column shortcut table (Key /
Function / Where does it work). Under `pdftotext -layout` the three columns are emitted as **three independent
top-to-bottom streams**, so the reader must re-pair them by counting. On PDF p.435 the counts happen to match
(15 keys : 15 functions) and the pairing is recoverable. **On p.436 and p.437 they do not** — p.436 yields 20
keys against 21 function-fragments, p.437 yields 10 against 11 — and any pairing read off the `-layout` dump is
a guess.

`pdftotext -f <p> -l <p> -raw` emits the table **cell by cell in true reading order**, one row at a time, and
resolves all three pages unambiguously. Command that worked:

```
pdftotext -f 437 -l 437 -raw "664311548-Tally-Prime-Book.pdf" -
```

**Consequence for this phase (see §2.4):** the `plan.md` R7 line claiming TallyPrime *"reserves **Ctrl+Enter**
for display-only drill-down"* is **contradicted by the corpus** once the table is read correctly. Ctrl+Enter is
*"To alter a master during voucher entry or from drilldown of a report."* The claim appears to have been read
off a scrambled `-layout` dump. **Any future R7 finding sourced from Book pp.435–437 must be re-derived with
`-raw`.**

**Standing instruction proposed:** add `-raw` as the second pass for any tabular corpus page, and treat a
`-layout` key/function pairing as UNVERIFIED unless the key count and the function count match exactly.

---

# §2 — R7: what the corpus actually settles

Sources are cited as `<pdf-file> PDF p.<n>` (the page index inside the PDF) with the book's own printed page in
brackets where the page carries one. Only the nine admissible PDFs were used.

## 2.1 SETTLED — how alteration is reached, and that there is no read-only voucher screen

The Book gives the identical recipe for **every** voucher family, repeated verbatim across the whole voucher
chapter:

> *"How to Show/Edit \<X> Voucher Entry in Tally Prime?
> Step: GOT > Display More Reports > Account Books > \<X> Register > Select Month & **Show/Edit Entry**.
> For Delete Entry Press `Alt+D' on Selected Entry"*

— `664311548-Tally-Prime-Book.pdf` PDF **p.32** (Contra), **p.34** (Receipt), **p.37** (Payment), **p.42**
(Purchase), **p.47** (Sales), **p.49** (Journal), **p.64** (Credit Note), **p.71** (Debit Note); and for the
inventory families with the same shape at **pp.74, 77, 81, 83, 87, 92, 94, 99, 101** (Purchase Order, Receipt
Note, Delivery Note, Stock Journal, Physical Stock, Job Work In/Out Order, Material In/Out), several of which
end *"& Show/Edit Entry > Press \"Ctrl+A\" for Save"* (PDF **pp.51, 53, 56, 58**).

Four facts fall out and all four are load-bearing for this phase:

1. **The register drill-down IS the alteration screen.** The corpus names one action, `Show/Edit`, not two. It
   never describes a read-only voucher display followed by a separate "now edit it" step.
2. **Alteration is reached from a register**, not only from the Day Book.
3. **`Ctrl+A` saves the altered voucher** — the same accept key as creation.
4. **`Alt+D` on the selected register row deletes it** — corroborated independently at §2.2.

**This is the corpus fact that our USER DECISION 1 knowingly diverges from** (`plan.md`: Ctrl+Enter opens
alteration, plain Enter keeps the read-only VoucherDetail column, *"BACKWARDS from TallyPrime on both keys"*).
The divergence is settled and is not re-litigated here — but §2.4 shows the *stated reason* for one half of it
is wrong.

## 2.2 SETTLED — Alt+D is Delete; Alt+X is Cancel; and the neighbours

From the Book's own shortcut table, re-extracted with `-raw` (§0.2), `664311548-Tally-Prime-Book.pdf`
PDF **p.435** [printed p.431]:

| Key | Function (verbatim) | Where does it work |
|---|---|---|
| `Alt+I / Alt+A` | "To insert or Add a voucher in a report" | Reports |
| `Alt+2` | "To create an entry in the report, by duplicating a voucher" | Reports |
| `Ctrl+T` | "To mark a voucher as Post Dated" | Vouchers |
| **`Alt+D`** | **"To delete an entry from a report"** | Reports |
| `Ctrl+R` | "To remove an entry from a report" | Reports |

PDF **p.436** [printed p.432]:

| Key | Function (verbatim) | Where |
|---|---|---|
| **`Ctrl+Enter`** | **"To alter a master during voucher entry or from drilldown of a report"** | Reports |
| `Ctrl+D` | "To remove item/ledger line in a voucher" | Vouchers |
| `Alt+R` | "To retrieve Narration from the previous ledger" | Vouchers |
| `Ctrl+R` | "To retrieve the Narration from the previous voucher, for the same voucher type." | Vouchers |
| `Shift+Enter` | "To expand or collapse information in a report" | Reports |

PDF **p.437** [printed p.433]:

| Key | Function (verbatim) | Where |
|---|---|---|
| **`Alt+X`** | **"To cancel a voucher To cancel a voucher from a report"** *(one cell, two phrasings)* | **Vouchers & Reports** |
| `Alt+Z` | "To zoom in while on print preview" | Vouchers & Reports |
| `Ctrl+P` | "To print the current voucher or report" | Vouchers & Reports |
| `F2` | "To change the date of voucher entry or period for reports" | Masters, Vouchers, and Reports |

**Findings:**

- **`Alt+X` = cancel a voucher — CONFIRMED, and its scope is BOTH "Vouchers" and "Reports"**, not report-only.
  `plan.md`'s R7 line *"scopes Alt+X to cancelling from a report"* is a **narrowing** of the corpus: the cell
  reads *"To cancel a voucher"* **and** *"To cancel a voucher from a report"*, and the Where column says
  *"Vouchers & Reports"*. Our slice may still ship report-only — but it must say so as **our** scope decision,
  not as fidelity.
- **`Alt+D` = delete — CONFIRMED**, and the same key deletes masters from the alteration screen (§2.3).
- **`Ctrl+D` removes a LINE inside voucher entry** while `Alt+D` deletes the whole entry from a report. Two
  different keys for two different granularities; our dispatcher must not collide them.
- **`Ctrl+R` is context-dependent** (Reports: remove an entry from a report; Vouchers: retrieve narration).
  Likewise `Alt+Z` (p.435 = data-exchange actions; p.437 = zoom in print preview). The Book contains these
  duplications itself; they are not extraction errors. Any future key-map table (IV-28) must carry a **context**
  column or it will be wrong.

## 2.3 SETTLED — the delete confirmation is a DOUBLE Yes/No, and its wording is published

Verbatim, `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` PDF **p.277** (deleting a **Group Company**):

> *"5. Press Alt+D to delete the Group Company
> 6. Tally Prime will ask you to **"Delete Yes or No?"**
> 7. Supply Yes to Delete
> 8. Tally Prime will ask your confirmation **"Are you sure Yes or No?"**
> 9. Supply Yes to Confirm it"*

Independently corroborated in shape (not wording) by the Book's recipes, which all end **"Press Two times
`Enter' Button"** — i.e. two consecutive confirmations:

- Delete **Company**: *"Gateway of Tally > Alt+K > Alter > Alt+D > Press Two times `Enter' Button"* — Book PDF **p.15**
- Delete **Ledger**: *"GOT > Alter > Ledger > Select Ledger for Delete & Press Enter > Alt+D > Press Two times Enter"* — Book PDF **p.21**
- Delete **Voucher Type**: *"GOT > Alter > Voucher type > Select Voucher for Delete & Press Enter > Alt+D > Press Two times Enter"* — Book PDF **p.23**
- Delete **Ledger** (second source): *"Press Alt+D → supply Yes to confirm Deletion."* — STUDY-GUIDE PDF **p.67**

**Finding.** The two-prompt pattern (`Delete Yes or No?` → `Are you sure Yes or No?`) is **corpus-settled for
masters and for a group company**. It is **NOT** directly attested for a *voucher*. The Book's voucher recipe
says only *"For Delete Entry Press `Alt+D' on Selected Entry"* and does not describe the prompt.

**Design consequence.** Ship the **single** `Delete Yes or No?` prompt for a voucher and record the second
"Are you sure" prompt as **NOT ATTESTED FOR VOUCHERS** rather than copying it across by analogy — the corpus
attests the double prompt only for objects whose loss is catastrophic and irreversible (a whole company). This
is a deliberate scope call, recorded, not a fidelity claim.

> ### 🔴 CORRECTED 2026-08-18 — THE "FINDING" ABOVE IS FALSE IN ITS LOAD-BEARING HALF, AND THE WHOLE D-6 RECORD RESTED ON IT
> This design record is a **historical snapshot** (see its own header) and the paragraphs above are kept exactly
> as written. **What they got wrong:** *"It is **NOT** directly attested for a voucher"*. **BOOK PDF pp.22-23
> carry a heading reading *"How to Delete Voucher …?"* directly over the same *"Alt+D > Press Two times Enter"*
> recipe.** The same entry then contradicts itself — its path reads `Alter > Voucher type` — so the attestation
> is **poor**. It is still attestation, and the entire D-6 record was built on its absence.
>
> **THE USER RULED ON 2026-08-18, IN TWO RULINGS. THE BEHAVIOUR IS UNCHANGED — one prompt everywhere, exactly
> as shipped. THE RECORD BECOMES TWO RECORDS, AND THEY ARE DIFFERENT R7 CLAIMS ON DIFFERENT EVIDENCE:**
> - **(A) VOUCHER routes — OUR DECISION AGAINST WEAK, SELF-CONTRADICTORY ATTESTATION.** Not "corpus silent",
>   not a decline-to-extend-an-unattested-behaviour.
> - **(B) THE THREE MASTER routes S4 ships (ledger, group, stock item) — A DELIBERATE DIVERGENCE FROM AN
>   ATTESTED SCOPE.** The double prompt is cleanly attested there: Book p.21 (ledger) and Study Guide p.277
>   (group company), both quoted above. *(Study Guide p.67's single prompt for the same ledger object — also
>   quoted above — narrows the divergence and does not change its category.)*
>
> **Keep (A) and (B) apart.** Conflating them is the exact R7 defect a review lens caught on S3. The live
> record is `docs/full-clone-census.md` §1.3 item 11 and the doc comments on `MasterDeletionRules` and
> `MainWindowViewModel.RequestDeleteHighlighted`; this block exists so the snapshot cannot be read as current.

## 2.4 🔴 CORRECTION TO A CLAIM ALREADY IN `plan.md` (R7)

`plan.md` Phase 10.11, R7 line, asserts TallyPrime *"reserves **Ctrl+Enter** for display-only drill-down."*

**The corpus says the opposite.** Book PDF p.436 [printed p.432], read with `-raw`:
`Ctrl+Enter` → *"To alter a master during voucher entry or from drilldown of a report"*.

So in TallyPrime `Ctrl+Enter` is an **alteration** key, not a display key — for a **master**, reached either
from inside voucher entry or from a report drill-down. Two things follow:

1. **USER DECISION 1 survives, but half its stated reason does not.** The decision binds `Ctrl+Enter` to
   *voucher* alteration and plain Enter to a read-only column. Against the corpus, the Enter half is still a
   deliberate divergence (Tally's Enter goes straight to Show/Edit — §2.1), but the **Ctrl+Enter half is closer
   to TallyPrime than `plan.md` believed**: Tally already uses Ctrl+Enter to *alter* from a drill-down. Our use
   extends it from masters to vouchers. That is a **smaller** divergence than recorded, and the record should
   be corrected rather than quietly left wrong.
2. **The R7 line in `plan.md` must be amended.** Owed to the post-merge documentation slice (R5/R6). It is a
   fidelity claim that would otherwise be cited forward by the next agent.

## 2.5 SETTLED — a ledger with transactions cannot be deleted

Verbatim, `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` PDF **p.67**:

> *"You cannot delete any ledger, if any transaction(s) has been already made with that ledger. To delete the
> ledger, delete all the transactions related to that ledger and then you can delete the ledger."*

Note the second sentence: TallyPrime's remedy is **delete the transactions first**, which is exactly the
capability this phase creates. The guard and the verb are designed together in Tally, and must be here too.

Related, same family, Book PDF **pp.104–105**: *"You will not be able to delete a Cost Category in multiple
modes"* / *"…a Cost Centers in multiple modes"* — i.e. multi-master screens offer alter but **not** delete.
Relevant if a Multi-Alter screen ever grows an Alt+D.

## 2.6 SETTLED — TallyPrime has Duplicate and Insert as *separate* verbs from Alter

- `Alt+2` — *"To create an entry in the report, by **duplicating** a voucher"* (Book PDF p.435).
- `Alt+I / Alt+A` — *"To **insert** or Add a voucher in a report"* (Book PDF p.435).

**Finding.** Duplicate and Insert are report-level *creation* verbs, distinct from Alter. Neither is in this
phase's scope and neither should be smuggled in. `Alt+A` is confirmed as TallyPrime's own key for
*"insert or Add a voucher in a report"*, which **validates ORCHESTRATOR RULING 4** (VL-4's replacement
settlement gesture is `Alt+A` on the Outstandings screen) — the plan cites this and the citation checks out.
`Alt+2` (Duplicate) is a **named carry-forward**, not built here.

## 2.7 🔴 NOT SETTLED BY THE CORPUS — five questions, and they must NOT be filled by invention

| # | Question | Corpus state |
|---|---|---|
| C-1 | What does **Cancel** MEAN — does the voucher keep its number? | **NOT SETTLED.** The corpus's only statement about cancellation is the four words *"To cancel a voucher"* (Book PDF p.437). No corpus text anywhere describes the *effect* of cancelling. `grep -oic cancel` over all nine admissible PDFs returns **2 hits total**, one of which is *"cancelled cheque with Form 5 IF"* in the EPF chapter (Book PDF p.320). |
| C-2 | Is a cancelled voucher shown **struck through**? | **NOT SETTLED.** Zero corpus hits for `struck`, `strike through`, `strike-through`. `plan.md line 267` specifies a **greyed** Day Book row — that is **ours**, and must be recorded as ours. |
| C-3 | The **cancellation confirmation wording**. | **NOT SETTLED** (as `plan.md` already states). The delete wording IS published (§2.3); the cancel wording is not. |
| C-4 | Does **un-cancel** exist? | **NOT SETTLED** (as `plan.md` already states). ORCHESTRATOR RULING 3 ships no un-cancel; that stands. |
| C-5 | What happens to the **number** of a **deleted** voucher — reused, retired, or a permanent gap? | **NOT SETTLED.** No corpus text. See §4, where our own code answers it — badly. |

**A word on C-1/C-2 that the implementer must read.** It is tempting to treat "a cancelled voucher retains its
number and appears struck through" as a known TallyPrime fact. **The corpus does not contain it.** Our engine
*already* implements retain-the-number (`LedgerService.Cancel` sets a flag and never touches `Number` — §1.3),
and that is a good design; but it must be documented as **UNVERIFIED-BY-DESIGN — our choice, corpus silent**,
in exactly the shape R7 already uses elsewhere. Do not write "as TallyPrime does" anywhere near it.

---

# §1 — Ground truth: what actually exists at `3e968b3`

## 1.1 🔴 THE BIGGEST FINDING — TWO OF THE FIVE PLANNED SLICES HAVE ALREADY SHIPPED

`plan.md`'s Phase 10.11 lists five slices S1…S5. **S1 and S2 are merged ancestors of HEAD and were verified in
the code, not just in the log:**

| Slice | Commit | Date | Verified in code |
|---|---|---|---|
| **S1 — the Alt+D modifier hole** (VL-2 step 1) | `6a28d15` *"fix(desktop): quick jumps and the accept prompt stop answering modifier chords (10.11 S1)"* | 2026-08-07 | ✅ `src/Apex.Desktop/Views/MainWindow.axaml.cs line 1096-1097` now reads `=> vm.IsMenuScreen && !IsTyping(e) && e.KeyModifiers == KeyModifiers.None;` |
| **S2 — settlement off Ctrl+B** (VL-4 / IV-5) | `f2abdbb` *"fix(desktop): settlement comes off Ctrl+B — Alt+A now OPENS a voucher instead of posting one (10.11 S2)"* | 2026-08-07 | ✅ `MainWindow.axaml.cs line 385-410` is now a RESERVED-DO-NOT-BIND comment block where the arm stood; `MainWindow.axaml.cs line 621-627` is the Alt+A Outstandings arm calling `vm.OpenSettlementVoucherFromOutstandings()`; `BillSettlementService.cs line 19` records *"`SettleAndPost` is therefore deleted"* |

**`plan.md` is stale on this and must be corrected.** Two specific sentences in the Phase 10.11 row are now
false:

1. *"**Also closes the modifier hole** — `CanQuickJump` never tests `e.KeyModifiers`, so **Alt+D already opens
   the Day Book today**."* — **FALSE at HEAD.** It tests `e.KeyModifiers == KeyModifiers.None`. The hole is shut.
2. The VL-4 warning that leaving the button-bar row *"would paint a red badge that fires nothing — the IV-31
   defect"* — **did not happen.** `MainWindow.axaml.cs line 1201-1202` `OnSettleBillsClick` was **repurposed**, not
   left dangling: `=> Vm?.OpenSettlementVoucherFromOutstandings();`, and `MainWindow.axaml line 5173` still binds it.
   Button and accelerator take the same route by construction.

**Consequence for the slice plan: this phase is now THREE slices, not five** (§6). Anyone starting from
`plan.md` alone would re-do 10.11 S1 and S2.

## 1.2 The aggregate — `Company`

`src/Apex.Ledger/Domain/Company.cs line 8` — `public sealed class Company`. Framework-agnostic; holds the posted set
in memory. 47 `private readonly List<T>` backing fields (`:10-56`), each exposed as an expression-bodied
`IReadOnlyList<T>` property. The two that matter:

- `Company.cs line 13` `private readonly List<Voucher> _vouchers = new();` → `Company.cs line 420` `public IReadOnlyList<Voucher> Vouchers => _vouchers;`
- `Company.cs line 26` `private readonly List<InventoryVoucher> _inventoryVouchers = new();` → `Company.cs line 461` `public IReadOnlyList<InventoryVoucher> InventoryVouchers => _inventoryVouchers;`

**⚠️ Trap.** Both properties return the **live `List<T>` instance**. `((List<Voucher>)company.Vouchers).Add(v)`
compiles and bypasses every posting guard. A new lifecycle verb must not be tempted by it, and a review should
grep for the cast.

**Mutators:**

| Member | Access | `Company.cs` | Note |
|---|---|---|---|
| `AddVoucherInternal(Voucher)` | `internal` | `:957` | bare `_vouchers.Add` |
| `RemoveVoucherInternal(Voucher)` | `internal` | `:958` | bare `_vouchers.Remove` |
| **`RemoveVoucher(Voucher)`** | **`public`** | `:971` | bare `List.Remove`, **no guards** — used by import roll-back and the Desktop save-failure undo |
| `AddInventoryVoucherInternal` / `RemoveInventoryVoucherInternal` | `internal` | `:938` / `:941` | |
| **`AddInventoryVoucher`** / **`RemoveInventoryVoucher`** | **`public`** | `:944` / `:985` | **no guards**; `AddInventoryVoucher` is the rehydration path |

**There is NO `ReplaceVoucherInternal`.** `plan.md` correctly lists it as new work.

## 1.3 The posting service — `LedgerService`

`src/Apex.Ledger/Services/LedgerService.cs`, **162 lines, six public members**. Full text read.

- `:30` `public Voucher Post(Voucher voucher)` → delegates with `CostAllocationStrictness.Strict`.
- `:42` `public Voucher Post(Voucher voucher, CostAllocationStrictness)` — the only guarded accounting entry
  point. In order: `:46` `StampInventoryLineDirections(voucher)` **mutates the voucher in place**; `:48`
  `VoucherValidator.EnsureValid(...)` throws on violation; `:51-52`
  `if (type.Numbering == NumberingMethod.Automatic && voucher.Number <= 0) voucher.Number = NextNumber(...)`
  — **mutates `Number`**; `:54` `_company.AddVoucherInternal(voucher)`; `:61` returns the **same instance**
  (no clone).
- **`:92` `public void Cancel(Guid voucherId)`** — EXISTS. Body is three lines: find-or-throw, then
  `v.Cancelled = true;` (`:96`). **Nothing else.** Number retained, row retained, position retained.
- **`:103` `public void Delete(Guid voucherId)`** — EXISTS. find-or-throw, then
  `_company.RemoveVoucherInternal(v);` (`:107`). No reversal voucher, no tombstone, no audit row.
- `:120` `ConvertToRegular(Guid, Guid)` — Memorandum → real. **Read this one: it is the closest existing
  precedent for a Replace.** It `Post`s the replacement **first** and only removes the memo once the post
  succeeded (`:148-149`), precisely so a failed post leaves the original intact.
- `:154` `public int NextNumber(Guid voucherTypeId)` — `max(Number) + 1` by linear scan. **Does not skip
  cancelled vouchers** (correct — a cancelled number must stay burned) **and cannot see deleted ones** (§4).

**So `Post` recomputes nothing and invalidates nothing.** Posting touches exactly two things: the `List<T>` on
`Company`, and the voucher's own `Number` + stamped line directions.

### 1.3b The inventory twin — `InventoryPostingService`

`src/Apex.Ledger/Services/InventoryPostingService.cs`. Its own doc (`:34`) says it is *"the **only** path that
mutates the company's stock/order voucher set"*. `Post` at `:68`, **`Cancel` at `:128`**, **`Delete` at `:140`**,
`NextNumber` at `:149` (byte-identical max+1 over `_company.InventoryVouchers`),
`DetectNegativeStock` at `:176`, `NegativeStockWarnings` at `:184`.

**So Cancel and Delete exist on BOTH sides already.** `plan.md`'s ORCHESTRATOR RULING 3 (*"the pure-inventory
Cancel analogue is deferred"*) is a **UI** deferral, not an engine one — the engine method is there.

## 1.4 🔴 CAN ANYTHING TODAY REMOVE OR AMEND A POSTED VOUCHER? — the precise answer

**Amend: NO.** There is no `Replace`, `Alter`, `Amend`, `Modify`, `Reverse`, `Void` or `Unpost` anywhere for a
posted voucher. The only in-place mutation `Post` itself performs is `Number` and inventory-line direction.

**Remove: YES, but only from three places, none of them a user gesture:**

1. `src/Apex.Ledger/Services/GstSetOffService.cs line 315-320` — the **only production call of
   `LedgerService.Delete` in `src/`**. Idempotent period replace: delete the prior set-off voucher(s) for the
   period, then re-post.
2. `src/Apex.Ledger.Io/ApplyJournal.cs line 234-236` — transactional import roll-back, undoing a failed apply in
   reverse dependency order via the public `RemoveVoucher`/`RemoveInventoryVoucher`.
3. **Desktop save-failure undo** — `VoucherEntryViewModel.cs line 3112`, `:4267`, `:4854`, `PosBillingViewModel.cs line 724`,
   all `undo.Push(() => _company.RemoveVoucher(posted));`. The pattern (`VoucherEntryViewModel.cs line 4251-4272`)
   is **`Post` → `try { _storage.Save(_company); } catch { _company.RemoveVoucher(posted); … }`**.
   **This is the transactional discipline every new lifecycle verb must copy**, for the reason recorded at
   `:4255-4260`: a narrow exception filter once let a locked-file failure escape with the refused voucher still
   on the aggregate, diverging every later save from the `.db`.

**`LedgerService.Cancel`, `InventoryPostingService.Cancel` and `InventoryPostingService.Delete` have ZERO
production callers in `src/`.** They are engine capability with no reachable UI. **This is the wiring gap
`plan.md` describes, and it is real.**

**Do not be fooled by these six near-misses** (each checked):

- `VoucherBaseType.ReversingJournal` + `Voucher.ApplicableUpto` (`Voucher.cs line 101`) — a what-if voucher *type*, not an action.
- `GstReversalService` — **ITC** reversal (Rule 37/37A/42/43, §17(5)); returns an `ItcReversal` audit row.
- `RcmService` "reverse charge" — GST liability direction.
- `EInvoiceService.Cancel(record, …)` (`:193`) / `EWayBillService.Cancel(record, …)` (`:281`) — cancel the **IRP/NIC artefact**, never the voucher.
- `PayrollVoucherService.Rollback()` (`:484`) — undoes auto-created **ledgers**, not vouchers.
- `MainWindowViewModel.CancelVoucher()` (`:4981`) — **Esc/Alt+X abandon-the-entry-screen**, not voucher cancellation. Its own doc: *"Alt+X: cancel the in-progress voucher (no save) and pop its page column."*

**The accounting-correct amendment path that DOES exist** is a §34 note:
`CreditDebitNoteService.BuildCreditDebitNote` (`:88`) registers a `GstCreditDebitNoteLink` back to the original,
which is never touched. §34(2) time limit guarded at `:108-114`.

## 1.5 The voucher model

`src/Apex.Ledger/Domain/Voucher.cs line 7` — **`public sealed class Voucher`. A class, not a record. Mutable.**

| Member | Declaration | line | Mutable |
|---|---|---|---|
| `Id` | `public Guid Id { get; }` | `:14` | **no** |
| `TypeId` | `public Guid TypeId { get; }` | `:17` | **no** |
| `Number` | `public int Number { get; set; }` | `:20` | yes — set by `Post` |
| **`Date`** | `public DateOnly Date { get; }` | `:23` | **no** |
| `Narration` / `PartyId` | `{ get; set; }` | `:26` / `:29` | yes |
| `Lines` | `IReadOnlyList<EntryLine> Lines => _lines` | `:32` | live list |
| `InventoryLines` | `=> _inventoryLines` | `:43` | live; mutated in place by `SetInventoryLineDirections` (`:181`) |
| **`Cancelled`** | `public bool Cancelled { get; set; }` | **`:88`** | yes — **the status field** |
| `Optional` / `PostDated` / `ApplicableUpto` | `{ get; set; }` | `:91` / `:94` / `:101` | yes |
| `ReferenceNo` / `ReferenceDate` | `{ get; set; }` | `:112` / `:118` | yes |
| `IsAccountingInvoice` | `{ get; }` | `:138` | **no** |

**Three consequences that shape the whole design:**

1. **The status field is `Cancelled` (a plain `bool`), NOT `IsCancelled`, and there is no enum.** There is **no
   `Status`, no `PostedAt`, no `CreatedAt`, no `ModifiedAt`, no user stamp** — on the object or in the table
   (`Schema.cs line 941-961`: no `created_at`, no `modified_at`, no `deleted_at`). §5 is about exactly this.
2. **`Date`, `TypeId` and `IsAccountingInvoice` are get-only.** So *"altering a voucher's DATE is
   warn-and-proceed"* (ORCHESTRATOR RULING 2) **cannot be done by mutating the voucher** — it forces
   construction of a **new `Voucher` carrying the SAME `Guid`**. That is the shape `Replace` must take, and it
   is not optional.
3. There is **no `VoucherNumber` string**. The raw sequence is `int Number`; the rendered document number is
   `Company.FormatVoucherNumber(Voucher)` (`Company.cs line 1004`) → `VoucherNumberFormatter.Render(type, number, date)`
   = prefix ++ zero-padded number ++ suffix, with date-selected affix rows. **A pure projection — nothing to
   unwind.** Inventory overload at `Company.cs line 1015`.

`EntryLine` (`src/Apex.Ledger/Domain/EntryLine.cs line 22`) is **fully immutable** — every property `{ get; }`. Its
detail hangers are `BillAllocations :40`, `CostAllocations :50`, `BankAllocation :60`, `Forex :71`, `Gst :83`,
`Tds :96`, `Tcs :110`, `Payroll :122`. The ctor (`:127`) enforces exactly one invariant (`:144`):
`payroll.Amount == amount`.

`InventoryVoucher` (`Domain/InventoryVoucher.cs line 21`) is deliberately a separate class — a stock/order voucher
posts no accounting entry (DP-5) and cannot satisfy Σ Dr = Σ Cr. `Cancelled` at `:49`; **no `Optional`**.

## 1.6 Persistence — the store is a SNAPSHOT, not the system of record

`src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs line 1790` `public void Save(Company company)`:
one transaction → `ReadStoredSourceOrders` → **`DeleteCompanyRows`** (delete-all) → ~40 `Insert*` calls
re-inserting the whole aggregate in FK order → commit. **A save is O(whole book).**

`Load` at `:1277` rebuilds and then **re-posts through the real engine**:

```csharp
// SqliteCompanyStore.cs line 1597-1599
var service = new LedgerService(company);
foreach (var v in ReadVouchers(companyId))
    service.Post(v, CostAllocationStrictness.Legacy);
```

Inventory vouchers are rehydrated *directly* (`:1586-1587`, `company.AddInventoryVoucher(iv)`) — an asymmetry
explained in-source at `:1584-1585`.

**🔴 AND THE ORDER IS PERSISTED.** `ReadVouchers` selects `FROM vouchers WHERE company_id = $cid ORDER BY rowid`
(`SqliteCompanyStore.cs line 4082`), and `Load` re-posts in that order, appending. **So the in-memory list position of
a voucher survives save→load and is a real, user-visible property** (the Day Book order of same-dated vouchers).
This is the hard evidence for `plan.md`'s "list position" identity requirement — it is not a nicety.

**🔴 A LATENT DEFECT FOUND WHILE MAPPING — `IVoucherRepository.Remove` IS INCOMPLETE.**
`SqliteCompanyStore.cs line 1919` `public void Remove(Guid companyId, Guid voucherId)` deletes, in one transaction:
`bill_allocations` → `cost_allocations` → `bank_allocations` → `entry_lines` → `vouchers`. It does **NOT** delete
`tds_lines` (`Schema.cs line 990`), `tcs_lines` (`:1007`), `payroll_lines` (`:1824`) — all FK `entry_lines` — nor
`voucher_inventory_lines` (`:1327`) or `pos_tender_allocations` (`:1502`), which FK `vouchers` directly. Compare
`DeleteCompanyRows` (`:4446-4467`), which does handle all of them. With `PRAGMA foreign_keys` on, removing a
TDS-carved or item-invoice voucher through this path would fail or orphan.

**It is not on the live path today** (the app uses whole-company `Save`), which is why it has never bitten.
**But it is exactly the method a "delete a voucher" feature would be tempted to reach for.** Named here so the
implementer either fixes it or deliberately does not touch it. See §8.

## 1.7 Day Book and registers — what exists

- `LedgerBalances.CountsAsOf` (`src/Apex.Ledger/Reports/LedgerBalance.cs line 45-51`) is the **single shared as-of
  convention**: excludes `Cancelled`, excludes `Optional`, excludes `PostDated`-and-not-yet-due, counts only
  `Date <= asOf`. `IsProvisionalBaseType` (`:36`) additionally carves out `Memorandum` and `ReversingJournal`.
- `ItemInvoiceStock.Counts(Company, Voucher, DateOnly)` (`Services/ItemInvoiceStock.cs line 45`) re-implements the
  same rule on the stock side, keyed on the **presence of item lines**, not the type's `AffectsStock` flag.

**⇒ Cancellation is ALREADY honoured by every balance and every stock figure.** `Cancel` works today at the
engine level; only the UI and the *visual* treatment are missing. That makes S3 by far the cheapest of the
three remaining verbs (§6).

- Alt+A on the Day Book already opens an add-voucher picker beside the live report:
  `MainWindow.axaml.cs line 629-636`, `vm.OpenAddVoucherFromReport()`, citing *Book p.431 "Add a voucher in a report"*
  — which §2.6 independently re-confirms.
- The app-wide **Alt+X** arm at `MainWindow.axaml.cs line 350-355` currently calls `vm.CancelVoucher()`, i.e.
  **abandon the entry screen**. It is guarded only by `e.KeyModifiers.HasFlag(KeyModifiers.Alt)` — **no
  `!IsPickerOpen`, no screen scope.** `plan.md`'s VL-3 description of this arm is accurate.

## 1.8 The regression surface is genuinely wide — W0-7 verified

`1de940e` *"test(fixture): W0-7 — extend the populated fixture to every voucher family"*, **2026-08-10**, is an
ancestor of HEAD (`git merge-base --is-ancestor 1de940e HEAD` ⇒ true). The lock lives at
`tests/Apex.Desktop.Tests/Fixtures/PopulatedFixtureCoverageTests.cs` and asserts coverage **as data, not as a
comment**: `SeededBaseTypes(c)` is `c.VoucherTypes.Select(t => t.BaseType).ToHashSet()` — derived from the
company's own seed — and `PostedBaseTypes(c)` unions **both** `c.Vouchers` and `c.InventoryVouchers`. POS
(`VoucherType.UseForPos`) and Attendance (`AttendanceEntry` rows, never a posted voucher) are asserted
separately because a base-kind sweep cannot see either. It also locks **odd-valued discipline**
(`HasOddPaisa`, `HasOddQuantityOrRate`) and a **real SQLite round trip**.

**The prerequisite is discharged. The reasoning it was built on still binds** — see §7.4.

---

# §3 — THE HARD PART: what amending a posted voucher means for derived state

## 3.0 The headline, stated before the detail because it inverts the expected risk

**Almost nothing needs unwinding, and the small part that does needs the OPPOSITE of unwinding.**

Every accounting, inventory, GST-report, outstandings, cost, interest, budget and order figure in this system is
a **stateless pure projection recomputed from `Company.Vouchers` + `Company.InventoryVouchers` on every single
call**. There is no cache, no materialised view, no running-balance table, no persisted cost-layer stack — I
grepped the whole SQLite schema and there is no `ledger_balances`, no `stock_layers`, no `outstanding_bills`, no
`voucher_number_counters`. For that entire surface, **replacing the voucher in the list is sufficient and
complete.**

The real risk is a much smaller, much sharper set: **records stored ALONGSIDE the voucher that snapshot a fact
about it**. And the plan's phrasing — *"the old voucher's derived records must be unwound before the
replacement re-derives"* — is right for half of them and **actively destructive for the other half**, because
half of them are not derived from the voucher's content at all. They are **relationships to the outside world**
(an IRN the IRP issued, a challan the bank stamped, a bank date a human ticked, a §34 link to another voucher).
Re-deriving those means **inventing** them; unwinding them means **losing** them.

**⇒ The single most important design rule in this phase: classify every voucher-attached record into
RE-DERIVE / CARRY-FORWARD / REFUSE before writing a line of `Replace`.** §3.3 does that classification.

## 3.1 COMPUTED ON THE FLY — nothing to unwind (verified, with the recompute site)

| Derived figure | Recompute site | Verdict |
|---|---|---|
| Ledger closing balance | `Reports/LedgerBalance.cs line 95-106` — `SignedOpening + Σ line.Signed` over `company.Vouchers`, every call | ON-FLY |
| Trial Balance | `Reports/TrialBalance.cs line 31-58` | ON-FLY |
| Balance Sheet | `Reports/BalanceSheet.cs line 65-133` (closing stock derived at `:133`) | ON-FLY |
| Profit & Loss | `Reports/ProfitAndLoss.cs line 71` (stock at `:102`) | ON-FLY |
| Stock on-hand | `Services/InventoryLedger.cs line 135-180` — replays opening + every movement; Physical Stock acts as a checkpoint (`:193-207`) | ON-FLY |
| **Stock valuation, incl. FIFO/LIFO layers** | `Services/StockValuationService.cs line 274-318` — **I opened this myself**: `BuildLayers` opens with `var layers = new List<Layer>(); // oldest-first` and replays the movement stream into a **local**. `Layer` is a `private readonly record struct` (`:505`) with no table and no id. Moving average `RunAverage` `:329-363`; issue costing `IssueValue` `:114-147` rebuilds from scratch | ON-FLY |
| Outstandings / bill ageing | `Reports/Outstandings.cs line 124-196` — transient `Dictionary<string, BillState>`; `BillState` is a local `private sealed class` (`:245-253`) | ON-FLY |
| Cost centre / category | `Reports/CostReports.cs line 136`, `:167` | ON-FLY |
| GSTR-1 / GSTR-3B | `Reports/Gstr1.cs line 217`, `Reports/Gstr3b.cs line 130` — re-read off posted `EntryLine.Gst` every call | ON-FLY (over stored line tax — see §3.2) |
| Electronic cash/credit ledgers | `Reports/ElectronicLedgersView.cs line 50` | ON-FLY |
| TDS/TCS deduction totals, challan reconciliation | `Reports/ChallanReconciliation.cs line 43-83` | ON-FLY |
| Interest calculation | `Reports/InterestCalculation.cs line 186` | ON-FLY |
| Budget variance | `Reports/BudgetVariance.cs line 45` | ON-FLY |
| Order fulfilment / reorder status | `Reports/OrderFulfilment.cs line 326`; `Reports/ReorderStatus.cs` | ON-FLY |
| Batch on-hand & batch valuation | `Services/BatchStockService.cs line 56`, `:135` | ON-FLY |
| Bank Reconciliation **statement** | `Reports/BankReconciliation.cs line 78-104` | ON-FLY (but see §3.3 for the stored bank *date*) |
| Saved report views | config only — `Schema.cs line 1561-1563` states in-source that a saved view *"can never go stale (ER-9)"* | ON-FLY |
| Scenarios / Optional / Post-dated / Reversing | plain flags, filtered per query by `LedgerBalances.CountsAsOf` (`LedgerBalance.cs line 45-51`) | ON-FLY |

**Two costs of this design worth naming even though they are not blockers.** (a) `TotalClosingStockValue`
(`StockValuationService.cs line 98-104`) loops every item calling `ClosingValue`, which itself replays the whole
book — so a Balance Sheet is roughly quadratic in book size, and every lifecycle test that asserts a Balance
Sheet pays that. (b) **Nothing here will tell you the unwind was wrong**; a stale figure is impossible, so the
tests in §7 must attack §3.2 and §3.3, not §3.1.

## 3.2 STORED **ON** THE VOUCHER'S OWN LINES — dies with the voucher, but MUST be RE-COMPUTED, never copied

These live on `EntryLine`/`Voucher` and vanish when the voucher is replaced. There is no orphan risk. The risk
is the opposite: **a `Replace` that copies the old lines forward keeps figures that the amended content no
longer justifies.**

| Stored on the line | Where | Computed at posting by |
|---|---|---|
| `EntryLine.Gst` (`GstLineTax`: head, rate bp, taxable value, RCM flags) | `EntryLine.cs line 83`; cols `gst_*` on `entry_lines`, `Schema.cs line 963-985` | `Services/GstService.cs line 519` `ComputeLineTax`, `:610` `ComputeInvoiceTax`, stamped `:685` |
| `EntryLine.Tds` / `.Tcs` (assessable value, rate bp, amount, deductee, PAN-applied) | `:96` / `:110`; `tds_lines` `Schema.cs line 990`, `tcs_lines` `:1007` | `Services/TdsService.cs line 58` `ComputeWithholding`, cumulative-FY threshold `:106` |
| `EntryLine.Payroll` | `:122`; `payroll_lines` `Schema.cs line 1824` | `Services/PayrollComputationService.cs` |
| `EntryLine.BillAllocations` / `.CostAllocations` | `:40` / `:50`; `bill_allocations` `:1068`, `cost_allocations` `:1097` | keyed by the operator |
| `EntryLine.Forex` | `:71` | keyed / rate master |
| `Voucher.InventoryLines`, `Voucher.PosTenders` | `Voucher.cs line 43`, `:70`; `voucher_inventory_lines` `Schema.cs line 1327`, `pos_tender_allocations` `:1502` | keyed, direction stamped by `Post` |

**🔴 THE INVERSION TRAP — "the posted `Voucher.Lines` is NOT what the operator keyed".** `plan.md` names this
and it is real. The clearest case is a TDS-carved purchase: the operator keys a **gross**, and the *posted*
lines carry the **net** party credit plus a separate TDS-payable leg. To re-open the entry screen pre-filled,
`Replace` must **invert the carve to recover the gross**, and on Accept **re-carve from the restored gross** —
not re-apply the stored carve to a new base. Get this wrong and the amount drifts by exactly the carve.
`plan.md`'s driving test (2) is precisely this, and §7 keeps it with an **odd-paise** carve so a ±₹0.50 drift
cannot hide.

**⚠️ A SECOND INVERSION THAT `plan.md` DOES NOT NAME.** `PayrollComputationService` and `GstService` are the
other two writers whose output is on the line rather than in the operator's head. Any `ForAlter` rehydration
must have a stated answer for each of the six rows above — "invert it", "re-key it", or "refuse alteration for
this family". ORCHESTRATOR RULING 1 already refuses payroll and the three `InventoryVoucher` entry screens,
which disposes of the payroll row; GST and TDS/TCS still need theirs.

## 3.3 🔴 STORED **BESIDE** THE VOUCHER — the real unwind surface, classified

Each of these is a separate object on `Company`, pointing at the voucher by `Guid`. **This table is the
deliverable of §3.** The right column is the design decision, not an observation.

| Record | Held at | Points at the voucher via | Freezes | **RE-DERIVE / CARRY / REFUSE** |
|---|---|---|---|---|
| **`EInvoiceRecord`** | `Company.cs line 500`; `einvoice_records` `Schema.cs line 480` | `SourceVoucherId` (`EInvoiceRecord.cs line 22`), `DocumentNumberUpper` (`:29`) | IRN, AckNo, AckDate, SignedQr, SignedJson | **REFUSE** when `Status == Generated` (ORCHESTRATOR RULING 2); **CARRY** otherwise. Never re-derive: an IRN cannot be recomputed. |
| **`EWayBillRecord`** | `Company.cs line 504`; `eway_bills` `Schema.cs line 502` | `SourceVoucherId`, `DocumentNumberUpper` | **`ConsignmentValuePaisa`** (`EWayBillRecord.cs line 52` — *I opened this*: *"The Rule-138 consignment value in integer paisa (computed off the posted lines, §1.3), **stored for audit**"*) | **CARRY + WARN.** ⚠️ **Highest silent-divergence risk in the phase**: amend the amounts and the EWB states a consignment value the invoice no longer supports, with nothing to detect it. |
| **`TdsChallan` / `TcsChallan` + `ChallanVoucherLink` / `TcsChallanVoucherLink`** | `Company.cs line 663-678`, `:681-706`; `tds_challans` `Schema.cs line 1022`, `challan_voucher_links` `:1036` | `(ChallanId, VoucherId)` pair | ChallanNo, BsrCode, DepositDate, Amount, Section | **CARRY.** ⚠️ Note the asymmetry I verified at `Reports/ChallanReconciliation.cs line 85-92`: `ChallanHasLiveVoucher` drops a challan whose booking voucher `is { Cancelled: false }` fails — so **cancel and delete SELF-HEAL the report; amend does NOT.** Amending the booking voucher's amount leaves a challan whose frozen `Amount` no longer matches, and the reconciliation simply reports the wrong Remaining. |
| **`GstCreditDebitNoteLink`** | `Company.cs line 508` | `CdnVoucherId`, `OriginalInvoiceVoucherId` (both `Guid`, `GstCreditDebitNoteLink.cs line 23`, `:29`) | `OriginalInvoiceDate` (drives the §34(2) 30-Nov cut-off), `ReasonCode`, `Is9BTarget` | **CARRY — and this is exactly what `plan.md`'s driving test (3) protects.** Preserving the **Guid** is what preserves the link; the object is immutable and needs no touching, *provided* the replacement keeps the Id. |
| **`GstSetoffLine`** | `Company.cs line 530`; `gst_setoff_lines` `Schema.cs line 623` | `VoucherId` | period set-off paisa | **RE-DERIVE — but not by us.** `Services/GstSetOffService.cs line 317-321` already deletes prior lines + their voucher and re-posts on a period re-run. Amending a source sales/purchase voucher afterwards leaves the set-off **stale until the operator re-runs the period**. **Named gap, not fixed here.** |
| **`ItcReversal`** | `Company.cs line 534`; `itc_reversals` `Schema.cs line 642` | `SourceVoucherId`, `SourceLineId` (`ItcReversal.cs line 46-49`) | frozen `CgstPaisa/SgstPaisa/IgstPaisa/CessPaisa`, `CreatedAt` | **CARRY + named gap.** Same shape as set-off. |
| **`Gstr2bReconResult`** | `Company.cs line 526` | `MatchedVoucherId` (`Gstr2bReconResult.cs line 22-40`) | `TaxableVariancePaisa`, `TaxVariancePaisa`, `ReconciledAt` | **CARRY + named gap** — a frozen variance against a voucher that just changed. |
| `GstAdvanceReceipt` | `Company.cs line 512` | voucher link | snapshot amounts | **CARRY + named gap** |
| `RcmDocument` (self-invoice / Rule-52 payment voucher) | `Company.cs line 496`, `:712-719`; `rcm_documents` `Schema.cs line 434` | voucher link; own series via `NextRcmDocumentSeries` (`Company.cs line 718-719`, `Max(SeriesNumber)+1`) | series number | **CARRY** |
| `GstChallan` (PMT-06) / `GstDrc03` | `Company.cs line 538`, `:541` | voucher link | deposit facts | **CARRY** |
| **`BankAllocation.BankDate`** | `EntryLine.BankAllocation` → `BankAllocation.cs line 39` `public DateOnly? BankDate { get; set; }` | it IS a line child | the human's reconcile tick | **🔴 CARRY — AND THIS ONE IS NOT IN `plan.md` AT ALL.** See §3.4. |
| `InventoryVoucher.OrderLinks` / `material_order_links` | `InventoryVoucher.cs line 87`; `Schema.cs line 1555` | `Guid` list / FK to `inventory_vouchers` | order↔delivery linkage | **CARRY**; and **deleting an order orphans the link** — a VL-2 guard question, §6. |

## 3.4 🔴 THE DEFECT `Replace` WILL CREATE IF NOBODY READS THIS PARAGRAPH — the bank reconciliation date

`Reports/BankReconciliation.cs line 151-170` `SetBankDate(Company, Guid voucherId, Guid bankLedgerId, DateOnly? bankDate)`
**mutates a POSTED voucher's line after posting**:

```csharp
// BankReconciliation.cs line 164
line.BankAllocation.BankDate = bankDate;
```

So `BankDate` is a fact **written onto the voucher graph by a later human action**, and it exists **nowhere in
the voucher entry screen**. A `Replace` implemented the obvious way — rehydrate the entry VM from the voucher,
let the operator edit, rebuild `EntryLine`s from the VM, swap them in — **silently destroys every bank
reconciliation date on that voucher**, un-reconciling a bank line that a human had ticked, with no message and
no test failing.

**Design requirement (blocking):** `Replace` must carry `BankAllocation.BankDate` forward for any line whose
ledger + bank-allocation identity is unchanged, and must **clear it deliberately, with a warning**, when the
line's amount changed (a cleared item that no longer matches the statement is not cleared). **This needs its
own test** (§7.3, T-6). It is the clearest example of the §3.0 rule: a voucher-attached fact that is not a
function of voucher content.

## 3.5 What Cancel and Delete need — much less, and it is worth saying why

**Cancel needs NOTHING unwound.** `LedgerBalances.CountsAsOf` (`LedgerBalance.cs line 45-51`) already excludes
`Cancelled`, and `ItemInvoiceStock.Counts` (`ItemInvoiceStock.cs line 45`) does the same on the stock side. Setting
the flag is the entire semantic. The open questions for Cancel are **presentational** (the greyed/struck row,
`plan.md line 267`), **referential** (the §34 and advance pickers that filter on base type only and would still
offer a cancelled invoice as the original supply — `plan.md` names `BuildSection34Pickers()` /
`BuildAdvancePickers()`), and **statutory** (an e-invoiced document — §6).

**Delete needs the §3.3 table read in the DELETE direction**: the voucher goes, and every row pointing at it by
`Guid` becomes an **orphan pointer**. `ChallanReconciliation` self-heals (it looks the voucher up and finds
nothing). Nothing else was checked. **A referential guard is therefore part of VL-2's definition, not a nicety:**
refuse to delete a voucher that is the `OriginalInvoiceVoucherId` of a live §34 note, or that carries a
`Generated` e-invoice, or that is linked to a challan — **with the count of blockers**, which is what `plan.md`
already specifies and which §2.5's corpus rule (*"You cannot delete any ledger, if any transaction(s) has been
already made"*) is the master-side twin of.

---

# §4 — NUMBERING

## 4.1 What the code does today — and it is worse than "leaves a gap"

`LedgerService.cs line 154-161`, read in full:

```csharp
public int NextNumber(Guid voucherTypeId)
{
    var max = 0;
    foreach (var v in _company.Vouchers)
        if (v.TypeId == voucherTypeId && v.Number > max)
            max = v.Number;
    return max + 1;
}
```

`InventoryPostingService.cs line 149-156` is byte-identical over `_company.InventoryVouchers`. **There is no stored
counter and no `last_used_number` column anywhere in the schema.** The rendered document number is a pure
projection on top (`VoucherNumberFormatter.Render`, `Company.FormatVoucherNumber` at `Company.cs line 1004`/`:1015`).

**Consequences, stated exactly:**

| Action | Effect on the number |
|---|---|
| **Cancel** | Voucher stays in `_vouchers`, so it still counts toward `max`. **Number retained, sequence unbroken, no reuse.** ✅ Correct, and it is correct today by accident of the flag design rather than by a rule anybody wrote down. |
| **Delete a MID-sequence voucher** (say #7 of 1…10) | `max` is still 10. Next post takes 11. **Permanent gap at 7.** |
| **🔴 Delete the HIGHEST-numbered voucher** (say #10 of 1…10) | `max` drops to 9. **The next post REUSES 10.** Two different documents, at different times, with the same tax-invoice number — and the first one no longer exists to prove which was which. |

**The last row is the finding.** `LedgerService.cs line 99` documents Delete as *"may leave a gap in numbering"*,
which describes the mid-sequence case and **silently misses the reuse case**. The doc comment is not wrong so
much as incomplete in the direction that matters.

## 4.2 What the corpus says — nothing

**NOT SETTLED (C-5, §2.7).** No admissible corpus PDF discusses what happens to a deleted voucher's number.
The nearest thing is the Book's delivery-challan rule, PDF **p.81**: *"It must be serially numbered and number
does not exceed 16 characters. This can be in a single series or in multiple series."* — which speaks to the
form of a series, not to reuse after deletion. **Do not fill this gap by invention.**

## 4.3 What the project's own doctrine already says — and it contradicts the engine

This is the important half, because the doctrine is already **in the shipped code**, written during Phase 10.7.
`src/Apex.Desktop/ViewModels/VoucherNumberingConfigViewModel.cs line 399-409` — I opened it:

> *"True when a voucher carries a filed statutory document whose number is **legally frozen**
> (numbering-design-v2 §5.4). For e-invoicing the frozen signal is any status that REACHED the IRP: GENERATED
> (IRN issued) OR CANCELLED (the IRN was reported and is **permanently burned — a cancelled doc-no is never
> reusable**, §2.5)."*

```csharp
private bool IsFiledDocument(Voucher v)
{
    if (_company.FindEInvoiceRecordForVoucher(v.Id) is { Status: EInvoiceStatus.Generated or EInvoiceStatus.Cancelled }) return true;
    if (_company.FindEWayBillRecordForVoucher(v.Id) is not null) return true; // finder already excludes Cancelled
    return false;
}
```

**Three things follow, and all three are conflicts this phase must resolve rather than inherit:**

1. **🔴 THE DOCTRINE AND THE ENGINE DISAGREE.** The doctrine says a filed document number is *permanently
   burned and never reusable*. `NextNumber` will hand that very number to the next voucher the moment the
   filed voucher is the highest-numbered one and somebody deletes it. Today that is unreachable (nothing calls
   `Delete`); **VL-2 makes it reachable.** This is a defect that this phase *creates* unless it is fixed in the
   same slice.
2. **A NAMED SOURCE THAT IS NOT IN THE REPOSITORY.** `numbering-design-v2 §5.4` and `§2.5` are cited by
   shipped code and **do not exist under `docs/`** (`docs/` holds `adr/0001-tech-stack.md`,
   `design/accounting-core.md` and 20 top-level files; no numbering design doc). The doctrine's *reasoning* is
   therefore unverifiable by anyone reading the repo. **Owed to the post-merge documentation slice**: either
   land the design note or restate its rule in-repo.
3. **A SMALL INCONSISTENCY WITH `plan.md`.** ORCHESTRATOR RULING 2 says *"Refuse alteration only when
   `EInvoiceStatus.Generated`, not `Pending`"* — silent on `Cancelled`. `IsFiledDocument` treats
   **`Generated` OR `Cancelled`** as frozen. For **alteration** the ruling is defensible (a cancelled IRN's
   voucher content is no longer filed); for **numbering** it is not (the doc-no stays burned either way).
   **Design call: keep the ruling for alteration, use `IsFiledDocument`'s two-status test for numbering.**
   Recorded so the difference is deliberate.

## 4.4 Statute — what I will and will not claim

The repository already treats a tax-invoice serial number as a **CGST Rule 46 particular** in many places
(`src/Apex.Ledger.Io/InvoicePrintData.cs line 31`, `:34`; `src/Apex.Ledger/Reports/GstReportSupport.cs line 334`;
`src/Apex.Desktop/Services/VoucherPrintProjector.cs line 658` calls a Rule 46 item *"a Rule 46(b) particular"*).
That a tax-invoice number is a Rule-46 particular is therefore **the project's own established position, cited
in shipped code**.

**What I am NOT asserting:** the exact clause letter, the precise wording of the consecutive-serial-number
requirement, or any conclusion about legality of reuse. That is a law fact and the standing rule is that law
facts are **web-verified against official sources, never asserted from memory** — and no corpus PDF settles it.

**⇒ USER/ORCHESTRATOR DECISION REQUIRED (R12), and it is cheap to state:** should a deleted voucher's number
be **retired** (never reissued) rather than reused? My recommendation and its reason are in §4.5.

## 4.5 Recommendation — the minimum change, and why it is minimal

**Recommendation: make `NextNumber` monotone per type by construction, and do it as part of VL-2 (S4-Delete),
not as a separate slice.**

The cheapest correct form needs **no schema change and no counter table**: retire numbers by **not deleting the
highest number's evidence**. Two candidate mechanisms, in preference order:

- **(a) PREFERRED — refuse the delete, offer the cancel.** If the voucher carries a filed statutory document
  (`IsFiledDocument`, `VoucherNumberingConfigViewModel.cs line 404`), **Delete is refused with a named message and
  Cancel is offered instead.** This is exactly TallyPrime's own two-verb shape (`Alt+D` delete vs `Alt+X`
  cancel, §2.2), it needs no new state, and cancelling **already** preserves the number by §4.1. It also
  matches §2.5's corpus rule in spirit: Tally refuses a destructive act rather than silently doing something
  lossy.
- **(b) FALLBACK, only if (a) is judged too strict** — teach `NextNumber` a floor. There is no place to store
  one without schema, so this would need v54 (§9). **Do not do this in the first pass.**

**What (a) does NOT fix, stated plainly:** deleting the highest-numbered voucher that is *not* filed still
reuses its number. That is defensible (an unfiled document number has no statutory life) and it is what
TallyPrime's own "may leave a gap" behaviour implies for the mid-sequence case. **Record it as a known,
accepted behaviour in the census row (§10), not as a silent one.**

---

# §5 — AUDIT: the exclusion is confirmed, and here is what it costs

## 5.1 Confirmed — Edit Log / Tally Audit is excluded by standing user decision

Verified in `plan.md`, four independent places:

- `plan.md line 39` — *"**Phase 11 and the REST of Phase 10 — TallyVault, Security Control / roles, Edit Log / Tally Audit, …**"* in the exclusion banner.
- `plan.md line 111-112` — the excluded-capability list: *"**Security & administration:** TallyVault, Security Control, user roles, password policy, **Edit Log / Tally Audit** (audit trail)."*
- `plan.md line 1787` — Phase 10.11's own scope fence: *"it builds **NO audit trail, NO Edit Log, NO security roles, NO user attribution.**"*
- `plan.md line 1872` USER DECISION 3 — *"**Alter and delete ship with NO audit trail**, by the earlier decision that excluded Phase 10."*

And the precedent it is held to, verbatim from `src/Apex.Ledger/Services/MasterAlterationRules.cs line 51-54`:

> *"**Out of scope by ruling:** the alteration **audit trail** (who altered what, when, from what to what). Tally
> keeps one, and it belongs with the Phase-10 security/roles/audit infrastructure this project has not built yet
> — **writing half of it here would leave an audit log no one can query or protect.** Deferred to Phase 10
> deliberately; nothing in this file records history."*

`plan.md line 3899-3900` additionally records that these are **two** deliverables, not one: *"**Edit Log** =
field-level before/after on every master/voucher; **Tally Audit** = the reviewer's audit-summary report. Build
both; don't conflate."*

**⇒ Design the three verbs with no audit trail. Confirmed, not assumed.**

## 5.2 What that costs — stated plainly, because a gate has to acknowledge it

The domain has **no place to put history even if we wanted to**. Verified in §1.5: `Voucher` carries no
`CreatedAt`, no `ModifiedAt`, no user field; `Schema.cs line 941-961` (`vouchers`) has no `created_at`,
`modified_at` or `deleted_at`. `cancelled` is the only status column, and it is a bare `INTEGER NOT NULL`.

So after this phase, **all six of these are true simultaneously**:

1. **An altered voucher is indistinguishable from one that was always that way.** No before/after, no flag, no
   timestamp, no count of alterations.
2. **A deleted voucher leaves nothing behind at all** — not a tombstone, not a gap marker, not a number
   reservation (§4). The only trace is a hole in the sequence, and §4.1 shows even that disappears when the
   deleted voucher was the highest-numbered one.
3. **A cancelled voucher is the ONLY one of the three that leaves evidence** — the row survives with
   `Cancelled = true`. **That is an argument for making Cancel the default gesture and Delete the exceptional
   one**, and it is a free argument: it costs nothing and it aligns with §4.5(a).
4. **There is no "who".** No user identity exists in the product at all (Security Control is excluded), so
   attribution is not merely unrecorded — it is unrecordable.
5. **A reviewer cannot answer "was this book edited?"** For a CA-facing product this is the material gap, and
   it is materially worse than the master-alteration gap `MasterAlterationRules` already deferred, because a
   master rename is visible in its effects while a voucher amendment is designed to be invisible.
6. **The gap widens monotonically with use.** Every alteration and deletion after this phase is permanently
   unrecoverable, exactly as every wrong figure before this phase was.

## 5.3 The three things I recommend doing anyway, because they cost ~nothing and are not an audit trail

None of these builds an Edit Log; all three shrink the blast radius, and each can be dropped without touching
the design.

- **(a) Prefer Cancel over Delete in the UI.** Where both are offered, Cancel is the default and Delete is the
  one that has to be chosen. Free (§5.2 item 3), corpus-consistent (TallyPrime ships both verbs, §2.2), and it
  converts the commonest destructive act into an evidence-preserving one.
- **(b) Refuse Delete on a filed statutory document and offer Cancel instead** — §4.5(a). This is the
  numbering fix and an evidence fix in one guard.
- **(c) Re-state the consequence AT THE GATE, in front of a working feature.** `plan.md` already requires this
  (*"the NO-AUDIT-TRAIL consequence is re-stated at the gate and acknowledged, not assumed"*). Keep it, and
  make the demonstration concrete: alter a posted invoice in the running app, then show that **nothing anywhere
  in the product** can tell the user it was altered.

---

# §6 — THE SLICE SHAPE

## 6.1 The corrected starting point

**Two of the five planned slices are already merged** (§1.1). What remains is **S3 Cancel, S4 Delete, S5 Alter**.
`plan.md`'s slice list must be amended to say so, or the first implementer re-does work.

I recommend one further change: **split S5.** `plan.md` sizes it *"XL / HIGH — last and largest; the only slice
that rebuilds a posted aggregate"*. That is exactly the argument for not shipping it as one diff. A single XL
slice puts the engine contract, the rehydration inverse and the tax-carve inversion in front of one reviewer at
once, and the failure mode this project has repeatedly hit is a defect that passes the full suite because the
test that would have caught it was written against the same misunderstanding as the code.

## 6.2 Proposed slices, in order, with what each PROVES

| # | Slice | Size / risk | What it proves — the one sentence a gate can check |
|---|---|---|---|
| **S3** | **Cancel on Alt+X** | M / med | A posted voucher can be taken out of the books **without destroying anything**, its number stays in sequence, and every report already agrees. |
| **S4** | **Delete on Alt+D** | L / med | A voucher/ledger/group can be removed **behind a confirmation and a referential guard that names its blockers**, and a filed document cannot be silently un-numbered. |
| **S5a** | **`LedgerService.Replace` — ENGINE ONLY, no UI** | M / **HIGH** | The three identities (Guid · Number · list position) survive, a **rejected** replacement leaves the original **byte-identical and at its index**, and an altered book equals a directly-posted book on **every** derived figure. |
| **S5b** | **`ForAlter` rehydration — simple families only** | L / med | A posted voucher re-opens **pre-filled** and re-accepts unchanged to a byte-identical book; every family that cannot yet round-trip is **refused with a named message**, never silently. |
| **S5c** | **The carve inversions + the CARRY table** | L / **HIGH** | A TDS-carved / GST-stamped / bank-reconciled voucher survives an alteration with its tax **re-derived from the restored gross** and its outside-world links **carried, not rebuilt**. |

**Gates (R9/R12) after S3, after S4, after S5a, after S5c.** S5a's gate is the important one: it is the last
point at which the engine contract can change cheaply.

## 6.3 S3 — Cancel. Contents, and the one trap that will bite

**Why first:** the engine is *complete*. `LedgerService.Cancel` (`:92`) is three lines, `LedgerBalances.CountsAsOf`
(`LedgerBalance.cs line 45-51`) and `ItemInvoiceStock.Counts` (`:45`) already exclude cancelled vouchers, and the
engine method **is already covered by tests** (`tests/Apex.Ledger.Tests/CostCentreTests.cs line 373`,
`CostAllocationParallelSetTests.cs line 378`, `InterestTests.cs line 715`, `Inventory/ItemInvoiceTests.cs line 319`, `:341`;
inventory twin at `Inventory/InventoryReportsTests.cs line 542`, `:914`). **S3 is pure wiring plus presentation.**

Contents:
1. **Delete the app-wide Alt+X arm** at `MainWindow.axaml.cs line 350-355`. Escape already reaches `Back()`.
2. **🔴 RENAME, DO NOT DELETE, the abandon verb.** `plan.md` says *"delete `CancelVoucher()` rather than
   repurpose it, so the compile breaks and every stale caller surfaces"*. **The intent is right; the wording
   will destroy a working feature if taken literally.** `MainWindowViewModel.CancelVoucher()` (`:4981`) is
   *abandon-the-entry-screen*, wired to **six** click handlers (`MainWindow.axaml.cs line 1118`, `:1153`, `:1274`,
   `:1284`, `:1300`, `:1310`). Rename it **`AbandonEntry()`** — the compile still breaks at every stale caller,
   which is the whole point, and the behaviour survives. **Blast radius in tests is exactly two files**:
   `tests/Apex.Desktop.Tests/InventoryVoucherEntryViewModelTests.cs` and
   `tests/Apex.Desktop.Tests/KeyboardArbitrationTests.cs` (one reference each — measured).
3. **New Alt+X arm**, narrowly gated: report-row context only, `!IsTyping`, `!IsPickerOpen`, no Ctrl.
4. **Confirmation** — our own string, recorded **UNVERIFIED-BY-DESIGN** (§2.7 C-3). Single prompt.
5. **`IsCancelled` on `ReportRow`, a `CancelledRowToBrushConverter`, the greyed Day Book row** (`plan.md line 267`).
   Record the greying as **ours** — the corpus does not attest struck-through (§2.7 C-2).
6. **Print overprint "CANCELLED"** on the two Io print DTOs.
7. **Close the two picker leaks in the SAME slice** — `BuildSection34Pickers()` / `BuildAdvancePickers()` filter
   on base type only, so a cancelled invoice would be offered as the original supply a §34 note adjusts. These
   go live the moment Cancel is reachable.

**NOT in S3:** un-cancel (RULING 3); the pure-inventory Cancel **UI** (the engine method
`InventoryPostingService.Cancel` at `:128` exists — record that the deferral is UI-only, which `plan.md`
currently implies is an engine gap); anything about alteration.

## 6.4 S4 — Delete. Contents, and the two guards that are not optional

1. Y/N confirmation on the **one** confirmation channel, keeping `ConfirmMasterAccept` / `DismissMasterAccept`
   by name (called by tests and the dispatcher). **Single prompt**, per §2.3 — ~~the corpus's double
   *"Delete Yes or No?" → "Are you sure Yes or No?"* is attested for a **group company**, not a voucher.~~
   🔴 **CORRECTED 2026-08-18 — read §2.3's correction block.** The single prompt is unchanged; its basis is now
   two records, (A) voucher and (B) master, and they must not be merged.
2. A new **pure** `Services/MasterDeletionRules.cs` on the `MasterAlterationRules` shape (throws, never mutates).
3. **Referential guard, from §3.3, refusing with the COUNT of blockers** — a voucher that is the
   `OriginalInvoiceVoucherId` of a live `GstCreditDebitNoteLink`, or is linked by a `ChallanVoucherLink` /
   `TcsChallanVoucherLink`, or carries an `EInvoiceRecord`/`EWayBillRecord`.
4. **Master-side guard, corpus-mandated** — *"You cannot delete any ledger, if any transaction(s) has been
   already made with that ledger"* (STUDY-GUIDE PDF p.67, §2.5). Refuse with the count.
5. **🔴 The numbering guard (§4.5a)** — refuse Delete on a filed statutory document (`IsFiledDocument`,
   `VoucherNumberingConfigViewModel.cs line 404`) and **offer Cancel**. Without it, **this slice creates the
   number-reuse defect §4.1 describes**, because it is the slice that makes `Delete` reachable.
6. Routing from Day Book / register drill / voucher detail / Chart of Accounts / Stock Item list.

**NOT in S4:** **company deletion** (USER DECISION 2 — split out; `CompanyStorage.Delete` swallows `IOException`
and `Company = null` appears nowhere); the `SqliteCompanyStore.Remove` completeness fix (§1.6) — **decide
explicitly**: my recommendation is *leave it and add a `// DO NOT USE — incomplete` comment*, because fixing it
invites someone to route delete through it instead of through `Save`.

## 6.5 S5a — `Replace`, engine only. The contract

```csharp
// LedgerService
public Voucher Replace(Guid voucherId, Voucher replacement);
// Company
internal void ReplaceVoucherInternal(Voucher existing, Voucher replacement);  // List[index] = replacement
```

**Five contract clauses, each with its reason:**

1. **Ordering: validate the replacement BEFORE removing the original.** `Post` mutates before it can fail
   (`LedgerService.cs line 46` stamps directions, `:51-52` assigns `Number`, `:54` appends), so a naive
   remove-then-post loses the original on a rejection. **The precedent is in the file**:
   `ConvertToRegular` (`:120-151`) posts first and removes second, *"only remove the memo once the real voucher
   is accepted"*. `Replace` must do the same in spirit: `EnsureValid` → then swap **in place**.
2. **Preserve the `Guid`.** 25+ tables `REFERENCES vouchers(id)`, and `GstCreditDebitNoteLink.OriginalInvoiceVoucherId`
   / `EInvoiceRecord.SourceVoucherId` / `ChallanVoucherLink.VoucherId` are all Guid pointers (§3.3). **The Guid
   is the only thing holding the outside-world links together.**
3. **Preserve the `Number`.** `Post` assigns when `Number <= 0` (`:51-52`), so passing `0` **renumbers a
   mid-sequence voucher to max+1**. Copy the original's `Number` onto the replacement before validating.
4. **Preserve the list index.** `List.Remove` + `Add` moves the voucher to the end; §1.6 proves the index is
   **persisted** (`ORDER BY rowid` at `SqliteCompanyStore.cs line 4082`) and therefore user-visible in the Day Book
   for same-dated vouchers. Use `_vouchers[index] = replacement`.
5. **`Date` and `TypeId` are get-only** (`Voucher.cs line 23`, `:17`), so `Replace` **must take a fully-constructed
   replacement `Voucher`**, not a mutator delegate. This is why the signature is `Replace(Guid, Voucher)` and
   not `Alter(Guid, Action<Voucher>)`.

**Also in S5a: the `DerivedStateSnapshot` helper** (§7.2). It is a test artefact but it belongs to this slice
because S5a is the slice whose correctness it defines.

## 6.6 S5b / S5c — and the refusals

**S5b** ships `VoucherEntryViewModel.ForAlter(...)` and the rehydration inverse of the line writers
(`ToBillAllocations()`, `ToCostAllocations()`, `ToInvoiceBillAllocations()` at `VoucherEntryViewModel.cs line 544`,
`:3045-3046`, `:4208`, `:4809`), **for families whose posted lines equal the keyed lines**. Everything else is
**refused with a named message** — including, temporarily, any voucher carrying `EntryLine.Gst`, `.Tds` or
`.Tcs`. A temporary refusal that is *named* is safe; a silent no-op is the failure mode RULING 1 exists to
prevent.

**S5c** removes those temporary refusals: the GST re-stamp, the **TDS re-carve from the restored gross**, the
**§3.4 bank-date carry-forward**, and the §3.3 CARRY/REFUSE table.

**Permanent refusals (ORCHESTRATOR RULING 1), each needing its own named message and its own test:** POS,
Manufacturing Journal, payroll, and the three `InventoryVoucher` entry screens.

**Also refused: `EInvoiceStatus.Generated`** (RULING 2). **Warn-and-proceed, not refuse:** a date change, and a
`Pending` e-invoice. **My addition (§3.3):** warn-and-proceed on an **active e-Way bill** too, because
`ConsignmentValuePaisa` is a frozen amount that will silently diverge.

**🔴 THIS SECTION STATED A PREDICATE, NOT A LIST, AND THE LIST IS NOW IN §6.6a (added 2026-08-19).** *"Families
whose posted lines equal the keyed lines"* is a test, and the only families named above are the COMPLEMENT — so
nothing here says which families S5b actually covers. **§6.6a is the derivation, and it supersedes this
section's complement in three places**: the temporary `Gst`/`Tds`/`Tcs` refusal is **not sufficient** (five
families carry no such line and still fail to round-trip), the base kind is **not the discriminator**, and the
*"three `InventoryVoucher` entry screens"* are **four** — the Manufacturing Journal is the fourth of them, not
a fifth family beside them. **Read §6.6a before writing any S5b code.**

## 6.6a 🔴 THE S5b ENUMERATION — the derivation §6.6 never made (added 2026-08-19)

**Why this subsection exists.** §6.6 above scopes S5b, verbatim, to *"families whose posted lines equal the
keyed lines"* — **a predicate, never a list** — and then enumerates only the COMPLEMENT (the permanent refusals
plus a temporary refusal of any line carrying `EntryLine.Gst` / `.Tds` / `.Tcs`). **A complement over an
unstated universe does not define a set.** §12.8 records what an unwritten family list already cost this phase
once: *"the mandated family test list was never written, so the family shipped green by construction"*. This
subsection states the universe, states the discriminator, and gives a verdict **with its evidence** for every
shape the product can post. It is the deliverable `plan.md`'s S5b blocking item asks for.

### 6.6a.1 The universe, and how it was counted

The universe was counted at five layers, because **no single layer is a partition of it**.

| Layer | What it is | Size | How counted |
|---|---|---|---|
| **L1 — base kinds** | the `VoucherBaseType` enum | **24 members** | `VoucherBaseType.cs lines 9-32`, counted with `grep -c "^    [A-Z][A-Za-z]*,$"` → 24 |
| **L2 — seeded base kinds** | what `CompanyFactory.CreateSeeded` puts on a company — exactly what `PopulatedFixtureCoverageTests.SeededBaseTypes` yields | **23 seeded** base kinds | `SeedVoucherTypes.cs` — sixteen core rows plus seven additional rows, every one a distinct base kind; `SeedVoucherTypes.Count` is a `const int` **23** with a runtime `Build()` guard that throws on any other total. The absent 24th is **Attendance**, deliberately un-seeded (decision D24-B) because nothing posts a `Voucher` of that kind |
| **L3 — aggregate** | which list the post lands in | **11 + 12** | `VoucherEffects.AffectsAccounts` names eleven kinds that post a `Voucher`; `VoucherEffects.IsInventoryBaseType` names twelve that post an `InventoryVoucher`. The two sets are disjoint and 11 + 12 = 23, so the aggregate split is itself a partition of L2 |
| **L4 — entry screens** | which VM keys it | **eight screens** | `VoucherEntryViewModel` (with **three** Accept paths), `PosBillingViewModel`, `ManufacturingJournalEntryViewModel`, `InventoryVoucherEntryViewModel`, `JobWorkOrderEntryViewModel`, `MaterialMovementEntryViewModel`, `PayrollVoucherEntryViewModel`, `AttendanceVoucherEntryViewModel` |
| **L5 — specialised voucher types sharing a base kind** | flags on `VoucherType`, invisible to any base-kind census | at least **six** | `IsPosSales` (Sales), `IsManufacturingJournal` (StockJournal), `IsStatPaymentType` (Payment), `IsRcmPaymentVoucher` (Payment), `IsGstStatAdjustment` (Journal), plus ordinary second series |

**🔴 The count in `plan.md`'s blocking item — "the universe here is 23 seeded base kinds" — is CORRECT at L2
and INSUFFICIENT as the universe.** It is correct because `SeedVoucherTypes.Count` really is 23 and
`SeededBaseTypes` really does return those. It is insufficient because L4 and L5 both cut ACROSS it: one base
kind (Sales) is keyed by three different Accept paths *and* a fourth screen; another (Payment) carries four
mutually-exclusive shapes on one base kind. **No figure written in the design or the plan is contradicted by
this count; the design simply never wrote one.**

> 🔴 **THE `file.cs lines NN` CITATIONS IN §6.6a WERE TAKEN BEFORE S5b WAS BUILT, AND S5b INSERTED ~700 LINES
> INTO THE FILE THEY MOSTLY CITE** (fix-pass finding L3-08). They are now stale by roughly 400 lines and — worse
> than dangling — they RESOLVE, into S5b's own code, and read plausibly: row 1's *"lines 3059-3086"* for
> `PostAndSave`'s `entryLines` projection now lands inside `AcceptAlteration`; row 16a's *"lines 2838-2841"* for
> `Accept`'s mode routing now lands on the `_rehydrating` field's doc comment; row 28's line for the provisional
> stamp now lands on the rollback's `_storage.Save`. The citation-verifying suite does not catch it, because
> §6.6a writes `file.cs lines NN` rather than the colon form that suite checks.
>
> **Read every §6.6a citation as a MEMBER NAME, never as a line number**: `PostAndSave`'s `entryLines` projection,
> `Accept`'s mode routing, `PostAndSave`'s provisional stamp, and so on. The member names are stable and are what
> the rest of this record's most durable passages already use.

### 6.6a.2 🔴 THE DISCRIMINATOR IS NOT THE BASE KIND — and the product already knows it

`MainWindowViewModel.PickAddVoucherType` (`MainWindowViewModel.cs lines 3233-3262`) is the existing precedent,
and it is written in exactly the order S5b's dispatcher must copy — **type flags first, base kind second** —
with its own comment saying why: *"Types whose identity means a DIFFERENT SCREEN, not just a different series
— checked before the base switch, because each of these shares its base kind with an ordinary type."*

An S5b `ForAlter` that switches on `VoucherBaseType` alone will hand a POS bill, a GST challan and a
manufacturing journal to the plain Dr/Cr grid. The verdict table below is therefore keyed on
**(base kind × discriminating predicate)**, and every row names the predicate that selects it.

### 6.6a.3 THE ENUMERATION — accounting aggregate (reachable by `LedgerService.Replace`)

`SIMPLE` = posted lines equal the keyed lines, so the rehydration inverse of the line writers can rebuild them
and S5b covers it. `REFUSE` = its posted shape cannot be recovered from the entry screen; named message, own
test, stays refused. `?` = **UNDETERMINED — do not treat as SIMPLE.**

🔴 **`DEFER` IS NOW TWO VERDICTS, because the single one was FALSIFIED by the slice it promised.** It used to read
*"round-trips only once a carve or stamp is inverted; refused by a NAMED message in S5b, **lifted in S5c**"* — and
S5c lifted some of its rows and not others, so a reader of the legend was told a thing about rows 8 and 23 that is
not true of them. Split, and each row re-verdicted explicitly:

* **`DEFER-LIFTED`** — refused by a NAMED message in S5b; the carve or stamp is inverted and the row round-trips
  **as of S5c**. Rows **3, 11, 12, 21**.
* **`DEFER-DEFERRED`** — refused by a NAMED message and **still refused after S5c**, with the blocker named on the
  row. Rows **8, 17, 22, 23**. A row here is not "waiting for the next slice" by default: row 8 is a **USER
  DECISION**, blocked by §6.6a.6's prohibition on an alteration performing a registration-side effect.

| # | Base kind | Discriminating predicate | Verdict | Evidence — what was checked |
|---|---|---|---|---|
| 1 | **Contra** | (none — one shape) | **SIMPLE** | Every appender is gated OFF for Contra: `TdsPossible` admits only Payment/Journal/Purchase; `RcmPossible` only Purchase/Journal; `CanBeAdvanceReceipt` only Receipt; `AdvanceActionForType` returns `None`; `CanBeItemInvoice` / `CanBeAccountingInvoice` / `CanBeSection34Note` all exclude it. So `PostAndSave` builds `entryLines` from `Lines.Where(l => l.IsComplete)` and appends nothing (`VoucherEntryViewModel.cs lines 3059-3086`). Line children only: bill / cost / bank / forex |
| 2 | **Payment** | plain — no `.Tds` line, no advance link, type is neither `IsStatPaymentType` nor `IsRcmPaymentVoucher` | **SIMPLE** | Same appender audit as row 1, minus the three carve-outs below |
| 3 | **Payment** | any line carrying `EntryLine.Tds` | **DEFER-LIFTED (S5c)** | `TdsPossible` is true for Payment; `TdsService.BuildCarveOut` replaces the party leg with the DERIVED net and appends the TDS-Payable leg (`VoucherEntryViewModel.cs lines 3006-3024`, `3065-3067`, `3079-3081`). §3.2's inversion trap; already the design's temporary refusal |
| 4 | **Payment** | 🔴 the voucher's id is a `GstAdvanceReceipt.RefundVoucherId` | **REFUSE — a hole the `Gst`/`Tds`/`Tcs` filter does NOT close** | `AdvanceReceiptService.BuildAdvanceReversalPair` builds `Dr Output {head}` plus `Cr suspense`, and its own doc comment states *"The reversal legs carry **no** `GstLineTax`"* (`AdvanceReceiptService.cs lines 225`, `235`). So the voucher carries **zero** tagged lines while carrying two-to-three lines the operator never keyed, and `Refund` also REPLACES the record on the company. Rehydrating only the grid drops the reversal pair — self-balancing, so nothing goes unbalanced — and the advance's output tax silently returns to the books while the record still reads refunded  🔴 **EVIDENCE CORRECTED BY THE S5b FIX PASS (finding L3-04).** The clause *"the voucher carries two-to-three lines the operator never keyed"* is true only for a **service** advance. `BuildAdvanceReversalPair`'s first statement returns `Array.Empty<EntryLine>()` when the advance is not a service or carries no tax, so a **goods** advance's refund Payment carries **no engine line at all** and its only off-line effect is the record replacement. The VERDICT survives regardless, and only because this row keys on `RefundVoucherId`, which the record does set for a goods advance — a shape proxy would have missed it. Implemented and locked (`An_advance_refund_Payment_is_refused_although_it_carries_no_tagged_line`). |
| 5 | **Payment** | `VoucherType.IsStatPaymentType` — the GST / TDS / TCS challan | **REFUSE** | `GstDepositService.cs line 66`, `TdsDepositService.cs line 41`, `TcsDepositService.cs line 45` each create a `VoucherBaseType.Payment` type flagged `isStatPayment` and post lines **no entry screen ever keyed**. §3.3 freezes `ChallanNo` / `BsrCode` / `DepositDate` / `Amount` on `TdsChallan` and `ChallanVoucherLink`, and `ChallanReconciliation.cs lines 85-92` self-heals on cancel/delete but **not** on amend |
| 6 | **Payment** | `VoucherType.IsRcmPaymentVoucher` (Rule 52) | **REFUSE** | Same shape as row 5 — a Payment base kind wearing a flag, with an `RcmDocument` series hung off it (§3.3) |
| 7 | **Receipt** | plain — no advance opt-in | **SIMPLE** | Appender audit as row 1; `ShowAdvanceReceiptDetails` is `CanBeAdvanceReceipt && IsAdvanceReceipt` and is off |
| 8 | **Receipt** | a **service** advance (`GstAdvanceReceipt`, `IsService`, tax > 0) | **DEFER-DEFERRED — NOT lifted by S5c; a USER DECISION** | `AdvanceReceiptService.cs line 107` tags the Output legs with `GstLineTax`, so the existing filter catches it — **but the suspense `Dr` at `line 120` is untagged and the record registration at `line 125` is an off-line mutation** the filter would not have caught on its own.
🔴 **STILL REFUSED AFTER S5c** (`VoucherAlterationEligibility.OffLineSideEffectRefusal`), and it is not a "next
slice" item: lifting it means an alteration REGISTERING or REPLACING a `GstAdvanceReceipt`, which §6.6a.6
prohibits, so it needs the user's ruling before any slice can take it |
| 9 | **Receipt** | 🔴 a **goods** advance (de-taxed, Notn 66/2017) | **REFUSE — second hole in the `Gst`/`Tds`/`Tcs` filter** | For a goods advance `BuildAdvanceReceipt` produces **no tax lines at all**, so posted lines DO equal keyed lines and the voucher passes every tag test — yet `_company.AddAdvanceReceipt(record)` still ran (`AdvanceReceiptService.cs line 125`) and `gst_advance_receipts.receipt_voucher_id` is `NOT NULL REFERENCES vouchers(id)`. A re-accept that does not rehydrate the advance panel leaves the record pointing at a voucher that no longer claims it |
| 10 | **Journal** | plain — no `.Tds`, no RCM pair, no advance link, type is not `IsGstStatAdjustment` | **SIMPLE** | Appender audit as row 1, minus the four carve-outs below |
| 11 | **Journal** | any line carrying `EntryLine.Tds` | **DEFER-LIFTED (S5c)** | `TdsPossible` admits Journal. Round trip: `A_TDS_carved_Journal_round_trips_byte_identically` — added by the S5c fix pass, which found this row had re-open and drift tests but **no canonical-export round trip** while the slice note claimed one |
| 12 | **Journal** | an RCM self-accounting pair | **DEFER-LIFTED (S5c)** | `RcmPossible` admits Journal; `RcmService.cs lines 210-211`, `223-224` tag both legs with `GstLineTax`. ⚠️ Under **composition** the balancing debit at `RcmService.cs lines 216` / `259` is **untagged**, but its paired credit is always tagged, so the voucher is still caught. 🔴 **Round trip added by the S5c fix pass** (`A_reverse_charge_Journal_round_trips_byte_identically`): every reverse-charge alteration fixture in the repository was a **Purchase**, so this row had no test of any kind while a doc comment on the Purchase fixture claimed to cover it. Behaviour was already right; the gap was evidence. ⚠️ And the composition clause is now implemented by reading the **posted shape** (a tagged RCM credit with no tagged RCM debit), never `Gst.RegistrationType` — reading today's registration made a REGULAR-posted voucher permanently unalterable the moment the company opted into composition, and let a composition-posted one OPEN on an unbalanced grid after a conversion to Regular |
| 13 | **Journal** | 🔴 **CORRECTED** — it carries a line to the advance-tax suspense ledger (`GstService.AdvanceTaxSuspenseLedgerName`), i.e. the SERVICE advance's release pair | **REFUSE — third hole** | 🔴 **THIS ROW WAS WRONG ON ARRIVAL AND SHIPPED A BLOCKER** (fix-pass finding L1-01, measured). Its stated discriminator, `GstAdvanceReceipt.AdjustedAgainstInvoiceVoucherId`, does **not** name the adjusting journal: `AdvanceReceiptService.AdjustAgainstInvoice` stores `adjustedAgainstInvoiceVoucherId: invoiceVoucherId`, its only caller passes `SelectedAdvanceInvoice` (a picker built from `Company.Vouchers` filtered to `BaseType == Sales`), and `Gstr1` reports the 11B row in that id's window. So the shipped arm refused the **sales invoice** — with a sentence beginning *"This journal…"* — while the adjusting Journal opened: deleting its engine-built release pair (self-balancing, so nothing went unbalanced) declared the advance's output tax a SECOND time, stranded the suspense debit, left the record reading adjusted and removed the advance from every picker, and the alteration reported plain success. **Nothing on the record names the adjusting voucher**, so the discriminator is now the SHAPE — the untagged `Cr Output Tax on Advances` release leg. §6.7 forbids the schema change (a persisted adjusting-voucher id) that would allow anything better |
| 13a | **Journal** | 🔴 **NEW ROW** — it adjusts a **GOODS** advance (de-taxed, Notn 66/2017) | **SIMPLE — measured, not assumed** | The stated limit of row 13's shape proxy, and it needs no refusal. `BuildAdvanceReversalPair` returns `Array.Empty<EntryLine>()` for a goods advance, so the adjusting Journal carries **nothing the operator did not key** and the suspense ledger is never even created; `AcceptAlteration` performs no registration side effect, so `AdjustAgainstInvoice` is not re-run and the record is untouched by `Replace`. Measured end to end: the journal opens, re-accepts, and the record still names the same invoice (`A_goods_advance_adjustment_Journal_opens_and_the_record_survives_the_alteration`) |
| 14 | **Journal** | `VoucherType.IsGstStatAdjustment` — Rule-88A set-off / ITC reversal | **REFUSE** | `GstSetOffService.cs line 290` and `GstReversalService.cs lines 141-147` post through this type; §3.3 classes `GstSetoffLine` as RE-DERIVE-but-not-by-us and `ItcReversal` as CARRY-plus-named-gap |
| 15 | **Journal** | posted by `PayrollVoucherService.PostGratuityProvision` or `ForexReportViewModel`'s revaluation | **SIMPLE — with the re-accept caveat in §6.6a.6** | Both post a plain balanced two-leg Journal with no line-level detail (`PayrollVoucherService.cs lines 168-181`; `ForexReportViewModel.cs lines 204-214`). The posted lines are exactly what a Journal screen would have keyed. The caveat is not about the shape but about re-running detection |
| 16a | **Sales** | as-voucher (`IsAsVoucherMode`), plain grid, **and no record of another voucher names it** | **SIMPLE** | `Accept` routes to `PostAndSave` only when neither `IsItemInvoice` nor `IsAccountingInvoice` (`VoucherEntryViewModel.cs lines 2838-2841`); `TdsPossible` and `RcmPossible` both exclude Sales, and `CanBeAdvanceReceipt` / `AdvanceActionForType` both exclude it  🔴 **NARROWED BY THE FIX PASS (finding L3-05).** This evidence audits only what `Accept` APPENDS, and so misses the roles a plain Sales voucher plays in records OTHER vouchers created — hence row 16b. |
| 16b | **Sales** | 🔴 **NEW ROW** — a `GstAdvanceReceipt` names it as its `AdjustedAgainstInvoiceVoucherId` | **REFUSE** | This is the correct home for the arm row 13 mis-labelled. `AdjustAgainstInvoice` re-reads this invoice's posted taxable value (`GstReportSupport.InvoiceTaxableValue`) to prove the advance was fully consumed, and GSTR-1 11B's figures are frozen against it; nothing re-derives either when a voucher is amended. The tag filter cannot see it — a goods advance's anchor invoice can be wholly untagged (`The_sales_invoice_an_advance_was_released_against_is_refused_by_name`) |
| 17 | **Sales** | item invoice (`Voucher.HasInventoryLines`) | **DEFER-DEFERRED — and NOT covered by S5c's stated contents** | The value leg and the party leg are **derived, never keyed** (`VoucherEntryViewModel.cs lines 4828-4840`). Two proven non-inverses beyond tax: **(a)** a batch-split line posts **one item line PER BATCH** (`TryAppendSplitBatchLines`, called at `VoucherEntryViewModel.cs lines 4722-4726`), so one keyed row becomes N posted rows; **(b)** the posted rate is `EffectiveRate` = `rate × (1 − discount/100)` (`InventoryVoucherLineViewModel.cs lines 350-353`) and **`VoucherInventoryLine` has no discount field at all** — its whole public surface is item, godown, quantity, billed quantity, rate, direction, batch label, unit — so the list rate and the Price-Level discount are unrecoverable |
| 18 | **Sales** | accounting invoice (`Voucher.IsAccountingInvoice`), **carrying no tax line** | **SIMPLE on the ROUND TRIP · REFUSE on the EDIT** (re-verdicted 2026-08-19, finding L1-04) | The mode itself is safely recoverable: `IsAccountingInvoice` is persisted and `Replace` already REFUSES a change to it (`LedgerService.cs lines 404-412`). The party leg is identifiable as the line whose `LedgerId == Voucher.PartyId`, and the income legs are one-per-keyed-particulars-row (`VoucherEntryViewModel.cs lines 4134-4142`) — so an inverse looks mechanically available. **It has not been measured**, and the branch that worries me is the one the code calls out by name at the `isAccountingInvoice` stamp: a **zero-rated (LUT/export) or wholly exempt** service invoice posts **no tax leg**, so it would pass the tag filter while its party leg is still a derived total. Do not ship this as SIMPLE without measuring that branch  🔴 **MEASURED BY THE FIX PASS — no longer UNDETERMINED (finding L1-04).** With the arm lifted and `SeedAlterationMode` pointed at the plain grid, a wholly exempt Sales accounting invoice posts, re-opens, re-accepts and exports **byte-identically** in memory and on disk, with `IsAccountingInvoice` and `PartyId` intact (both are already carried by `AcceptAlteration`) and GSTR-1's exempt value unmoved. **Verdict: SIMPLE on the ROUND TRIP, REFUSE on the EDIT** — what is not recoverable is the party leg's DERIVED STATUS: on the plain grid it becomes an ordinary editable row that nothing re-derives, so an operator can move the party total off the sum of the service rows and still balance. The refusal's SENTENCE was changed to say that; the old one said the round trip "has not been measured", which is no longer a fact about this repository, and also argued from the LINES when the arm reads the persisted flag |
| 19 | **Sales** | `VoucherType.IsPosSales` (`Voucher.HasPosTenders`) | **REFUSE — permanent** | RULING 1. `PosBillingViewModel.cs lines 693`, `704-708` append `PosTenderService.BuildTenderDebitLines(tenders)` and post through `LedgerService.Post` **into `Company.Vouchers`** — so unlike the inventory screens this one IS reachable by `Replace`, and the refusal must be explicit rather than architectural |
| 20 | **Purchase** | as-voucher, plain grid, no `.Tds`, no RCM | **SIMPLE** | As row 16a, minus rows 21-23 |
| 21 | **Purchase** | any line carrying `EntryLine.Tds`, or an RCM pair | **DEFER-LIFTED (S5c)** | `TdsPossible` and `RcmPossible` both admit Purchase |
| 22 | **Purchase** | item invoice | **DEFER-DEFERRED — the same two non-inverses as row 17** | Plus the additional-cost legs (`VoucherEntryViewModel.cs lines 4756-4770`), which post as ordinary `Dr` expense lines and are **indistinguishable on rehydration** from any other debit leg without a classification rule |
| 23 | **Purchase** | accounting invoice | **DEFER-DEFERRED — NOT lifted by S5c** | `CanBeAccountingInvoice` admits Purchase, and `DetectAccountingTdsShape` / `DetectAccountingRcmShape` are wired to the Particulars lines (`VoucherEntryViewModel.cs lines 4176-4215`), so the purchase arm is never tax-free by construction the way row 18 can be. 🔴 **STILL REFUSED AFTER S5c** —
`VoucherAlterationEligibility.EntryModeRefusal` refuses `BaseType == Purchase && IsAccountingInvoice`, and
`AcceptAlteration` refuses anything that is not the plain Dr/Cr grid. The blocker is the invoice grid's own
writers, not the carve: the party leg is DERIVED there and its bill-wise panel is targeted at the withholding NET,
so the plain-grid inversion S5c built does not apply to it |
| 24 | **Credit Note** | plain — `ShowSection34Details` false | **SIMPLE** | `CanBeItemInvoice` / `CanBeAccountingInvoice` exclude CN and DN, so the only path is `PostAndSave`; `TdsPossible`, `RcmPossible` and the advance gates all exclude it. Any GST legs on a CN are **hand-keyed**, carrying no `GstLineTax` — which is precisely why the tag filter cannot be relied on here |
| 25 | **Credit Note** | 🔴 `ShowSection34Details` true | **REFUSE — fourth hole in the filter** | `RegisterSection34Link` mints a **`Guid.NewGuid()`** link on every Accept (`VoucherEntryViewModel.cs lines 2511-2520`), so a `ForAlter` re-accept adds a SECOND `GstCreditDebitNoteLink` for one note. The note carries no `Gst` / `Tds` / `Tcs` line, so nothing in the temporary filter sees it. §3.3 classes the link CARRY — and CARRY means *do not rebuild it*, which the entry path does unconditionally |
| 26 | **Debit Note** | plain | **SIMPLE** | As row 24 |
| 27 | **Debit Note** | `ShowSection34Details` true | **REFUSE** | As row 25 — `CanBeSection34Note` admits both CN and DN (`VoucherEntryViewModel.cs lines 2256-2257`) |
| 28 | **Memorandum** | (none) | **SIMPLE — and the flag carry is mandatory** | Provisional by base kind (`LedgerBalance.cs lines 37-38`), so `PostAndSave` forces `optional: !IsProvisionalType && IsOptional` to **false** (`VoucherEntryViewModel.cs line 3127`) and `applicableUpto` stays null. **`PostDated` can still be true**, and S5a REFUSES a change to it — so a rehydration that drops it fails the refusal rather than a balance check, which is the intended outcome and must have a test |
| 29 | **Reversing Journal** | (none) | **SIMPLE *with* an `ApplicableUpto` · REFUSED without one** (split 2026-08-19, finding L3-02 — the ALWAYS-non-default premise is REFUTED) | `IsReversing` makes `ApplicableUptoText` mandatory and on/after the voucher date (`VoucherEntryViewModel.cs lines 3100-3112`), so **every** Reversing Journal carries a non-null `ApplicableUpto`. A `ForAlter` that does not rehydrate `ApplicableUptoText` throws S5a's provisional-vector refusal on **every** voucher of this family — the loudest possible failure, and the reason this family is the cheapest S5b smoke test  🔴 **PREMISE REFUTED BY THE FIX PASS (findings L1-03 and L3-02).** *"Every Reversing Journal carries a non-null `ApplicableUpto`"* is a property of the ENTRY SCREEN, not of the model: `VoucherValidator` has no ReversingJournal clause, `LedgerService.Post` takes one straight through, and the product's own canonical import parses one with the attribute stripped with **zero** parse errors. Such a voucher OPENED, seeded `ApplicableUptoText` from the constructor's financial-year-end default, and could then never be accepted — `Replace` refused a provisional-state change *from (none) to 31-Mar-…*, an engine message about a field the operator never saw. **Split: a Reversing Journal WITH a date is SIMPLE (unchanged); one WITHOUT is REFUSED at the door**, by `ProvisionalShapeRefusal`'s new mirror arm |
| 30 | **Payroll** | (none) | **REFUSE — permanent** | RULING 1. Every line carries `EntryLine.Payroll` written by `PayrollComputationService`, and the `EntryLine` constructor enforces `payroll.Amount == amount` (`EntryLine.cs lines 144-146`). It is keyed on `PayrollVoucherEntryViewModel` as a period plus an employee set, never as a Dr/Cr grid — `MainWindowViewModel.cs lines 3254-3256` already routes it away for exactly that stated reason |

### 6.6a.4 THE ENUMERATION — inventory aggregate (NOT reachable by `LedgerService.Replace`)

All **twelve** of these post an `InventoryVoucher` through `InventoryPostingService`, into a **different list**
on `Company`. `LedgerService.Replace` cannot see them: `FindVoucher` misses, and the not-found throw says so by
name — *"is a pure-stock inventory voucher; LedgerService.Replace does not reach the inventory aggregate — use
InventoryPostingService"* (`LedgerService.cs lines 497-502`).

**Their refusal is ARCHITECTURAL, not shape-based**, and the distinction matters: their posted lines mostly
*do* equal their keyed lines, so a future slice could serve them cheaply once an `InventoryPostingService`
counterpart of `Replace` exists. S5b refuses all twelve.

| Base kinds | Screen | Verdict |
|---|---|---|
| PurchaseOrder · SalesOrder · ReceiptNote · DeliveryNote · RejectionIn · RejectionOut · StockJournal · PhysicalStock | `InventoryVoucherEntryViewModel` | **REFUSE** |
| JobWorkInOrder · JobWorkOutOrder | `JobWorkOrderEntryViewModel` | **REFUSE** |
| MaterialIn · MaterialOut | `MaterialMovementEntryViewModel` | **REFUSE** |
| StockJournal wearing `UseAsManufacturingJournal` | `ManufacturingJournalEntryViewModel` | **REFUSE** |

**🔴 A correction to §6.6's own complement.** §6.6 lists the permanent refusals as *"POS, Manufacturing
Journal, payroll, and the three `InventoryVoucher` entry screens"*. There are **four** `InventoryVoucher` entry
screens, and the Manufacturing Journal is the fourth of them, not a fifth thing beside them: it posts a
`StockJournal`-based **`InventoryVoucher`** (`ManufacturingJournalService.cs lines 5-16` — *"a pure inventory
voucher (`VoucherType.AffectsAccounts` is `false`), so it books no double-entry"*). Reading §6.6's list as five
disjoint families over-counts by one and, more dangerously, implies the Manufacturing Journal needs a refusal
in `Replace` — it does not; it needs one on the **inventory** side, where it will land beside the other twelve.

**Physical Stock keeps its own explicit test regardless** (§7.4): `InventoryLedger.cs lines 193-207` makes a
count a checkpoint that RESETS the running balance, so altering one changes on-hand for every later movement.

### 6.6a.5 THE LINE WRITERS AND THEIR INVERSES — including two §6.6 does not name

§6.6 names three writers. `PostAndSave` calls **five** per line (`VoucherEntryViewModel.cs lines 3067-3070`).

| Writer | Forward | What the inverse must recover | Lossless? |
|---|---|---|---|
| **`ToBillAllocations()`** `VoucherLineViewModel.cs line 261` | `IsBillWise` ? complete rows → `BillAllocation` : empty | one row per posted `BillAllocation` | **Lossless on content. NOT lossless under master drift** — `SyncBillWise` sets the gate from `SelectedLedger?.MaintainBillByBill` (`VoucherLineViewModel.cs line 169`), so if that master flag was turned OFF after posting, the rehydrated panel hides and the writer returns **empty** — the allocations vanish on re-accept with no message. **S5b must refuse when a posted line carries allocations its rehydrated gate would hide.** (Incomplete rows are dropped by the `.Where(a => a.IsComplete)` filter, but they were never posted, so the book still round-trips) |
| **`ToCostAllocations()`** `VoucherLineViewModel.cs line 429` | `IsCostApplicable` ? complete rows → `CostAllocation` : empty | one row per posted `CostAllocation`, **carrying its `CategoryId`** | **Lossless on content** — the row VM does carry a category picker (`SelectedCategory`, used by `UsedCostCategories` at `VoucherLineViewModel.cs lines 348-353`), so the parallel multi-category axes of §4.2 rule C-27 are representable. **The same master-drift hole**: `SyncCostApplicable` gates on `_costCentres.Count > 0 && ClassificationRules.CostCentresApplicableFor(...)` (`VoucherLineViewModel.cs lines 277-280`) |
| **`ToInvoiceBillAllocations()`** `VoucherEntryViewModel.cs line 544` | `InvoiceBillWiseApplies` ? complete rows : **`null`** (null, not empty, for ER-13 byte-identity) | the derived party leg's `BillAllocations` | **Lossless on content**, the same master-drift gate. Note it belongs only to the two invoice Accept paths, both of which are DEFER or REFUSED above — so **S5b never exercises this writer's inverse at all**, and building it in S5b is premature |
| **🔴 `ToBankAllocation()`** `VoucherLineViewModel.cs line 484` — **not named in §6.6** | transaction type, instrument number, instrument date | the same three | **NOT lossless — it never writes `BankDate`**, because `BankDate` is written post-entry by `BankReconciliation.SetBankDate` (§3.4). **The loss is already closed, but by S5a, not by S5b**: `LedgerService.CarryBankDatesForward` (`LedgerService.cs line 531`) carries the tick with a two-pass exact-first pairing and an ECHO rule that treats a rehydrated equal date as the posted fact rather than a statement. **The consequence for S5b is a constraint, not a task: `ForAlter`'s accept path must go through `Replace`, never through `Post`.** Through `Post`, every reconciliation tick on the voucher is destroyed silently |
| **🔴 `ToForexInfo()`** `VoucherLineViewModel.cs line 590` — **not named in §6.6** | (currencyId, forexAmount, rate); returns null unless both parse > 0 | `ForexAmountText`, `ForexRateText` **and the line's DENOMINATION** | **NOT lossless as the screen formats today, and the inverse's stated obligation was also INCOMPLETE (fix-pass finding L3-01).** The forward is `(currencyId, forexAmount, rate)` but the inverse was scoped to the amount and the rate only — and `currencyId` comes from `SelectedLedger.CurrencyId`, the **live master**. So `ToForexInfo` is a **FOURTH master-drift reader**, and the only one whose drift reaches a posted `EntryLine` as a VALUE rather than as a panel gate: a ledger repointed from USD to EUR after posting opened silently, accepted with a plain "altered.", and restated the line in a currency it was never denominated in — measured, with the canonical export no longer identical. `EnsureForexValid` cannot catch it (it checks only that the currency EXISTS and that base ≈ forex × rate) and `LedgerMasterViewModel` writes `CurrencyId` unconditionally with no transacted-ledger guard. **The inverse must compare the DENOMINATION and refuse by name**, which it now does. The formatting half, as originally stated: `ForexInfo.Rate` persists at `Schema.ForexScale` = **1,000,000** (six decimal places; `Schema.cs lines 162`, `973`), but the screen's own rate formatter is `"0.####"` — **four** places (`VoucherLineViewModel.cs lines 528`, `608`). An inverse reusing that format truncates a six-place rate, and since `Money.ForexBase` snaps `forexAmount × rate` to the paisa, the rebuilt line's base amount can differ from the posted one — which `VoucherValidator` enforces. **The inverse must format the rate at six or more places.** This is a one-line fix, and exactly the kind that ships silently if nobody writes it down |

**Consequence for the verdicts above.** Two writers are not losslessly invertible, and **neither failure
disqualifies a SIMPLE row**: the bank one is compensated in the engine by S5a, and the forex one is a
formatting choice inside the inverse S5b is building. The failures that DO disqualify are the master-drift
gates (all three named writers) and the item-invoice discount (rows 17 and 22).

### 6.6a.6 🔴 THE THREE QUESTIONS S5b DEPENDS ON — answered

**(1) Is the temporary `Gst` / `Tds` / `Tcs` refusal sufficient? NO. Five families carry no such line and
still fail to round-trip.** Named, with their rows:

1. **Advance REFUND on a Payment** (row 4) — untagged reversal legs, by the engine's own doc comment.
2. **Advance ADJUSTMENT on a Journal** (row 13) — the same untagged pair, plus a record the engine refuses to
   adjust twice.
3. **A GOODS advance Receipt** (row 9) — **zero** appended lines, so posted lines genuinely equal keyed lines,
   and a `GstAdvanceReceipt` row still points at the voucher under a `NOT NULL` foreign key.
4. **A §34 Credit/Debit Note** (rows 25 and 27) — a fresh `Guid.NewGuid()` link per Accept; GST legs on a
   CN/DN are hand-keyed and therefore untagged.
5. **Statutory-type Payments and Journals** (rows 5, 6, 14) — `IsStatPaymentType`, `IsRcmPaymentVoucher`,
   `IsGstStatAdjustment`: ordinary base kinds wearing a flag, posted by a service, never keyed on any screen.

The common shape of all five is **an off-line side effect of Accept** — a record added or replaced on
`Company` — which no test of `EntryLine` contents can see. **S5b's refusal predicate must therefore be a
union**: any tagged line, OR any of the five links above, OR a specialised type flag.

**(2) What does `ForAlter` do about a voucher keyed in a DIFFERENT screen from the one that would re-open
it?** For the Sales/Purchase three-way it is **already answered by persisted state, and safely**:
`Voucher.HasInventoryLines` selects item-invoice mode, `Voucher.IsAccountingInvoice` selects accounting-invoice
mode, and neither can drift, because `Replace` refuses a change to `IsAccountingInvoice` by name. **But
`ForAlter` must seed `Mode` from the VOUCHER, never from the type's opening default** — §6.5's note that the
opening mode is *"seeded per voucher type"* means a Sales type configured to open as an item invoice would
otherwise re-open a plain Dr/Cr Sales in the wrong grid and post a different voucher. For every other
cross-screen case — POS, Manufacturing Journal, payroll, the twelve inventory kinds, the statutory types — the
answer is **refuse**, which is why rows 5, 6, 14, 19 and 30 exist, and why the dispatcher must be ordered as
§6.6a.2 requires.

**(3) The rehydration inverse of the line writers** — answered in full in §6.6a.5. Short form: three of five
are lossless on content and lossy only under master drift; **`ToBankAllocation` is not lossless and is
compensated by S5a**; **`ToForexInfo` is not lossless as formatted today and must be fixed inside the
inverse**.

**A fourth thing S5b must decide, which none of the three questions reaches.** `Accept()` is
build + `Post` + side effects. **`ForAlter` cannot reuse it.** Re-running `Accept` on a rehydrated voucher
re-runs `DetectTdsContext`, `DetectRcmShape` and `BuildAdvanceLines` against **today's** masters, so a voucher
that carried no carve at posting can acquire one on a narration-only alteration — and one that carried a carve
can lose it. S5b needs a separate `AcceptAlteration` that ends in `Replace` and performs **no** registration
side effect. This is stated here because it is the shape §12.8's defect took: the slice that consumes a
contract silently re-introducing what the contract exists to prevent.

### 6.6a.7 WHERE THE COVERAGE TEST HAS TO LIVE — and it is not where you would look

The deliverable `plan.md` names is *"a test that fails when a newly seeded kind belongs to neither set."*
It **cannot** live in `Apex.Ledger.Tests`: that project references only `Apex.Ledger` and `Apex.Ledger.Io`
(`Apex.Ledger.Tests.csproj lines 22-23`), so it can see neither `PopulatedFixtureCoverageTests.SeededBaseTypes`
nor `VoucherEntryViewModel.ForAlter`. It must live in **`Apex.Desktop.Tests`**, which references
`Apex.Desktop`, `Apex.Ledger` and `Apex.Persistence.Sqlite` (`Apex.Desktop.Tests.csproj lines 26-28`) — the
same project that already owns the coverage lock this test is modelled on.

🔴 **AND WHAT WAS BUILT FROM THIS SECTION DID NOT REACH THE PREDICATE** (fix-pass finding L2-01, measured).
The lock as written below iterates `SeededBaseTypes(PopulatedCompanyFixture.BuildRegular())` — a denominator of
**base kinds**, decided by code the predicate does not own. Measured against the shipped slice: replacing the
ENTIRE body of `VoucherAlterationEligibility.RefusalFor` with `return null` left the lock **green** while 23
other S5b tests died, and deleting a whole L5 type-flag arm (`IsPosSales`) left it green too. In that fixture
seven of the ten SIMPLE families are refused by MASTER DRIFT (a refusal `VoucherLineViewModel.RehydrateFrom`
owns, in a different file), the twelve inventory kinds by the architectural aggregate check UPSTREAM of the
predicate, and Sales/Purchase by the item-invoice arm — so its "at least one refused" clause is carried entirely
by other checks, and its "at least one opened" clause by exactly ONE base kind, Memorandum.

**The section's own §6.6a.2 says why: the discriminator is not the base kind.** The lock therefore has to be
keyed on the layer the dispatcher actually switches on. Three tests now carry it, and the first two are the ones
that bite:

1. `Every_specialised_type_flag_has_its_own_named_refusal_from_the_predicate` — the **(type flag × base kind)**
   cross-product, asserted against `VoucherAlterationEligibility.RefusalFor` ITSELF so no other layer can cover
   for it, requiring a **distinct** sentence per flag, with the denominator read by REFLECTION off
   `VoucherType`'s own derived (get-only) `Is…` predicates — so a NEW flag added without a decision fails on the
   day it is added. Gutting the predicate, dropping the POS arm and dropping the inventory-base-kind arm each
   redden it.
2. `Every_SIMPLE_base_kind_opens_on_a_drift_free_book` — all ten SIMPLE base kinds on masters that have not
   moved, so the OPENED side is not carried by Memorandum alone.
3. The seeded-base-kind lock below, kept for what it genuinely does: it fails when a newly **seeded** base kind
   has no decision at all.

**What it must assert, phrased so a new seed row fails it:** for every base kind in
`SeededBaseTypes(PopulatedCompanyFixture.BuildRegular())`, the S5b dispatcher returns either a rehydrated VM or
a refusal **whose message is non-empty and family-specific** — never a null, never a silent no-op. That is
RULING 1's standard: *"a silent no-op is the failure mode being avoided"*, and a test asserting "nothing
happened" passes for a silent no-op too.

### 6.6a.8 TOTALS, AND WHAT I COULD NOT DECIDE

🔴 **RESTATED 2026-08-19 BY THE THREE-LENS REVIEW — the figures below are DERIVED from the verdict column, not
carried forward.** The pre-review text read *"thirty accounting-aggregate rows: eleven SIMPLE · eight DEFER · ten
REFUSE · one UNDETERMINED (row 18)"*. That is superseded: the review added rows **13a**, **16b** and **31**, split
row 16 into 16a/16b, re-verdicted row 18, and split row 29 — and this section was not updated with them, so for a
day it disagreed with the table above it. ⚠️ **A restated tally circulated as `12 SIMPLE · 8 DEFER · 13 REFUSE ·
1 OPEN · 0 UNDETERMINED` = 34 against 33 rows; that sums wrong because it counts each CONDITIONAL row in BOTH
buckets.** Counting each row once:

**Totals over the thirty-three accounting-aggregate rows** — 32 in the table above plus row 31 in §6.6a.9:
**eleven SIMPLE** (rows 1, 2, 7, 10, 13a, 15, 16a, 20, 24, 26, 28) · **eight DEFER** (rows 3, 8, 11, 12, 17, 21,
22, 23 — six tax-driven, and two, rows 17 and 22, not) · **eleven REFUSE** (rows 4, 5, 6, 9, 13, 14, 16b, 19, 25,
27, 30) · **two CONDITIONAL**, each SIMPLE one way and REFUSED the other (row 18 — SIMPLE on the round trip,
REFUSED on the edit, because the party leg's DERIVED status is what is unrecoverable; row 29 — SIMPLE with an
`ApplicableUpto`, REFUSED without one) · **one OPEN-then-REFUSE** (row 31). 11 + 8 + 11 + 2 + 1 = **33**. Plus
**twelve inventory base kinds, all REFUSE**. **Zero UNDETERMINED remain.**

**Every one of the 23 seeded base kinds appears**: ten of them (Contra, Payment, Receipt, Journal, Sales,
Purchase, Credit Note, Debit Note, Memorandum, Reversing Journal) have at least one SIMPLE or CONDITIONAL-SIMPLE
shape; the remaining thirteen (Payroll plus the twelve inventory kinds) have none.
**Attendance is the 24th enum member and is NOT in the universe** — it is un-seeded and posts `AttendanceEntry`
rows, never a `Voucher`; if it is ever seeded, the §6.6a.7 test is what fails on that day.

🔴 **SUPERSEDED 2026-08-19 — a restated tally lived here and it did not reconcile.** It read *"twelve SIMPLE …
eight DEFER … thirteen REFUSE … one OPEN-then-refuse"* = **34 against 33 rows**, under a heading claiming the
totals above it were the pre-fix-pass count. Both halves were wrong by then: the totals above have been rewritten
and derived from the verdict column, and the 34 is not a different counting convention but an internally
inconsistent one — **it counted row 29 TWICE** (29-with-a-date under SIMPLE, 29-without under REFUSE) while
counting the equally conditional **row 18 only ONCE** (under REFUSE). The corrections it described are real and
are in the table; only its arithmetic was not. **The derived totals in §6.6a.8 above are authoritative: 11 · 8 ·
11 · 2 CONDITIONAL · 1 = 33.** If a per-SHAPE rather than per-ROW count is ever wanted, both conditional rows
must be split, giving 35 shapes — not 34.

**🔴 STATED PLAINLY — what I could not decide.**

1. ~~**Row 18 … is UNDETERMINED and must not be shipped as SIMPLE.**~~ 🔴 **RESOLVED 2026-08-19 (finding
   L1-04) — MEASURED, not left undecided.** A wholly exempt Sales accounting invoice round-trips byte-identically;
   what is unrecoverable is the party leg's DERIVED status, so the verdict is SIMPLE on the round trip and REFUSE
   on the edit. The zero-rated / exempt branch named here as the worry is precisely the branch that was measured.
2. **Row 15 (gratuity-provision and forex-revaluation Journals) is SIMPLE only on its posted shape.** Whether
   re-accepting one can newly fire TDS or RCM detection depends on flags on the ledgers those services create,
   which I did not audit ledger by ledger. The §6.6a.6 fourth-thing decision (`AcceptAlteration` with no
   detection re-run) makes the question moot; without that decision, row 15 is undetermined too.
3. **I did not enumerate USER-DEFINED voucher types beyond the flags at L5.** A second Sales series behaves as
   its base kind, which is why the enumeration is keyed on predicates rather than on type names — but a
   user-defined type wearing a combination of flags nobody has tried is outside what I measured.
4. **The DEFER rows are stated against S5c's contents as §6.6 writes them**, and **rows 17 and 22 do not fit
   there**: S5c is scoped to *"the GST re-stamp, the TDS re-carve, the §3.4 bank-date carry-forward, and the
   §3.3 CARRY/REFUSE table"*, none of which covers the item-invoice batch split or the Price-Level discount.
   Either S5c's scope widens or those two rows need a slice of their own. **That is a scoping decision, and it
   is not mine to take.**

### 6.6a.9 🔴 THE S5b FIX PASS — WHAT THE THREE ADVERSARIAL LENSES CHANGED (added 2026-08-19)

Twenty-six findings over three sequential lenses. This subsection records what they changed **in the
enumeration**, so a later slice reads the corrected rows rather than re-deriving them. The code changes are
carried in the files themselves; every one ships with a test that fails without it.

**FOUR ROWS WERE WRONG ON ARRIVAL** (rows 13, 16, 18, 29 — joining the already-known row 15), and two more
carried evidence that is false without changing the verdict (rows 4 and 13's mechanism). All six are corrected
in place above. The corrections are not cosmetic: row 13's error shipped a **BLOCKER** — an alteration that
silently declared ₹1,800 of output tax twice and stranded the suspense — and row 29's shipped a screen that could
be opened and never accepted.

**THREE ROWS ARE ADDED**: 13a (a goods advance's adjusting Journal — SIMPLE, measured), 16b (the sales invoice a
GST advance was released against — REFUSE), and 31 below.

| # | Base kind | Discriminating predicate | Verdict | Evidence |
|---|---|---|---|---|
| 31 | **any** | 🔴 **NEW** — the line carries cost allocations that foot only ACROSS axes (the superseded partition rule), admitted by `CostAllocationStrictness.Legacy` | **OPEN, then REFUSE at the accept — with an ACCURATE message** | The population `CostAllocationStrictness.Legacy` exists for: `SqliteCompanyStore.Load` re-posts every stored voucher through it and the canonical import uses it too, so these vouchers are on disk today. `Replace` validates with `Strict`, so such a voucher passes every S5b gate, opens, and cannot be accepted. The message it got said the allocations *"must sum to the line amount (5,000.00)"* while they summed to exactly 5,000.00 — the abolished rule quoted back at the operator on the ONE screen that can remediate it. It now names the short AXIS and states the per-category rule, mirroring `VoucherValidator`'s own C-27 text. **`ForAlter` deliberately still OPENS it**, because it is the remediation screen. 🔴 **And the general lesson: SIMPLE-by-line-equality is NECESSARY BUT NOT SUFFICIENT** — this shape satisfies "posted lines equal the keyed lines" and still does not round-trip, because a REHYDRATION TOLERANCE, not an appender, admitted it |

**§6.6a.5's writer table gained a fourth master-drift reader** — `ToForexInfo`'s `currencyId`, see the row
itself. That is the only value a live master contributes to a posted `EntryLine`; every other writer either
reads a gate the inverse compares (`ToBillAllocations`, `ToCostAllocations`, `ToBankAllocation`) or reads no
master at all (`BillAllocationRowViewModel.ToAllocation`).

**TWO PREMISES OF THE SLICE'S OWN DISCLOSURE ARE CORRECTED.**

1. **The canonical-export instrument is NOT vacuum-capable here** (finding L2-04, and no change was needed).
   Short-circuited to a constant it reddens nothing — which is the expected shape for a snapshot instrument, a
   constant being self-consistent — but made to return a fresh nonce per call it reddens **30 of 75**, and all
   **eleven** SIMPLE round-trip tests are among them. S5a's 2-of-30 failure mode is absent. Recorded so the next
   reviewer does not re-run it.
2. **There are TWO unfalsifiable carries, not three** (finding L2-05). `number: existing.Number` was declared
   unfalsifiable on the ground that `number: null` will not compile — true, but an ARITHMETIC mutant does, and
   `existing.Number + 1000` kills 29 tests. The other two declarations hold under measurement
   (`isAccountingInvoice: existing.IsAccountingInvoice` → `false` survives; `existing.Id` → a fresh Guid kills 32).

**THE OTHER 24 ROWS SURVIVE CONTACT** — audited row by row against the code they describe rather than against
themselves, and recorded here as CONFIRMED-BY-AUDIT so the next reviewer does not re-derive them: rows 1, 2, 7,
10, 20, 24, 26, 28 (the appender audit re-verified, and each measured end to end: opened, re-accepted, export
identical); 3, 11, 21, 12 (DEFER — `RcmService` tags the liability leg on every branch, composition included);
5, 6, 14, 19, 30 (REFUSE — the type flags exist and are checked first); 8, 9 (the advance receipt itself); 17,
22, 23, 25, 27 (item invoice, accounting invoice, §34 links); and §6.6a.4's twelve inventory kinds
(`VoucherEffects.IsInventoryBaseType` lists exactly those twelve). §6.6a.5's parallel-cost-axes claim is
MEASURED TRUE (two axes each at the full 5,000.55 posted, rehydrated, re-accepted, export identical in memory
and on disk), and the lossiness it does not name — `CreditPeriodDays` — is MEASURED CLOSED (an import-shaped
allocation carrying one is refused by name).

**ER-13 holds by REACHABILITY, and that limit is stated rather than papered over.** `grep` over `src/` finds
**zero** production call sites for `ForAlter(`, `AcceptAlteration` or `VoucherAlterationEligibility` on the
voucher screen; the slice's two state fields are written only inside `RehydrateFrom`, which only `ForAlter`
calls; and a fresh screen measures inert (`IsAltering=False`, and `AcceptAlteration` refuses by name). A byte
diff against the pre-slice tree was NOT taken — the fix pass is forbidden every git command — so no such claim
is made.

**ONE DEFECT OUTSIDE THE SLICE WAS FOUND AND FIXED HERE, and it should be raised on its own** (finding L2-03): a
forex line whose FOREX AMOUNT carried more than two decimal places posted through the real screen, passed
`VoucherValidator.EnsureForexValid` and **saved** — SQLite stores the magnitude at 1,000,000 scale — after which
`CanonicalXml.Export` threw, because the canonical model carries `ForexAmountPaisa` at two places. That is a
company the app itself produced and cannot export, and Export Data → XML is the only door out of it. The typed
amount is now guarded at the keyboard like every other. It is pre-existing and not S5b's doing, but it lands on
S5b because it makes the round-trip INSTRUMENT unusable on a reachable shape of the eleven SIMPLE families.

## 6.7 🔴 WHAT I WOULD NOT BUILD IN THE FIRST SLICE — the explicit list

Un-cancel · any alteration at all (S3 is Cancel only) · any audit trail, Edit Log, role or user attribution ·
company deletion · **Duplicate (`Alt+2`) and Insert (`Alt+I`)** — both corpus-attested (§2.6) and both out of
scope · the `SqliteCompanyStore.Remove` completeness fix · Basis of Values (Ctrl+B stays **reserved and
unbound**) · pure-inventory Cancel/Delete **UI** · a numbering floor / counter table (§4.5b) · any schema
change (§9).

---

# §7 — TESTS

## 7.1 The RED-PROOF — one test, runnable on the current tree, that FAILS

**`VoucherLifecycleRedProofTests.CorrectingAPostedVoucherLosesItsIdentity`** — project `Apex.Ledger.Tests`.

The point of a red-proof is to make the *harm* visible with the tools that exist today, so it must compile and
run at `3e968b3`. Today the only way to correct a posted voucher is **Delete + re-Post**. The test asserts the
property `Replace` is being built to give, against a reference book:

```
Book A (the only correction available today):
    post Sales #1 … #9, then Sales #10 with the WRONG total ₹1,84,733.45
    post Sales #11 (a later, unrelated invoice)
    Delete(#10)
    Post(a corrected Sales for ₹1,84,731.95)        // -₹1.50, odd paise
Book B (the reference — the same book, keyed right the first time):
    post Sales #1 … #9, then Sales #10 for ₹1,84,731.95, then Sales #11

ASSERT: the corrected voucher's Number      == B's  →  FAILS today (it takes max+1 = 12)
ASSERT: the corrected voucher's index in company.Vouchers == B's → FAILS today (it is last, not 10th)
ASSERT: DerivedStateSnapshot(A) == DerivedStateSnapshot(B)       → FAILS today (both of the above leak into the Day Book and the register)
```

**A second red-proof, cheaper and even more direct — `DeletingTheHighestNumberedVoucherReusesItsNumber`:**
post Sales #1…#10, `Delete` #10, post a new Sales, assert `Number == 11`. **It will fail, returning 10** —
proving §4.1's reuse defect on the current tree, before VL-2 makes it reachable from the keyboard.

Both are **kept green afterwards**, rewritten to use `Replace` / to assert the §4.5(a) refusal. Neither is
deleted — a red-proof that is deleted after it goes green proves nothing about the next regression.

## 7.2 🔴 THE EQUIVALENCE TEST — and the helper that makes it possible

The requirement is: *post a voucher, alter it, and prove EVERY derived figure matches a book where the amended
voucher was posted directly*. "Every" is the hard word, and asserting fifteen figures by hand will miss the
sixteenth. **Build one helper and the problem collapses:**

```csharp
// tests/Apex.Ledger.Tests/Support/DerivedState.cs   (built in S5a)
public static string Snapshot(Company c, DateOnly asOf);
```

A **canonical, ordered, paisa-exact text dump** of the entire derived surface — so a diff names the divergence
instead of a boolean hiding it. It must cover, in a fixed order:

1. Trial Balance — every ledger, signed closing (`Reports/TrialBalance.cs line 31`)
2. Balance Sheet — every group total **and** closing stock (`Reports/BalanceSheet.cs line 65`, `:133`)
3. Profit & Loss — every line (`Reports/ProfitAndLoss.cs line 71`)
4. Stock — **on-hand AND closing valuation for every item and every godown** (`InventoryLedger.cs line 135`, `StockValuationService.cs line 67`)
5. Batch on-hand and batch valuation (`BatchStockService.cs line 56`, `:135`)
6. Outstandings — every bill: reference, pending, **ageing bucket** (`Outstandings.cs line 95`, `:213`)
7. Cost — category summary, cost-centre breakup, ledger breakup (`CostReports.cs line 185`, `:208`, `:264`)
8. GSTR-1 — every section row (`Gstr1.cs line 217`); GSTR-3B — every box (`Gstr3b.cs line 130`)
9. Electronic cash/credit ledgers (`ElectronicLedgersView.cs line 50`)
10. Challan Reconciliation — every section's Deducted / Deposited / Remaining (`ChallanReconciliation.cs line 43`)
11. Interest (`InterestCalculation.cs line 186`), Budget variance (`BudgetVariance.cs line 45`), Order fulfilment
    (`OrderFulfilment.cs line 326`), Reorder status (`ReorderStatus.cs`)
12. **The voucher identity vector** — for every voucher: `Id`, `Number`, rendered number via
    `Company.FormatVoucherNumber`, `Cancelled`, and **its index in `company.Vouchers`**

**`T-1 — AlterEqualsDirectPost`:** build A (post-wrong → `Replace`) and B (post-right) from the **same
`PopulatedCompanyFixture` seed**; assert `Snapshot(A) == Snapshot(B)`; then **`Save` both to real SQLite,
`Load` both, and assert again**. The second half is not ceremony: §1.6 proves list order is persisted via
`ORDER BY rowid`, so a `Replace` that got the index right in memory and wrong on disk would pass the first
assertion and fail the second.

## 7.3 The rest of the suite

| id | Test | Why it exists |
|---|---|---|
| **T-2** | **A rejected `Replace` leaves the original byte-identical and still at its index.** Feed an unbalanced replacement; catch `UnbalancedVoucherException`; assert `Snapshot(company)` is unchanged **string-for-string**. | `plan.md` driving test (1). Guards the §6.5(1) ordering clause — the one `Post`'s mutate-before-fail shape makes easy to get wrong. |
| **T-3** | **A TDS-carved purchase re-derives from the RESTORED GROSS**, at a carve rounding to odd paise. Gross ₹47,239.55 → alter to ₹47,241.05 → assert the carve, the net party credit and the `tds_lines` detail all match a directly-posted book to the paisa. | `plan.md` driving test (2). The §3.2 inversion trap. |
| **T-4** | **A §34 Credit Note altered with `ShowSection34Details` false KEEPS its `GstCreditDebitNoteLink` and its GSTR-1 9B row.** | `plan.md` driving test (3). The §3.3 CARRY column — the hidden-sub-form rule is inverted for vouchers. |
| **T-5** | **Alt+X on a ₹1,84,733.45 invoice: greyed in the Day Book, "CANCELLED" overprinted, number still in sequence, and every balance moves by exactly the invoice.** | `plan.md` driving test (4). |
| **🔴 T-6** | **Bank reconciliation date survives an alteration.** Post a bank Payment, `BankReconciliation.SetBankDate(...)`, then alter the **narration only**; assert `BankDate` is unchanged. Then alter the **amount**; assert `BankDate` is cleared **and a warning was raised**. | **§3.4 — this defect is not in `plan.md` and will ship silently without this test.** |
| **T-7** | **Delete is REFUSED on a filed statutory document, with Cancel offered**; and **`NextNumber` never returns a number already used by a filed document.** | §4.5(a). Prevents S4 from creating the §4.1 reuse defect. |
| **T-8** | **ER-13 byte-identity** — §8.3. | |
| **T-9** | **Every REFUSED family refuses with its NAMED message.** One test per family: POS, Manufacturing Journal, payroll, and the three `InventoryVoucher` entry screens. Assert the **message**, not merely that nothing changed. | RULING 1: *"a silent no-op is the failure mode being avoided."* A test asserting "nothing happened" passes for a silent no-op too. |
| **T-10** | **Delete's referential guard names the COUNT of blockers**, for each blocker class in §3.3. | |

## 7.4 🔴 FAMILIES BEYOND THE ACCOUNTING EIGHT — non-negotiable, and why

The prerequisite that made W0-7 a blocker is discharged (§1.8) **but the reasoning still binds**: this is the
one phase that rebuilds a posted aggregate, so its regression surface **is** the set of voucher families the
fixture can post. A lifecycle test written only over Receipt/Payment/Contra/Journal/Sales/Purchase/CN/DN ships
**green by construction** over the other fifteen.

**Every test above that can be family-parameterised must be** — `[Theory]` over the base kinds
`PopulatedFixtureCoverageTests.SeededBaseTypes` yields, so **a newly seeded type fails the lifecycle suite on
the day it is added**, exactly as W0-7 made a newly seeded type fail the coverage lock. Specifically required:

- **Item-invoice Sales/Purchase** (`Voucher.InventoryLines`) — the accounts+stock atomic case
- **Stock Journal** — source/destination balance is still *enforced* (`InventoryPostingService.cs line 88-89`)
- **🔴 Physical Stock** — `InventoryLedger.cs line 193-207` makes a count a **checkpoint that RESETS the running
  balance**. Altering or deleting a Physical Stock voucher therefore changes on-hand for every *later*
  movement. **This is the single nastiest family in the phase and it must have its own explicit test**, not
  just a `[Theory]` row.
- **Purchase Order / Sales Order** + **Delivery Note / Receipt Note** — `OrderFulfilment`, `InventoryVoucher.OrderLinks`
- **Job Work In/Out Order, Material In/Out** — `material_order_links` (`Schema.cs line 1555`)
- **Credit Note / Debit Note** — T-4
- **Memorandum, Optional, Post-dated, Reversing Journal** — all four are excluded by
  `LedgerBalances.CountsAsOf`/`IsProvisionalBaseType` already, so the test is that altering one still changes
  **nothing** in the snapshot except the voucher identity vector. **🔴 See §12.8:** the family test list being
  unwritten is what let S5a ship with `Replace` passing `Optional`/`PostDated`/`ApplicableUpto` through
  wholesale, and the vector is now **REFUSED by name**, not carried and not warned about
- **POS and Payroll** — the **refusal** tests, T-9

## 7.5 Odd values — the standing rule, made concrete

A ±₹0.50 defect survived this project's life under round numbers, and `PopulatedFixtureCoverageTests` already
locks `HasOddPaisa` / `HasOddQuantityOrRate` on the fixture. **Every figure invented for these tests carries
odd paise or odd quantity**, and every assertion is to the **paisa**, never to the rupee:

- invoice total **₹1,84,733.45**, amended to **₹1,84,731.95** (a −₹1.50 delta that a rupee-rounded assertion would still see, and a −₹0.50 variant that it would not — **include both**)
- TDS gross **₹47,239.55** at a rate whose carve does not land on a whole paisa before rounding
- quantity **3.75** at rate **₹1,010.33**; a second line at **12.125** units to exercise 6-dp quantity
- GST 18% on a taxable value chosen so CGST and SGST do **not** halve evenly

---

# §8 — RISK

## 8.1 What could break — ranked

1. **🔴 `Replace` renumbers a mid-sequence voucher.** `Post` assigns when `Number <= 0` (`LedgerService.cs line 51-52`).
   A `Replace` built by calling `Post` on a fresh `Voucher(Guid.NewGuid(), …)` gets **both** the Guid and the
   Number wrong, and every §3.3 link breaks at once. *Mitigation:* §6.5 clauses 2–3; T-1 and T-2.
2. **🔴 `Replace` moves the voucher to the end of the Day Book.** `List.Remove` + `Add`. *Mitigation:* §6.5(4);
   T-1's **post-round-trip** half is the assertion that catches it.
3. **🔴 A rejected `Replace` destroys the original.** `Post` mutates at `:46` and `:51-52` before it can throw
   at `:48`. *Mitigation:* validate-then-swap; T-2 asserting byte-identity, not just "still present".
4. **🔴 Silent loss of `BankAllocation.BankDate`** — §3.4. No existing test would notice. *Mitigation:* T-6.
5. **🔴 S4 creates the number-reuse defect** — §4.1. It exists in the engine today but is unreachable; VL-2 is
   what reaches it. *Mitigation:* the §4.5(a) guard **in the same slice**; T-7.
6. **Copying old tax lines forward instead of re-deriving** — §3.2. *Mitigation:* T-3, plus S5b's temporary
   blanket refusal of GST/TDS/TCS-carrying vouchers so this cannot ship half-done.
7. **Someone routes delete through `SqliteCompanyStore.Remove`** (§1.6), which does not delete `tds_lines`,
   `tcs_lines`, `payroll_lines`, `voucher_inventory_lines` or `pos_tender_allocations`. *Mitigation:* §6.4's
   explicit decision + an in-source `DO NOT USE` note.
8. **The live-list cast** — `((List<Voucher>)company.Vouchers)` compiles (§1.2). *Mitigation:* a review grep.
9. **Deleting `CancelVoucher()` literally, destroying the abandon-entry behaviour** on six buttons. *Mitigation:*
   §6.3(2) — rename to `AbandonEntry()`.
10. **Test runtime.** `TotalClosingStockValue` (`StockValuationService.cs line 98-104`) loops every item, each
    replaying the whole book, so `DerivedStateSnapshot` is roughly quadratic. On the populated fixture, two
    snapshots per test × a family `[Theory]` could dominate the Ledger suite. *Mitigation:* build the snapshot
    once per book, not once per assertion; if it bites, gate the stock section behind a flag for the
    accounts-only families — **but never for the item-invoice or Physical Stock tests.**

## 8.2 What currently passes that would start failing

Measured, not estimated:

- **`CancelVoucher` is referenced in exactly two test files** — `tests/Apex.Desktop.Tests/InventoryVoucherEntryViewModelTests.cs`
  and `tests/Apex.Desktop.Tests/KeyboardArbitrationTests.cs`, one reference each. The S3 rename touches both.
- **The engine's `Cancel`/`Delete` are already covered** and those tests should be **unchanged** by this phase:
  `tests/Apex.Ledger.Tests/CostCentreTests.cs line 373`, `CostAllocationParallelSetTests.cs line 378`,
  `InterestTests.cs line 715`, `Inventory/ItemInvoiceTests.cs line 319`, `:341`,
  `Inventory/InventoryReportsTests.cs line 542`, `:914`. **If any of them moves, the engine semantics changed and
  that is a finding, not a fix.**
- Any keyboard-dispatch test asserting Alt+X reaches `CancelVoucher` app-wide will change by design (S3 step 1).
- **Nothing in `Apex.Ledger.Io` or `Apex.Persistence.Sqlite` should move at all.** A moved Io or Sqlite count is
  a **red flag**, not a pass — see §8.3.

**Per-project gate discipline (§6.2 of `plan.md`): predict all four counts before each merge and treat an exact
match as evidence the merge is semantically clean.** Baseline at HEAD: **Ledger 1668 · Io 414 · Sqlite 231 ·
Desktop 2195**. ⚠️ `plan.md`'s Phase 10.11 exit gate still quotes the **stale** baseline
*"Ledger 1294 · Io 368 · Sqlite 214 · Desktop 1836"* — **correct it before the first merge** or the gate
compares against a figure four phases old.

## 8.3 ER-13 — a book that never uses these verbs must be byte-identical

**This is satisfied by construction, and here is the proof rather than the assertion:**

- **No schema change** (§9). `vouchers.cancelled` already exists (`Schema.cs line 941-961`) and already round-trips.
- **`Cancel` and `Delete` already exist and are already persisted.** This phase adds *reachability*, not state.
- **`Replace` adds no field.** It writes an existing `Voucher` shape into an existing list slot.
- **The persisted SEMANTIC content is a pure function of the in-memory graph** — `SqliteCompanyStore.Save` is
  delete-all + full re-insert. A company whose voucher list is untouched serialises to the same semantic content.

> 🔴 **CORRECTION (S4 review, 2026-08-17) — this paragraph used to claim "persistence is a pure function of the
> in-memory graph" full stop, and assertion 1 below used to ask for the `.db` BYTES. Measured, that is unachievable
> for ANY book and always was, independently of this phase.** `entry_lines.id` is
> `INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT`; SQLite keeps its high-water mark in `sqlite_sequence`, so a
> delete-all + full re-insert **renumbers those surrogate ids on every save** and the file bytes change for a book
> nobody touched. Measured on a real round trip: the `.db` differed, and a schema-driven semantic dump differed too
> at the first `entry_lines` row (an id that had advanced 1 → 3) while every declared column beside it was equal.
> **The correct instrument is the canonical export** (`Apex.Ledger.Io.CanonicalXml.Export`), which carries the
> semantic model and no surrogate ids — measured byte-identical across a load → save round trip, across a REFUSED
> Alt+D and across a DECLINED one. A raw-`.db` comparison must never be written into an ER-13 test; a schema-driven
> semantic dump is acceptable **only** if it excludes surrogate autoincrement ids.

**T-8, the test that turns that into evidence — three assertions:**

1. Open a populated company, perform **no** lifecycle action, `Save`, reload, and assert the **canonical export**
   is byte-identical to the pre-save baseline. *(Shipped with S4:
   `VoucherDeleteAltDTests.The_canonical_export_is_byte_identical_across_a_load_and_save`.)*
2. Assert the same byte-identity across a **refused** Alt+D and a **declined** one — the delete verb touches
   nothing until it is both permitted and confirmed. *(Shipped with S4:
   `VoucherDeleteAltDTests.A_refused_and_a_declined_delete_both_leave_the_book_byte_identical`.)*
3. `Snapshot(company)` before and after loading the new build is identical.

---

# §9 — SCHEMA

**NO MIGRATION. Schema-clean end to end, and it is designed rather than lucky.**

- `Schema.CurrentVersion = 51` — verified at `src/Apex.Persistence.Sqlite/Schema.cs line 159`.
- The only state any of the three verbs writes is **`Voucher.Cancelled`**, and its column `cancelled INTEGER NOT NULL`
  has existed since v1 (`Schema.cs line 941-961`, the `vouchers` DDL). The inventory twin has
  `cancelled INTEGER NOT NULL DEFAULT 0` (`:1271-1281`).
- **`Replace` writes no new field.** It swaps a `Voucher` for another `Voucher` at the same list index.
- `SqliteCompanyStore.Save` (`:1790`) re-inserts the whole aggregate in one transaction, so persistence is a
  **pure function of the in-memory `Company` graph** — nothing to migrate, nothing to back-fill.
- **Io: none for the canonical model** — but that is an **assertion to be tested** (§8.3 T-8), not assumed.

**The version-allocation position, stated plainly as required:**

- `plan.md line 1636-1638` — the binding allocation is **WF-1 = v51, WF-2 = v52, WF-3 = v53**, and
  **🔴 "THE ALLOCATION ENDS AT v53. NOTHING IS RESERVED BEYOND IT."**
- **v54 is contested and reserved for nobody.** Two rows were each promised it by different sentences — WF-8's
  conditional persisted-closure flag, and W0-2b. `plan.md`'s ruling: *"whichever of the two ships a migration
  first takes v54, and MUST amend this line in the same commit."* Expected outcome, binding on nobody:
  **W0-2b = v54, WF-8 schema-clean.**
- **⇒ Phase 10.11 takes NO version and reserves none.** The only design choice in this document that could
  require one is §4.5's **fallback (b)** — a stored numbering floor. **Recommendation (a) exists precisely so
  that (b) is not needed.** If an implementer nonetheless needs a migration, the rule is absolute: read
  `Schema.CurrentVersion` **first** and the allocation line **second, at implementation time**, take the next
  free number, and **amend `plan.md`'s allocation line in the same commit** — with columns byte-identical in
  **both** `CreateV1` and the migration (`SchemaMigrationEquivalenceTests`), a **true-inverse** `DowngradeTo`,
  and Io parity. Watch the default-asymmetry trap in **both** directions: a `DEFAULT` back-filling to the *new*
  behaviour silently changes shipped figures (the v51 lesson); a `DEFAULT 0` back-filling to the *old* one
  silently re-ships the bug (the v52 lesson).

---

# §10 — RULING 5: the fidelity rows owed to `docs/full-clone-census.md` §1.3

RULING 5 (R12, 2026-08-16) requires **every slice** to end with a fidelity row in `docs/full-clone-census.md`
§1.3, or a record of why the corpus cannot settle the question. §1.3 is a numbered list of capabilities with
*any* sourced behavioural verification; when this design was written it ran to **item 9** (Company creation &
alteration, added with W0-2b and rewritten 2026-08-17), and item 9 established the two-column shape this design
follows. ⚠️ **Superseded as a statement of current state, 2026-08-17: the three rows drafted below were
subsequently landed in §1.3 as items 10-12, so the list is longer than "item 9" now. §1.3 is the single
derivation for how long it is and for the four fidelity figures; do not re-copy either into this file.** The
shape:
**"What IS sourced — each with the page it comes from, and nothing else claimed"** vs **"What is OURS, or
unsettled — separated deliberately."**

**Owed rows, one per slice.** Drafted here so the implementer copies rather than composes.

### Item 10 — Voucher cancellation (S3)
- **SOURCED:** `Alt+X` = *"To cancel a voucher / To cancel a voucher from a report"*, scope *"Vouchers &
  Reports"* — Book PDF **p.437** [printed p.433], **re-extracted with `-raw`** because `-layout` scrambles this
  table (§0.2).
- **OURS / UNSETTLED — and this is most of the row:** *what cancellation MEANS* is **not in the corpus at all**
  — `grep -oic cancel` over all nine admissible PDFs returns **2 hits**, one of them an EPF cancelled cheque
  (Book PDF p.320). Retaining the number, zero effect on balances, the **greyed** row and the **"CANCELLED"**
  overprint are **ours**. The confirmation wording is **ours, UNVERIFIED-BY-DESIGN**. **Un-cancel is
  unsettled** and not built.
- **CORRECTION owed to the plan:** `plan.md` says Tally *"scopes Alt+X to cancelling from a report"*. The
  corpus cell says **both** *"a voucher"* and *"a voucher from a report"*, scope *"Vouchers & Reports"*. Our
  report-only scope is **our** decision, not fidelity.

### Item 11 — Voucher and master deletion (S4)
- **SOURCED:** `Alt+D` = *"To delete an entry from a report"* — Book PDF **p.435** [printed p.431]. The
  per-family register recipe *"For Delete Entry Press `Alt+D' on Selected Entry"* — Book PDF **pp.32, 34, 37,
  42, 47, 49, 64, 71** and the inventory families. Master deletion is `Alt+D` from the **Alter** screen —
  Book PDF **p.15** (company), **p.21** (ledger), **p.23** (voucher type), each *"Press Two times Enter"*.
  Confirmation wording, verbatim, STUDY-GUIDE PDF **p.277**: *"Delete Yes or No?"* then *"Are you sure Yes or
  No?"*. The guard, verbatim, STUDY-GUIDE PDF **p.67**: *"You cannot delete any ledger, if any transaction(s)
  has been already made with that ledger."* Multi-master screens offer alter but **not** delete — Book PDF
  **pp.104-105**.
- **OURS / UNSETTLED:** the **single** prompt for a *voucher* — ~~the double prompt is attested for a **group
  company** and masters, **not** for a voucher, and we decline to copy it across by analogy.~~ 🔴 **CORRECTED
  2026-08-18 (§2.3's correction block; live record in `docs/full-clone-census.md` §1.3 item 11):** a voucher
  attestation DOES exist (Book pp.22-23), weak and self-contradictory, so this is **our decision against weak
  attestation** (A) — and the three master routes are separately a **deliberate divergence from an attested
  scope** (B). **What happens to
  a deleted voucher's NUMBER is not in the corpus**; our behaviour (reuse when it was the highest, permanent
  gap otherwise) is **ours** — and our **refusal to delete a filed statutory document, offering Cancel
  instead**, is ours, taken from the project's own numbering doctrine at
  `VoucherNumberingConfigViewModel.cs line 399-409` (whose cited source `numbering-design-v2 §2.5/§5.4` **is not in
  the repository**). Company deletion is out of scope.

### Item 12 — Voucher alteration (S5a/b/c)
- **SOURCED:** the register drill-down **is** the alteration screen — *"How to Show/Edit \<X> Voucher Entry in
  Tally Prime? … Select Month & **Show/Edit Entry**"*, Book PDF **pp.32, 34, 37, 42, 47, 49, 64, 71** and the
  inventory families; saved with **`Ctrl+A`** (Book PDF **pp.51, 53, 56, 58**). **TallyPrime has no separate
  read-only voucher screen** — one action is named, not two. `Ctrl+Enter` = *"To alter a master during voucher
  entry or from drilldown of a report"*, Book PDF **p.436** [printed p.432]. `Ctrl+D` removes a **line** inside
  voucher entry (same page) — a different granularity from `Alt+D`.
- **OURS / UNSETTLED:** **our key bindings are a deliberate divergence** — plain Enter keeps a read-only
  VoucherDetail column and **`Ctrl+Enter` opens voucher alteration**, to preserve the Miller-column cascade
  (USER DECISION 1, with a follow-up to reconsider). **🔴 The `plan.md` R7 line that Tally *"reserves
  `Ctrl+Enter` for display-only drill-down"* is WRONG and must be amended** — the corpus says Ctrl+Enter is an
  *alteration* key for a **master**; our extension of it to **vouchers** is a **smaller** divergence than the
  plan recorded. The five refused families, the warn-and-proceed date change, and the e-invoice/e-Way
  interlocks are **ours**. **Duplicate (`Alt+2`) and Insert (`Alt+I`/`Alt+A`) are corpus-attested (Book PDF
  p.435) and NOT BUILT** — named carry-forward, not a silent omission.

### Item 13 — the method note (not a capability row; propose it as a §0.2-style footnote to §1.3)
**`pdftotext -layout` scrambles the Book's three-column shortcut tables on pp.436-437** (key count ≠ function
count); **`-raw` resolves them row by row.** At least one shipped R7 claim was read off a scrambled dump. Any
fidelity claim sourced from Book pp.435-437 must be re-derived with `-raw`, and a `-layout` pairing is
UNVERIFIED unless the two counts match exactly.

---

# APPENDIX — the decisions this design asks the orchestrator/user to take (R12)

| # | Decision | My recommendation |
|---|---|---|
| D-1 | `plan.md` Phase 10.11 says five slices; **S1 and S2 are already merged** (`6a28d15`, `f2abdbb`). Amend the row? | **Yes — before any work starts.** Two false sentences are named in §1.1. |
| D-2 | Split S5 into **S5a (engine) / S5b (rehydration, simple families) / S5c (carves + carry)**? | **Yes.** §6.1. |
| D-3 | **Should a deleted voucher's number be retired?** | **Refuse Delete on a filed statutory document and offer Cancel** (§4.5a). No schema, no counter, corpus-shaped. Do **not** build a numbering floor. |
| D-4 | `plan.md` R7 claims Tally reserves `Ctrl+Enter` for **display-only** drill-down. **The corpus says it is an ALTER key.** | **Amend the R7 line.** USER DECISION 1 stands; its stated reason was half wrong (§2.4). |
| D-5 | `plan.md` R7 says Tally **scopes Alt+X to cancelling from a report**. The corpus scope is **"Vouchers & Reports"**. | Ship report-only if we choose, but **record it as our scope decision**, not fidelity (§2.2). |
| D-6 | Voucher delete confirmation: **one** prompt or **two**? | **One.** ~~The double prompt is attested only for a group company / masters (§2.3).~~ 🔴 **CORRECTED 2026-08-18 — see the correction block in §2.3.** A voucher attestation DOES exist (Book pp.22-23), weak and self-contradictory. The answer is unchanged; the basis is now **two** records: **(A)** the voucher routes ship one prompt as **our decision against weak attestation**, and **(B)** the three master routes ship one prompt as a **deliberate divergence from an attested scope**. Do not merge them. |
| D-7 | `SqliteCompanyStore.Remove` is **incomplete** (§1.6) and off the live path. Fix, or fence? | **Fence** — add a `DO NOT USE — incomplete` note. Fixing it invites routing delete through it. |
| D-8 | `plan.md`'s 10.11 exit gate quotes the **stale** baseline *Ledger 1294 · Io 368 · Sqlite 214 · Desktop 1836*. | **Correct to Ledger 1668 · Io 414 · Sqlite 231 · Desktop 2195** before the first merge (§8.2). |
| D-9 | `numbering-design-v2 §2.5/§5.4` is cited by shipped code and **is not in the repository**. | Land it, or restate its rule in-repo. Owed to the post-merge documentation slice (§4.3). |
| D-10 | `plan.md` implies the **pure-inventory Cancel** deferral is an engine gap; `InventoryPostingService.Cancel` (`:128`) **exists**. | Re-word: the deferral is **UI-only** (§6.3). |

---

# §11 — LATE FINDINGS (written last; they correct two things above and one thing in `plan.md`)

## 11.1 🔴 THE "CANCELLED VOUCHER KEEPS ITS NUMBER AND IS GREYED" CLAIM IS **MODEL-KNOWLEDGE**, AND THE PROJECT'S OWN VERIFICATION REPORT SAYS SO

This is the most consequential R7 finding in this document, and I found it only by chasing a stale line
reference (§11.2).

`plan.md line 320` states:

> *"**Cancel (Alt+X)** keeps the number in sequence (**greyed in Day Book**); **Delete (Alt+D)** removes it and
> can gap numbering (**verification §A14**)."*

Chasing "verification §A14": `docs/tally-feature-catalog-verification-report.md` has **no section `A14`**. The
referent is **item 14 of a numbered list**, at **line 68**, and it reads, verbatim:

> *"14. **Alt+X Cancel vs Alt+D Delete are not interchangeable** **`[model-knowledge]`**. Alt+X marks the
> voucher cancelled but keeps its number in sequence and shows it greyed in Day Book (preserves audit trail;
> meaningful mainly for Automatic numbering). Alt+D deletes and can create numbering gaps. State the differing
> effect on voucher-number continuity."*

**It is self-labelled `[model-knowledge]`.** And the same document lists it again at **line 177**, under
*"5. **Model-knowledge behavioral claims** (no external URL) needing a Tally spot-check"*, naming
**"Alt+X vs Alt+D numbering behavior"** explicitly, and closing:

> *"Each is individually plausible; **verify in-app or against TallyHelp before treating as authoritative.**"*

**What this means, stated without softening:**

1. **The belief that a cancelled voucher keeps its number and appears greyed/struck through is asserted from a
   model's memory. It has never been sourced.** My independent corpus sweep (§2.7, C-1/C-2) found the corpus
   silent — `grep -oic cancel` over all nine admissible PDFs returns **2 hits**, one an EPF cancelled cheque.
   **Two independent lines of evidence now agree that this is unsourced.**
2. **It has been propagating as if sourced.** `plan.md line 320` cites it as *"(verification §A14)"* — a citation
   shape that reads like a reference to a verified fact, to a section identifier that does not exist, pointing
   at an item whose own tag says the opposite.
3. **This is precisely what R7 forbids** (*"never assert a TallyPrime behaviour from memory"*). The design
   above is unaffected in substance — §2.7 already refused to treat it as settled and §4.1 shows our engine
   implements retain-the-number for its own good reasons — but **every place that cites it must be relabelled.**

**Actions (owed to the post-merge documentation slice, R5/R6):**

- **`plan.md line 320`** — replace *"(verification §A14)"* with an honest tag: the numbering/greying behaviour is
  **UNVERIFIED — model-knowledge, flagged for spot-check by the verification report itself (line 68, line 177
  item 5); corpus silent (2 hits, neither relevant)**. Our implementation is **ours**, chosen on the merits.
- **Census §1.3 item 10** (§10 above) — add this provenance to the "OURS / UNSETTLED" column. It strengthens
  the row: we are not merely un-sourced, we are **knowingly** un-sourced with a paper trail.
- **A general sweep is owed**: line 177 names **five more** model-knowledge claims travelling under the same
  flag — the single-entry-mode F12 toggle path, Payroll/Job-Work-requires-F11 availability, Bank Allocation vs
  Stat-Payment challan split, Stock-in-Hand derived balance, and rename-in-place semantics. **Anything in
  `plan.md` citing "verification §Ann" should be checked for the same defect.** Out of scope here; named so it
  is not lost.

**One genuinely sourced fact found in the same file, worth keeping** — `docs/tally-feature-catalog-verification-report.md line 162`
records *"Online e-Invoice & e-Way Bill (Rel 1.1 / 2.0; **cancel-on-alteration 7.0**)"* with an official source
(`help.tallysolutions.com` release notes). That supports §3.3 / §6.6's e-invoice interlock being a real
TallyPrime concern, though it does not settle our behaviour.

## 11.2 CORRECTION TO §6.3 AND §10 — `plan.md line 267` IS A STALE POINTER

`plan.md`'s VL-3 bullet says the greyed Day Book row is *"`plan.md line 267` already specifies"*. **Line 267 is the
tech-stack comparison section** (*".NET (C#) + Avalonia/WPF + SQLite…"*). The real specification is
**`plan.md line 320`**, quoted in §11.1.

I repeated the stale pointer in §6.3 item 5 and in §10 item 10 because I quoted `plan.md`. **Read `plan.md line 320`,
not `:267`** — and note that what it "specifies" turns out to be model-knowledge (§11.1). Add the pointer fix to
the documentation slice.

## 11.3 OPERATIONAL — THE WORKING TREE WAS NOT CLEAN, AND WHAT I TOOK FOR A SECOND PHASE 10.11 DESIGN

> **RESOLVED AFTER THE RUN — READ THIS BEFORE THE REST OF §11.3.** The `…-PARTIAL.md` reported below was **not**
> another agent's design. It was the **main loop snapshotting this document's own partial output mid-write**, to
> survive a session close. There was never a second Phase 10.11 design, and that snapshot has since been replaced
> by this complete file; it no longer exists in `docs/design-records/`. What follows is preserved as the record of
> what was visible at the time — the observations are accurate, the inference drawn from them was not.

The brief stated the checkout was **CLEAN**. At the end of my run `git status --porcelain` in
`…\worktrees\recursing-swirles-3138c6` reports:

```
 M tests/Apex.Ledger.Tests/DocumentCodeAgreementTests.cs
?? docs/design-records/
```

**None of it is mine.** I ran only read commands (`git log`, `git status`, `git diff`, `git merge-base`,
`sed`, `grep`, `pdftotext`) and wrote nothing inside the repository.

What is there:

- **`docs/design-records/phase-10-11-voucher-lifecycle-design-PARTIAL.md`** — 33 082 bytes, mtime **09:17
  that day**, i.e. **written during my run**. I read this as somebody else producing a Phase 10.11 design at
  the same time as this one. It was my own output, snapshotted by the main loop — see the resolution note above.
- `docs/design-records/w0-2b-company-screen-design.md` (92 279 B) and `w0-7-fixture-audit.md` (45 069 B), same mtime.
- `tests/Apex.Ledger.Tests/DocumentCodeAgreementTests.cs` — **+9 lines**, adding
  `["docs/design-records/w0-7-fixture-audit.md|24 voucher types"] = 2` to a slack allow-list, i.e. a
  doc-vs-code check being taught about the new `docs/design-records/` directory.

**I did not read, merge, or touch that file** — deliberately. Absorbing what I believed to be an unverified
concurrent draft would have defeated the point of independent grounding, and editing another agent's in-flight
file in a shared worktree is the two-agents-one-worktree failure this project has already been bitten by. The
caution was right even though the premise behind it was not.

**For the main loop, three things follow:**

1. **NOTHING NEEDS RECONCILING — THIS FILE IS THE SINGLE, COMPLETE, AUTHORITATIVE PHASE 10.11 DESIGN.** The
   `-PARTIAL.md` was this document's own mid-write snapshot rather than a rival design, and it has been replaced
   by this file. Execute from here: **the file:line and PDF-page citations in this document are the ones I
   opened myself** and can be re-checked in minutes.
2. **The gate figures in my §8.2 assume HEAD `3e968b3` with a clean tree.** With `DocumentCodeAgreementTests.cs`
   modified, the **Ledger** count and/or that test's assertions may already differ from the stated baseline
   **Ledger 1668**. Re-measure before predicting.
3. **A new top-level directory `docs/design-records/` is being introduced**, and a test allow-list has been
   amended to accommodate it. That is a repo-convention change nobody has recorded in `plan.md` — worth a
   deliberate decision rather than an accident.


---

# §12 — S5a REVIEW ADDENDUM (written after three adversarial review passes over the committed slice)

> This section is **normative** for S5a and supersedes the earlier text where they conflict. It exists because
> the slice shipped GREEN on all four projects and still held 33 findings, six of them BLOCKER.

## 12.1 The three unratified deviations — RATIFIED, with the reason each belongs

§6.5 listed five contract clauses; the shipped `Replace` added three refusals the design had not authorised.
All three were judged **by construction** and all three are **KEPT**. Recorded here so they are not
re-litigated:

| Deviation | Verdict | Why it belongs |
|---|---|---|
| **TypeId change refused** | **KEEP** | The collision is real and permanent, not speculative. Measured with the guard disabled: retyping Sales #2 to Purchase in a book holding Purchase #1-3 produced **two live Purchase vouchers numbered 2**, a permanent hole at Sales #2, and `NextNumber` unaware of either. What it blocks is the legitimate "keyed under the wrong type" correction, so **the message now names the remedy** (delete and re-enter under the correct type). |
| **Cancelled change refused** | **KEEP, and it is not over-broad** | Cancel is its own verb and un-cancel is §6.7-excluded. A **cancelled voucher can still be altered** when the caller carries the flag — pinned by its own test so nobody "fixes" the guard into a blanket refusal. |
| **Renumber refused** | **KEEP — and it is now actually enforced** | It was defeated by aliasing: `Replace(id, livePostedVoucher)` compared every identity field to itself, so a renumber, a cancel and an Optional flip all drove through unrefused and `NextNumber` jumped 12 to 100. An explicit `ReferenceEquals` refusal now makes clause 5's "construct a NEW voucher" assumption enforceable rather than merely stated. |

**A THIRD refusal was added after this table, by ORCHESTRATOR RULING, and it is recorded in §12.8: a change to
the PROVISIONAL-STATE VECTOR (`Optional` / `PostDated` / `ApplicableUpto`) is refused by name.** Read §12.8
before touching that guard — it supersedes the warn-and-proceed the S5a fix pass shipped.

**Two refusals ADDED by this review**, on the same standard:

- **`IsAccountingInvoice` change refused.** `Voucher.cs` declares the property get-only *"deliberately: the
  printed document type of an issued invoice must not be flippable after the fact"*. Clause 5's own premise —
  construct a new voucher — is the door that immutability left open, and `Replace` flipped it **in both
  directions, silently**. Same category as `Cancelled`: a fact about what the operator DID at posting time.
- **Voucher-Guid uniqueness enforced at both posting doors.** Clause 2 calls the Guid *"the outside world's only
  handle on this voucher"*; that was an assertion, not an invariant. A second accounting voucher carrying an
  already-used Guid posted without complaint, and a pure-stock `InventoryVoucher` could take an **accounting**
  voucher's Guid — after which one Guid named two different things and `Replace` silently altered the accounting
  one. `LedgerService.Post` and `InventoryPostingService.Post` now refuse both. The **rehydration** doors
  (`AddVoucherInternal` / `AddInventoryVoucher`) are deliberately NOT guarded — the store's PRIMARY KEY is the
  uniqueness authority there — and that is a declared gap, not an oversight.

## 12.2 Three PREMISE CORRECTIONS to this document and to the shipped code

1. **§6.5 clause 4's justification was FALSE.** The clause said the list index *"is the Day Book order of
   same-dated vouchers"*. `DayBook.Build` sorts by **(Date, Number)** and never reads the index — move a voucher
   to the end of the list and the Day Book is unchanged. The clause is still **CORRECT** (the index is the
   rehydration order, and it surfaces in `Outstandings`, which walks `company.Vouchers` in list order
   *"preserving first-seen order"*), but the reason is now the true one in both the service and the aggregate.
2. **The red proof's stated premise was FALSE.** Its comment claimed the lost number and lost position *"both
   leak into the derived surface"*. Measured: the corrected-in-place book and the Delete-then-rePost book agree
   on **every** balance, valuation, outstanding, cost and return figure. The divergence is the **voucher
   identity vector** plus the registers that list the voucher itself — now asserted exactly, as sections 12
   and 14 and no others.
3. **§3.2's stale-line-tax hazard is REAL but is NOT an S5a regression.** `Replace` accepts a replacement whose
   lines carry stale stamped `GstLineTax`, and GSTR-1/3B then declare the stale taxable value rather than the
   posted amounts. **`Post` accepts the byte-identical shape on a fresh Guid** — both go through the same
   `VoucherValidator.EnsureValid`, which cross-checks neither — so the engine is SYMMETRIC and nothing in S5a
   introduced it. Patching it inside `Replace` would make the two paths diverge for no stated reason. It is
   recorded as a **declared gap on `Replace`** with the sentence §3.2 was missing from the shipped code:
   **S5b's `ForAlter` must RE-DERIVE the line tax, never echo it.**

## 12.3 §3.3 CARRY + **WARN** — now implemented, and what it is not

§3.3 assigned the e-Way row **CARRY + WARN** and called it *"the highest silent-divergence risk in the
phase"*. S5a shipped the CARRY (free, via the preserved Guid) and **not the WARN**. Measured end to end: a
Generated e-Way Bill kept its portal-issued EWB number against a consignment value **ten times** the amended
invoice, and the EWB-01 request the app files became internally contradictory — `totInvValue` 70,800 over an
`itemList` summing to 7,080 — with the movement now below the Rule-138 threshold. Zero warnings, and no reader
of `ConsignmentValuePaisa` anywhere in `Reports` to notice.

`VoucherAlterationWarningCode.StatutoryRecordDiverged` now covers: the **e-Way** consignment value, the
**GSTR-1 Table 11A** advance receipt (whose frozen `AdvanceAmount` the return declares while reading the
voucher only as a date/liveness gate), the **e-invoice** IRN, the **section-34 CDN link**'s frozen
`OriginalInvoiceDate` (the 30-Nov cut-off basis), and the **RCM document** date.

**These are WARNINGS, not refusals, deliberately.** §6.6 puts the `EInvoiceStatus.Generated` REFUSAL in
**S5b**; S5a does not invent a refusal a later slice owns.

**A known limit, recorded so nobody points at it to argue the case is covered:** `Gstr1.EInvoiceReconciliation`
compares only the **document NUMBER**, so after an amount-only amendment it still reports `Mismatched = 0` — a
clean bill of health over a diverged document. That is worse than no detector, and it is pinned as a known
limit by its own test rather than quietly relied on.

## 12.4 §7.2 — what `DerivedStateSnapshot` actually covers

The helper was documented as *"a canonical, ordered, paisa-exact text dump of a company's **ENTIRE** derived
surface"*. It is not, and the overstatement was load-bearing: in a book with a **Physical Stock count
downstream**, a quantity-only alteration (10 units out becomes 20 units out for identical money) left the dump
**BYTE-IDENTICAL**, because a count RESETS the running balance and the stock section reads only on-hand and
closing valuation at the as-of date. `VoucherReplaceInventoryFamilyTests` contained no count at all.

Added for exactly that reason: **13 Stock movement**, **14 Day Book**, **15 Bank reconciliation statement**
(S5a's own headline carry-forward had no section in the instrument that defines its correctness), **16 GSTR-1
amendments**. Anything outside the numbered list is still uncovered, and that is now a **stated limit** rather
than an implied total.

Two structural guards were added with them: `SwallowedThrowCount` (a section that throws renders `!!` rather
than aborting — deliberate, but it made a dead section invisible) and a refusal to return a dump that lost the
**voucher identity section**, which is 36% of the dump and carries the whole discrimination of the red proof.

## 12.5 §8.3 ER-13 — the limit of what the canonical-export tests prove

**Non-interference is unfalsifiable in-tree.** ER-13 asks that a book which never uses these verbs be
unaffected; that comparison needs the **pre-S5a binary**, and all three canonical-export tests EXERCISE
`Replace`. What they prove is **residue-freedom** (an alteration and its inverse leave no trace; a refused one
leaves none at all), not non-interference — which is argued structurally in §8.3 and nowhere else.

**A trap for whoever extends those tests:** two INDEPENDENTLY built copies of the same book do **not** export
identically, because their masters carry different Guids. The ER-13 instrument must compare one book against
**itself** across an operation. Cross-book comparison is `DerivedStateSnapshot`'s job — it normalises Guids
precisely so it can do it.

## 12.6 The mutant/kill ledger — recorded so the next slice EXTENDS it rather than re-deriving it

The review ran **31 source mutations** across every clause of the Replace contract. **23 were killed; 8
survived** the full Ledger + Io + Sqlite gate, and one of those (the validator's Prevent-Duplicate self-skip)
survived **all four projects — 4,699 tests**.

| Survivor | What it permitted | Now killed by |
|---|---|---|
| the number guard tightened so any non-zero number is refused | refused every rehydrated alteration | `VoucherReplaceContractGuardTests.Replace_accepts_a_replacement_that_carries_the_vouchers_own_number` |
| carry-vs-clear `Side` clause dropped | a tick survived a Dr/Cr flip | `VoucherReplaceBankPairingTests.A_side_flip_clears_the_tick_and_the_warning_names_the_SIDE_not_the_amount` |
| `SameBankInstrument` `TransactionType` clause dropped | a cheque re-keyed as NEFT inherited a clearance | `VoucherReplaceBankPairingTests.A_changed_bank_transaction_type_is_a_different_instrument` |
| `SameBankInstrument` `InstrumentDate` clause dropped | a cheque re-issued later inherited a clearance | `VoucherReplaceBankPairingTests.A_changed_instrument_date_is_a_different_instrument` |
| pairing `LedgerId` clause dropped | one bank's tick migrated to another bank | `VoucherReplaceBankPairingTests.One_banks_reconcile_tick_never_migrates_to_another_bank` |
| validator self-skip deleted | clause 3 unsatisfiable under Prevent Duplicates | `VoucherReplacePreventDuplicateTests.Replace_is_accepted_under_Prevent_Duplicates_when_the_number_is_merely_preserved` |
| the null guard deleted | NRE instead of a named error | `VoucherReplaceContractGuardTests.Replace_names_a_null_replacement_instead_of_dereferencing_it` |
| pairing first-match `break` deleted (last-match-wins) | **the shipped first-match-wins was the WORSE of the two**; superseded by two-pass exact-first pairing | `VoucherReplaceBankPairingTests.Reordering_two_identical_instrument_bank_lines_keeps_both_reconcile_ticks` |

**One test was measured NOT to be load-bearing and is kept anyway:**
`Two_independently_built_identical_books_snapshot_identically` — 29 of the 30 S5a tests reddened under at least
one mutant; this one did not, because its failure mode is *impossibility* (the instrument cannot compare two
books at all) rather than vacuity. It is the precondition every equivalence assertion in the slice rests on.

## 12.7 Declared gaps carried OUT of S5a

1. **Stamped-vs-posted line tax is policed on NEITHER path** (§12.2 item 3). S5b's `ForAlter` must re-derive.
2. **No statutory REFUSAL of any kind** — `EInvoiceStatus.Generated` is §6.6's S5b refusal; S5a warns only.
3. **Guid uniqueness is enforced at the two POSTING doors, not at the rehydration doors.**
4. **`Gstr1.EInvoiceReconciliation` is a document-NUMBER comparison**, not an amendment detector (§12.3).
5. **`DerivedStateSnapshot` covers 16 numbered sections, not the whole report surface** (§12.4).
6. **ER-13 non-interference is unfalsifiable in-tree** (§12.5).

---

## 12.8 🔴 ORCHESTRATOR RULING — the provisional-state vector is **REFUSED**, not warned

> **This subsection is normative and supersedes §12's earlier disposition of the `Optional` blocker
> (review findings L1-01 / L2-01).** It also supersedes §7.4's implicit reading that the family merely needs a
> test. Nothing else in §12 changes.

### The finding that produced this

`Replace` passed `Optional`, `PostDated` and `ApplicableUpto` through **wholesale, in both directions, with no
guard and no warning**, and not one shipped test anywhere in the repository combined `Replace` with any of the
three. Measured, all on **byte-identical amounts**:

- an Optional voucher altered by a replacement that left the flag at its default became a **real posting** and
  swung the Sales closing by **₹1,84,733.45**;
- the mirror — a live voucher altered by a replacement built `optional: true` — dropped the same
  **₹1,84,733.45** out of the live books;
- a Reversing Journal accruing **₹3,000** "applicable upto 30-Apr" lost `ApplicableUpto` to a plain
  narration-only alteration and **the accrual never lapsed**: the scenario figure at 01-May moved 0 → 3,000.

The root cause is structural, and it is the one §7.4 warned about: **the mandated family test list was never
written**, so the family shipped green by construction.

### SUPERSEDED IN PLACE — the S5a fix pass's decision, and its reasoning, preserved

The fix pass closed the blocker with **warn-and-proceed**: it raised a new
`VoucherAlterationWarningCode.ProvisionalStateChanged`, let the flags pass through, let the balance move, and
pinned that movement as *intended* in `VoucherReplaceProvisionalFamilyTests`. Its stated reasoning, kept
verbatim in substance so it is not re-litigated from a strawman:

> *Warn, not refuse — TallyPrime's Ctrl+L (Optional) and Ctrl+T (Post-Dated) are available while a voucher is
> being altered, so refusing would be an infidelity; and the design's own treatment of an equally large
> semantic move (a date change, RULING 2) is warn-and-proceed. What is NOT acceptable is silence.*

**That fidelity argument is SOUND and is not deleted — see "R7" below, where it becomes the reason this
refusal is recorded as a divergence rather than as a neutral engineering choice.** What it does not survive is
the *caller* argument, immediately below.

**Honesty note on where the superseded decision lived.** It was never written into this document at all: the
fix pass recorded it only in `LedgerService.cs` doc comments and in the family test file's class comment.
There was consequently no §12 entry to strike through, and the block quoted above is taken from that shipped
code. That a new contract behaviour reached the tree with **no design-record entry** is precisely the
"unratified deviation" category §12.1 exists to close, and it is logged as such.

### THE RULING

**`Replace` REFUSES a change to the provisional-state vector — `Optional`, `PostDated` and `ApplicableUpto` —
by name, exactly as it already refuses a `Cancelled` change and for the identical reason: `Replace` is for
CONTENT, not for lifecycle state. It must not be a back door to Ctrl+L any more than to Cancel.** A
replacement that **carries** the vector unchanged is accepted silently, and that half is untouched.

**Why not carry-when-default**, the alternative both review lenses offered: `Optional` and `PostDated` are
**bools**. "Left at default" and "explicitly set to false" are indistinguishable, so carrying-when-default
would make it **impossible to turn an Optional voucher live** — it would silently ignore a real operator
intent. That ambiguity is exactly why the §3.4 bank-date **ECHO** rule works where this would not:
`BankDate` is `DateOnly?`, so `null` genuinely means "not stated". `ApplicableUpto` **is** nullable and
carry-when-default *would* have been expressible for it — but split behaviour across one conceptual vector is
worse than one rule, so all three are refused alike, and the code says so.

**Why refuse rather than warn:** warn-and-proceed cannot distinguish *"the operator pressed Ctrl+L"* from
*"the caller forgot to carry the flag"* — and the caller that will produce exactly that second shape is
**S5b's `ForAlter` rehydration**, the next slice. A refusal turns a silent balance corruption into a compile-
or test-time failure the moment S5b gets it wrong. This restores the precedent already set by finding L3-07,
which binds `ForAlter` to **RE-DERIVE** line tax rather than echo it (§12.2 item 3), for the same reason.

### R7 — recorded honestly, and in the right category

**This is OUR DELIBERATE NARROWING OF AN ATTESTED BEHAVIOUR. It is NOT a "corpus silent" case.** TallyPrime
genuinely attests `Ctrl+L` and `Ctrl+T` as **alteration-time** verbs (§2's shortcut table records `Ctrl+T` as
*"To mark a voucher as Post Dated"*). Refusing the change is therefore an **infidelity**, and it is taken
deliberately and with the divergence stated, on this ground and no other: **our `Replace` is a general-purpose
engine primitive with no operator intent attached to it, not an entry screen.** The toggle belongs on **its
own verb**, and a UI that wants Ctrl+L / Ctrl+T must call that verb rather than `Replace`. Nothing in the
corpus forbids the toggle; we are narrowing where it may be invoked from.

### Consequences

1. **`VoucherAlterationWarningCode.ProvisionalStateChanged` is REMOVED**, along with
   `RaiseProvisionalStateWarnings`. Nothing else in the tree raised or read it — the enum removal produced
   exactly five compile errors, all five inside the family test file, which is itself the evidence that no
   other caller depended on the warn-and-proceed. A comment stands where the member was, so it is not
   reintroduced by reflex.
2. **S5b's `ForAlter` must CARRY `Optional`, `PostDated` and `ApplicableUpto` from the posted voucher.** It is
   now the second obligation on that rehydration alongside re-deriving line tax; a `ForAlter` that drops
   either is a **test failure**, not a silent figure move.
3. **The §7.4 family test file keeps all four kinds** — see the table below. Five tests were re-pointed from
   "the move happens and is warned" to "the move is refused by name"; none was deleted, because each documents
   a real harm and the harm is now the thing being refused. One test was **added** for the
   several-flags-at-once message.
4. **Any future Ctrl+L / Ctrl+T verb** is a NEW method on `LedgerService` with its own contract, its own
   warnings and its own tests. It is out of S5a and out of S5b unless `plan.md` says otherwise.

### §7.4 family coverage after the re-point — all four kinds still covered

| §7.4 kind | Test |
|---|---|
| **Optional** | `Altering_an_Optional_voucher_that_carries_its_flag_leaves_it_out_of_the_live_books` (carried ⇒ accepted, no warnings) · `Turning_an_Optional_voucher_live_is_refused_by_name_and_the_books_do_not_move` · `Making_a_live_voucher_Optional_is_refused_by_name_and_its_value_stays_in_the_books` |
| **Post-dated** | `A_post_dated_voucher_that_carries_its_flag_is_altered_silently` (carried ⇒ accepted) · `Dropping_the_post_dated_flag_is_refused_by_name` |
| **Reversing Journal** (`ApplicableUpto`) | `A_narration_only_alteration_of_a_Reversing_Journal_keeps_it_lapsing_on_time` (carried ⇒ accepted) · `Dropping_ApplicableUpto_is_refused_by_name_so_the_accrual_still_lapses` · `Moving_ApplicableUpto_is_refused_and_the_refusal_names_both_dates` |
| **Memorandum** | `Altering_a_Memorandum_leaves_the_real_books_exactly_where_they_were` (snapshot diff confined to the voucher's own registers) |
| *(vector-wide)* | `The_refusal_names_every_member_of_the_vector_that_moved` — all three moved at once; the message must name all three, so a caller is not refused three times in sequence |

### Mutation proof

The refusal was **deleted**, the file's timestamp touched, and the solution rebuilt: **0 warnings, 0 errors,
and six Ledger tests reddened** — every one of the six refusal tests, and nothing else. The file was then
restored from a byte-exact copy and verified by **comparing all 55,957 bytes** (and SHA-256
`512359BC…F529C5FD`) against the pre-mutation baseline, not by a substring count — a substring count is never
1 for a block deletion and has already silently failed on this branch. The gate was re-run after restoring.
