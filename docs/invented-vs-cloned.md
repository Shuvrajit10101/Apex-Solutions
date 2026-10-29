# Invented vs cloned — the register

**Author:** A1 (Business Analyst) · **Date:** 2026-08-06 · **Status:** for review
**Fidelity target:** TallyPrime. Tally 7.2 is a checklist only, never the spec.
**Question this document answers:** *where did we build our own behaviour when TallyPrime already publishes one,
and what does the customer suffer for it?*

---

## ⚠️ RE-VERIFIED 2026-08-15 AGAINST HEAD `c56e5c3` — READ THIS BEFORE TRUSTING ANY ROW

Every one of the 35 rows was re-opened against the current worktree on **2026-08-15**. Corrections are marked
**†** and carried as a `†` line directly under the affected row's severity line, and in the Index. **Nothing was
silently rewritten** — the original claim is always quoted beside the correction. Four classes of change:

**1 · FOUR ROWS ARE NOW FIXED IN CODE.** They are left in place, struck through in the Index and re-verdicted in
the body, because a register that quietly deletes a row loses the record of why the fix was needed:

| Row | Fixed by | Proof at HEAD |
|---|---|---|
| **IV-5** Ctrl+B posts vouchers | `f2abdbb` | The app-wide arm is gone; `MainWindow.axaml.cs:386-409` is a RESERVED block that says "Ctrl+B IS FREE AND RESERVED — DO NOT BIND IT". `BillSettlementService.SettleAndPost` is deleted; settlement is now Alt+A scoped to Outstandings (`MainWindow.axaml.cs:604-615`) and **opens** a voucher. |
| **IV-7** Interest "Always" on the opening balance | `c408037` | `InterestCalculation.AlwaysLines` (`:218-239`) now accrues off `WalkRunningBalance` (`:258-275`), per movement inside the window. |
| **IV-9** Negative stock hard block | `a12e651` (schema v50) | `EnsureNoNegativeStockAnywhere` is **deleted**; `InventoryPostingService.cs:176` `DetectNegativeStock()` never throws, and `Company.cs:268` `WarnOnNegativeStock = true` gates the warning at `:184-185`. |
| **IV-10** Reorder Status ignores Sales Orders Due | `7e0457b` | `ReorderStatus.cs:120` `var nettAvailable = closing + pendingPO - soDue;`, shortfall off Nett Available at `:123`, `max(shortfall, MOQ)` at `:128-130`, DD-4 retired at `:63-67`. |

**2 · THREE ROWS ARE HALF-FIXED and their unqualified wording is now misleading** — IV-4, IV-8, IV-20. Each
carries a `†` correction naming which half moved and which half is live.

**3 · ~30 ROWS CARRY DRIFTED `file:line` CITATIONS.** `MainWindow.axaml.cs` moved ~+84 lines in the 800-900
band, `MainWindowViewModel.cs` ~+41 in the 5800 band, and **every `plan.md:NNN` citation in this register is
stale** (`plan.md` shifted +20 to +70 depending on the region). Corrected in place.

**4 · TWO SOURCING CLAIMS ARE OVERSTATED** — see the `†` lines on **IV-1** (the corpus is *not* silent on the
GST hierarchy *levels*, only on their ordering) and **IV-19** (the census measured 71 of 77, not "~50").

🔴 **AMENDED 2026-09-03 — THE REGISTER IS NO LONGER 35 ROWS. A NEW BLOCK, §2A, ADDS EIGHT: IV-36 … IV-43.**
They are the divergences the **T0-4 GST rate hierarchy** invented when its resolution half shipped (slices
S1/S2a/S2b), and they live in their own block for the reason the next paragraph gives — re-cutting §2's tables
would rewrite this document's record of what it found on 2026-08-06, which this header forbids. **The register
is 43 rows, of which 39 are open** (31 open at HEAD before that block, plus eight, none fixed). **IV-1 is
half-fixed and carries a ††† block**: the RESOLVER shipped; **CAPTURE did not**, the HSN half did not, and five
master-block rate readers are still unreconciled with it. ⚠️ **The eight new rows rest on VENDOR sources with a
SILENT corpus, and §2A's header states the ruling-9 category of every one of them and the fact that the corpus
was not on disk to re-measure this pass.**

🔴 **The counts in §1 and §2 below were NOT re-cut and are now wrong as totals.** They still say 35 rows /
8 CRITICAL / 11 HIGH, which counted IV-5, IV-7, IV-9 and IV-10 as open. **31 rows are open at HEAD** (5 CRITICAL,
9 HIGH). The per-row detail is authoritative; the summary arithmetic is not. Re-cutting the tables was declined
deliberately — it would have rewritten the document's own history of what it found on 2026-08-06.

🔴 **`659947760-Tally-Prime-Short-Key.pdf` (SHORTKEY) is now REJECTED outright, not merely "untrusted".**
`docs/w0-2-company-screen-grounding.md:52-76` (2026-08-14) rules it out as a source: its table is misaligned
against its own key column, the break is visible in the extraction (row 36 `Alt+2 → Sales Order` followed by two
key-less orphan labels), and it asserts `F3` = Email, `Ctrl+A` = Zoom, `F6` = Contra. **No row here rests on it
alone** — every SHORTKEY mention below is flagged "corroboration only" and is independently carried by
`help.tallysolutions.com` or BOOK — but **do not add a new citation to it, and do not treat its corroboration as
adding confidence.** The corpus-tag note below still calls it "shifted by one"; the measured shift is **two rows**.

---

**Inputs merged** — four independent hunts, run this session, deduplicated here:

| Hunt | Lens | Raw findings |
|---|---|---|
| `iv-claims` | uncited claims about Tally sitting in our own code comments | 6 |
| `iv-algos` | algorithms we invented where Tally publishes one | 11 |
| `iv-rulings` | deliberate deviations and deferrals, re-examined | 8 |
| `iv-analogue` | Tally idioms we never cloned, and gestures we invented | 12 |
| | **raw total** | **37** |
| | merged (entry-mode pair → IV-20; Ctrl+B key-squat → IV-5) | −2 |
| | reclassified as **not a divergence** (F10) → §5 | −1 |
| | **register rows** | **35** |

---

## How to read this document

**Ranking rule.** Rows are ordered by **what a real business actually suffers** — not by how large the code
change is, and not by severity label. A wrong tax figure outranks an unfamiliar keystroke. Something an
accountant would query outranks something only a power user notices. A missing capability that makes a
routine task impossible outranks a cosmetic divergence. **IV-1 is the worst item in the product.**

**Three kinds of problem, and they need three kinds of urgency.** These are not thirty-five instances of the
same thing:

| Class | What it is | Rows | Count |
|---|---|---|---|
| **A — wrong figures** | the app computes a number that is arithmetically wrong. It reaches an invoice, a return, a Balance Sheet or a payment. Nobody notices until an auditor, a supplier or a tax officer does. | IV-1, IV-2, IV-6, IV-7, IV-8, IV-10, IV-11, IV-14, IV-17, IV-22, IV-33 | **11** |
| **B — missing fundamentals** | not a divergence at all — a capability TallyPrime has that we simply do not have, which makes a routine task impossible. | IV-3, IV-4, IV-9, IV-12, IV-13, IV-15, IV-18, IV-19, IV-21, IV-25 | **10** |
| **C — divergences** | we do something, TallyPrime does it differently. Costs familiarity, keystrokes and trust. | IV-5, IV-16, IV-20, IV-23, IV-24, IV-26, IV-27, IV-28, IV-29, IV-30, IV-31, IV-32, IV-34, IV-35 | **14** |

Class A is a correctness emergency and should be scheduled as one. Class B is a product-completeness backlog.
Class C is a fidelity backlog. **Do not let Class C's size hide Class A's eleven rows** — that is exactly how
a wrong tax figure survives a review.

**One row in Class C moves money anyway: IV-5.** It is filed as a divergence because the *gesture* is what
diverges, but the consequence is posted vouchers the customer cannot remove.

**Evidence marks.** Each row's Tally-side claim carries one:

| Mark | Meaning |
|---|---|
| **[corpus]** | extracted from a licensed PDF with `pdftotext -layout` during the hunt, page/line given |
| **[web]** | `help.tallysolutions.com` or an official GST portal only — the licensed corpus is silent |
| **[code]** | our side, read from source in this worktree |
| **[none]** | the claim in our code has *no* source on either side — that is the finding |

**Corpus tags** (git-ignored, never committed, at `…\Apex Solutions(end)\tally\`):
**BOOK** = `664311548-Tally-Prime-Book.pdf` · **SG** = `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` ·
**GSTN** = `703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf` · **TB2** = `719244897-Tally-Book.pdf` ·
**SHORTKEY** = `659947760-Tally-Prime-Short-Key.pdf` (**untrusted** — its voucher-key block is shifted by one;
used only as corroboration, never alone).

> 🔴 **† 2026-08-15 — NEVER CITE A BARE `D`-NUMBER. Three registers use the same letter for different rows.**
> `docs/tally-fidelity-defects.md` has its own **D1/D3/D4/D7**; `docs/tally-gap-decisions.md` has a *different*
> **D3/D7/D12/D13**; and the eight "diverged rule" copies use yet a third **D1–D8**. Every `D`-reference in this
> document has been qualified with its owning file as part of this pass. Confusion between them has already cost
> this project real time.

**Companion documents.** `docs/tally-fidelity-defects.md` holds the 19-row defect register (D1–D18, U-A);
`docs/tally-gap-decisions.md` holds the decision set (D22, D23, X1, X6); `docs/voucher-entry-specification.md`
holds the condition matrix. Where a row here maps onto one of those IDs, it is given. **This register does
not repeat them** — it records the *invented-vs-cloned* dimension, which cuts across all three.

---

## 1. Executive summary

**35 distinct items** survive deduplication of the four hunts.

| Severity | Count | Meaning |
|---|---|---|
| **CRITICAL** | 8 | a wrong figure on a document the customer files, pays or is paid on — or correct data made permanently uncorrectable |
| **HIGH** | 11 | a routine task is impossible, or a figure a business acts on is wrong |
| **MEDIUM** | 12 | wrong or missing, with a workaround the operator must discover |
| **LOW** | 4 | costs familiarity or trust, not money |

By area:

| Area | Count | Rows |
|---|---|---|
| **TAX** — tax & money | 7 | IV-1, IV-2, IV-7, IV-8, IV-14, IV-15, IV-22 |
| **VAL** — valuation & inventory | 5 | IV-6, IV-9, IV-10, IV-11, IV-33 |
| **ENT** — entry & navigation | 17 | IV-3, IV-4, IV-5, IV-13, IV-16, IV-17, IV-18, IV-20, IV-24, IV-25, IV-27, IV-28, IV-29, IV-30, IV-31, IV-32, IV-34 |
| **MST** — masters | 3 | IV-12, IV-21, IV-26 |
| **RPT** — reports | 3 | IV-19, IV-23, IV-35 |

### The single worst item

**IV-1 — GST rate resolution runs TallyPrime's hierarchy backwards, and three of its five levels do not
exist.** `GstService.ResolveBase` walks Stock Item → Sales/Purchase ledger → give up, under a rule we named
ourselves ("most-granular-wins"). TallyPrime's shipped default is the opposite order —
**Ledger → Group → Stock Item → Stock Group → Company** — with the reverse selectable at F11. We have no
Accounting-Group level, no Stock-Group level and no Company level at all.

It is the worst item because it is the only one that is *simultaneously*: (a) arithmetically wrong on the
customer's own money, (b) triggered by the **normal** Tally habit of setting the rate on the sales ledger,
(c) invisible — nothing warns, the invoice simply carries the wrong tax, (d) carried onward into GSTR-1
rate-wise and GSTR-3B 3.1(a), where it becomes a filed under-declaration with interest and penalty exposure,
and (e) a **hard block** for the equally normal habit of setting rates once on a Stock Group, because the
missing level falls through to our unresolved-rate refusal (D8). A ledger at 18% against an item at 5% on a
₹1,00,000 intra-state sale: TallyPrime prints ₹1,18,000, we print ₹1,05,000. **₹13,000 of output tax
under-collected per invoice, silently.**

### Second, and different in kind

**IV-3 + IV-4 together** — a saved voucher can never be altered, and nothing anywhere in the shipped UI can
delete a voucher, ledger, group or company. These are not fidelity nits and not divergences; they are the two
most-used corrective actions in any accounting package, absent. On a "can the customer run their books at
all" ranking they are joint first. They are ranked below IV-1/IV-2 only because the framing that governs this
register puts a wrong tax figure above a missing keystroke — but note that **IV-3 and IV-4 make every other
row on this list permanent**: any voucher a Class-A defect posts wrongly can never be corrected or removed.

### The shape of the problem

Four root causes produce most of the thirty-five:

1. **We invented an algorithm and then wrote it into a comment as if it were Tally's.** IV-1
   ("most-granular-wins"), IV-8 (the Per divisors), IV-11 (the "best-available-cost chain"), IV-23 (the five
   ageing buckets), IV-33 (the by-value→by-quantity swap). In every case the comment is now the only
   specification anyone has, and it reads as sourced.
2. **The engine has the Tally behaviour; the UI never called it.** IV-4 (`LedgerService.Delete` exists,
   documented as "Alt+D", never called from `Apex.Desktop`), IV-16 (`LedgerService.Cancel` likewise), IV-22
   (`applyInvoiceRoundOff` has no production caller that passes `true`), IV-13 (`Manual`/`None` exist in the
   enum, unreachable). Four rows are a wiring gap, not a design gap — the cheapest fixes on the list.
3. **"Default" quietly became "only".** IV-9 is the clearest: DP-7 authorised a hard block *by default* with a
   company flag deferred to Phase 6; six phases shipped and the flag does not exist, so what we shipped is a
   wider deviation than the decision that authorised it.
4. **Accelerators were chosen from the first letter of our own feature name.** IV-5, IV-28, IV-30, IV-31 —
   Ctrl+B for "Bill", Ctrl+R for "Rate", Alt+R for "Reconciliation", Ctrl+F for "Filing" — each bound
   app-wide with `e.Handled = true`, so the four TallyPrime meanings can never be added without unpicking
   these first.

**Read §6 before scheduling any of this.** The register is a floor, not a census.

---

## 2. THE REGISTER — ranked by customer impact, worst first

### Index

**† = citation or verdict corrected 2026-08-15 against HEAD `c56e5c3`. ~~Struck rows are FIXED IN CODE~~ and
are no longer open defects — they are kept for the record, with the fixing commit named in the row body.**

| # | Sev | Class | Area | Item | Primary `file:line` |
|---|---|---|---|---|---|
| **IV-1** | CRITICAL | A | TAX | GST rate hierarchy runs backwards; the missing levels now exist as masters, nothing reads them (corrected 2026-08-16) — **††† 2026-09-03: the RESOLUTION half is FIXED (T0-4 S1/S2a/S2b); the row stays OPEN for CAPTURE, the HSN half and the five unreconciled bypasses** | **†††** `src/Apex.Ledger/Services/GstService.cs` `Hierarchy` / `WalkFor` *(was `GstService.cs:396`)* |
| **IV-2** | CRITICAL | A | TAX | §194Q TDS on the whole purchase value, not the excess | `TdsService.cs:74` |
| **IV-3** | CRITICAL | B | ENT | A saved voucher can never be altered | `VoucherDetailViewModel.cs:15` |
| **IV-4** | CRITICAL | B | ENT | Nothing can delete a voucher, ledger, group or company | **†** `LedgerService.cs:99` *(was `:112`)* |
| ~~**IV-5**~~ | ~~CRITICAL~~ | C | ENT | ~~Ctrl+B posts real, irreversible receipt/payment vouchers~~ — **† FIXED `f2abdbb`** | **†** binding deleted; `MainWindow.axaml.cs:386-409` reserves Ctrl+B |
| **IV-6** | CRITICAL | A | VAL | "Last Sale Cost" values closing stock at selling price | `StockValuationMethod.cs:31` |
| ~~**IV-7**~~ | ~~CRITICAL~~ | A | TAX | ~~Interest "Always" accrues only on the opening balance~~ — **† FIXED `c408037`** | **†** `InterestCalculation.cs:218-239` now walks the running balance |
| **IV-8** | CRITICAL | A | TAX | Interest "Per" divisors annualised; Calendar Month × 12 — **† half fixed; ×12 held on purpose pending T8** | **†** `InterestCalculation.cs:473-484` *(was `:213`)* |
| ~~**IV-9**~~ | ~~HIGH~~ | B | VAL | ~~Negative stock is an unrelaxable hard block~~ — **† FIXED `a12e651`, schema v50** | **†** guard deleted; `InventoryPostingService.cs:176`, `Company.cs:268` |
| ~~**IV-10**~~ | ~~HIGH~~ | A | VAL | ~~Reorder Status never nets Sales Orders Due~~ — **† FIXED `7e0457b`** | **†** `ReorderStatus.cs:120-133` *(was `:77`)* |
| **IV-11** | HIGH | A | VAL | Rateless-inward "best-available-cost chain" contaminates FIFO/LIFO | `StockValuationService.cs:496` |
| **IV-12** | HIGH | B | MST | A top-level (Primary) account group cannot be created | `GroupService.cs:13` |
| **IV-13** | HIGH | B | ENT | No Voucher No. field; Manual and None are unreachable | **†** `VoucherEntryViewModel.cs:1039` *(was `:935`)* |
| **IV-14** | HIGH | A | TAX | Intra-state CGST and SGST deliberately differ by one paisa | `GstService.cs:526` |
| **IV-15** | HIGH | B | TAX | e-invoice number guard is not FY-scoped; FY restart deferred on a circular rationale | **†** `Company.cs:681-682` *(was `:660`)* |
| **IV-16** | HIGH | C | ENT | Alt+X means the opposite of Tally's Alt+X | `MainWindowViewModel.cs:4831` |
| **IV-17** | HIGH | A | ENT | 2-digit years resolve on .NET's 1930–2029 pivot | `ApexDate.cs:36` |
| **IV-18** | HIGH | B | ENT | Alt+G "Go To" does not exist | `MainWindow.axaml.cs:182` |
| **IV-19** | HIGH | B | RPT | Drill-down stops at two screens; **† 71 of 77 report surfaces** are dead ends *(the "~50" below is the census-corrected figure)* | `MainWindowViewModel.cs:2083` |
| **IV-20** | MEDIUM | C | ENT | Entry mode: ~~wrong default~~ **† (a) FIXED `f277318`**, and (b) never remembered — **still open** | **†** `VoucherEntryViewModel.cs:155` + `:141-144` *(was `:91`)* |
| **IV-21** | MEDIUM | B | MST | No Alt+H Multi-Masters — one form per ledger | `MainWindowViewModel.cs:1180` |
| **IV-22** | MEDIUM | A | TAX | Invoice round-off hardcoded to the rupee, and never switched on | `GstService.cs:729` |
| **IV-23** | MEDIUM | C | RPT | Ageing buckets are ours; no age-by-bill-date | `Outstandings.cs:85` |
| **IV-24** | MEDIUM | C | ENT | Automatic numbering is `max+1`: gaps, and not date-ordered | **††** `LedgerService.cs:169` *(was `:154`; before that `:171`, past EOF when the file was 162 lines — see the ††-block)* |
| **IV-25** | MEDIUM | B | ENT | Three of TallyPrime's five numbering methods | **†** `NumberingMethod.cs:9` *(was `:8`)* |
| **IV-26** | MEDIUM | C | MST | Predefined groups cannot be renamed | `MasterAlterationRules.cs:211` |
| **IV-27** | MEDIUM | C | ENT | "Accept? Yes/No" exists on masters only; vouchers save silently | **†** `MainWindowViewModel.cs:4882` *(was `:4873`)* |
| **IV-28** | MEDIUM | C | ENT | Three TallyPrime report keys squatted app-wide | **†** `MainWindow.axaml.cs:331` *(was `:290`)* |
| **IV-29** | MEDIUM | C | ENT | Gateway sections and vocabulary are ours; **"Alter Company" landed under Masters 2026-08-17 — partially fixed** | **†** `MainWindowViewModel.cs:1047` *(was `:912`)* |
| **IV-30** | MEDIUM | C | ENT | Bare-letter menu hotkeys auto-assigned by row position | `GatewayColumn.cs:381` |
| **IV-31** | MEDIUM | C | ENT | The button bar paints seven non-keys in the accelerator colour | **†** `MainWindowViewModel.cs:6728` *(was `:6756`)* |
| **IV-32** | LOW | C | ENT | Report-line gestures absent: Alt+2, Ctrl+U, Alt+U, Ctrl+N | **†** `MainWindow.axaml.cs:182` — a pointer into the dispatcher, not a positive citation *(was `:875`)* |
| **IV-33** | LOW | A | VAL | A by-value additional-cost pool silently becomes a by-quantity spread | `AdditionalCostApportionment.cs:217` |
| **IV-34** | LOW | C | ENT | Ctrl+F7 = Physical Stock is attributed to a source with no locator | `SeedVoucherTypes.cs:33` |
| **IV-35** | LOW | C | RPT | "Tally-faithful blank-at-zero" is uncited and governs 100 call sites | **†** `IndianFormat.cs:30` *(was `:37`)* |

---

### IV-1 · GST rate resolution runs TallyPrime's hierarchy backwards; the missing levels now EXIST as masters and nothing READS them
**CRITICAL** · Class **A** · Area **TAX** · relates to `tally-fidelity-defects.md` D8

> **🔴 ††† 2026-09-03 — THE RESOLVER SHIPPED. THE ROW TITLE AND THE 2026-08-16 †† BLOCK BELOW ARE NOW HALF
> FALSE AND ARE LEFT STANDING AS THE RECORD OF WHAT THAT DATE FOUND. READ THIS FIRST.** T0-4 slices **S1 / S2a /
> S2b** landed the five-level walk. **The row is still OPEN, and again for a different reason than it says.** The
> accurate statement is now: **the levels exist, the RESOLVER exists, the CAPTURE does not.**
> - **What is now FIXED, and what the fix is grounded on.** `GstService` walks all five rungs as **data** — one
>   ordered list per `GstDetailSource` (`LedgerFirstWalk` / `StockItemFirstWalk`) driving ONE loop, transcribed
>   from the two vendor order strings this row's own Citation cell already records, with **stop-at-first-hit**
>   and **Company last**. `MasterAncestry` supplies the two ancestry rungs under a cycle guard. The ER-5
>   unresolved sentinel moved from two rungs in to **behind Company**, so the D8 hard block can no longer fire on
>   a customer who set the rate where the reference product tells them to. `ResolveDetailBlock` gives cess and
>   reverse charge the **same winning rung** as the rate. The invented **"most-granular-wins (DP-6)"** class doc
>   the *How it got in* cell condemns is **deleted**, and the two stale comments this row cites are gone with it.
> - **The ₹13,000 worked example in the *What the customer experiences* cell is now the CORRECT behaviour on a
>   v51+ book, not the defect.** Under the honoured `LedgerFirst` order the 18% sales ledger wins over the 5%
>   item, exactly as that cell says TallyPrime does. **The cell is left unedited** because it is the clearest
>   statement of what the defect cost, and because on a pre-v51 book — back-filled to `StockItemFirst` — the
>   old figure is still what the product does, by deliberate migration design.
> - 🔴 **R12 — USER RULING, 2026-09-03:** `LedgerFirst` is honoured; on books created from v51 onward the
>   **sales/purchase ledger outranks the stock item**. Books migrated from earlier schemas are back-filled to
>   `StockItemFirst` and resolve exactly as before, so no posted book changes.
> - **A live defect closed as a side effect:** canonical import already PARSED the Group / Stock Group / Company
>   GST blocks and **silently discarded** them. They are now read.
> - 🔴 **WHY THE ROW STAYS OPEN — three named pieces.** **(1) CAPTURE.** Census row **3.13** is still `ABSENT`:
>   `AccountGroupMasterViewModel` and `StockGroupMasterViewModel` contain **zero** `Gst` hits, neither master has
>   a GST field in `MainWindow.axaml`, and the importer is still the only writer. The gap is **sharper** than
>   before the resolver landed, not softer — the walk now reads blocks an operator cannot type. **(2) THE HSN
>   HALF.** `SourceOfHsnSacDetails` still has no reader; `GstReportSupport.HsnSacOf` still takes only a stock
>   item. **(3) FIVE UNRECONCILED BYPASSES** — census **T0-17**, two of which feed INV-01 and EWB-01.
> - **Where the detail now lives, so this row is not the only place to look:** the fidelity record is
>   `docs/full-clone-census.md` **§1.3 item 15** (`[GRADE: COMPARED]`, PARTIAL, ruling-9 categories, two OPEN R12
>   questions with measured figures); the eight things the chain invented are **IV-36 … IV-43** in §2A below;
>   the residue it left is census **T0-17 … T0-20**.
> - ⚠️ **THE *Fix* CELL'S "STILL OWED" LIST IS NOW PART-DISCHARGED AND IS NOT REWRITTEN.** Of its five clauses,
>   three shipped (the five-level walk defaulting to Ledger-first; the sentinel moved behind Company; the DP-6
>   doc deleted), one shipped only in part (*"cover all four item-first lookups"* — the resolver, cess and RCM
>   now share one walk, but the five D9 bypasses do not, which is T0-17), and one did **not** ship at all (the
>   **F11 switch** — `SourceOfGstRate` is settable only through import, not through any screen).

> **🔴 †† 2026-08-16 — THE ROW TITLE AND FIVE SENTENCES BELOW WERE FALSIFIED BY `e49b88e` AND ARE CORRECTED
> HERE (owed review of WF-1, lens 3 finding 9). READ THIS BEFORE ANYTHING ELSE IN THE ROW.** The row is **still
> OPEN**, but it is open for a *different reason* than it says, and it was overstating in the direction that
> **understates what shipped** — a session reading the old text alone would have **rebuilt the masters that
> already exist.** The accurate statement is: **the levels exist; the RESOLVER does not.**
> - The title said *"three of its five levels do not exist"*. They exist as masters:
>   `src/Apex.Ledger/Domain/MasterGstDetails.cs` is carried by `src/Apex.Ledger/Domain/Group.cs`,
>   `src/Apex.Ledger/Domain/StockGroup.cs` and `GstConfig.DefaultGst`, persisted at schema **v51**, with full
>   canonical XML/JSON parity and import validation.
> - The 2026-08-15 dagger below says *"`StockGroup.cs` still has no GST member"* and the *What we invented* cell
>   says *"carries no GST member anywhere in the file"*. **Both are now false** — `StockGroup.cs` declares
>   `public MasterGstDetails? Gst { get; set; }`. The dagger's own claim that "all code citations re-read and
>   unchanged" was true when written and is not now; it is left standing, with this correction above it, because
>   the sourcing correction it carries is still right.
> - The summary table's *"3 of 5 levels missing"* and the Tier-1 list's *"Group, Stock Group and Company levels
>   absent"* are corrected in place.
> - **What is still TRUE, and why the row stays open:** `GstService.cs`, `RcmService.cs` and `Reports/Gstr1.cs`
>   are **untouched**; `SourceOfHsnSacDetails` / `SourceOfGstRate` have **no reader outside the persistence and
>   Io layers**; every rate still resolves item-first; the D8 hard block still fires; the invented
>   "most-granular-wins (DP-6)" class doc is **still there**. Census **T0-4 stays OPEN**. See `plan.md` slice
>   **S4** for what landed, the R6 deviation it landed under, and the review's carry-forwards.
> - *(Pre-existing drift, unrelated to `e49b88e`: the *What we invented* cell cites `GstService.cs:412-414` for
>   "no company default rate field"; that comment is at `:416-418`. `GstService.cs` is untouched by `e49b88e`,
>   so this predates it.)*

> **† 2026-08-15 — verdict STANDS, one sourcing claim CORRECTED.** All code citations re-read and **unchanged**
> (`GstService.cs:396-415`, `:14-15`, `:412-414`; `StockGroup.cs` still has no GST member — ⚠️ **false since
> `e49b88e`, see the †† correction above**; `memory.md:405` and
> `:380-381` exact). **But the Citation cell's "Corpus **silent**" is overstated.** Re-extracted this session with
> `pdftotext -layout`: `703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf`, extracted **lines 2661-2665**, enumerates
> *"GST can be implemented in Tally prime by using any one method of the following: 1. Defining at Company Level
> 2. Defining at Stock Group Level 3. Defining at Stock item level 4. Defining at Ledger Level 5. Creating GST
> Classification"*. **The corpus therefore names Company, Stock Group, Stock Item and Ledger as GST-rate levels —
> so "we have no source for the missing levels" is false.** What the corpus does *not* give is the **resolution
> order**, and the literal grep is still right: 0 hits for "hierarchy" in a GST-rate sense across all ten PDFs
> (the only two hits, BOOK :546 and SG :1969, are about *account-group* hierarchy). Note the corpus's fifth item
> is **GST Classification**, not the accounting **Group** that TallyHelp's order carries — the two lists are not
> the same five. Also recorded at `plan.md:1188-1190`.

| | |
|---|---|
| **What the customer experiences** | A dealer sets 18% on the "Sales — 18%" ledger (the normal Tally habit) and 5% on a stock item. On a ₹1,00,000 intra-state sale TallyPrime charges 18% = ₹18,000 (CGST 9,000 + SGST 9,000) and prints ₹1,18,000. We charge 5% = ₹5,000 and print ₹1,05,000 — **₹13,000 of output tax under-collected on every such invoice**, carried into GSTR-1 rate-wise and GSTR-3B 3.1(a) as a filed under-declaration. In the other direction, an item-level rate set for one branch silently overrides the ledger rate Tally would have honoured. And because our chain has **no Stock Group and no Company level at all**, a customer who follows Tally's normal practice of setting a rate once on a Stock Group gets an "unresolved rate" **hard block** (D8) on every line of that group. |
| **What we invented** | `src/Apex.Ledger/Services/GstService.cs:396-415` — `ResolveBase` walks Stock Item (`:398-404`) → Sales/Purchase ledger (`:405-411`) → unresolved sentinel (`:415`), under a rule we named ourselves and wrote into the class doc at `:14-15`: **"most-granular-wins (DP-6)"**. ⚠️ **CORRECTED 2026-08-16 — this cell used to continue "`src/Apex.Ledger/Domain/StockGroup.cs` carries no GST member anywhere in the file". Since `e49b88e` it does**: `src/Apex.Ledger/Domain/StockGroup.cs` declares `public MasterGstDetails? Gst { get; set; }`, as does `src/Apex.Ledger/Domain/Group.cs`, and `GstConfig` carries `DefaultGst` plus the two source-order options. **What is still invented and still shipping is the RESOLVER, not the absence of masters** — `GstService.cs` is untouched, nothing reads the new members, and there is still no F11 switch. (`GstService.cs:412-414` was cited for "no company default rate field"; that comment is now at `:416-418` — pre-existing drift, `GstService.cs` is untouched by `e49b88e`.) **[code]** |
| **What Tally does** | Resolves GST Rate and HSN/SAC through a **five-level** hierarchy, stopping at the first master that carries the detail. Shipped default: **Ledger → Group → Stock Item → Stock Group → Company**. The alternative — Stock Item → Stock Group → Ledger → Group → Company — is selectable at F11 > Set/Alter Company GST Rate and other details > Additional Configurations. |
| **Citation** | **[web]** `help.tallysolutions.com/tally-prime/gst-master-setup/india-gst-manage-hsn-code-sac-and-tax-rates-tally/` — "HSN/SAC & GST Rate Hierarchy in TallyPrime", both hierarchy strings quoted verbatim. Corpus **silent**: 0 hits for "hierarchy" in a GST-rate sense across all ten PDFs. ⚠️ **This cell records the two hierarchy STRINGS — i.e. ONE selectable alternative — and NOTHING about the two lookups (HSN/SAC and rate) being SEPARATELY selectable.** `GstDetailSource` used to point here for that claim and the pointer was dangling; the claim lives only in `plan.md`'s Phase 10.10 "R7 fidelity — sources of record" bullet, is `[web]`, and **A14 never ran on it** (owed review 2026-08-16, lens 3 finding 5). It carries two shipped schema columns. Corpus measured this review: 0 hits for "Source of HSN/SAC", 0 for "Source of GST Rate", across all ten PDFs. |
| **How it got in** | A plan decision, uncited. `memory.md:405` records it under "User-approved DPs" as "rate resolution item→ledger→company"; `memory.md:380-381` repeats it in the Phase-4 slice note. No Tally source was consulted, and the invented label was then written into the class doc where it now reads as a specification. |
| **Fix** | ~~Add a GST details block to `StockGroup` and to the accounting `Group`, plus a company-level default rate on `GstConfig`.~~ **✅ DONE — `e49b88e`, schema v51** (`MasterGstDetails` on both masters + `GstConfig.DefaultGst`, plus the two source-order options, with SQLite and canonical XML/JSON parity). **STILL OWED, and the whole reason this row is open:** implement the five-level walk in `ResolveBase` with the F11 "source of GST Rate & HSN/SAC" switch, defaulting to Ledger → Group → Stock Item → Stock Group → Company; cover **all four** item-first lookups, not just `ResolveBase` (`plan.md` orchestrator ruling 1); keep the taxability short-circuit, but move the fail-fast to *after* the Company level so D8's hard block can never fire on a customer who set the rate where Tally tells them to; and delete the invented "most-granular-wins (DP-6)" class doc. **Read `plan.md` slice S4's carry-forwards (a)–(h) first** — the back-fill's real scope, the validation reachability limit and the missing rate upper bound all bear on the resolver. |

---

### IV-2 · §194Q TDS is computed on the whole purchase value, not on the value exceeding ₹50 lakh
**CRITICAL** · Class **A** · Area **TAX**

> **† 2026-08-15 — verdict STANDS; every citation exact, zero drift.** `TdsService.cs:74`, `ThresholdCrossed`
> `:88-94`, `TcsService.cs:139-146`, `SeedTdsTcsRates.cs:63` and `memory.md:1035` are all unchanged.
> `NatureOfPayment` still has **no** "calculate on value exceeding the threshold" field — its members are
> Id / SectionCode / Name / RateWithPanBp / RateWithoutPanBp / SingleTransactionThreshold / CumulativeThreshold /
> FvuSectionCode / EffectiveFrom / IsPredefined. Corroborated by `docs/full-clone-census.md` T0-1, which names
> this row and records that WF-2 was planned in Phase 10.10 and **has not landed**.

| | |
|---|---|
| **What the customer experiences** | A trader whose annual purchases from one supplier reach ₹60,00,000 has **₹6,000 withheld by us where TallyPrime withholds ₹1,000**. The supplier is short-paid ₹5,000 and disputes the payment. The Form 26Q we produce reports a ₹60,00,000 assessable value against a ₹6,000 deduction that the seller's 26AS cannot be reconciled to. The over-deduction repeats on **every subsequent bill in the year**, because the full value is charged each time rather than the incremental excess. |
| **What we invented** | `src/Apex.Ledger/Services/TdsService.cs:74` — `var tds = NearestRupee(assessableValue.Amount * rateBp / 10_000m);`, gated by `ThresholdCrossed` at `:88-94`. The threshold is a **pure gate**: once crossed, the full assessable is charged. `NatureOfPayment` has no "calculate on value exceeding the threshold" field at all. Our own engine disagrees with itself — the mirror carve **is** implemented for TCS §206C(1H) at `src/Apex.Ledger/Services/TcsService.cs:139-146`. And the seed comment states the rule the code does not implement: `src/Apex.Ledger/Seed/SeedTdsTcsRates.cs:63` "§194Q Purchase of goods: 0.1% on value over ₹50,00,000/FY". **[code]** |
| **What Tally does** | Carries **"Calculate tax on value exceeding the threshold/exemption limit"** on the TDS Nature of Payment master and, when set, deducts only on the excess. Its worked example: purchase of Rs. 60 lakhs from a seller with PAN ⇒ TDS on Rs. 10 lakhs at 0.1%. |
| **Citation** | **[web]** `help.tallysolutions.com/tds-on-purchase-of-goods-under-section-194q/` — the option name and the 60-lakh/10-lakh worked example. Corroborated at `help.tallysolutions.com/tds-transactions-tally/` ("50 lakhs participate under Exemption limit and the amount crossing the threshold limit … participate in the Deduction at Normal Rate"). Corpus silent on §194Q mechanics. |
| **How it got in** | Never questioned. The threshold model (single-transaction OR cumulative, gate only) was written once in `ThresholdCrossed` and reused; `memory.md:1035` records the design as "tests the section threshold (single-transaction OR cumulative-FY)" with no mention of a chargeable-base carve. The seed comment asserting the correct rule was written and never checked against the code. |
| **Fix** | Add `CalculateOnValueExceedingThreshold` to `NatureOfPayment` (default Yes for §194Q, matching Tally's documented use) and give `TdsService` the same `ChargeableBase(nature, current, prior)` helper `TcsService.cs:139-146` already has: `excess = (prior + current) − threshold`, clamped to `[0, current]`. Keep `AssessableValue` as the full value so the FY projection stays exact — exactly as the TCS path does. **Then reconcile the two services**, because having the rule right on one side and wrong on the other is how this survived. |

---

### IV-3 · A saved voucher can never be altered — the drill target is read-only and no alter path exists
**CRITICAL** · Class **B** · Area **ENT**

> **† 2026-08-15 — verdict STANDS** (`AlterVoucher|OpenVoucherForAlter|EditVoucher|ReplaceVoucher|UpdateVoucher`
> still returns **zero hits** repo-wide; `IsAltering` still exists only on the three master VMs).
> **Citations corrected:** `OpenVoucherDetail` `:2124-2135` → **`:2125-2133`** · the "exactly as Tally does it"
> comment `:5804-5817` → **`:5845-5847`** and the `IsAltering` block `:5804-5842` → **`:5846-5881`** ·
> `plan.md:335` → **`plan.md:399`**. `VoucherDetailViewModel.cs:15`, `:3144` and `:3312` are unchanged.

| | |
|---|---|
| **What the customer experiences** | A typo in a posted voucher — wrong amount, wrong ledger, wrong narration, wrong date — is **permanent**. The habit that makes Tally operators fast (key at speed, fix in the Day Book afterwards) is unavailable. The only remedy is a compensating journal, which a Tally user would never think to write, and which leaves both the wrong voucher and the correction visible in every register, every ledger and every statement they hand their accountant. |
| **What we invented** | Drilling a Day Book or register row opens `VoucherDetailViewModel`, whose own summary states it is "a terminal (non-drillable) leaf column in the cascade — **read-only**, so it never mutates the books" (`src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:15`), opened by `MainWindowViewModel.OpenVoucherDetail` (`:2124-2135`). Repo-wide search for `AlterVoucher`, `OpenVoucherForAlter`, `EditVoucher`, `ReplaceVoucher`, `UpdateVoucher` — **zero hits across the solution**. Alteration exists for masters only, and only three: Ledger and Group via `AlterHighlightedChartRow` (`:3144`), Stock Item via `AlterHighlightedStockItemRow` (`:3312`). **[code]** |
| **What Tally does** | Enter on a register or Day Book row opens the voucher in **alteration mode** — the same entry screen, pre-filled — re-accepted with Ctrl+A. The corpus teaches this as *the* correction workflow for every voucher type, phrased "Show/Edit Entry". |
| **Citation** | **[corpus]** BOOK p.28 — "How to Show/Edit contra Voucher Entry in Tally Prime? Step: GOT > Display More Reports > Account Books > Contra Register > Select Month & Show/Edit Entry"; repeated verbatim for Receipt (p.29), Payment (p.32), and for the Purchase, Sales and Journal registers (extracted lines 1499, 1676, 1735). **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` — "Ctrl+Enter — To drill-down and open a voucher for display". |
| **How it got in** | Never questioned. `plan.md:335` lists "Ctrl+A save, Alt+D/Alt+X" among the voucher gestures, but the drill was built to RQ-7 as a read-only viewer and **no plan item ever added the alter path**. The master side got `IsAltering` (`MainWindowViewModel.cs:5804-5817`, whose comment says it works "exactly as Tally does it"); the voucher side never did. |
| **Fix** | Make the voucher drill open the matching entry view model pre-filled in an alteration mode, re-using the master pattern already proven in `MainWindowViewModel.ActivateSelected` (one screen serves Create and Alter; Ctrl+A runs whichever verb it was opened for). Re-post through `LedgerService` so the voucher is **replaced** rather than a second one added, keeping its number. |

---

### IV-4 · Alt+D exists nowhere: nothing in the UI can delete a voucher, ledger, group or company
**CRITICAL** · Class **B** · Area **ENT**

> **† 2026-08-15 — the DEFECT STANDS; two of its supporting sub-claims are now FALSE and the title overstates.**
> **(1) "The string 'Alt+D' appears zero times anywhere in `src/`" is no longer true** — commit `6a28d15` added
> `MainWindow.axaml.cs:1080, 1085-1087`, and it also appears at `LedgerService.cs:8, 99` and
> `InventoryPostingService.cs:136`. **(2) The bare `Key.D` Day-Book jump moved and narrowed**: it is now
> `MainWindow.axaml.cs:959` `case Key.D when CanQuickJump(vm, e):`, and `CanQuickJump` (`:1096-1097`) requires
> `e.KeyModifiers == KeyModifiers.None`, **so Alt+D no longer opens the Day Book** — `:1084-1087` reserves the
> chord for a later delete slice. **What has NOT changed is the defect itself:** Alt+D still deletes nothing.
> The only `.Delete(` calls in `src/Apex.Desktop` remain `CompanyStorage.cs:105/142` and
> `MainWindowViewModel.cs:2488 DeleteSelectedSavedView`; `LedgerService.Delete` still has no Desktop caller.
> **Citations corrected:** `LedgerService.cs:112` → **`:99`** · `MainWindow.axaml.cs:875` → **`:959`** ·
> `plan.md:268` → **`plan.md:290`** · `MainWindowViewModel.cs:4895-4923` → **`:4893-4930`** (`RequestMasterAccept()`
> at `:4904`). `docs/voucher-entry-specification.md:101` is **unchanged and still false** — the Fix instruction to
> correct it had not been carried out; it is corrected there now, dated 2026-08-15.
>
> **†† 2026-09-05 (b1 + b4 landings, PRs #47 and #49) — THE TITLE IS NOW WRONG IN TWO OF ITS FOUR NOUNS, AND THE
> 2026-08-15 LINE "Alt+D still deletes nothing" IS SUPERSEDED.** `Alt+D` now really deletes: **a COMPANY**
> (`DeletionTarget.Company` → `PerformOpenCompanyDeletion`, behind the Y/N confirmation, removing the `.db` and
> landing on Company Select — census row **1.4**, now `COMPLETE`), and **four of the eight PAYROLL MASTER kinds**
> (`DeletionTarget.PayrollMaster` — census row **7.16**, now `PARTIAL` at 4 of 8). The enum is now
> `{ None, Voucher, Ledger, Group, StockItem, PayrollMaster, Company }`.
> ⚠️ **The row is NOT struck, because what remains open is still substantial:** the **voucher**, **ledger** and
> **group** arms named in the title are the ones still to re-derive against HEAD, and the payroll half is only
> half done — pay head is blocked on **T2-38** (an engine gap: `PayHeadService` has no `Alter` at all), and
> salary structure and tax declaration are unscoped.
> 🔴 **This note deliberately does NOT re-audit the whole entry.** The failure mode this document keeps catching
> is the sweeping correction that itself goes stale within one commit — so it records only what these two
> landings changed. **Whoever next touches IV-4 should re-derive all four nouns against HEAD rather than trusting
> the title, the 2026-08-15 note, or this one.**

| | |
|---|---|
| **What the customer experiences** | A duplicated or plainly wrong voucher stays in the books forever; a ledger created by mistake can never be removed and clutters every picker and every Trial Balance from then on. The operator's first reflex — select the row, press Alt+D — produces **nothing at all**: no action, no message. That reads as the app being broken rather than as a missing feature, and it is the state they are in every time they mis-key something. |
| **What we invented** | The string "Alt+D" appears **zero times** anywhere in `src/`. The only bare `Key.D` arm in the ~700-line dispatcher is the Day-Book quick jump (`src/Apex.Desktop/Views/MainWindow.axaml.cs:875`); the XAML has no `KeyBinding`/`HotKey` for it either. The engine's delete is written *and documented as the Tally gesture* — `src/Apex.Ledger/Services/LedgerService.cs:112` "Alt+D — remove entirely; may leave a gap in numbering" — and **no code in `Apex.Desktop` ever calls it** (the only `.Delete(` calls in the Desktop project are a file delete in `CompanyStorage` and `SavedViews.Delete()`). **[code]** |
| **What Tally does** | Alt+D is the universal delete gesture — a voucher from a report, a ledger, a group, a company, a column in a report — each behind a Yes/No confirmation. |
| **Citation** | **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` — "Alt+D — To delete a voucher — Bottom bar". **[corpus]** SG p.67 "Ledger Deletion" and p.69 "Group Deletion": "…which you want to delete Press Alt+D supply Yes to confirm Deletion". BOOK pp.28/29/32 — "For Delete Entry Press `Alt+D' on Selected Entry". GSTN extracted line 30, deleting a company — "Press Alt+D to Delete. A confirmation message appears". |
| **How it got in** | An **uncited claim in our own spec**: `docs/voucher-entry-specification.md:101` records the interrupt row as "Esc/Alt+D present". It is not present. `plan.md:268` specified Delete (Alt+D) correctly — the engine half shipped and the gesture half never did. |
| **Fix** | Bind Alt+D on the Day Book / register drill and on the Chart of Accounts and master lists, routing vouchers to `LedgerService.Delete` and masters to their remove paths, behind the Y/N confirmation the WI-11 prompt already provides (`MainWindowViewModel.cs:4895-4923`). **Correct `docs/voucher-entry-specification.md:101` in the same change** so the false claim cannot survive another review. |

---

### IV-5 · ~~Ctrl+B posts real receipt/payment vouchers~~ — **FIXED IN CODE**
**CRITICAL** · Class **C** · Area **ENT** · ✅ **CLOSED 2026-08-15**

> **† 2026-08-15 — FIXED by `f2abdbb` ("settlement comes off Ctrl+B — Alt+A now OPENS a voucher instead of
> posting one", Phase 10.11 S2). Everything below describes the pre-fix state and is retained only as the record
> of why the fix was needed. Do not schedule it.** Verified at HEAD `c56e5c3`:
> - The app-wide arm at `MainWindow.axaml.cs:346-351` is **deleted**. In its place `:386-409` is a reservation
>   block: *"Ctrl+B IS FREE AND RESERVED — DO NOT BIND IT. (Phase 10.11 S2 / VL-4 / register row IV-5.)"*
> - `MainWindowViewModel.SettleBills()` (`:5617-5620`) is **deleted**, replaced by
>   `OpenSettlementVoucherFromOutstandings()` (doc at `:5626-5648`), bound to **Alt+A scoped to Outstandings**
>   (`MainWindow.axaml.cs:604-615`) — it **opens** a Receipt/Payment pre-loaded with the bills, which is exactly
>   what the Fix cell below prescribed.
> - `OutstandingsViewModel.SettleSelected()` (`:117`) is **deleted** (`:44-47` records the removal);
>   `BillSettlementService.SettleAndPost` is **deleted** and `:9` now reads *"it only produces domain objects, and
>   it **posts nothing**"*. The literal-"Cash" ledger (`:127`) and full-amount rule (`:163`) are gone with it.
>
> **One residual, deliberately NOT closed by that commit:** Basis of Values still does not exist (grep for
> "Basis of Values" / "ScaleFactor" still returns zero), so Ctrl+B is free but does nothing. That is an
> *absence*, not the defect this row recorded, and it belongs with IV-32's report-gesture backlog.

| | |
|---|---|
| **What the customer experiences** | A Tally user opens Bills Receivable, presses Ctrl+B expecting to switch the scale to millions, and **instead posts a batch of receipt vouchers against their debtors**. Because nothing in the shipped UI can delete or cancel a posted voucher (IV-3, IV-4), those receipts are permanent. Even used deliberately the feature cannot express a real settlement: no part payment, no cheque, no bank — every settlement is full-value **cash**, and it fails outright if the company's cash ledger is called anything other than "Cash". |
| **What we invented** | Ctrl+B is bound **app-wide and unconditionally** (`src/Apex.Desktop/Views/MainWindow.axaml.cs:346-351`, which handles and returns regardless of screen) to `MainWindowViewModel.SettleBills()` (`:5617-5620`) → `OutstandingsViewModel.SettleSelected()` (`src/Apex.Desktop/ViewModels/OutstandingsViewModel.cs:117`). For every spacebar-selected bill it posts a real Receipt or Payment through `BillSettlementService.SettleAndPost` — always the bill's **full** pending amount (`:163`), always through a ledger literally named "Cash" (`:127`), dated at the report's as-of, **with no preview, no confirmation and no undo**. There is no Basis-of-Values feature anywhere: repo-wide grep for "Basis of Values", "ScaleFactor", "Scale Factor" returns zero hits. **[code]** |
| **What Tally does** | Ctrl+B is **Basis of Values** — a right-button report option that re-bases how figures are computed and presented (scale factor, stock valuation method, type of voucher entries). **It writes nothing to the books.** TallyPrime's Bills Outstanding report has no settlement action at all; a bill is settled by keying a Receipt/Payment voucher and choosing Against Reference from the List of Pending Bills, where the operator picks the bill, the amount and the cash/bank ledger. |
| **Citation** | **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` — "Ctrl+B — To view values in different ways in a report — Right button". **[corpus]** SG p.92 §5.5 "Against Reference" — the bill is selected from the list of pending bills and the amount may be broken across several. Corpus silent on any report-side settle action in all ten PDFs. |
| **How it got in** | A plan decision: `plan.md` §5/C-3 specified a "Settle-Bill (Ctrl+B) helper", and `src/Apex.Ledger/Services/BillSettlementService.cs:6-13` carries that name in its own doc comment. **The key was chosen for the mnemonic "Bill"** and never checked against TallyPrime's Ctrl+B. |
| **Fix** | Take settlement off Ctrl+B and off the report. Selecting bills should **open** a Receipt/Payment voucher pre-loaded with those bills as Agst Ref allocations, so the operator confirms date, cash/bank ledger and per-bill amounts and presses Accept — which is what Tally makes them do anyway. Free Ctrl+B for Basis of Values. If the current behaviour must survive an interim: scope the binding to the Outstandings screen, require an explicit confirm, and let the operator choose the ledger and the amount. |

---

### IV-6 · Our costing-method list mixes in a market-valuation method and omits four of Tally's; "Last Sale Cost" values closing stock at selling price
**CRITICAL** · Class **A** · Area **VAL**

> **† 2026-08-15 — verdict STANDS; every citation exact, zero drift.** `StockValuationMethod.cs:13-32` and `:31`,
> `StockValuationService.cs:82-83` and `:85`, `StockItem.cs:43`, `StockValuationMethod.cs:3-8` and `memory.md:362`
> are all unchanged. A repo-wide grep for `MarketValuation|AtZeroCost|MonthlyAverageCost|FifoPerpetual` returns
> **zero hits across zero files** — no split, no added Tally method. Corroborated by `docs/full-clone-census.md`
> **T0-2**, which names this row; and its **T0-3** adds a defect this row did not record — **`StandardCost` is
> offered in the dropdown (`StockItemMasterViewModel.cs:333`) while its input field does not exist anywhere in
> `MainWindow.axaml`**, so choosing it silently falls through to `LastPurchaseRate` (`StockValuationService.cs:86-87`).
> ⚠️ One incidental note: `memory.md:362`'s companion phrase *"hard no-negative-stock guard"* is itself now stale
> (see IV-9).

| | |
|---|---|
| **What the customer experiences** | An operator picks "Last Sale Cost" believing it a Tally option and gets **closing stock valued at his own selling price**. Buy 100 @ ₹100, sell 40 @ ₹150, closing 60: we report Stock-in-Hand ₹9,000; every TallyPrime costing method reports ₹6,000. **Balance Sheet overstated ₹3,000, COGS understated ₹3,000, profit overstated ₹3,000** — unrealised margin booked as profit, which an auditor will reject. Separately, an operator who wants At Zero Cost (the documented way to hold consumables at nil), Monthly Avg. Cost, or annual FIFO/LIFO cannot get them; our FIFO is silently Tally's **FIFO Perpetual**, which values differently the moment the books cross a year end. |
| **What we invented** | `src/Apex.Ledger/Domain/StockValuationMethod.cs:13-32` defines six methods: AverageCost, Fifo, Lifo, StandardCost, LastPurchaseCost, **LastSaleCost**. `src/Apex.Ledger/Services/StockValuationService.cs:85` values an item's **closing stock** at the most recent rated *outward* rate under LastSaleCost. Fifo/Lifo (`:82-83`) replay from company inception — the perpetual variant only. `src/Apex.Ledger/Domain/StockItem.cs:43` has one valuation field and **no market-valuation field at all**. **[code]** |
| **What Tally does** | Splits the two into **separate per-item fields**. Costing Methods: At Zero Cost, Avg. Cost, FIFO, FIFO Perpetual, Last Purchase Cost, LIFO Annual, LIFO Perpetual, Monthly Avg. Cost, Std. Cost. **Market Valuation Methods** (a distinct field, valuing the market column only, never closing cost): At Zero Price, Avg. Price, **Last Sale Price**, Std. Price — "the selling price of the stock item is based on the last price at which the stock item was sold". |
| **Citation** | **[web]** `help.tallysolutions.com/stock-valuation-methods-tallyprime/` — the page is explicitly sectioned "Costing Methods" and "Market Valuation Methods". Corpus **silent**: 0 hits for FIFO, LIFO, "costing method" or "Average Cost" across all ten PDFs. |
| **How it got in** | Never questioned. The enum's own doc (`StockValuationMethod.cs:3-8`) cites only "catalog §9 clone-note; requirements RQ-21", and `memory.md:362` records the deliverable as "six valuation methods" with no source for *which* six. The corpus could not have supplied them — it never names a costing method. |
| **Fix** | Split the field. Keep a `CostingMethod` enum limited to Tally's nine (add `AtZeroCost`, `MonthlyAverageCost`, `FifoPerpetual`, `LifoPerpetual`; rename `Fifo`/`Lifo` to the annual variants) and add a separate `MarketValuationMethod` feeding only a market-value column. **Migrate any item currently on `LastSaleCost` to Last Purchase Cost and flag it** — its Stock-in-Hand figure has been wrong for the life of the book, and the customer's prior-year Balance Sheets are affected. |

---

### IV-7 · ~~Interest "Always" accrues only on the balance that existed at the start of the report window~~ — **FIXED IN CODE**
**CRITICAL** · Class **A** · Area **TAX** · ✅ **CLOSED 2026-08-15**

> **† 2026-08-15 — FIXED by `c408037` (WF-4 + WF-5). Retained as the record of the defect; do not schedule it.**
> The flat-opening-balance code at `InterestCalculation.cs:110-119` **no longer exists**. `AlwaysLines` is now
> `:218-239`, and its principal comes from `:226` `var carriedIn = WalkRunningBalance(company, ledger, windowStart,
> to, out var movements);` with per-movement segmentation at `:227-236` — i.e. exactly the running-balance accrual
> the Fix cell below prescribed. `WalkRunningBalance` (`:258-275`) reads every dated movement inside the window, so
> a zero opening no longer suppresses the row. The uncited comment at `:108-109` is gone; the class doc now states
> the behaviour at `:127-130`: *"**Always** — accrues on the **running balance** … A bill raised inside the window
> therefore starts earning the day after it is raised."* **Other citations in this row drifted too:**
> `PostDueLines` `:134-165` → **`:285`**; the day+1 rule `:145` → **`:298`**.

| | |
|---|---|
| **What the customer experiences** | A debtor with a nil opening balance is invoiced ₹1,00,000 on 10-Apr at 18%. The operator runs Interest Calculation for 01-Apr to 30-Apr. TallyPrime shows interest from 11-Apr to 30-Apr (≈ ₹936.99 on a 365-day basis). **We show no row at all** — the ledger's balance at 01-Apr was zero. The customer bills the party nothing for that month, and re-running the report for May starts the accrual on 01-May, so the twenty days are **lost permanently, not deferred**. Any business whose receivables turn over inside the reporting period under-bills interest on essentially all of it. |
| **What we invented** | `src/Apex.Ledger/Reports/InterestCalculation.cs:110-119` — the principal is `LedgerBalances.Closing(company, ledger, windowStart)` taken once at the window start and **held flat** to the end date; a zero opening returns no line at all (`:117`). Nothing in `AlwaysLines` reads the vouchers inside `[from, to]`. **[code]** |
| **What Tally does** | "Always — this Option calculate interest **from next day of transaction**." Interest under Always runs per transaction from the day after it is entered, so a bill raised inside the reporting period accrues from its own date. |
| **Citation** | **[corpus]** BOOK printed p.118, extracted line 4264; corroborated at line 4272 ("Date of Applicability — this Option calculate interest from next day of transaction"). |
| **How it got in** | Never questioned. The comment at `:108-109` states the design plainly — "the ledger closing balance carried into the window … held flat across the accrual window" — with **no citation**. The Post-Due path was built per-bill (`:134-165`) while the Always path was left on the opening-balance shortcut. |
| **Fix** | Replace the flat opening-balance principal with a **running-balance accrual**: walk the ledger's dated movements inside the window, accrue each segment on the balance in force, and start each transaction's own accrual the day after its date — the same day+1 rule `PostDueLines` already applies at `:145`. The Ctrl+B Outstandings engine already exposes the movement set. |

---

### IV-8 · Interest "Per" divisors are all annualised — and Calendar Month multiplies the month length by twelve
**CRITICAL** · Class **A** · Area **TAX** · **† HALF FIXED — the ×12 defect is LIVE, and held live ON PURPOSE**

> **† 2026-08-15 — the arithmetic defect STANDS and is deliberate; the documentation half is FIXED.**
> **(1) The defect is still live.** `BasisFor` moved from `:213-220` to **`InterestCalculation.cs:473-484`**, and
> **`:481` still reads `InterestPer.CalendarMonth => DateTime.DaysInMonth(segmentStart.Year, segmentStart.Month) * 12,`**
> with `ThirtyDayMonth => 360` at `:480`. Applied at `:410` (was `:183`), basis now resolved **per accrual segment**
> at `:408` — a real improvement from `c408037`, but not this defect. **(2) It is held live by an explicit gate,
> not by neglect.** `InterestCalculation.cs:157-183` is a banner: *"THE DIVISOR VALUES BELOW ARE NOT THIS SLICE'S
> TO CHANGE … the `DaysInMonth × 12` arm — the IV-8b defect — STAYS until T8 lands"*, with the reasoning that the
> two possible answers prescribe **different** replacements and there is no safe interim (hedging toward per-annum
> would move a live figure *further* from the corpus answer, and `DaysInYear` would collapse Calendar Month into
> Calendar Year). **T8 is the blocking user measurement** — ₹44,000 at 10%, Per = 30-Day Month, 30-day window
> (`docs/NEXT_SESSION_KICKOFF.md:221-222`; §6 U-2 below). **(3) One sub-claim is now FALSE:** *"the code contradicts
> its own domain doc"*. `InterestPer.cs` was rewritten — `CalendarMonth` is now at `:48` and its doc at `:39-47`
> states the real behaviour: *"⚠ Resolves to that month's length × 12 today — 336, 348, 360 or 372 — which is not a
> real period length and is the defect IV-8b records."* The paraphrase formerly at `:56-58` no longer exists.

| | |
|---|---|
| **What the customer experiences** | Two separate harms. **(1)** A customer who picks "30-Day Month" and one who picks "365-Day Year" get results 1.4% apart from us, where the corpus's definitions put them roughly **12× apart** — ₹44,000 at 10% over 30 days is ₹366.67 from us against the ₹4,400 the book's own monthly illustration gives. **(2)** Independent of that question, Calendar Month makes the month length the denominator's *multiplicand*: the same ₹1,00,000 at 12% for 28 days accrues **₹1,000.00 in February** (basis 336) and **₹903.23 in January** (basis 372) — a 10.7% swing driven by nothing but which month the bill fell in. No reading of the corpus produces that. |
| **What we invented** | `src/Apex.Ledger/Reports/InterestCalculation.cs:213-220` `BasisFor`: ThirtyDayMonth ⇒ **360**, ThreeSixtyFiveDayYear ⇒ 365, CalendarMonth ⇒ **`DateTime.DaysInMonth(start.Year, start.Month) * 12`**, CalendarYear ⇒ 365/366; applied at `:183` as `principal × (rate/100) × days / basis`. `src/Apex.Ledger/Domain/InterestPer.cs:28` documents CalendarMonth as "Actual days in the calendar month(s) the accrual spans" — **the code contradicts its own domain doc**. **[code]** |
| **What Tally does** | Each Per style names the day count of the period the rate is quoted against: "30 Day Month — … on the basis of 30 Day in one Month"; "365 Day Month — … on the basis of 365 Day in one Year"; "Calendar Month — … Month-wise (28, 29, 30 or 31 Days)"; "Calendar Year — … Year-wise (365 or 366)". The same chapter's simple-interest illustration puts a 10% monthly rate on ₹44,000 at ₹4,400 per month. |
| **Citation** | **[corpus]** BOOK printed p.117 (extracted lines 4237-4248) for the four Per definitions; printed p.116 (lines 4177-4183) for the ₹44,000 @ 10% = ₹4,400 illustration. TallyHelp's interest pages name the four styles but give **no** divisor or worked example — see §6 U-2. |
| **How it got in** | Uncited. The XML doc at `:56-58` states the basis rule as "360 (30-day month), 365 (365-day year), or the actual calendar days in the month/year" — a paraphrase of the corpus that then quietly annualises two of the four. `memory.md`'s Phase-2 interest notes carry no worked figure to check it against. |
| **Fix** | Make `BasisFor` return the **period's own day count** — 30, 365, `DateTime.DaysInMonth(start)`, `DaysInYear(start)` — so the rate is applied per the selected period as the corpus defines it. Before shipping, settle the per-period-vs-per-annum question by running one interest report in a real TallyPrime (Rate 10%, Per = 30-Day Month, 30-day window). **The Calendar-Month × 12 defect needs fixing under either answer.** |

---

### IV-9 · ~~Negative stock ships as an unconditional, unrelaxable block~~ — **FIXED IN CODE (posting half)**
**HIGH** · Class **B** · Area **VAL** · `tally-fidelity-defects.md` D7 · ✅ **CLOSED 2026-08-15 for the posting half**

> **† 2026-08-15 — THIS ROW READ THE OPPOSITE OF THE CODE. FIXED by `a12e651` ("negative stock warns instead of
> blocking", schema **v50**), which landed the same day this register was written and was never folded back in.**
> Everything below describes the pre-fix state. Verified at HEAD `c56e5c3`:
> - **`EnsureNoNegativeStockAnywhere` is deleted.** `InventoryPostingService.cs:348` is now an unrelated Job-Work
>   throw, and the message *"Negative stock is not allowed."* (`:400`) does not exist anywhere in `src/`.
> - The guard became a **detector**: `:176` `public IReadOnlyList<NegativeStockShortfall> DetectNegativeStock()`
>   — never throws, never mutates (doc `:158-176`). All four old call sites (`LedgerService.cs:60`,
>   `InventoryPostingService.cs:81/:101/:117`) are un-blocked; the class doc at `:37-44` now states
>   *"⚠️ Negative stock is NOT blocked (plan.md NS-3; changed at schema v50)."*
> - **The "no relaxation anywhere" grep claim is FALSE.** The flag exists: `Company.cs:268`
>   `public bool WarnOnNegativeStock { get; set; } = true;`, consulted at exactly one place,
>   `InventoryPostingService.cs:184-185` `NegativeStockWarnings()`. DP-7's deferred company flag **shipped**.
> - **The "permanently dead menu item" sub-claim is FALSE.** `ReportsViewModel.BuildNegativeStock` (`:2656`, over
>   `NegativeStock.Build`) can now return rows, because negative stock is reachable.
>
> 🔴 **TWO HALVES REMAIN OPEN, and they are CODE defects, not documentation ones:**
> **(a) The warning never reaches the operator.** `NegativeStockWarnings()` has **zero callers outside
> `Apex.Ledger`** — no `src/Apex.Desktop` file references it, so "warn-only" currently warns nobody.
> **(b) There is no control surface for the flag** — zero `WarnOnNegativeStock` hits in `src/Apex.Desktop`, so a
> company that wants the block back cannot ask for it. Both are tracked as `docs/NEXT_SESSION_KICKOFF.md` queue
> item 4 (W0-5). **The valuation half (NS-8) is untouched and still blocked** — see
> `docs/NEXT_SESSION_KICKOFF.md:192-199`: eight reverted attempts, and the fix is now believed to be *deleting*
> the invented repayment model rather than writing a ninth.

| | |
|---|---|
| **What the customer experiences** | The commonest sequence in Indian trading — **invoice the goods out today, book the supplier's purchase bill when it arrives next week** — is impossible; the operator is stopped at Accept and loses the keyed invoice. Because the guard rescans the entire timeline on every post, one such situation can make the **whole company unpostable** rather than failing one voucher. And our own Negative Stock exception report is structurally incapable of ever showing a row, so it is a permanently dead menu item. |
| **What we invented** | `InventoryPostingService.EnsureNoNegativeStockAnywhere` (`src/Apex.Ledger/Services/InventoryPostingService.cs:348`) rescans every (item, godown, batch) key at every affected date and throws at `:400` — "Negative stock is not allowed." Invoked on every accounting post, inventory post, Cancel and Delete (`LedgerService.cs:60`, `InventoryPostingService.cs:81`, `:101`, `:117`). **No relaxation anywhere**: `grep -rn "AllowNegativeStock\|allowNegative" src/` returns zero hits. The authorising decision did not say that — `docs/phase3-inventory-requirements.md:323` (DP-7) recommended "hard-block **by default** (ER-5); a company flag to 'allow negative stock (warn only)' is **deferred to Phase 6**". Phases 6, 7, 8, 9, 10.5 and 10.9 have all shipped; the flag does not exist. **"Default" quietly became "only".** **[code]** |
| **What Tally does** | Does **not** block. Negative stock is a state it permits; the operator gets an *optional alert*, configured on the voucher screen via F12 as "Warn on negative stock balance", and the invoice or delivery note is still accepted. That TallyPrime also ships a Negative Stock exception report (Display > Exception Reports) is corroborating evidence: it surfaces a state it lets you reach. |
| **Citation** | **[web]** TallyHelp Sales FAQ and Stock Items FAQ; ERP-9-era "Configuring Warning Message for Negative Stock Balance" and "Negative Stock Report". Corpus **silent** — zero hits for "negative stock" / "allow negative" / "negative balance" across all ten PDFs, re-run this session, confirming D7. |
| **How it got in** | A considered plan decision (DP-7) with an honest engineering rationale — but grounded in **our** valuation engine, not in Tally; DP-7 cites no corpus and asks no fidelity question. Scope then crept from "default + a deferred flag" to "unconditional" because the deferred half was never scheduled. The failed valuation attempts recorded in `memory.md` (Phase 10.8) show the engineering rationale was real: **this is a genuine trade-off whose second half was forgotten, not ignorance.** |
| **Fix** | Two separable pieces. **(a)** Keep the block but make it the company/voucher-screen setting DP-7 authorised, defaulting to Tally's shape (allow, warn) once (b) lands. **(b)** The prerequisite is already specified as NS-8 in `memory.md`'s 2026-07-29 entry — per-(item, godown, batch) valuation **and** cost-flowing stock-journal transfers, built together — and must be oracle-gated via `tools/HeadOracle`. **Do not relax the guard before NS-8.** Observe a real TallyPrime with the F12 option both ways before freezing the UX, since the corpus is silent. |

---

### IV-10 · ~~Reorder Status never nets Sales Orders Due~~ — **FIXED IN CODE**
**HIGH** · Class **A** · Area **VAL** · ✅ **CLOSED 2026-08-15**

> **† 2026-08-15 — FIXED by `7e0457b` (WF-7). Retained as the record; do not schedule it.** Verified at HEAD:
> `var shortfall = reorderLevel - closing;` (`:77-78`) is **gone**. `ReorderStatus.cs:120` now reads
> `var nettAvailable = closing + pendingPO - soDue;`, shortfall off Nett Available at `:123`, `max(shortfall, MOQ)`
> at `:128-130`, and Nett Available is emitted as a row column. The `:98` "drop any item whose closing exceeds the
> level" filter is gone (`:69-73`: *"No listing filter beyond the reorder level itself"*), the double-subtraction
> at `:90` is gone (`:125-130`), and **DD-4 is retired by user decision** at `:63-67`; `DD-4` no longer appears in
> `docs/phase6-advanced-inventory-requirements.md` at all.
>
> ⚠️ **The Fix cell below is REFUTED IN BOTH CLAUSES and must not be re-applied** (WF-7 review, recorded at
> `plan.md:1191-1195`). (i) An **unguarded** `max(shortfall, MOQ)` returns the MOQ at *zero* shortfall — the very
> PR-8 behaviour the user retired. (ii) *"list every item with a positive shortfall"* still filters — on shortfall
> instead of closing quantity — and drops the PO-covered row the corpus shows on screen `[CORPUS-BOOK p.164]`.
> What shipped instead: every item that resolves a reorder level is listed, per TallyHelp's *"all stock items in
> the selected group/category display"* with **F8 (Reorder Only)** as the operator's filter.

| | |
|---|---|
| **What the customer experiences** | Reorder level 100 Nos, closing 120, no PO pending, **60 Nos committed on open sales orders**, MOQ 25. TallyPrime: Nett Available 60, Shortfall 40, Order to be Placed 40 — the buyer raises a PO. **We do not list the item at all** (closing 120 > level 100), so nothing is ordered and 60 units go undelivered. A milder case: level 100, closing 90, sales orders due 50, MOQ 25 — Tally orders 60, we order 25, and the customer is 35 units short. This is precisely the out-of-stock scenario the corpus opens the reorder chapter with. |
| **What we invented** | `src/Apex.Ledger/Reports/ReorderStatus.cs:77-78` `var shortfall = reorderLevel - closing;` — **closing stock only**. Pending purchase orders are netted afterwards off the order quantity (`:90`), and sales orders due are fetched (`:75`) but explicitly excluded from the arithmetic: the class doc at `:41-42` states "Sales Orders Due is shown for context but is **not** netted (DD-4)". `:98` then drops any item whose closing quantity exceeds the level, whatever is committed against it. **[code]** |
| **What Tally does** | The Reorder Status report carries a **Nett Available** column = closing stock + purchase orders pending − sales orders due, and "the shortfall in the stock item is calculated based on the nett available stock and reorder Level". "When the Shortfall is less than the Min Order Quantity, the quantity displayed in Min Order Quantity appears under Order to be Placed." |
| **Citation** | **[web]** `help.tallysolutions.com/reorder-stock-items-reorder-status-and-reorder-quantity/` and `…/Reports/Display_Inventory_Reports/Reorder_Status.htm` — the column set: Closing Stock, Purch Orders Pending, Sales Orders Due, Nett Available, Reorder Level, Shortfall, Min Reorder Qty, Order to be Placed. **[corpus]** BOOK pp.158-162 walks the screens but states no formula. |
| **How it got in** | An explicit, uncited design decision recorded as **DD-4** in the class doc (`:41-42`) and in `docs/phase6-advanced-inventory-requirements.md`. Made without the Reorder Status column definitions in hand — the corpus never prints them, and no help page was consulted. |
| **Fix** | `nettAvailable = closing + pendingPO − salesOrdersDue`; `shortfall = max(reorderLevel − nettAvailable, 0)`; list every item with a positive shortfall regardless of raw closing quantity; `Order to be Placed = max(shortfall, MOQ)`. **Drop the separate pendingPO subtraction at `:90`** — it double-counts once purchase orders are inside Nett Available. Add a Nett Available column so the row reconciles on screen. (The max/MOQ half rests on an inference — see §6 U-6.) |

---

### IV-11 · The no-rate-inward "best-available-cost chain" is our own invention, and it costs a FIFO/LIFO lot at the weighted average
**HIGH** · Class **A** · Area **VAL**

> **† 2026-08-15 — verdict STANDS; every citation exact, zero drift.** `StockValuationService.cs:496-502`,
> `:18-29`, `:400-407`, `:415-423`, `:86-87` and — the one that proves the cross-method contamination without any
> Tally fact — **`:286`** `var unit = e.Rate ?? cost.NoRateInwardCost(RunningAverage(runningQty, runningCost));`
> are all unchanged. `AtZeroCost` still does not exist anywhere in `src/`.

| | |
|---|---|
| **What the customer experiences** | A FIFO item with In 100 @ ₹100 and In 100 @ ₹200, then a Stock-Journal destination of 50 units carrying no rate: we push a FIFO layer of 50 at the running average ₹150, **adding ₹7,500 to Stock-in-Hand**. A Stock Journal does not post to accounts, so that ₹7,500 arrives on the Balance Sheet **with no counter-entry**. The customer sees Stock-in-Hand rise ₹7,500 with no purchase behind it, and the item's stated method (FIFO) did not produce the number. Under Last Purchase Cost the same chain means an item never purchased at a rate is valued at an averaged figure rather than the ₹0 its stated method implies. |
| **What we invented** | `src/Apex.Ledger/Services/StockValuationService.cs:496-502` — `NoRateInwardCost` = running average → `StockItem.StandardCost` → last rated inward rate → 0, presented in the class doc (`:18-29`) as a four-step contract. Two further chains hang off it: `LastPurchaseRate` (`:400-407`) and `LastSaleRate` (`:415-423`); and the StandardCost **method itself** falls back to the last purchase rate when no standard cost is set (`:86-87`). Provable regardless of Tally: **`BuildLayers` at `:286` applies the running-average-headed chain when pushing a layer for a FIFO or LIFO item**, so one item's layer stack mixes two costing models. **[code]** |
| **What Tally does** | Publishes each costing method as a flat definition with **no fallback** — Last Purchase Cost: "The inventory will be valued based on the last purchase cost of that item"; Standard Cost: "Once you define the Standard Cost, the rate is applicable for inventories irrespective of price" — and publishes a separate, explicit method, **At Zero Cost**: "The value of stock items will always be zero, irrespective of the cost incurred." No cascade between methods is documented anywhere. |
| **Citation** | **[web]** `help.tallysolutions.com/stock-valuation-methods-tallyprime/` — the nine costing-method definitions including At Zero Cost. Corpus silent on costing entirely. **Note the split**: what Tally does with a *rateless inward* is UNVERIFIED (§6 U-3); the **cross-method contamination is not** — it is proven from our own code. |
| **How it got in** | An uncited comment that became the specification. The four-step chain is asserted in the class XML doc (`:18-29`) justified as "never a crash, never a non-paisa value, never a silent ₹0 for real units when any cost signal exists" — an **engineering preference, not a Tally rule**, with no source for any of it. |
| **Fix** | Add Tally's **At Zero Cost** so "value at nothing" is a choice the operator makes rather than a fallback we invent, then collapse the chains: a rateless inward on a FIFO/LIFO item should take that method's own basis (the last rated inward for a layer method), not the running average; Last Purchase Cost with no rated purchase should yield the published answer. Settle the Tally-side rateless behaviour by observation before choosing the replacement, and **correct the class doc either way** — as written it specifies behaviour no source supports. |

---

### IV-12 · A top-level (Primary) account group cannot be created — and the comment that forbids it is uncited and wrong
**HIGH** · Class **B** · Area **MST** · relates to `tally-fidelity-defects.md` D17

> **† 2026-08-15 — verdict STANDS; one citation off by one.** `GroupService.cs:13-17`, `:96`,
> `AccountGroupMasterViewModel.cs:26`, `:156`, `:226` and all five sibling-master "Primary" pickers are
> **unchanged and exact**. **Corrected:** the throw is at **`GroupService.cs:53-55`**, not `:52-54`
> (`:53` `if (parentId is not { } pid)`, `:55` the message); the nature is derived at `:61`.

| | |
|---|---|
| **What the customer experiences** | An accountant **cannot create a top-level head of account**. Every custom group must hang under one of the 28 seeded groups, so a business that wants its own primary head — the standard way to add a new Balance-Sheet or P&L head in Tally — must misfile it under an existing head, where it inherits that head's nature and prints on that side of the Balance Sheet forever. It also blocks importing any Tally book containing a user-created primary group. |
| **What we invented** | `src/Apex.Ledger/Services/GroupService.cs:13-17` states as fact that "a **parent (Under) is required and must exist**" and that "the nature is DERIVED from the parent's primary ancestor, **never accepted from the caller** … the user picks only the parent". **No citation for either clause.** Enforced literally: `:52-54` throws "A parent group (Under) is required" when `parentId` is null; `AccountGroupMasterViewModel.cs:26` and `:156` repeat the claim, and the Under picker is built only from existing groups. **The codebase's own counter-evidence:** every other master ships a Primary option — `GodownMasterViewModel.cs:53`, `StockCategoryMasterViewModel.cs:51`, `CostCentreMasterViewModel.cs:55`, `EmployeeGroupMasterViewModel.cs:54`, `AttendanceTypeMasterViewModel.cs:80` — and `AccountGroupMasterViewModel.cs:226` even *renders* the word "Primary" for a top-level group it cannot create. **[code]** |
| **What Tally does** | "Under: Select **Primary** or any other predefined groups, based on your requirement", and "**Nature of Group**: Appears only if the group is created under **Primary**" — the user then chooses Assets / Liabilities / Expenses / Income. So Tally (a) permits a parentless group and (b) **does not derive** the nature in exactly the case our comment says it does. |
| **Citation** | **[web]** `help.tallysolutions.com/tally-prime/masters-tally/groups-in-tallyprime/`, fields "Under" and "Nature of Group". **[corpus]** SG p.67 (extracted line 2085) "One can also create groups under the Primary group category, if required"; the same Primary-or-sub-group wording for the sibling masters at SG p.107 (Stock Group), p.109 (Stock Category), p.99 (Cost Centre), p.111 (Godown). |
| **How it got in** | An uncited comment. The claim was written into the service's XML doc as a bullet of "the same discipline the other masters ship with", implemented as a throw, then copied verbatim into the ViewModel doc twice. **Nothing else supports it — and the rest of the codebase does the opposite for every sibling master.** |
| **Fix** | Add a "Primary" sentinel to the Under picker; allow `CreateGroup(name, parentId: null, …)`; when the parent is null, **capture** Nature of Group from the user (Assets/Liabilities/Income/Expenses) instead of deriving it, keeping `ValidateNatureAgainstParent` for the parented case only. Replace the comment at `:13-17` with the two citations above, scoping the derive-the-nature rule to non-Primary groups. |

---

### IV-13 · There is no Voucher No. field: the number is an int, the screen renders it read-only, and Manual/None are unreachable
**HIGH** · Class **B** · Area **ENT** · `tally-fidelity-defects.md` D9 · `tally-gap-decisions.md` D23

> **† 2026-08-15 — verdict STANDS; heavy citation drift.** `Voucher.cs:20`, `VoucherNumberFormatter.cs:22`,
> `VoucherNumberingConfigViewModel.cs:114-115`, `MainWindow.axaml:2056` and `:3544` are unchanged, and no
> voucher-number `TextBox` exists anywhere. **Corrected:** `VoucherEntryViewModel.cs:990` → **`:984`** ·
> `MainWindow.axaml:3894` → **`:3882`** · `:4104` → **`:4107`** · `LedgerService.cs:48` → **`:51-52`** ·
> `InventoryPostingService.cs:53` → **`:91-92`** · `plan.md:611-613` → **`plan.md:677-679`** ·
> `docs/tally-gap-decisions.md:563` → D23's heading is now **`:560`**.

| | |
|---|---|
| **What the customer experiences** | An operator migrating a live book **cannot key the invoice number their existing series uses**. They cannot enter the pre-printed number on a manual bill book, cannot correct a wrong number, and cannot override one voucher without changing the whole type. A firm whose numbers look like "APX/25-26/0147" can only approximate it with a type-wide prefix, which applies to every voucher of that type, cannot vary per voucher, and still cannot express a non-numeric core. On the fidelity target this is **one field and one keystroke**. |
| **What we invented** | Three choices compound. **(1)** `Voucher.Number` is an `int` (`src/Apex.Ledger/Domain/Voucher.cs:20`) and the human number is an unpersisted projection (`VoucherNumberFormatter.Render`, `src/Apex.Ledger/Services/VoucherNumberFormatter.cs:22`). **(2)** The entry screen exposes it as a **read-only preview**: `[ObservableProperty] private int _voucherNumber` (`src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:990`), bound in a non-editable `<Run Text="{Binding FormattedVoucherNumber}"/>` (`MainWindow.axaml:2056`, `:3544`, `:3879`, `:4104`). **There is no TextBox for a voucher number anywhere.** **(3)** The method cannot be changed: `MethodDisplay` is a string getter marked "DISPLAY-ONLY this slice … editing it is deferred (S5+)" (`VoucherNumberingConfigViewModel.cs:114-115`), no code in `src/Apex.Desktop/` assigns `Numbering`, and there is **no Voucher Type master at all** (D9). The engines branch only on `Automatic` (`LedgerService.cs:48`, `InventoryPostingService.cs:53`), so `Manual` and `None` exist in the enum and the schema but are unreachable. **[code]** |
| **What Tally does** | Voucher No. is an ordinary **editable** header field. Under Manual the operator enters each number individually; under Automatic (Manual Override) an auto-assigned number can be typed over — TallyHelp's worked example is auto-numbered 10 overridden to 21, after which the next prefills as 22 — with an optional prevent-duplicate check. Real Indian voucher numbers are alphanumeric. |
| **Citation** | **[web]** `help.tallysolutions.com/use-voucher-numbering-methods/` — Manual, Automatic (Manual Override) with the 10→21→22 example, duplicate prevention. **[corpus]** SG Illustration 1 (extracted lines 1065-1069) — "Voucher No. 3", "Voucher No. M/15/7", "Bill No. 2015/F/3". **Honest scope:** the official page does not state in words whether the Manual field accepts alphanumerics, so the int-vs-string half is corpus-*implied* (§6 U-9). The read-only half is proven from our own code and needs nothing further. |
| **How it got in** | Never questioned **as a set**. Each piece was individually reasonable — the int is the sequence seed and the identity (`plan.md:611-613`), and method-editing was explicitly "deferred (S5+)" in the config VM's own comment — but S5 shipped the counterparty reference field instead. `docs/tally-gap-decisions.md:563` (D23) asks only about *adding* methods; nobody asked whether the three we have are **reachable**. |
| **Fix** | Sequence: **(a)** make Voucher No. an editable header field bound to a string, honoured when the type's method is Manual or Automatic-with-override; **(b)** widen the persisted number to carry an operator-supplied string alongside the int seed (the seed can stay for `max+1`); **(c)** make the method selectable — either in the F12 numbering config (lifting the S5 deferral, a one-property change) or as part of the Voucher Type master D10 proposes. Until (c) lands, state plainly that Apex supports **one** numbering method, not three. |

---

### IV-14 · Intra-state CGST and SGST are deliberately allowed to differ by one paisa
**HIGH** · Class **A** · Area **TAX**

> **† 2026-08-15 — verdict STANDS.** `GstService.cs:526-527` and `:518` are byte-for-byte unchanged;
> `memory.md:414-417` exact. **Corrected:** the per-rate-group re-split `:671` → **`:702`** (accumulated at
> `:713-714`; `:671` is now `var cgst = 0m; var sgst = 0m; var igst = 0m;`) · the "differ by exactly 1 paisa"
> doc sentence `:510-513` → **`:511-513`** · the original Tally-shaped code in `memory.md:381-382` is at **`:382`**.

| | |
|---|---|
| **What the customer experiences** | Taxable ₹1,000.05 at 18% intra-state: we post **CGST ₹90.00 and SGST ₹90.01**. TallyPrime posts ₹90.00 and ₹90.00. The printed invoice shows two "9%" lines with **different amounts** — the first thing any Indian accountant or GST officer reads as an error — and the GSTR-1 rate-wise table inherits the asymmetry. On an e-invoice the payload trips **IRP validation 2227**. It is one paisa, but it is on the face of the document. |
| **What we invented** | `src/Apex.Ledger/Services/GstService.cs:526-527` — `var cgst = new Money(total.Amount / 2m).RoundToPaisa(); var sgst = new Money(total.Amount - cgst.Amount);` where `total` is the single rounded IGST-equivalent computed at `:518`. The XML doc at `:510-513` states the consequence outright: "on an odd total they legitimately differ by exactly 1 paisa (SGST carries the remainder)". The split is re-run per rate group at `:671`ff, so it reaches the **posted tax lines**, not just the display. **[code]** |
| **What Tally does** | Computes each head on the taxable value at its own half rate, so CGST and SGST are **equal**. The rule is hard enough that the e-invoice portal validates it: IRP error **2227**, "SGST and CGST Amounts should be equal for HSN – {0} and Sl. No {1}". |
| **Citation** | **[web]** `einvoice6.gst.gov.in/content/validation-rules/` (IRP validation error 2227). The licensed corpus prints only round-rupee GST illustrations and cannot settle a paisa-level rule — recorded honestly as web-only on the Tally side. |
| **How it got in** | **An orchestrator/review ruling that overturned the correct code.** `memory.md:414-417` records that an A10 adversarial review found the halves being rounded independently, called it CRITICAL, and replaced it with compute-total-then-split to satisfy a **self-invented invariant**, "CGST+SGST == IGST == round(V×rate) by construction". `memory.md:381-382` shows the original code was the Tally-shaped one. No Tally source was consulted before overturning it. |
| **Fix** | Restore per-head rounding — `CGST = SGST = round_paisa(taxable × halfBp / 10000)` — and **drop the CGST+SGST == IGST invariant**, which is not a rule Tally or the law asserts (an intra-state and an inter-state supply of the same value are separate documents and need not foot to each other). Keep the per-rate-group aggregation; only the split changes. Re-run the exhaustive paise sweep with the new expectation. |

---

### IV-15 · The e-invoice document-number guard is not FY-scoped — and the FY restart was deferred on a circular rationale
**HIGH** · Class **B** · Area **TAX** · `tally-gap-decisions.md` D22

> **† 2026-08-15 — verdict STANDS; the guard is still unscoped.** `Company.HasEInvoiceDocumentNumber` is now at
> **`Company.cs:681-682`** (was `:660`) and still compares against **every** e-invoice record ever created, with
> no financial-year term; the refusal message still throws at `EInvoiceService.cs:162-164`. **Corrected:**
> `EInvoiceService.cs:115` → **`:138-141`** (`DocumentNumberOf` … `var rendered = company.FormatVoucherNumber(voucher);`
> — the "rendered string, not the bare int" proof) · `plan.md:618` → **`plan.md:683-685`** ·
> `docs/tally-gap-decisions.md:541` → **`:550`** (D22's heading is now `:543`).

| | |
|---|---|
| **What the customer experiences** | Most Indian businesses restart invoice numbering on 1 April; **they cannot**. Their Apex sales series runs 1..4,812 across five years while their previous books, their GSTR-1 and their accountant all expect 1..900 per year. Worse, **the guard bites even without the feature**: a company that legitimately reuses "INV/001" in a new financial year — which Rule 46(b) permits and the IRP accepts — is refused by our own e-invoice preparation with "Document number 'INV/001' is already used by an e-invoice record and cannot be reused", with no way past it. **That is our app blocking a statutorily compliant document.** |
| **What we invented** | `plan.md:618` defers the FY restart on the rationale that "the bare `Voucher.Number` **IS** the statutory document number … so a naive FY reset makes the `int` collide across years and **hard-blocks** the new-FY `#1` e-invoice." **The collision is manufactured by our own code**: `Company.HasEInvoiceDocumentNumber` (`src/Apex.Ledger/Domain/Company.cs:660`) compares a candidate against **every e-invoice record ever created**, with no FY scoping — `_eInvoiceRecords.Any(r => string.Equals(r.DocumentNumberUpper, documentNumber, OrdinalIgnoreCase))`. The premise is also stale on its own branch: since Phase 10.7 S2 the number handed to the IRP is the **rendered string**, not the bare int (`src/Apex.Ledger/Services/EInvoiceService.cs:115`). **[code]** |
| **What Tally does** | Ships **Restart Numbering** as a first-class part of voucher-type numbering — Starting Number, Periodicity (Daily/Weekly/Monthly/Yearly/Never) and Applicable From, with more than one restart date permitted. The statutory scheme it serves is FY-scoped by construction: CGST Rule 46(b) requires a serial number unique **for a financial year**, and the NIC e-invoice system computes the IRN as a hash of supplier GSTIN + **Year** + document type + document number, checking only that the "same invoice from same supplier pertaining to same financial year" is not re-uploaded. **Restarting at 1 on 1 April is what the IRP expects, not something it rejects.** |
| **Citation** | **[web]** `help.tallysolutions.com/use-voucher-numbering-methods/` and `…/Restart_Voucher_Numbering.htm`. **[web]** `einvoice1.gst.gov.in/Documents/GST_eInvoiceSystemDetailedOverview.pdf` and `/Others/Faqs` — IRN hash inputs and the per-FY duplicate check. Corpus screenshot-only on this feature. |
| **How it got in** | An orchestrator/user ruling at `plan.md:618` whose rationale nobody re-checked against the IRP's actual uniqueness rule. `docs/tally-gap-decisions.md:541` (D22) then **repeated it approvingly** — "the original reason is a good one" — inheriting the premise rather than testing it. Neither document cites the IRN hash inputs or Rule 46(b). This is the same shape as the AverageCost tautology `memory.md` already convicts itself of: the deferral's evidence is our own echo. |
| **Fix** | **First fix the guard — it is a live defect independently of the feature:** scope `Company.HasEInvoiceDocumentNumber` by financial year, keyed on the source voucher's date, mirroring the IRP. Once done, the stated obstacle to FY restart evaporates and **D22 should be re-put to the user as build-it, not as A-stay-deferred**. When built, implement Tally's actual shape — restart rows `{Applicable From, Starting Number, Periodicity}` feeding a period-scoped `NextNumber` — **not** merely dated prefix rows: a rolling prefix changes the label while the sequence keeps climbing, and that is not the Tally feature. |

---

### IV-16 · Alt+X means the opposite of Tally's Alt+X: it destroys unsaved keying instead of cancelling a saved voucher
**HIGH** · Class **C** · Area **ENT**

> **† 2026-08-15 — verdict STANDS.** `MainWindow.axaml.cs:350` still routes Alt+X to `vm.CancelVoucher()`, and
> `LedgerService.Cancel` still has no Desktop caller (every `.Cancel(` in `MainWindowViewModel.cs:4835-4845` is a
> per-screen VM Cancel). `LedgerService.cs:8` unchanged. **Corrected:** `MainWindow.axaml.cs:309-314` →
> **`:349-354`** · `LedgerService.cs:95-99` → **`:87-97`** · the Escape pop `:847-849` → **`:931`** ·
> `MainWindowViewModel.cs:4830-4831` → **`:4831-4832`** · `plan.md:267` → **`plan.md:289`**.
> **New since the register:** `plan.md:1414-1415` now carries a *planned* fix (VL-3) naming IV-16 — planned, unbuilt.

| | |
|---|---|
| **What the customer experiences** | Two harms from one key. The operator's Tally reflex for "void this voucher but keep the number" **does nothing** on a saved voucher, so the audit-safe alternative to deletion — the one an auditor expects for a spoiled invoice — is unreachable. And if they press Alt+X out of habit while a screen is open, **an entry they have been keying for twenty minutes vanishes with no prompt**. |
| **What we invented** | Alt+X is bound app-wide (`src/Apex.Desktop/Views/MainWindow.axaml.cs:309-314`) to `MainWindowViewModel.CancelVoucher()` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:4830-4831`, "cancel the in-progress voucher (no save) and pop its page column"). It abandons whatever is being keyed on ~40 screens, with no confirmation. The engine's real Alt+X — `LedgerService.Cancel(Guid)` (`src/Apex.Ledger/Services/LedgerService.cs:95-99`, "mark cancelled; keeps the number in sequence, zero effect on balances") — is **never called from the Desktop project**. **[code]** |
| **What Tally does** | Alt+X cancels a **saved** voucher: the number stays in sequence, the entry is nulled, and the cancelled voucher remains visible in the Day Book. Abandoning an in-progress entry is **Escape**. |
| **Citation** | **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` — "Alt+X — To cancel a voucher — Bottom bar". **[corpus]** BOOK p.433 shortcut table — Alt+X against "To cancel a voucher" / "To cancel a voucher from a report"; SHORTKEY item 55 (corroboration only). `plan.md:267` states the TallyPrime semantics **correctly**. |
| **How it got in** | Never questioned; the word "cancel" was read as "cancel this dialog" rather than as Tally's transaction verb. The engine author knew the difference — `LedgerService.cs:8` distinguishes "Cancel (Alt+X, keep number)" from "Delete (Alt+D, may gap numbering)" — and the UI binding contradicted it. |
| **Fix** | Move abandon-entry to Escape (which already pops the column at `MainWindow.axaml.cs:847-849`) and re-point Alt+X at `LedgerService.Cancel` on the selected voucher in the Day Book / register drill, rendering cancelled vouchers in the register the way Tally does rather than hiding them. Ships naturally with IV-3/IV-4. |

---

### IV-17 · The lenient date parser accepts 2-digit years on .NET's 1930–2029 pivot
**HIGH** · Class **A** · Area **ENT**

> **† 2026-08-15 — verdict STANDS.** `ApexDate.cs:36`, `:40` and `:75` are unchanged, and the file still contains
> no `TwoDigitYearMax` or `GregorianCalendar`; `BatchMasterViewModel.cs:184-191` and `:266` unchanged.
> **Corrected:** the test citation `ApexDateTests.cs:95` → the 2-digit cases are the `InlineData` at **`:91-92`**
> (`"03-Apr-24"`, `"03/04/24"`) — still the only two, still inside the pivot · `docs/ca-audit-backlog.md:1281`
> → **`:1280`**.

| | |
|---|---|
| **What the customer experiences** | Batch expiry is the live one. `BatchMasterViewModel` parses the expiry field through this ladder (`src/Apex.Desktop/ViewModels/BatchMasterViewModel.cs:184-191` → `:266` → `ApexDate.TryParse`), and pharma/FMCG expiry dates are routinely 2030 and later. An operator typing **"31/12/30" gets a batch that silently expired in 1930**: it is flagged expired on every report, **FEFO picks it first**, and nothing on screen says the year was reinterpreted — the field echoes back "31-Dec-1930" in a format the operator did not use and may not re-read. The same applies to any post-2029 due date or applicable-from date. |
| **What we invented** | `ApexDate.Ladder` includes `"dd-MMM-yy"`, `"d-MMM-yy"` (`src/Apex.Desktop/Services/ApexDate.cs:36`) and `"dd-MM-yy"`, `"d-M-yy"` (`:40`), parsed via `DateOnly.TryParseExact(..., CultureInfo.InvariantCulture, ...)` at `:75`. InvariantCulture's Gregorian calendar carries `TwoDigitYearMax = 2029`, so any 2-digit year 30–99 resolves into the **1900s**. `tests/Apex.Desktop.Tests/ApexDateTests.cs` exercises 2-digit years only at "24" (`:95`) — inside the pivot, where the bug cannot show. **[code]** |
| **What Tally does** | **UNVERIFIED** (§6 U-11). Neither the corpus nor the official pages state TallyPrime's pivot. What the corpus *does* establish is that Tally renders 2-digit years and accepts day-first all-numeric input, so 2-digit years are unquestionably in the accepted-input set — which is precisely why the pivot matters. **Note this row is a defect on any pivot Tally could plausibly have**: 1930 is not a date any user of an Indian accounting package means. |
| **Citation** | **[corpus]** rendered 2-digit dates at SG lines 5008, 5725 and GSTN line 962; day-first numeric input at BOOK lines 13078, 4727-4728, 6240. In-repo: `docs/ca-audit-backlog.md:1279-1283`. Framework behaviour: .NET `Calendar.TwoDigitYearMax` = 2029 for the invariant Gregorian calendar. |
| **How it got in** | **An explicit "answer this before specifying" item the slice shipped past.** `docs/ca-audit-backlog.md:1281` lists "Tally's 2-digit-year pivot rule (does '23' mean 2023?)" under "STILL UNVERIFIED (… **must be answered before 4b is specified**, because the accepted-input set IS the requirement)" and calls it "a correctness trap in an accounting app — a wrong pivot silently posts to the wrong FY". WI-5 shipped anyway (`0dad03f`). The ladder was designed around the day-first-vs-month-first bug (which it fixes well) and the **year** question travelled with it unexamined. |
| **Fix** | Two lines. Either **drop `yy` from the ladder** — the canonical echo is 4-digit, so nothing in the app produces a 2-digit year to round-trip — or parse with an explicit `GregorianCalendar` whose `TwoDigitYearMax` is set forward (e.g. current year + 20). Then pin it: theory rows asserting "31/12/30" → 2030 and "01/04/99" → the intended century, **which today would fail**. Separately ask A14 to settle Tally's own pivot so the choice is fidelity-grounded rather than merely safe. |

---

### IV-18 · Alt+G "Go To" does not exist — the corpus offers it as the second route to nearly every screen
**HIGH** · Class **B** · Area **ENT**

> **† 2026-08-15 — verdict STANDS, independently corroborated.** "Alt+G", "Ctrl+G" and "Go To" still return
> **zero source hits** across `src/`, and the dispatcher rooted at `MainWindow.axaml.cs:182` has no arm for
> either. `docs/full-clone-census.md` T2-7 measured the same thing by a different route: *"Zero `Key.G`
> occurrences in `src/Apex.Desktop`"*. `docs/voucher-entry-specification.md:83` unchanged.
> **Corrected:** `plan.md:94-95` → **`plan.md:116`** (NFR-2's own line is `:115`) · `plan.md:381` → **`:445`**.

| | |
|---|---|
| **What the customer experiences** | With ~160 screens behind a cascade, the single habit that makes an experienced operator fast — **Alt+G, type "gstr", Enter** — is gone; they must instead remember which of eight sibling Reports sub-groups holds a screen and arrow through columns to reach it. It is also **the most-cited navigation instruction in the training material their staff learn from**, so every course exercise fails at step one. |
| **What we invented** | "Alt+G", "Ctrl+G" and "Go To" appear **zero times** anywhere in `src/`. The key dispatcher (`src/Apex.Desktop/Views/MainWindow.axaml.cs:182` onward, ~700 lines of first-match-wins arms) has no arm for either key. Every screen is reachable only by walking the Miller-column cascade or by that screen's own accelerator. **[code]** |
| **What Tally does** | Alt+G / Ctrl+G opens **Go To**: type a few characters of any report, master or voucher and jump straight to it from anywhere, without leaving the current screen. It is the primary navigation idiom of TallyPrime and the reason the product advertises "multi-tasking". |
| **Citation** | **[corpus]** BOOK p.430, the **first row** of its shortcut table — "Alt+G/Ctrl+G — To primarily open & switch to a different report, and create masters and vouchers in the flow of work — Across Tally Prime"; used as the literal step for GSTR-3B (lines 7545, 7684), GSTR-2 (7866), Form 26Q (10268), TDS Challan (10324), Form 27EQ (10829), TCS Challan (10882). SG gives it as the alternate route on essentially every walkthrough (pp.66, 67, 73). **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/`. |
| **How it got in** | **Specified and never built.** `plan.md` NFR-2 (`:94-95`) requires "single-window navigation (GOT hub, Alt+G Go To, Ctrl+G Switch To)" and `plan.md:381` lists "Go To multi-tasking" as a baseline enrichment; `docs/voucher-entry-specification.md:83` then quietly downgraded it to "Alt+G equivalent not surveyed here", and no phase picked it up. |
| **Fix** | Add a Go To overlay on Alt+G/Ctrl+G: a prefix-filtered list over the **same route table** that `HandleMenuLetter` / `SelectRootItem` / `BuildRootColumn` already drive, opening the chosen destination as a cascade column. This is a search over an index the app already owns, not new navigation machinery. |

---

### IV-19 · Drill-down stops at two screens; roughly fifty report screens are dead-end tables
**HIGH** · Class **B** · Area **RPT** · **† the number in this title is WRONG — it is 71 of 77**

> **† 2026-08-15 — verdict STANDS, the COUNT is corrected UPWARD.** Code re-read: `DrillSelectedRow()`
> (`MainWindowViewModel.cs:2083-2100`) is **byte-for-byte unchanged** — `Screen.LedgerVouchers` at `:2086`,
> `Screen.Report` at `:2093`, `return false` for everything else at `:2099`. Only two view models still carry any
> Drill member. **But "~50" understates the defect by ~40%.** `docs/full-clone-census.md` T1-9 measured it:
> **"71 of 77 report surfaces are dead ends. 6 of 45 `ReportKind` values drill; 0 of 32 dedicated report Screens
> drill"** (`ReportsViewModel.cs:1093-1120`; `MainWindowViewModel.cs:2083-2100`), and it names this row explicitly:
> *"This corrects IV-19, which says '~50' and counts only the separate Screens."* The census also files the
> discrepancy in its Tier 3 REGISTER AND PLAN FALSEHOODS table (`full-clone-census.md:142`). **The census figure is
> the one to plan against** — it counted both `ReportKind` values and dedicated Screens; this row counted only the
> latter. Two related census rows bound the same surface: **T1-10**, 32 of 77 surfaces cannot be printed at all,
> and **S4** in the census sequencing proposal, which says the drill/print/export back-fill is **one** refactor and
> must precede the ~14 new report families or they will be born un-drillable.

| | |
|---|---|
| **What the customer experiences** | On Bills Receivable the operator sees an overdue bill and **cannot open it**. On the GST return screens they see a B2B total and **cannot see which invoices make it up** — which is exactly the check a filer performs before filing. Reconciling any figure means abandoning the report, going to the Day Book and filtering by hand, which is the work Tally's drill exists to abolish. |
| **What we invented** | `MainWindowViewModel.DrillSelectedRow()` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:2083-2100`) drills only `Screen.LedgerVouchers` and `Screen.Report`. Every other report is its own `Screen` the drill never handles — Outstandings, Cost reports, Budget Variance, Interest, Forex, Bank Reconciliation, CMP-08/GSTR-4/GSTR-9/GSTR-9C, Electronic Ledgers, ITC Set-Off/Reversal/Gate, GSTR-2B recon, QRMP, GST Amendments, e-Invoice/e-Way status, Challan Reconciliation, Form 26Q/27EQ/16/16A/27A/27D, PF ECR, ESI, PT Register, Gratuity, Bonus. Only two view models in the whole Desktop project contain any Drill member. **[code]** |
| **What Tally does** | **Every figure in every report drills.** Enter opens the next level down on a separate screen (Shift+Enter expands in place) and the chain continues all the way to the voucher. |
| **Citation** | **[web]** `help.tallysolutions.com/working-with-reports/` — drill-down with Enter / Shift+Enter as a general report capability. **[corpus]** GSTN extracted lines 2060-2068 (the three cost-centre reports), 2922 ("Press enter on B2B Invoices, you can drill down up to transaction details"), 2987 (B2C Small), 3085 (Exports Invoices). |
| **How it got in** | RQ-7 scoped drill to the accounting reports and the ledger book. Each later report family (Phase 7 TDS/TCS, Phase 8 payroll, Phase 9 GST) was built as its own `Screen` with its own view model and **inherited nothing from that contract**. |
| **Fix** | Lift the drill contract into a **shared report base** — a row exposing a drill key plus a handler the shell can call — so a newly added report screen gets Enter-drill by construction. Back-fill the highest-traffic surfaces first: Outstandings row → voucher, GST return section → invoice list → voucher, cost reports → ledger vouchers. |

---

### IV-20 · ~~Voucher entry mode: AsVoucher is the wrong default for Payment/Receipt/Contra~~, and the mode is never remembered
**MEDIUM** · Class **C** · Area **ENT** · `tally-fidelity-defects.md` D1 · *(merged: `iv-claims` #2 + #3)*
**† HALF (a) FIXED · half (b) STILL OPEN**

> **† 2026-08-15 — half (a) READ THE OPPOSITE OF THE CODE and is FIXED; half (b) stands verbatim.**
> **(a) FIXED by `f277318` ("Payment, Receipt and Contra open in Single Entry, as Tally ships them").** The quoted
> phrase *"AsVoucher, **the default** and the only mode on every non-Purchase/Sales type"* **no longer exists in
> the file.** The literal `_mode = VoucherEntryMode.AsVoucher;` moved `:96` → **`:100`** and is **no longer the
> opening mode**: its own doc at `:96-98` now says *"**This field initialiser is NOT the opening mode.** … The mode
> a screen actually OPENS in is seeded per voucher type — see `SeedOpeningMode`."* `SeedOpeningMode()` at
> **`:141-144`** is `if (CanBeSingleEntry) Mode = VoucherEntryMode.SingleEntry;`, called from the constructor at
> **`:1194`**. Its doc `:107-128` carries the GSTN citation this row asked for, **including the one apparent
> counter-example (GSTN line 330) recorded rather than buried** — which is the standard §5.3 asks for.
> **(b) STILL OPEN, and the exact wording below is still accurate.** `:93-94` *"the mode is transient screen state,
> **never persisted**"* is **unchanged**; `VoucherEntryMode` is declared in
> `src/Apex.Desktop/ViewModels/AccountingInvoiceLineViewModel.cs:21` and has **zero occurrences anywhere in
> `src/Apex.Ledger/`** — no company, voucher-type or settings field holds it, so `SeedOpeningMode` can only ever
> apply the hardcoded per-type rule, never a remembered one. The row's own analysis was right that these are two
> different concerns; only the first was built.
> **Citations corrected:** `:91` → the quoted text is **gone** · `:96` → **`:100`** · `:1176` `CanBeSingleEntry` →
> **`:1231`** · `:1184` `IsSingleEntry` → **`:1239`** · `:132` `ShowPlainDrCrGrid` → **`:180`**.
> `AccountingInvoiceLineViewModel.cs:12-13`, `:15` and `:23` are **unchanged and still uncited** — the three
> "(the default)" assertions this row objected to survive in the enum's own doc and should be reworded there.

| | |
|---|---|
| **What the customer experiences** | The **three highest-volume vouchers in the product open in the wrong layout every single time.** The operator presses Ctrl+H before every payment, receipt and contra, and on the grid they land on they must make two Dr/Cr side decisions Tally never asks for — where getting the side backwards **silently reverses** a cash or bank entry rather than failing. And the app forgets the choice the moment the screen closes, so on a 100-voucher day that is 100 extra keystrokes and 100 chances to start typing into the wrong grid. |
| **What we invented** | **(a)** `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:146` — "the classic Dr/Cr grid (…AsVoucher, **the default** and the only mode on every non-Purchase/Sales type)", enforced at `:96` by the unconditional literal `private VoucherEntryMode _mode = VoucherEntryMode.AsVoucher;`. Repeated uncited at `AccountingInvoiceLineViewModel.cs:15` and `:23`. Single Entry exists and works (`:1176` `CanBeSingleEntry`, `:1184` `IsSingleEntry`) but is reachable only by Ctrl+H. **(b)** `:93-94` — "the mode is transient screen state, **never persisted**", repeated at `AccountingInvoiceLineViewModel.cs:12-13`. `_mode` is an instance field on a per-voucher ViewModel with **no company/voucher-type backing store**. Neither claim carries a Tally citation. **[code]** |
| **What Tally does** | Records Payment/Receipt/Contra in **Single Entry** mode (one Account field plus a Particulars list, no Dr/Cr labels) unless the operator turns it off; double-entry is the opt-in. And it **remembers**: "when you switch a voucher to double-entry mode, this setting will be saved, and all future vouchers will open in double-entry mode by default". Ctrl+H is a preference change, not a per-voucher toggle. |
| **Citation** | **[corpus]** GSTN extracted lines 330/334 — the exercises instruct the reader to set "Use single entry mode for payment/receipt/contra vouchers? **NO**", which is only meaningful if the shipped state is Yes; BOOK pp.26-27, 29, 31-32 ("In Single Entry Mode Dr & Cr not Show"); SG p.76. **[web]** `help.tallysolutions.com/payments-and-receipts-tally/` — single entry as the mode for users not versed in Dr/Cr, plus the saved-setting sentence. |
| **How it got in** | Never questioned. "The default" was asserted in the enum's own XML doc and in the VM's doc, with a Tally reference attached to the **mechanism** (Ctrl+H Change Mode) but not to the **default** — which reads as sourced on a skim. On persistence, two different concerns were conflated: not persisting the mode *on the voucher* is correct (it is inferable from the posted legs at print/GSTR-1 time); not persisting it *as a preference* is the divergence. |
| **Fix** | Seed the mode per type instead of a single literal: `SingleEntry` when `CanBeSingleEntry` (Contra/Payment/Receipt), `AsVoucher` otherwise — `ShowPlainDrCrGrid` (`:132`) already stops the two grids co-rendering. Keep the mode off the posted voucher, but **persist the last-used mode as a per-company, per-base-type preference** and seed `_mode` from it. Reword `:91` and `:93-94` (and `AccountingInvoiceLineViewModel.cs:12-13,15,23`) with the GSTN and TallyHelp citations, distinguishing "never persisted on the voucher" from "remembered as a preference, as TallyPrime does". |

---

### IV-21 · ~~No Alt+H Multi-Masters — every ledger, group and stock item is created one form at a time~~ — **BUILT 2026-10-13, REMEDIATED 2026-10-16**
**MEDIUM** · Class **B** · Area **MST** · relates to `tally-fidelity-defects.md` D4

> 🔴 **2026-10-16 — THIS BANNER SAID "CLOSED IN CODE" WHILE THE ALTER VERB IT CLOSED ON CARRIED A
> DATA-INTEGRITY DEFECT. That was overstated closure and it is corrected here rather than quietly rewritten.**
> The wave-1 adversarial review of `claude/apex-bb-multimaster` found, and reproduced in three families, that a
> **REFUSED Multi Alter batch was not all-or-nothing**: the rows written before the refusing row stayed mutated
> in the live aggregate and **became durable on the next unrelated save**, so a ledger rename the operator was
> told had been refused reached disk — and a rename is resolved by id by every historical voucher. The class
> doc claimed all-or-nothing, which was true of the CREATE arm and false of the ALTER arm, and the catch block
> justified itself with "the in-memory aggregate is reloaded from disk by the caller's Escape/re-open", which
> was simply false (`CompanyStorage.Load` is reached only from company-open and company-unlock).
>
> **FIXED on the same branch**, by `Apex.Ledger.Services.MasterStateSnapshot`: both arms take a rollback point
> before the first write and restore it on any refusal, so a refused batch leaves the book exactly as it was —
> including `RecomputeNatureFor`'s nature cascade over descendants that were never rows of the batch. Proved by
> re-reading from SQLite after a later unrelated save, and by mutation: deleting the restore reddens five named
> tests in `MultiMasterAlterRollbackTests`. A pre-validation pass was rejected as the fix because it is
> *unsound* here — the cycle and same-category guards judge the state the book will be in once the OTHER rows
> of the batch are applied.
>
> ⚠️ **And the register's voice on this row is the "CLOSED IS NOT COMPLETE" paragraph below, not the green tick
> above it.** Three vendor-documented things are still absent (Employees / Employee Groups, Cost Categories,
> `Delete Multiple Unused Masters`), so **census row 2.12 stays PARTIAL**; the build closing IV-21's three-part
> verdict is not the same as the census cell being satisfied.

> 🟢 **BUILT 2026-10-13 (wave 1, track bb, branch `claude/apex-bb-multimaster`) — ALL THREE PARTS OF THE
> VERDICT, AND THE "Fix" ROW BELOW WAS FOLLOWED EXACTLY INCLUDING ITS OPENING-BALANCE COLUMN.** Measured at
> `origin/main` `bf9b1e6` BEFORE any code was written, because the † banner's own greps had gone stale twice:
> `MultiMasterKind` held **Ledger and AccountGroup only**; `grep -rniE "multi[- ]?alter" src/` returned **exactly
> one** hit and it was a grounding *comment* (`MultiMasterCreateViewModel.cs:104`); and the only `Key.H` arms in
> `MainWindow.axaml.cs` were the two `Ctrl+H` arms, so **Alt+H fell through and did nothing at all.**
>
> What now ships: **five families** (Ledger · Group · **Stock Item** · **Godown** · **Cost Centre**), **both
> vendor verbs** (Multi Create / Multi Alter), **Alt+H** bound on the Chart of Accounts, and the vendor's
> **Alt+B (Zero Opening Balance)** on the Multi Ledger Alteration screen. The Opening Balance column this row
> asked for had already shipped with PR #54 and now works on the ALTER verb too.
>
> 🔴 **THE CITATION IN THE TABLE BELOW IS INOPERATIVE AND IS KEPT ONLY TO SHOW WHAT IT WAS.** It reads
> **[corpus]** *SG p.66 / p.68 / p.69*, and ruling 14 (2026-09-04) struck the corpus — `tally/` is empty and was
> never git-tracked. **The verdict survived the source change rather than depending on it:** the vendor's own
> pages say the same thing and were opened BY CONTENT on 2026-10-13 —
> `help.tallysolutions.com/tally-prime/charts-of-accounts-tally/` for the chord, both verbs, the *"Under Group"*
> header defaulting to *"All Items"*, the *Name of Ledger / Under / Opening Balance* columns, the *Multi Ledger
> Alteration* screen and Alt+B; plus `stock-items-faq/`, `inventory-storage-using-godowns-locations-tally/` and
> `cost-centre-or-profit-centre-tally/` for the other three families. Each quote sits on `MultiMasterKind`.
>
> ⚠️ **CLOSED IS NOT COMPLETE, and the difference is written down rather than left to a reader.** Three things
> the vendor documents here are still absent: **Employees / Employee Groups** (attested, and **out of scope** by
> user rulings 29+30 — Area 7), **Cost Categories** (attested on the cost-centre page, left for a later slice
> rather than half-built), and 🔴 **`Delete Multiple Unused Masters` — a THIRD Multi Masters verb, found as a
> heading on the Chart of Accounts page during this slice, which has NO CENSUS ROW AT ALL and is owed one.**
> What this slice OWNS as divergences is filed as **`IV-85`…`IV-91` in §2G**, and `IV-85` is a finding against
> the census rather than against us: row 2.12's target of *"the vendor's **SIX** families"* has **no source**.

> **† 2026-08-15 — verdict STANDS; but the D4 cross-reference is now STALE.** "Alt+H", "MultiCreate",
> "Multi Create" and "Multi-Master" still return zero source hits, and `MainWindowViewModel.cs:1180` still emits
> one `Page` row per master. **However `tally-fidelity-defects.md` D4 (no Opening Balance field) was FIXED by
> `c8b44cf`** — `LedgerMasterViewModel.cs:1053-1054` now writes `OpeningBalance`/`OpeningIsDebit` from a real
> form control (`MainWindow.axaml:4379-4410`). **So the closing sentence "together with D4 … the initial setup is
> the worst-served workflow" now overstates by one row.** What survives of that pairing is the *bill-wise opening
> breakup* sub-screen (SG p.91 step 6), which is still absent.

| | |
|---|---|
| **What the customer experiences** | Opening a new set of books means keying a chart of accounts — **routinely 60–150 ledgers** — through 60–150 separate form screens, each with its own Under picker, instead of one grid. This is the very first thing a migrating accountant does, and it is the moment they decide whether the product is usable. Combined with the missing Opening Balance field (D4), **the initial setup is the worst-served workflow in the app**. |
| **What we invented** | "Alt+H", "MultiCreate", "Multi Create" and "Multi-Master" appear **zero times** anywhere in `src/`. Master creation is one screen per master (`BuildCreateColumn`, `src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:1180-1256`); alteration is one row at a time from the Chart of Accounts. **[code]** |
| **What Tally does** | From Chart of Accounts → Ledgers (or Groups, or Stock Items), **Alt+H** opens Multi-Masters → Multi Create / Multi Alter: a grid where dozens of masters are keyed or edited in one pass, with a common "Under" group set once at the top. |
| **Citation** | **[corpus]** SG p.66 "Multiple Ledger Creation" — "Go To (Alt+G) → Chart of Accounts → Ledgers → Enter → Press Alt+H for Multi-Masters → Multi Create"; SG p.68 "Multiple Group Creation"; SG p.69 "Multiple Group Alteration", with the caveat "You cannot delete Groups and Ledgers from the Multiple Alteration screen". |
| **How it got in** | Never questioned — **no `plan.md` item covers multi-master entry**, and the single-master forms were built first and never revisited. |
| **Fix** | Add a Multi-Masters grid on Alt+H from the Chart of Accounts, initially for Ledgers and Groups (the two the corpus walks), with a header "Under" applying to all rows and a per-row override. The domain create paths already exist and are shared with the single forms — this is a **grid over them** — and it should carry the Opening Balance column D4 wants anyway. |

---

### IV-22 · Invoice round-off is hardcoded to nearest rupee with no method and no limit — and no production path ever switches it on
**MEDIUM** · Class **A** · Area **TAX** · FIX-F10

> **† 2026-08-15 — verdict STANDS, and the code now says so itself.** `GstService.cs:729`, `:226-233`, `:61` and
> `:610` are unchanged, and the only `applyInvoiceRoundOff: true` anywhere in the repo is in tests.
> `VoucherPrintProjector.cs:642-644` now states the defect outright: *"Zero for every voucher this app posts:
> nothing in `src/` ever posts a Round-Off leg."* Corroborated by `docs/full-clone-census.md:139`
> (*"No path posts a Round-Off leg at all"*). **Corrected:** `VoucherEntryViewModel.cs:3505` → **`:3664`** ·
> `:3638` → **`:3852`** · `:4070` → **`:4284`** · `VoucherPrintProjector.cs:344` (FIX-F10) → **`:338`**
> (also `:357`, `:567`, `:601`) · **`VoucherPrintProjector.cs:354` is no longer a call site at all** — the print
> path was moved onto the posted legs, so it no longer calls `ComputeInvoiceTax`. `PosBillingViewModel.cs:438`
> and `CreditDebitNoteService.cs:129` are unchanged and still omit the argument.

| | |
|---|---|
| **What the customer experiences** | A retailer who rounds every bill to the nearest ₹10 **cannot**. Taxable ₹1,235.60 + 18% = ₹1,458.01: TallyPrime with an Invoice Rounding ledger at Downward Rounding / limit 10 prints ₹1,450 and books ₹8.01 to Round Off; we print ₹1,458.01. Worse, **since no screen ever passes the flag we do not round at all** — a "Round Off" ledger appears in the customer's chart of accounts the day GST is enabled and never receives a single posting, which reads as a broken feature. |
| **What we invented** | `src/Apex.Ledger/Services/GstService.cs:729` `var rounded = Math.Round(grand, 0, MidpointRounding.AwayFromZero);` — Normal rounding, limit 1, not configurable. The "Round Off" ledger is auto-created under Indirect Expenses with **no ledger type and no rounding settings** (`:226-233`, name constant at `:61`). `applyInvoiceRoundOff` defaults to `false` (`:610`) with **no production call site passing `true`** — `VoucherPrintProjector.cs:354`, `PosBillingViewModel.cs:438`, `VoucherEntryViewModel.cs:3505`, `:3638`, `:4070` and `CreditDebitNoteService.cs:129` all omit the argument; `VoucherPrintProjector.cs:344` already records this as **FIX-F10**. **[code]** |
| **What Tally does** | Rounding is a property of a **ledger the operator adds to the invoice**: Type of Ledger = "Invoice Rounding", with a Rounding Method (Not Applicable / Downward / Normal / Upward) and a **Rounding Limit** that fixes the multiple — "if the invoice value is 125.60, the invoice value is rounded off to 125 (when the Rounding limit is set to 1)". |
| **Citation** | **[web]** `help.tallysolutions.com/round-off-invoice-and-ledger-values/` and `…/india-gst-expense-income-non-revenue-invoice-round-off-ledgers-tally/`. **[corpus]** the same three-way Downward/Normal/Upward vocabulary plus a Limit field appears for interest (BOOK p.118, lines 4280-4292) and for payroll pay heads (lines 11717-11720), confirming it is Tally's general rounding model. |
| **How it got in** | Half plan decision, half drift. `memory.md:392-395` records the judgment call "(b) Round-Off ledger auto-created under Indirect Expenses"; `memory.md:383` describes "optional invoice round-off nearest-rupee" — the nearest-rupee shape was chosen with **no Tally citation**, and the optional flag was then **never wired to a UI control**. |
| **Fix** | Model it as Tally does: add a `LedgerType` of `InvoiceRounding` with `RoundingMethod` and `RoundingLimit` on the Ledger master, and have `ComputeInvoiceTax` take the **resolved ledger** rather than a bool — rounding the grand total to the nearest multiple of the limit in the method's direction. Trigger it when such a ledger is on the voucher, which is Tally's gesture. This closes FIX-F10 at the same time. |

---

### IV-23 · Ageing buckets are ours, not Tally's, and there is no age-by-bill-date mode
**MEDIUM** · Class **C** · Area **RPT**

> **† 2026-08-15 — verdict STANDS.** `Outstandings.cs:85-92` (five hardcoded buckets, literals at `:87-91`),
> `:37-41` and `:50-52` are unchanged. **Corrected:** `AgeingOf` is declared at **`:213`**, not `:218` — `:218`
> is `var overdue = b.OverdueDays(asOf);`, which is the line that actually proves due-date-only ageing.

| | |
|---|---|
| **What the customer experiences** | An accountant who has always read a **four**-column ageing schedule gets **five** columns whose boundaries share nothing with Tally's — no column of ours maps onto any column of a Tally-shaped ageing statement, so the receivables note in the financials has to be rebuilt by hand. A bill dated 01-Apr with 30 credit days, viewed on 20-May, sits in Tally's 45-90 column under Ageing by Bill Date (49 days old) but in our "0-30 days" column (19 days overdue) — **two different provisioning bands for the same bill**. And a business that ages by bill date, the more common practice for provisioning, cannot do so at all. |
| **What we invented** | `src/Apex.Ledger/Reports/Outstandings.cs:85-92` fixes five buckets — "Not due", "0-30 days", "31-60 days", "61-90 days", "90+ days" — with **no configuration anywhere**. Bucketing is driven solely by `OverdueDays` (`:37-41`, `asOf − DueDate` floored at 0) via `AgeingOf` at `:218`, so **only due-date ageing exists**. **[code]** |
| **What Tally does** | The Ageing Analysis report ships default periods of **<45, 45-90, 90-180, >180** days, and the From/To values are operator-editable ("you can set the values to 0 to 15, 15 to 30, 30 to 45 and so on"). F6 Age-wise offers two **Methods of Ageing**: Ageing by Bill Date (the voucher entry date) and Ageing by Due Date. |
| **Citation** | **[web]** `help.tallysolutions.com/article/Tally.ERP9/Reports/MIS_Reports/Ageing_Analysis_Report.htm` (defaults and editable ranges); `help.tallysolutions.com/manage-receivables-outstanding-tally/` (the two Methods of Ageing). Corpus **silent**: 0 hits for "ageing"/"aging" as a receivables report. |
| **How it got in** | Never questioned. The field is even labelled honestly — the record's doc at `:50-52` calls them "simple ageing buckets" — but **no Tally default was looked up**, and the extra "Not due" bucket has no counterpart in Tally at all. |
| **Fix** | Make the bucket edges **data** on the report options (seeded to <45 / 45-90 / 90-180 / >180) and add an `AgeingMethod` of `ByBillDate | ByDueDate`, computing the age from `asOf − Date` or `asOf − DueDate` accordingly. Keep `OverdueDays` for the drill-down column. |

---

### IV-24 · Automatic numbering is `max+1` over the whole company: it leaves gaps (Tally's non-default option) and is not ordered by voucher date
**MEDIUM** · Class **C** · Area **ENT**

> **† 2026-08-15 — verdict STANDS; both primary citations were past or near the end of their files.**
> **Corrected:** `LedgerService.cs:171` → **`:153-160`** — *the file is only 162 lines, so `:171` never existed at
> HEAD* (`public int NextNumber(Guid voucherTypeId)` at `:154`, `return max + 1;` at `:160`) ·
> `InventoryPostingService.cs:127` → **`:148-155`** · `plan.md:624` → **`plan.md:690-691`**. Both `NextNumber`
> bodies are still duplicate implementations taking a plain max over `Number` with no date term.
>
> **†† 2026-08-17 (Phase 10.11 S4) — THE 2026-08-15 CORRECTION'S OWN LINE NUMBERS HAVE NOW MOVED, and the
> sentence above about the file's length is no longer true of HEAD.** S4 amended `LedgerService`'s doc comments —
> `Delete` documented itself as *"may leave a gap in numbering"*, which describes the mid-sequence case and
> silently missed the number-REUSE case this row is about — so the file grew from **162 to 177 lines**.
> **Consequences, recorded rather than left to drift:** `NextNumber` is now at `LedgerService.cs` **line 169**
> (`return max + 1;` at **line 175**), so this row's live citations below were re-pointed from `:153-160` to
> `:169-175`. And `:171` — quoted above only in order to be condemned as past-EOF — **now resolves**, to
> `var max = 0;` inside that same method; it is still the wrong citation for what the 2026-08-15 note was
> correcting, but it is no longer a DANGLING one, so its exemption in
> `DocumentCodeAgreementTests.HistoricalCitationAllowList` was deleted (that list may only shrink, and the test
> itself flagged the entry as stale). **The original claim is still quoted verbatim above and nothing was
> rewritten** — this note is the correction beside it.
>
> **The verdict is UNCHANGED and S4 did not close this row.** S4 built the guard that bounds the damage
> (`MasterDeletionRules` refuses Delete on a filed statutory document and offers Cancel), **not** the fix this row
> asks for. Both `NextNumber` bodies are still duplicate plain-max implementations with no date term, there is
> still no Renumber / Retain toggle, and gap-on-delete still stands. **What S4 DID settle, as an explicit user
> decision (D-3), is that deleting the highest-numbered NON-filed voucher REUSES its number, and that this is
> accepted behaviour rather than a defect** — pinned by
> `MasterDeletionRulesTests.Deleting_the_highest_NON_FILED_number_REUSES_it_which_is_accepted_behaviour_under_D3`.
> Building the numbering floor named in the Fix row is what would change it.

| | |
|---|---|
| **What the customer experiences** | The sales register develops **holes the operator cannot close**, and a GST officer or auditor reading a series that jumps 44, 45, 47 asks where invoice 46 went — every time, for the life of the book. Separately, an accountant who keys Friday's invoices on Monday and then a back-dated Thursday one gets **invoice 51 dated before invoice 50**; the sales register sorted by number is not in date order, which is exactly what a serial number is for. |
| **What we invented** | `LedgerService.NextNumber` (`src/Apex.Ledger/Services/LedgerService.cs:169-175`) — and its twin `InventoryPostingService.NextNumber` (`src/Apex.Ledger/Services/InventoryPostingService.cs:148-155`) — returns the maximum existing `Number` for the type plus one, scanning all vouchers with **no date scoping and no renumbering**. `plan.md:624` records the consequence as settled and unremarkable: "the Renumber / Retain delete-behaviour toggle (today's only behaviour — Cancel keeps the number, Delete leaves a gap — is retained)", **without noting that gap-on-delete is Tally's non-default choice**. **[code]** |
| **What Tally does** | Under Automatic, offers two sub-options and **defaults to Renumber Vouchers**: deleting voucher 2 from the sequence 1,2,3 results in 1,2. "Retain Original Voucher No." — which preserves the gap, leaving 1,3 — is the **opt-in** alternative. The same pair is offered under Multi-User Auto. |
| **Citation** | **[web]** `help.tallysolutions.com/use-voucher-numbering-methods/` — "Renumber Vouchers (default) … results in 1,2"; "Retain Original Voucher No. (preserves gaps) … leaves 1,3". The **date-ordering half is inferred** from the renumbering semantics, not separately quoted (§6 U-10). |
| **How it got in** | An orchestrator/plan ruling at `plan.md:624` that framed the existing implementation as a **deferral of a toggle** rather than as a choice between two Tally behaviours. Nobody recorded which side Tally defaults to, so the app's incidental behaviour became the shipped policy by omission. |
| **Fix** | Add the Renumber / Retain Original toggle to the voucher type with **Renumber as the default**, and make Automatic sequence by (voucher date, entry order) rather than by `max`. **Both engines must change together** — `LedgerService.cs:169-175` and `InventoryPostingService.cs:148-155` are duplicate implementations and have already been a source of drift. Lock it with a test that deletes a middle voucher and asserts contiguity, and one that back-dates an insert and asserts the numbers follow the dates. |

---

### IV-25 · Apex ships three of TallyPrime's five numbering methods — and D23's "unverified premise" is now verified
**MEDIUM** · Class **B** · Area **ENT** · `tally-gap-decisions.md` D23

> **† 2026-08-15 — verdict STANDS, and the Fix's FIRST action is still outstanding.** `NumberingMethod.cs` still
> declares exactly three members and `AutomaticManualOverride` appears nowhere in `src/`. **The Fix says "Record
> the verification in `memory.md` and close D23" — that has not happened:** `D23` still appears in exactly one
> file, `docs/tally-gap-decisions.md`, and `memory.md` has no D23 entry. **Corrected:** `NumberingMethod.cs:8` →
> **`:9`** (`:8` is the doc line; the members are `:9`, `:12`, `:15`) · `plan.md:625` → **`plan.md:689`** (cited
> twice in this row) · `LedgerService.cs:48` → **`:51`** · `InventoryPostingService.cs:53` → **`:91`**.
> `docs/tally-gap-decisions.md:562` and the `[UNCITED]` flag at `:563-564` are unchanged.

| | |
|---|---|
| **What the customer experiences** | The one mode a real billing desk asks for — **number my invoices automatically, but let me type over this one and carry on** — does not exist. Today the only escape is to switch the entire voucher type to Manual (itself unreachable, IV-13), so a single out-of-band number costs the whole series its automation. Multi-User Auto's absence matters only when Apex becomes multi-user. |
| **What we invented** | `src/Apex.Ledger/Domain/NumberingMethod.cs` declares exactly three members — Automatic, Manual, None. `plan.md:625` deferred "the **5-method** `NumberingMethod` extension" — asserting the number five with no citation. `docs/tally-gap-decisions.md:562` (D23, 2026-08-01) correctly refused that assertion, flagging it `[UNCITED]` and recommending option A: verify before deciding. **That verification has now been run, and the plan's uncited claim was right.** **[code]** |
| **What Tally does** | Five methods: **Automatic** (with Renumber / Retain sub-options), **Automatic (Manual Override)**, **Manual**, **Multi-User Auto** (displays "1\<Auto\>" per user and resolves on save), and **None**. Automatic (Manual Override) automates the number while letting you override a specific voucher, with the next number prefilling from the override. |
| **Citation** | **[web]** `help.tallysolutions.com/use-voucher-numbering-methods/` — all five enumerated with behaviour. **[corpus]** names only "Automatic" (SG line 6057; BOOK line 5455; TB2 lines 1885, 1894) and "Use advanced configuration: Yes" (SG line 6058) — consistent with, but not enumerating, the five. |
| **How it got in** | `plan.md:625` asserted the five-method fact from memory and deferred acting on it; the 2026-08-01 audit correctly downgraded it to an unverified premise. **The gap is not a wrong decision — it is a verification task that was queued (D23 option A) and never run.** |
| **Fix** | **Record the verification in `memory.md` and close D23 as "premise confirmed".** Then add `AutomaticManualOverride` to `NumberingMethod`, with the engine branch at `LedgerService.cs:48` / `InventoryPostingService.cs:53` treating it as Automatic unless the voucher already carries an operator-supplied number, and the next seed taken from `max+1` of the resulting numbers (what "prefills as 22" means). **Depends on IV-13** — schedule after it. Defer Multi-User Auto; it is meaningless single-user. |

---

### IV-26 · "Predefined groups cannot be renamed" is derived from a citation that only says they cannot be DELETED
**MEDIUM** · Class **C** · Area **MST**

> **† 2026-08-15 — verdict STANDS; every citation exact, zero drift.** `MasterAlterationRules.cs:211-213` (the
> citation-to-conclusion gap, verbatim), the rename throw `:221-223`, the re-parent block `:225-227` and
> `GroupService.cs:96` are all unchanged, and `AlterGroup` (`:89`) is still the only caller. The rename throw is
> still guarded only by `if (!group.IsPredefined) return;` at `:218`.

| | |
|---|---|
| **What the customer experiences** | A firm migrating from Tally that **renamed a reserved group** — "Sundry Debtors" → "Customers", "Sundry Creditors" → "Suppliers" is common practice — cannot reproduce its own chart of accounts here, and (because the same guard sits on the Alter path an import ultimately drives) cannot carry those books across without hand-editing every report heading it expected to see. |
| **What we invented** | `src/Apex.Ledger/Services/MasterAlterationRules.cs:211-213` — "Tally ships 28 reserved groups; **the catalogue states they cannot be deleted**, and re-parenting one would move a whole primary head … A predefined group may not be **renamed** or re-parented". **The cited fact (no deletion) does not support the rule implemented**, a hard block on rename at `:221-223` plus re-parenting at `:225-227`. `GroupService.AlterGroup` (`:96`) is the only caller, so the Alter screen refuses outright. **[code]** |
| **What Tally does** | Predefined group names **are alterable**; TallyPrime only advises against it: "You are recommended not to alter the predefined group names, as this may lead to incorrect ledger classification." **Deletion** is what it actually forbids. |
| **Citation** | **[web]** `help.tallysolutions.com/tally-prime/masters-tally/groups-in-tallyprime/` — the alteration recommendation and the deletion restriction. **[corpus]** corroborates deletion only: SG p.69 (line 2130) lists predefined groups among what cannot be deleted. The corpus is **silent on renaming**. |
| **How it got in** | **A citation-to-conclusion gap inside the comment itself**: a real, sourced fact about deletion was used to license a different, unsourced rule about renaming, and the sentence structure makes the second look sourced by the first. |
| **Fix** | Split the guard: **keep** the re-parent block (justified — it would move a Balance-Sheet head), **keep** any delete block, and **downgrade the rename block to a confirmation/warning** carrying TallyPrime's own wording. Correct the comment to cite deletion for deletion and the alteration recommendation for renaming. (Note the 28-group count itself is corpus-confirmed and correct — only the conclusion drawn from it is not.) |

---

### IV-27 · "Accept? Yes/No" exists on masters only — every voucher saves silently on Enter
**MEDIUM** · Class **C** · Area **ENT**

> **† 2026-08-15 — verdict STANDS; every one of the four our-side citations drifted.** The "~24 master screens,
> zero voucher screens" count was re-counted and is exactly right. **Corrected:** `IsMasterAcceptScreen`
> `:4873-4885` → **`:4882-4891`** · the prompt text `:4899` → **`:4908`** · the WI-11 arm
> `MainWindow.axaml.cs:808-811` → **`:892-895`** · the Enter fall-through `:815-818` → **`:899-902`** ·
> the status-bar overlay `MainWindow.axaml:16991-16995` → **`:16987`/`:16991`** ·
> `docs/voucher-entry-specification.md:96` → **`:93`**.

| | |
|---|---|
| **What the customer experiences** | The one screen where a Tally operator expects a last look before committing — **the voucher, which moves money** — commits silently on Enter, while the ledger master they barely care about stops and asks. The asymmetry teaches them to distrust Enter everywhere, which is corrosive in a product whose entire entry model is Enter-through-the-fields. |
| **What we invented** | `IsMasterAcceptScreen` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:4873-4885`) enumerates ~24 **master** screens; no voucher-entry screen is in the list. The WI-11 confirmation arm (`MainWindow.axaml.cs:808-811`) never fires on a voucher and Enter falls through to `ActivateSelected()` (`:815-818`), which saves outright. The prompt itself is also a letter-key question — "Accept Ledger? (Y/N)" (`:4899`) — painted over the status bar (`MainWindow.axaml:16991-16995`), not a Yes/No field on the form. **[code]** |
| **What Tally does** | The terminal **Accept** field is on vouchers as well as masters, and it is a two-option field defaulting to Yes, taken with Enter (or bypassed with Ctrl+A). Every corpus voucher walkthrough ends on it. |
| **Citation** | **[corpus]** SG p.77 step 4 (Contra) — "Accept the screen or press Ctrl+A to save the voucher" — with the identical closing step for Payment, Receipt, Journal, Purchase, Sales, Debit Note and Credit Note across SG pp.77-84 (extracted lines 2304, 2347, 2369, 2388, 2419, 2440, 2459, 2481, 2501, 2526, 2600). Master side for contrast: SG p.74. |
| **How it got in** | `docs/voucher-entry-specification.md:96` correctly recorded "No `Accept? Yes/No` confirmation prompt" as a gap; **WI-11 then implemented it for master screens only** and that spec line was never revisited, so the register still reads as if the gap were whole. |
| **Fix** | Extend the WI-11 prompt to the voucher-entry screens — the mechanism already yields correctly to Ctrl+A and to an open dropdown, so this is a **list change plus tests** — and render it as a Yes/No field at the foot of the form with Yes preselected, so Enter accepts and N/Escape returns to the screen. |

---

### IV-28 · Three TallyPrime report keys are squatted by unrelated screens, all bound app-wide
**MEDIUM** · Class **C** · Area **ENT** · *(Ctrl+B, the fourth, is IV-5 — **† now FIXED and unbound**)*

> **† 2026-08-15 — verdict STANDS; all three bindings intact, all three line numbers drifted.** Commit `6a28d15`
> did **not** touch these arms (its hunks are the accept-prompt block and `CanQuickJump`); the +41/+59 shift comes
> from that block plus the Ctrl+B reservation added by `f2abdbb`. All three are still `e.Handled = true; return;`
> with **no `CurrentScreen`/`IsReportContext` scoping** — the only gates are `GstEnabled` and `!IsTyping(e)`.
> `:426-432` in particular still fires Ctrl+F from **any** screen, which is the sub-claim this row calls "the
> subtle one". **Corrected:** Ctrl+R `:290-297` → **`:331-338`** · Alt+R `:356-362` → **`:415-421`** ·
> Ctrl+F `:367-373` → **`:426-432`**.

| | |
|---|---|
| **What the customer experiences** | An operator tidying a Trial Balance presses **Ctrl+R to hide a line and is thrown into a GST rate-maintenance screen**, losing their place in the report. Because the bindings are global and consume the key, **none of the TallyPrime meanings can ever be added later** without first unpicking these — so this is not only a today problem. |
| **What we invented** | `src/Apex.Desktop/Views/MainWindow.axaml.cs` binds, each with `e.Handled = true; return;`: **Ctrl+R** → GST Rate Setup (`:290-297`), **Alt+R** → Challan Reconciliation (`:356-362`), **Ctrl+F** → TDS Stat Payment (`:367-373`). **[code]** |
| **What Tally does** | **Ctrl+R** removes a line entry from a report; **Alt+R** retrieves the narration for the same party from the previous voucher entry; **Ctrl+F** filters data in a report — and opens Stat Payment **only on a Payment voucher**. |
| **Citation** | **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` — "Ctrl+R — To remove a line entry from a report"; "Alt+R — To retrieve Narration for the same party from the previous voucher entry"; "Ctrl+F — To filter data in a report — Right button". **[corpus]** BOOK pp.431-432 (Ctrl+R against "To remove an entry from a report"); SHORTKEY item 37 (corroboration only). |
| **How it got in** | Each accelerator was chosen slice-by-slice from **the first letter of its own feature** (Rate, Reconciliation, Filing) with no check against a TallyPrime key map. **Ctrl+F is the subtle one**: the key *is* right for Stat Payment, but TallyPrime scopes it to the Payment voucher and we fire it from anywhere. |
| **Fix** | Scope each binding to the screen it belongs to — Ctrl+F only on a Payment voucher; move Alt+R and Ctrl+R to unclaimed keys — and reserve Ctrl+B, Ctrl+R, Alt+R and Ctrl+F for their TallyPrime meanings. **A single key-map table checked against `help.tallysolutions.com` would prevent the next one**; build it as part of this fix. |

---

### IV-29 · The Gateway's sections and vocabulary are ours, not Tally's — and "Alter" is not on it
**MEDIUM** · Class **C** · Area **ENT**

> **†† 2026-08-17 — THE PRESCRIBED FIX WAS PARTLY APPLIED, AND EVERY CITATION IN THIS ENTRY WAS RE-ANCHORED.**
> W0-2b (the company profile screen) first added a **new "Company" section ABOVE Masters** carrying an "Alter
> Company" row — i.e. it grew one more section per phase, put it ahead of Masters, and put a company verb on a
> Gateway row: three moves in the direction this entry says is wrong, and it moved the Gateway's default
> keyboard highlight off Masters → Create for every entry into the screen. **That was corrected to this
> entry's own prescribed fix:** `BuildRootColumn` now reads Masters (Create, **Alter Company**, Chart of
> Accounts), the section list is back to five, and the opening highlight is back on Create.
> **What that does NOT close, and the row stays open for it:** the shipped row alters the **company**, whereas
> TallyPrime's Masters → **Alter** is a *master*-alteration submenu (SG p.67/69/73 — Alter → Ledger / Group /
> Voucher Type). The door to the alteration FORMS is still only "press Enter on a Chart of Accounts row". The
> rest of the Fix below — collapse the report groups behind "Display More Reports", move Banking to Utilities,
> reach F11 by the key — is untouched.
>
> **Citations re-anchored 2026-08-17** (the slice moved this method by ~135 lines and every line-number in this
> entry, including the 2026-08-15 block below, had drifted): `BuildRootColumn` **`:1047-1126`** *(was
> `:912-978`)* · F11 as a menu row **`:1074`** *(was `:926`)* · "Statements" / "Statements of Accounts"
> adjacent at **`:1091-1092`** *(was `:943-944`)* · the Reports section's 3 direct pages **`:1087-1089`** *(was
> `:939-941`)* and its 8 `▸` groups **`:1090-1095`**, **`:1101`**, **`:1112`** *(was `:942-947`, `:953`,
> `:964`)* · the "deliberate promotion" comment **`:1114-1118`** *(was `:966-970`)* · the `IsAltering`
> machinery **`:6013-6046`** *(was `:5846-5881`)*.
>
> **† 2026-08-15 — verdict STANDS; one count loose, two citations drifted.** *(Kept as the dated record it is;
> the line numbers in it are superseded by the block above.)* `BuildRootColumn` was unchanged and contained
> **no "Alter" row**; F11 was a menu row; "Statements" and "Statements of Accounts" sat adjacent.
> **Corrected:** *"Reports (ten sub-groups)"* → the Reports section carries **11 rows** — 3 direct pages plus
> **8** `▸` groups (the last two conditional) · the "deliberate promotion" comment `:6806-6812` → its
> 2026-08-15 value `:966-970` · the `IsAltering` machinery `:5804-5842` → its 2026-08-15 value `:5846-5881`.

| | |
|---|---|
| **What the customer experiences** | The first screen the operator ever sees is **not the one screen they know by heart**, so every printed instruction they own fails at the first step: "Gateway of Tally → Alter → Ledger" and "GOT → Display More Reports → Account Books → Sale Register" both dead-end because neither entry exists. Alteration in particular is only reachable by knowing to press Enter on a Chart of Accounts row — a route the corpus never teaches. |
| **What we invented** | `BuildRootColumn` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:1047-1126`) ships: Masters (Create, **Alter Company** — added 2026-08-17, see the †† block — Chart of Accounts) · **Statutory** (GST & Taxation F11, GST Rate Setup) · Transactions (Vouchers, Banking, Day Book) · Reports (11 rows) · **Data** (Backup/Restore) · "Quit — Change Company". The only "Alter" item is the **company** one; there is still no door to the MASTER alteration forms, which are reachable only by pressing Enter on a Chart of Accounts row. "Statements" and "Statements of Accounts" sit side by side as two different things; F11 Company Features is a Gateway **row** rather than a key. **[code]** |
| **What Tally does** | Gateway of Tally is Masters (Create, **Alter**, Chart of Accounts) · Transactions (Vouchers, Day Book) · Utilities (Banking) · Reports, where everything beyond the headline statements sits behind **one door, "Display More Reports"**. F11 opens Company Features from the top menu, not from a Gateway row. |
| **Citation** | **[corpus]** SG p.73 — "One can also go to Gateway of Tally → **Alter** → Voucher Type"; SG pp.67/69 — "Gateway of Tally → Alter → Ledger" / "→ Alter → Group". BOOK pp.28-33 route every register through "GOT > **Display More Reports** > Account Books > …" (extracted lines 1162, 1236, 1309, 1499, 1676, 1735). **[web]** "F11 — To open Company Features screen — Top menu". **Scope note:** the corpus proves "Display More Reports" is the single door; it does **not** prove the exact contents of TallyPrime's "Statements of Accounts" submenu (§6 U-16). |
| **How it got in** | The menu **grew a section per phase** rather than being laid out once from the reference product: Statutory arrived with Phase 4 GST, Data as the R-7 backup carve-out (the comment at `:1114-1118` explains the deliberate promotion), Statements with RQ-5. **W0-2b nearly added a seventh the same way** — a "Company" section above Masters — which is why this entry is now cited from the code that places the row (`MainWindowViewModel.cs:1047-1067`) and not only from the register. |
| **Fix** | Restore the reference shape: add **"Alter"** to Masters as the door to the alteration forms (the `IsAltering` machinery at `:6013-6046` already exists and only needs a picker in front of it) — **partly done 2026-08-17: a Masters row exists, but it alters the COMPANY, not a master, so the master-alteration door is still owed** — collapse the report groups behind a single **"Display More Reports"**, move Banking to a Utilities section, and reach F11 by the key rather than by a Gateway row. |

---

### IV-30 · The red bare-letter hotkeys are auto-assigned by row position, against the audit's own recommendation
**MEDIUM** · Class **C** · Area **ENT** · WI-9 / R7

> **† 2026-08-15 — verdict STANDS and, unusually, EVERY citation is still exact** (`GatewayColumn.cs:381`, `:379`,
> `:383`; `MainWindow.axaml:365`; `MainWindowViewModel.cs:784`; `docs/ca-audit-backlog.md:2425`, `:2216-2227`,
> `:2418-2423`). The walk at `GatewayColumn.cs:392-406` is still position-dependent. **One correction:** the row
> quotes *"Until answered, the fidelity target is an assumption"* as being at `ca-audit-backlog.md:2418`; that
> sentence is **not** on that line — `:2418` reads *"**For the CA / A14 (🟡 R7 — currently UNVERIFIED; needs a real
> TallyPrime build or official Tally help):**"*, with the three questions at `:2419-2422`. A mis-quotation, not a
> false claim about the code.

| | |
|---|---|
| **What the customer experiences** | **Muscle memory does not survive.** Because the letter depends on position, adding a menu row — or an F11 toggle that reveals one — silently re-letters every row below it, so the key that opened Balance Sheet last week opens something else this week. The greedy rule also produces letters no one would choose: the second row starting with a claimed letter gets an **interior** character highlighted, so the operator is asked to notice the "a" in "B**a**nking". For a keyboard-first accounting app, an unstable accelerator is worse than none. |
| **What we invented** | `GatewayColumn.AssignHotKeys` (`src/Apex.Desktop/ViewModels/GatewayColumn.cs:381`) walks `Items` **in order** and gives each row the first letter of its label not already claimed, seeded from `ReservedLetters = { 'O', 'Y' }` (`:379`); the chosen index is painted `#C62828` bold (`MainWindow.axaml:365`). The letters are a function of row **order**, recomputed on every column build (`MainWindowViewModel.cs:784`). `docs/ca-audit-backlog.md:2425` recommended the opposite: "**Authored letters** (stable, hand-picked — **recommended**) or auto-assigned (reshuffles when an F11 feature toggles)". **[code]** |
| **What Tally does** | **UNVERIFIED, and honestly so.** The official shortcuts page documents F-key, Alt and Ctrl combinations only and does not mention selecting a menu item by typing a bare letter. That is **silence, not a denial** — the corpus is exam material and the shortcut reference is not a menu-rendering spec. What remains unknown is the activation gesture, the colour, and above all whether Tally's designated letters are **stable per menu**. |
| **Citation** | **[web]** `help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/` — F/Alt/Ctrl only. **[corpus]** zero hits across all ten PDFs for red-letter / menu-letter terms, re-run at `docs/ca-audit-backlog.md:2216-2227`. **This row is scoped to what our own code proves without any Tally fact: the letters are order-dependent and therefore unstable.** |
| **How it got in** | A **user-specified** requirement (the CA reported it as a real gap) built **before the R7 verification it was conditioned on** — `docs/ca-audit-backlog.md:2418-2423` lists three open questions under "Until answered, the fidelity target is an assumption." Implementation question 7 (authored vs auto) was answered by the implementer in favour of auto, and **no record of that choice or its reasoning exists in `plan.md` or `memory.md`**. |
| **Fix** | Move the letters into an **authored table keyed by menu path**, so a row's accelerator is a property of the row and not of its neighbours; keep `AssignHotKeys` only as a fallback for uncovered rows. Then close the R7 questions by observing a real TallyPrime — bare vs Alt, the actual colour, the collision rule — and record the answer in `docs/tally-feature-catalog.md` §21. **Note the cross-point conflict already flagged:** WI-2/KB-3 wants a typed letter to *filter* a data-driven column, which is why `AssignHotKeys` returns early for `DataDriven` (`:383`). |

---

### IV-31 · The right button bar paints seven non-keys in the accelerator colour
**MEDIUM** · Class **C** · Area **ENT**

> **† 2026-08-15 — verdict STANDS; all seven badges survive, and every line number drifted.** They are now at
> `"Scn"` `:6795` · `"Outs"` `:6802` · `"BRS"` `:6803` · `"Imp"` `:6804` · `"Int"` `:6806` · `"SS"` `:6807` ·
> `"SMTP"` `:6817`. **Corrected:** `BuildButtonBar` `:6750-6772` → **`:6742-6827`** · the "Outs" comment
> `:6753-6755` → **`:6799-6801`** · the red-bold template `MainWindow.axaml:16956-16959` → **`:16953-16956`** ·
> `plan.md` NFR-2 `:94-95` → **`plan.md:115`**. **Uncredited improvement:** `f2abdbb` removed an **eighth**
> offender — `:6796-6798` now reads *"there is deliberately NO 'Ctrl+B' row here … leaving the badge would paint a
> red accelerator for a key that fires nothing, which is register defect IV-31."* So "seven" is correct at HEAD,
> but it was eight when this row was written.

| | |
|---|---|
| **What the customer experiences** | A keyboard-only operator — which is what a Tally operator is — reads **seven red badges as keys, presses them, and nothing happens**. Those seven features (Scenarios, Outstandings, Bank Recon, Import Statement, Interest, Stock Summary, SMTP) are **mouse-only** in a product whose whole premise is that the mouse is optional. |
| **What we invented** | `BuildButtonBar` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:6736-6758`) adds bar items whose `Key` is an invented badge rather than a keystroke — "Scn", "Outs", "BRS", "Imp", "Int", "SS", "SMTP" — and the template renders `Key` in bold AlertRed exactly like a real accelerator (`MainWindow.axaml:16956-16959`). The comment at `:6753-6755` says so outright: "Outs" is used "so the Outstandings quick-button uses a non-key mnemonic badge and is reached by click, never by a colliding 'O' keystroke". **[code]** |
| **What Tally does** | The right button bar **is a key legend**: every button carries the keystroke that fires it, and the red character is the key you press. There are no mouse-only buttons on it. |
| **Citation** | **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` tabulates every shortcut against where it appears ("Right button" / "Bottom bar" / "Top menu") — the bar is populated **by shortcuts**, not by labels. Our own `plan.md` NFR-2 (`:94-95`) requires "every catalogued action reachable by its documented shortcut without a mouse". |
| **How it got in** | A real collision (bare **O** was already Import on the Gateway) was resolved by **inventing a badge** rather than by choosing a free accelerator, and the pattern then spread to six more entries. |
| **Fix** | Give each of the seven a real, unclaimed accelerator and print that in the red slot; where genuinely no key is free, move the entry into the Gateway cascade rather than showing a red badge that is not a key. **Add a test that every `ButtonBarItem.Key` resolves to a handled keystroke.** |

---

### IV-32 · The report-line gestures a Tally user works a report with are all absent
**LOW** · Class **C** · Area **ENT**

> **† 2026-08-15 — verdict STANDS, absence re-confirmed by direct search.** Zero hits across all `*.cs`/`*.axaml`
> in `src/` for `Key.D2`, `Key.NumPad2`, `Alt+2`, `Key.U`, `Ctrl+U`, `Alt+U`, `Ctrl+N`; the only `Key.N` arms are
> `MainWindow.axaml.cs:322`, `:324` and `:367` (Alt+N Auto Columns), none of them Ctrl+N. No calculator UI exists.
> The Ctrl+R dependency still holds — it is claimed by `MainWindow.axaml.cs:331-338` (IV-28). **Corrected:** the
> index pointer `MainWindow.axaml.cs:875` is now `case Key.Enter …`; use **`:182`**, the head of the dispatcher —
> this was always a pointer into the key handler, not a positive citation.

| | |
|---|---|
| **What the customer experiences** | Keying a month of near-identical vouchers — the common case in a trading business — means typing each one from scratch, because **Alt+2 (duplicate the row you are standing on) does not exist**. Narrowing a cluttered report by removing lines, and getting them back, are habits that simply produce nothing here; so is reaching for the calculator without leaving the amount field. |
| **What we invented** | None of **Alt+2, Ctrl+U, Alt+U, Ctrl+N** appears anywhere in `src/`, and Ctrl+R is bound elsewhere (IV-28). The report screens therefore offer no way to duplicate a voucher from a row, remove a line from the view, restore removed lines, or open a calculator. **[code]** |
| **What Tally does** | On a report: **Alt+2** creates an entry by duplicating a voucher; **Ctrl+R** removes a line entry; **Ctrl+U** restores the last hidden line and **Alt+U** restores all of them; **Ctrl+N** opens the calculator panel (**Alt+C** opens it from an Amount field). |
| **Citation** | **[web]** `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` — all four. **[corpus]** BOOK pp.431-432 shortcut table carries the same four; SHORTKEY items 36, 37, 49 (corroboration only). **Note** BOOK's extracted table is column-misaligned; each pairing was cross-checked against the web page before assertion, and ambiguous rows were dropped (§6 U-18). |
| **How it got in** | Never questioned; **no `plan.md` item covers report-line manipulation**, and the Day Book's Alt+A "Add Voucher" picker (WI-12) was built without its Alt+2 sibling. |
| **Fix** | Add these where the Day Book Alt+A picker already lives: **Alt+2** duplicating the highlighted voucher into a pre-filled entry screen, and **Ctrl+R / Ctrl+U / Alt+U** as a view-only removed-line stack on `ReportsViewModel`. The calculator (Ctrl+N, and Alt+C from an Amount field) is independent and small. Depends on IV-28 freeing Ctrl+R. |

---

### IV-33 · An Appropriate-by-Value additional-cost pool silently becomes a by-quantity spread when no destination line carries a rate
**LOW** · Class **A** · Area **VAL**

> **† 2026-08-15 — verdict STANDS, citations essentially exact.** `AdditionalCostApportionment.cs:217-221`,
> `:173-176` and `:66-77` are unchanged; `:220` still silently swaps the basis with nothing surfaced to the
> caller. **Corrected:** the uncited-rationale comment block runs **`:212-216`**, not `:213-217` (`:217` is the
> first code line).

| | |
|---|---|
| **What the customer experiences** | A stock journal moves 10 units of item A and 90 of item B with no rates on either destination line, carrying ₹1,000 of freight on a ledger set to **Appropriate by Value**. We load A with ₹100 and B with ₹900 — **a quantity spread** — while the ledger, the screen and the report all say the cost was apportioned by value. The customer's landed cost per unit is defensible arithmetic **under a method he did not choose**, and there is nothing on screen to tell him the method was swapped. |
| **What we invented** | `src/Apex.Ledger/Services/AdditionalCostApportionment.cs:217-221` — if every destination allocation is rateless, `valueBasis` is swapped to `qtyWeights`. The doc at `:173-176` names it a "Money-conservation guard" so the pool "falls back to a by-quantity spread rather than vanishing". The largest-remainder paisa distribution with an ascending-index tie-break (`:66-77`) is likewise ours. **[code]** |
| **What Tally does** | Exposes exactly **two** apportionment methods on the expense ledger's Method of Appropriation — by quantity and by value — and describes additional cost on a transfer as an accounting of the cost incurred in moving the goods. **No fallback between the two methods, and no paisa-allocation rule, is published.** |
| **Citation** | **[corpus]** BOOK extracted lines 2904-2905 — "Additional Cost/Expenses involved in the Transfer of goods: You can also account the additional cost incurred in connection of transfer of materials from one location to another" — states the feature but no apportionment arithmetic. Tally's behaviour when the by-value basis is zero: **UNVERIFIED** (§6 U-5). |
| **How it got in** | **An uncited engineering invariant (Σ shares == pool) elevated to a business rule.** The rationale is written into the code at `:213-217` with no source, and the same discipline visible in IV-14 is visible here: an internal conservation property was preferred over the published method. |
| **Fix** | **Do not swap the method.** Either surface the condition as a warning and leave the by-value pool unallocated (Tally's own model is post-entry correction), or require a rate on the destination line when a by-value additional cost is present. Whichever is chosen, **the report must never label a quantity spread as a value apportionment.** Confirm Tally's behaviour on a rateless-destination transfer before implementing. |

---

### IV-34 · Ctrl+F7 = Physical Stock is attributed to "TallyPrime's official keyboard-shortcut reference" with no locator
**LOW** · Class **C** · Area **ENT** · `tally-gap-decisions.md` X1 / GAP-4

> **† 2026-08-15 — verdict STANDS; the Fix has NOT been performed.** `SeedVoucherTypes.cs:33-34` still presents
> the quotation with a *description* rather than a locator — no URL, page or retrieval date anywhere in `:33-40` —
> and `MainWindowViewModel.cs:1071` is unchanged. **Corrected:** `MainWindow.axaml.cs:674` → **`:758`**
> (the Ctrl+F7 binding itself is at `:765`). **🔴 The SHORTKEY citation in this row must now be read under the
> outright rejection recorded in `docs/w0-2-company-screen-grounding.md:52-76`** — the file is no longer merely
> "internally shifted", it is ruled out as a source, and the measured shift is **two rows, not one**. This row is
> unaffected on the merits: it already discards SHORTKEY and rests the binding on the TallyHelp page.

| | |
|---|---|
| **What the customer experiences** | Small but real: the seeded voucher type advertises a shortcut on screen. If it is wrong, a Tally-trained operator presses the key they know, gets a different screen, and learns to distrust the hint row. **No money moves.** |
| **What we invented** | `src/Apex.Ledger/Seed/SeedVoucherTypes.cs:33-36` — "Physical Stock is Ctrl+F7 — **TallyPrime's official keyboard-shortcut reference gives** 'To open Physical Stock \| Ctrl+F7'", repeated at `src/Apex.Desktop/Views/MainWindow.axaml.cs:674` and `MainWindowViewModel.cs:1071`. **The quotation is presented verbatim but no URL, page or file is given**, so it cannot be re-checked — precisely the shape of the bill-reference failure this lens exists to catch. **[code]** |
| **What Tally does** | **Unresolved from the sources reached during the hunt.** The only shortcut list in the licensed corpus gives **Ctrl+F8** for Physical Stock — but that list is internally shifted by one across the whole voucher block (it prints F6 Contra / F7 Payment / Ctrl+F7 Journal where TallyPrime is F4 / F5 / F7), so **it is not usable as evidence either way**. A later fetch of the TallyPrime shortcuts page (for IV-32/IV-28) located Ctrl+F7 "To open Physical Stock" under "F10 > Inventory Vouchers", which **supports the binding** — but that reading was made for a different row and has not been pinned into the comment. |
| **Citation** | **[corpus, discarded]** SHORTKEY item 34 "Ctrl+F8 — Physical Stock" (with items 29-33 showing the one-key shift). **[web]** `help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/` — Ctrl+F7 at F10 > Inventory Vouchers. Note the adjacent claim Ctrl+H = Change Mode **is** confirmed (SHORTKEY item 65 plus the payments-and-receipts page) and is sound. |
| **How it got in** | An **uncited quotation**. A previous session corrected this row from "F10" and recorded the reasoning, but wrote the source as a *description* ("TallyPrime's official keyboard-shortcut reference") rather than a **locator**. |
| **Fix** | **Do not change the binding.** Pin the claim: put the URL and retrieval date in the comment, and note explicitly that the corpus shortcut PDF is **discarded as internally inconsistent** so nobody re-litigates it from that file. |

---

### IV-35 · "The Tally-faithful blank-at-zero" is uncited and governs 100 call sites including printed invoices
**LOW** · Class **C** · Area **RPT**

> **† 2026-08-15 — verdict STANDS, and the count was RE-MEASURED and is right.** `IndianFormat.Amount(` matches
> **100 lines across exactly 16 files** at HEAD — the five files the row names by hand are all in that set.
> (Pedantic caveat: raw *occurrences* are 103, because three lines carry two calls; "100 call sites" is defensible
> as a line count.) The claim is if anything stronger now: `6a0268a` added a class comment at `:14-18` re-affirming
> *"The blank-at-zero and Dr/Cr conventions below remain this class's own"* — **without adding a source.**
> **Corrected:** the "Tally-faithful" sentence `:37` → **`:30-31`** · `Amount(decimal)` `:30` → **`:22-23`** ·
> the `SignedAlways`/`AmountAlways` pair `:49-56` → **`:44-45`** and **`:54`** (the pair now spans `:44-57`).

| | |
|---|---|
| **What the customer experiences** | An accountant reconciling with their own books **cannot distinguish "this ledger settled to exactly zero" from "this cell has no data"** — on screen or on a printed report they hand to a client. |
| **What we invented** | `src/Apex.Desktop/Services/IndianFormat.cs:37` — "Exactly zero renders EMPTY — the **Tally-faithful** blank-at-zero of `Amount(decimal)`". `Amount(decimal)` at `:30` returns `string.Empty` for exactly zero, and it has **100 call sites across 16 files**, including `VoucherPrintProjector.cs` (printed vouchers/invoices), `ReportsViewModel.cs`, `OutstandingsViewModel.cs`, `ChartOfAccountsViewModel.cs` and `PrintPreviewViewModel.cs`. **The word "Tally-faithful" carries no page, section or URL.** The sibling `SignedAlways`/`AmountAlways` pair at `:49-56` exists precisely because some rows need the zero shown, which suggests the blank was a formatting choice, not a sourced one. **[code]** |
| **What Tally does** | **Partly supported, not established.** TallyPrime does leave value cells blank in places — the corpus's free-goods walkthrough tells the operator to leave Rate and Amount blank on a zero-valued line — but nothing was found stating that a genuine zero *balance* in a report column prints blank rather than 0.00, **and the two cases are not the same claim**. |
| **Citation** | **[corpus]** supports the zero-valued **invoice line** only (BOOK, free-goods / zero-valued-transactions walkthrough, pp.142-143 area). Nothing in the ten PDFs addresses zero **balances** in report columns. See §6 U-19 for what would settle it. |
| **How it got in** | Never questioned. **"Tally-faithful" was used as a justification adjective** in a formatting helper, where it reads as settled and is cheap to copy — the same class as the bill-reference comment, at lower stakes. |
| **Fix** | **Do not change the rendering on this evidence.** Either cite it (a Help page or corpus figure showing a nil balance printed blank) or **downgrade the comment to what is actually known**: "blank at zero, chosen for a Tally-like look; the zero-valued invoice line is corpus-supported, the zero *balance* case is not." If it later proves configurable in Tally, it belongs behind the report config, not in the formatter. |

---

## 2A. ADDED 2026-09-03 — THE EIGHT THINGS THE T0-4 GST RATE HIERARCHY INVENTED

**Why these are a separate block rather than folded into §2's tables.** §2's Index, its three-class table and the
per-area tables in §3 are all a **2026-08-06 snapshot** whose own header (top of this file) already declares its
arithmetic wrong as totals and its per-row detail authoritative. Re-cutting them for these eight would rewrite the
document's record of what it found on that date, which the header forbids. So the eight live here, each in the
same shape as a §2 row, and **§2's counts are NOT re-cut**: the register now holds **43 rows, of which 39 are
open** (31 open at HEAD before this block, plus these eight, none of which is fixed).

🔴 **READ THE SOURCE CLASS BEFORE ANY ROW BELOW.** These come out of a fix whose grounding is **VENDOR-attested
and CORPUS-SILENT** — the two order strings come from one `help.tallysolutions.com` page, and the corpus states no
GST-rate resolution order anywhere. Ruling 9 requires that distinction be written down rather than blurred, so
every row names its category: **(a)** *corpus silent, ours by design* · **(b)** *the corpus attests X and we
deliberately ship a narrower Y*. The two are different claims and neither substitutes for the other.

⚠️ **AND THE LIMIT ON THIS PASS, STATED ONCE AND APPLYING TO ALL EIGHT.** The git-ignored `tally/` corpus is
**EMPTY in this environment** — the ten PDFs are not on disk — so every corpus claim below is **RELAYED from the
T0-4 design pass of 2026-08-20**, which did have them, and is **not independently re-measured here**. The vendor
URLs were not re-fetched either. The next agent with the corpus mounted should re-run the `hierarch*` grep before
any of these rows is quoted as settled. The full record is `docs/full-clone-census.md` **§1.3 item 15**.

**Index of this block:**

| # | Sev | Class | Area | Ruling-9 category | Item |
|---|---|---|---|---|---|
| **IV-36** | HIGH | C | TAX | **(a)** | The Group and Stock Group rungs climb the PARENT CHAIN; nobody says they should |
| **IV-37** | LOW | C | TAX | **(a)** | The "Ledger" rung is the sales/purchase ledger, never the party's |
| **IV-38** | MEDIUM | C | TAX | **(a)** | A non-Taxable taxability at one rung short-circuits the whole walk |
| **IV-39** | LOW | C | TAX | **(a)** | The HSN and rate lookups walk independently past a partly-filled rung |
| **IV-40** | HIGH | A | TAX | **(b)** | Cess, reverse charge and ITC-eligibility do NOT walk the hierarchy — measured at ₹1,200.00. 🟡 **PARTLY CLOSED 2026-09-04 under labelled assumption A-QA**: cess now walks between the two DETAILED rungs, so the ₹1,200.00 → ₹0.00 move is gone. Reverse charge, ITC-eligibility and all three NARROW rungs are UNCHANGED and still need a schema widening. |
| **IV-41** | MEDIUM | B | TAX | **(b)** | The per-master source selector is not built: four values collapse into one nullable |
| **IV-42** | MEDIUM | B | TAX | **(b)** | No GST Classification master exists — one of the corpus's own five methods |
| **IV-43** | MEDIUM | B | TAX | **(b)** | No per-master dated rate history, and no GST Rate Setup report |

---

### IV-36 · The Accounting Group and Stock Group rungs climb the PARENT CHAIN to the nearest ancestor bearing a block — and no source says whether they should
**HIGH** · Class **C** · Area **TAX** · ruling-9 category **(a)** — corpus silent, ours by design

| | |
|---|---|
| **What the customer experiences** | A dealer types 12% once on a top-level Stock Group and creates three sub-groups under it with no rate of their own. **We charge 12% on every item in all three**, because the Stock Group rung climbs past the empty sub-group to the grandparent that declares a block. Under the other reading — immediate parent only — the sub-groups declare nothing, the walk falls through to the next rung, and the same items resolve off the Company default or hard-block as unresolved. **The two readings give DIFFERENT TAX on the same book, and nobody outside this repository has said which is right.** |
| **What we invented** | `src/Apex.Ledger/Services/MasterAncestry.cs` — `NearestGroupGst` and `NearestStockGroupGst`, a guarded nearest-ancestor-bearing-a-block walk over both parent chains, consumed by `GstService`'s two ancestry rungs and by nothing else. **The choice is pinned by named tests** (`tests/Apex.Ledger.Tests/GstHierarchyAncestryTests.cs`) precisely because it is a choice: a resolver whose behaviour here is whatever the code happened to do is the shape this project has already been bitten by. **[code]** |
| **What Tally does** | **UNKNOWN — and this is the point of the row.** The vendor's hierarchy page names *"Group"* and *"Stock Group"* as rungs and never says whether a rung means the master's immediate parent or its nearest declaring ancestor. The corpus does not reach the question at all. |
| **Citation** | **[web]** `help.tallysolutions.com`, "HSN/SAC & GST Rate Hierarchy in TallyPrime" — names the rungs, **silent on ancestry**. **Corpus: UNREACHED** (relayed; see the block header). Grounding is unreached in BOTH directions, which is why this is a divergence row and not a defect row. |
| **How it got in** | A design decision taken deliberately and recorded: both parent chains are real trees, and a rate typed on a grandparent group is an ordinary book setup that the immediate-parent reading would silently drop. **Recorded, not smuggled** — the reasoning is in `MasterAncestry`'s own class doc under a red flag naming it a ruling-9 divergence. |
| **Fix** | **Do not change it on this evidence.** Either find a source that settles immediate-parent versus nearest-ancestor, or leave it. ⚠️ **One thing that IS owed regardless:** the cycle guard. `MasterAncestry.Climb` records every id before following it, so a cyclic parent chain throws a named domain error in bounded time. The only comparable walk in the tree, `ReorderStatus.ResolveDefinition`, has **no guard at all**, and a cyclic chain can already arrive through canonical import, which does not pass through `InventoryService.EnsureStockGroupParentValid`. That is a live hazard on a different code path and is not closed by this row. |

---

### IV-37 · The "Ledger" rung is the sales/purchase ledger, and the Group rung is THAT ledger's ancestry — never the party's
**LOW** · Class **C** · Area **TAX** · ruling-9 category **(a)** — corpus silent, ours by design

| | |
|---|---|
| **What the customer experiences** | GST details typed on a **party** ledger are never consulted for the rate, on either order. A dealer who set a rate on a customer master — which the corpus shows being done — finds it inert. |
| **What we invented** | The two ledger-side rungs read `salesPurchaseLedger` only: the rate rung takes its `SalesPurchaseGst`, and the Accounting Group rung climbs **that** ledger's `GroupId`. No party ledger is reachable from `ResolveRate`'s signature at all. **[code]** |
| **What Tally does** | **Unresolved.** The vendor writes *"Ledger (sales/purchase)"* once and never addresses the party case. |
| **Citation** | **[web]** as above, one parenthetical. **[corpus]** sets GST details on party ledgers too (BOOK PDF pp.226 / 239 / 242) but never says they are read for the rate — relayed, see the block header. |
| **How it got in** | Chosen because `ResolveRate`'s signature already carried exactly the sales/purchase ledger, and because `GstReportSupport.ResolveValueLedger` **deliberately excludes** the party ledger under a documented rule that says the exclusion is load-bearing (a party carries no `SalesPurchaseGst`). Reading the party would have contradicted a rule already locked elsewhere in the tree. |
| **Fix** | Leave it unless a source resolves the party case. If one does, note that changing it means widening `ResolveRate`'s signature, which reaches every caller. |

---

### IV-38 · A non-Taxable taxability declared at ONE rung short-circuits the whole walk
**MEDIUM** · Class **C** · Area **TAX** · ruling-9 category **(a)** — corpus silent, ours by design

| | |
|---|---|
| **What the customer experiences** | An item marked **Exempt** makes the line non-taxable even when the sales ledger below it on the walk carries a live 18% rate — and, on a `LedgerFirst` book, an **Exempt sales ledger** does the same to a taxable item. The walk stops; it does not look for a rate further down. |
| **What we invented** | `ResolveBase` returns `RateResolution.NonTaxable(rung.Taxability)` the moment a rung reports `!IsTaxable`, before testing whether that rung supplies a rate. **[code]** |
| **What Tally does** | **UNKNOWN.** No source states whether a non-Taxable taxability at one level terminates the hierarchy or is merely one more empty rung. |
| **Citation** | **[web]** hierarchy page — states only *"stopping at the first master that carries the detail"*, which does not distinguish "carries a rate" from "carries a taxability". **Corpus: UNREACHED** (relayed). |
| **How it got in** | Preserved deliberately from the pre-T0-4 resolver, because the alternative would have quietly redefined the existing `Exempt_item_short_circuits_to_non_taxable` and `Non_taxable_lines_attract_no_tax` tests instead of changing behaviour honestly. 🔴 **Labelled in the resolver's own doc as OURS** — this is precisely the failure mode that produced the invented "most-granular-wins (DP-6)" comment IV-1 condemns, and it was labelled this time rather than asserted. |
| **Fix** | Leave it and keep the label. **Note the second-order consequence, which is the reason this is MEDIUM and not LOW:** it is this short-circuit that makes the document-title flip in `docs/full-clone-census.md` §1.3 item 15's second open R12 question reachable — an untaxed voucher's statutory title is re-resolved live and has no posted anchor. |

---

### IV-39 · The HSN and rate lookups walk INDEPENDENTLY, so a rung carrying an HSN but no rate does not stop the rate walk
**LOW** · Class **C** · Area **TAX** · ruling-9 category **(a)** — corpus silent, ours by design

| | |
|---|---|
| **What the customer experiences** | Nothing yet. **This divergence is not exercisable today** — the HSN half of the hierarchy is slice S5 and `SourceOfHsnSacDetails` still has no reader. It is recorded now because the decision has been taken and would otherwise be taken again, silently, by whoever writes S5. |
| **What we invented** | The rate walk falls **through** a rung whose block declares an HSN/SAC and a taxability but no `RateBasisPoints`, to the rung below it. Once S5 lands, the HSN lookup will walk its own order independently, so one line can take its HSN from one master and its rate from another. **[code / design]** |
| **What Tally does** | **Not stated.** Two separately-selectable F12 toggles (*Source of GST Rate Details in Transactions* and *Source of HSN/SAC Details in Transactions*) **imply** two independent walks, and CGST **Rule 46** treats **(g)** HSN and **(l)** rate of tax as distinct mandatory particulars, which is consistent with it. Neither says it. |
| **Citation** | **[web]** `help.tallysolutions.com`, "How to Set Up GST Rate and HSN/SAC Details in TallyPrime" — names both toggles and their F12 location. **[statute]** CGST Rule 46(g), (l). **Neither is a statement about walk independence.** |
| **How it got in** | Recorded as a design decision in the T0-4 design with its own "record now, test in S5" note. |
| **Fix** | Nothing to fix now. **S5 must implement it deliberately and pin it**, or the first HSN-bearing rate-less rung will settle the question by accident. |

---

### IV-40 · Cess, reverse charge and ITC-eligibility do NOT walk the hierarchy — a rate resolved above the Stock Item carries none of them
**HIGH** · Class **A** · Area **TAX** · ruling-9 category **(b)** — a deliberate NARROWING of an attested screen

| | |
|---|---|
| **What the customer experiences** | ~~**Measured, with literals.** An item declaring ad-valorem Compensation Cess at **1200 bp**, sold under a sales ledger declaring 18% and **no cess fields**, on a taxable value of **₹10,000.00**: cess is **₹1,200.00** on a pre-v51 book (`StockItemFirst` — the ledger is not the winning rung) and **₹0.00** on a v51+ book (`LedgerFirst` — the ledger wins the walk and its block has no cess to give). The rate is 1800 bp either way. **A statutory levy silently disappears on a book shape that is ordinary.**~~ 🟡 **THIS HALF IS CLOSED 2026-09-04 — assumption A-QA, and the struck figures are kept as the record of what it was.** The same fixture now yields **₹1,200.00 under BOTH orders** (`GstCessIndependentWalkTests.A_rung_silent_on_cess_does_not_suppress_a_lower_rungs_declared_cess`, and the inverted `LedgerFirst` row of `GstWinningBlockTests.The_source_order_no_longer_decides_which_master_supplies_the_cess`). 🔴 **WHAT IS UNCHANGED, AND IT IS THE LARGER HALF:** a rate resolved at the Accounting Group, Stock Group or Company rung **still can never carry cess and can never fire reverse charge at all**, because those three rungs are typed `MasterGstDetails`, which has no such fields; and **reverse charge and §17(5) ITC-eligibility do not walk at all**, because they are read off `ResolveDetailBlock`, which A-QA deliberately did not touch. |
| **What we invented** | `MasterGstDetails` carries four members (HSN/SAC, Taxability, Rate, Supply Type). `StockItemGstDetails` additionally carries `CessApplicable`, `CessValuationMode`, `CessRateBasisPoints`, `CessPerUnit`, `CessRspFactorMillis`, `RetailSalePrice`, `ReverseChargeApplicable`, `ItcEligibility` and `BlockedCreditCategory`. `ResolveDetailBlock` returns the **first rung declaring a block**, and yields `null` for the three rungs typed as the narrow record — so cess and reverse charge follow the rate's rung and get nothing when that rung is one of the three. **[code]** |
| **What Tally does** | The reference product does **not** narrow this way. Its **GST Classification** screen carries *Cess*, *"Is reverse charge applicable"* and *"Is ineligible for input credit"* alongside the rate. **This is the difference between category (a) and category (b), and it is why this row is class A and not class C: the corpus is NOT silent here, it attests the wider shape and we ship the narrower one.** |
| **Citation** | **[corpus]** BOOK PDF **p.234** (printed 230) — the GST Classification screen and its fields. *(Relayed; the corpus is not on disk this pass — see the block header.)* |
| **How it got in** | **Forced by an earlier ruling and recorded as a decision so it is not misread as an oversight.** Adding any of the nine fields to `MasterGstDetails` is a schema change, i.e. an escalation, not something a rate-resolution slice may take. The narrowing was the honest alternative to a silent nullable. 🔴 **What was NOT forced, and is the live half:** the ₹1,200 → ₹0.00 move above is a consequence of honouring `LedgerFirst`, and it is **pinned by test but not decided** — it is the first of the two **OPEN R12 questions** in `docs/full-clone-census.md` §1.3 item 15. |
| **Fix** | ~~**Needs a user ruling before any code moves.**~~ 🟡 **SHAPE (iii) IS BUILT, 2026-09-04, AS A LABELLED ASSUMPTION AND NOT AS A RULING** — the user directed the track to proceed without stopping, so the assumption ships one-line reversible at `GstService.CessWalksIndependentlyOfTheRate` and **the R12 question stays open**. Three shapes existed and they are not equivalent: (i) accept the narrowing and warn at capture time; (ii) let cess and reverse charge walk their **own** hierarchy, which means widening `MasterGstDetails` — a schema change, **NOT taken** (three sibling tracks share this v52 base) and therefore the reason the narrow-rung half above is still open; (iii) keep the rate on the winning rung but let cess fall through to the first rung that declares any. **(iii) is what shipped, and the invariant objection it used to carry is narrower than it read:** the RATE walk is still one walk with one winning block and is byte-identical (asserted in the same test), `ResolveDetailBlock` is untouched so reverse charge and ITC still follow the rate's rung, and what changed is only that a rung which says **nothing** about cess no longer terminates the cess question on the strength of having answered the rate question. ⚠️ **Do not "fix" the remainder by reverting the winning-block rule** — before that rule existed, a line could be RATED off the ledger while its cess and its RCM category were read off the item, which is a worse defect and is what the chain closed. ⚠️ **A FORCED LIMIT, not a choice:** `CessApplicable` is a non-nullable `bool`, so "cess does NOT apply here" is indistinguishable from silence and both let the walk continue. Separating them is the same schema escalation. |

---

### IV-41 · The per-master GST source selector is not built — TallyPrime's four values collapse into one nullable
**MEDIUM** · Class **B** · Area **TAX** · ruling-9 category **(b)**

| | |
|---|---|
| **What the customer experiences** | An operator cannot say, on a given master, *where* its GST details should come from. TallyPrime's per-master field takes four values — **Specify Details Here**, **As per Company/Group**, **Use GST Classification**, **Specify in Voucher**. We collapse the first three into "block present" versus "block absent" (`MasterGstDetails?`) and **cannot express the fourth at all**: there is no way to say *"ask me at voucher entry"*. |
| **What we invented** | A nullable block standing in for a four-valued enum. **[code]** |
| **What Tally does** | Offers the four-valued field on the master's GST Details screen. |
| **Citation** | **[web]** `help.tallysolutions.com`, "How to Set Up GST Rate and HSN/SAC Details in TallyPrime". |
| **How it got in** | Recorded as a design decision rather than left implicit in the nullable, because the collapse is invisible from the type. Building it is a schema change. |
| **Fix** | An escalation, not a slice: it needs a new persisted member on every master that carries GST details, and *Specify in Voucher* additionally needs a voucher-entry surface that does not exist. |

---

### IV-42 · No GST Classification master exists — one of the corpus's own five methods of applying GST
**MEDIUM** · Class **B** · Area **TAX** · ruling-9 category **(b)**

| | |
|---|---|
| **What the customer experiences** | A business that maintains its rates as reusable **classifications** and points masters at them cannot do so. Every master carries its own copy, so a rate revision must be re-typed everywhere it was typed. |
| **What we invented** | Nothing — this is an **absence**, which is why it is class B. What was invented is the *record*: until now its absence was not catalogued as a divergence anywhere. |
| **What Tally does** | Ships GST Classification as a master and as a **template applied INTO** another master — *"tax details disappear from the GST Details screen as the details are taken from the GST Classification"*. |
| **Citation** | **[web]** `help.tallysolutions.com` classifications page. **[corpus]** BOOK PDF pp.232-235; and the corpus's own five-methods list names it as method 5 (GSTN PDF p.121). *(Relayed.)* |
| **How it got in** | Never built. ✅ **Correctly left OUT of the five-level walk** — it is a template, not a rung, and the existing plan ruling on that point is right. **What was wrong was treating "correctly out of the walk" as "correctly absent from the product".** Those are different claims and only the first was ever argued. |
| **Fix** | A master, a screen and an application rule. Sequence it with the capture slices (S3/S4), since a classification that nothing can be pointed at is worth nothing. |

---

### IV-43 · No per-master dated rate history, and no GST Rate Setup report
**MEDIUM** · Class **B** · Area **TAX** · ruling-9 category **(b)**

| | |
|---|---|
| **What the customer experiences** | **Two absences with one cause.** (1) A master carries **one undated rate**. TallyPrime keeps a per-master *"GST Rate & Related Details (History)"* with an *"Applicable from"* date, so a rate revision is a new dated row and old vouchers keep resolving at the historic rate. We have a **company-level** `GstRateHistory` only, and it keys its override on a hard-coded item-first HSN pick that contradicts the walk it now sits on — census **T0-20**. (2) There is no **GST Rate Setup** report: the vendor's bulk-edit surface across Ledgers, Stock Items, Ledger Groups and Stock Groups, with its *"GST Rate Details Not Specified"* section that shows an operator which masters are still blank. With the hierarchy now live across five rungs, *"which masters declare a rate"* is exactly the question an operator cannot answer. |
| **What we invented** | A single undated `RateBasisPoints` per master, plus a company-level dated override that resolves its key off a different rule than the rate walk. **[code]** |
| **What Tally does** | Per-master dated history, and a report that lists and bulk-edits GST rate details across all four master kinds. |
| **Citation** | **[web]** `help.tallysolutions.com` — the GST Rate Setup page and the per-master history screen. |
| **How it got in** | The company-level history shipped in Phase 9 for a different purpose (the 22-Sep-2025 revisions) and was never reconciled with a per-master hierarchy that did not exist yet. The Rate Setup report was never scoped. |
| **Fix** | Two separable items. The **report** is buildable today and gets more valuable with every rung the walk gained. The **per-master history** is a schema change and should be sequenced with, not before, the fix for T0-20 — because shipping dated rows per master while the override still picks its HSN item-first would put two inconsistent dated resolutions in one book. |

---

## 2B. ADDED 2026-09-04 — THE TWELVE THINGS THE WAVE-2 VERIFICATION PASSES FOUND WE OWN

**Why these are a separate block, and why the block matters more than its size.** Three read-only A14 verification
passes ran on 2026-09-04 across all sixteen census areas. Their **verified** and **divergent** findings went into
`docs/full-clone-census.md` §1.3 items **16**, **17** and **18** and into the gap register. **What lands HERE is
the third bucket, and under ruling 9 it is the one that can never be undone: behaviour the admissible sources do
not settle, which therefore ships as a DOCUMENTED DIVERGENCE LABELLED AS OURS and can never join the compared
set.** §2's counts are **NOT re-cut**, for the reason §2A gives: the register now holds **55 rows, of which 51 are
open** (39 open at HEAD before this block, plus these twelve, none of which is fixed).

🔴 **READ THIS BEFORE ANY ROW, BECAUSE IT CHANGES WHAT SOME OF THESE ROWS MEAN AND ONE OF THEM IS A TRAP.**
`tally/` — the git-ignored corpus R7 names as the fidelity authority — **held ZERO PDFs on 2026-09-04**, measured
independently by three agents, in the base tree and in all seven worktrees. So there are **two different reasons a
row can be here**, and they are not interchangeable:

- **Category (a) — CORPUS SILENT, ours by design.** A reader opened the source and the source says nothing.
  **Permanent under ruling 9.** Only rows whose silence was established when the corpus was readable, or whose
  authority is a law or a vendor page that demonstrably does not cover the point, may carry this.
- **Category (b) — the source attests X and we deliberately ship a narrower Y.** Also permanent, but a different
  claim: the attestation exists and we departed from it.
- 🔴 **NEITHER — "UNREACHED".** *"The corpus might settle this and I could not look"* is **not silence**, and
  writing it as silence would manufacture a **permanent, unfalsifiable** verdict out of a **temporary** tooling
  failure. Those rows are **NOT in this block**; they are in `docs/full-clone-census.md` **§6a**, each with what
  would settle it, and they come back the moment the PDFs do.

**Index of this block:**

| # | Sev | Class | Area | Ruling-9 category | Item |
|---|---|---|---|---|---|
| **IV-44** | MEDIUM | B | TAX | **(a)** | The ESI contribution BASE is uncapped actual wages — and a secondary summary points the other way |
| **IV-45** | LOW | C | TAX | **(a)** | ESI rounding ceilings each side independently, on an in-code "Rule 51" citation that is currently an assertion |
| **IV-46** | MEDIUM | B | TAX | **(a)** | Statutory bonus — every figure, and a pro-rating rule the code labels as our own |
| **IV-47** | MEDIUM | B | TAX | **(a)** | Gratuity — every figure, and a provision that accrues BEFORE vesting |
| **IV-48** | HIGH | A | TAX | **(a)** | Five professional-tax state slab tables with no citation, under an "A14-verified" label |
| **IV-49** | LOW | C | TAX | **(a)** | The four EPFO conditions for the reduced 10% EPF rate are not encoded — a free config toggle |
| **IV-50** | LOW | C | VOUCHER | **(a)** | Attendance is deliberately not seeded while its enum ordinal is deliberately retained |
| **IV-51** | MEDIUM | B | UX | **(b)** | Our `F10` opens an "Other Vouchers" menu; the attested `F10` lists **all** vouchers or masters |
| **IV-52** | LOW | C | UX | **(a)** | Bare `E` opens the export panel — an unattested third binding beside two attested chords |
| **IV-53** | LOW | C | UX | **(a)** | Bare `P` opens print preview — no source attests a bare-`P` print |
| **IV-54** | LOW | C | UX | **(a)** | The SORT half of `Alt+F12` is ours; only the filter half is attested |
| **IV-55** | HIGH | A | BANKING | **(a)** | The whole of Banking — ten capabilities with no admissible source of any kind |

---

### IV-44 · The ESI contribution BASE is uncapped actual wages, and this is a MONEY question we could not settle
**MEDIUM** · Class **B** · Area **TAX** · ruling-9 category **(a)** — silent in every source reached

| | |
|---|---|
| **What the customer experiences** | For a member who starts a contribution period at ₹20,000 and rises to ₹25,000 mid-period, we charge 0.75% / 3.25% on **₹25,000**. |
| **What we invented** | `EsiContribution`'s doc asserts contribution is charged *"on the **actual** wages with **no ₹21,000 cap** on the base"*. |
| **What Tally does / what the law says** | **Unknown.** ESIC's `/contribution` and `/coverage` pages state the **coverage** ceiling and say **nothing** about the base once a covered member's wages rise mid-period. |
| **Citation** | **[web]** `esic.gov.in/contribution` and `/coverage`, both read 2026-09-04. |
| **How it got in** | Chosen when the ceiling was read as a coverage test rather than a base cap; never revisited. |
| **Fix** | 🔴 **DO NOT TREAT THIS ROW AS VERIFIED IN EITHER DIRECTION.** A secondary summary of **Rule 50** states that where wages exceed the limit after a period starts, contribution is paid *"on the wage limit prescribed"* — **which would contradict our rule**. That summary is **NOT recorded as a source** (aggregated, inadmissible under R7). **What settles it: the literal text of ESI (Central) Rules 1950, Rule 50 — see census §6a row U-B, which also records the two retrieval routes that failed and the one that would work.** |

---

### IV-45 · ESI rounding is a CEILING applied to each side independently, on an in-code citation that is currently an assertion
**LOW** · Class **C** · Area **TAX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | On ₹17,500 the two sides round to 132 + 569 = **701**, not 700. |
| **What we invented** | `CeilRupee`, and the deliberate refusal to compute 4% and then split it. |
| **What Tally does / what the law says** | **Unretrieved.** The code cites *"ESI Central Rules 1950, Rule 51"*. |
| **Citation** | **[in-code assertion]** — 🔴 **and that is the finding. Rule 51 could not be retrieved** (census §6a, U-B). The rule is *plausible* and the worked example is self-consistent, but **a citation nobody has opened is an assertion**, and this project has already had to strip such citations out of shipped code once. |
| **How it got in** | Written from the rule's reputation rather than its text. |
| **Fix** | Read Rule 51 and either confirm the citation or replace it. **OURS until then.** |

---

### IV-46 · Statutory bonus — every shipped figure, and a pro-rating rule the code itself labels as our own
**MEDIUM** · Class **B** · Area **TAX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | Bonus computed on §2(13)/§8 eligibility **₹21,000**, §8 **30 days**, the §12 calculation ceiling **₹7,000**, the §10–§11 band **8.33%–20%**, the §10 floor **₹100**, and a pro-rating rule. |
| **What we invented** | All of the above as shipped constants — **and the pro-rating rule explicitly, which the code annotates "(DP-4)", i.e. our own design decision rather than a statutory one.** |
| **What Tally does / what the law says** | **Unretrieved.** |
| **Citation** | **none** — the Payment of Bonus Act 1965 could not be reached: `labour.gov.in` 403s and **`indiacode.nic.in` was down** throughout (census §6a, U-C). |
| **How it got in** | Figures written from general knowledge of the Act at a time when the source was assumed reachable. |
| **Fix** | Retrieve the Act and compare figure by figure. **Until then all of it is OURS** — recorded so that nobody reads familiar-looking numbers as verified ones. |

---

### IV-47 · Gratuity — every shipped figure, and a provision that accrues BEFORE vesting
**MEDIUM** · Class **B** · Area **TAX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | Gratuity on §4(2) **15/26**, the **≥6-month round-up**, §4(1) **five-year** vesting and the §4(3) cap **₹20,00,000** — **and the provision accrues for all active members *before* vesting.** |
| **What we invented** | All the figures, **and the before-vesting accrual explicitly: the code labels it "(DP-4)", ours.** |
| **What Tally does / what the law says** | **Unretrieved** — same hosts, same failure as IV-46. |
| **Citation** | **none** (census §6a, U-C). |
| **How it got in** | As IV-46. |
| **Fix** | Retrieve the Payment of Gratuity Act 1972. **The before-vesting accrual is a genuine accounting choice and may well survive verification — but it must be recorded as a choice, not inherited as a fact.** |

---

### IV-48 · Five professional-tax state slab tables ship as a live monthly deduction with NO citation, under an "A14-verified" label
**HIGH** · Class **A** · Area **TAX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | A monthly salary deduction driven by five seeded slab tables — Maharashtra men, Maharashtra women (annotated *"(2023 amendment)"*), Karnataka, West Bengal — including a **February over-charge** and a ₹25,000 women's exemption. |
| **What we invented** | Every one of those tables, the February rule and the exemption. |
| **What Tally does / what the law says** | **Unknown from anything in the repo.** |
| **Citation** | 🔴 **NONE — and the file's doc comment says *"A14-verified FY 2025-26"*. A14 ran the 2026-09-04 verification pass and reports there is nothing here to verify against**: no URL, no Act section, no page, for any of the five tables. **The only sourced figure in the file is the ₹2,500 annual cap, correctly attributed to Article 276(2) of the Constitution.** |
| **How it got in** | Phase 8 slice 6, under a label that asserted verification instead of performing it. |
| **Fix** | **Remove the label or source the tables.** This is **the T0-6 pattern one step worse** — T0-6's rates at least cited something inadmissible; these cite nothing while carrying an agent's name as warrant. **Mitigations are real but partial:** the tables are per-company editable and the annual cap bounds even a mis-configured table, so exposure is **capped at ₹2,500 per employee per year, not unbounded.** Gap register **T0-25**. |

---

### IV-49 · The four EPFO conditions for the reduced 10% EPF rate are not encoded — the rate is a free config toggle
**LOW** · Class **C** · Area **TAX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | Any company can select the reduced 10% EPF rate. |
| **What we invented** | Accepting it as a configuration choice with **no eligibility rule at all**. |
| **What Tally does / what the law says** | EPFO's own contribution sheet lists **four** qualifying conditions (fewer than twenty employees; a BIFR sick company; accumulated losses ≥ net worth; jute / beedi / brick / coir / guar-gum). **It does not say what software must do about them.** |
| **Citation** | **[web]** EPFO *"PRESENT RATES OF CONTRIBUTION"*, read 2026-09-04 with `pdftotext -layout` **and** `-raw`, outputs compared and in agreement. |
| **How it got in** | A deliberate simplification. |
| **Fix** | Optional. **Recorded because it is a simplification, not a divergence** — it under-collects only if an operator sets it wrongly — but the distinction between *"we chose not to enforce"* and *"nobody noticed"* is exactly what this register exists to preserve. |

---

### IV-50 · Attendance is deliberately NOT seeded while its enum ordinal is deliberately RETAINED
**LOW** · Class **C** · Area **VOUCHER** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | No predefined Attendance voucher type. |
| **What we invented** | Decision **D24 option B** — do not seed it — **while keeping the enum member**, because `voucher_types.base_type` persists the **ordinal** and removing it would renumber `Payroll` and silently reinterpret every stored Payroll type. |
| **What Tally does** | **Unknown.** The official shortcut page does not list Attendance. `docs/tally-feature-catalog-verification-report.md`'s claim about it is `[model-knowledge]`-adjacent, and on the Physical Stock evidence (census §1.3 item 16) **that report item is not reliable and must not be used as a source.** |
| **Citation** | **[web]** the vendor shortcut page, read 2026-09-04 — **the source is silent on whether a predefined Attendance type must exist.** |
| **How it got in** | An argued decision, documented in-code at the point of decision. |
| **Fix** | None owed. 🔴 **Recorded here specifically because the in-code comment is EXEMPLARY and must not be mistaken for a sourced claim** — the ordinal-safety reasoning is ours, and reasoning that good is exactly the kind a later reader promotes to fact. |

---

### IV-51 · Our `F10` opens an "Other Vouchers" menu; the attested `F10` lists ALL vouchers or masters
**MEDIUM** · Class **B** · Area **UX** · ruling-9 category **(b)** — attested, and we ship narrower

| | |
|---|---|
| **What the customer experiences** | `F10` offers a short "Other Vouchers" list. |
| **What we invented** | The narrowing. |
| **What Tally does** | `F10` = *"view list of all vouchers **or masters**"* — a far wider object. |
| **Citation** | **[web]** the vendor keyboard-shortcut page, read 2026-09-04, asked directly. |
| **How it got in** | Built to serve the voucher-type menu it was needed for. |
| **Fix** | Widen the object. **Category (b), not (a): the attestation exists and we departed from it** — which is a different and more actionable claim than silence. |

---

### IV-52 · Bare `E` opens the export panel — an unattested third binding beside two attested chords
**LOW** · Class **C** · Area **UX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | Pressing `E` on an exportable page opens the export panel. |
| **What we invented** | The bare-letter binding. Its own code comment states the intent: *"Accepts both the bare E and Alt+E (the header hint reads \"E: Export\"). No Ctrl."* |
| **What Tally does** | Attests **two** chords and neither is bare `E`: `Ctrl+E` (current object) and `Alt+E` (bulk menu). |
| **Citation** | **[web]** the vendor shortcut page and the Export-Data page, read 2026-09-04. |
| **How it got in** | The cascade's bare-letter convention, which is itself ours (**IV-30**). |
| **Fix** | Defensible under that convention and **may stay** — but it must be **logged as ours rather than counted as fidelity**, and it must not be confused with the real defect on the same handler: **`Ctrl+E` is INERT and `Alt+E` is doing `Ctrl+E`'s job**, which is gap-register **T2-17** and census row 13.5. |

---

### IV-53 · Bare `P` opens print preview — no source attests a bare-`P` print
**LOW** · Class **C** · Area **UX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | Pressing `P` on a printable page opens print preview. |
| **What we invented** | The bare-letter binding. |
| **What Tally does** | Attests `Ctrl+P` — *"To print the current voucher or report"* — **which we also ship correctly**, and which is one of the four AGREES rows in census §1.3 item 18. |
| **Citation** | **[web]** the vendor shortcut page, read 2026-09-04. |
| **How it got in** | As IV-52. |
| **Fix** | As IV-52 — log as ours, do not count as fidelity. *(There is a second bare-`P` arm, a menu quick-jump to Profit & Loss; the two do not collide because the print arm is checked first and requires a printable page.)* |

---

### IV-54 · The SORT half of `Alt+F12` is ours; only the filter half is attested
**LOW** · Class **C** · Area **UX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | `Alt+F12` opens a combined sort-and-filter panel. |
| **What we invented** | The **sort** half. |
| **What Tally does** | `Alt+F12` = *"To filter data in a report, with a selected range of conditions"* — **filter only**, on this page. |
| **Citation** | **[web]** the vendor shortcut page, read 2026-09-04. |
| **How it got in** | Two related report operations were merged onto one panel. |
| **Fix** | None urgent. **Recorded because the FILTER half is one of only four verified-AGREES rows in the whole reports/printing area, and a reader must not let the verified half launder the unverified one.** |

---

### IV-55 · The whole of Banking ships with no admissible source of any kind
**HIGH** · Class **A** · Area **BANKING** · ruling-9 category **(a)** — silent by necessity

| | |
|---|---|
| **What the customer experiences** | BRS mechanics, cheque layout, deposit slip, post-dated-cheque handling and the Banking menu's row set — census rows **8.1 – 8.10**, ten capabilities. |
| **What we invented** | **All of it**, in the sense that nothing in it has ever been compared to anything. |
| **What Tally does** | **Unknown and, today, unknowable from the sources this project admits.** |
| **Citation** | **none, and the reason is structural rather than an omission: nothing in Area 8 is law-shaped**, so unlike TDS/TCS/PF/ESI there is **no official-web substitute** for the corpus — and the corpus is empty. **Area 8 contributed ZERO verified rows to the 2026-09-04 passes and could not have contributed any.** |
| **How it got in** | Built to a reasonable reading of what a banking module does. |
| **Fix** | 🔴 **Nothing an agent can do. This row is here so that Area 8's absence from the verified set is never read as an oversight, and so that it is the FIRST place to look the day `tally/` is restored (U-0).** Ten capabilities in one row is the largest single block of unverified behaviour this register holds. |

---

## 2C. ADDED 2026-09-04 (LATER THE SAME DAY) — THE SIX THINGS THE **WAVE-3** VERIFICATION PASSES FOUND WE OWN

**Why this block is SMALL, and the smallness is the finding.** Five read-only A14 wave-3 passes ran on 2026-09-04
across census areas **1, 2, 3, 6 (GST), 9, 10, 11, 12 and 13** — 98 rows examined, **78 capabilities compared**
against cited sources. Their verified and divergent findings went into `docs/full-clone-census.md` §1.3 items
**19–23** and into the gap register (**2 new TIER 0, 8 new TIER 1, 16 new TIER 2, 3 new TIER 3**). **Only SIX
findings land here.**

🔴 **That ratio — 78 compared, 63 diverging, and only 6 vendor-silent — is the single most useful thing this block
says, and it says it about RULING 14 rather than about us.** Section 2B's twelve rows were written when the
question was *"is the corpus silent?"* and the corpus was gone. **Under ruling 14 the question is *"is the VENDOR
DOCUMENTATION silent?"*, and the vendor documentation turns out to be far less silent than the block above
assumed.** Most of what wave 2 could only call *ours by necessity* in areas 11–13, wave 3 was able to **compare
and find divergent** — which is a strictly better verdict, because a divergence is fixable and silence is not.
⚠️ **Do NOT read that as a licence to re-open section 2B's rows wholesale.** Areas 7 and 8 (payroll statute and
banking) are where 2B's silence rows actually live, and wave 3 did not touch either. **IV-55 in particular stands
exactly as written** — with its *"the day `tally/` is restored"* clause now dead under ruling 14, so the honest
reading of IV-55 is that **Area 8 has no admissible source at all, full stop, not that one is awaited.**

**Category discipline is unchanged and is repeated because it is the whole value of this file.** **(a)** = the
source was opened and says nothing — permanent. **(b)** = the source attests X and we deliberately ship a narrower
Y — also permanent, different claim. **NEITHER** = *"I could not reach a source"*, which is **§6a UNREACHED** and
**must never be written here**; wave 3 filed 4 new §6a rows rather than let a non-retrieval harden into silence.

| # | Sev | Class | Area | Ruling-9 category | Item |
|---|---|---|---|---|---|
| **IV-56** | LOW | C | UX | **(a)** | The two Cost Category allocation-field LABELS — the vendor attests the concept, never the strings |
| **IV-57** | LOW | C | UX | **(a)** | The PLACEMENT of our two BOM switches on the F11 page |
| **IV-58** | MEDIUM | B | UX | **(a)** | Six payroll-statutory switches as TOP-LEVEL F11 rows |
| **IV-59** | LOW | C | REPORT | **(a)** | The Forex Gain/Loss REPORT SURFACE — the vendor documents a Balance-Sheet head, not a report |
| **IV-60** | MEDIUM | B | PRINT | **(a)** | Whether an e-invoiced supply is RENDERING-ELIGIBLE without its IRN and signed QR |
| **IV-61** | LOW | C | DATA | **(a)** | Our backup archive naming and timestamp default — invented, and defensibly better |

---

### IV-56 · The two Cost Category allocation-field LABELS are ours; the vendor attests only the concept
**LOW** · Class **C** · Area **UX** · ruling-9 category **(a)** — silent in the source reached

| | |
|---|---|
| **What the customer experiences** | The Cost Category master offers `Allocate Revenue Items` and `Allocate Non-Revenue Items`. |
| **What we invented** | **The exact field strings.** |
| **What Tally does** | The vendor's cost-centre page attests the **concept** — revenue items are incomes and expenses; non-revenue items are assets and liabilities — and **does not quote the field labels**. |
| **Citation** | **[web]** `help.tallysolutions.com/cost-centre-or-profit-centre-tally/`, read 2026-09-04 (§1.3 item 19). |
| **How it got in** | The labels were derived from the attested concept, which is a defensible derivation and is exactly why it must be labelled rather than assumed correct. |
| **Fix** | None. **Recorded so a later reader does not "confirm" these labels off a page that never carried them** — the failure mode that put an unsourced field name into rows 2.2 and 3.7 for three weeks before wave 3 discharged them. |

---

### IV-57 · The PLACEMENT of our two BOM switches on the F11 page is ours
**LOW** · Class **C** · Area **UX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | `_setComponentsBom` and `_defineBomComponentType` appear as rows on the F11 Company Features page. |
| **What we invented** | **Where they live.** The switches themselves are not in question. |
| **What Tally does** | The vendor's **F11 Inventory list does not include a BOM toggle at all**, and the stock-item page puts *"Set Components List (Bill of Materials) in Stock Items"* on the **stock-item F12**. |
| **Citation** | **[web]** `…/company-features-f11-tally/` and `…/manage-stock-item-tally/`, read 2026-09-04 (§1.3 item 19). |
| **How it got in** | Every company-wide inventory switch was collected onto one page. |
| **Fix** | Low priority. ⚠️ **Stated with its exact honest limit, which the pass insisted on: this is NOT enough to call a divergence from the pages read — but it is NOT attested where we put it either.** That is precisely the state category (a) exists to record. |

---

### IV-58 · Six payroll-statutory switches ship as TOP-LEVEL F11 rows; the vendor names only two at that level
**MEDIUM** · Class **B** · Area **UX** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | PF, ESI, Professional Tax, salary TDS, gratuity and bonus each have their own row directly on F11. |
| **What we invented** | **The flat, six-wide top level.** |
| **What Tally does** | The vendor's F11 page names only **`Maintain Payroll`** and **`Enable Payroll Statutory`** at the top level; everything else sits **below Payroll Statutory**. Separately, the same page places *Enable Job Order Processing* and *Enable Cost Tracking* behind **Show All Features** — **our F11 panel shows every row at once** (`MainWindow.axaml:9787-9805`), so the progressive-disclosure mechanism is absent generally. |
| **Citation** | **[web]** `…/company-features-f11-tally/`, read 2026-09-04 (§1.3 items 19 and 23). |
| **How it got in** | Each payroll statute was added as its own toggle as it shipped, with no parent to hang it under. |
| **Fix** | 🔴 **Graded MEDIUM rather than LOW deliberately: this is a genuine navigation-hierarchy divergence on the product's most-visited configuration screen, and it collides with the standing "professional UI hierarchy" preference — never a flat dump.** ⚠️ **Honest limit stated by the pass and kept: the PAYROLL pages themselves were not read.** If they attest a different structure this row becomes category (b), or falls away. **Do not upgrade or retire it without reading them.** |

---

### IV-59 · The Forex Gain/Loss REPORT SURFACE is ours; the vendor documents a Balance-Sheet head, not a report
**LOW** · Class **C** · Area **REPORT** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | A dedicated Forex Gain/Loss report screen. |
| **What we invented** | **The report surface.** |
| **What Tally does** | The vendor's page describes forex gain/loss as an **unadjusted-difference head that appears on the Balance Sheet** and is journalised; it documents **no report screen** for it. ✅ **Our ACCOUNTING AGREES with the vendor** — that half is compared and is inside §1.3 item 21's count. |
| **Citation** | **[web]** `help.tallysolutions.com/account-for-forex-gain-or-loss/`, read 2026-09-04 (§1.3 item 21). |
| **How it got in** | Built alongside Interest Calculation and Budget Variance, which do have surfaces. |
| **Fix** | None. **Recorded because it is the ONE vendor-silent finding among fifteen comparisons in Area 11** — and because the pass could easily have called the whole row DIVERGES and did not. **The accounting is verified; the surface is ours. Keep the two apart.** |

---

### IV-60 · Whether an e-invoiced supply is RENDERING-ELIGIBLE without its IRN and signed QR
**MEDIUM** · Class **B** · Area **PRINT** · ruling-9 category **(a)**

| | |
|---|---|
| **What the customer experiences** | We render and hand over a printed invoice for an e-invoiced supply **without** the IRN and without the signed QR, because `PdfWriter` has no image primitive (**T0-9**, **T2-4**). |
| **What we invented** | **The decision to render it at all.** |
| **What Tally does** | **Unknown from the pages read.** The vendor documents *that* the IRN and QR are printed; it does **not** state what the product does when they cannot be. |
| **Citation** | **[web]** the print/export/share and print-invoices pages, read 2026-09-04 (§1.3 item 22). **This is the entire residual of census row 12.2** — every other question on that row is answered. |
| **How it got in** | The print path has no concept of a document being ineligible to render. |
| **Fix** | 🔴 **This is NOT the same question as T0-9, and conflating them is the trap.** T0-9 is *"we cannot draw the QR"* — a **capability** defect with a known fix (a raster primitive). **IV-60 is *"and therefore, should we refuse?"*** — a **policy** question the vendor does not answer, so it ships as ours either way. **A reader closing T0-9 must not assume IV-60 closed with it.** |

---

### IV-61 · Our backup archive naming and timestamp default are ours — and defensibly better
**LOW** · Class **C** · Area **DATA** · ruling-9 category **(b)** — the source attests a different shape and we departed from it on purpose

| | |
|---|---|
| **What the customer experiences** | Archives are named `Name[_yyyyMMdd-HHmm].apexbak`, with the timestamp **on** by default, so successive backups never collide. |
| **What we invented** | **The naming scheme and the timestamp default.** |
| **What Tally does** | Auto-names the backup `TBK1800_******.***` and raises an **overwrite-or-rename prompt** on conflict. |
| **Citation** | **[web]** `help.tallysolutions.com/backup-restore-company-data-tally/`, read 2026-09-04 (§1.3 item 22). |
| **How it got in** | Deliberate: a timestamped name makes accidental overwrite of the previous backup impossible. |
| **Fix** | 🔴 **None, and this row exists to say so explicitly: record it as INVENTED, not as a defect.** It is filed here rather than in the gap register because the wave-3 pass drew that line itself, and because **a register that files every departure as harm loses the ability to say which departures are harm.** ⚠️ **The rest of row 13.1 IS harm and is filed as T2-30** — no company picker, no multi-company backup, no persisted path. **Do not let this row launder that one.** |

---

### IV-62 · Our export File Format list carries an EIGHTH entry the vendor's does not: `CSV (.csv)` alongside `ASCII (.txt)`
**LOW** · Class **C** · Area **DATA** · ruling-9 category **(b)** — the source attests a different shape and we departed from it on purpose

| | |
|---|---|
| **What the customer experiences** | The export dialog offers **seven** formats. Six map one-for-one onto the vendor's list; the seventh, **`CSV (comma-separated)` writing `.csv`**, has no counterpart in the reference product, and a user who knows that product will look for *ASCII (Comma Delimited)* and find **both** it and a near-twin above it. |
| **What we invented** | **Keeping `Csv` as a format of its own after adding `Ascii`.** The vendor's *ASCII (Comma Delimited)* writes **`.txt`**; ours writes **`.txt`** correctly as `ExportFormat.Ascii`, and `ExportFormat.Csv` (`.csv`) is retained beside it. |
| **What Tally does** | Offers exactly seven File Format entries: *ASCII (Comma Delimited)* `.txt` · *Excel (Spreadsheet)* `.xlsx` · *HTML (Web-Publishing)* `.html` · *JPEG (Image)* `.jpg` · *JSON (Data Exchange)* `.json` · *PDF (Read-only Document)* `.pdf` · *XML (Data Interchange)* `.xml`. **There is no separate CSV entry.** |
| **Citation** | **[web]** two independent official vendor pages, agreeing, read 2026-09-04 in the wave-2 reports/printing pass and recorded verbatim in census row **13.6**. |
| **How it got in** | `Csv` predates the comparison and is depended on by the existing whole-company and master-list export paths. When `Ascii` was added 2026-09-05 (b5, PR #50) the honest options were to *rename* `Csv` — silently changing the extension every existing caller and every user's muscle memory expects — or to add `Ascii` beside it and say so. **The second was chosen and `ExportConfig.cs` states it in the enum's own doc comment**: "Our `Csv` (`.csv`) and `Ascii` (`.txt`) are the SAME vendor entry under two extensions." |
| **Fix** | 🔴 **None proposed; this row exists to LABEL it as ours (ruling 9), not to schedule a change.** If parity ever outweighs compatibility the fix is to drop `Csv` and re-point its callers at `Ascii` — but that is a breaking change to a shipped path and belongs to a user decision, not to a print slice. ⚠️ **Do not "fix" this by deleting the `.csv` extension; the census row it belongs to (13.6) is `PARTIAL` for a different reason entirely — JPEG.** |

---

### IV-63 · The print-configuration panel names NO function keys where the vendor names four — and we declined to invent them
**MEDIUM** · Class **C** · Area **PRINT** · ruling-9 category **(c)** — no admissible source speaks to what OUR keys should do, so nothing was bound

| | |
|---|---|
| **What the customer experiences** | The print-configuration panel offers its settings with **no keyboard accelerators named on them at all**. A user of the reference product reaches for `F8` to change print format, `F9` for paper, `F5` for copies and `F10` for page numbering, and **none of those keys does anything here**. On a keyboard-first product that is a real cost. |
| **What we invented** | 🔴 **Nothing — and that is the entry.** The captions previously *advertised* `F8`/`F9`/`F5`/`F10` while **nothing routed them**, which is worse than silence: it promises a gesture that does not exist. The b5 landing (PR #50) **removed the captions rather than binding the keys**, and added a lock shaped as a SUBSET rule (**advertised ⊆ routed**) so the panel can never again name a key it does not route. |
| **What Tally does** | Puts the print-configuration axes on four separate keys: `F8` **Print Format** (Dot Matrix Format / Neat Mode / Quick-Draft Format), `F9` **paper** (Plain ↔ Pre-Printed), `F5` **number of copies and Type of Copy**, `F10` **starting page number and page ranges**. |
| **Citation** | **[web]** the official vendor print-configuration page, as recorded in census row **12.4**, which corrected that row's own target list on 2026-09-04. |
| **How it got in** | **RULING 14: the `tally/` corpus is gone**, and `help.tallysolutions.com` was not consulted for the *binding* question during the b5 slice. Binding keys without a source would have been a fidelity guess dressed as a feature — and three TallyPrime report keys are **already** squatted by unrelated screens on this product (**IV-28**), so guessing a fourth set is exactly the move that created that entry. |
| **Fix** | 🔴 **A USER DECISION, not an implementation detail (R12).** Two admissible routes: **(1)** supply the vendor page and bind the four keys to the axes it names — but first check each against the app's existing app-wide bindings, because IV-28 records that this product has already lost three such keys; or **(2)** accept shipping **our own** bindings as a documented divergence labelled as ours under ruling 9. **Until one is chosen, silence is the correct state** — the subset lock permits the real work and blocks only the false advertisement. Census row **12.4** carries the same note. |

---

## 2D. ADDED 2026-09-06 — THE ONE THING WAVE 8 / TRACK F3 INVENTED (census row 14.10)

**Scope note.** Wave 8's F3 track built census row **14.10 "WhatsApp sharing of a document"**. Exactly **one**
divergence needed inventing, and it is a keystroke, not a figure. The row's *substance* — that we save the
document and hand over a prepared `wa.me` link instead of sending anything — is **not** in this register,
because it is not a case of building our own behaviour where the vendor publishes one: it is a case of the
vendor's behaviour being **unbuildable here on stated grounds**, declared on the panel's own face and locked by
`WhatsAppShareReachabilityTests.The_realised_notice_says_nothing_is_sent_and_the_file_is_not_attached`. See §6
if that reasoning is later disputed; it belongs with the open questions, not with the inventions.

**🔴 THE SOURCE FOR "UNBUILDABLE HERE", ADDED 2026-09-07 — it was asserted uncited, which is an R7 breach on the
single fact this row's whole scope decision rests on.** `help.tallysolutions.com/share-documents-using-whatsapp-for-business-faq/`
(read 2026-09-07) states: *"To be able to use WhatsApp on TallyPrime, you need to connect a valid mobile number
with Interakt that can receive an OTP and complete WhatsApp Business API registration first"*; that Interakt is
one of Meta's Business Solution Providers; and that *"Using a number that is already in use for an existing
WhatsApp Business Account (Business App) for WhatsApp Business API on-boarding is not allowed by Meta. A user
will have to de-register himself from the WhatsApp Business Account to start using the WhatsApp Business API
features."* A WABA through a commercial BSP is therefore **mandatory** and there is no account-free path — which
is the premise, and it checks out.

### IV-64 · `W` / the "WhatsApp" badge is OUR chord — the vendor puts WhatsApp *under* its Share access point, not on its own key

| | |
|---|---|
| **Severity** | **LOW** — a discoverability divergence, not a figures one. Nothing is mis-stated; a TallyPrime operator simply will not find this where their muscle memory expects it. |
| **What TallyPrime does** | WhatsApp is **not a sibling of E-Mail on its own accelerator**. Per `help.tallysolutions.com/configure-for-print-export-share/` (read 2026-09-06), WhatsApp is one **channel under the same Share access point** as Print / Export / E-mail, sharing one configuration layer (file format; company details; party details; document elements). |
| **What we ship** | A dedicated **`W` / Ctrl+W** chord on any printable page (report or drilled voucher/tax-invoice), opening `Screen.WhatsAppShare` as its own cascading Miller column beside the page — `MainWindow.axaml.cs` (the `Key.W` arm, immediately after the `Key.M` e-mail arm), `MainWindowViewModel.BuildButtonBar()` badge `W` / caption `WhatsApp`. |
| **How it got in** | 🔴 **CORRECTED 2026-09-07 — THE ORIGINAL REASON GIVEN HERE WAS FACTUALLY WRONG, AND IS STRUCK RATHER THAN DELETED so the correction is visible.** ~~"The vendor's own chord could not be reused: our `M` / Ctrl+M is already spent on the shipped e-mail compose column, so nesting WhatsApp under it would have displaced a shipped behaviour rather than added one."~~ **The vendor's chord is `Alt+M`, not `M`, and `Alt+M` IS FREE in this application.** Measured 2026-09-07: `grep -n "Key\.M\b" src/Apex.Desktop/Views/MainWindow.axaml.cs` returns **exactly one** arm, line 1160, and it reads `e.Key == Key.M && vm.IsPrintablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !IsTyping(e)` — it **explicitly excludes Alt**. A repo-wide `grep -rn "Alt+M" src/` returns only the two comments that asserted the collision. So `Alt+M` was available and displaces nothing; `W` was taken to avoid a conflict **that does not exist**. What remains true: `W` itself was measured free (`grep -n "Key.W\b"` → zero hits) before it was taken. |
| **Fix** | 🔴 **A USER DECISION (R12), and the more faithful option is the one we did NOT take — at NO cost, which the original framing above concealed.** Route **(1)**, recommended: put the Share access point on the vendor's own **`Alt+M`** as a **channel picker** (E-mail / WhatsApp), matching the vendor's shape *and its keystroke* exactly, and retire `W`. This costs nothing — `Alt+M` is unclaimed, `M`/Ctrl+M keeps the e-mail compose column it has today, and no shipped behaviour moves. Route **(2)**: keep `W` as a documented divergence. The panel and its view model are already channel-shaped and would move under a picker without change; only the key arm and the button-bar entry would be retired. **The user should be told route (1) is free, not merely "more faithful".** |

---

### IV-65 · Security Control is a control over the KEYBOARD, not over the FILE — and its Password Policy settings are ours

| | |
|---|---|
| **Severity** | **MEDIUM** — no figure moves, but the row's NAME over-promises. "Security Control · present" reads as data protection, and what shipped is access control between colleagues at one machine. The divergence is recorded because a customer who believed otherwise would be relying on something that is not there. |
| **What TallyPrime does** | Two default security levels — vendor, verbatim: *"TallyPrime provides two default security levels: owner and data entry operator."* — reached at *"Press **Alt+K** (Company) > **Users and Passwords**"*, with the level captions *"Use Basic Facilities of"*, *"Days Allowed for Back Dated Vouchers"*, *"Cut-off date for Backdated vouchers"* and *"Disallow/Allow the following facilities"* over **Full Access · Create · Alter · Display · Print · Preview**; plus *"Press **Alt+K** (Company) > **Password Policy"***, which *"can enable supplementary choices for passwords, including enhancing password strength and setting password expiration intervals"* (`help.tallysolutions.com/manage-users-in-tallyprime/`, read 2026-09-07). Data-at-rest protection is a **separate** vendor feature (the company data vault, census 16.1), not part of this one. |
| **What we ship** | All of the above, cloned: the Alt+K route, both screens, the two level names verbatim, the six access types, the three level captions, the Disallow/Allow list, and the F12 *"Use User Access Control"* gate. Passwords are stored as **one-way PBKDF2-HMAC-SHA256 verifiers** (per-user 128-bit random salt, 600,000 iterations stored alongside, 256-bit key, `FixedTimeEquals` comparison) in `company_users.password_hash` at schema v56 — see `Apex.Ledger.Security.PasswordHash`. |
| **THE DIVERGENCE — limb (a): the architectural limit** | 🔴 **A local desktop application storing its users in its own SQLite file cannot enforce access control against anyone who can read that file.** Ours cannot, and neither can the reference product's — this is a property of the architecture, not a defect in either. It is stated **on the screen**, verbatim, in `SecurityControl.ArchitecturalLimitNotice`, and a realised-visual-tree test (`SecurityControlScreenTests`) requires it to be visible. The row that WOULD address file access is census 16.1, whose page-encryption half is blocked on a user decision. |
| **THE DIVERGENCE — limb (b): the Password Policy knobs are OURS** | The vendor names the screen and its two **capabilities** but publishes **no field captions and no defaults** for them. So the shipped **"Minimum length"** and **"Expire after (days)"** are this application's concretisation of "password strength" and "expiration intervals", not cloned captions. Both default OFF. The screen says so itself in `PasswordPolicyViewModel.SourceNoticeText`, pinned by a test — rather than presenting invented captions as attested ones. |
| **THE DIVERGENCE — limb (c): the rules are RECORDED, not yet ENFORCED** | 🔴 **Nothing in `src/` consults a security level before opening a screen, and there is no sign-in step.** `SecurityLevel.IsAllowed`, `SecurityLevel.AllowsVoucherDate` and `SecurityControl.Authenticate` all decide correctly and are tested, and **none of the three has a caller outside the security screens and the tests.** So an operator can create users, assign levels and write Disallow rules — and be blocked by none of them. That is stated **on the screen**, verbatim, in `SecurityControl.EnforcementNotice` and pinned by a test, because an operator who restricted a report and was not told would believe it was restricted, which is worse than having no screen at all. **This is why census 16.2 is graded PARTIAL and not COMPLETE.** |
| **THE PRODUCT CONSEQUENCE — say it plainly** | 🔴 **A forgotten Owner password locks the company irrecoverably.** There is no reset, no master key and no back door, because each would be the recoverable storage a password verifier exists to refuse. `SecurityControl` keeps at least one Owner user in EXISTENCE (the last one cannot be deleted **or demoted**), but nothing can recover that Owner's forgotten password. The reference product is in the same position and says so: if both the Admin and the user password are forgotten the only remedy is to *"restore earlier backup in which you have not mentioned the password"* (`help.tallysolutions.com/tally-prime/access-control-data-security/data-security-faq/`). The warning is on both screens, verbatim, in `SecurityControl.LockoutWarningNotice`. |
| **Fix** | Limb (a) is not fixable at this layer and is not a defect — it is closed by census **16.1** if and when the user rules on the native-encryption dependency. Limb (b) closes the moment a source attests the actual field captions; until then the honest smaller thing is shipped and labelled. |

---

### IV-66 · Split Company Data: the two new companies must be NAMED, because our store keys a book by its file name

| | |
|---|---|
| **Severity** | **LOW** — a naming divergence, not a figures one. The books themselves are the vendor's ranges exactly. |
| **What TallyPrime does** | Splitting produces new companies **without asking for names**: its company list is keyed by an opaque **company number**, with the display name a payload of the entry — <i>"TallyPrime will create a new company by assigning a new company number and retaining the existing company name"</i> (`help.tallysolutions.com/tallyvault-for-company-tally/` and `…/data-security-faq/`, read 2026-09-07). Both halves can therefore carry the original name. |
| **What we ship** | The Split Data panel asks for **one name per new book**, defaulted to `"<name> (to dd-MMM-yyyy)"` and `"<name> (from dd-MMM-yyyy)"`, and **refuses** two names that sanitise to one file, or a name that would land on the book being split. `CompanyStorage.PathForName` derives the `.db` file name from the company name (and is not injective), so two books cannot share a name here without one silently overwriting the other. |
| **How it got in** | Forced by the storage identity model, not chosen. The registry that would remove the constraint is the wave-13 design's **16.1-P** (companies stored as `<guid>.db` with a `registry.json`), which is not built. |
| **Fix** | 🔴 **None proposed. Record it as ours.** If 16.1-P ever lands, the defaults can become the vendor's shape (same name, distinct id) with no change to the engine — the naming lives entirely in `CompanySplitService`/`SplitCompanyViewModel`. |

---

### IV-66a · The Gateway row is labelled "Split Company Data", where the vendor's menu word is "Split"

| | |
|---|---|
| **Severity** | **LOW** — a label divergence on a menu that is ours in shape anyway (our Data "menu" is a Gateway section, not the vendor's Alt+Y popup). |
| **What TallyPrime does** | The menu word is **Split** (`Alt+Y (Data) > Split > Verify Data` / `> Split Data`). **Split Company Data** is the vendor's own name for the feature — the title of `help.tallysolutions.com/split-company-data-tally/`. |
| **What we ship** | The Gateway's Data section carries **"Split Company Data"**; the two pages under it keep the vendor's exact words, **"Verify Data"** and **"Split Data"**. |
| **How it got in** | **Measured, not preferred.** WI-9 computes this column's bare-letter accelerators. Labelled "Split", every letter (S·p·l·i·t) was already taken and the rehousing pass took **C away from "Create"** — the most-memorised key on the Gateway — which `DashboardReachabilityTests.The_root_Gateway_column_paints_exactly_these_accelerators` caught and `MenuHotKeyAndAcceptTests` confirmed. The longer label's free **'m'** costs no incumbent a letter. |
| **Fix** | None. If the root column's letters are ever re-designed, the shorter label becomes available again. |

---

### IV-67 · The split books keep the SOURCE company's id, and its whole audit log

| | |
|---|---|
| **Severity** | **LOW–MEDIUM** — invisible in every shipped screen today; it would matter to anything that ever keyed across company files. |
| **What TallyPrime does** | Assigns a **new company number** to each company a split creates (citation as IV-66). |
| **What we ship** | Two things, both stated rather than hidden: **(a)** each split book carries the **same `Company.Id`** as the source, because `Company.Id` is immutable by construction and the only way to change it would be to rebuild the aggregate field by field — a deep copy that would silently drop anything added to `Company` afterwards. Nothing in this application keys across company files (each `.db` holds one book, and `CompanyStorage.Load` reads the single stored row), so it is unobservable today. **(b)** the **voucher edit log is carried WHOLE into both books** rather than partitioned, because it is append-only by construction (`Company.RemoveLastVoucherEditLogEntryInternal` can only drop the entry just appended) and dropping older entries would be an audit deletion. The cost is that a split book's log can name a voucher that book does not contain. |
| **A third, smaller one, stated here rather than left to be found** | **Voucher NUMBERS are not re-sequenced.** The from-book keeps each voucher's original number, so its first Payment may be #4. Nothing retrieved says what the reference product does, and renumbering a book's vouchers is a change to primary records — so the split leaves them alone (R7 silence rule). |
| **How it got in** | Deliberate, in both halves: the alternative to (a) is a hand-written clone that goes stale on the next field added to `Company`; the alternative to (b) is deleting audit rows. |
| **Fix** | (a) becomes free the moment 16.1-P's registry exists. (b) is the smaller harm and should stay until there is a stated requirement. |

---

### IV-68 · A new book begins on the split date — the vendor's split pages do not say what the new company's period is

| | |
|---|---|
| **Severity** | **LOW** |
| **What TallyPrime does** | `help.tallysolutions.com/split-company-data-tally/` and `…/split-company-data-in-tallyprime-faq/` (read 2026-09-07) give the three options and their **date ranges**, and say <i>"After splitting your company, the original company will remain as it is"</i> — and are **silent** on the new company's Financial Year Start and Books Begin From. |
| **What we ship** | The from-book's **`FinancialYearStart` and `BooksBeginFrom` are both set to the split date**; the before-book keeps the original's. This is the smallest rule that is consistent with the vendor's own framing (its split is documented around financial-year boundaries, where the split date IS the year start). |
| **How it got in** | The R7 silence rule: no source speaks, so the smaller honest thing ships and is labelled here rather than asserted as fidelity. |
| **Fix** | None until a source speaks. If one does and it disagrees, the change is two lines in `CompanySplit.ShapeAsFromBook`. |

---

### IV-69 · Verify Data checks the OPEN company; the vendor lets you pick one

| | |
|---|---|
| **Severity** | **LOW** |
| **What TallyPrime does** | <i>"Press Alt+Y (Data) &gt; Split &gt; Verify Data, **select your company**, and resolve any identified errors before proceeding"</i> (`help.tallysolutions.com/split-company-data-tally/`, read 2026-09-07). |
| **What we ship** | Verify Data (and Split Data) act on the company that is **open**, whose file path is printed on the panel. |
| **How it got in** | This shell holds one company at a time (`MainWindowViewModel.Company` is a single nullable field — census 14.9 / 16.7's missing primitive). A picker here would be the multi-company shell by the back door. |
| **Fix** | Falls out of the loaded-set primitive when it is built; not worth inventing before then. ⚠️ Note the same constraint already governs backup/restore (T2-30). |

---

### IV-70 · A split is REFUSED where this build cannot carry a book's state forward faithfully

| | |
|---|---|
| **Severity** | **MEDIUM** — it is a capability limit, and an operator with a bill-wise book will meet it. |
| **What TallyPrime does** | Splits regardless, and carries bill-wise references, unreconciled bank transactions and unreconciled GST transactions into the new company: <i>"All the unreconciled bank transactions, except those recorded before 180 days or more, will get carried forward"</i>, <i>"All the unreconciled GST transactions will get carried forward"</i> (`…/split-company-data-in-tallyprime-faq/`, read 2026-09-07). |
| **What we ship** | `CompanySplit.Check` **refuses**, naming the reason, when the book carries: **bill-wise references still open at the split date** (a `Ledger` has an opening balance but no opening bill-wise breakdown, so the references have nowhere to go — and a book whose party balance says money is owed while Outstandings says nothing is owed is worse than no split); **payroll attendance entries** (each spans a date RANGE, so no partition is unambiguous); **voucher-linked statutory/e-document records** (TDS/TCS/GST challans and links, e-invoice and e-Way Bill records, credit/debit-note links, advance receipts, GSTR-2B snapshots and reconciliations, IMS actions, set-off lines, ITC reversals, DRC-03s), whose rows point at voucher ids the split would leave in the other book; and **opening stock whose carried unit rate would not be paisa-exact**. |
| **How it got in** | Deliberate, and it is the row's own instruction: *verify before splitting, and refuse with a clear reason rather than producing a half-written company.* |
| **Fix** | Each refusal has its own unblocker, and none of them is this row: opening bill-wise references need a `Ledger`-level opening breakdown **and a schema version**; the statutory records need a re-homing rule per record type (and the vendor's 180-day and unreconciled-GST rules above are the specification for two of them); attendance needs a stated rule for a range that straddles the date. **What is NOT deferred is honesty about it** — the operator is told which of these stopped the split. |
| **What matches the vendor by NOT being carried** | Two things this build also does not carry are the vendor's own behaviour, not our limit: <i>"you cannot carry forward the closing balance of the cost center from the previous year when splitting your data"</i>, and <i>"Sales and Purchase Orders … aren't carried forward during the split"</i> (same FAQ). |

---

### IV-71 · The Alt+P menu's **"Current"** row is OURS — the vendor's Print menu has three rows and that is not one of them

| | |
|---|---|
| **Severity** | **LOW** — an added row, not a wrong figure. It prints exactly what the vendor's `Ctrl+P` prints; the divergence is that a TallyPrime operator will find a fourth row where their product has three. |
| **What TallyPrime does** | `Alt+P` opens the print menu, whose rows — verbatim from `help.tallysolutions.com/tally-prime/reports-printing-tally/print-invoices-reports/` (re-opened **by content** 2026-09-24) — are **Configuration**, **Others** and **All Tiles**. The word *"Current"* appears on that same page, but as the **name of the `Ctrl+P` verb**, not as a menu row: *"Open the invoice or report you want to print and press **Ctrl+P** (Current)."* |
| **What we ship** | A **Current** row as the first item of `Screen.PrintMenu` (`ReportChordMenus.cs`, `ReportPrintMenu.CurrentVerb`), which runs the same `OpenPrintPreview()` that bare `P` / `Ctrl+P` runs. `Others` is cloned and reaches Multi-Account Printing; `Configuration` and `All Tiles` are **not built** and are disclosed on the menu face. |
| **How it got in** | Deliberate, and for a keyboard-reachability reason this project has a standing rule about: the menu is the door a new operator opens first, and a menu whose only live row is *Others* reads as though printing the thing in front of you is not available. The label is the vendor's own word for that verb, so the row is not inventing a concept — only a place to press it. **It was not disclosed as ours, which is the defect this row closes.** |
| **Fix** | 🔴 **A USER DECISION (R12), two ways, both cheap.** Route **(1)**: drop the row and let `Ctrl+P` be the only door to Current, matching the vendor's menu exactly — costs nothing, since `Ctrl+P` and bare `P` both already work on every printable page. Route **(2)**: keep it as a documented divergence, which is the state recorded here. Nothing depends on the choice; `ReportPrintMenu.BuildColumn` takes the row as a callback. |

---

### IV-72 · `Ctrl+S` for Save View is OUR chord, kept as a legacy alias beside the vendor's `Ctrl+L`

| | |
|---|---|
| **Severity** | **LOW** — an extra door, not a wrong one. The vendor's chord works; this one works as well. |
| **What TallyPrime does** | Save View is `Ctrl+L`, verbatim (`help.tallysolutions.com/use-save-view-feature-in-tallyprime/`, re-opened **by content** 2026-09-24): *"Press **Ctrl+L** (Save View) to save the report with the specific configurations."* **`Ctrl+S` does not appear on that page at all**, and the vendor's shortcut table does not bind it. |
| **What we ship** | **Both.** `Ctrl+L` is bound as the vendor's chord (the report-chord slice added it), and the pre-existing `Ctrl+S` arm in `MainWindow.axaml.cs` is **retained** and still opens `Screen.SaveView`. The Saved Views panel's own empty-state sentence was corrected to name `Ctrl+L`, so nothing user-visible advertises `Ctrl+S` any more. |
| **How it got in** | `Ctrl+S` was the original RQ-8 binding, chosen before the vendor page was read — the familiar desktop "save" chord, not a Tally fact. When `Ctrl+L` arrived it was added **beside** it rather than replacing it, on the standing rule that deleting a shipped door regresses an operator who uses it. |
| **Fix** | 🔴 **A USER DECISION (R12).** Route **(1)**, recommended: retire `Ctrl+S`, because unlike the `W` chord in IV-64 it has **no unique capability** — `Ctrl+L` reaches the identical panel, so nothing is lost but the muscle memory of operators of this build. Route **(2)**: keep it as a documented alias, which is the state recorded here. The same open question applies as to IV-64's `W`, and the two should be ruled together. |

---

### IV-73 · The Day Book's `F4` (Voucher Type) narrowing also applies to the three `Ctrl+J` exception registers — ours

| | |
|---|---|
| **Severity** | **LOW** — an added consistency, not a changed figure. The narrowing is declared in each register's own subtitle, so a narrowed register cannot be misread as a complete one. |
| **What TallyPrime does** | It attests `F4` on the **Day Book** and says nothing either way about the registers. Verbatim, opened **by content** 2026-10-09 (`help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/`): *"**Day Book** > **F4** (Voucher Type), and select the **Debit Note** voucher type."* and *"Press **F4** (Voucher Type) > **Purchase**."* The same page reaches the registers from the Day Book — *"pressing **Ctrl+J** (Exception Reports)"* — and the vendor's generic report page says *"**F4** — This button name differs based on the report you are viewing"* (`help.tallysolutions.com/working-with-reports/`), which is permissive about per-report meaning but is **not** an attestation for these three. |
| **What we ship** | `ExceptionVouchers.Build` takes the same optional `voucherTypeId` and passes it to `DayBook.Build`, and `ReportsViewModel.SupportsVoucherTypeFilter` is gated on `IsDayBookFamily` — the Day Book plus `OptionalVouchersRegister`, `CancelledVouchersRegister`, `PostDatedVouchersRegister`. So `F4` is offered, and honoured, on all four. |
| **How it got in** | Deliberately, on this repository's own prior commitment rather than on a vendor sentence. `ExceptionVouchers`' class summary already binds these registers to being "the Day Book filtered by one flag … so a register built here cannot disagree with the Day Book the operator pressed `Ctrl+J` on", and the design note on the registers makes the shared arm load-bearing. |
| **🔴 CORRECTION 2026-10-09 (A12, wave 45)** | ~~*"A register that silently **dropped** a narrowing the operator could see in the book's subtitle would be exactly such a disagreement — so the inconsistent option was the riskier one."*~~ **The struck sentence argues for a consistency this entry does not actually deliver, and it is struck rather than deleted so the claim can be audited.** `MainWindowViewModel.OpenExceptionRegister` (`:6224`) builds a **fresh** `ReportsViewModel` carrying only the period, so **`Ctrl+J` from a narrowed Day Book opens the register UNNARROWED** — read first-hand, and the Ctrl+J picker carries the *period* across explicitly with a comment saying why, so only the new parameter was left out. What this divergence genuinely ships is **`F4` pressed *on* a register**, which is tested and real. **No wrong figure either way** — a register that holds no narrowing carries no clause in its subtitle. Whether `Ctrl+J` should carry the narrowing across is a product call, **filed, not decided**. |
| **Fix** | **None proposed.** If a user ruling says the registers must always list the whole flagged set regardless of `F4`, the change is one argument: stop passing `_voucherTypeFilterId` in `BuildExceptionRegister` and narrow `SupportsVoucherTypeFilter` to `Kind == DayBook`. `DayBookVoucherTypeFilterTests.An_exception_register_honours_the_voucher_type_narrowing` (both the engine and the Desktop copy) pins the current behaviour and would be the test to invert. |

---

## 2E. ADDED 2026-10-13 — PHASE 0 OF THE PLAN-TO-FINISH (wave 1): EIGHT ENTRIES, AND SEVERAL GO AGAINST US

**Scope note, and the reason this batch is unlike the others above.** These eight did not come out of building
anything. They came out of a **read-only vendor pass** run specifically to test whether a sizing pass's claim
that "45 clauses are already closed" survived contact with the vendor documentation. Every URL below was opened
**BY CONTENT** and every quoted sentence was read off the page, not inferred. 🔴 **Tally.ERP 9 pages were
DISCARDED, not quoted** — searches for units, currency, reorder levels and batches all surface `/docs/te9rel*/`
and `/article/Tally.ERP9/` results ABOVE the TallyPrime ones, R7 (ruling 14) makes only TallyPrime ground truth,
and a prior campaign fabrication quoted ERP 9.

🔴 **READ THE DIRECTION OF EACH ENTRY BEFORE READING ITS SEVERITY.** Three of the eight (`IV-74`, `IV-75`,
`IV-76`) are cases where **we are LESS capable or MORE restrictive than the vendor and had not recorded it** —
`IV-75` is outright **contradicted** by the vendor page. Two (`IV-77`, `IV-79`) say the opposite: a thing our own
backlog treated as a gap is **not owed at all**, so building it would be inventing. One (`IV-81`) is a
divergence label that was simply **false** — we match. **A register that only ever accumulates "ours is fine"
entries is not being audited, and that is why these are filed together.**

### IV-74 · The compound-unit **composition lock** on the Unit Alteration screen is OURS — the vendor's alteration screen excludes no field

| | |
|---|---|
| **Severity** | **MEDIUM** — no wrong figure, but a capability the vendor grants is refused, and a **test asserted the refusal with the word "must"**, which made our choice read as fidelity. That mislabel is the real finding. |
| **What TallyPrime does** | Gives **Compound Unit** its own subsection under *Alter Units for Stock Items* and states no restriction. Verbatim, `help.tallysolutions.com/manage-stock-item-tally/` (read 2026-10-13): *"Press Alt + G (Go To) > Alter Master > type or select Unit and press Enter."* … *"In the Unit Alteration screen, make the necessary changes, as per your requirement."* … *"Press Ctrl+A to save."* **No field is excluded and no restriction is stated.** |
| **What we ship** | `UnitMasterViewModel.IsCompositionReadOnly => IsAltering` — the alteration screen **hard-locks** base unit, conversion factor and alternate unit whenever it is altering. |
| **How it got in** | On a real engineering ground, stated in the view model: re-pointing a Dozen from 12 Nos to 10 Nos would **silently restate every posted quantity** denominated in it. That reasoning is sound. What was missing is that it was never checked against a source, and the census cell for row 3.5 had itself written the test — *"If the vendor permits it, that is a divergence and the row should stay `PARTIAL`"* — and then nobody ran it. |
| **🔴 The honest limit of the finding** | **The vendor does not explicitly PERMIT editing the conversion factor either** — the page never names that field. So this is vendor **silence on the factor** against a **blanket permission on the screen**. Recorded as silence, not as a vendor claim, because the difference decides whether a future slice is cloning or inventing. |
| **Consequence taken** | **Census row 3.5 was REFUSED promotion to `COMPLETE`** on 2026-10-13 — the one Phase-0 candidate A12 declined. `InventoryMasterPointerAlterTests` previously asserted *"The alteration screen must lock the composition."*; the assertion is **KEPT** (deleting it would let the shipped behaviour change unnoticed) and **relabelled as ours**, naming this entry. |
| **Fix** | **A USER DECISION (R12).** Route **(1)**: adopt the vendor's permissiveness and allow composition edits on alteration — this needs a restatement strategy for already-posted quantities, so it is **not free**. Route **(2)**: keep the lock as a labelled divergence and say so on the screen's own face, which is cheap and is what we effectively do today. **A12 recommends (2) until a user ruling says otherwise, because (1) silently rewrites history and no test in the tree would catch that.** |

---

### IV-75 · Refusing to alter the **base currency** is OURS, and the vendor page **CONTRADICTS** it

| | |
|---|---|
| **Severity** | **MEDIUM-HIGH for the register** — the refusal is not merely unsourced, it is **the opposite of what the vendor documents**, and the campaign record carried a false claim in its favour for weeks. No wrong figure is produced; a capability is withheld. |
| **What TallyPrime does** | Alters the base currency on an ordinary alteration screen. Verbatim, `help.tallysolutions.com/create-alter-or-delete-currencies/` (read 2026-10-13, **200** — this is the live slug that replaces the page which 404'd for two previous agents): *"**You can also alter the base currency. The screen for altering it is similar to the Currency Creation screen.**"* The **only** base-currency carve-out on the page is about the *rates*: *"Modify the **Rates of Exchange** , if required."* … *"**You will not see this option if you are altering the Base currency.**"* |
| **What we ship** | `CurrencyMasterViewModel.ForAlter` returns `null` for the base currency; `MainWindowViewModel`'s `Ctrl+Enter` arm now raises a lifecycle notice naming **Alter Company (F3)** and returns `true` so the chord cannot fall through. |
| **🔴 The false claim this corrects** | The campaign record held that *"the vendor separates base-currency alteration into a distinct procedure"*. **That is FALSE** — the page says the screen is *similar to the Currency Creation screen*. Only *Rates of Exchange* is withheld, which is a **sub-option**, not a procedure. |
| **What was genuinely fixed, and what was not** | `T1-37` closed the **silence** of the refusal — before it, `Ctrl+Enter` on the base currency did nothing at all, no message, no beep, and the operator could not tell a deliberate refusal from a dead keystroke. Mutation **M2a** reddens the notice test by name. **The refusal ITSELF was never sourced and is now known to be contradicted.** |
| **Fix** | **A USER DECISION (R12).** Route **(1)**, the faithful one: allow base-currency alteration on the currency master, withholding **only** the Rates of Exchange grid, exactly as the page describes. The cost is real but bounded — the company profile and the currency master must not desynchronise, which is the legitimate concern behind the original refusal. Route **(2)**: keep routing the operator to Alter Company (F3) as a labelled divergence; the notice already names the destination, so it is honest, just not faithful. **Also unimplemented and attested on the same page:** *"If you use the same symbol for two currencies, TallyPrime automatically prefixes the second currency symbol with letters from its formal name (e.g., "U$") to avoid confusion."* |

---

### IV-76 · Our master **GST block is a REDUCTION** of the vendor's design — four flat fields where the vendor offers a source-mode choice

| | |
|---|---|
| **Severity** | **MEDIUM** — it is a missing capability rather than a wrong figure, but it is the capability that decides **where a rate comes from**, so it shapes every downstream GST figure the operator cannot otherwise explain. |
| **What TallyPrime does** | For **both** HSN/SAC **and** GST Rate it offers a choice of source: *As per Company/Group* · *Specify Details Here* · *Use GST Classification* · *Specify in Voucher* — plus a **Description**, a **slab-based** tax rate, and an F12 *"Provide breakup of Tax Rate"*. Attested on `help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/` and `help.tallysolutions.com/set-up-gst-rate-hsn-sac-details/` (both read 2026-10-13): *"The steps to set up GST Rates and HSN/SAC Details remain the same in **all masters**."* and *"For any other masters: In the master creation or alteration screen, set GST applicability to **Applicable**."* |
| **What we ship** | Four flat fields on `MasterGstBlockEditor` (`TaxabilityChoices`, `SupplyTypeChoices` and their neighbours) and **none of the four source modes**: `grep` over `src/` returns **ZERO** hits for "As per Company/Group", "Specify Details Here", "Use GST Classification" and "Specify in Voucher". |
| **How it got in** | By building the storage first and the screen to match the storage. `MasterGstDetails` is a flat record, so the editor became flat too. Nobody compared the editor to a vendor page because the **hierarchy** was the thing under test. |
| **🔴 One thing this entry DISCHARGES rather than adds** | The census's long-standing caveat that the **accounting-Group** rung of the hierarchy was *"NOT independently sourced"* is **closed** — the page was found and it names *"**Ledger Group**"* and *"**Accounting Group**"* explicitly, in both resolver order strings, matching our two arrays. **That half was fidelity all along.** |
| **Fix** | Build the source-mode choice on the master GST block (census row 3.13 is the owner; row 6.4's capture clause depends on it). ⚠️ **Note that a GST Classification master already exists in the product (census 6.25) but is NOT offered as a *mode* here — so part of this is wiring, not new capability.** 🔴 **One field must NOT be assumed:** *"Type of Supply"* is attested on the Stock **Item** page, and no TallyPrime page was found naming it on a Stock **Group** screen — so the Stock-Group field list is **not** fully sourced on that field. |

---

### IV-77 · We ship a **standalone Batch / Lot master** the vendor does not appear to have — and that makes a "missing Alter verb" not a gap

| | |
|---|---|
| **Severity** | **LOW for the customer, HIGH for the backlog** — nothing is mis-stated on any document, but census row 3.8 carried *"No Alter"* as a gap for a master the vendor does not publish, so the gap was unbuildable by construction and would have been "fixed" by inventing a screen. |
| **What TallyPrime does** | Routes **every** batch operation through the **Stock Item** master and the voucher's item allocation. No TallyPrime page documenting a Batch master was found. Verbatim, `help.tallysolutions.com/tally-prime/inventory/manage-inventory-batch-wise-tally/` (read 2026-10-13): *"In the Stock Item Creation/Stock Item Alteration screen, set the option **Maintain in batches** to Yes."* and *"You can alter the details in the Stock Item Masters, by pressing **Alt + S** (Stock Alter)."* |
| **What we ship** | A standalone Batch/Lot master with its own menu row (gated on the F11 batch flag), Create and — since the W33 residual cluster — a guarded `Alt+D` Delete on the shared master-list arm. No Alter. |
| **🔴 Stated as an honest negative, deliberately** | *"We could not find the page"* and *"the page does not exist"* are different claims and **only the second licenses a divergence label.** This is filed as the second: every vendor batch route found runs through the item. If a TallyPrime batch-master page is later produced, **this entry is wrong and should be struck**, and that is the right way for it to fail. |
| **Fix** | **None proposed as clone work.** The withheld Alter verb is **not** a gap — there is no vendor batch-master alteration to clone, and the existing withholding (recorded by evidence in `MainWindowViewModel`) was the correct call for the right reason. **Whether this master should exist in this shape at all is a USER call**, not a wave: retiring it would move batch entry onto the Stock Item master and is a real migration. |

---

### IV-78 · Refusing to move **Applicable Upto** during an alteration is OURS — the vendor is permissive about the screen and silent on the field

| | |
|---|---|
| **Severity** | **LOW** — a withheld correction on an off-books voucher. Nothing posted to the books is affected, because a Reversing Journal is not posted to any book. |
| **What TallyPrime does** | Permits alteration from the register and says nothing about this field. Verbatim, `help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/` (read 2026-10-13): *"All vouchers are maintained in a Reversing Journal Register. These are not posted to any books of accounts and cannot be included in regular reports. **You can view or alter Reversing Journals from the register.**"* On the field itself, only: *"**Applicable Upto**: This is the date up to which the Reversing Journal is available for inclusion in a scenario report."* |
| **What we ship** | The field rehydrates and re-parses on alteration, but moving it is **refused by name** — pinned by `VoucherAlterForAlterTests.Constraint2_moving_applicable_upto_during_an_alteration_is_refused_by_name`. |
| **How it got in** | As a deliberate constraint when voucher alteration was built: moving the date changes which scenario reports the entry appears in, retrospectively. Reasonable, unsourced. |
| **Fix** | **None proposed.** Kept as a labelled divergence; census row 4.18 no longer counts it as a gap. If a user ruling adopts the vendor's permissiveness, the named test is the one to invert. |

---

### IV-79 · A **Dr/Cr split** on the Day Book would be OURS to invent — the vendor's Day Book page sets out **no column layout at all**

| | |
|---|---|
| **Severity** | **LOW for the customer, HIGH for scheduling** — census row 11.4 carried the single Amount column as gap **(a)**, i.e. as a fidelity debt. It is not one, and scheduling it as clone work would have spent a slice on an invention. |
| **What TallyPrime does** | Publishes **no column layout** for the Day Book. On `help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/` (read 2026-10-13) the only column words on the page are in the F7 gross-profit context — *"press **F7** (Show Profit) to see the Gross Profit against the **Transaction Value** and **Cost**"* — a different feature. The page **does** attest the chords we already ship: *"Open Day Book and press **Ctrl+F** (Apply Filter)"*, *"you can also press **F6** (Multi-Filter) and **F7** (Advanced Filter)"*, *"Press **Alt+F5** (Detailed) for a detailed format of the report."* |
| **What we ship** | `src/Apex.Ledger/Reports/DayBook.cs` carries a single `Money Amount` on the row record. |
| **Fix** | **Do not schedule as clone work.** If the user wants a Dr/Cr split it ships as a **labelled divergence behind a user ruling** — it cannot be justified by R7, because R7's first source speaks on this page and says nothing about columns. **The gap shrinks from a fidelity debt to an unsourced wish.** |

---

### IV-80 · Adding the two godown fields **unconditionally** would itself be a divergence — both are **F12-gated** in the vendor

| | |
|---|---|
| **Severity** | **LOW, and it is a trap rather than a defect** — the fields are genuinely missing (census row 3.7 stays `PARTIAL`), but the obvious build is the wrong one and **no test in the tree would have caught it**. |
| **What TallyPrime does** | Hides both fields behind F12. Verbatim, `help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/` (read 2026-10-13, **200** — the TallyPrime slug that replaces the **Tally.ERP 9** route census row 3.7's header still quotes, which R7 does not make ground truth): *"**Allow storage of materials** — By default, each godown can store materials. However, you can set a godown to not store any materials. To do so, setting this option to Yes to displays the Allow storage of materials option on the Godown Creation screen. Set that option to No."* · *"**Use address for Locations** — You can add the Locations address by enabling this option. The Locations address can be printed in your invoices by configuring invoice printing."* · and the page's own rule *"What you see on the screen can vary based on your choices. Press **F12** (Configure) to change the options."* |
| **What we ship** | 🟢 **CLOSED 2026-10-18 (census 3.7). 🔴 READ THE DATE: the 2026-10-16 claim on this row was FALSE and is corrected rather than deleted.** It read *"🟢 CLOSED 2026-10-16 BY SCHEMA v67 … Both fields ship, and each is behind its own F12 switch"*, which any reader would take as **reachable**. It was not: the two switches existed in the schema, in `Company`, in the store and in the godown master's gate — **29 occurrences across `src/`, and ZERO ASSIGNMENTS.** Nothing in the product could turn either on, so `ShowAllowStorageOfMaterials` / `ShowAddress` could only ever be `false` and **both fields were permanently invisible to every user** (BF-4). ~~Neither field, on a Godown Creation screen with no F12 configuration layer for them.~~ **As of 2026-10-18 both fields ship AND a user can reach them:** each is behind its own F12 switch — `companies.config_allow_storage_of_materials` / `config_use_address_for_locations`, surfaced as `GodownMasterViewModel.ShowAllowStorageOfMaterials` / `ShowAddress` — and **each switch is now a real CheckBox on the company Configuration screen** (`ConfigAllowStorageOfMaterialsCheck` / `ConfigUseAddressForLocationsCheck` in `MainWindow.axaml`, bound to `GstConfigViewModel`), beside the BOM F12 switches that already use that placement. **The gate decides whether the value is WRITTEN, not merely whether the control is drawn** (the `ShowJobCostCentre` contract): with a switch off, Create and Alter write the vendor default. `godowns.allow_storage_of_materials` is `DEFAULT 1`, which is the vendor's own *"By default, each godown can store materials"*. |
| **Fix** | **DONE — and the trap was real.** The warning was that "build two fields" would have been the wrong build; what shipped is two F12 switches plus two conditionally-shown fields. Mutation **M3** (let the gate draw the fields but not decide what is written) reddens `With_the_f12_switches_off_neither_godown_field_is_shown_and_the_vendor_defaults_are_written` by name, and mutation **M5** (flip the `DEFAULT 1` to `0`) reddens three schema tests including the `CreateV1` equivalence. 🔴 **AND THE BIGGER TRAP WAS THE ONE THE ROW ITSELF FELL INTO:** two F12 switches with no writer are not a build. Six tests in `GodownF12SwitchReachabilityTests` now drive the **shipped** `MainWindow`: mutation **M-BF4a** (delete both CheckBoxes — literally the state v67 shipped in) reddens **5 of 6** by name, *"the 'Allow storage of materials' F12 switch is not in the visual tree — census 3.7's field is unreachable again"*; mutation **M-BF4b** (let the switch draw but not reach the company) reddens **4 of 6**; mutation **M-BF7** (swap the two store read ordinals, which the reviewer showed reddened NOTHING before) now reddens `Flipping_the_realised_switches_persists_to_the_reloaded_book`, because that test writes the two switches to DIFFERENT values in both directions. ⚠️ **STILL OURS, and not closed:** the switches sit on the company Configuration screen rather than behind an F12 key on the Godown screen itself; the address is ONE free-text box, not the vendor's multi-line address block; *"The Locations address can be printed in your invoices"* is **not** built — no invoice prints it; and `AllowStorageOfMaterials = false` is an ELECTION only: it does not yet refuse a stock movement into the godown. **Census row 3.7 therefore still has open clauses and is NOT proposed for promotion.** |

---

### IV-81 · 🔴 A **MIS-LABELLED** divergence: bare **F10 → Other Vouchers** is what the vendor documents — **we MATCH**

| | |
|---|---|
| **Severity** | **LOW in direction, worth correcting in kind.** Nothing the customer sees is wrong. What was wrong is the **register**, and a register that claims we diverge where we match invites a later slice to "fix" a faithful behaviour. |
| **What TallyPrime does** | Documents exactly our route. Verbatim, `help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/` (read 2026-10-13): *"Press Alt + G (Go to) > Create Voucher > press **F10** (Other vouchers) > type or select **Reversing Journal**."* and *"Alternatively, Gateway of Tally > Vouchers > press **F10** (Other vouchers) > type or select Reversing Journals."* |
| **What we ship** | The same: `Key.F10` → `ShowOtherVouchersMenu()` in `src/Apex.Desktop/Views/MainWindow.axaml.cs`, with the page item dispatching to `OpenVoucher(VoucherBaseType.ReversingJournal)`, pressed through the real `MainWindow` tunnel by `VoucherTypeNavigationIdentityTests`. |
| **What was mis-stated** | `VoucherTypeNavigationIdentityTests` called bare F10 *"a deliberate, documented divergence — decision D7 option A / X6"*. **It is not a divergence; D7/X6 does not apply to it.** The claim is **struck in the test comment, not deleted**, so the mislabel can be audited. |
| **Consequence taken** | Census row **4.18's first clause** — *"no seeded shortcut, so it is menu-only"* — was carried as a **GAP** on the strength of this reading and is now **WITHDRAWN**: the vendor is menu-only too, under F10. **This is one of three gaps that vanished in Phase 0 without anything being built.** |
| **Fix** | **Done** — the label is corrected in the test and the gap is withdrawn on the row. No code change; none is owed. |

---

## 2F. ADDED 2026-10-13 — SCHEMA v66: VOUCHER-LEVEL GST REGISTRATION ATTRIBUTION AND THE PER-TYPE INSERTION BEHAVIOUR

**Scope note.** These three came out of **building** two capabilities (user ruling 30(3) and 30(4)) rather than
out of a read-only pass, so each one names a line this wave shipped. Every vendor sentence quoted below was
opened **BY CONTENT** on 2026-10-13 and read off the page. 🔴 **No Tally.ERP 9 page is quoted here** — R7
(ruling 14) makes only TallyPrime ground truth.

🔴 **READ WHAT THESE ARE NOT.** None of them is a wrong figure, and none of them is the capability itself: the
F3 registration selector and the Renumber-vs-Retain switch are both **CLONED and ATTESTED**, with the vendor
quotes on `VoucherEntryViewModel.SelectedGstRegistrationId` and `VoucherType.RetainOriginalVoucherNo`. These
three are the **edges those two capabilities stop at**, filed here so a later slice does not read our boundary
as the vendor's.

### IV-82 · The vendor's insertion/deletion switch names **deletion** too — we honour it on INSERT only

| | |
|---|---|
| **Severity** | **MEDIUM.** No figure is wrong today, but half an attested setting is unwired, and under the vendor's own default (*Renumber Vouchers*) our deletion behaviour **does not match**: a deletion leaves a gap in a statutory number series where the vendor closes it. |
| **What TallyPrime does** | Names BOTH verbs in one sentence. Verbatim, `help.tallysolutions.com/use-voucher-numbering-methods/` (opened BY CONTENT 2026-10-13, and the two examples below re-verified by a second targeted read of the same page): *"This setting ensures that when you **insert or delete** a voucher, the existing voucher numbers are adjusted automatically."* And for the other option: *"Set this option, if you do not want to change the existing voucher numbers on insertion of new vouchers **or deletion of existing vouchers**."* 🔴 **AND THE PAGE'S WORKED EXAMPLE FOR *EACH* OPTION IS A DELETION, NOT AN INSERTION** — *Renumber Vouchers*: *"If the sequence is 1, 2, 3 and voucher 2 is deleted, the remaining vouchers are renumbered as 1, 2."*; *Retain Original Voucher No.*: *"Deleting voucher 2 from the sequence 1, 2, 3 will result in a sequence of 1, 3."* So deletion is not an aside in the vendor's description of this setting — **it is the behaviour the vendor chose to illustrate it with.** |
| **What we ship** | `VoucherType.RetainOriginalVoucherNo` (schema v66, column `retain_original_voucher_no`) is read by **`VoucherInsertion.Plan` and by nothing else.** `LedgerService.Delete` renumbers nothing and never did — `NextNumber` is `max + 1` computed by scanning, with no stored counter, so deleting the highest-numbered voucher of a type even hands the same number out again (that method says so in its own remarks, and `MasterDeletionRules` bounds it). |
| **The consequence, stated both ways** | Under **Retain Original Voucher No.** our behaviour coincides with the vendor's **by accident**, not by implementation. Under **Renumber Vouchers** — the vendor's documented default and the only behaviour this build had before v66 — a deletion diverges: the vendor closes the gap, we leave it. |
| **Why it was not built with the insert half** | Renumbering on delete rewrites the document number of vouchers that have **already been issued**, and this build's insert path already refuses that outright when any voucher in the shift range carries a live IRN or e-Way bill (`VoucherInsertion.FiledDocumentRefusal`). Wiring the deletion half needs the same freeze applied at `LedgerService.Delete`, which is an engine change on the destructive verb and is **not** a corollary of adding a column. |
| **Fix** | **Its own slice.** Honour the flag in `LedgerService.Delete` behind the identical `MasterDeletionRules.IsFiledStatutoryDocument` freeze the insert path uses, so insert and delete cannot disagree about which documents are frozen. Until then the flag is honestly **half-scoped and labelled**, in `VoucherInsertion`'s own remarks, rather than advertised as the vendor's whole setting. |

### IV-83 · An ALTERATION cannot move a posted voucher between GST registrations — our refusal, not the vendor's

| | |
|---|---|
| **Severity** | **LOW-MEDIUM.** A capability the vendor neither grants nor withholds in print is refused, by name, on screen. The risk filed here is the **label**, not the behaviour: a later slice must not read this refusal as fidelity. |
| **What TallyPrime says** | Only this, and only about **creation**. Verbatim, `help.tallysolutions.com/tally-prime/gst-regular-sales/gst-sales-tally/` (read 2026-10-13): *"For TallyPrime 3.0 & later, if you have multiple registrations, press F3 (Company/Tax Registration) and select the registration under which you want to **create** the voucher."* 🔴 **Nothing sourceable was found about a POSTED voucher**, and this is recorded as vendor **SILENCE**, not as a vendor prohibition — the difference decides whether a future slice is cloning or inventing. |
| **What we ship** | `VoucherEntryViewModel.SelectGstRegistration` returns `RegistrationLockedOnAlterationRefusal` whenever `IsAltering`, `MainWindowViewModel.OpenVoucherRegistrationPicker` raises that same sentence on the notice bar rather than opening a picker whose every row would then be refused, and `RehydrateFrom` seeds the screen from the posted voucher so the alteration still **shows** which registration's return the document belongs to. |
| **The engineering ground** | Moving a posted voucher between registrations is not a field edit: it moves a document **out of one filed return and into another, under a different GSTIN**. That is the wrong-filed-document class this build refuses by construction elsewhere — `GstReportSupport.EnsureRegistrationScoped` throws rather than fold two GSTINs into one return. It is also the exact shape of `LedgerService.Replace` §7.4/§12.8, which refuses a replacement that moves the provisional-state vector for the same reason. |
| **Fix** | **A USER DECISION (R12)** if it is ever wanted. Route **(1)**, recommended and what we ship: keep the refusal, labelled as ours, with the named sentence that tells the operator what to do instead (*"Cancel or delete it and record it again under the other registration"*). Route **(2)**: permit the move — which needs a rule for what happens to a period already filed under the old GSTIN, and that is a returns-amendment question, not an entry-screen one. |

### IV-84 · F3 (Company/Tax Registration) is offered on the ACCOUNTING voucher screen only — the stock, order, payroll and POS screens stamp nothing

| | |
|---|---|
| **Severity** | **MEDIUM**, and it is a **narrowing of scope** rather than a wrong answer — but it is the kind that reads as complete from the outside. |
| **What TallyPrime does** | Does not scope its F3 sentence to a voucher family at all (citation as IV-83). |
| **What we ship** | `MainWindowViewModel.IsVoucherRegistrationSelectable` requires `CurrentScreen == Screen.VoucherEntry`, so the F3 → *Tax Regn* row appears on the **accounting** voucher-entry screen and nowhere else. The stock/order screens (`InventoryVoucherEntryViewModel`), the Manufacturing Journal, Material In/Out, the payroll screens and `PosBillingViewModel` are untouched: they post through other services into other collections, and none of them stamps a registration. |
| **Why it is the half that shipped first** | 🔴 **A caption over a key that stamps nothing is the dead-knob shape this project files as a defect** (an inert Alt+P, three reports that printed blank cells, a verb button that ran `Create()`), so the affordance is withheld where the stamp does not exist rather than painted everywhere. The accounting book is where every GST return is folded from — `GstReportSupport.PostedDirectionalVouchers` walks `Company.Vouchers` — which is why it is the half that closes the four findings behind `T2-121(i)`. |
| **The residual, measured** | On a multi-registration book a **POS bill** and a pure-**inventory** document still attribute to the primary registration with no way to say otherwise. For POS this is a real hole on a Sales document (`PosBillingViewModel` builds its own `Voucher`); for the pure-inventory collection it matters less, because those documents carry no GST legs and no return folds over them — but *less* is not *none*, and it is written here rather than left to be discovered. |
| **Fix** | **Its own slice**, and POS first: `PosBillingViewModel`'s two `new Voucher(...)` sites need the same stamp and the shell needs the F3 row enabled on `Screen.PosBilling`. ⚠️ Note the collision to resolve before building it: on `Screen.PosBilling` the vendor's **Alt+I** is already spent on the tender-mode toggle in this build (census row 5.5's own U-6 chord note), so the POS screen's chord budget is already contested and should be ruled, not assumed. |

---

## 2G. ADDED 2026-10-13 — WAVE 1 TRACK BB: WHAT THE **MULTI MASTERS** SLICE OWNS (census rows 2.12 / 3.3)

> 🔴 **RENUMBERED AT MERGE, 2026-10-16 (wave 46 wave-2, by A12).** This section was authored as **§2F** with
> entries **`IV-82`…`IV-88`**, and `claude/apex-ba-v66-attribution` independently authored a DIFFERENT **§2F**
> with **`IV-82`, `IV-83`, `IV-84`** for different divergences. `ba` landed first and owns those three numbers,
> so this section is **§2G** and its seven entries are **`IV-85`…`IV-91`**. **Nothing was dropped from either
> side — the conflict was resolved hunk by hunk and BOTH sets of register entries are kept.** The collision was
> predicted in `T2-138` and is the schema-version "two live tracks both take the next number" trap hitting the
> IV register; **decide up front which track owns the next IV number, exactly as we now do for schema versions.**

**Scope note.** These came out of *building* the thing `IV-21` asked for, so unlike §2E they are the divergences
a shipped screen now carries. Every vendor sentence quoted below was read off a page opened **BY CONTENT** on
2026-10-13; the four URLs are named per entry. 🔴 **No Tally.ERP 9 page is quoted.** Searches for multi-master
screens surface `/docs/te9rel*/` results (notably *Creating Multiple Stock Categories*) above the TallyPrime
ones; R7 (ruling 14) makes only TallyPrime ground truth and they were discarded.

🔴 **ONE OF THESE SIX GOES AGAINST THE CENSUS RATHER THAN AGAINST THE VENDOR, and it is listed first because it
changes what a future slice would build.** `IV-85` records that census row 2.12's own target — *"two of the
vendor's **SIX** families"* — has **no source behind the number six**. A register that only ever files
"the vendor is silent, so ours is fine" is not being audited.

### IV-85 · "Two of the vendor's **SIX** families" is an uncited count — the vendor page **refuses to close the list**

| | |
|---|---|
| **Severity** | **MEDIUM, and it is a MEASUREMENT defect rather than a product defect.** No customer is harmed. But the number was the stated target of a census row, so a slice built to it would have stopped at six families for no reason, and a reviewer checking "did they ship all six?" would be checking against nothing. |
| **What TallyPrime documents** | A list it explicitly leaves open. Verbatim, `help.tallysolutions.com/tally-prime/charts-of-accounts-tally/`: *"Similarly, you can create other masters such as stock items, groups, employees, **and so on**"*, and the matching *"Similarly, you can **alter** other masters such as stock items, groups, employees, and so on."* The page walks the feature in full **for Ledgers only** — its four headings are *View Chart of Accounts*, *Create Multiple Masters*, *Alter Multiple Masters*, *Delete Multiple Unused Masters* — and enumerates no fixed family set anywhere. |
| **What the families actually are, each on its own page** | **Ledgers**, **Groups**, **Stock Items**, **Employees** (Chart of Accounts page) · **Godowns** (`inventory-storage-using-godowns-locations-tally/`) · **Cost Centres** *and* **Cost Categories** (`cost-centre-or-profit-centre-tally/`) · **Stock Items** again, by screen name (`stock-items-faq/`). That is **at least seven named across pages, plus "and so on"** — not six. |
| **How it got in** | Row 2.12's evidence sentence has been corrected once already (§1.3 item 19 struck a `Multi Ledger`/`MultiLedger` grep that targeted a Tally.ERP 9 name). The "six" appears to have been written in the same pass and was never sourced. It is the same shape as row **2.5**, where the census measured us against `{Automatic, Manual, None}` while the vendor's set was four — *"the TARGET LIST was wrong, so the gap could not have been closed by building to it."* |
| **What was built instead** | Five families — Ledger, Group, **Stock Item**, **Godown**, **Cost Centre** — each with its own citation on `MultiMasterKind`, and **no test anywhere pins a family COUNT**. Pinning six would enshrine an uncited number as fidelity, which is precisely what cost row 3.5 its promotion (`IV-74`). |
| **🔴 Deliberately NOT built, and why** | **Employees** and **Employee Groups** are vendor-attested and are **out of scope by user rulings 29+30** (Area 7, payroll). **Cost Categories** are attested on the cost-centre page and were left for a later slice rather than half-built. **`Delete Multiple Unused Masters`** is a THIRD Multi Masters verb, discovered as a heading on the Chart of Accounts page during this slice — it has **no census row at all** and is owed one. |
| **Fix** | Correct row 2.12's cell to drop "of the vendor's six" and state the attested families with their URLs. **A12's call, not the build track's** — the build track proposes. |

---

### IV-86 · The **◦ Primary** option on the godown grid is ours — the vendor's wording implies a parent is always given

| | |
|---|---|
| **Severity** | **LOW.** It adds a capability rather than withholding one, and it matches what our own single Godown master already stores. |
| **What TallyPrime documents** | A prompt for a parent on every row. Verbatim, `help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/`: *"If you select **Under Godown > All Items**, you will be prompted to select the parent godown for each godown name that you specify under the **Name** column."* The header name **Under Godown** and the column name **Name** are the vendor's and are cloned exactly. The page does not say what happens for a godown that has no parent. |
| **What we ship** | A `◦ Primary` sentinel in the per-row picker (`Guid.Empty`, mapped back to `null` by `ParentIdOrNull`). |
| **🔴 Why it is not optional for us** | **Without it the Multi Godown Alteration screen refused itself.** The first draft loaded every godown, found `Main Location.ParentId == null`, could represent it with no picker option, and refused the whole batch with *"Row 1 (Main Location): pick a parent godown"* — over a grid the operator had not touched. Main Location is the only godown most companies have. A multi screen that cannot express what the single screen stores cannot show the company's own data. |
| **Fix** | None proposed. Labelled and kept. |

---

### IV-87 · The **Multi Ledger Alteration** header acts as a **filter**, and reloading it discards uncommitted edits

| | |
|---|---|
| **Severity** | **LOW-MEDIUM.** The discard is a real surprise an operator can meet. |
| **What TallyPrime documents** | Only the verb. Verbatim, `help.tallysolutions.com/tally-prime/charts-of-accounts-tally/`: *"In **Chart of Accounts**, press **Alt+H** (Multi-Masters) > **Multi Alter** > press **Enter**. In the **Multi Ledger Alteration** screen, change the name of ledger, **Group**, and **Opening Balance** of the ledgers."* **The page never says what the header field does on the alteration screen** — on the CREATE screen it says the field "defaults to All Items" and sets the parent for every row. |
| **What we ship** | On Alter the header selects which masters LOAD (`All Items` loads them all), and changing it re-reads the grid from the company. The screen's own hint changes wording with the verb ("…to **load** only its ledgers" vs "…to **put** every row under it") so the two meanings are not silently interchangeable. |
| **🔴 The surprise, stated rather than hidden** | Changing the header after editing rows **discards those edits**, because the reload comes from the company. |
| **Fix** | If a user ruling asks for it: warn before a reload that would discard pending edits, or re-apply them by id. Not built on a guess. |

---

### IV-88 · What Multi **Alter** may change is narrower than the single-master screens — three exclusions, each preventing wrong money or a wrong quantity

| | |
|---|---|
| **Severity** | **LOW**, and the vendor records a limit of the same shape on its own side. |
| **What TallyPrime documents** | For ledgers, exactly what we allow: *"change the name of ledger, **Group**, and **Opening Balance**"* plus *"To remove the opening balances from all the ledgers, press **Alt+B** (Zero Opening Balance)"* (`charts-of-accounts-tally/`). For stock items it documents its OWN reduction: *"Currently, TallyPrime does not support viewing and editing all the details related to stock items in the **Multi Stock Item Alteration** screen"* (`stock-items-faq/`). It does **not** enumerate which fields its stock-item alteration screen does carry. |
| **What we ship** | Alter edits the **name** and the **parent**, plus the **Opening Balance + Dr/Cr** on the ledger grid. Three exclusions, each with an engineering reason: a stock item's **base unit** is never re-assigned (it would silently reinterpret every quantity already recorded against it — the same exclusion `StockItemMasterViewModel` makes); a stock item's **opening quantity** is never re-applied (it would ADD a second opening allocation on every accept and multiply opening stock, which is closing stock, which is the Balance Sheet); and a **primary** group is not loaded into the group grid at all (it has no parent to show and its Nature of Group is not alterable here). |
| **🔴 A fourth exclusion that is NOT a design choice but a consequence of our own data model** | A ledger under the reserved **Profit & Loss** head is excluded from the alteration grid. `Company.ProfitAndLossHead` is a `Group` deliberately **absent from `Company.Groups`** — which is why `FindGroupOrHeadByName` exists beside `FindGroupByName` — so no group picker in this application can offer it. The first draft loaded the seeded *Profit & Loss A/c* ledger anyway and refused the whole untouched batch with *"Row 4 (Profit & Loss A/c): pick a group"*. Excluding it is the conservative answer; the alternative, putting the reserved head in the picker, would newly permit moving **ordinary** ledgers under it, which no source asks for. |
| **⚠️ AND A LATENT DEFECT IN THE *SINGLE* LEDGER MASTER, FOUND HERE AND NOT PATCHED HERE** | `LedgerMasterViewModel.ForAlter` resolves its picker with `SelectedGroup = Groups.FirstOrDefault(g => g.Id == ledger.GroupId) ?? SelectedGroup`. For the P&L A/c ledger that lookup **MISSES** and falls back to whatever was already selected, so the screen shows a group the ledger is **not** under — and accepting would silently **RE-PARENT** it. Outside this slice; **owed a TIER register row.** |
| **Fix** | The three design exclusions: keep, labelled. The P&L exclusion: revisit only alongside the single-master defect above, because they share a root cause. |

---

### IV-89 · The stock-item grid's **column set**, and its read-only computed **Amount**, are ours

| | |
|---|---|
| **Severity** | **LOW.** |
| **What TallyPrime documents** | The screen **names**, and nothing about its columns. `help.tallysolutions.com/stock-items-faq/` gives *"Alt+H (Multi-Masters) to open the **Multi Stock Item Creation** or **Multi Stock Item Alteration** screens"* — both cloned verbatim as our titles. 🔴 **The column set could not be sourced.** Web-search summaries offer *"Name of Items, Units, Opening Qty, Rate, and Amount"*, but **no page this project opened by content carries those strings** — `charts-of-accounts-tally/`, `manage-stock-item-tally/`, `tally-prime/inventory/manage-stock-item-tally/` and `tally-prime/vat-masters/india-vat-stock-item-tally/` were each opened and each lacks them. **A search snippet is not a citation**, so the column set is recorded as OURS. |
| **What we ship** | `Name of Item · Under · Units · Opening Qty · Rate · Amount`, with **Amount read-only and computed** as Qty × Rate. |
| **🔴 Why Amount is not editable, which is a deliberate step AWAY from the reference product** | TallyPrime's grids generally let an operator key an Amount and back-solve the Rate. We do not: two editable boxes over one derived third are **three ways to disagree about an opening valuation**, and an opening valuation is Balance-Sheet money. The cell is blank whenever either input is blank or unparseable, so it never shows a figure the batch would refuse. |
| **Also ours on this family** | A **rate with no quantity** is refused rather than silently discarded; the cost grid's columns are `Name of Cost Centre · Category` with **no parent column**, so every centre it creates is top-level (a per-row parent picker would have to be re-scoped to each row's own category to keep the domain's *"a parent must be in the SAME category"* invariant, and a picker that silently offers an invalid parent is worse than no picker — re-parent from the single Cost Centre master). |
| **Fix** | If the column set is ever sourced, reconcile. Until then: labelled. |

---

### IV-90 · Multi Masters is reachable from the **Create cascade** as well as from Alt+H, and the Alt+H column names the family on every row

| | |
|---|---|
| **Severity** | **LOW** — additive. |
| **What TallyPrime documents** | One door, and the family taken from context. Verbatim, `help.tallysolutions.com/tally-prime/charts-of-accounts-tally/`: *"Press **Alt+G** (Go To) > type or select **Chart of Accounts** > **Ledgers** and press Enter"*, then *"Press **Alt+H** (Multi-Masters) > **Multi Create**"*. The menu offers two rows — Multi Create / Multi Alter — and the master **family** comes from whichever Chart of Accounts the operator is standing in. |
| **What we ship** | **Alt+H is now bound** (census row 2.12 recorded it as unbound and it was: the only `Key.H` arms in the shell were two `Ctrl+H` arms). Our Chart of Accounts is **one accounting tree holding groups and ledgers together**, so there is no single family to infer — the Alt+H column therefore names the family on every row, under a header per verb. The create grids are ALSO reachable through `Masters → Create → Multi Masters`, which is the route that shipped in PR #54 and whose tests still hold. |
| **Why** | The UI contract requires every capability to be reachable by the arrows-and-Enter cascade and **never only by a chord**. The chord ADDS the vendor's route rather than replacing ours. |
| **Ruling 25 honoured** | No **Display** row is offered on either verb, and a test asserts the column contains no row whose label mentions "Display". |
| **Fix** | None proposed. |

---

### IV-91 · 🔴 ADDED 2026-10-16 — **all-or-nothing commit** on both verbs is ours, and it was BROKEN on the Alter verb before it was true

| | |
|---|---|
| **Severity** | The divergence is **LOW** and additive. **The defect it is recorded alongside was BLOCKING**, and it is written here because §2F is the slice's own register and the slice shipped the claim before it shipped the behaviour. |
| **What TallyPrime documents** | **Nothing.** None of the four pages reached (`charts-of-accounts-tally/`, `stock-items-faq/`, `inventory-storage-using-godowns-locations-tally/`, `cost-centre-or-profit-centre-tally/`) says what happens to the other rows of a multi-row batch when one row is refused. Searched by content; no sentence speaks. **Ours, labelled (ruling 9).** |
| **What we ship** | One bad row refuses the WHOLE batch, naming the offending row, on Multi Create and Multi Alter alike. ⚠️ **Whether the operator's typing survives depends on WHICH refusal fired, and the earlier unqualified claim here that it always does was measured FALSE by the wave-2 review.** A *screen-level* refusal (empty name, in-batch duplicate, no parent, unparseable opening balance) returns before any write and leaves the grid exactly as typed, on both verbs — the bad row is corrected in place. An *engine* refusal on the **Alter** verb reloads the grid from the restored book (`LoadExistingRowsPreservingMessage` → `LoadExistingRows`), so **every edit in the batch is discarded and must be retyped**. Pinned by `MultiMasterAlterRollbackTests.After_a_refusal_the_grid_redisplays_the_ORIGINAL_values`; preserving the typing over an engine refusal is an **open decision**, not an oversight. |
| **Why** | A partial write leaves the operator guessing which of twenty names landed, and the engine's uniqueness guard structurally cannot see a name typed twice in the same batch (neither copy exists yet), so a batch-level pass is needed regardless. |
| 🔴 **What was wrong, and how it was found** | The ALTER arm pre-validated name / in-batch duplicate / parent / opening-balance and then wrote row by row. The refusals the **engine** owns fire inside that loop, so a batch refused on row 3 had already written rows 1–2 into the shell's **live** aggregate; the screen said "refused" and the **next unrelated save made it durable**. Reproduced in three families by the wave-1 adversarial review. Two statements in our own source were false: the class doc's all-or-nothing claim (true of Create, false of Alter) and the catch block's "the in-memory aggregate is reloaded from disk by the caller's Escape/re-open" — `CompanyStorage.Load` has exactly two call sites, company-open and company-unlock, and **neither is Escape**. |
| **Fix** | **Done 2026-10-16.** `Apex.Ledger.Services.MasterStateSnapshot` — a rollback point taken before the first write and restored on any refusal. It is built by **reflection over every writable property** of every master in the six covered collections, not from a hand-written field list, so a property added later is covered the day it is added; the completeness test writes a property no batch path touches and proves it comes back. It also undoes `RecomputeNatureFor`'s nature cascade over descendants that were never rows of the batch. A fuller pre-validation was rejected as **unsound**: the cycle and same-category guards judge the state the book will be in once the other rows are applied. Evidence is a re-read **from SQLite after a later unrelated save**, plus mutation — deleting the restore reddens five named tests. |

---

### IV-92 · 🔴 ADDED 2026-10-18 — a **MIS-CITED** divergence: the Sales-ledger hiding rule is TWO INDEPENDENT conditions, and on condition (1) **we MATCH**

| | |
|---|---|
| **Severity** | The mis-citation is **MEDIUM** — it sat in shipped source and in the view, and it described our behaviour as wrong in the **opposite direction** to the truth. The residual divergence is **LOW** and open. Census row **2.6**. |
| **What was claimed in our own source** | `VoucherEntryViewModel.VoucherClass.cs` stated the vendor's rule was *"CONDITIONAL on two things, not one: … a voucher class … **AND** if F12 > 'Select common ledger account for Item allocation' is set to **No**"*, and concluded that `ShowStockLedgerPicker` is *"a NARROWER rule than the vendor's"* — i.e. that we **over-hide** the field. `MainWindow.axaml` repeated it. |
| **What the vendor actually publishes** | **[web]** `help.tallysolutions.com/accounting-faq/`, re-fetched by content 2026-10-18. A lead-in, *"The field to select the Sales ledger is not available, if:"*, followed by a **numbered list of two items** — (1) *"You have selected a voucher class to automate the sales details."* (2) *"F12 (Configure) > Select common ledger account for Item allocation is set to No."* **There is no "and" and no "or" on the page.** Two numbered items under "not available if" are two independent sufficient conditions. |
| **What that makes true** | On condition (1) **we match exactly** — we are not narrower there and we do not over-hide. `ClassSuppliesValueLedgers` tests `LedgerAllocations.Count > 0`, which is the right reading of *"to automate the sales details"*: the vendor's own round-off class (`round-off-invoice-and-ledger-values/`) carries an Additional Accounting Entry and **no** Default Accounting Allocation, automates no sales detail, and must keep the field. Pinned by `A_round_off_only_class_automates_no_sales_detail_so_the_value_ledger_field_survives`. |
| **What remains ours** | Condition (2) is unmodelled: we never hide the field for the F12 flag alone. **Recorded as OURS, labelled, and deliberately NOT closed this wave** — see IV-93. |
| **Fix** | Mis-citation corrected in place in both files 2026-10-18, with the struck text kept visible. The residual gap is IV-93's. |

---

### IV-93 · 🔴 ADDED 2026-10-18 — **"Select common ledger account for Item allocation" is not a flag we may ship alone**: it needs per-item accounting allocation, which does not exist here

| | |
|---|---|
| **Severity** | **MEDIUM**, open, and deliberately left open. Census row **2.6**, third clause. |
| **What TallyPrime documents** | **[web]** `help.tallysolutions.com/accounting-faq/` names `F12 (Configure) > Select common ledger account for Item allocation` and states that setting it to **No** makes the common Sales-ledger field unavailable. The vendor's alternative is per-item ledger selection — the Accounting Details the item line then carries. |
| **What we ship** | Nothing for it. Every item invoice in this product resolves **exactly one** value ledger (`SelectedStockLedger`, or the class's `LedgerAllocations`). 🔴 **And the absence is STRUCTURAL, not just a missing screen:** `Apex.Ledger.Domain.VoucherInventoryLine` has **no ledger field of any kind** — its whole surface is `StockItemId`, `GodownId`, `Quantity`, `BilledQuantity`, `Rate`, `Direction`, `BatchLabel`, `UnitId`, `TrackingNumber`, `CostTrackingNumber`. There is nowhere for a per-item ledger to be stored. |
| 🔴 **Why the flag was NOT added** | Adding the switch alone would hide a **required** field and offer no substitute: the invoice would have no value leg and no field left to name one with. That is the unreachable-capability defect this project has shipped before, dressed as a closure. The honest state is an open divergence. |
| **Fix** | Build per-item accounting allocation first (its own row of work, and — because `VoucherInventoryLine` would gain a stored field — **it needs a schema migration**, so it cannot be a side-effect of any track that is forbidden one). Only then does the F12 switch become a two-line gate on top of it. Until then the field is always offered, which is the vendor's behaviour whenever condition (2) does not hold and is never wrong money. |

---

### IV-94 · 🔴 ADDED 2026-10-18 — a voucher class on an **accounting (service) invoice**: no TallyPrime source speaks, so the item-invoice gate is **ours**

| | |
|---|---|
| **Severity** | **LOW–MEDIUM**, open. Census row **2.6**, second clause. |
| **What we ship** | `ActiveVoucherClass => IsItemInvoice ? SelectedVoucherClass?.Class : null`. A class is therefore unreachable on an accounting invoice; `AcceptAccountingInvoice` is a separate path that never consults one. |
| **What TallyPrime documents** | **Nothing, either way — searched by content and stated as a finding.** `voucher-types-tally/` (the product's own Voucher Class page), `accounting-faq/`, `accounting-entry-tally/` and `round-off-invoice-and-ledger-values/` were each opened; none mentions a voucher class on an accounting / service invoice, and none restricts a class to item invoices. The round-off page says only to *"enter the sales details"*. |
| 🔴 **What must NOT be cited for it** | The pages that **do** document a class in accounting-invoice mode are **Tally.ERP 9** — `docs/te9rel60|63|64/Tax_India/Service_tax/Voucher_Class_for_Sales_Accounting_Invoice.htm` and `article/Tally.ERP9/…`. Under ruling 14 ERP-9 is not ground truth for this clone, and those pages must not be used to justify building the path. |
| **Fix** | None taken. Lifting the gate would let a class post money on a screen no TallyPrime page says it governs. Settle the vendor's behaviour by observation first; the gate is then either confirmed or a two-line change. |

---

### IV-95 · 🔴 ADDED 2026-10-18 — the voucher class's **"Remove if Zero"** is keyed, stored and displayed, and is **provably inert**: we always behave as if it were *Yes*

| | |
|---|---|
| **Severity** | **LOW** on money (it can only add or withhold a **nil** line), but **MEDIUM as a capability claim** — the operator sets a switch, sees it read back "Yes"/"No" on the class row, and it changes nothing. Census row **2.6**. |
| **What TallyPrime documents** | **[web]** `help.tallysolutions.com/round-off-invoice-and-ledger-values/`, **verbatim**: *"**Remove if Zero** – Set it to **Yes** to remove the ledger from the invoice if the ledger value is zero."* `/tally-prime/excise-for-importer/ei-configure-vch-class-customs-duty-tally/` carries the same field on an Additional Accounting Entry without explaining it. ⚠️ **Precision: only the *Yes* arm is quoted.** That *No* leaves the nil ledger on the invoice is the **implication** of a switch whose documented purpose is removal — it is not a sentence on the page, and it is marked as an implication here rather than dressed as a citation. |
| **What we ship** | The field is captured (`VoucherTypeMasterViewModel:400`, `:573`), stored (`SqliteCompanyStore:6958`), displayed (`:437`, as "Yes"/"No") — and read in **exactly one place in `src/`**, `VoucherClassPosting.cs:125`. That read is immediately followed by the same unconditional test: `if (amount == Money.Zero && entry.RemoveIfZero) continue;` then `if (amount == Money.Zero) continue;`. **Both arms `continue`, so the flag changes no control flow whatever.** The rounding pass (pass 2) never consults it at all. |
| **Why it is inert, and why that is not simply a bug to delete** | `EntryLine.Amount` is documented *"Magnitude, always > 0 (a zero line is invalid in Phase 1)"* — the ledger model **cannot represent a zero line**, so the *No* behaviour is unreachable by construction, not by oversight. We therefore always behave as *Yes*. |
| 🔴 **No test was written for it** | Pinning the current behaviour would enshrine **our** divergence as fidelity — the failure this project has already made four times. The honest record is this row. |
| **Fix** | Either (a) let `EntryLine` carry a zero-magnitude line so *No* can show the nil ledger, which is a change to the ledger core and far beyond this field, or (b) remove the switch from the master screen and say in the UI that a nil additional entry is never shown. **(b) is the smaller honest option and is a user decision, not ours.** Until one is taken, the switch should at minimum stop being offered as if it did something. |

---

## 2H. ADDED 2026-10-16 — SCHEMA v67: THE AREA-2/3 MASTER-FIELD STORAGE MIGRATION (census 2.4 / 2.7 / 2.9 / 2.11 / 3.7)

> 🔴 **RENUMBERED AT MERGE, 2026-10-18 (wave 46 wave-3, by A12), AND THE SECTION LETTER WITH IT.** This section was authored as **§2G** carrying **`IV-85`…`IV-91`** and then **`IV-92`…`IV-94`**, on a branch cut before `claude/apex-bb-multimaster` (PR #165) and `claude/apex-bk-voucher-class` (PR #167) landed. **BOTH of those had already taken those numbers for entirely different divergences** — `bb` owns §2G and `IV-85`…`IV-91`, `bk` owns `IV-92`…`IV-95`. This section is therefore **§2H** and its ten entries are **`IV-96`…`IV-105`**. **Nothing was dropped from either side; the conflict was resolved hunk by hunk and every register entry on both sides is kept**, and the three cross-references in `BudgetVariance.cs`, `CanonicalCostCategoryAliasRoundTripTests.cs` and `CostCentreBudgetVarianceTests.cs` were moved with them. ⚠️ **THIS IS THE SECOND WAVE RUNNING IN WHICH THE IV REGISTER HIT THIS TRAP** — see the identical note on §2G and `T2-138`. It is the schema-version *"two live tracks both take the next number"* hazard, on a register instead of a migration, and a conflict-free merge would have published duplicate headings rather than flagging it. **Decide up front which track owns the next IV number, exactly as we now do for schema versions.**

**Scope note.** These five came out of **building** the twenty-two master-field columns, so each one names a
line this wave shipped rather than a line a read-only pass observed. Every vendor sentence quoted below was
opened **BY CONTENT** on 2026-10-16 and read off the page. 🔴 **No Tally.ERP 9 page is quoted** — R7 (ruling
14) makes only TallyPrime ground truth. 🔴 **`IV-75` (the base-currency alteration refusal) IS DELIBERATELY
UNTOUCHED BY THIS WAVE.** The vendor contradicts our refusal, that is a **USER DECISION**, and this track's
brief said in terms: build the formatting options and leave the refusal alone. It did.

---

### IV-96 · 🔴 **DECLINED, NOT BUILT: census row 2.10's "no period" is NOT a vendor gap — the Scenario Creation screen has no period field**

| | |
|---|---|
| **Severity** | **NONE as a divergence — this is a CENSUS CORRECTION, and it is recorded here because the alternative was shipping an invention under a fidelity row.** |
| **What TallyPrime does** | `help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/`, opened **BY CONTENT** 2026-10-16: the Scenario Creation screen's fields are **Name**, **Include actuals**, and the voucher types to **Include** / **Exclude**. **There is no period, no From and no To on the master.** The period is chosen on the REPORT the scenario is applied to. |
| **What we ship** | The same three. `scenarios` has exactly `id, company_id, name, include_actuals`. |
| **What was asked for** | The v67 brief named *"a scenario period from/to (2.10)"* as one of the six rows the migration carries, on the strength of census row 2.10's own cell (*"no period"*). |
| **What was done** | **Nothing — and that is the result.** No `scenarios.period_from` / `period_to` column was added. Building it would have been an INVENTION shipped under a fidelity row, and a round-trip test over it would have **pinned the wrong answer** — the failure this project has now hit three times. |
| **Fix** | **Census row 2.10's "no period" clause should be STRUCK by A14/A12**, not carried forward. The absence is pinned by `MasterFieldsV67SchemaTests.The_scenario_master_gains_no_period_because_the_vendor_screen_has_none`, which asserts the four columns exactly, so a later wave adding them has to come to this entry and argue against the page. ⚠️ The row's OTHER clauses (no Alter, no Delete, `Scenario.ExcludeType` with no Desktop caller) are untouched and still open. |

---

### IV-97 · One alias per master, where the vendor says **"multiple aliases"**

| | |
|---|---|
| **Severity** | **LOW.** Nothing is mis-stated on a document; an operator who wants a second alias simply cannot record one. |
| **What TallyPrime does** | `help.tallysolutions.com/cost-centre-or-profit-centre-tally/`, opened by content 2026-10-16, in the Cost **CATEGORY** section as well as the Cost Centre section: *"**Name &amp; alias**: As in other masters, you can specify multiple aliases."* |
| **What we ship** | **ONE** alias. v67 added `cost_categories.alias` (census 2.7 — the field the row was DEMOTED for on 2026-10-04), matching `cost_centres.alias`, `godowns.alias`, `stock_items.alias` and every other master in this product, all of which are single. |
| **Why** | Widening every master from a column to an alias collection is a change across the schema, the readers, the writers, the Io canonical formats and every master screen. It is **not smuggled in under row 2.7**, whose gap was the absence of ANY alias on the category. |
| **Fix** | Its own slice. Recorded so the next reader does not take the single alias for fidelity. |

---

### IV-98 · A cost-centre budget is **one measure per line**, where the vendor's Cost Centre Budget screen carries **three figures per centre**

| | |
|---|---|
| **Severity** | **LOW–MEDIUM. No posted figure moves, but the SHAPE of a cost-centre budget differs, so an operator coming from the reference product must enter it differently.** |
| **What TallyPrime does** | `help.tallysolutions.com/budgets-tally/`, opened by content 2026-10-16: the budget screen offers *"Set **Groups** to **Yes**"*, *"Set **Ledgers** to **Yes**"* and *"Set **Cost Centres** to **Yes**"*, and *"In the **Cost Centre Budget** screen, select the required **Cost Centre**, **Expenses**, **Income** and **Closing Balance**."* — i.e. **three figures against one centre**. |
| **What we ship** | v67 added `budget_lines.cost_centre_id` and `BudgetLine.ForCostCentre`, so the **target** is now faithful (the gap census row 2.9 names is closed). But a `BudgetLine` carries **one** `BudgetType` and **one** amount, so a three-figure cost-centre budget is expressed as up to **three LINES** against the same centre. |
| **Why** | `BudgetLine`'s one-measure shape is shared with the group and ledger targets and with `BudgetVarianceReport`, which emits one row per line. Giving the cost-centre target three columns would have forked the line model. |
| **Fix** | Recorded, not built. ⚠️ Note the related invariant that DID have to change: `BudgetMasterViewModel.SameTarget` now compares `CostCentreId`, because every cost-centre line has `GroupId` and `LedgerId` both null and the two-field test made all of them compare EQUAL — budgeting a second centre would have overwritten the first. Mutation **M2** reddens `Budgeting_a_second_cost_centre_adds_a_line_rather_than_overwriting_the_first`. |

---

### IV-99 · A cost centre has **no opening balance**, so *On Closing Balance* on a cost-centre budget means *cumulative allocations to date*

| | |
|---|---|
| **Severity** | **LOW, but it is an arithmetic reading and therefore worth naming.** |
| **What TallyPrime does** | Names a *Closing Balance* column on the Cost Centre Budget screen (`help.tallysolutions.com/budgets-tally/`, opened by content 2026-10-16) and does not define, on that page, what it is computed over. |
| **What we ship** | `BudgetVarianceReport.CostCentreActualSigned`: `OnClosingBalance` sums every **counted** allocation dated on or before the period end; `OnNettTransactions` bounds it below by the period start. A cost allocation exists only on a posted voucher line — **there is no opening-allocation master anywhere in this schema** — so the two measures differ only by the lower bound. Both measures **consolidate the centre's whole subtree** (BF-2, 2026-10-18 — see `IV-102`). 🔴 **CORRECTED 2026-10-18 (BF-3):** this row used to end *"The voucher filter is the SHARED `LedgerBalances.CountsAsOf`, so the budget's 'actual' agrees with the Cost Centre Summary for the same window rather than disagreeing with it."* **The second half of that was false and is struck.** The filter IS shared; the **sign rule is not**, and the two reports disagree by 8,000 on a measured three-voucher fixture. See `IV-101`. |
| **Why** | Any other reading would require an opening balance the model does not have. |
| **Fix** | Recorded. If a later slice gives cost centres opening allocations, this reading must be revisited. |

---

### IV-100 · The eleven new Voucher Type fields are an **ELECTION ONLY** — v67 stores them and nothing acts on them

| | |
|---|---|
| **Severity** | **MEDIUM as a RISK OF OVERSTATED CLOSURE, which is the failure this register exists to catch — three slices have shipped green holding 34, 42 and 17 defects. Nothing a customer sees is WRONG; what would be wrong is reading census row 2.4 as closed.** |
| **What TallyPrime does** | `help.tallysolutions.com/voucher-types-tally/`, opened by content 2026-10-16, carries all eleven on the Voucher Type screen and **acts on each**: effective dates add a voucher-entry field and drive ageing, *Optional by default* opens a new voucher unposted, the Printing block feeds the printed document, and the Invoice Sharing block really does share. |
| **What we ship** | v67 captures, stores, round-trips and **surfaces on the master** the operator's election on all eleven (`VoucherTypeMasterFields`). It does **NOT**: add the voucher-entry effective-date field or prefer it for ageing; open a new voucher as Optional; change any accounting allocation; send a WhatsApp message (this product's WhatsApp surface is an **offline** document/link builder, census 14.10 — **nothing is transmitted**); generate a payment link or QR code (**none exists anywhere in this product**); or change what the print path emits. |
| **Why** | Each is its own slice. Claiming otherwise is the "overstated closure" defect class. |
| **Fix** | **Census row 2.4 must stay `PARTIAL`.** The *fields* are no longer missing and are no longer zero-hit; the *behaviours* are. Said here, in the service record and on each property, so three independent readers meet it. |

---

### IV-101 · A cost-centre budget's *actual* **nets Dr − Cr**, while the Cost Centre Break-up **sums magnitudes** — the two disagree and **no vendor sentence picks a winner**

| | |
|---|---|
| **Severity** | **MEDIUM. Two of our own reachable reports put DIFFERENT figures against the same cost centre for the same window — 8,000 apart on a three-voucher fixture — and until 2026-10-18 the source comment beside the money asserted that they AGREED.** |
| **What TallyPrime does** | **Not known, and this is the honest answer.** `help.tallysolutions.com/budgets-tally/`, opened by content 2026-10-16 and re-opened 2026-10-18, shows a Cost Centre Budget screen carrying *"Cost Centre, Expenses, Income and Closing Balance"* — **three** figures, splitting debit and credit before any sign question arises. This product carries **one** measure per line (`IV-98`), so the vendor's shape does not map onto either convention and **neither rule can be attributed to the vendor**. The page does not define the Actuals column's arithmetic for a cost centre. |
| **What we ship** | `BudgetVarianceReport.CostCentreActualSigned` signs each allocation by its parent entry line's Dr/Cr side, sums, and the caller reports the magnitude of the **net**. `CostReports.BuildCostCentreBreakup` sums allocation **magnitudes** and never reads the line's side. A centre carrying 4,000 Dr expense and 5,000 Cr revenue in one window is therefore **9,000** on the Break-up and **1,000** on the Budget Variance. Same centre, same window, same `LedgerBalances.CountsAsOf` voucher filter. |
| **Why** | The netted reading falls out of sharing the group and ledger arms' "signed total, then magnitude" convention. It was never chosen against the magnitude reading, because nobody had noticed the two differed — the comment said they agreed. |
| **Fix** | **Recorded, NOT silently resolved.** 🔴 A test pins the disagreement rather than blessing it: `Apex.Ledger.Tests.CostCentreBudgetVarianceTests.A_mixed_sign_centre_nets_while_the_Break_up_sums_magnitudes` asserts the measured 8,000 gap and says in its own doc comment that it is **a divergence tripwire, not a fidelity assertion**. A later wave that rules on the convention must change that test deliberately. ⚠️ **Do not "fix" this by making the two agree until a source says which one is right** — guessing is how row 3.5 lost its promotion. |

---

### IV-102 · A cost-centre budget **consolidates the centre's whole subtree** — vendor-grounded, and it CLOSES a wrong figure rather than opening a divergence

| | |
|---|---|
| **Severity** | **This entry records a FIX, not a divergence. It is here because the behaviour it describes was wrong on the withheld v67 branch and a reader of `IV-98`/`IV-99` needs to know which way it now goes.** |
| **What TallyPrime does** | `help.tallysolutions.com/cost-centre-or-profit-centre-tally/`, opened by content 2026-10-18: *"Such Cost Centres can be created in a hierarchical structure to get a **consolidated** view of the allocation"*; *"You can create multiple Cost Centres (also known as Profit Centres) in a hierarchy to get a consolidated view of the allocations made to each business unit, employee, project, department, and so on"*; and, of the Sales Department worked example, *"Once the hierarchy is created whenever you allocate an expense to any of the employees, **it would also become part of the relevant department**."* |
| **What we ship** | **We now match.** `CostCentreActualSigned` walks `ClassificationRules.CostCentreIsUnder`, so a budget on a parent centre counts every allocation to it **or to any centre under it, at any depth** — the same rule the group arm of the same method uses for ledgers, and the same rule the product's own `CostCentreLine.RolledUpTotal` already used. |
| **Why it is recorded** | 🔴 **Before 2026-10-18 the arm matched `a.CentreId == centreId` with no descendant walk.** On a Delhi(6,000 Dr) + Delhi-North(3,000 Dr) book with a 9,000 budget on Delhi, the Budget Variance report said *actual 6,000, variance −3,000* — **a 3,000 under-spend that did not exist**, on a report a user can open. Nothing caught it because no test anywhere posted an allocation against a cost-centre budget, and the one test that existed asserted `Actual = 0` under a doc comment claiming it took "a real actual from a posted allocation". |
| **Fix** | **Done.** Seven tests in `Apex.Ledger.Tests.CostCentreBudgetVarianceTests` post real vouchers. Mutation **M-BF2** (restore `a.CentreId == centreId`) reddens three of them with the original wrong figure, *Expected 9000.00 / Actual 6000.00*; mutation **M-BF2b** (match any existing centre) reddens six, proving the walk is **directional** and not "every centre in the category". |

---

### IV-103 · The **Default bank** picker is scoped to **bank ledgers**, by OUR predicate — the vendor says "a bank" and nothing more

| | |
|---|---|
| **Severity** | **LOW. It narrows an election (`IV-100`) rather than moving a figure — but the previous behaviour let Sales, Capital or Purchase be a voucher type's "default bank", which is not a thing.** |
| **What TallyPrime does** | `help.tallysolutions.com/voucher-types-tally/`, opened by content 2026-10-18, under *Set up printing options*: *"Select a **bank** in Default bank to print its details on the vouchers."* and *"(Applicable to Sales voucher types)"*. 🔴 **The page says "a bank" and stops there** — it does not say which ledgers the list contains, nor name a group. |
| **What we ship** | The picker lists ledgers under the **Bank Accounts / Bank OD / Bank OCC** chain via `ClassificationRules.IsBankGroup`, plus the "◦ No default bank" row. **The PREDICATE is ours**, not a vendor clause; it is the one Bank Reconciliation, the Cheque Register and the Deposit Slip already share, so one screen cannot drift from another on what counts as a bank. ⚠️ We do **not** scope the field to Sales voucher types as the vendor's parenthesis does, and we do not print the bank's details on anything — the field remains an election (`IV-100`). |
| **Why** | Offering every ledger was the state v67 shipped (BF-5). Picking the existing shared predicate rather than inventing a new rule is the smallest honest narrowing. |
| **Fix** | Recorded. A **saved** non-bank id from a pre-filter book is still offered, captioned *"(not a bank — saved earlier)"*, so an unrelated alteration cannot silently clear it. Mutation **M-BF5** (drop the `Where`) reddens `The_default_bank_picker_offers_bank_ledgers_only` and `A_saved_non_bank_default_survives_being_opened_for_alteration`. |

---

### IV-104 · 🔴 **NOT A DIVERGENCE — A DEFECT, FOUND AND FIXED HERE: v67's Default bank made a book PERMANENTLY UNSAVABLE on the second save**

| | |
|---|---|
| **Severity** | 🔴 **BLOCKING, and it reached neither the builder's report nor the reviewer's finding list.** Set a Default bank on any voucher type; the first save succeeds; **every later save of that company fails and rolls back entirely** — every master, every voucher. The operator cannot save their book again by any route and is told only *"FOREIGN KEY constraint failed"*. |
| **What TallyPrime does** | Not applicable. This is our own storage defect, not a fidelity question. |
| **What went wrong** | `Save` is delete-all-then-reinsert. `DeleteCompanyRows` runs `DELETE FROM ledgers` roughly **25 lines BEFORE** `DELETE FROM voucher_types`, and v67 gave `voucher_types` a `default_bank_ledger_id REFERENCES ledgers(id)`. The first save is fine because the database holds no such row yet. The second finds the stored row pointing at the ledger being deleted. **This is EXACTLY the v62 voucher-class trap already documented twelve lines above the offending statement**, repeated by v67 for a new column. `budget_lines.cost_centre_id`, the other FK v67 added, is safe only by luck of ordering. |
| **Why nothing caught it** | Every v67 test saved such a book **exactly once**. Both new FK columns were put through `MasterDeletionForeignKeyCoverageTests`, which guards *deleting the master* — a different question from the store's delete ORDER during an ordinary save. A green suite proved nothing. |
| **Fix** | **DONE.** `UPDATE voucher_types SET default_bank_ledger_id = NULL WHERE company_id = $cid;` immediately before the ledgers delete — the same child-clear the POS and v47 numbering tables already use. The aggregate rewrites the column moments later, so nothing is lost. Three tests in `VoucherTypeDefaultBankResaveTests`; mutation **M-BF8** (drop the clear) reddens all three with the original `SQLite Error 19: 'FOREIGN KEY constraint failed'`. ⚠️ **Lesson for the next migration that adds an FK:** ask where BOTH tables sit in `DeleteCompanyRows`, and save the fixture TWICE. |

---

### IV-105 · **20 of the 22 v67 fields never reach the canonical transport** — Export → Import silently drops them

| | |
|---|---|
| **Severity** | **LOW-MEDIUM. Silent data loss on a user-reachable Export/Import pair.** It cannot crash and it is not a regression, but a book exported and re-imported comes back missing elections the operator made. **Filed here because it was NOT filed anywhere — that omission is the defect this register exists to catch.** |
| **What TallyPrime does** | Not the question. This is our own transport completeness, not a fidelity claim. |
| **What we ship** | Of v67's five rows, only the **cost-category alias** and the **cost-centre budget target** reach `CanonicalModel` / `CanonicalXml` / `ImportPlan` — the alias because row 2.7 is the row proposed for promotion, the budget target because leaving it out took the WHOLE import down (BF-1). **Dropped on export → import:** the eleven Voucher Type fields (`VoucherTypeDto`), the two godown fields (`GodownDto`), the five currency formatting options (`CurrencyDto`), **and the two company configuration flags `config_allow_storage_of_materials` and `config_use_address_for_locations`** — 11 + 2 + 5 + 2 = **20**. 🔴 **THE HEADLINE READ "18 of the 22" UNTIL A12 RE-COUNTED IT AT MERGE (2026-10-18), AND THE TWO COMPANY FLAGS WERE THE ONES MISSED** — in a register whose §7 is titled *"Coverage, counted rather than claimed"*, so the count is corrected rather than softened. ⚠️ **THE TRAP THAT PRODUCED THE WRONG FIGURE, RECORDED BECAUSE IT WILL BITE THE NEXT COUNTER: an unfiltered `grep -r <FieldName> src/Apex.Ledger.Io/` returns a hit for EVERY one of these names, because it matches the compiled `bin/Debug/net10.0/Apex.Ledger.dll`. Restrict to `--include=*.cs` — source-only, the answer is ZERO hits for all twenty.** The shape IV-105 describes is unchanged and is if anything stronger: **no company feature flag reaches the transport at all** (`EnableJobCosting`, `SetComponentsBom`, `MaintainBatchwiseDetails`, `EnableCostCentres` are all zero hits too), and `CanonicalXml.BuildGodown` carries only id/name/parentId/alias/thirdParty/isMainLocation — no address, no storage flag. |
| **Why** | This is the **established practice** on this codebase rather than a v67 slip: v66's own `RetainOriginalVoucherNo` is likewise absent from the canonical model (0 hits), and `GodownDto` already drops v58's `JobCostCentreId`. The wave that takes a migration has not been carrying its fields into Io. |
| **Fix** | **NOT DONE — recorded, owed, and deliberately not attempted in the same pass as BF-2/BF-4/BF-8.** ⚠️ Note the asymmetry that makes this safe-but-wrong rather than dangerous: a dropped NULLABLE field reads back as null, which is why none of these crash — unlike `BudgetLineDto`, whose missing target dereferenced a `Nullable` and aborted the entire import. **A later wave should sweep ALL of them together, including the pre-v67 arrears, rather than one field per migration.** |

## 2I. ADDED 2026-10-20 — ORDER RETIREMENT (census rows 4.11 / 4.12 / 9.8): THE ONE THING `OrderFulfilment` HAS BEEN ASKING TO HAVE WRITTEN DOWN SINCE WF-8

> 🔴 **NUMBER COLLISION WARNING, READ BEFORE MERGING.** This section is authored as **§2I** carrying **`IV-106`**,
> on a branch cut from `origin/main` **02a0a74**, at which point the highest number in the register is `IV-105`
> (§2H, itself renumbered at merge). **Three sibling tracks were building in parallel when this was written.** If
> any of them also took `IV-106`, renumber this one — the §2G and §2H headers both record this exact trap biting,
> and the instruction there is to decide the next IV number up front, as we now do for schema versions. **Nothing
> here depends on the number.**
>
> **Scope note.** This entry ships **no behaviour change**. It was written because `OrderFulfilment`'s own class
> doc has, since WF-8, said its attribution rule is *"A STATED ENGINEERING CHOICE, NOT A SOURCED TALLY BEHAVIOUR
> (R7)"* and that it is **owed** *"its own row in `docs/invented-vs-cloned.md`, and a user go/no-go on whether a
> derived match may retire an order at all."* That row did not exist. This is it. Both vendor pages were opened
> **BY CONTENT** on 2026-10-20 and every sentence below is read off the page. 🔴 **No Tally.ERP 9 page is quoted.**

---

### IV-106 · Order retirement is a FIFO guess over a `(party, item)` cohort — the vendor retires by the **Order No.** the operator selects per line, and that field exists nowhere in this product

| | |
|---|---|
| **Severity** | **HIGH. Class A. Area: INVENTORY.** It is wrong money on a report the operator reads today, not a missing feature — and the engine cannot tell you which way it is wrong, because which order gets retired depends on the order the book happened to be keyed in. |
| **What TallyPrime does — the ORDER link** | `help.tallysolutions.com/purchase-order-tally/`, Receipt Note steps: **"Select Order No(s) from the List of Orders, if a purchase order exists for that supplier."** · **"The stock items details in the purchase order appear in the receipt note."** · **"The Order No. is prefilled in the Stock Item Allocations screen."** · **"Set the Order No. to Not Applicable."** · on the invoice, **"Tracking No.: and Order No.: will be auto-filled by default based on the receipt note and order number reference."** `help.tallysolutions.com/sales-order-tally/`: **"The Order no. field appears in the Items Allocation screen. It does not appear on the main screen of the invoice."** · **"In the Order Details screen, select the order number. The details from the sales order appear in the sales voucher."** · and the closure rule in the vendor's own words — **"The Sales Order gets closed if completely delivered. Otherwise, it is partially closed, and you can view the details in the Sales Order Outstandings report."** and **"The Sales Order gets closed if completely billed."** **So an order is retired by an explicit, per-line, operator-chosen Order No. — selected from the open orders of that party, and declinable ("Not Applicable").** |
| **🔴 The Tracking No. is a DIFFERENT field and does NOT retire an order** | Stated here because the opposite was assumed at the start of this track, and because census row 9.8's correction text compresses the two into *"the note's Tracking No. / Order No."* as though they were one mechanism. They are not, and the sales page says so in terms (the Order No. is per-order; the Tracking No. identifies the delivery or invoice). The Tracking No.'s attested job is the **note ↔ bill** link: **"Enter a Tracking No. By default, the invoice number appears."** · **"You can select the tracking number of the receipt note to track the received goods in the purchase invoice."** · **"A receipt note is closed if billed completely."** · **"appears as Bills made but goods not received in the Purchase Bill Pending report."** **That is exactly, and only, what `src/Apex.Ledger/Reports/BillsPending.cs` already consumes it for — correctly.** Re-pointing `tracking_number` at order retirement would give one persisted operator datum two incompatible meanings: a **new** invention, not a de-invention. |
| **What we ship** | `src/Apex.Ledger/Reports/OrderFulfilment.cs`, member `Accumulate`. Order lines are bucketed by `(PartyId, StockItemId)`; every fulfilling movement, oldest first, retires the **earliest still-open line** in its own cohort, spilling into the next. `grep -c TrackingNumber` over that file returns **0**, and `grep -c OrderNo` returns **0** — it is a FIFO machine end to end. It feeds `InventoryRegisters.BuildOrders` (the Order Register) and `ReorderStatus` (Purc Orders Pending / Sales Orders Due → Nett Available → Shortfall). **It does NOT feed `BillsPending`** — measured, and recorded because the opposite was asserted when this track was briefed. |
| **What the customer experiences — MEASURED, not reasoned** | One party (*Acme Traders*), one item. **SO-1** 05-Apr, 100 Nos **@ ₹47.33**. **SO-2** 10-Apr, 100 Nos **@ ₹90.00**. A Delivery Note on 15-Apr for 100 Nos, which the operator raised against **SO-2** — the vendor's gesture is to pick Order No. SO-2 in the Items Allocation screen, and this product has nowhere to record that. **Shipped answer:** SO-1 fulfilled 100 / outstanding **0** / value **₹0.00**; SO-2 fulfilled 0 / outstanding **100** / value **₹9,000.00**. **Vendor answer:** SO-1 outstanding 100 = **₹4,733.00**; SO-2 outstanding **0**. **The two rows are exactly INVERTED, and the Sales Order Outstandings value is overstated by ₹4,267.00 — 90.2% high on a ₹4,733 truth.** 🔴 **And note precisely what is NOT wrong in this shape:** the item-level quantity (100 Nos) is identical either way, so `ReorderStatus`'s Nett Available and Shortfall are **unaffected here**. The error is confined to the per-ORDER rows and the money on them — which is the column an operator chases a supplier or a customer with. The class doc's own analysis says the item total *does* move when the **cohort** or the **date ordering** misattributes; this fixture isolates the per-row half. Probe output: `C:/Users/dkpho/OneDrive/Desktop/Apex-Review-Artifacts/bp-order-retirement-measurement.txt`. |
| **Why it is not fixed here — 🔴 THE FIX IS SCHEMA-BLOCKED, AND THE BLOCK IS NAMED RATHER THAN WORKED AROUND** | **There is no order-reference datum anywhere in this product to retire by.** Searched: `src/Apex.Persistence.Sqlite/Schema.cs` (both the fresh-create and the migration bodies), `src/Apex.Ledger/Domain/` and `src/Apex.Ledger.Io/`, source only, `bin`/`obj` excluded. `order_lines` is `id, inventory_voucher_id, line_order, stock_item_id, godown_id, quantity_micro, rate_paisa` — **no order number**. `inventory_allocations` and `voucher_inventory_lines` each carry `tracking_number` and `cost_tracking_number` (v58) and **nothing order-shaped**. The `InventoryVoucher` header carries `Id, TypeId, Number, Date, Narration, PartyId, Cancelled, PostDated, JobWorkOrder` — **no order reference**. The only `order_no` columns in the schema are on **one** job-work table — `job_work_orders`, declared twice (fresh-create `Schema.cs:2055` and the v24 migration body `:3239`); `job_work_order_lines` and `material_order_links` carry none. Its FK (`material_order_links.job_work_order_id REFERENCES job_work_orders(id)`) rejects a purchase or sales order outright. **Carrying the operator's selection therefore needs a new column on BOTH movement-line tables, i.e. a migration.** At `02a0a74` `Schema.CurrentVersion` is **67**. 🔴 **CORRECTED AT REVIEW 2026-10-22 — THIS CELL SAID *"v68 is reserved for `claude/apex-bn-gstr3b-rcm`"* AND THAT WAS FALSE.** Measured first-hand: that branch does **not** touch `Schema.cs` (empty `git diff origin/main...` on that path) and reads `CurrentVersion = 67`, and a sweep of **every** remote branch found **no claimant for v68 anywhere**. **v68 is FREE.** The block on this track is therefore the **SIZE** of the change — two movement-line tables, an Order No. picker, the engine arm and the canonical transport — **not a version collision**; the track **stopped at that boundary and built the non-schema half**, which remains the right call for the stated reason and the wrong one for the reason originally given. |
| **Fix — the whole of it, so the next wave does not have to re-derive it** | (1) **Schema vNN:** `order_voucher_id TEXT NULL` + `order_line_index INTEGER NULL` on `inventory_allocations` **and** `voucher_inventory_lines` — an identity link rather than the vendor's display string, so the retirement cannot be broken by renumbering; the vendor's rendered *Order No.* stays a presentation of the linked order. (2) **UI:** an Order No. picker on the Items Allocation row listing that party's open orders (the vendor's *"List of Orders"*), prefilling the order's item/quantity lines, with **"Not Applicable"** as an explicit choice. (3) **Engine:** `OrderFulfilment.Accumulate` retires the **linked** line and nothing else; an unlinked movement retires **nothing** — which is the pre-WF-8 figure for that order, i.e. a bounded and explainable gap rather than an unbounded invisible misattribution. (4) **Carry both columns into `CanonicalModel`/`CanonicalXml`/`ImportPlan` in the same pass** — see `IV-105` for what happens when a migration does not. (5) **Then, and only then,** the three residuals the class doc enumerates (the cancelled-order cohort exclusion, the duplicate-document per-order spill, the un-netted Rejection In) collapse, because each of them is the FIFO walk guessing. |
| **🔴 USER GO/NO-GO — this is the one `OrderFulfilment.cs` has been asking for** | Its class doc asks for *"a user go/no-go on whether a derived match may retire an order at all"*. Put plainly, with the figure above attached: **until the Order No. link is built, do we keep retiring orders on a guess (today: per-order rows can be exactly inverted and the outstanding value 90% wrong, item totals usually right), or do we stop retiring derived matches and go back to reporting every order fully outstanding (always wrong, but wrong in one known direction and never inverted)?** This register's standing view is that a bounded, explainable error beats an unbounded invisible one — but **this is the user's call, not ours**, and it has been open since WF-8. |

### IV-107 · The shared report surface folds its five GST reports over the **primary registration only**, and no vendor page says the reference product does that

| | |
|---|---|
| **Severity** | **MEDIUM. Area: RPT.** It is a NARROWING, disclosed on the face of the report — not a wrong figure. It replaces something strictly worse: on a multi-registration book the same five reports were previously **unreachable** (two refused, three threw an unhandled `InvalidOperationException` out of `ReportsViewModel`'s constructor and took the shell down). |
| **What we ship** | `src/Apex.Desktop/ViewModels/ReportsViewModel.cs`, member `GstScopedRegistrationId` — `_company.Gst?.PrimaryRegistration?.Id ?? GstRegistration.PrimaryId`, passed to Tax Analysis, GSTR-1, GSTR-3B, GSTR-9 and GSTR-9C. The narrowing is disclosed by `GstRegistrationScopeNote`: a first grid row on the three `Rows`-based reports, a footnote on the two matrix annual returns, naming the GSTIN folded and pointing at **GST Offline Returns** for another registration's return. |
| **What is vendor-attested, and exactly how far it goes** | That a multi-registration taxpayer **selects** the registration: *"If you have multiple registrations, select the required GST Registration"* — `help.tallysolutions.com/upload-gstr-1/`, opened by content and quoted in `GstOfflineReturnsViewModel.BuildJson`. **That is the whole of it.** 🔴 **NO claim is made here that any vendor page describes a report surface defaulting to the primary registration, and none was found.** What was searched is named rather than left implicit: the GSTR-1, GSTR-3B, GSTR-9, GSTR-9C and keyboard-shortcut pages already cited elsewhere in this repository. **This is NOT a universal negative** — it is a statement about those pages. |
| **Why ours and not the vendor's** | This surface has **no F3 registration control**, and adding one is a shell change rather than a report change (the refusal note on `ReportKind.ItcSetOffProjection`'s block says the same of GST Amendments / ITC Reversal / the ITC Gate, and that note is still true). Defaulting to the primary is the repository's own established default for every screen that *does* have a picker — `GstAmendmentsReportViewModel`, `GstOfflineReturnsViewModel`, `ItcGateReportViewModel`, `ItcReversalReportViewModel` and `PostItcReversalViewModel` are each `_selectedRegistration?.Id ?? GstRegistration.PrimaryId`. |
| **What is NOT given up** | `GstReportSupport.EnsureRegistrationScoped` is **untouched**, so a silent fold over every registration — the one outcome that would be a wrong filed figure rather than a narrower true one — remains impossible. Re-asserted directly, not assumed: `GstReportSurfaceRegistrationScopeTests.The_engines_own_refusal_still_stands_for_a_caller_that_names_nothing`. |
| **ER-13** | A single-registration book is byte-identical and that is structural, not hoped for: `GstConfig.FindRegistration` normalises `null` and `GstRegistration.PrimaryId` to the same registration, and `GstReportSupport.RegistrationOf` reads a voucher's `null` registration as `PrimaryId` — so naming the primary and naming nothing select the same voucher set and resolve the same GSTIN. No disclosure row appears on such a book. |
| **The fix that retires this entry** | An F3 registration picker on the shared report surface, carried in `ReportOptions` so a saved view remembers which registration it was taken under. Until then the primary-only scope and its disclosure stand, and **the scope belongs to the operator's choice, not to us** — so whether the surface should instead keep refusing is a **USER call** this track did not take. |

> 🔴 **RENUMBERED AT MERGE BY A12, 2026-10-22 (wave 5) — `IV-107` → `IV-117`, AND THE REASON IS A COLLISION
> THAT ACTUALLY HAPPENED, NOT A PRECAUTION.** This section was authored as **`IV-107`** on a branch cut from
> `origin/main` **9ddeec5**, where the highest number in the register is **`IV-106`**, so `IV-107` was
> genuinely the next free number *as this tree could see it*. **Three sibling tracks of the same wave each
> reached that same conclusion independently** and all three took `IV-107` —
> `claude/apex-bs-gstr1-tables` (registration scoping) and `claude/apex-bq-comparative` (`IV-107` **and**
> `IV-108`) besides this one. The wave's pre-assigned disjoint blocks are **bs 107–116, bt 117–126, bu
> 127–136, bq 137–146**, so this section takes the first number of **its own** block and `bs` keeps `107`.
> **Nothing on `main` is touched or dropped**, and the three citing sites — the register heading, the
> `OpenVoucher` reader comment and the `PrintAfterSavingTests` class doc — were moved together, so the
> citation still resolves by content. ⚠️ **This is the THIRD wave running in which the same trap has bitten,
> and the pattern is now mechanical rather than unlucky: a branch measures "the highest IV on main" correctly
> and is still wrong, because its siblings are measuring the same thing at the same moment.**

### IV-117 · `Print voucher after saving` opens the printing screen on ONE save path — the accounting voucher-entry screen. The alteration, duplicate, inventory-entry and POS save paths print nothing

> 🔴 **SUPERSEDED IN PART 2026-10-26 (wave 7, track `dv`) — THE DUPLICATE, INVENTORY AND POS DOORS ARE NOW WIRED;
> see IV-195.** Only the ALTERATION door remains unwired, for the reason given below, and that half of this
> entry stands. The measurement in *What we ship* (four doors print nothing) is kept as the record of what
> was true on `main` before wave 7, not as a current statement.

| | |
|---|---|
| **Severity** | **LOW. Class C. Area: ENT.** A deliberate narrowing of a flag that, before this wave, did nothing at all on any path (census 5.11). Nothing is mis-stated and no money moves; the operator who alters a voucher simply presses Ctrl+P themselves, which is the route the detail column has always offered. |
| **What TallyPrime does — the ATTESTED part, which we DO clone** | `help.tallysolutions.com/voucher-types-tally/`, opened and verified BY CONTENT 2026-10-22: **"Enable Print voucher after saving to automatically open the Voucher Printing screen for the voucher, once it is saved."** The same page lists the option under the voucher type's *Set up invoice sharing options* group beside *WhatsApp voucher after saving* and *Generate Payment link/QR code after saving voucher*. 🔴 **CORRECTED BY REVIEW 2026-10-22 — A SCOPING QUOTE THAT WAS CITED HERE DOES NOT EXIST.** This row, the reader's comment in `MainWindowViewModel` and `PrintAfterSavingTests` all quoted the page as scoping the option to *"vouchers/invoices created using that particular voucher type"*, and gave that quote as the GROUND for wiring creation only. **The substring `created using` does not occur on that page** — measured 2026-10-22 by three separate content fetches of `help.tallysolutions.com/voucher-types-tally/` plus a web search for the exact phrase, which returned nothing on any vendor host. What the page actually shows is the opposite emphasis: of the three options in that group, only *Generate Payment link/QR code* carries a voucher-kind restriction (Sales, Debit Note, Delivery Note, Journal, Point of Sales, Sales Order); **`Print voucher after saving` carries no stated restriction of any kind.** The narrowing below is therefore OURS, labelled as ours, and is not vendor-attested. |
| **🔴 What no source we found settles** | **Whether re-saving an ALTERED voucher of that type re-opens the printing screen.** The sentence *"once it is saved"* is unqualified and would cover an alteration. ~~the scoping phrase *"created using that particular voucher type"* reads the other way~~ — **struck: that phrase is not on the page.** We searched the voucher-types page, the keyboard-shortcuts reference, the Day Book page and the sales-of-goods guide; **none of them addresses the alteration re-save**, and we are not asserting a universal negative beyond those four pages. |
| **What we ship** | `MainWindowViewModel.OpenVoucher(VoucherType, …)` wraps the caller's `onSaved` and calls `OpenVoucherPrintingAfterSave` when `type.PrintAfterSaving` is set. 🔴 **THAT IS THE ONLY READER IN THE PRODUCT** — `grep -rn "PrintAfterSaving" src/` (bin/obj excluded) returns exactly one site that changes behaviour — the `if (type.PrintAfterSaving && …)` inside `MainWindowViewModel.OpenVoucher(VoucherType, DateOnly?, Action?)`'s wrapped `onSaved` (cited by member, not by line: T1-63). **Four other save paths post a voucher and print nothing**, and the checkbox is offered on every voucher type with no base-type gate — it sits in the Voucher Type page's unconditional `WrapPanel` in `MainWindow.axaml`, the `CheckBox` captioned *"Print voucher after saving"*, beside *"Activate this Voucher Type"* — so an operator can tick it on any of them: (1) `ShowVoucherAlteration` — the alteration door, deliberately not wired, reasons below; (2) **`ShowVoucherDuplicate` (Alt+2, "Duplicate Vch")** — whose own column is titled *"Accounting Voucher Creation — … (Duplicate)"* and which posts a genuinely NEW voucher of that type, so this one is a creation door and its omission is a plain gap, not the alteration judgement call; (3) **`OpenInventoryVoucher(VoucherType, …)`** — the structurally identical inventory sibling 80 lines further down the same file, covering Delivery Note / Receipt Note / Stock Journal / Physical Stock / the orders / Rejection In-Out; wiring it needs more than a copied line, because an inventory voucher resolves through `FindInventoryVoucher` and `InventoryVoucherDetailViewModel`, not the `VoucherDetailViewModel.BuildPrintPreview` this reader uses; (4) `PosBillingViewModel` (a POS bill's own save path). **Measured at runtime, not inferred:** a review probe ticked the flag, posted through the wired door (preview opened), then duplicated that voucher through the real Alt+2 route and accepted — a second voucher of the same type was committed and `PrintPreview` came back null; the same probe ticked the flag on a Delivery Note type, posted through `OpenInventoryVoucher`, and `PrintPreview` came back null. |
| **Why we chose the narrower half — OURS, on OUR reasoning, with no vendor wording behind it** | One reason, about documents that leave the building: an alteration re-save under the SAME live voucher number would push a SECOND document for one number out of the automatic door, and this register's own `VoucherDetailViewModel.Refresh` entry records what re-issuing a superseded document under a live number already cost this product once. Where the readings disagree we take the one that prints fewer documents without being asked — a missing print is a keystroke away, an unwanted issued document is not. **A second reason used to stand here — "the narrower reading is the vendor's own words rather than our convenience" — and it is STRUCK, because the words it rested on are not on the page (see the corrected attestation row above).** The judgement survives on its own merits; the attestation does not. **It does NOT extend to the duplicate and inventory creation doors**, which are an unclosed half of the row rather than a decision. |
| **How to de-invent it** | One line: call the same `OpenVoucherPrintingAfterSave(entry.SavedVoucherId)` from `ShowVoucherAlteration`'s `onSaved` behind the same `type.PrintAfterSaving` test. `SavedVoucherId` is already written on the alteration exit (`VoucherEntryViewModel`, the `replacement.Id` site), precisely so this needs no new plumbing. Do it if a vendor page is found that shows the printing screen opening after an alteration, or on a user ruling. |
| **Tests** | `tests/Apex.Desktop.Tests/PrintAfterSavingTests.cs` — four tests, with a CONTROL first so "the preview opened" cannot pass by opening on every save. The creation arm is pinned; the alteration arm is **not** pinned either way, deliberately, so de-inventing it does not have to fight a test that asserts the narrowing is correct. |

## 2J. Wave 46 wave-4 — the comparative engine (census 11.14 / 11.17, cluster C3)

> 🔴 **THIS SECTION'S NUMBERS COLLIDED **TWICE**, AND THE SECOND COLLISION IS THE ONE THAT MATTERED. RENUMBERED
> AT MERGE BY A12, 2026-10-23: section **`2I` → `2J`**, and **`IV-107` → `IV-137`, `IV-108` → `IV-138`.** The
> history below is kept rather than rewritten, because the *sequence* is the lesson.
>
> **First collision, recorded by the branch itself.** The entry was authored on `claude/apex-bq-comparative`,
> cut from `origin/main` `02a0a74`, and took **`IV-106`** as "the next free number *as this branch sees main*" —
> the exact state §2G and §2H were each in when they took numbers another branch had already claimed. The
> order-retirement branch landed first, `origin/main` `9ddeec5` took `IV-106`, and this branch moved itself to
> **`IV-107`** having correctly measured that 106 was then the highest.
>
> **Second collision, found by A12 before any of the four wave-5 branches merged.** `IV-107` was ALSO taken, by
> `claude/apex-bs-gstr1-tables`, and `IV-108` would have been free only by luck. **Three sibling tracks each
> measured "the highest `IV-` on main is 106" correctly and independently, and all three were still wrong,
> because they measured at the same moment.** The wave's pre-assigned disjoint blocks are **bs 107–116,
> bt 117–126, bu 127–136, bq 137–146**; `bs` keeps `107`, `bt` already moved to `117`, `bu` allocated none, and
> this section — the last of the four to land — takes the first two numbers of **its own** block.
>
> **Nothing on `main` is touched or dropped**, and that is verified rather than asserted: at this merge the
> register's lines 1–2418 and everything from `## 3. Grouped by area` onward are **byte-for-byte identical to
> `origin/main`**, and this section is carried over **byte-for-byte** from the branch with only the four
> citations renumbered. Those citations move together — this heading, the two `### IV-` headings, and
> `ComparativeReport`'s `PeriodicColumns` doc-comment — so each still resolves **by content**.
>
> ⚠️ **This is the FOURTH wave running in which the same trap has bitten, and the pattern is mechanical rather
> than unlucky: a number read off a `main` that four branches are racing is a GUESS until the last of them
> merges. The pre-assigned block is the fix; what failed was that three branches did not use theirs.**

> ⚠️ **RESTATED HERE SO IT IS NOT LOST — THE Alt+N / Alt+O CHORD CONFLICT IS STILL UNRESOLVED, AND THIS
> SECTION WIDENED EXACTLY THAT FEATURE.** Census row 11.17 records it as a constraint on whoever takes the row:
> `help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/` gives **`Alt+N`** for
> auto-repeat, while `help.tallysolutions.com/working-with-reports/` gives **`Alt+O`** — *"To display reports
> details by repeating columns based on the selected periodicity"* — and lists **`Alt+A`** as *"To alter the
> parameters in the newly added column"*. **Two vendor pages, two chords**, and the row says *"resolve against a
> third page before binding either"*. **That third page was NOT opened by this section and the conflict is NOT
> resolved by it.** We ship `Alt+N`, which matches the shortcut reference — the better-scoped of the two pages,
> and the chord that was already shipped before this wave, so binding it to two more report kinds widened an
> existing choice rather than making a new one. **`Alt+A` does not exist in this product at all.** Neither is a
> divergence this section is entitled to close; both are owed to row 11.17, which stays `PARTIAL` over them.

### IV-137 · **Auto-column bucket anchoring where no vendor page states one** — Alt+N daily / weekly / fortnightly / yearly

| | |
|---|---|
| **Severity** | **LOW. A presentation choice on a new, additive axis, labelled rather than hidden.** No figure is wrong under any anchor — the buckets partition the chosen period exactly, so the column values always sum back to the single-column report. What an anchor changes is *where the boundaries fall*, and therefore which column a given voucher lands in. |
| **What TallyPrime does** | **Partly attested, partly silent — and the silence is the divergence.** Both vendor pages, opened by content 2026-10-20, state the *list* of periodicities but not the *anchor*: `help.tallysolutions.com/funds-flow-report-tally/` gives *"add periodic columns like monthly, quarterly, weekly, and so on for a comparative view of the funds flow across periods"*, and `help.tallysolutions.com/cash-flow-and-projection-report-tally/` gives *"you can configure the columns to appear quarterly, weekly, daily, fortnightly, and so on"*. ⚠️ **Neither page says where a week starts, and no page we have opened does** — stated as a bounded claim, not a universal negative: we searched the two report pages and the shortcut reference, not the whole site. `Yearly` is not in either list; both lists end in *"and so on"*, so it is an extension rather than a contradiction. |
| **What we ship** | **Months and quarters are anchored to the CALENDAR; days, weeks, fortnights and years are fixed-length blocks measured from the chosen period's own first day.** The split is not arbitrary: where the calendar itself defines the unit we borrow it, and where it does not — a "week" has no vendor-stated start day, and a financial year is not a calendar year — we anchor to the window the operator actually chose rather than invent a convention. 🟢 **The calendar-quarter choice is invisible for the standard Indian financial year:** for a 1-April start the four calendar quarters (Apr–Jun, Jul–Sep, Oct–Dec, Jan–Mar) and the four FY quarters coincide exactly. That is *why* it was chosen, and it is pinned by a test that asserts the quarter boundaries for FY 2024-25. |
| **🔴 The CAPTION is ours too, and it was NOT recorded here until the review said so** | **On a product whose financial year starts 1 April, the FIRST quarter of the year is captioned `"Q2 2024"`** — and `ComparativeFlowKindsTests` pins `Assert.Equal("Q2 2024", quarters[0].Label)`. Verified here rather than taken on report: `CalendarQuarterBlocks` computes `firstMonthOfQuarter = ((period.From.Month - 1) / 3) * 3 + 1`, which is **4** for an April start, and labels `Q{(cursor.Month - 1) / 3 + 1}` = **Q2**. So FY 2024-25 reads `Q2 2024 · Q3 2024 · Q4 2024 · Q1 2025`. **No figure is wrong** and the caption is exactly consistent with the calendar anchor this entry documents — but this entry documented the ANCHOR and said nothing about the CAPTION, and an Indian accountant reading a financial-year comparative expects `Q1..Q4`. **RECORDED RATHER THAN RELABELLED, deliberately:** relabelling to Q1..Q4 of the chosen window would make the caption disagree with the anchor the buckets are actually computed from, which is a second divergence rather than a de-invention, and no vendor page we have opened states either convention. The two options are the reviewer's own; this is the conservative one. |
| **Why** | The alternative was to pick an anchor silently. Three of the four unanchored periodicities had no shipped behaviour at all before this wave (only Monthly and a by-scenario axis existed), so there was no precedent to follow and nothing to regress. |
| **Fix** | **None owed unless a vendor page is found that states an anchor.** If one is, the change is confined to the four private block helpers in `ComparativeReport` (`FixedBlocks`, `YearBlocks`, and the two calendar ones) and to the two bucket-count tests — `PeriodicColumns` is the single axis every caller goes through, so no call site moves. The partition invariant (no gap, no overlap, first bucket opens the window, last closes it) is asserted for **every** periodicity and is anchor-independent, so it would survive the change and catch an error in it. |

### IV-138 · **Printing a report too wide for any sheet by splitting its COLUMNS across sheets** — the stub column repeated, the slice named in the sub-title

| | |
|---|---|
| **Severity** | **LOW-MEDIUM. Class: PRESENTATION, on the cure for a wrong-money defect.** No figure is wrong under it — it exists precisely because without it the figures were wrong. What is OURS is the *shape of the answer*: how many sheets, which columns land on which sheet, that the Particulars column repeats, and the wording that names the slice. |
| **What TallyPrime does** | ⚠️ **NOT RESOLVED AGAINST A TALLYPRIME PAGE, AND I AM NOT CITING ONE. Here is exactly what I searched, 2026-10-22.** `help.tallysolutions.com/print-invoices-reports/` — the current TallyPrime printing page — was opened by content and carries **nothing** about a line that exceeds the page width; so did the TDL `developer-reference/procedural-capabilities/what-are-print-related-functions-in-tdl/` page. A site search surfaced the claim *"In case the entire printable line does not fit the specified page width, it is split into parts... Each part is identified with an alphabet and associated to the respective page numbers, such as 1A and 1B"*, **but every page that carries it is Tally.ERP 9 documentation** (`article/Tally.ERP9/Reports/Printing_Reports/Multi_Part_Printing.htm`, which banners *"Tally.ERP 9 Release 6"*, and the `docs/te9rel49` / `te9rel54` / `te9rel65` trees). 🔴 **Tally.ERP 9 is not ground truth on this project, and a search engine's summary is not a citation** — it asserted that "the TallyPrime reference repeats the core point" and I could **not** reproduce that by content on any TallyPrime page. So the resemblance is recorded as a LEAD for whoever can open a TallyPrime printing-configuration page, and **not** as attestation. This is a bounded statement about four pages I actually opened plus one site search, not a claim about the whole site. |
| **What we ship** | `ReportPdf.SplitWideColumns`. When a document's own floors — each figure column's full requirement plus each prose column's requirement capped at the prose floor — exceed the **widest** sheet the configuration could choose, the columns are dealt into consecutive groups, **column 0 repeated on every group**, and each group becomes one document of the existing multi-document job (so page numbering runs across the whole thing and the preview pane shows the same sheets the paper carries). The stub is budgeted **one third of the sheet** — derived from the page, not guessed — rather than its 60pt minimum, so the labels stay readable on every sheet instead of buying one fewer sheet with every row's legibility. Each sheet's sub-title gains `"columns 2-7 of 14"`, 1-based over the whole document: **a sheet of a split document that does not say which slice it is cannot be collated, and an operator holding sheet 2 would have no way to know sheet 1 exists.** Ours too is the ORDER (all of part 1's row-pages, then all of part 2's) and the absence of Tally.ERP 9's `1A / 1B` part-letter numbering — our footer numbers sheets straight through. |
| **🔴 Where it deliberately does NOTHING** | **Pre-printed stationery is never split**, for the same reason `FitOrientation` never turns it: the sheet already carries a letterhead and a ruled column grid the operator aligned the tray to, and dealing the columns onto two of those sheets puts every figure under the wrong printed rule. **A caller that pinned the page with `AutoFitOrientation = false` is never split either** — `PageConfigWithOrientationTests.Pinning_the_orientation_keeps_portrait_even_when_the_figures_will_cut` asserts the clip there as *"the honest consequence of pinning the page"*, and adding sheets behind that caller's back would be this change quietly re-answering a question another slice already answered. Both residuals are the ones census 12.1 is PARTIAL over, and they are **not cured** here. 🔴 **CORRECTED BY THE W5 REVIEW, 2026-10-22 — the sentence that stood here also said they are "not widened", and that is measurably FALSE.** Before this change the comparative grid never reached the print projector at all (both projectors read `vm.Rows`, the single-column rows), so no configuration could produce a fourteen- or fifty-five-column document and neither residual was reachable from a comparative report. It now is, by **one click**: `PrintPreviewViewModel.BuildConfig` sets `AutoFitOrientation = false` as soon as the operator *touches* the preview's Landscape box in **either** direction, so both of its states disable the split. Measured on the reviewer's own fixture (A4, Cash Flow, FY2023-24, three long-named counterparties, seven-figure sums), reading the `Tj` operands of the real PDF: **ticked Landscape** — Monthly 49 of 63 figures not drawn whole (money drawn `11,71,354....`), Weekly every body cell drawn as `...`; **unticked (pinned portrait)** — Monthly 62 of 63 lost, Weekly every figure drawn as a **bare single digit with no ellipsis** (a cell holding ₹91,83,472.19 drawn as `9`); **pre-printed** — Monthly 62 of 63 lost. The damage is preview-visible (the pane splits through this same method, so paper and preview agree) and the three head-less PDF paths — Export → PDF, the e-mail attachment and the WhatsApp share — all build a default `PageConfig` and therefore do split correctly. **What is owed, and is NOT decided here:** the pinning justification above is an argument about PORTRAIT only, and that is all `Pinning_the_orientation_keeps_portrait_even_when_the_figures_will_cut` asserts. Splitting changes the number of sheets, never their shape, so a caller that pinned **Landscape** would have its choice honoured exactly by a split — there is no shipped assertion against it and no argument offered for it. Resolving that is a design call for the next wave, not a correction a reviewer may make inside another slice's contract. |
| **Why** | A comparative column multiplies every figure a report shows, and Alt+N monthly over a financial year is fourteen print columns of which thirteen must each hold a rupee figure. No allocation of A4 landscape's 769.89pt fits that, so the renderer took its own documented "not even the floors fit" branch and the printed page came out unreadable — every Particulars cell drawn as `"..."`, every figure as `"37,000...."` **with the In/Out suffix clipped off, so an outflow printed identically to an inflow**; weekly drew single characters and daily produced a 1142-byte sheet. **The answer to "the figures do not fit across the page" is more page, not smaller figures.** The alternative — refusing to print a wide comparative at all — would have been a divergence too, and a worse one. |
| **Fix** | **None owed on the mechanism.** What IS owed, and is the reason this entry exists rather than a silent commit: if a TallyPrime page is found that states the part-letter numbering (`1A`, `1B`) and the part-at-a-time print order, our straight-through sheet numbering and our group order both become divergences from an attested behaviour, and `PageConfig`'s F10 page range would need to accept part-qualified entries. That is a self-contained change at `ReportPdf.SliceColumns` and the footer, plus `PageConfig.IncludesPage`. |

### IV-139 · **The restore target is asked for as a COMPANY NAME; the vendor asks for a PATH** — "Restore into", not "Restore Data Path"

| | |
|---|---|
| **Severity** | **LOW-MEDIUM. Class: INPUT SHAPE on a destructive action.** It is the field through which an operator aims a restore that overwrites a whole book, so the shape of the question is worth recording rather than assuming. 🔴 **CORRECTED 2026-10-25 (A12 review w6, F1) — this cell used to say "no figure is wrong and nothing is lost under it", and that was FALSE.** A restore is a file-level swap that keeps the company row inside the archive, and every later save derives its file from that row's NAME. Restoring "Alpha Co"'s archive into the name "Alpha Co Restored Copy" therefore wrote a file holding a company called "Alpha Co"; the shell opened it, and the next save replaced the ORIGINAL "Alpha Co" book — measured: 4 vouchers came back as 1. **Now refused:** `RestoreCompanyViewModel.Apply` refuses, before anything is touched, a target that does not land on the archive's own book file, and re-checks the loaded company row after the swap (undoing the restore if they disagree). Pinned by `RestoreOnFreshInstallTests.Restoring_under_a_different_name_is_refused_and_the_original_book_keeps_every_voucher`. 🔴 **And the redirect that remains (open book → archive's own book) moved the DISPLAYED book without moving the save target** (A12 re-review w6, N1): the shell kept the previous book's `OpenEntry`, so Data Vault on the restored company vaulted the other book and the next save wrote into that vault. Fixed in `d930b00` with the vault-session half (N2); pinned by `RestoreOpenBookSessionTests`. |
| **What TallyPrime does** | **Attested, opened by content 2026-10-23.** `help.tallysolutions.com/restore-company-data/` reaches the screen as *"Press **Alt+Y** (Data) > **Backup & Restore** > **Restore**"* and asks the destination as a **path**: *"This is the location where your company data will be restored"*, which the operator may change. The same page handles a name collision at the destination with a two-way choice — *"If a file with the same name already exists in the destination, you will see options to **Skip** or **Replace**"*, where *"Select **Replace** to overwrite the existing folder with the backup"* and *"Select **Skip** to cancel the restore for the company"*. |
| **What we ship** | A **"Restore into"** free-text box bound to `RestoreCompanyViewModel.TargetCompanyName`, from which the path is **derived** (`_storage.PathForName`) and shown read-only beneath it. So the operator names the book; we choose the file. A `KnownCompanies` list sits under the box — empty on a fresh install, which is why the box is free text rather than a pick-list. **The name must be the archive's own company** (or one that lands on the same file — a difference of case or of a character the file system cannot hold): the panel says so before confirmation, and Apply refuses any other. The redirect the box offers is therefore away from the OPEN book onto the archive's book, not a rename. **Restoring an archive as a differently-named copy is NOT offered** — it would mean renaming the restored company, which the vendor page does not describe and which is a product decision not taken here. |
| **Why** | Our books are not folders. The vendor's data path addresses a company **directory**; ours is one SQLite file per company inside a single companies directory, and every write derives its file from the company's name. Asking for a raw path would let an operator restore a book to a file its own name does not resolve to — the F1 shape above, by another door. Asking for the *name* keeps the one identifier the rest of the app addresses books by. (An earlier version of this cell said a path would leave a restored book unlisted "with no way to open it again", which `RegisterPlain` prevents. That was overstated: `CompanyRegistry.Load` adopts any unregistered plain `.db` in the companies directory on its own, so such a book would be listed under its file name; `RegisterPlain` only sets the display name — A12 review w6, F5.) |
| **Fix** | **None owed unless the storage model changes.** If per-company directories are ever adopted, this box becomes the vendor's path field directly and `PathForName` disappears. Confined to the one `TextBox` in `MainWindow.axaml`, `TargetPath` and `TargetCompanyName`. |

### IV-140 · **Restore is reachable from Company Select, a route no vendor page we opened describes**

| | |
|---|---|
| **Severity** | **LOW. Class: REACHABILITY, additive.** It removes no route and changes no behaviour; it adds a second door to a screen the attested door also still opens. |
| **What TallyPrime does** | **One route is attested and we ship it:** *"Press **Alt+Y** (Data) > **Backup & Restore** > **Restore**"* (`help.tallysolutions.com/restore-company-data/`, opened by content 2026-10-23). ⚠️ **Whether the vendor ALSO offers Restore from its company-selection screen is something I did not establish, and I am not claiming either way.** I opened that one page plus the three print pages this wave needed; I did not search the company-selection or data-path pages. **This is a bounded statement about what I read, not a universal negative** — it is recorded here as ours because it is unattested *to me*, not because the vendor is known to lack it. |
| **What we ship** | A **"Restore Company"** row on Company Select, in addition to the attested Data-menu route, and `BuildDataColumn` now offers Restore with **no company open** (Backup still requires one, because Backup copies the open book). |
| **Why** | Census 13.2 recorded two independent locks that together made Restore unreachable on a machine with zero companies — `ShowDataMenu` bounced to Company Select, and `OpenRestoreCompany` returned early with no company. Removing both is what closes the row, and **Company Select is the screen a machine that has just lost its only company actually lands on**: leaving the only door on a menu the operator has to know to go looking for would have closed the row on paper while leaving the disaster-recovery case awkward in practice. The user ruling that reopened this row gave the reason — *a filing product that cannot restore a company book loses the entire accounting history*. |
| **Fix** | **None owed on the Data-menu route, which is attested.** If a vendor page is found that enumerates Company Select's rows and this one is absent, delete the row — it is a single entry in the Company Select builder and one test; the attested route is untouched by its removal. |

### IV-141 · **A confirmation tick and a CREATE/REPLACE notice in place of the vendor's Skip / Replace choice**

| | |
|---|---|
| **Severity** | **MEDIUM. Class: DESTRUCTIVE-ACTION GUARD.** Both designs stop an accidental overwrite; they stop it with different gestures, and a guard is exactly the kind of thing that should not drift from the source silently. |
| **What TallyPrime does** | **Attested** (`help.tallysolutions.com/restore-company-data/`, opened by content 2026-10-23): the collision is resolved by an explicit two-way choice *at the moment of collision* — *"you will see options to **Skip** or **Replace**"*, Replace being *"overwrite the existing folder with the backup"* and Skip *"cancel the restore for the company"*. |
| **What we ship** | The question is asked **before** the operator commits rather than at the moment of collision: `TargetExists` drives a one-line `TargetEffectNote` reading either *"'X' already exists on this machine and will be REPLACED. This cannot be undone."* or *"'X' does not exist on this machine and will be CREATED from the backup."*, and Apply refuses until `Confirmed` is ticked. **Re-pointing the target disarms the tick** — the tick means "I accept that *this* book is replaced", and carrying it across a change of target would let an operator who confirmed over one book press Restore and destroy another he never confirmed. There is no Skip, because with one target there is nothing to skip *to*: cancelling is leaving the screen. |
| **Why** | The vendor's Skip exists because its Restore can process **several** companies in one action, so "skip this one, continue with the rest" is a meaningful answer. Ours restores exactly one target (census 13.1 records the same single-company narrowing on the Backup side, as T2-30), so a Skip button would be a second spelling of Cancel. The tick is placed before Apply rather than after, because our restore stages beside the target and swaps — by the time a collision were detected the work would already be done. |
| **Fix** | **Owed if and when multi-company restore is built** (the natural partner of T2-30's multi-company backup): at that point Skip becomes meaningful per company and this entry's justification expires. Confined to `RestoreCompanyViewModel`'s `Confirmed` / `TargetEffectNote` pair and the panel's markup. |

### IV-142 · **The batch's print ORDER, its cancelled-voucher marking and its refusal of an inverted range** — three answers the vendor pages state the question for but not the answer

| | |
|---|---|
| **Severity** | **LOW. Class: PRESENTATION and INPUT HANDLING.** No figure is wrong under any of the three: the same vouchers print with the same amounts. 🔴 **CORRECTED 2026-10-25 (A12 review w6, F2).** This cell used to add that "each voucher's document is projected by the single-voucher projection cell for cell, so a voucher cannot print one way alone and another way in a batch". That was **FALSE** when written: the batch built its own three-column Dr/Cr document and mirrored only the plain-voucher branch, so an item Sales invoice — the document the cited vendor page uses this screen for — printed alone with its item, HSN and GSTINs and in the batch with none of them. It is now TRUE and pinned on bytes: each voucher is drawn by the renderer that prints it alone (`VoucherSetPdf` over `InvoicePdf.RenderInto` / `VoucherPdf.RenderInto`), and `MultiVoucherPrintTests.An_item_invoice_prints_in_the_batch_exactly_as_it_prints_alone` finds every sheet of the single print verbatim in the batch. The job-level choices that change brought with it are recorded as **IV-143**. |
| **What TallyPrime does** | **The three SELECTION AXES are attested and are not ours** — voucher type: *"Press **Alt+P** (Print) > **Others** > select **Multi-Voucher** > press Enter"* (`help.tallysolutions.com/faq-e-way-bill-tally/`) and *"Press **C** (Configure) > type or select **Voucher Type** and press Enter."* (`help.tallysolutions.com/gcc-sales-and-print-fta-tally/`, which also gives the screen its name, *"Type or select **Multi-Voucher Printing** > and press Enter."*); party and period: *"You can also print, export, or e-mail multi-voucher reports for a specific party and period."* (`help.tallysolutions.com/tally-prime/release-notes-tally-prime/release-notes-tallyprime-2-1/`). All four quotations re-opened by content 2026-10-23. ⚠️ **None of those three pages states the print ORDER, what a CANCELLED voucher does in a batch, or what happens when the period is typed backwards.** Bounded claim: three pages, opened this wave. |
| **What we ship** | **Order:** date, then voucher number, then id as a final tie-break, so the job is deterministic and two runs of the same range collate identically. **Cancelled vouchers print, marked** rather than being dropped silently or printed as though live — a batch that silently omitted them would make the paper disagree with the on-screen match list, and the list shows them. **An inverted range is refused, not swapped**: the panel sets a status naming both dates and leaves the match list EMPTY, so Print can never arm over it. Swapping would print a batch the operator did not ask for while telling him he got what he typed. |
| **Why** | Each is a question the operator's input forces an answer to and no opened page answers. The conservative answer was taken in each case: deterministic order over insertion order, visible over silent, refuse over guess. |
| **Fix** | **None owed unless a vendor page states one of the three.** Order lives entirely in the comparator in `MultiVoucherPrintProjector.Select`; the cancelled marking is the voucher's own CANCELLED over-print (the same one its single print carries) plus `MultiVoucherRowViewModel.NumberText` in the match list — which, until A12 review w6 F7, the list did not show at all, so the "the list shows them" reason above was half true; the inversion refusal is one branch in `MultiVoucherPrintViewModel.Refresh` mirrored in `BuildJob`. The batch no longer uses `PrintKind.ReportSet` at all (it is `PrintKind.VoucherSet`), so nothing here can reach the egress-band invariant that `MatrixSecondBandEgressTests` pins. |

### IV-143 · **The multi-voucher JOB: a party matched on its posting lines, each voucher numbering its own sheets, and a cheque-drawn payment printing its voucher** — job semantics no opened vendor page states

| | |
|---|---|
| **Severity** | **LOW. Class: SELECTION and JOB LAYOUT.** No figure is wrong under any of them; each decides WHICH vouchers a party's job holds or HOW the job's sheets are laid out. |
| **What TallyPrime does** | The party axis is attested — *"You can also print, export, or e-mail multi-voucher reports for a specific party and period."* (`help.tallysolutions.com/tally-prime/release-notes-tally-prime/release-notes-tallyprime-2-1/`, re-opened by content 2026-10-23). ⚠️ **None of the pages cited on IV-142 says how a voucher is judged to be "for" a party, how a batch's sheets are numbered, or whether a cheque-drawn payment prints its cheque in a batch.** Bounded claim: those pages, nothing wider. |
| **What we ship** | **Party:** a voucher is the party's when its recorded party is that ledger OR any posting line is on it (`MultiVoucherPrintProjector.InvolvesParty`) — the plain entry grid never records a party except as a posting line, so matching the recorded party alone found none of a party's receipts (A12 review w6, F3). **Numbering:** each voucher numbers its own sheets "Page 1 of N", exactly as it prints alone; the F10 range selects the job's sheets, F5 repeats the whole job, and the F10 start number is not applied (renumbering a voucher would print a sheet it never prints alone). **Cheques:** a payment drawn by cheque prints its VOUCHER in a batch, not the cheque leaf its single print routes to. |
| **Why** | Party: the alternative silently drops what an operator keyed. Numbering: the same voucher must not print two different sheets — that is the F2 defect this job was rebuilt to remove. Cheques: a range print of mixed vouchers onto cheque stationery is not a coherent job, and the cheque path has its own register and its own (user-held, T2-117) open decisions that this job must not touch. |
| **Fix** | **None owed unless a vendor page states one of them.** Party: one predicate. Numbering: `VoucherSetPdf.Render`. Cheques: the routing in `MultiVoucherPrintProjector.Project`, which does not consult `ChequePrintProjector`. |

### IV-144 · **Multi-Voucher Printing is reached from Reports → Statements of Accounts, not from Alt+P > Others** — the vendor's documented door is not this panel's

| | |
|---|---|
| **Severity** | **LOW. Class: NAVIGATION.** The panel and all three axes are reachable by the ordinary arrows-and-Enter cascade; the key path the vendor documents does not lead to it. Recorded by the A12 re-review of w6 (2026-10-25): the divergence was stated only in a code comment (`MainWindowViewModel.BuildReportsColumn`), never in this ledger. |
| **What TallyPrime does** | *"Press **Alt+P** (Print) > **Others** > select **Multi-Voucher** > press Enter"* (`help.tallysolutions.com/faq-e-way-bill-tally/`, quoted by content on IV-142). Bounded claim: that page. |
| **What we ship** | Gateway → Reports → Statements of Accounts → **Multi-Voucher Printing**. Our **Alt+P > Others** row runs the multi-ACCOUNT panel directly (`ReportPrintMenu`, pinned by `ReportChordFidelityTests`, which asserts it lands on `Screen.MultiAccountPrint`), so there is no Others LIST to offer Multi-Voucher from. |
| **Why** | Giving Others a list is a re-cut of a shared column that row 13.5's fidelity test pins, and was not the w6 slice's to make. |
| **Fix** | Build the vendor's Others list (Multi-Account, Multi-Voucher, …) behind Alt+P; this panel then gains that door and does not move. |

### IV-159 · **The DELIVERY CHALLAN's layout, title and acknowledgement line** — the document is attested, its shape is ours

| | |
|---|---|
| **Severity** | **LOW-MEDIUM. Class: PRESENTATION on a document handed to a third party.** No figure on it is ours: Quantity, Rate and Amount come from the voucher's own allocations with the same base-unit normalisation the drilled pane performs, and the two sites are locked together by a test rather than written alike. What is ours is everything a reader would call "the form". |
| **What TallyPrime does** | **The DOCUMENT is attested; the LAYOUT is not, and the second half of that sentence is the entry.** Opened by content 2026-10-23: `help.tallysolutions.com/tally-prime/sales-process/faq-sales/` — *"A delivery note is an inventory document/challan sent with the goods to be delivered to the customer"*, and *"In such a case the sales invoice is issued on a later date referring the delivery note/challan issued"*. `help.tallysolutions.com/tally-prime/sales-process/sales-order-tally/` names the fields the voucher captures — *Party A/c name*, *Tracking No.*, *Order No.*, *Quantity*, *Rate*, *Godown* — and states *"A Delivery Note with Tracking No. marked as Not Applicable is an inventory document affecting only the inventory values"* / *"This will not affect the accounts."* `help.tallysolutions.com/keyboard-shortcuts-tally-prime/` gives **Alt+F8** *"To open Delivery Note"* under *"F10 > Inventory Vouchers"* and **Ctrl+P** *"To print the current voucher or report"*. ⚠️ **None of them states a printed layout.** `help.tallysolutions.com/inventory-vouchers-tallyprime/` mentions the delivery note once and links away; `help.tallysolutions.com/configure-for-print-export-share/` lists print options (*Show Dispatch Details*, *Show Consignee details*, *Show godown name*, …) **for invoices and never names a delivery note**, so those option names are a LEAD and are deliberately **not** cited as the challan's field list. 🔴 **This is a bounded statement about four pages actually opened, not a claim that no such page exists.** Tally.ERP 9 pages that describe printing a job-work material-out challan were found and are **not ground truth on this project**. |
| **What we ship** | `DeliveryChallanPrintProjector` renders through `ReportPdf` — the same renderer as this row's reminder letter and confirmation of accounts, so all three of census 12.7's documents share one pipeline. **OURS:** the title string `"Delivery Challan"`; the six columns *Description of Goods · Godown · Tracking No. · Quantity · Rate · Amount* and their order; the subtitle `"<company>  -  No. N dated DD-MM-YYYY"`; the addressee line `"To: <party>"`; the sentence *"The goods described below have been despatched to you. This challan is not a tax invoice and demands no payment."*; the total row; and the acknowledgement *"Received the above goods in good order and condition."* with the ruled *"Received by ____ Date ____"*. **Also ours and worth naming separately:** the **Amount** column at all — the vendor states Quantity and Rate, and Quantity × Rate is our derivation — and the **absence of an Order No. column**, which the vendor's recording steps DO name; our allocation does not carry one (orders link through `InventoryVoucher.OrderLinks`), so the column is omitted rather than filled with a guess. |
| **🔴 A RESIDUAL ON THE FIGURES, pinned rather than hidden — and it is INHERITED, not introduced** | ✅ **CURED in wave 7 track dr — see IV-205. The pane and the challan now print the line in its ENTERED unit, with the unit, through one shared helper, and the test named below was rewritten to assert the multiplying-out document. The paragraph is kept as the record of what shipped before; its figures (`0.42`, ₹10.08) were also wrong — the shipped cell read `0.83`.** ~~On a **compound-unit** line whose per-base rate does not land on a round paisa, the printed **Quantity × printed Rate does not equal the printed Amount**. 2 Doz @ ₹10.00/Doz is ₹20.00; normalised to the base unit that is 24 Nos at ₹0.41666…/Nos, and a two-decimal Rate cell can only show `0.42` — so the challan prints `24 · 0.42 · 20.00` and a consignee multiplying the two visible figures gets ₹10.08. **The AMOUNT is right** (computed from the unrounded per-base rate and rounded once, so it equals the ₹20.00 the books hold); only the Rate cell is a rounded view of a derived intermediate. `InventoryVoucherDetailViewModel` has shown exactly these three figures since it shipped and the challan is deliberately LOCKED to that pane, so curing it here alone would make the printed document disagree with the screen the operator printed it from — a worse defect than the one it fixes. **The cure is to print the line in the unit it was STATED in** (`2 · Doz · 120.00 · 240.00`), which is a change to the pane as well and belongs to the pane's own census row. Pinned in both directions by `DeliveryChallanDocumentTests.A_compound_line_with_a_non_round_per_base_rate_prints_a_rate_that_does_not_multiply_out`.~~ |
| **🔴 Where the layout is known to read oddly** | `ReportPdf` draws its column-caption band at the top of every page, **above** the first body row — so the goods table's captions print above the `"To: <party>"` line rather than below it. That is inherited from the two letters this row already ships (both put their addressee in a body row) and is **recorded, not cured**: moving the band is a renderer change that would move the bytes of every report in the product, which is not this slice's to make. |
| **Why** | The alternative was to invent a layout and present it as fidelity. The Day Book Dr/Cr split was killed in an earlier wave for exactly that reason — the vendor page set out no column layout, so building one would have been inventing. Here the DOCUMENT is attested and only its shape is not, so the document ships and the shape is labelled. |
| **Fix** | **None owed unless a TallyPrime page is found that states the printed layout.** If one is, the change is confined to the single `Project` method: the columns, the prose rows and the title are all built in one place and nothing downstream reads their shape. |

### IV-160 · **`PrintRow.FullWidth`, and the already-shipped letters whose bytes it moves** — prose was being clipped to a column

| | |
|---|---|
| **Severity** | **MEDIUM. Class: a document that did not say what it was written to say — on paper sent to a counterparty.** Not a figure error: the figure columns were correct throughout. |
| **The defect, measured** | The three documents of census 12.7 are LETTERS rendered through the **tabular** `ReportPdf`, so their addressee and their sentences were carried in **cell 0** of a four-column grid whose first column is *Reference*. `ReportPdf.DrawRowCells` clips every cell to its column's inner width through `PdfWriter.FitToWidth`, which appends an ellipsis. So a reminder letter to *Bharat Heavy Electricals & Components Private Limited* was drawn **`To: Bharat Heavy Electricals & Comp...`**, and the sentence *"Our records show the following amounts still outstanding as at 31-03-2026."* was cut mid-word — on the letter posted to that counterparty. The same held for the confirmation of accounts. Pinned on the emitted bytes by `DeliveryChallanDocumentTests.The_reminder_letter_and_the_confirmation_name_their_addressee_in_full_too`. |
| **What we ship** | A new `PrintRow.FullWidth` flag (`PrintRow.FullWidthText` / `FullWidthHeader`). A flagged row draws cell 0 across the whole content width, left-aligned, and **contributes no width requirement to column 0** in `ReportPdf.MeasureRequirements` — because letting a sentence bid for column width is precisely how a figure column gets starved and `99,99,999.00` comes out as `99,99,9...`. `ReportPdf.SliceColumns` carries the flag so a column-split document does not re-clip the addressee into its repeated stub. |
| **🔴 It MOVES BYTES, deliberately, and ER-13 is not being waived quietly** | **Default `false`, so every document that does not set it is byte-identical.** Three documents DO set it and their bytes move: the reminder letter, the confirmation of accounts and the new challan. That is the fix, not a side effect — the previous bytes were the defect. **Nothing else in the product sets the flag**, so no report, voucher, invoice, payslip or certificate moves. |
| **What is OURS here** | The full-width behaviour itself: left alignment regardless of column 0's alignment, and clipping at the **full** content width rather than wrapping to a second line. **We do not wrap.** A line longer than the sheet still ends in an ellipsis; wrapping would change the row count and therefore pagination for every document that adopts the flag, which is a larger change than this slice is entitled to make. No vendor page we opened says anything about either. |
| **Fix** | If wrapping is wanted, the seam is the single `row.FullWidth` arm in `DrawRowCells` plus `Paginate`'s height accounting — both in one file. |

### IV-161 · **Refusing to print any pure-stock voucher that is not a Delivery Note** — and the wrong document that refusal replaced

| | |
|---|---|
| **Severity** | **The REFUSAL is LOW and ours. What it replaced was HIGH and is a confidentiality defect, recorded here because the cure is a divergence even though the defect was not.** |
| **The defect, measured** | `MainWindowViewModel.OpenPrintPreview` had **no arm for `Screen.InventoryVoucherDetail`**. That screen is a drill column raised FROM a register, so `Reports` stays live beneath it, and `IsReportContext` — which excludes `Screen.VoucherDetail` and `Screen.LedgerVouchers` but **not** this one — stayed TRUE. **Ctrl+P on a drilled stock voucher therefore previewed the whole register**: every other party's goods movements, on a sheet the operator raised from one voucher and would hand to one consignee. Same class as the Day Book this product once *shared to a third party* in place of one invoice. Asserted on the emitted bytes by `DeliveryChallanRouteTests`. |
| **What TallyPrime does** | ⚠️ **Not resolved.** No TallyPrime page we opened states what Ctrl+P does on a drilled Stock Journal, Physical Stock, order or rejection voucher. The vendor's Ctrl+P row says only *"To print the current voucher or report"* (`help.tallysolutions.com/keyboard-shortcuts-tally-prime/`) — which says the CURRENT voucher, and is the ground for not printing the register, but says nothing about what a voucher with no document should do. |
| **What we ship** | A Delivery Note prints its challan. **Every other pure-stock kind raises a notice** — *"This voucher has no printed document. Only a Delivery Note prints a delivery challan."* — and opens no preview. The notice wording is ours. The narrowness is deliberate: Tally.ERP 9 pages describe printing a challan from a *Material Out / Material Transfer Out* voucher, and Tally.ERP 9 is not ground truth here, so those kinds are left refusing rather than given a document on a lead. |
| **🔴 The TWIN of this defect is STILL LIVE on the SHARE and EXPORT paths, and is NOT fixed here** | ✅ **CLOSED in wave 7 track dr (T2-159) — see IV-206 and IV-207.** `IsReportContext` is TRUE on `Screen.InventoryVoucherDetail` for `IsExportablePage` and the share menu as well, so **Alt+E / e-mail / WhatsApp raised from a drilled stock voucher still act on the register beneath it.** That is the same confidentiality class, it belongs to the export and share rows rather than to 12.7, and deciding what a share from a stock pane *should* attach is a design call this slice is not entitled to make. **Recorded loudly rather than silently widened.** |
| **Fix** | When a vendor page settles what the other kinds print, the change is one predicate — `DeliveryChallanPrintProjector.PrintsAChallan` — and the one call site beneath it. |

### IV-162 · **The multi-account EXPORT writes ONE FILE PER ACCOUNT** — the route is the vendor's, the file shape is ours

| | |
|---|---|
| **Severity** | **LOW, and chosen deliberately against the cheaper alternative.** It is recorded because the cheaper alternative is the one a reader would assume we shipped. |
| **What TallyPrime attests** | `help.tallysolutions.com/printing-multi-account-reports` ("Configure for Print, Export, E-mail & WhatsApp"), opened BY CONTENT 2026-10-23, under *Configure multi-account reports*: *"For Export: **Alt+E** (Export) > **Others** > select a report from the List of Reports."* The same page's opening sentence names the document class: *"Before printing, exporting, or sharing documents, like your invoices, financial reports, payment reminder letters,"*. **The ROUTE and the fact that a reminder letter is an exportable document are therefore CLONED, not invented.** |
| **What the vendor does NOT state** | ⚠️ **Nothing on that page, or on `help.tallysolutions.com/export-data-in-tally/`, says whether a multi-account export produces ONE file holding every selected account or one file per account, nor what any of them is named.** This is a genuine silence and it is reported as one. **It is NOT spent as a licence**: "no page I opened says so" is not evidence that no page says so, and a hedge converted into a justification is the exact move a reviewer caught on another branch last wave. |
| **What we ship, and why** | **One PDF per selected account**, named `<document title> - <account>[_timestamp].pdf`, written by the same `ReportPdf.Render` the print arm uses so an exported letter and a printed one are the same document. The reason is confidentiality, not taste: the PRINT arm's single collated job is right for print — an operator takes the stack off the tray and posts one sheet to each counterparty — but an **exported** file is a thing that gets attached to a mail, and one file holding every selected party's balances is one careless attachment away from telling Acme what Bharat owes us. **This application has already shared a whole register to a third party once when a single voucher was meant** (see IV-161's own twin, still live on the share path). Pinned on the emitted bytes of every file by `MultiAccountExportTests.Each_exported_file_carries_its_OWN_party_and_no_other_partys_figures`. |
| **⚠️ One step more than the vendor's sentence, stated rather than glossed** | The vendor reads *"Alt+E (Export) > Others > select a report"* — three steps to the export. Ours is **Alt+E → Others → the Multi-Account Printing panel → Export**, because `Others` opens the one panel that is both the report list and the account picker, and the output verb is chosen there. **So the chord and the row are the vendor's and the step count is not.** The panel carries both buttons, Print and Export, and nothing marks which verb the operator arrived by. Low cost, but it is a divergence and is not left for a reader to discover by counting. |
| **Fix** | If a vendor page is later found stating a combined-file shape, the change is confined to `MultiAccountExportViewModel.Apply`'s loop. **Do not make that change without also deciding what protects the operator from the attachment above** — the shape is load-bearing, not cosmetic. The step count is fixed by giving the panel an arrival intent, which is only worth doing alongside IV-164's re-model. |

### IV-163 · **The multi-account export offers PDF only** — the vendor's export screen offers a File Format list

| | |
|---|---|
| **Severity** | **LOW.** A real shortfall against the vendor, structural rather than lazy, and disclosed on the panel itself. |
| **What TallyPrime does** | `help.tallysolutions.com/export-data-in-tally/` offers a File Format choice on the export screen; `…/printing-multi-account-reports` adds *"In the E-mail or WhatsApp screen, press **F8** (File Format)."* ⚠️ **Neither page was found stating the format list for a multi-account EXPORT specifically**, so the shortfall is stated against the general export screen and not against a page that speaks to this job. |
| **What we ship** | PDF only, with the reason on screen: *"PDF only - a letter is not a table, so no CSV/XLSX for this job"*. Every other format in this application serialises a `TabularExport`, and these three documents are not tables — a reminder letter is a prose addressee line, a bill grid, a sentence asking for settlement and a signatory line. There is no `PrintReport`→`TabularExport` projection in this build, and inventing one would flatten the prose rows away, so a CSV "reminder letter" would be a bill list that no longer asks for anything. **A format row that produced that would hide the gap; the disclosure shows it.** |
| **Fix** | A `PrintReport`→`TabularExport` projection that carries full-width prose rows as single-cell rows, then the existing six writers come free. Belongs to census 13.6 (the format list), not to 12.7. |

### IV-164 · **Reminder Letter and Confirmation of Accounts are modelled as per-ACCOUNT document kinds; TallyPrime models them as a REPORT TYPE set on a chosen report**

| | |
|---|---|
| **Severity** | **MEDIUM — a SHAPE divergence on a route, not a missing document.** Both documents exist and both now print and export. Recorded because the census has twice described this row's shape and both descriptions are narrower than what the vendor pages actually say. |
| **What TallyPrime does** | Opened BY CONTENT 2026-10-23. `help.tallysolutions.com/print-invoices-reports/`: the *Multi Account Reports* list is reached by *"Press **Alt+P** (Print) > **Others**"* and the page names **All Ledger Accounts** and **Group of Accounts** in it. **Reminder Letter is not a member of that list** — it is a **Report Type** set on a report: *"Set Report Type as Reminder Letter"*, reached from Bills Receivable. **Confirmation of Accounts is likewise a Report Type**: *"Set Report Type as Confirmation of Accounts and save"*, under Group of Accounts. `…/printing-multi-account-reports` gives the two applicability lists directly: *"You can configure reminder letters for Bills Receivable, All Ledger Outstandings, Ledger Outstandings, Group of Account Outstandings, and Group Outstandings"* and *"You can configure Confirmation of Accounts for Cash Book, Bank Book, All Ledger Accounts, One Ledger Account, and Group of Accounts."* |
| **What we ship** | A single **Multi-Account Printing** panel that lists ACCOUNTS with three radio-selected document kinds (Ledger Account / Reminder Letter / Confirmation of Accounts), with the eligible account set narrowed per kind by `MultiAccountPrintProjector.MayProduce`. So our axis is *pick accounts, then pick a document*; the vendor's is *pick a report, then set its Report Type*. The documents produced are the same two; the way an operator asks for them is not. |
| **🔴 What this corrects in the census** | Row 12.7's 2026-09-04 note described the two letters as *"Print Multi-Account Reports → … Print Confirmation of Accounts; Export Reminder Letters, Export Confirmation of Accounts"* — menu names in that form appear on **Tally.ERP 9** pages, **which are not ground truth** (R7, ruling 14). The TallyPrime pages above do not carry them. **The documents named by that note are real and now ship; the MENU SHAPE it described does not match the current product**, and a later reader should build to the Report-Type model above rather than to those four strings. |
| **Fix** | Not a bug fix — a re-model, and a sizeable one: it needs a Report Type knob on the print/export configuration of five named outstandings reports and five named account reports, which is census rows 11.9 and 12.3 territory as much as 12.7's. **Deliberately not attempted in the pass that closed 12.7's export arm**, because re-routing two already-shipped documents to close a shape question is exactly the silent scope expansion R12 forbids. |

## 2K. ADDED 2026-10-23 — WAVE 5 TRACK `cb`: THE BILL-SIDE TRACKING-NUMBER SELECTION AND THE BILLS PENDING DRILL (census rows 4.13 / 4.14 / 9.8)

> **Block discipline, stated first because this register has now had an `IV-` collision in four consecutive
> waves.** This track's pre-assigned block is **IV-149..158** and it takes **IV-149** and **IV-150**. The
> highest `### IV-` on `origin/main` `8eea0c6` was **IV-138**; the gap from 139 to 148 is **track `ca`'s block,
> not free space**, and is deliberately left empty here. No number was read off `main` and guessed at.

> 🔴 **THE CENSUS'S OWN NAME FOR THIS GAP IS NOT THE VENDOR'S, AND THAT IS RECORDED BEFORE THE ENTRIES RATHER
> THAN INSIDE ONE.** Census rows 4.13, 4.14 and 9.8 all name the missing feature a ***"List of Tracking
> Numbers"* picker**. Both vendor pages were opened BY CONTENT on 2026-10-23 and searched for that exact
> phrase: **neither carries it.** The vendor names *"List of Orders"* for the ORDER picker
> (`help.tallysolutions.com/purchase-order-tally/`: *"Select Order No(s) from the **List of Orders**, if a
> purchase order exists for that supplier."*) and never names the tracking list at all. **The behaviour is
> attested; the name is the census's.** So no caption in this product says "List of Tracking Numbers", and a
> later reader must not treat the census phrase as a quotation — it is exactly the shape of thing that became
> this project's twelfth caught mis-citation (*a design justified by a quotation nobody re-opened*).

### IV-149 · **The bill-side tracking-number picker's SHAPE** — what it offers, how each row reads, and the three things it deliberately does not do

| | |
|---|---|
| **Severity** | **LOW. Class: PRESENTATION AND SCOPE, on a feature whose existence is attested.** No figure is wrong under it. What is ours is the *shape* of an attested selection, not whether the selection exists. |
| **What TallyPrime does** | **ATTESTED, opened BY CONTENT 2026-10-23.** `help.tallysolutions.com/purchase-order-tally/`: *"You can select the tracking number of the receipt note to track the received goods in the purchase invoice."* and *"Select **New Number** for the Tracking No."* · `help.tallysolutions.com/sales-order-tally/`: *"You can select the tracking number of the delivery note to track the delivery of goods in the sales invoice."* So **the selection and the New Number entry are the vendor's.** ⚠️ **What the pages do NOT do is describe the list** — no caption, no columns, no ordering, and no statement of which notes are eligible. That silence is this entry. |
| **What we ship** | A `ComboBox` beside the existing Tracking No. box on each item line of a Purchase/Sales invoice, offering every **still-unbilled** note as at the bill's own date, **oldest first**, each row reading `"<number> — <date> · <party> · <items>"`, followed by the vendor's **New Number** entry which clears the box for the operator to key one. **Eligibility is DERIVED from `BillsPending`'s own `GoodsNotBilled` section** rather than from a second walk of the book — so the picker and the Purchase/Sales Bills Pending report answer the same question by construction and cannot drift, which is the discipline `ReportColumnBands` imposed on the print and export projectors after they *had* drifted. ⚠️ **Added by the wave-6 review (2026-10-25): the list is NOT filtered by the bill's party.** A bill for one supplier is offered every other supplier's open notes too, each labelled with its party. The vendor's attested flow is party-FIRST (*"After selecting the party, a sub-screen opens. Select the receipt note reference."*, `/purchase-order-tally/`), so offering cross-party notes is OURS and lets a mis-pick link one supplier's bill to another supplier's goods. No figure is wrong unless the operator mis-picks; the fix, if wanted, is a `PartyId` filter in `VoucherEntryViewModel.RefreshTrackingNumberChoices`, and it belongs with IV-151's party-level sub-screen. |
| **🔴 The three things it deliberately does NOT do** | **(1) It does not auto-fill quantity or rate.** The vendor states that only for a sales ORDER number — *"The stock item, quantity, and rate appear automatically after selecting the sales order number"* — and for the tracking number says merely *"Change the stock item's Quantity and Rate on the Stock Item Allocations screen, if required."* Populating a bill line on a tracking pick would be inventing a figure-bearing behaviour from a page that does not state it. **(2) It offers no *"Not Applicable"* entry.** That option IS attested, but on the **note** (*"A Receipt Note with Tracking No. marked as Not Applicable is an inventory document affecting only the inventory values"*) — searched for on the bill's Tracking No. field and **not found there**. A blank box already means untracked on our bill, so nothing is lost; a named option would be ours. **(3) It is not offered on the NOTE side at all** — a note is routinely keyed before the bill that will quote it, so there is nothing to pick, and the vendor's rule there is a DEFAULT (*"By default, the invoice number appears"*), already shipped. |
| **🔴 What it refuses to offer, and why that is the load-bearing half** | A number in the **`BilledNotReceived`** section — goods a bill has already over-claimed — is **not** offered. Nothing on either vendor page says so. The reason is ours: offering it invites a second bill against one movement, which is the double-billing hazard `VoucherEntryViewModel.ShowItemTrackingNumber` already states in its own words when it withholds the column from Credit/Debit Notes. **If a vendor page is found that offers the over-billed side, this is the behaviour to change.** |
| **Why** | The alternative was to ship the attested selection with an unlabelled list shape, which is how a presentation choice becomes an undocumented behaviour. |
| **Fix** | **None owed unless a vendor page describes the list.** If one is found, the change is confined to `BillsPending.OpenTrackingNumbers` (eligibility and order) and to the label built in `VoucherEntryViewModel.RefreshTrackingNumberChoices` — one producer, one consumer, no call site moves. |

### IV-150 · **Which voucher a Bills Pending row drills to** — the EARLIEST document on the row's own side, and exactly one of them

| | |
|---|---|
| **Severity** | **LOW. Class: NAVIGATION.** No figure is wrong under it. It decides where `Enter` lands from a pending row, which before this change was nowhere. |
| **What TallyPrime does** | ~~⚠️ **NOT ATTESTED, AND I AM NOT CITING A PAGE.** `help.tallysolutions.com/purchase-order-tally/` and `/sales-order-tally/` were opened by content 2026-10-23; both describe the **Purchase/Sales Bills Pending report** and its two sections, and **neither states a drill-down from a pending row, nor which voucher one would reach.** That is a bounded statement about the two pages this track opened — **it is not a claim that no vendor page describes it**, and it is deliberately NOT spent as a justification. The census names the gap (*"no drill from a pending row to its voucher"*), so the *existence* of a drill is this project's own requirement; the *target* is the choice recorded here.~~ 🔴 **THE STRUCK PARAGRAPH IS WRONG AND IS STRUCK RATHER THAN DELETED, BECAUSE THE WAY IT WENT WRONG IS THE POINT.** Re-measured the SAME DAY, on the SAME two pages, by fetching each page's text and searching it for `drill` rather than searching near the tracking-number prose: **the drill-down IS attested, on BOTH pages, word for word.** `help.tallysolutions.com/purchase-order-tally/`, inside *"Know your Purchase Bills Pending"*: *"To drill down to the voucher level, place the cursor on the voucher detail line and press Enter."* — and the identical sentence inside *"Know your Sales Bills Pending"* on `help.tallysolutions.com/sales-order-tally/`. **So the drill's EXISTENCE and its KEY (`Enter`) are the vendor's, and this product matches both.** ⚠️ **What is still unattested is the TARGET**: *"the voucher detail line"* does not say WHICH voucher a row reaches when several carry one tracking number, and neither page says more. That narrower silence is what the rest of this entry records. 🔴 **THE LESSON, STATED PLAINLY BECAUSE THIS PROJECT HAS PAID FOR IT BEFORE: a hedge is not a finding.** The struck text was honest, bounded and careful — and it was still false, because *"I searched and found nothing"* was written up as a property of the pages instead of as a property of the search. The sentence was always there; it sits in the report section rather than beside the tracking-number prose. |
| **What we ship** | A pending row drills to the document that **made it pending**, and to the **earliest** such document when several share the number: a *Goods Recd. but Bills not Recd.* row reaches its **note** (an `InventoryVoucher`), a *Bills Recd. but Goods not Recd.* row reaches its **bill** (an accounting `Voucher`). Earliest, not latest, so the row's displayed **date and its drill target are provably the same document** — the row is aged from the earliest voucher on its own side, and landing the operator on a document whose date is not the one they just read would be a navigation that lies. |
| **🔴 Exactly ONE drill slot is populated per row, and that is correctness rather than tidiness** | `ReportRow` carries both `DrillVoucherId` and `DrillInventoryVoucherId`, and `MainWindowViewModel:9299` tests the **inventory** slot first and returns on it. A row that filled both would answer `Alt+X` by offering to cancel the **note** while the operator is standing on a row about the **bill** — a destructive verb aimed at the wrong voucher, in the one place the two aggregates must never be confused (census 4.9). |
| **Why** | The alternative — drilling to the *latest* document, or listing all of them — would either contradict the row's own date or require a second screen the census does not ask for. |
| **Fix** | **None owed.** If a vendor page is later found stating a different target, the change is two assignments in `ReportsViewModel.AddBillsPendingSection` and two fields on `BillsPendingRow`. |

### IV-151 · **The note-reference sub-screen on the bill is ATTESTED AND NOT BUILT** — our selection is per item line and carries only the number across

| | |
|---|---|
| **Severity** | **MEDIUM. Class: MISSING ATTESTED SURFACE, plus a figure-bearing carry-across we do not perform.** 🔴 **This entry OPENS a clause rather than closing one, and it is being written by the same track that built the picker, in the same wave.** It was found by re-reading the two pages end to end instead of around the sentences already quoted. |
| **What TallyPrime does** | **ATTESTED, opened BY CONTENT 2026-10-23.** `help.tallysolutions.com/purchase-order-tally/`, *Purchase Against Receipt Note*: *"Select the party ledger in the Party A/c name."* → *"After selecting the party, a sub-screen opens. Select the receipt note reference."* → *"The details from the receipt note reflect will reflect in the purchase voucher."* [sic] → *"If you have generated the receipt note against a purchase order, the purchase order details also get prefilled."* → *"Change the stock item's Quantity and Rate on the Stock Item Allocations screen, if required."* → *"Tracking No.: and Order No.: will be auto-filled by default based on the receipt note and order number reference."* The sales twin on `/sales-order-tally/`, *Record Sales Against Delivery Note*: *"Select the Delivery Note reference. The details from the Delivery Note reflect in the Sales Voucher."* with an explicit escape — *"If you want to generate the bill against the Sales Order without selecting the delivery note tracking number, you can select End of List and press Ctrl+A to return to the sales invoice."* The sub-screen is gated by F12: *"set the option Provide Dispatch, Order, and Export details to Yes"* (sales) / *"Provide Receipt, Order, and Import details to Yes"* (purchase). |
| **What we ship** | The selection, **on the item line**, as a combo box beside the Tracking No. box (IV-149). Picking a note writes **the tracking string and nothing else**. There is **no party-level Receipt/Dispatch Details sub-screen**, no *End of List* entry in one, no prefill of the note's items, quantities or rates onto the bill, and no Order No. carried across — `VoucherInventoryLine` **has no order-number field at all** (`grep -n OrderNumber src/Apex.Ledger/Domain/VoucherInventoryLine.cs` returns nothing, re-run 2026-10-23). |
| **🔴 Why this is recorded as an open clause and not as a variant** | Two of the differences are cosmetic — which screen the selection lives on, and whether the escape is a named *End of List* row or simply leaving the box alone. **The third is not:** the vendor's flow carries the note's details INTO the bill, so the operator who selects a reference gets quantities and rates they then *"change … if required"*. Ours leaves the bill blank and the operator re-keys every figure. **A re-keyed figure is a figure that can differ from the note's**, and the whole point of the tracking link is that the two documents describe one consignment. **Calling that a presentation choice would be the grade inflation this project has already refused once** (`T2-128`(iii)). |
| **🔴 What it does NOT change about IV-149** | IV-149's point (1) — *we do not auto-fill quantity or rate on a **tracking-number** pick* — **still stands and is still right.** The vendor's carry-across hangs off selecting a **receipt/delivery note reference** in the sub-screen, of which the Tracking No. is an *output* (*"will be auto-filled … based on the receipt note"*). On the one flow where the vendor has the operator pick a tracking number directly, it tells them to key the figures themselves: *"Select New Number for the Tracking No. Enter the New Number. Enter the Quantity and Rate."* (`/sales-order-tally/`). **The two flows are different and are not merged here.** |
| **Why** | Shipping the item-line picker was still worth doing: before it, a bill could be linked to a note only by typing the number by hand, and a typo produced a Bills Pending row that never clears. **But the gap it closes is narrower than "the vendor's bill-side tracking selection", and saying so is the only way the next reader does not treat rows 4.13 / 4.14 / 9.8 as one clause lighter than they are.** |
| **Fix** | Owed, and **it is not small**: a party-level Receipt/Dispatch Details sub-screen behind the F12 gate, a reference list with *End of List*, a carry-across that populates item lines from the chosen note, and — for the Order No. half — the **same migration on both movement-line tables that IV-106 already blocks behind a user go/no-go.** It should be designed with IV-106, not before it. |

### IV-152 · ~~**The two SALES Bills Pending section captions are OURS**~~ — 🔴 **RETRACTED 2026-10-25 (wave-6 review): ALL FOUR CAPTIONS ARE NOW THE VENDOR'S, VERBATIM**

> 🔴 **RETRACTED, AND THE ENTRY BELOW IS KEPT RATHER THAN DELETED SO THE MISTAKE STAYS AUDITABLE.** The entry
> said the sales page gives no derivable caption pair. **It does.** `help.tallysolutions.com/sales-order-tally/`,
> re-opened BY CONTENT 2026-10-25 by the wave-6 review, carries in the *Sales Bills Pending* **F12 (Configure)
> table**: *"Display the Goods Delivered but Bills not Made section with transaction details."* and *"Display the
> Bills Made but Goods not Delivered section with transaction details."* — the word *section* is the page's own.
> The "three conflicting phrasings" reading below came from the two PROSE sentences; the configuration rows settle
> it, and the loose *"Sales bills to make"* is the only outlier. `ReportsViewModel.AddBillsPendingSection` now
> ships *"Goods Delivered but Bills not Made"* / *"Bills Made but Goods not Delivered"*, and
> `TrackingNumberPickerTests.Both_section_captions_are_the_vendors_on_screen_and_in_the_export` pins all four
> captions on the exported CSV bytes (reverting either sales literal to "Raised" reddens it by name). **The
> `What we ship` and `What is NOT claimed here` rows below describe the shipped state BEFORE the correction and are
> no longer true of the code.** This was never counted as a clause, so no census integer moves.

| | |
|---|---|
| **Severity** | **LOW. Class: CAPTION.** No figure and no navigation changes under it. Recorded because it was measured, and because it goes **against us**. |
| **What TallyPrime does** | **PURCHASE — attested and we MATCH, by content 2026-10-23.** `help.tallysolutions.com/purchase-order-tally/`: *"This report lists transactions of **Goods Recd. but Bills not Recd.:** and **Bills Recd. but Goods not Recd.:**"* — colons and all, which is what makes those read as on-screen captions rather than prose. **SALES — the page gives two DIFFERENT phrasings and neither is ours.** `/sales-order-tally/`: *"This report lists transactions of **Goods Delivered but bills not Made** and **Sales bills to make**."* and, earlier on the same page, *"appears as **Bills made but goods not delivered** in the Sales Bill Pending report"*. **Three candidate wordings across two sentences, no colons, and the actual screen is shown only as an image this project cannot read.** |
| **What we ship** | `ReportsViewModel.cs:3609,3611` — purchase: *"Goods Recd. but Bills not Recd."* / *"Bills Recd. but Goods not Recd."* (**exact vendor match**); sales: *"Goods Delivered but Bills not Raised"* / *"Bills Raised but Goods not Delivered"* (**ours** — symmetrical with the purchase pair, and matching none of the three vendor phrasings; *"Raised"* vs the vendor's *"Made"*). |
| **🔴 What is NOT claimed here** | **That our sales captions are wrong.** The page's own wording is self-inconsistent, so no single caption can be derived from it, and *"I could not derive it"* is **not** evidence that the vendor's screen says what we say. **This entry exists precisely so the sales pair is not read as attested merely because the purchase pair beside it is.** |
| **Why** | Symmetry with the attested purchase pair is readable and was evidently the intent. The alternative — copying *"Sales bills to make"*, which does not describe the section's contents in our wording of it — would trade a labelled divergence for a confusing caption. |
| **Fix** | **Two string literals** at `src/Apex.Desktop/ViewModels/ReportsViewModel.cs:3609` and `:3611`, the moment a page or screenshot settles the sales captions. Nothing else moves: `ReportColumnBands.cs:222` declares the column band for both kinds and does not depend on the section text. |

## 2L. ADDED 2026-10-25 — WAVE 6 TRACK `cd`: THE INVENTORY-MASTERS SLICE (census rows 3.6 / 3.11 / 3.12)

### IV-169 · **The ROUTE to the Reorder Level Alteration screen is ours** — arrows + Ctrl+Enter on the existing-definitions list, and a commit button the vendor does not render

| | |
|---|---|
| **Severity** | **LOW. Class: NAVIGATION.** No figure changes under it. What is ours is only how the operator *reaches* an alteration screen whose existence, fields and accept chord are all attested. |
| **What TallyPrime does** | **ATTESTED, opened BY CONTENT 2026-10-23** at `help.tallysolutions.com/tally-prime/inventory/reorder-stock-items-reorder-status-and-reorder-quantity/`: *"Press Alt+G (Go To) > Alter Master > type or select Reorder Level (Stock Item) and press Enter."*, then *"Select the stock item."* The same page attests the field set — *"Period: Select the consumption for various periods – Days, Weeks, Months, or Years."*, *"Minimum Order Quantity: Enter the minimum quantity you want to order."* — and puts the two Advanced toggles on that screen as *"Press Alt+S (Adv Reorder)."* and *"Press Alt+V (Adv Min Qty)."*, which is what this screen already binds. ⚠️ **CITATION CORRECTED 2026-10-23 BEFORE COMMIT, and the correction is recorded rather than quietly applied.** An earlier draft of this entry claimed the page attests *"a Criteria of **Higher** or **Lower**"*, and census row 3.12 quotes it harder still as *"**Criteria**: You can select **Higher** or **Lower** based on the consumption"*. **Neither sentence is on the page.** Re-opened BY CONTENT 2026-10-23, the Criteria field description reads, in both of its two occurrences, exactly *"Select the criteria."* — four words, no options named. The words "higher" and "lower" do appear, four times, but only lowercase and only in the consumption prose: *"If you want to reorder quantity, when the consumption is higher than that in the specified period."* and its three siblings. 🔴 **So the CAPABILITY is attested and our Higher/Lower criterion is NOT ungrounded** — the page plainly describes a higher-than and a lower-than consumption branch for both Reorder Quantity and Minimum Order Quantity. **What was wrong was the quotation, not the feature.** The census row's quoted sentence is owed the same correction. 🔴 **This retires the repeated in-repo claim that the reorder page 404s**: the page was MOVED, not withdrawn, and the dead slug is `…/tally-prime/inventory-masters/reorder-level-tally/`. The old claim is struck in `MainWindowViewModel` (three places) and in `MasterVerbsW33ResidualAlterTests` rather than deleted, so a reader can see what changed. |
| **What we ship** | The vendor reaches the screen through a **Go To → Alter Master → type-or-select** flow; neither the Alt+G menu nor a type-ahead master picker is built in this product. So the route is this codebase's own shared master-list shape: the existing-definitions list on the Create screen, Up/Down to highlight a definition, **Ctrl+Enter** to open it for alteration, **Ctrl+A** to accept. Identical to every other arm of `MainWindowViewModel.AlterHighlightedMasterListRow` (godown, unit, stock category, cost category, cost centre, price level, price list, currency, budget, scenario). An on-screen **"Save (Ctrl+A)"** button is rendered too; TallyPrime is keyboard-driven and documents no commit button on this screen. |
| **Why** | Building Alt+G and a master type-ahead is a separate capability (it is its own census row); withholding the reorder alter until then would have left a **data loss** standing — see IV-170's neighbour below and `ReorderLevelsViewModel.ForAlter`'s own remarks: the create screen showed blank boxes over an existing definition and a blank box means *unset*, so raising one figure wiped the other figure, the period and the criterion. Reusing the shipped route was the smaller divergence. |
| **Fix** | None owed on the semantics. When the Alt+G / Alter Master flow is built, this arm becomes a second route to the same act rather than the only one; the view-model factory (`ForAlter`) and the engine (`ReorderLevelsService.Alter`) are route-agnostic and would not move. |

### IV-170 · **Withdrawing a WHOLE price list (every dated version for a level + item) is ours** — the verb, the Alt+Shift+D chord and the button

| | |
|---|---|
| **Severity** | **MEDIUM. Class: DESTRUCTIVE VERB with a money consequence on the next invoice**, which is why it is registered rather than shipped quietly. Nothing already posted is re-priced — `PriceResolver` reads a price list at ENTRY time and the resolved rate is stored on the voucher line — but after the withdrawal the (level, item) pair is priced by the stock item's own rate from then on. |
| **What TallyPrime does** | ⚠️ **NOT ATTESTED, AND NO SENTENCE IS CLAIMED. Here is exactly what was searched, 2026-10-23.** `help.tallysolutions.com/tally-prime/sales-purchase-voucher-entry/selling-buying-prices/` was opened BY CONTENT. It documents **creating** a price list, **revising** one — *"Press Alt+G (Go To) > Alter Master > Price List (Stock Group or Stock Category) > press Alt+R (Revise Price)."* — and **altering** one — *"You can alter a price list by overwriting the details entered in the Price List screen."* It never mentions deleting a price list, deleting price-list entries, or Alt+D on that screen; the nearest passage is about a price during a revision (*"if you want to change the price that is auto-populated or remove it completely"*). 🔴 **That silence is recorded as silence.** It is NOT read as permission and NOT as prohibition, and this is a bounded statement about one page opened by content — not a claim about the whole site. |
| **What we ship** | `PriceListService.DeleteAllListsFor(levelId, itemId)` — all-or-nothing, returning the count, refusing an unknown level, an unknown item and a pair with no list by name. Reached by **Alt+Shift+D** on the Price List master and by a **"Delete price list (Alt+Shift+D)"** button, both through the one `MainWindowViewModel.RequestDeleteWholePriceList` and the single existing Y/N confirmation channel, which names the pair and the version count and says in words that the pair will then be priced by the item's own rate. |
| **🔴 Why a SECOND chord rather than widening Alt+D** | Alt+D on this screen already means something precise and tested — withdraw the ONE highlighted dated version (itself ours, on the same footing). Overloading it would make one key mean "remove one price" or "remove every price" depending on state the operator cannot see, with a live invoice rate on the other side of the difference. Alt+Shift+D is free **by construction, not by hope**: the window's Alt+D arm matches `e.KeyModifiers == KeyModifiers.Alt` EXACTLY (not `HasFlag`) and its own comment already names Alt+Shift+D as a chord it deliberately excludes. `Plain_Alt_D_still_withdraws_only_the_highlighted_version` pins that the existing meaning is untouched. |
| **Why the verb at all** | Census 3.11's own first gap sentence — *"no route deletes a list or a version"* — was only half closed. A (level, item) pair with ANY surviving version is priced, so removing a re-priced list one row at a time **falls the pair back onto the OLDEST surviving rate**, live again on every later invoice; `Withdrawing_versions_one_at_a_time_falls_back_onto_the_oldest_price` demonstrates exactly that with ₹100 resurfacing on a November date after the ₹110 and ₹120 versions are withdrawn. There was no single act meaning *"this level does not price this item"*. All-or-nothing is what makes no intermediate price reachable by accident. |
| **Fix** | If a vendor page is found that describes removing a price list, both the chord and the confirmation wording become candidates for de-invention; the engine method is the shape any route would call and would not move. If a page is found that FORBIDS it, the verb is withdrawn — the arm, the chord, the button and the engine method are one `Screen` case, one key arm, one handler and one method. |

### IV-171 · **Refusing an Alternate Unit that would give one item TWO conversions is ours** — the vendor is silent on a compound alternate of the item's own base unit

| | |
|---|---|
| **Severity** | **MEDIUM-HIGH as a defect; the REFUSAL itself is LOW-risk.** Class: WRONG QUANTITY, with money downstream through valuation. The state being refused produced two reachable, disagreeing conversions for one (item, unit) pair, both of which print as quantities. |
| **What TallyPrime does** | **The feature and the field are ATTESTED — but NOT on the page this project has been citing; see IV-173.** The sentence is on `help.tallysolutions.com/units-of-measurement/`, opened BY CONTENT 2026-10-23: *"where: Provide the conversion factor between the simple or compound unit and the alternate unit."* ⚠️ **The page does not say what happens when the chosen alternate unit is a COMPOUND unit that reduces to the item's own base unit and whose own factor differs from the one typed.** Three pages were opened by content for this question — `units-of-measurement/`, `manage-stock-item-tally/` and `tally-prime/inventory/manage-stock-item-tally/` — and none addresses it. Stated as a bounded claim about those three pages, not a universal negative. |
| **What we ship** | A fifth refusal in `InventoryService.SetAlternateUnit`, beside the four half-state refusals already there: when the alternate unit **is compound** AND its `BaseMeasureUnitId` equals the item's base unit AND its own factor differs from the one typed, the save is refused by name, printing BOTH factors so the operator can retype the right one. An **agreeing** factor is accepted; a compound unit reducing to some other measure is untouched; a simple alternate unit is untouched. |
| **🔴 The defect it closes, and why row 3.6's own claim was too strong** | Row 3.6 records its design as *"the base unit is stored and the alternate is ALWAYS derived … so no stock table gained a quantity column, no second quantity exists, and 'a report and an invoice disagree' is impossible **by construction** rather than by test."* That is sound about **storage** and silent about **conversions** — and there was a second conversion. A compound unit carries its own exact factor (`Unit.QuantityInBaseMeasure`), and `InventoryVoucherLineViewModel.RefreshUnitOptions` offers every compound unit whose `BaseMeasureUnitId` is the item's base unit as a LINE unit. So an item based in Nos whose Alternate Unit was set to the compound `Box-Nos` (= 20 Nos) with a factor of **10** had both live at once: a voucher line keyed *"2 Box-Nos"* posted **40 Nos** (the UNIT's factor) while every alternate display of those 40 Nos read *"4 Box-Nos"* (the ITEM's). **It was reachable from the UI, not only from the service:** `StockItemMasterViewModel.RefreshPickers` fills the Alternate Units picker from `_company.Units` with no filter at all, compounds included — one ordinary save. |
| **Why a refusal and not a reconciliation** | Silently preferring one factor would make the other screen wrong without saying so, and both numbers look like quantities. Refusing at the engine choke point is where the other four half-state refusals already live, so both the Create and the Alter route inherit it with no second copy of the rule. **Deliberately narrow:** refusing every compound alternate unit would have been the easy fix and would have removed a capability the vendor's own sentence names. |
| **Fix** | None owed unless a vendor page states the precedence (which factor wins). If one is found, the change is confined to that single `if` in `SetAlternateUnit` plus `AlternateUnitTwoConversionsTests`. |

### IV-172 · **An ALTER that would move a reorder definition onto a (scope, target) another definition owns is REFUSED, where CREATE upserts over it**

| | |
|---|---|
| **Severity** | **LOW-MEDIUM. Class: DESTRUCTIVE SIDE EFFECT prevented.** No figure is wrong under the refusal; without it, one save would have silently deleted a second definition the operator never named. |
| **What TallyPrime does** | ⚠️ **SILENT, and no sentence is claimed.** The reorder page (opened BY CONTENT 2026-10-23, slug as in IV-169) describes setting a reorder level per stock item through Alter Master and says nothing about what happens when an alteration is re-pointed at a different item that already has a definition of its own. |
| **What we ship** | `ReorderLevelsService.CreateOrUpdate` **upserts** — one definition per (scope, target), RQ-32, so creating over an existing pair replaces it, which is the shipped and attested-enough create behaviour. `ReorderLevelsService.Alter` does **not**: re-pointing the definition being altered onto a pair that ANOTHER definition owns throws *"Another reorder definition already covers this {scope}. Alter or delete that one instead."* Re-accepting over its OWN pair is of course allowed. |
| **Why the two verbs differ** | A create acts on the pair the operator just picked, and replacing that pair's definition is the whole meaning of an upsert. An alteration acts on **a record the operator opened**, and letting it consume a different, unrelated record as a side effect is the opposite of a correction — the operator would lose a definition they were not looking at, from a screen whose caption says *Alteration*. Making Alter route through the upsert would also have minted a new `Guid`, which is the defect class this area has been demoted for three times. |
| **Fix** | None owed. If a vendor page is found that describes an alteration absorbing another definition, the change is one `if` in `ReorderLevelsService.Alter` and one test. |
| **🔴 Superseded in part 2026-10-25 (wave 7, track dm) — see IV-187** | ~~`ReorderLevelsService.CreateOrUpdate` **upserts** … which is the shipped and attested-enough create behaviour.~~ The CREATE screen no longer upserts: it calls `ReorderLevelsService.Create`, which refuses an occupied (scope, target) by name, and the screen loads the saved definition for alteration. `CreateOrUpdate` survives only for seeding and tests. The Alter refusal described above is unchanged. |

### IV-173 · 🔴 **MIS-CITED VENDOR PAGE IN `src/`, FOUND AND FIXED — the alternate-unit conversion sentence is NOT on `manage-stock-item-tally/`**

| | |
|---|---|
| **Severity** | **MEDIUM as a process defect; ZERO as a behaviour defect.** No figure moves and no capability was invented. This is registered because it is **the thirteenth** instance of this project's recorded pattern — *a design justified by a quotation nobody re-opened* — and because it had reached **five files in `src/`** plus two test files and the census. |
| **What was claimed** | Five `src/` sites and two test files cited `help.tallysolutions.com/manage-stock-item-tally/` as attesting, verbatim, *"The **Alternate units** field appears"* and that the operator *"provide[s] the conversion factor between the simple or compound units and alternative units"*. Census row 3.6 carries the same page as its vendor citation. |
| **What the page actually says** | **Re-opened BY CONTENT twice on 2026-10-23.** `…/manage-stock-item-tally/` contains **neither sentence**. Its entire treatment of the feature is one clause in step 6 of *Create Stock Items*: *"You can also provide alternate units, if applicable in your business."* No conversion factor, no field-appears sentence, nothing about transactions. The sibling page `…/tally-prime/inventory/manage-stock-item-tally/` was opened too and carries even less. |
| **Where the sentence really lives** | `help.tallysolutions.com/units-of-measurement/`, opened BY CONTENT: *"where: Provide the conversion factor between the simple or compound unit and the alternate unit."* The same page adds the sentence that actually grounds census 3.6's open voucher clause — *"The alternate unit gets saved for the stock item that you can use in transactions."* — and one this product currently diverges from: *"Once you have set the alternate unit, you cannot alter it."* |
| **🔴 Note the wording drift, which is the tell** | The repo's version reads *"between the simple or compound **units** and **alternative units**"*; the page reads *"between the simple or compound **unit** and the **alternate** unit"*. A quotation that has been retyped rather than re-opened drifts exactly like this. **The capability was never in doubt — it is genuinely attested. Only the page reference and the wording were wrong.** |
| **Fix** | **DONE this wave** in all five `src/` sites (`AlternateUnitConversion.cs`, `StockItem.cs`, `InventoryService.cs`, `StockItemMasterViewModel.cs`, `Schema.cs`), each pointing at `units-of-measurement/` with the verbatim sentence and **striking rather than deleting** the old reference so a reader sees what changed. **Also fixed this wave:** the two test files that carried it, `AlternateUnitTwoConversionsTests.cs` and `GroupBehaviourAndAlternateUnitTests.cs`. ⚠️ **STILL OWED:** census row 3.6's own citation line, flagged in that cell for A12 to amend in place rather than rewritten under A12's grade. ⚠️ **NOT TOUCHED AND NOT CHECKED:** `docs/full-clone-census.md:343` and `:403` cite the same page for a *different* claim (*Alter Units for Stock Items* / compound-unit alteration). That claim was **not** verified this wave and is **not** asserted to be wrong — it simply was not looked at. |

### IV-174 · 🔴 **CENSUS 3.6's VOUCHER CLAUSE IS NOT THE SMALL SEAM-FOLLOW IT LOOKS LIKE — 46 read-back sites would report a wrong quantity. NOT BUILT, AND THIS IS WHY.**

| | |
|---|---|
| **Severity** | **This entry prevents a HIGH-severity wrong-quantity defect rather than recording one.** Nothing is shipped under it. It exists so the next track does not do the obvious thing. |
| **The clause** | Row 3.6 is PARTIAL on exactly one clause: *"a line cannot yet be keyed in the alternate unit"*, and the cell names the seam — *"`InventoryVoucherLineViewModel.UnitOptions` / `ParsedQuantityInBaseUnit`, which already does exactly this for compound units; the only new work is that an alternate unit's factor lives on the ITEM, not the Unit."* The wave brief repeated that framing: *"The seam is named in the cell and already does this for compound units — follow it."* |
| **🔴 Why following it naively ships a wrong quantity** | **Because that last half-sentence is the whole problem, not a detail.** A line's unit is persisted as `InventoryAllocation.UnitId` / `VoucherInventoryLine.UnitId`, and **every** consumer re-derives the base quantity by asking the **UNIT** for its factor: `company.FindUnit(unitId).QuantityInBaseMeasure(qty)`. And `Unit.QuantityInBaseMeasure` opens with **`if (!IsCompound) return quantity;`** — measured, `src/Apex.Ledger/Domain/Unit.cs:147`. A simple alternate unit is **not** compound, so it carries no factor of its own and the item's factor is never consulted. Stamp "Box" on a line of an item whose factor is 1 Box = 10 Nos and the book stores `Quantity=2, UnitId=Box`; every report then reads it back as **2 Nos, not 20** — a silent 10× understatement of quantity, flowing into on-hand, valuation and COGS. `RateInBaseMeasure` has the identical `!IsCompound` short-circuit. |
| **The measurement** | **46 call sites across 23 files** use `QuantityInBaseMeasure` / `RateInBaseMeasure` outside `Unit.cs` itself. Nineteen files are in `Apex.Ledger` (`InventoryRegisters`, `InventoryMovements`, `BillsPending`, `ItemCostAnalysis`, `JobWorkReports`, `NegativeStock`, `AdditionalCostApportionment`, `BatchStockService`, `InventoryLedger`, `InventoryPostingService`, `ItemInvoiceStock`, `JobWorkService`, `MarketValuationService`, `StockValuationService`, `UqcResolver`, `VoucherValidator`, …), four in `Apex.Desktop`. **Each takes a `Company` and a `UnitId` and has no `StockItem` in hand**, which is precisely why the item-held factor cannot reach them without a signature change at every one. |
| **The two honest routes, neither of them small** | **(a)** Thread the `StockItem` into all 46 sites so the item's factor can be applied — a change across the valuation core, which is the area this project has already taken three demotions and *"three DIFFERENT unbounded Balance-Sheet errors"* in. **(b)** Convert at ENTRY so the stored line is always in base units with `UnitId` left null — far smaller and it matches row 3.6's own stated design (*"the base unit is stored and the alternate is ALWAYS derived… there is no second stored number to drift"*), **but** it breaks round-tripping: `InventoryVoucherLineViewModel.RehydrateUnitId` and the two `if (UnitId != posted.UnitId)` arms exist to reopen a voucher in the unit it was keyed in, and the rate is *per the unit the line is stated in* (WI-10 slice C), so the rate would need dividing too and the voucher would reopen reading "20 Nos" where the operator typed "2 Box". |
| **🔴 The decision, and who owns it** | **NOT BUILT THIS WAVE. Row 3.6 stays `PARTIAL` with its one clause open.** Shipping half of route (a) would have put a 10× quantity error into the reporting surface behind a green suite — this repo's single most-repeated failure mode. The clause is now *sourced* (`units-of-measurement/`: *"The alternate unit gets saved for the stock item that you can use in transactions."*), so it is buildable; it is **not** a seam-follow and should be scoped as its own slice with the route decided up front. |
| **Fix** | Pick (a) or (b) deliberately. If (b), decide first what a reopened voucher shows and what happens to the rate — both are operator-visible. Either way the proof demanded by the brief stands: the posted line must read back off the **reloaded** book in both units, and a later conversion-factor change must not restate posted quantities. **(b) satisfies that second property for free; (a) does not, and would need it pinned.** |

### IV-185 · **Showing "Alter Components (BOM)" only while the company's BOM feature is on is ours**

| | |
|---|---|
| **Severity** | **LOW. Class: VISIBILITY GATE.** No figure can be wrong under it. |
| **What TallyPrime does** | **The option is ATTESTED.** `help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/`, opened BY CONTENT 2026-10-25: *"In the Stock Item Alteration screen, enable the option Alter Components (BOM)"*. ⚠️ **The page does not tie that option to an F11 or F12 switch.** The only visibility condition it states is on the CREATION-side option: *"The Set Components (BOM) option will not displayed in Stock Item master"* when the unit of measure is not selected. Bounded to that one page. |
| **What we ship** | `StockItemMasterViewModel.ShowAlterComponentsBom => IsAltering && ShowBomSwitch`, i.e. the tick is realised only on the **Alteration** screen and only while `Company.SetComponentsBom` is on — the same gate the product already applies to the item's *Set Components (BOM)* switch, the BOM master and the Manufacturing Journal. `SaveMaster` also re-checks the gate before honouring a tick, so a tick left over from before the feature was switched off opens nothing. |
| **Why** | Every BOM surface in this product is hidden while the feature is off; an option that opened a hidden screen would be a dead end. The gate is ours and is labelled ours. |
| **Fix** | None owed. If a vendor page shows the option independent of any feature switch, drop `&& ShowBomSwitch` and the re-check, and update the gate test. |

### IV-186 · **"Alter Components (BOM)" opens the item's FIRST BOM by name directly — the vendor's BOM-name list step is ours to skip**

| | |
|---|---|
| **Severity** | ~~**LOW-MEDIUM. Class: NAVIGATION SHAPE.** An item with several BOMs reaches its other BOMs by a different route than the vendor's.~~ 🔴 **RE-CLASSED 2026-10-26 by A12 (wave 7 census, reviewer F5): MEDIUM. Class: MISSING CAPABILITY, not a navigation shape.** No route reaches an item's second and later BOMs, so they cannot be altered anywhere, and the vendor's add-a-BOM purpose of the tick cannot be met for an item that already has one. A divergence label cannot cover a missing capability. Census 3.9 stays `PARTIAL` on it until the M-BOM list below is built. |
| **What TallyPrime does** | Same page, same date: after enabling the option the vendor shows a BOM-name list first — *"In the Stock Item Multi Components (M BOM) screen, enter the name of the new BOM and press Enter."* — and only then the components screen (*"Stock Item Components (BOM) screen"*). The page does **not** say whether existing components are shown when an existing BOM's name is picked; its worked example is ADDING a BOM. |
| **What we ship** | `MainWindowViewModel.ShowComponentsBomForItem` opens `BomMasterViewModel.ForAlter` over the item's **first BOM by name (case-insensitive)**, every line pre-filled; saving goes through `BomService.AlterBom`, which keeps the BOM's id and swaps it at its own index. An item with **no** BOM gets the BOM creation screen with the item already chosen as the finished good. **There is no M-BOM list screen**, so a second or third BOM of the same item cannot be reached from this option. |
| **Why** | The defect this row was demoted for three times is an "alter" that is really create-and-append; the in-place alter of the item's BOM is the load-bearing part and is what ships. A BOM-name picker is a separate screen with no vendor sentence describing its alter behaviour. |
| **Fix** | Build the M-BOM name list between the tick and the components screen: pick an existing name → `ForAlter` over that BOM; type a new name → creation with the item preset. Report owed: this is recorded as NOT DONE in the track report. 🟢 **BUILT 2026-10-26 (wave 8, track `eb`) — PROPOSED FOR CLOSURE, A12 GRADES.** The tick now opens `BomNameListViewModel` (*Stock Item Multi Components (M BOM)*); see §2Q, IV-225..IV-227, and `BomNameListTests`. The "What we ship" cell above describes the wave-7 shape and is kept as history. |

### IV-187 · **CREATE over an occupied reorder (scope, target) refuses by name and loads the saved definition — nothing is merged or saved — is ours**

| | |
|---|---|
| **Severity** | **MEDIUM (the defect it replaces). Class: DATA LOSS prevented.** |
| **What TallyPrime does** | ⚠️ **SILENT on this case, and no sentence is claimed.** `help.tallysolutions.com/tally-prime/inventory/reorder-stock-items-reorder-status-and-reorder-quantity/`, re-opened BY CONTENT 2026-10-25, reaches the reorder level through *"Press Alt+G (Go To) > Alter Master > type or select Reorder Level (Stock Item) and press Enter."* and *"Press Ctrl+A to save."*; it contains no sentence about an item that already has a reorder level. Bounded to that page. |
| **What we ship** | `ReorderLevelsViewModel.Create` checks the picked (scope, target) first. If a definition exists it **saves nothing**, says *"A reorder level already exists for item 'X' — nothing was saved. Its saved figures are loaded for alteration; correct them and press Ctrl+A to save in place."*, and loads every saved figure through the same `LoadForAlteration` path `ForAlter` uses. The next Ctrl+A (or click) runs `Alter`, which keeps the id. Moving the scope or target away from the loaded definition returns the screen to Creation with blank figures, so the routed alteration can never silently RE-POINT the loaded definition at another target. The engine half is `ReorderLevelsService.Create`, which refuses the same case by name. **The figures keyed before the refusal are discarded, not merged.** |
| **Why** | The old upsert minted a new id and wiped every figure not re-keyed (reviewer probe P1c: 100 / 25 → Create, key 150, Ctrl+A → new id, minimum NULL, notice "saved"). Merging the half-typed form over the stored figures would silently combine values nobody chose together; showing the stored figures and requiring a second accept is the smaller invention. |
| **Fix** | None owed. If a vendor page describes creating over an existing level, align the message and the merge rule to it. |

### IV-188 · **The standalone "Bill of Materials" master on the Create menu is ours**

| | |
|---|---|
| **Severity** | **LOW. Class: EXTRA ROUTE.** It reaches the same BOM records the item-side route does. |
| **What TallyPrime does** | On the one page opened (`…/manage-inventory-in-manufacturing-tally/`, BY CONTENT 2026-10-25) BOMs are defined from the stock item — *"In the Stock Item Creation screen, set the option Set Components (BOM) to Yes and press Enter."* — and altered from it (IV-186). That page describes no separate BOM master. This is a bounded statement about one page, recorded as silence, not as a prohibition — the same treatment IV-77 gives batches. |
| **What we ship** | Masters → Create → Bill of Materials (`BomMasterViewModel`, create + Alt+D delete). It stays **create-only**: Ctrl+Enter on its existing-list has no alter arm, and `MasterVerbsW33ResidualAlterTests.The_masters_without_a_vendor_documented_alter_are_delete_only` still pins `IsAltering == false` for it. Altering goes only through the item-side tick. |
| **Why** | It shipped in Phase 6 and is the only place a BOM's carve-out lines were enterable before the item-side route existed. Removing it is a scope decision for the user. |
| **Fix** | User decision: keep as a labelled extra route, or fold BOM creation into the Stock Item Creation screen's *Set Components (BOM)* flow and remove the menu entry. |

## 2M. ADDED 2026-10-25 — WAVE 7 TRACK `dg`: GSTR-2B RECONCILIATION OUTPUT AND THE RE-HOMED GST YEAR WINDOW (census rows 6.16 / 6.19)

> Vendor page for every entry below, opened BY CONTENT on 2026-10-25:
> `help.tallysolutions.com/india-gst-status-gstr-2b-reconciliation-tally-2/`. Each sentence quoted is on that page.

### IV-175 · **The GSTR-2B reconciliation is scoped to the SELECTED STATEMENT'S OWN registration — the vendor consolidates by default and narrows with F3**

| | |
|---|---|
| **What TallyPrime does** | On the reconciliation page: *"If you have multiple GST registrations, the report shows consolidated data for all registrations by default."* The operator then narrows it with **F3 (Company/Tax Registration)**. |
| **What we do** | `Gstr2bReconReportViewModel.Rebuild` passes the selected statement's `Gstr2bSnapshot.OwningRegistrationId` to `Gstr2bReconciler.Reconcile`, so the books side is exactly the registration the portal statement was issued to. On a multi-registration book the subtitle and the export name that registration. There is no consolidated view and no F3 on this page. |
| **Why** | Before this change the page reconciled UNSCOPED and the engine's census-6.23 refusal (`GstReportSupport.EnsureRegistrationScoped`) replaced the whole page — and so any print or export of it — with refusal text on every book holding a second registration. A consolidated reconciliation needs N statements matched against N registrations' books at once, which the engine does not build. Scoping to the statement's owner is the narrowest honest answer: each imported statement is anchored to the one registration it was imported for (schema v66), and the vendor's own import screen asks for that registration. (The CGST rule 60(7) reading in `GstAdvancedReportOptions.cs` was not re-verified against cbic-gst.gov.in by this track and is not relied on here.) |
| **Proof** | `Gstr2bReconOutputTests.On_a_two_registration_book_the_export_carries_the_statements_own_registration_only` (mutation: drop the registration argument ⇒ red). |
| **Fix** | Build the vendor's consolidated default plus an F3 registration picker on this page if the user wants parity; until then this entry is the divergence. |

### IV-176 · **The LAYOUT of the exported / printed reconciliation is ours; the vendor's old-GSTR-2-format Excel option is NOT built**

| | |
|---|---|
| **What TallyPrime does** | The page attests the export ROUTE — *"Press Alt+E (Export) > Current > F8 (File Format)."* — and one option on it: *"you can export the GSTR-2B report to Excel as per the old GSTR-2 format."* It does not describe the exported document's layout, and it says nothing about printing this report. |
| **What we do** | Ctrl+P, Ctrl+E, bare E and Alt+E > Current all reach the page through `IMasterListExportSource`. The document leads with the statement period, its recipient GSTIN, the registration (multi-registration books only) and the window; then every bucket as a heading with its count — **empty buckets included as "(0)"** — and under it one row per document: `GSTIN  doc no`, date, taxable value, tax, and the partial-mismatch variances as two numeric columns of their own. The print path is the shared `ExportViewModel.TabularToPrint`, so Print and Export → PDF are one document. |
| **Why** | No vendor sentence describes the layout, so it is labelled ours. The identity cell is the widest printed column because the supplier GSTIN is what an auditor matches on; the variances get numeric columns so a spreadsheet can sum them and the printed page cannot clip them out of a sentence. |
| **Not built** | The *"old GSTR-2 format"* Excel option. Our XLSX is the generic tabular export. |

### IV-177 · **T2-22's *"Excluded, but available on Portal"* bucket is NOT built — the vendor page does not support mapping our automatic RCM set-aside into it**

| | |
|---|---|
| **What TallyPrime does** | *"Excluded, but available on Portal"* appears on the page only as a Reconciliation Statistics heading, among nine. **The page does not define it.** The only route into excluded portal transactions it describes is the operator's own act: *"If a transaction on the portal is not relevant for your books"*, after which *"The transaction moves to the Excluded Portal Transactions section."* |
| **What we do** | Our reconciler excludes reverse-charge portal lines **automatically** (symmetrically with the books register) and they land in no bucket. That is unchanged by this track. |
| **Why not built** | Putting an AUTOMATIC set-aside under a heading the vendor populates by a USER exclusion would be an inference from a heading's wording — the exact pattern this project has withdrawn twelve times. The optional T2-22 work in the wave plan was conditional on the vendor naming the bucket BY CONTENT for that case, and it does not. |
| **Fix** | Build the vendor's user-driven exclude / restore verbs first (they are attested above); the RCM question can then be answered against real behaviour. |

### IV-178 · **The re-homed ITC Set-Off, QRMP / IFF and Electronic Ledgers reports offer the financial year of the default as-of in addition to the page's own three**

| | |
|---|---|
| **What TallyPrime does** | F2 changes the period of a report freely (keyboard-shortcuts page, already cited in `GstAdvancedReportOptions.cs`). No vendor page we have opened describes a bounded year list on these three reports. |
| **What we do** | `ReportsViewModel.OfferTheDefaultAsOfYear` adds the financial year containing the surface's default as-of (the last voucher's date) to the page's `[FYStart.Year−2 .. FYStart.Year]` list before the lookup. A period outside the widened list is still refused by name. |
| **Why** | **T2-151(1):** on an aged book (FY start 01-04-2024, last voucher 09-09-2025) all three reports opened EMPTY by default. The bounded list itself is ours and pre-dates this track; this entry records that the bound now has one more member, and why. |
| **Proof** | `RehomedReportSurfaceBj2Tests.An_aged_book_opens_each_rehomed_report_on_its_default_year_with_figures` (three kinds; mutation: delete the call ⇒ all three red). |

## 2N. ADDED 2026-10-26 — WAVE 7 TRACK `dr`: DRILLED-VOUCHER EGRESS AND THE CHALLAN LINE LAYOUT (T2-159, census 12.7, IV-205..IV-214)

### IV-205 · **The delivery challan's LINE LAYOUT, revised: the description on its own full-width rows, the quantity in its entered unit WITH the unit** (wave 7, track dr; T2-160)

| | |
|---|---|
| **Severity** | **MEDIUM. Class: a document handed to a third party that did not say what it was written to say.** Three blockers on census 12.7, measured on emitted bytes: **F2** the Quantity cell carried **no unit** (12,345.678 MT printed `12,345.678`; 2 Doz printed `24`); **F8** printed Quantity x printed Rate did not equal the printed Amount on a compound-unit line (`24 · 0.83 · 20.00`); **F1** a long Description of Goods was **clipped with an ellipsis** inside cell 0. |
| **What TallyPrime does** | The entry screen records the **Quantity** and the **Rate** the operator types (`help.tallysolutions.com/tally-prime/sales-process/sales-order-tally/`, "Record a Delivery Note for Goods Dispatched"). **No TallyPrime page we opened states a printed layout for a delivery note** (IV-159's four pages). So printing the figures AS ENTERED is the faithful reading of the vendor's fields; the row geometry below is ours. |
| **What we ship** | `DeliveryChallanPrintProjector.StatedLine`: the quantity in the unit the line was stated in, followed by that unit's symbol (the item's base unit when the line has none), the rate per that unit, and Amount = Quantity x Rate rounded once — so `2 Doz · 10.00 · 20.00` multiplies out for every unit. `InventoryVoucherDetailViewModel` renders its rows through the same helper, so pane and document are locked by construction. **OURS:** each goods line is drawn as one or more FULL-WIDTH prose rows `"<n>. <description>"` above a figure row whose first cell is empty, wrapped by the new `PdfWriter.WrapToWidth` (the SAME measure `FitToWidth` clips with) at a fixed 500pt — under the narrowest content width the print panel can produce (A4 portrait, 36pt margins). The line number prefix is ours. This revises IV-160's "We do not wrap" for this one document only: the WRAP happens in the producer, as several ordinary full-width rows, so `ReportPdf`'s pagination and every other document are untouched. |
| **Tests** | `DeliveryChallanDocumentTests.A_compound_line_prints_in_its_entered_unit_so_quantity_times_rate_is_the_amount_on_the_page` (REWRITTEN from the test that pinned the non-multiplying document), `A_quantity_prints_with_its_unit_including_a_base_unit_line_with_decimals`, `A_long_goods_description_is_drawn_whole_and_never_ellipsised` — all on the emitted Tj operands. |
| **🔴 Carried, not argued away** | IV-164 (the reminder letter / confirmation of accounts are shaped per Report-Type rather than per-account letter) remains a legitimate reading for holding census 12.7 PARTIAL. This entry does not touch it. |
| **Fix** | None owed unless a vendor page states the challan layout; the geometry lives in the one `Project` method. |

### IV-206 · **Ctrl+E / Alt+E > Current on a drilled pure-stock voucher exports THAT voucher — and the file shape is ours** (wave 7, track dr; T2-159)

| | |
|---|---|
| **Severity** | **What it replaced was HIGH, a confidentiality defect: the export wrote the WHOLE Day Book.** Measured on main (93beb40) through the real key handler on a two-consignee book: Ctrl+E on a drilled Delivery Note opened Export titled `Day Book`, and the CSV contained the OTHER consignee's row (`... Delivery Note No. 2  Himalayan Textiles Cooperative`). Cause: `MainWindowViewModel.IsReportContext` excluded `LedgerVouchers` and `VoucherDetail` by screen id but not `InventoryVoucherDetail`, while the register stays bound beneath the pane. |
| **What TallyPrime does** | `help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/` (opened by content 2026-10-25): **Ctrl+E** *"To export the current voucher or report"*; **Alt+E** *"To open the export menu for exporting masters, transactions, or reports"*. The CURRENT voucher — the ground for exporting the pane and never the register. |
| **What we ship** | `InventoryVoucherDetailViewModel.BuildExportPanel`: CSV/XLSX/HTML/XML/JSON/ASCII carry the pane's own lines (Particulars · Direction · Quantity · Rate · Value) under a heading row naming the voucher type, number, date and party; **PDF is the delivery challan** when the voucher prints one (so Export > PDF and Print are one document), otherwise the same lines through `ExportViewModel.TabularToPrint`. **OURS:** the column set and the heading row. The vendor does not state an export layout for a stock voucher. |
| **Tests** | `DrilledVoucherEgressTests.Ctrl_E_on_a_drilled_delivery_note_writes_that_voucher_and_no_other_consignee`, `Alt_E_then_Current_on_a_drilled_delivery_note_exports_that_voucher`, `A_drilled_stock_journal_refuses_share_by_name_and_exports_only_its_own_lines` — asserted on the written file's bytes. |

### IV-207 · **Share (Alt+M > E-Mail / WhatsApp, Ctrl+M, M, W) on a drilled pure-stock voucher attaches its CHALLAN — or is refused by name** (wave 7, track dr; T2-159)

| | |
|---|---|
| **Severity** | **What it replaced was HIGH: the draft attached `Day Book.pdf`** — every consignee's movements, to whichever address the operator typed. Measured on main the same way as IV-206. |
| **What TallyPrime does** | Same page: **Ctrl+M** *"To e-mail the current voucher or report"*; **Alt+M** *"To open the Share menu for sharing transactions or reports through e-mail or WhatsApp"*. |
| **What we ship** | Both channels attach the exact bytes Print renders for the challan. **OURS:** a pure-stock voucher with no printed document (every kind but a Delivery Note with a party — IV-161) is **refused** on the window-level notice bar with the challan projector's own reason (`"M: E-Mail — This voucher has no printed document. ..."`), rather than attaching a generic grid of the lines. Reason: Share sends a DOCUMENT to a third party, and a document Print refuses to produce should not be produced by another door. The Alt+M menu still opens on such a pane so the refusal is reachable and said. |
| **Tests** | `DrilledVoucherEgressTests.Alt_M_then_E_Mail_on_a_drilled_delivery_note_attaches_its_challan_and_no_other_consignee`, `Ctrl_M_and_W_on_a_drilled_delivery_note_share_its_challan_and_no_other_consignee` (on the saved `.eml`, every base64 MIME part decoded, and the WhatsApp document), and the stock-journal refusal (realised in the visual tree). |

### IV-208 · **The SAME leak through a second door: report verbs on the voucher-ALTERATION screens drilled from the Day Book** (wave 7, track dr)

| | |
|---|---|
| **Severity** | **HIGH (same confidentiality class), found while counting the drilled views for T2-159.** `OpenDrillColumn` has nine call sites; two raise a report, seven raise non-report screens — `LedgerVouchers`, `VoucherDetail`, `InventoryVoucherDetail`, `InventoryVoucherEntry` (Ctrl+Enter alteration of a stock voucher), `VoucherEntry` (Ctrl+Enter alteration AND Alt+2 duplicate) and `PosBilling` (POS bill alteration). Only two were excluded from `IsReportContext`. Measured by probe on this branch: Ctrl+Enter on a Day Book Delivery Note opened its alteration screen with focus on no text field, and **Ctrl+E then opened Export for "Day Book"**; the same held on the accounting alteration and the Alt+2 duplicate. Report-parameter keys (F2, F12, Alt+F1/F2/F12, Ctrl+S, Alt+C on a comparative report) reached the hidden register in the same way. |
| **What TallyPrime does** | Not separately stated — this is the same "current voucher or report" ground as IV-206. Not a divergence in itself; recorded because it is a fix outside the row's literal scope. |
| **What we ship** | All three voucher-entry drill screens are excluded from `IsReportContext`, so the list equals the set of non-report screens `OpenDrillColumn` can raise. Ctrl+E / Alt+E / Alt+M / Ctrl+M / W on an alteration screen are now inert rather than acting on the register. **⚠️ Not built:** exporting or sharing the voucher being ALTERED (the vendor's Ctrl+E would export it) — that is an export of unsaved entry state and is reported as owed. The POS-bill alteration door is held by its own key-driven case (a bill posted through the real POS screen, then Ctrl+Enter from the Day Book). |
| **Tests** | `DrilledVoucherEgressTests.Egress_keys_on_an_alteration_drilled_from_the_day_book_never_build_the_register` (four doors: `inventory-alter`, `accounting-alter`, `accounting-duplicate`, `pos-alter`). Removing `PosBilling` alone from the exclusion fails exactly the `pos-alter` case. |

### IV-209 · **The same leak through a THIRD door: a column stacked OVER a drill page** (wave 7, track dr, found and fixed by the adversarial review)

| | |
|---|---|
| **Severity** | **HIGH (same confidentiality class).** Excluding the drill screens' own ids was not enough. Print Preview, Export, E-Mail and WhatsApp columns are appended ON TOP of a drilled pane and carry their own screen ids, while the Day Book stays bound beneath. Measured on the track head before the fix, through the real key handler on a two-consignee book: a drilled Delivery Note, then P (its challan preview), then Ctrl+E, Ctrl+M, Alt+M > E-Mail or W wrote or attached the **whole Day Book** with the other consignee; Ctrl+E then M did the same; and so did a drilled accounting voucher's preview (that half was already live on main). |
| **What TallyPrime does** | Same ground as IV-206/IV-207: "the current voucher or report". Not a divergence in itself. |
| **What we ship** | `MainWindowViewModel.IsReportContext` walks the cascade from the top; when a drilled voucher, ledger-vouchers or voucher-entry page is met before the report page, the report is not the context and its verbs stay inert. **OURS / owed:** the verbs on such a stacked column are silently inert rather than acting on the voucher beneath or refusing by name. |
| **Tests** | `StackedColumnOverDrillEgressTests.Probe` (seven routes, on the written CSV, the decoded `.eml` and the WhatsApp PDF) and the positive control `A_report_preview_still_e_mails_the_report`. Deleting the new clause reddens all seven routes; the control stays green. **Independently checked (wave 7 verifier):** a second positive control, `A_report_stacked_above_a_drill_page_keeps_its_own_export_and_share` (Day Book > Ledger Vouchers > F6 Monthly Summary, then Ctrl+E to a written CSV / Ctrl+M), was added because the first control has no drill page in the cascade at all: changing the walk's `return false` on meeting the report to `continue` (so any drill page ANYWHERE kills the report's verbs) left the entire Desktop suite green while the Monthly Summary lost Ctrl+E, Alt+M, P then W, F12 and Ctrl+H. The new control reddens on that mutation by name. Dropping `InventoryVoucherDetailViewModel` from the walk's type list reddens exactly the five delivery-note routes of `Probe`. |

## 2O. ADDED 2026-10-26 — WAVE 7 TRACK `dv`: VOUCHER-TYPE FLAGS, SCHEMA v68 (census row 5.11)

> **Renamed `2M` → `2O` by A12 at merge:** tracks `dg` (2M) and `dr` (2N) landed first and took those letters.

> **This track's pre-assigned block is IV-195..IV-204.** It takes **IV-195 … IV-200** and leaves IV-201..204
> unused. The section letter `2M` is this branch's guess at the next free letter; a sibling track may take the
> same letter this wave, and A12 renames the section at merge if so — the entry NUMBERS are the stable handle.
>
> **Vendor ground for the whole section** — `help.tallysolutions.com/voucher-types-tally/`, opened BY CONTENT on
> 2026-10-26 with a fresh fetch:
> - *Set up invoice sharing options*: **"Enable Print voucher after saving to automatically open the Voucher
>   Printing screen for the voucher, once it is saved."**
> - *Set up transaction options*: **"Enable Provide narration for each ledger in voucher to add narration for
>   individual ledgers"** — the sentence continues that this applies by voucher mode, such as Accounting/Item
>   Invoice or As Voucher, and that it does not apply to Delivery Note, Receipt Note, Sales Order, Purchase
>   Order, Physical Stock, Stock Journal, Rejection In and Rejection Out.
> - **The page does NOT say a ledger narration prints, nor where it sits on screen, nor what happens to it on an
>   alteration.** Those halves below are ours. The struck *"created using"* phrase (T2-155, IV-117) is NOT used
>   anywhere in this section.

### IV-195 · `Print voucher after saving` now fires on the Alt+2 duplicate, inventory and POS creation doors — WHICH document each opens is ours

| | |
|---|---|
| **Severity** | **LOW. Class C. Area: ENT.** Closes the gap IV-117 recorded as *"an unclosed half of census 5.11 rather than a decision"*. No figure moves. |
| **What TallyPrime does — attested** | The *Print voucher after saving* sentence quoted in the section head: the Voucher Printing screen opens **for the voucher**, once it is saved. No base-type restriction is stated. |
| **What we ship** | Each door opens the printing column for **the voucher that door just committed**, read off the screen's own `SavedVoucherId`, and only when the voucher TYPE has the flag on. (1) **Alt+2 duplicate** (`ShowVoucherDuplicate`): the same `OpenVoucherPrintingAfterSave` as `OpenVoucher`, on the NEW voucher's id, never the source's. (2) **Inventory vouchers** (`OpenInventoryVoucher`): `OpenInventoryVoucherPrintingAfterSave`, which reuses the drilled voucher's own Ctrl+P projection (`InventoryVoucherDetailViewModel.BuildPrintPreview`) — so a **Delivery Note opens its delivery challan**; (3) **POS bill**: the type flag now ALSO triggers the retail-receipt preview that the POS configuration's own *print after save* already opened. |
| **What is OURS** | (a) **Which document is "the Voucher Printing screen" on a stock voucher and on a POS bill.** We open the document this product prints for that voucher on Ctrl+P — the challan for a Delivery Note (IV-159's layout), the retail receipt for a POS bill. (b) **Every other stock kind (Receipt Note, Rejection In/Out, Purchase/Sales Order, Stock Journal, Physical Stock) has no printed document in this product** (IV-161), so with the flag on the save raises that arm's own NAMED notice instead of opening nothing silently. (c) **Creation only** — the alteration door stays unwired, for IV-117's reason. |
| **Tests** | `PrintAfterSavingDoorsTests` — a with-flag and a without-flag test per door; the opened preview's `SourceVoucherId` is asserted to be the saved voucher's id, and a null preview fails. Mutation log: `Apex-Review-Artifacts/dv-mut-pas.txt` (P1–P6 each redden named tests) and `w7-dv-mut.txt` (P7: dropping the duplicate door's flag test). |

### IV-196 · Per-ledger narration is offered on the Dr/Cr, Single Entry and Accounting Invoice grids — NOT on Item Invoice entry

| | |
|---|---|
| **Severity** | **MEDIUM. Class B (attested, partly built). Area: ENT.** |
| **What TallyPrime does — attested** | The *Provide narration for each ledger in voucher* sentence in the section head, naming the Accounting/Item Invoice and As Voucher modes. |
| **What we ship** | With the flag on, a narration field sits under each Dr/Cr line (As Voucher), under each particular (Single Entry, the layout Payment/Receipt/Contra open in) and under each particulars line of an **Accounting Invoice**; it is next in Tab order after that line's Amount. It persists on `entry_lines.narration` (schema v68), reloads on alteration and on Alt+2 duplicate, and round-trips canonical JSON/XML. With the flag off the field is realised and hidden. |
| **🔴 NOT BUILT — the gap** | ✅ **SUPERSEDED in wave 8 (track `ec`) — see IV-235; the row below is the wave-7 record.** **Item Invoice ENTRY.** The item screen's ledger legs (party, stock/value ledger, additional costs) have no narration field, so a narration cannot be KEYED there. What IS built for item invoices is the no-loss half: an alteration CARRIES a narration an import put on the party, value or additional-cost leg (IV-199). The vendor's own exclusion list (Delivery Note … Rejection Out) needs no work: those screens have no ledger lines in this product. |
| **What is OURS** | The field's placement (a full-width row under the line), its caption and its Tab position. The page says nothing about layout. ALSO ours: a line that was POSTED carrying a narration keeps its field visible on alteration even after the type's flag is switched off, so text the operator cannot see is never silently kept or dropped. |
| **Tests** | `LineNarrationEntryTests` (Dr/Cr and Single Entry through the real Tab/typing/Ctrl+A, reload, alteration, export→import), `LineNarrationInvoiceModesTests` (Accounting Invoice through the real keys, read off the reloaded book; the flag-off control). |

### IV-197 · A line narration PRINTS under its line on the voucher document — the vendor page is silent on printing

| | |
|---|---|
| **Severity** | **LOW. Class C. Area: PRN.** |
| **What TallyPrime does** | **SILENT on the page above.** No sentence on `voucher-types-tally/` says a ledger narration prints. Bounded to that page; no universal negative is claimed. |
| **What we ship** | `VoucherPdf` prints the narration indented under its posting line, wrapped to the particulars column, on the SAME F12 *Show Narration* toggle as the voucher narration; the on-screen preview mirrors it. |
| **🔴 NOT BUILT** | ✅ **SUPERSEDED in wave 8 (track `ec`) — see IV-237; the row below is the wave-7 record.** The GST **invoice** documents (`InvoicePdf` — the item and service tax invoices) do not print line narrations. A narration keyed on an Accounting Invoice (IV-196) is stored and exported but does not appear on its printed tax invoice. |
| **Tests** | `LineNarrationEntryTests.The_line_narration_prints_on_the_voucher_pdf` (emitted PDF bytes of the reloaded voucher, and the preview mirror). |

### IV-198 · Line-narration normalisation and its canonical encoding are ours

| | |
|---|---|
| **Severity** | **LOW. Class C. Area: IO.** |
| **What we ship** | `EntryLine` trims the narration and stores blank/whitespace as `null`, so an unnarrated line is byte-identical to a pre-v68 line (ER-13). Canonical JSON omits the `narration` key when null; canonical XML writes a `narration` attribute on `<line>` only when set. The v67→v68 migration adds the nullable column and backfills nothing; the v68 downgrade drops it with every child row and index preserved. |
| **Why** | The canonical formats are this product's own, and "absent means none" keeps every existing export byte-stable. |
| **Tests** | `CanonicalLineNarrationRoundTripTests`, `LineNarrationV68SchemaTests` (a real v67 database climbs to v68; migration DDL equals `CreateV1`; downgrade; fresh-handle read-back). |

### IV-199 · No alteration may silently drop a line narration the operator never saw — refused BY NAME; item-invoice legs CARRY it

| | |
|---|---|
| **Severity** | **This entry PREVENTS a data loss rather than recording one.** Class C (ours). Area: ENT. |
| **The loss it stops** | A narration can sit on a line the altering screen does not show as an editable field: a re-derived GST/TCS leg on an item invoice, a TDS-Payable leg, every line of a POS bill. Each of those screens REBUILDS its lines on accept, so before this the narration vanished under "altered.". A canonical import is all it takes to put one there. |
| **What we ship** | (1) `VoucherAlterationDerivedLegs.DroppedLineNarrationRefusal`: every posted narration must survive onto the replacement or have been loaded into an editable field (so a deliberate clear on the Dr/Cr grid stays legal); otherwise the accept is refused naming the ledger and the words. ⚠️ *A12 review, wave 7:* the editable fields' CURRENT texts are taken out of the rebuilt pool before a derived leg's narration is matched, so an editable line and a derived leg carrying the SAME words can no longer share one surviving copy (that shape dropped the TDS-Payable leg's narration under "altered."); and the below-threshold TDS deductee row now re-opens WITH its narration (`Untagged` had dropped it, which made every alteration of such a voucher refuse with a false "no field" sentence). Run at the top of `VoucherEntryViewModel.CommitAlteration` (both the grid and the item-invoice paths) and before `Replace` in the POS bill alteration. (2) The item-invoice rebuild CARRIES a narration on the party and value legs while their ledger is unmoved, and on an additional-cost leg matched by ledger alone (a narration moves no figure, so an amended amount keeps it). |
| **What is OURS** | The whole rule: the vendor documents no alteration behaviour for ledger narrations. Refusing rather than dropping follows this register's standing practice for every other child an alteration cannot show (T1-22, T1-23). |
| **Tests** | `LineNarrationInvoiceModesTests.An_item_invoice_re_accept_keeps_the_narrations_an_import_put_on_its_party_and_value_legs`, `…A_narration_on_a_re_derived_tax_leg_is_refused_by_name_and_left_on_the_book`, `…An_additional_cost_leg_keeps_its_narration_when_its_amount_is_amended`, `…A_pos_bill_alteration_that_would_drop_a_line_narration_is_refused_by_name`; the grid's excuse path (an edited narration is NOT refused) is pinned by `LineNarrationEntryTests.The_line_narration_reloads_on_alteration_and_an_edit_to_it_persists`. Each reddens under its named mutation (`Apex-Review-Artifacts/w7-dv-mut.txt`, N4–N9). |

### IV-200 · The More Details footnote still withholds "Ledger Narration" — its REASON changed, the row did not move

| | |
|---|---|
| **Severity** | **LOW. Class C. Area: ENT (census 14.4, not 5.11).** |
| **What changed** | The footnote used to say *"this book stores narration on the voucher, not on the line"*. After v68 that is false, so the reason now reads that an item invoice does not offer it and the Dr/Cr and service grids take it when the voucher type allows. The entry stays in the withheld list: the More Details panel itself still has no Ledger Narration row. Promoting it is census 14.4's work. |
| **Tests** | `MoreDetailsTests.Per_line_narration_is_stored_but_the_footnote_still_names_what_this_panel_cannot_offer` (renamed from `Per_line_narration_genuinely_has_nowhere_to_be_stored`, which reddened on v68 as it was written to). |

## 2P. ADDED 2026-10-26 — WAVE 8 TRACK `ed`: OVERDUE RECEIVABLES / OVERDUE PAYABLES (census row 11.13, IV-245..IV-254)

> **This track's pre-assigned block is IV-245..IV-254.** It takes **IV-245 … IV-247** and leaves IV-248..254
> unused. The section letter `2P` is this branch's guess at the next free letter; A12 renames it at merge if a
> sibling track took it — the entry NUMBERS are the stable handle.
>
> **Vendor ground for the whole section** — `help.tallysolutions.com/manage-receivables-outstanding-tally/`
> (TallyPrime; page title *"How to Manage Outstanding Receivables in TallyPrime"*), opened BY CONTENT on
> 2026-10-26:
> - **"Alternatively, you can use Alt+G (Go To) > Overdue Receivables."** — and the same sentence for
>   **Overdue Payables** in the Payables section.
> - **"To view only overdue bills, use Basis of Values to select Overdue Bills in Type of Bills."**
> - **"Review the outstanding bills, party names, pending amounts, due dates, overdue days, and other details."**
> - **"Select any outstanding line and press Enter to show/hide the transaction details."**
>
> The page does NOT mention Exception Reports, F2, an as-on date, or how the due date itself is treated. The
> Tally.ERP 9 help pages that do describe an "Overdue by days" column were NOT used: Tally.ERP 9 is not ground
> truth for this project.

### IV-245 · **A bill due EXACTLY on the as-of date is not overdue** (wave 8, track ed)

| | |
|---|---|
| **Severity** | **MEDIUM. Class C (ours). Area: RPT.** It decides whether a bill appears on the report at all on one day in its life. |
| **What TallyPrime does** | The page lists "overdue days" as a column and says nothing about the due day itself. No vendor or statutory source found speaks to it. |
| **What we ship** | `OutstandingBill.IsOverdue(asOf)` = `OverdueDays(asOf) > 0`, i.e. due date strictly before the as-of date. A bill due ON the as-of date is "due" (`IsDueBy`, the existing "due till today" figure) but not listed as overdue; the next day it appears with **1 day overdue**. **OURS:** the boundary. It matches the boundary the existing ageing buckets already draw ("Not due" holds zero overdue days), so Bills Receivable's ageing and this report cannot disagree about the same bill. |
| **Tests** | Mutating `IsOverdue` to `IsDueBy(asOf)` (measured 2026-10-26) reddens six by name: `OverdueBillsTests.A_bill_due_exactly_on_the_as_of_date_is_not_overdue_until_the_next_day`, `…An_earlier_as_of_date_lists_nothing_and_a_settled_bill_is_never_overdue`, `…Overdue_receivables_list_only_bills_past_due_with_odd_paisa_pending_and_the_opening_voucher`; `OverdueExceptionReportsTests.Overdue_receivables_figures_and_due_date_boundary_off_the_reloaded_book`, `…Overdue_receivables_export_csv_and_print_pdf_carry_the_figures_and_omit_the_boundary_bill`, `…A_period_ending_on_the_first_due_date_lists_nothing_and_says_so`. |

### IV-246 · **Enter drills to the voucher (bill row) or the ledger (party row) in a NEW column, instead of showing/hiding details in place** (wave 8, track ed)

| | |
|---|---|
| **Severity** | **LOW. Class B (shape divergence). Area: RPT.** |
| **What TallyPrime does** | "Select any outstanding line and press Enter to show/hide the transaction details." Pressing Enter on those details then opens the voucher. |
| **What we ship** | Party heading row → that party's ledger vouchers over the report's window; bill row → the voucher whose New-Ref/Advance allocation raised the bill (`OutstandingBill.OpeningVoucherId`). Each opens as a cascade column; Esc returns. The bill drill opens the read-only voucher DETAIL, where the vendor page opens the voucher in alteration mode. **OURS:** no in-place show/hide level — the accounting report grid has no expand/collapse row model, so the operator reaches the same voucher one level sooner. |
| **Tests** | `OverdueExceptionReportsTests.Enter_on_a_bill_row_opens_its_voucher_and_on_a_party_row_opens_the_ledger` (real Enter on a shown window), `Enter_on_the_overdue_total_row_is_a_safe_no_op`. |

### IV-247 · **The two views are ALSO menu rows under Gateway > Exception Reports, and they are separate reports rather than a Type-of-Bills filter on Bills Receivable / Payable** (wave 8, track ed)

| | |
|---|---|
| **Severity** | **LOW. Class B. Area: RPT / NAV.** |
| **What TallyPrime does** | Reaches them by **Alt+G (Go To) > Overdue Receivables / Overdue Payables**, or by **Basis of Values > Type of Bills > Overdue Bills** on the Receivables / Payables report. |
| **What we ship** | Two report kinds, `OverdueReceivables` / `OverduePayables`, listed under **Gateway > Exception Reports** (where census row 11.13 counts them). Go To's index is walked from the menu, so the same two rows are what make **Alt+G > Overdue Receivables / Overdue Payables** work — the vendor's route. Each carries the as-of (F2) and period (Alt+F2, as of the period's end) of the shared report surface, and prints / exports through the accounting report path (label = reference · overdue days, then due date · bill date; amount = pending). **OURS:** the Exception Reports menu placement and the row layout. **⚠️ NOT BUILT:** the *Basis of Values > Type of Bills > Overdue Bills* filter on Bills Receivable / Bills Payable, and an ageing (F6) basis switch on these views. |
| **Tests** | `OverdueExceptionReportsTests.Alt_G_Go_To_opens_the_overdue_view_by_name` and `The_exception_reports_menu_reaches_the_overdue_view_by_keyboard` (both through the real key handlers, both kinds). Deleting either menu row reddens that kind's two cases by name. |

## 2Q. ADDED 2026-10-26 — WAVE 8 TRACK `eb`: THE M BOM NAME LIST (census row 3.9, IV-225..IV-227)

> Vendor page, opened BY CONTENT on 2026-10-26:
> `help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/`.
> The sentences this section rests on, each read on that page: *"In the Stock Item Alteration screen, enable the
> option Alter Components (BOM) to add a new BOM"*; *"In the Stock Item Multi Components (M BOM) screen, enter the
> name of the new BOM and press Enter."*; and, on creation, *"If you want to add one more BOM for this item, enter
> the new BOM name under the Standard BoM and press Enter."* So the M BOM screen shows the item's existing names
> with the new name typed beneath them, and Enter goes on to the components screen. **That much is CLONED.**
> The page's worked example is only ever ADDING a BOM; what it does not say is recorded below as ours.
> ⚠️ **NOT BUILT, AND NOT COVERED BY ANY LABEL HERE:** the same page documents *Ctrl+C (Copy BoM)* on the
> Stock Item Components (BOM) screen. We have no Copy BoM. That is a missing capability, reported as such in the
> track report, not a divergence.

### IV-225 · **Opening an EXISTING name on the M BOM list for an in-place alteration is ours**

| | |
|---|---|
| **Severity** | **LOW. Class: WIDENED ROUTE.** It is the only route to an item's second and later BOMs. |
| **What TallyPrime does** | The page documents the M BOM screen only for adding a name. It does not say what Enter on an already-listed name does. |
| **What we ship** | `BomNameListViewModel` lists the item's BOMs in stored order with the first highlighted; Up/Down move the highlight (wrapping) through `MainWindowViewModel.StepActive`; Enter (or Ctrl+A) runs `OpenBomFromNameList`, which opens `BomMasterViewModel.ForAlter` over the highlighted BOM. Ctrl+A there saves through `BomService.AlterBom`, which keeps the id and swaps the BOM at its own index, so the item's other BOMs and any `job_work_orders.fill_components_bom_id` are untouched. Locked by `BomNameListTests.The_second_BOM_of_an_item_is_reached_by_arrow_and_Enter_and_altered_in_place`. **Wave-8 review F1 (fixed in the same wave):** the screen opens with the caret in the new-name box, per the page's *"enter the name of the new BOM and press Enter"*, so typed letters land there and Enter creates the typed BOM. Before the fix nothing held focus, so the letters went nowhere and Enter opened the highlighted BOM for alteration. Up/Down still move the highlight while the caret is in that box, through an arm in `MainWindow.OnKeyDown` scoped to that box by name. Locked by `…The_list_opens_with_the_caret_in_the_new_name_box_so_typing_then_Enter_creates_the_typed_BOM` and `…Arrows_walk_the_names_while_the_caret_is_in_the_new_name_box`. |
| **Why** | Without it, an item's second and later BOMs could not be corrected anywhere. That was the open clause of census 3.9. |
| **Fix** | None proposed. Re-open only if a vendor page describes Enter on a listed name differently. |

### IV-226 · **Typing a name the item already has opens THAT BOM for alteration, not a refused create**

| | |
|---|---|
| **Severity** | **LOW. Class: INPUT SHAPE.** |
| **What TallyPrime does** | The page says only that a NEW name is typed and Enter pressed. It is silent on typing a name that already exists. |
| **What we ship** | The match is case-insensitive, the per-item uniqueness rule the schema's `UNIQUE (stock_item_id, name COLLATE NOCASE)` index enforces. A typed existing name opens that BOM exactly as arrow + Enter would. A typed new name opens `BomMasterViewModel.ForNewBomOfItem`: a creation screen captioned *Stock Item Components (BOM) Creation*, with the name filled in and the finished good fixed to the item. A typed name wins over the highlight. With nothing typed and no BOM listed, Enter refuses with *"Enter the name of the new BOM."* Locked by `BomNameListTests.Typing_an_existing_name_opens_that_BOM_for_alteration_not_a_duplicate_create` and `…Typing_a_new_name_and_Enter_creates_a_further_BOM…`. |
| **Why** | A create under a name the item already has can only be refused by the uniqueness rule. Opening the BOM the operator named is the useful reading. |
| **Fix** | None proposed. |

### IV-227 · **Leaving the components screen returns to the menu, not to the M BOM list**

| | |
|---|---|
| **Severity** | **LOW. Class: NAVIGATION SHAPE.** |
| **What TallyPrime does** | Not stated on the page. |
| **What we ship** | The M BOM list and the components screen are each a page column. `OpenPageColumn` REPLACES the page, so Escape on the components screen goes back to the menu column. To reach another BOM of the same item, the operator re-opens Stock Item Alteration and ticks the option again. The same holds after a create from the list (wave-8 review F3): the screen stays a creation screen with the item fixed, and there is no route back to the list short of re-navigating. No data is at risk. Escape on the M BOM list writes nothing (`BomNameListTests.Escape_on_the_name_list_leaves_it_and_writes_nothing`). |
| **Why** | The shell keeps at most one page column; stacking the components screen over the list would need a new column shape. |
| **Fix** | Optional: stack the components screen beside the list (as the batch-allocation sub-screen does), so Escape returns to the list. |

## 2R. ADDED 2026-10-26 — WAVE 8 TRACK `ec`: THE REST OF THE VOUCHER-TYPE FLAGS (census rows 5.11 and 5.6(b))

> **This track's pre-assigned block is IV-235..IV-244.** It takes **IV-235 … IV-239** and leaves IV-240..244
> unused. The section letter `2P` was this branch's guess (renamed `2R` at merge by A12, 2026-10-29: `ed` took `2P` and `eb` `2Q`) at the next free letter; a sibling track may take the
> same letter this wave, and A12 renames the section at merge if so — the entry NUMBERS are the stable handle.
>
> **Vendor ground, opened BY CONTENT on 2026-10-26 with a fresh fetch:**
> - `help.tallysolutions.com/voucher-types-tally/`: **"Enable Print voucher after saving to automatically open the
>   Voucher Printing screen for the voucher, once it is saved."** — the page states no voucher-type restriction for
>   it. And **"Enable Provide narration for each ledger in voucher to add narration for individual ledgers based on
>   the voucher mode, such as Accounting/Item Invoice or As Voucher."**, with the exclusion list (Delivery Note,
>   Receipt Note, Sales Order, Purchase Order, Physical Stock, Stock Journal, Rejection In, Rejection Out). The page
>   says nothing about where the field sits, about printing a ledger narration, or about alteration.
> - `help.tallysolutions.com/accounting-entry-tally/` and `…/tally-prime/accounting/accounting-faq/` for the
>   activation prompt (quoted in IV-239; the census row 5.6 records both pages).
> - `help.tallysolutions.com/principal-manufacturer/` and `…/job-worker/` were opened by content for IV-238 and
>   **mention no printing at all** for Job Work orders or Material In/Out. That is a statement about two pages,
>   not a universal negative, and it is not used as a ground for anything below — it is why IV-238 stays a
>   divergence.

### IV-235 · Item Invoice ledger narration: the party, the value ledger and each additional-cost row take one — WHERE the fields sit is ours

| | |
|---|---|
| **Severity** | **MEDIUM → closes the IV-196 gap.** Class B (attested, now built). Area: ENT. |
| **What TallyPrime does — attested** | The *Provide narration for each ledger in voucher* sentence in the section head, naming Item Invoice. |
| **What we ship** | With the flag on, the invoice header grows a narration row under its pickers: the **party** ledger's field (both invoice modes — so the Accounting Invoice's party leg, which IV-196 left without one, now has it too) and the **value (Sales/Purchases) ledger**'s field (item invoice only, and only while that picker is shown). Each **additional-cost** row grows a field under it, next in Tab order after the row's amount. The fields post on the legs they name (`entry_lines.narration`, v68 — no schema change here), reload INTO THOSE FIELDS on alteration and on Alt+2 duplicate, and round-trip canonical JSON/XML. The fields are the one source on accept, so an untouched posted narration survives, an edit persists and a **deliberate clear is honoured** — the dropped-narration guard (IV-199) excuses exactly what was loaded into a field. Engine-derived legs (GST, TCS) still take none, and a narration an import put on one is still REFUSED by name. |
| **What is OURS** | (a) The placement: one header row (party, then value ledger) below the header pickers, so Tab reaches it from the value-ledger picker; and a full-width row under each cost row. (b) A cost row carrying ONLY a narration is refused as incomplete rather than its words dropped. (c) A value-ledger narration typed before a voucher class that pre-maps ledgers is chosen is refused by name (the class splits the value leg, so the words have no single leg to ride). (d) IV-196's visibility rule extends here: a posted narration keeps its field visible even if the type's flag is later switched off. |
| **Tests** | `LineNarrationItemInvoiceEntryTests` — the flag-off control, the real Tab/typing/Ctrl+A path read off a reloaded book, the cost row through the real Tab, the narration-only cost row refusal, the alteration (reload, edit, clear), canonical export→import, and the Accounting Invoice party leg. Mutation log `Apex-Review-Artifacts/w8-ec-mut.txt` M1–M9, M12. |
| **AMENDED 2026-10-27 (w8 fix, review ec LOW-2)** | A header narration typed in Item Invoice mode and left behind by Ctrl+H is refused by name instead of dropped: Accounting Invoice has no single value leg for the value-ledger narration, and As Voucher reads neither the party nor the value narration. `VoucherEntryViewModel.Accept` now refuses (`HeaderNarrationOrphanedByModeError`) and names the way out (Ctrl+H back to Item Invoice). Wording OURS. Test: `LineNarrationInvoiceModesTests.A_value_narration_left_behind_by_a_mode_switch_is_refused_by_name_not_dropped`. Mutation L2. The As-Voucher arm is covered by the same code path but is not separately pinned. |

### IV-236 · POS bill ledger narration: the Sales ledger and each tender ledger take one

| | |
|---|---|
| **Severity** | **MEDIUM → closes the POS half of the census 5.11 entry gap.** Class B. Area: ENT. |
| **What TallyPrime does — attested** | The same sentence. A POS bill is a Sales voucher in invoice mode in this product. |
| **What we ship** | With the POS type's flag on, a field sits under the **Sales ledger** picker and under every **tender** row (Cash in single mode; Gift, Card, Cheque and Cash in multi mode). Each posts on its own leg; the tender lines are `PosTenderService.BuildTenderDebitLines`' lines re-stamped, so a bill without narrations posts byte-identically. On alteration each posted narration is loaded back into its field (the tender line is matched to its tender by ledger and amount), an edit or a clear is honoured, and a narration on a re-derived GST leg is still refused by name. |
| **What is OURS** | The placement and caption of the fields; the matching rule above. ⚠️ The pre-wave-8 test `LineNarrationInvoiceModesTests.A_pos_bill_alteration_that_would_drop_a_line_narration_is_refused_by_name` stamped its narration on the SALES leg, which now has a field and is carried; it was moved to a GST leg (where the refusal still applies) rather than deleted. |
| **Tests** | `LineNarrationPosBillTests` — flag-off control, the real Tab/typing/Ctrl+A path (Sales and cash), alteration (reload, edit, clear), canonical export→import. Mutations P1–P6. |
| **AMENDED 2026-10-27 (w8 fix, review ec MEDIUM-2 / MEDIUM-3)** | **(a) No tender narration is dropped silently.** A narration typed under a tender that posts no leg (an unused Gift/Card/Cheque row; the Cash row when the non-cash tenders pay the whole bill; any non-cash row after Alt+I back to Single tender) is now REFUSED by name in `PosBillingViewModel.BuildPosBill` (`UnpostedTenderNarrationError`) and nothing posts; the message names the row and the way out. The wording is OURS; the rule is the one the cost rows already keep. **(b) Every tender leg, not just cash, is now pinned:** a Card + Cash bill with distinct narrations typed through the realised boxes posts each on its own leg of the reloaded book, and a multi-tender alteration re-keys each into its own row. Tests: `LineNarrationPosBillTests.A_multi_tender_bill_posts_each_tenders_own_narration_on_its_own_leg`, `…A_multi_tender_alteration_reloads_each_tenders_narration_into_its_own_row`, `…A_narration_under_a_tender_that_posts_no_leg_is_refused_by_name_not_dropped` (gift / cash / single). Mutations M2, M3 (the reviewer's R1), M4. |

### IV-237 · Ledger narrations print on the TAX INVOICE and on the POS RECEIPT — the vendor page is silent on printing

| | |
|---|---|
| **Severity** | **LOW. Class C. Area: PRN.** Closes IV-197's "NOT BUILT" row. |
| **What TallyPrime does** | **SILENT** on `voucher-types-tally/`, bounded to that page as in IV-197. |
| **What we ship** | `InvoicePrintData.LedgerNarrations` / `PosReceiptData.LedgerNarrations`, filled from the POSTED voucher by `VoucherPrintProjector.LedgerNarrationsOf` (posting order, each named by its ledger, ASCII-folded). The tax invoice prints them as `<ledger>: <words>` lines under the remarks, on the same F12 *Show Narration* toggle and measured by the same closing-block arithmetic; the POS receipt prints a **Ledger Narrations** block under the payment lines. Both on-screen mirrors show the same. A document with no ledger narration is byte-identical (ER-13). The item, service and §34-note projections all carry the list. |
| **What is OURS** | The whole layout. **Not measured:** the service (Accounting) invoice and the §34 note carry the list through the same projector call, but no test prints one of them with a ledger narration — the claim is the code path, not an emitted byte. |
| **Tests** | `LineNarrationItemInvoiceEntryTests.The_item_invoice_ledger_narrations_print_on_its_tax_invoice` (+ its control), `LineNarrationPosBillTests.The_pos_receipt_prints_the_ledger_narrations` (+ its control). Mutations M10, M11, M13, P7–P9. |

### IV-238 · `Print voucher after saving` on the Job Work order and Material In / Out doors — read, but there is NO document to open, so the door SAYS so

| | |
|---|---|
| **Severity** | **LOW. Class C (divergence, labelled as ours). Area: ENT/PRN.** |
| **What TallyPrime does — attested** | The *Print voucher after saving* sentence: the Voucher Printing screen opens **for the voucher**, no base-type restriction stated. **What a Job Work order or a Material In/Out voucher PRINTS is not stated** on any TallyPrime page we opened (section head). |
| **What we ship** | Both creation doors (`OpenJobWorkOrder`, `OpenMaterialMovement`) now wrap their save the way `OpenInventoryVoucher` does: navigate first, then — only when the type's flag is on — route the voucher THIS screen saved (its new `SavedVoucherId`) through `OpenInventoryVoucherPrintingAfterSave`. This product prints no document for those kinds (IV-161, deliberately narrow: Tally.ERP 9 pages describe a Material Out challan and Tally.ERP 9 is not ground truth), so the door raises its NAMED notice — *"This voucher has no printed document. …"* — instead of the flag being silently ignored. |
| **🔴 NOT BUILT, stated rather than implied** | **No Voucher Printing screen opens for these vouchers.** The census cell asked for the preview of THAT voucher; there is no document to preview, and inventing one (a Material Out challan) on a Tally.ERP 9 lead is what IV-161 refuses to do. When a TallyPrime page settles it, the change is `DeliveryChallanPrintProjector.PrintsAChallan` (one predicate) and both doors light up without further wiring. **The Payroll voucher door is OUT of scope by ruling 30 and was not touched.** |
| **Tests** | `PrintAfterSavingJobWorkDoorsTests` — Job Work Out Order (flag on: the screen's saved id is the order on the reloaded book and the notice is raised; flag off: nothing), Material Out (flag on/off theory), Material In. Every save goes through the real Ctrl+A. Mutations J1–J6. |
| **🔴 AMENDED 2026-10-27 (w8 fix, review ec MEDIUM-1) — MATERIAL OUT IS NOT "NO DOCUMENT"** | The "no source speaks" premise above is **false for Material Out**. The vendor pages are silent, but the R7 order's SECOND source speaks: **CBIC Circular No. 38/12/2018, dated 26 March 2018** (`cbic-gst.gov.in/pdf/circularno-38-cgst.pdf`, opened BY CONTENT 2026-10-27), para 8.2: *"Rule 45 of the CGST Rules provides that the inputs, semi-finished goods or capital goods being sent for job work … shall be sent under the cover of a challan issued by the principal, containing the details specified in rule 55"*; para 8.4(i): *"The principal shall prepare in triplicate, the challan in terms of rules 45 and 55 of the CGST Rules, for sending the goods to a job worker."* A Material Out is exactly that dispatch. **What now ships:** `DeliveryChallanPrintProjector.Project` refuses a Material Out with `JobWorkChallanNotBuilt` — *"A Material Out sends goods to a job worker under a job-work delivery challan (CGST Rules 45 and 55). This product does not print that challan yet, so prepare it outside the product."* — on the print-after-saving door, Ctrl+P and Share alike; it never says "no printed document". **🔴 OPEN GAP, NOT BUILT:** the Rule 45/55 job-work challan itself. The Delivery-Note challan was NOT reused in its place: its layout carries none of the Rule 55 particulars (consignor/consignee GSTIN, HSN, taxable value, tax rate and amount, place of supply), so handing it to a job worker would be a document the rule does not describe. Job Work In/Out **orders** and Material In are not the principal's dispatch documents and keep the generic notice. Grade consequence: **5.11(b) is PARTIAL** — the flag is read on these doors but Material Out does not print. Tests: `DeliveryChallanDocumentTests.A_material_out_refusal_names_the_job_work_challan_rather_than_denying_a_document` (Material Out + three controls), `PrintAfterSavingJobWorkDoorsTests.Material_out_reads_the_flag` (realised notice in the window). Mutation M1. |

### IV-239 · Alt+A from the Day Book offers INACTIVE voucher types and asks to activate — the prompt's shape and wording are ours

| | |
|---|---|
| **Severity** | **MEDIUM → closes census 5.6 clause (b).** Class B. Area: ENT. |
| **What TallyPrime does — attested** | `help.tallysolutions.com/accounting-entry-tally/`: *"In case the voucher is inactive, you will be prompted to activate the Memorandum voucher type"*; `…/tally-prime/accounting/accounting-faq/`: *"TallyPrime prompts you to activate the voucher types"*. (Both pages are quoted in census 5.6's own cell; this branch re-used them and did not re-derive them.) |
| **What we ship** | The Alt+A picker lists inactive types AFTER the active ones, each marked **"(Inactive)"**. Choosing one turns the picker column, in place, into a prompt — *"'X' is inactive. Activate it?"* — with **Yes - activate and enter** (activates through `VoucherTypeService.SetActive`, SAVES, rolls the flag back with a named notice if the save fails, then opens that type's entry exactly as an active row would) and **No** (restores the list; the type stays inactive). Esc closes the picker as before. An inactive type whose own screen is feature-gated — Job Work In/Out Order and Material In/Out while job-order processing is off, Payroll while payroll is off — is NOT offered, because activating it would open nothing. |
| **What is OURS** | The suffix, the prompt wording, the in-column Yes/No shape and the feature-gate exclusion. ⚠️ **A divergence in the key:** the accounting-entry page adds that you *"select Yes or press Y to proceed"* (re-opened by content 2026-10-26). Here **Yes is the default highlight, so Enter alone accepts**, but a bare **Y is not wired** to accept — in this data-driven column a bare letter type-ahead-filters rather than activating (the same column rule the Alt+A picker has always had). ⚠️ **Scope:** only the Day Book's Alt+A picker; the F10 / Other Vouchers routes the vendor pages describe still filter inactive types out and are untouched here. |
| **Tests** | `AddVoucherInactiveTypeTests` (list, Yes, No — real Alt+A / arrows / Enter, activation read off a reloaded book); `DayBookAddVoucherKeyboardTests.AltA_picker_lists_all_active_types_including_credit_and_debit_note` still pins the default book's list (no feature-gated inactive type leaks in). Mutations A1–A7, A6b. |
| **AMENDED 2026-10-27 (w8 fix, review ec LOW-1)** | *Yes - activate and enter* on a type whose identity means a dedicated screen (Job Work In/Out Order, Material In/Out, POS, Manufacturing Journal) now opens **that type**: `PickAddVoucherType` hands the chosen `VoucherType` to `OpenJobWorkOrder` / `OpenMaterialMovement` / `OpenPosBilling` / `OpenManufacturingJournal`, which use it when it is active and of the right base and otherwise resolve as before. Before, a second series (e.g. *Material Out - Site B*) was activated and then the seeded series opened, with Site B's numbering and flags ignored. Test: `AddVoucherInactiveTypeTests.Yes_on_an_inactive_second_material_out_series_opens_that_series`. Mutation L1. Only the Material Out door is pinned by a test; the other three take the same one-line hand-off. |

## 3. Grouped by area

> 🔴 **▶ 2026-10-13 (second entry, same day) — NOR ARE THE THREE v66 ENTRIES (`IV-82` … `IV-84`, §2F).** Where
> they would sit: **ENT** gains `IV-82` (the unwired deletion half of the insertion/deletion switch) and `IV-83`
> (the alteration registration lock); **ENT** also gains `IV-84` (F3 scoped to the accounting voucher screen).
> So the per-area counts below understate by **eleven** entries now, not eight. Stated rather than quietly
> patched, for the reason the note immediately below gives.
>
> 🔴 **▶ 2026-10-13 — THE EIGHT PHASE-0 ENTRIES (`IV-74` … `IV-81`, §2E) ARE **NOT** IN THE GROUPINGS BELOW,
> AND THE PER-AREA COUNTS IN THE HEADINGS ARE THEREFORE UNDERSTATED. STATED RATHER THAN QUIETLY PATCHED,
> because §7 of this document is *"Coverage, counted rather than claimed"* and an uncounted claim here is the
> exact defect it exists to catch.** Where they would sit: **MST** gains `IV-74` (unit composition lock),
> `IV-75` (base currency), `IV-76` (GST block reduction), `IV-77` (batch master) and `IV-80` (godown F12 gates)
> — i.e. **MST goes from 3 to 8**; **ENT** gains `IV-78` (Applicable Upto) and `IV-81` (the mis-labelled F10,
> which is a *retraction* rather than a divergence); **RPT** gains `IV-79` (Day Book Dr/Cr). ⚠️ **Read §2E
> directly until these tables are re-derived — and note that `IV-81` should be counted NOWHERE, because it
> records that we MATCH the vendor.**

### TAX — tax & money (7)
Wrong figures on documents the customer files, pays or is paid on.

| # | Sev | Item |
|---|---|---|
| IV-1 | CRITICAL | GST rate hierarchy runs backwards; the Group, Stock Group and Company levels now EXIST as masters (v51, `e49b88e`) but no resolver reads them — corrected 2026-08-16 |
| IV-2 | CRITICAL | §194Q TDS on the whole purchase value, not the excess over ₹50 lakh |
| IV-7 | CRITICAL | Interest "Always" accrues only on the opening balance |
| IV-8 | CRITICAL | Interest "Per" divisors annualised; Calendar Month multiplied by twelve |
| IV-14 | HIGH | CGST and SGST deliberately differ by one paisa |
| IV-15 | HIGH | e-invoice number guard not FY-scoped; FY restart deferred on a circular rationale |
| IV-22 | MEDIUM | Invoice round-off hardcoded to the rupee, and never switched on |

**Pattern:** six of the seven are places where **our own invariant beat a published rule** — most-granular-wins, a threshold as a pure gate, an annualised divisor, CGST+SGST == IGST, a rupee-only round-off. IV-15 is the exception: a guard stricter than the statute it enforces.

### VAL — valuation & inventory (5)

| # | Sev | Item |
|---|---|---|
| IV-6 | CRITICAL | "Last Sale Cost" values closing stock at selling price; four Tally methods missing |
| IV-9 | HIGH | Negative stock is an unrelaxable hard block where Tally warns |
| IV-10 | HIGH | Reorder Status never nets Sales Orders Due |
| IV-11 | HIGH | Rateless-inward fallback chain contaminates FIFO/LIFO with the running average |
| IV-33 | LOW | By-value additional-cost pool silently becomes a by-quantity spread |

**Pattern:** the corpus is **completely silent on costing** (0 hits for FIFO, LIFO, "costing method", "Average Cost" across all ten PDFs), so every valuation rule in the product was authored rather than cloned. That is the single largest unsourced surface in the codebase, and IV-6 and IV-11 are what came of it.

### ENT — entry & navigation (17)

| # | Sev | Item |
|---|---|---|
| IV-3 | CRITICAL | A saved voucher can never be altered |
| IV-4 | CRITICAL | Nothing can delete a voucher, ledger, group or company |
| IV-5 | CRITICAL | Ctrl+B posts real, irreversible receipt/payment vouchers |
| IV-13 | HIGH | No Voucher No. field; Manual and None unreachable |
| IV-16 | HIGH | Alt+X inverted |
| IV-17 | HIGH | 2-digit years resolve on the 1930–2029 pivot |
| IV-18 | HIGH | Alt+G Go To does not exist |
| IV-20 | MEDIUM | Entry mode: wrong default, never remembered |
| IV-24 | MEDIUM | `max+1` numbering: gaps, not date-ordered |
| IV-25 | MEDIUM | Three of five numbering methods |
| IV-27 | MEDIUM | No Accept? Yes/No on vouchers |
| IV-28 | MEDIUM | Ctrl+R, Alt+R, Ctrl+F squatted app-wide |
| IV-29 | MEDIUM | Gateway sections and vocabulary; no "Alter" row |
| IV-30 | MEDIUM | Bare-letter hotkeys auto-assigned by position |
| IV-31 | MEDIUM | Seven non-keys painted in the accelerator colour |
| IV-32 | LOW | Alt+2, Ctrl+U, Alt+U, Ctrl+N absent |
| IV-34 | LOW | Ctrl+F7 attributed without a locator |

**Pattern:** **five of the six voucher-lifecycle gestures TallyPrime defines are wrong or missing** — alter (IV-3), delete (IV-4), cancel (IV-16), number (IV-13), accept (IV-27); only save (Ctrl+A) is faithful. The engine implements four of them correctly and documents them by their Tally key; the UI calls none of them.

### MST — masters (3)

| # | Sev | Item |
|---|---|---|
| IV-12 | HIGH | A top-level (Primary) account group cannot be created |
| IV-21 | MEDIUM | No Alt+H Multi-Masters |
| IV-26 | MEDIUM | Predefined groups cannot be renamed |

**Pattern:** all three bite **at migration**, on day one, before the customer has posted a single voucher — together with D4 (no Opening Balance field) they make the first hour with the product the worst hour.

### RPT — reports (3)

| # | Sev | Item |
|---|---|---|
| IV-19 | HIGH | Drill-down stops at two screens |
| IV-23 | MEDIUM | Ageing buckets are ours; no age-by-bill-date |
| IV-35 | LOW | Uncited blank-at-zero across 100 call sites |

**This area is under-counted.** See §6 — report layouts, column sets and printing had almost no direct examination.

---

## 4. Ranked by customer impact — the top ten

The full ranking is the register order in §2. The ten to schedule first:

| Rank | # | Why it is here |
|---|---|---|
| 1 | **IV-1** | Wrong GST on every invoice for the normal Tally setup, in both directions, filed onward into GSTR-1 and 3B — and a hard block for anyone who sets rates on a Stock Group. |
| 2 | **IV-2** | 6× over-deduction of TDS. The supplier is short-paid and the 26Q cannot be reconciled to their 26AS. Our own TCS service already has the right rule. |
| 3 | **IV-3** | No voucher can ever be corrected. Every mistake — including every mistake the rows above cause — is permanent. |
| 4 | **IV-4** | Nothing can be deleted. Compounds IV-3; the operator's reflex produces silence, which reads as a broken product. |
| 5 | **IV-5** | A Tally reflex (Ctrl+B) silently posts irreversible full-value cash receipts against debtors. Given IV-3 and IV-4, unfixable by the customer. |
| 6 | **IV-6** | Closing stock at selling price: Balance Sheet, COGS and profit all wrong, and the error is in every prior period's accounts too. |
| 7 | **IV-7** | Interest under "Always" is silently not billed on anything invoiced inside the period, and the accrual is lost, not deferred. |
| 8 | **IV-8** | The same report's divisors are off by up to 12×, and February and January give different interest on identical facts. |
| 9 | **IV-9** | The commonest Indian trading sequence (sell now, book the purchase bill later) is impossible, and one such situation can make the whole company unpostable. |
| 10 | **IV-10** | Committed sales orders are invisible to reordering, so the buyer under-orders or does not order at all — a stockout the report exists to prevent. |

**Cheapest-first note for sequencing.** Four of the thirty-five are **wiring gaps, not design gaps** — the correct behaviour already exists in `Apex.Ledger` and is documented there by its Tally key, and no UI calls it: **IV-4** (`LedgerService.Delete`), **IV-16** (`LedgerService.Cancel`), **IV-22** (`applyInvoiceRoundOff`), **IV-25/IV-13** (`Manual`/`None` in `NumberingMethod`). They are disproportionately cheap for their rank.

---

## 5. Considered, and still right — do not re-open these

Recorded so that a future session does not "restore fidelity" by undoing a good decision, or re-audit something already settled.

> ### † 2026-08-15 — §5 SWEEP: no claim here is false on the merits, but TWO LOCATORS DO NOT RESOLVE
>
> **🔴 Two citations point at code that is not there and cannot be re-derived from the text — treat both claims
> as UNSUPPORTED until someone re-locates them:**
> - **§5.4 `InterestParameters.cs:225-231`** ("interest On-balance and Applicability semantics").
>   `src/Apex.Ledger/Domain/InterestParameters.cs` is **117 lines long**, so the range is past EOF.
>   `OnBalance`/`Applicability` are at `:37`, `:40`, `:62-63`, `:77-78`; the nearest plausible re-read,
>   `InterestCalculation.cs:225-231`, is the running-balance walk and is about something else.
>   **This is the weakest citation in the document.**
> - **§5.1 `MainWindowViewModel.cs:6314`** (the fourth of four F10 locators). `:6314` is
>   `case "Ledgers without PAN": OpenReport(ReportKind.LedgersWithoutPan); break;` — unrelated. There is **no**
>   `"F10"` hint string anywhere in that file; the only textual hits are the comments at `:1071-1072` and the
>   `"Other Vouchers"` rows at `:1012-1013`, `:1129-1130`. **The other three locators (`:1071`, `:1136`, `:3527`)
>   are exact, so §5.1's conclusion stands** — only the fourth pointer is bad.
>
> **🔴 One §5.4 parenthetical is now STALE:** the reorder entry says *"only the report **arithmetic** diverges,
> IV-10"*. **IV-10 is FIXED** (`7e0457b`); `ReorderStatus.cs:117-120` now nets Sales Orders Due. The master-model
> half remains faithful — which is what §5.4 was asserting — but the pointer to an open IV-10 is wrong.
> **Same entry, two further errors:** the file is at **`src/Apex.Ledger/Reports/ReorderStatus.cs`**, not
> `Services/`, and the cited `:114-154` now lands on the new Nett-Available arithmetic. The Item > Group >
> Category specificity resolver is at **`:141-146`**; Simple/Advance at **`:38-40`** and **`:171-174`**.
>
> **Locator drift, claims unaffected:** §5.2 `VoucherEntryViewModel.cs:2618` (`Accept()`) → **`:2766`** ·
> §5.3 `VoucherValidator.cs:313` ("within each cost category independently") → **`:339`**, and `:374-377`
> (the short-category machinery) → **`:378-387`** · §5.4 `LedgerMasterViewModel.cs:496` (party ledgers default to
> bill-by-bill) → **`:563`** · §5.4 `MainWindowViewModel.cs:5804-5842` (`IsAltering`) → **`:5846-5881`** ·
> §5.4 `MainWindow.axaml.cs:316-343` (Alt+C in both Tally meanings) → **`:357-369`** and **`:371-384`** ·
> §5.4 `InterestParameters.cs:82-104` → the range's tail is right but its head is not; the rounding doc is at
> `:88` and the Upward/Downward arms at `:103-104`.
> **Verified unchanged and exact:** `ApexDate.cs:11-13`, `:16-20`, `:25` · `docs/ca-audit-backlog.md:1472-1474` ·
> `docs/tally-gap-decisions.md:615` (X6) · `BillAllocation.EffectiveDueDate:68-69` · `TcsService.cs:139-146` ·
> `GstService.cs:326-338` · `PriceResolver.cs:36-54` · `AdditionalCostApportionment.cs:142-160`.
>
> **⚠️ Every "SHORTKEY item N" corroboration — §5.4's Ctrl+H item 65 included — must now be read under the
> outright rejection at `docs/w0-2-company-screen-grounding.md:52-76`.** Ctrl+H survives because TallyHelp's
> payments-and-receipts page carries it independently; nothing in §5 rests on SHORTKEY alone.

### 5.1 One item was reclassified: it is not a divergence at all

**F10 = Other Vouchers is TALLY-FAITHFUL.** `docs/tally-gap-decisions.md:615` (X6) instructs that F10 = Other Vouchers be recorded as an "accepted, documented divergence from TallyPrime". **It is not a divergence.** On the voucher-entry screen TallyPrime's F10 is exactly this: TallyHelp documents navigation as F10 > Accounting Vouchers, F10 > Inventory Vouchers, F10 > Order Vouchers, F10 > Payroll Vouchers — and locates Ctrl+F7 (Physical Stock) at "F10 > Inventory Vouchers". F10's *other* meaning, "to view the list of all vouchers or masters", belongs to the Gateway/report context. Our binding (`MainWindowViewModel.cs:1071`, `:1136`, `:3527`, `:6314`) is faithful.
**Action:** reword X6 to keep only the Miller-column navigation as a divergence, and record F10 as **faithful and cited** (`help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/`). **One genuine sub-gap remains and is small:** F10's Gateway-context meaning (list all vouchers/masters) is not implemented — an addition, not a conflict.
*Cost of leaving X6 as written: a faithful behaviour filed as a divergence invites a future session to remove it, and consumes review attention at every acceptance round — exactly what X6 was written to prevent.*

### 5.2 Deliberate deviations that are better than the reference

- **`dd-MMM-yyyy` instead of Tally's `dd-MMM-yy`.** `ApexDate.Canonical` (`src/Apex.Desktop/Services/ApexDate.cs:25`), documented at `:11-13` as "chosen over Tally's `dd-MMM-yy` to remove year ambiguity in an accounting app", with the trade-off put to the user explicitly at `docs/ca-audit-backlog.md:1472-1474`. The corpus does show Tally rendering 2-digit years (SG lines 5008, 5725; GSTN line 962), so the deviation is real — **and a 4-digit year is a strict improvement.** The day-first parsing decision beneath it (`:16-20`, no ambient-culture `MM/dd` fallback) is careful work with 12 tests behind it. **IV-17 is about the input pivot, not this display choice. Leave the display choice alone.**
- **Miller-column cascade navigation.** The largest thing in the app with no TallyPrime analogue (TallyPrime is single-window with a path line). It is a **standing user decision, approved 2026-07-03**, applying to all future features. Recorded here so the omission from the register is visibly deliberate, not an oversight.
- **FY-gated dual statute vocabulary.** `StatuteVocabulary.IsAct2025(fyStartYear) => fyStartYear >= 2026` is a deliberate deviation from a blanket rename — **a blanket rename would have falsified prior-year certificates.**
- **TallyPrime is the yardstick; Tally 7.2 is a checklist only** (decision D1). All ten licensed PDFs are TallyPrime documents, there is no 7.2 primary material in the corpus, and the 7.2 behaviours a user might miss (Ctrl+V/Alt+I mode keys, Credit Note on Ctrl+F8) are things TallyPrime **deliberately removed**.
- **The greyed Accept button.** `IsEnabled="{Binding CanAccept}"` on five entry surfaces has no TallyPrime analogue (Tally's Accept is a form field and cannot be greyed) — but **Ctrl+A is not gated by `CanAccept`**: it calls `VoucherEntryViewModel.Accept()` (`:2563`), which validates and narrates a specific message. The keyboard operator is never silently stuck, so this is cosmetic, not a dead end. **Deliberately not raised.**

### 5.3 Already found, fixed and logged — closed

- **Cost allocation across cost categories.** `VoucherValidator.cs:313` now validates "within each cost category independently" and `:374-377` names the short category. The corpus's own worked example (₹5,000 to Branch→Kolkata **and** Department→Marketing, SG pp.101-102) now posts, and GAP-2 (`aed9a50`) carries legacy-book rehydration. **Nothing left to do.**
- **The bill-reference default.** Commit `9608567` reversed an F12 default that "defaulted it to No with a comment asserting that matched TallyPrime — it was backwards", citing TallyPrime's Manage Outstanding Receivables and SG p.92. **The post-fix comments are the strongest in the codebase**: both sources cited, the previous wrong comment recorded so it cannot come back, and the Purchase-with-no-supplier-invoice-number fallback explicitly labelled "INFERENCE (not sourced)". **This is the model for how the rest of §2 should be closed.**
- **The AverageCost tautology** is already self-convicted in `memory.md` (2026-07-27) and the deferral was reversed on evidence. No second literal instance of the echo-oracle pattern was found; **IV-15 is its nearest relative** (a rationale resting on our own uniqueness key rather than on the IRP's) but is circular reasoning, a different mechanism.

### 5.4 Verified faithful this session — do not re-audit

**Masters and defaults.** Cost Category "Allocate Revenue Items = Yes / Non-Revenue = No" (SG p.100, BOOK p.99, GSTN line 2003) · Employee Category carrying the same two flags (BOOK p.323) · the **28 predefined groups** count (SG p.66, BOOK line 539) · "Method of voucher numbering = Automatic" on the seeded types (four corpus files) · party ledgers defaulting to bill-by-bill (`LedgerMasterViewModel.cs:496`; ten corpus walkthroughs) · master alteration reusing the creation form pre-filled with Ctrl+A running whichever verb it was opened for (`MainWindowViewModel.ActivateSelected`).

**Money rules.** Bill due-date derivation (`BillAllocation.EffectiveDueDate:68-69`; one narrow gap — Tally's credit period accepts Days/Weeks/Months, ours is an int of days) · interest rounding methods Downward/Normal/Upward (`InterestParameters.cs:82-104`, matching BOOK p.118 clause for clause) · interest On-balance and Applicability semantics (`:225-231`) · **TCS §206C(1H) chargeable base** (`TcsService.cs:139-146` — correct, and the contrast that convicts IV-2) · place-of-supply default for an unrecorded party state (`GstService.cs:326-338`, statutory §10(1)(b) proviso, and it **corrects** an earlier project ruling that had it backwards) · price-list slab resolution (`PriceResolver.cs:36-54`) · the reorder **master** model — Item > Group > Category specificity and Simple/Advance (`ReorderStatus.cs:114-154`, BOOK pp.158-162; only the report *arithmetic* diverges, IV-10) · additional-cost apportionment **weights** (`:142-160`) · FY boundary 1 April – 31 March · compensation-cess valuation modes.

**Shortcuts that are right, for the right reason.** Ctrl+A accept/save on ~40 screens · **Ctrl+H Change Mode** (SHORTKEY item 65 + TallyHelp) · Ctrl+T Post-Dated · Ctrl+L Optional · Alt+F1 detailed/condensed · Alt+F12 report filter · F12 report configuration · F11 Company Features · F4–F9 voucher keys with **F4 = Contra** · Alt+F5 Debit Note / Alt+F6 Credit Note · **Alt+C in both its Tally meanings**, correctly prioritised by focus context (`MainWindow.axaml.cs:316-343`) · Alt+N Auto Columns · **Alt+A "Add Voucher" on the Day Book** (BOOK p.431) · spacebar row select/deselect with arrow highlight (BOOK p.432).

---

## 6. UNVERIFIED — open questions, not findings

**Nothing in this section is claimed as a defect.** Each is a place where the Tally-side fact could not be settled from the licensed corpus or from official documentation. Several **block** a fix in §2 and are listed against it.

> **† 2026-08-15 — four of these now have a formal home, and one is being acted on.** The blocking measurements
> are scheduled as named test books in `docs/tallyprime-valuation-test-books.md` and listed under
> **"BLOCKED ON THE USER"** in `docs/NEXT_SESSION_KICKOFF.md:213-224`:
> **U-2 ⇒ T8** (₹44,000 at 10%, Per = 30-Day Month, 30-day window — ₹4,400 ⇒ per period, ~₹366 ⇒ per annum).
> **The `DaysInMonth × 12` arm is live in `c408037` DELIBERATELY, pending T8** — see the gate at
> `InterestCalculation.cs:157-183` and the † note on IV-8. **U-3 / rateless-inward and the valuation questions
> ⇒ T1/T2/T3/T4**, of which **T3 falsifies the whole Average Cost design** and **T4 is the question that stopped
> the negative-stock work eight times**. ⚠️ **T7 as written cannot discriminate its own model** — item-level
> stateless predicts ₹3,504.55, which is not among its two offered answers; a third option must be added before
> it is run. **U-11 (the 2-digit-year pivot), U-12 (the bare-letter hotkeys), U-5, U-6 and U-9 have NOT been
> scheduled** and remain exactly as written below.

| U | Question | Evidence today | What would settle it | Blocks |
|---|---|---|---|---|
| **U-1** | **What TallyPrime's own date format actually is.** `ApexDate.cs:12` cites "Tally's `dd-MMM-yy`" as the thing we deliberately deviate from (§5.2). | The corpus contains **no statement** of Tally's date format anywhere — only four rendered 2-digit dates (SG lines 5008, 5725; GSTN line 962), which is thin but consistent. | A TallyPrime date field or report header, or a help page stating the format. | nothing — the deviation is **right either way** (§5.2). Recorded only so nobody later reads it as an accident. |
| **U-2** | Does Tally's interest "Per" quote the rate **per period or per annum**? | BOOK p.117 defines each style by its day count and p.116's illustration uses 10% **per month**. Suggestive, not conclusive. TallyHelp's interest pages give no divisor. | One interest report in a real TallyPrime: ₹44,000 balance, Rate 10%, Per = 30-Day Month, 30-day window. ₹4,400 ⇒ per-period; ~₹366 ⇒ per-annum. | **IV-8** (the Calendar-Month × 12 half is wrong under **either** answer) |
| **U-3** | Does TallyPrime have **any fallback for a rateless inward**? | No help page, no corpus page. The published "At Zero Cost" method is suggestive, not proof. | A Stock Journal in real TallyPrime whose destination line carries no rate, on a FIFO item, read back through Stock Summary. | **IV-11** (the cross-method contamination half is proven without this) |
| **U-4** | Is TDS **caught up on prior below-threshold transactions** once a cumulative threshold is crossed? | TallyHelp's §194Q page shows the excess model; the general TDS page mentions a threshold-crossed notification and a 26Q "Under Exemption Limit" section, but neither states catch-up. | Three ₹20,000 §194J bills in real TallyPrime (₹50,000 threshold): does the third deduct on ₹20,000, ₹60,000 or ₹10,000? | scoping of **IV-2** |
| **U-5** | Tally's behaviour when a **by-value additional-cost basis is zero**. | No source found on either side. | A rateless-destination transfer with a by-value expense ledger, in real TallyPrime. | **IV-33** |
| **U-6** | Tally's exact **"Order to be Placed" formula when shortfall exceeds MOQ**. | Help states only the less-than case ("When the Shortfall is less than the Min Order Quantity, the Min Order Quantity appears"). Nett Available and Shortfall **are** documented. | The Reorder Status report in real TallyPrime with shortfall > MOQ. | the `max(shortfall, MOQ)` half of **IV-10** |
| **U-7** | **Manufacturing Journal cost absorption.** `ManufacturingJournalService` values the finished good at Σ component + Σ additional − Σ carve-outs (`:205-235`), additional cost never touching P&L (`:14-19`). | The corpus's manufacturing chapter is job-work only (BOOK lines 3039-3330) and states no formula; no help page found. | A real Manufacturing Journal with additional-cost ledgers and a by-product line, checked against Stock Summary **and** P&L. | — |
| **U-8** | **Forex revaluation rate and opening balances.** `ForexGainLoss.Revalue` (`:132-167`) uses the Standard rate; `ForexPosition` (`:97-98`) deliberately excludes openings, so a foreign-currency opening is never revalued. | Tally's currency master carries Standard, Selling and Buying rates; which revalues a debtor vs a creditor, and whether openings revalue, is not stated in the corpus and no help page was found. | An unadjusted-forex-gain/loss run in real TallyPrime with distinct Std/Selling/Buying rates and a forex opening. | — |
| **U-9** | Does Tally's **Manual voucher number accept alphanumerics**? | The numbering page says "users enter each voucher number individually" without stating the character set. Corpus support is indirect: SG's illustration carries "Voucher No. M/15/7" (exercise source-data, not a field instruction). | Set a voucher type to Manual, type "M/15/7", see whether it saves. | the int-vs-string half of **IV-13** (the read-only half needs nothing) |
| **U-10** | Does Tally's **Automatic numbering sequence by voucher date**? | The delete case is cited exactly. The **back-dated-insert** case is inferred from what "renumber" must mean to keep a contiguous series. | Enter three dated vouchers, then a fourth dated between the first two; read the numbers. | the date-ordering half of **IV-24** |
| **U-11** | **Tally's 2-digit-year pivot.** Also unresolved: its day-only/partial-date completion, and its rejection UX (block, beep or revert). | Genuinely not found in the corpus or the official pages; `docs/ca-audit-backlog.md:1281` reached the same conclusion independently. | Type "31/12/30" into a Tally date field and read the rendered year. | grounding for **IV-17** — but **IV-17 is a defect on any plausible pivot**; 1930 is not a date any user means |
| **U-12** | **The bare-letter menu hotkeys, entirely** — bare letter or Alt+letter; genuinely red or a theme accent; Tally's collision rule and whether its letters are stable. | The official page documenting only F/Alt/Ctrl is **silence, not a denial**. No claim is made that Tally lacks the feature. | Open a real TallyPrime, press a bare letter on a Gateway menu, photograph it. | the fidelity target of **IV-30** (whose order-dependence claim needs no Tally fact) |
| **U-13** | Does TallyPrime raise **"Quit? Yes or No" before discarding a part-keyed voucher on Escape**? We pop the column and discard silently (`MainWindow.axaml.cs:847-849`); the code comments there record **two separately measured work-loss incidents** where a single Escape destroyed a half-typed ledger. | The corpus never mentions it; help documents Esc only as "To go back to the previous screen" / "To remove inputs". | Open a real TallyPrime voucher, type into one field, press Esc. | not raised as a defect — deliberately, on an inference |
| **U-14** | **Ctrl+F1 as report configuration.** Named in a lens brief; no binding found in the help shortcut table or the corpus. F12 is TallyPrime's report-configuration key and we match it. | — | The shortcut table. | **premise unconfirmed** — nothing was raised from it |
| **U-15** | Where **"Track additional costs of purchase"** lives — voucher type or F11. Ours is a voucher-type flag (`VoucherType.TrackAdditionalCosts`). | Not verified either way. | The TallyPrime voucher-type master and F11. | — |
| **U-16** | The exact contents of TallyPrime's **"Statements of Accounts"** submenu. | The corpus proves "Display More Reports" is the single door, **not** the submenu contents. "Ratio Analysis" has **zero occurrences across all ten PDFs**, so the corpus cannot place it on the Gateway. | The Display More Reports menu in a live TallyPrime. | the weakest sub-claim of **IV-29** |
| **U-17** | ~~Whether TallyPrime's **Save View** matches ours in scope or gesture (`Screen.SaveView`, `Screen.SavedViews`).~~ 🔴 **CLOSED 2026-09-24 — THE GESTURE HALF IS ANSWERED AND THE ROW IS STRUCK RATHER THAN DELETED.** `help.tallysolutions.com/use-save-view-feature-in-tallyprime/`, opened **by content**, states every gesture: save is **`Ctrl+L` (Save View)**, recall is *"press **Ctrl+H** (Change View), and select the view under **Saved Views**"*, the same menu carries **Delete Saved Views** and **Show Original View**, and a delete is confirmed by *"Press **Enter** or **Y** to confirm deletion."* All five are now shipped and tested. **What remains open is the SCOPE half only**: which report configuration a vendor view actually captures, and whether a view can be set as a report's default (the vendor's *Set/Alter Default View* and *Show Original View* imply a default-view concept this build does not have). | ~~Corpus silent; not chased on the web.~~ **Chased on the web and answered for gestures.** The corpus is gone (ruling 14); the vendor page is now the ground truth. | A real TallyPrime: save a view, alter one F12 knob, recall it, and read which knobs came back. | the gesture half grounds the report-chord slice; the scope half is why census **11.16** is PARTIAL, and it is the premise of **IV-71** / **IV-72** |
| **U-18** | BOOK pp.431-432's shortcut table is **column-misaligned**; pairings were read by offset and cross-checked against the web page. Four survived both and are used (IV-32); several ambiguous rows were dropped. | — | A clean extraction or a page image. | confidence in **IV-32**'s citation, not its claim |
| **U-19** | Does a **zero *balance*** in a TallyPrime report column print blank or 0.00? The zero-valued **invoice line** is corpus-supported; the zero balance is a different claim. | — | A Trial Balance or Ledger page showing a nil-balance row, or the report-configuration page on suppressing zero balances (if Tally has a "Show zero balance" toggle, the blank is **configurable**, not intrinsic). | **IV-35** |
| **U-20** | Uncited "Tally-faithful" comments too small to rank, listed so they are not lost: `PayHeadMasterViewModel.cs:596` (attendance/production link a conscious choice) · `LedgerMasterViewModel.cs:353` ("Tally's mailing block does not sit behind an F11 flag" — note the corpus says the **opposite** about a neighbouring field: BOOK lines 3952/4305, "If Maintain Bill by Bill Option not show in ledger screen then press **F12** & Enable") · `VoucherPrintProjector.cs:776` + `LedgerMasterViewModel.cs:358` ("Mailing Name (auto, editable)" — confirmed for the **Company** master at SG p.62, **not** for the **ledger** master where the code applies it) · `MainWindow.axaml:2891-2895` (the batch knob "On by default", justified by making a corpus walkthrough work, which is not evidence of Tally's shipped state) · `JobWorkService.cs:39` (Material In/Out flags "matching TallyHelp's semantics", cited by name only, no URL; job work is outside the corpus entirely) · `StockValuationMethod.cs:7` (a new item defaults to Average Cost — correctly attributed to **DP-1, user-approved**, so not a false claim, but the corpus has zero occurrences of "costing method"). | — | Per-item, as noted. | — |

---

## 7. Coverage — read this before treating any count as a count

**This register is a floor, not a census.** The previous register in this project (`docs/tally-fidelity-defects.md` §3) said the same thing of itself, and saying so is what made it useful. Thirty-five is the number of items **four lenses found in the surfaces they looked at**, not the number that exists.

> **† 2026-08-15 — §7's unmeasured list is now PARTLY CLOSED, by a document written after this one.**
> `docs/full-clone-census.md` (2026-08-10) took a denominator over the whole product — **the split is in census
> §1.2, summed from the named list in §1.2a, and is deliberately not restated here** — and its §6 item 10
> states which of §7's rows it closed:
> **printing and print layouts (structurally, not fidelity-wise), report layouts (existence only), company
> creation and F11/F12, backup/restore, import/export, POS, banking, security.** **Still genuinely unmeasured:
> GST return *content*, payroll *entry-surface* fidelity, budgets, scenarios, forex, manufacturing, job work,
> multi-currency.** The census's own headline is the one to carry forward, and it is harsher than §7's:
> **only the capabilities enumerated in census §1.3 have ever had their behaviour compared to a source; the
> rest have not** — a `PRESENT`
> row means *reachable*, not *correct*. **⚠️ 2026-08-17 — this note was written reading *"only 8 of 115 …
> ~107 have not"*, and those figures have since moved. The digits are removed rather than re-copied: census
> §1.3 is the single derivation, it carries an as-of date, and this branch has three times corrected that
> figure and left live copies behind.**
> **⚠️ 2026-08-18 — THE DENOMINATOR ITSELF THEN MOVED, and the capability split above went the same way for the
> same reason.** This note read *"~115 capabilities: 42 complete, 44 partial, 21 absent, 8 undetermined"*: the
> **2026-08-10 snapshot at HEAD `468a96e`**, which census §1.2 now retains in place under a *(superseded)*
> heading because notes like this one still quote it. **The new figure is not a typo for the old one** —
> census §1.2b keeps the three causes apart: mostly **granularity** (capabilities written out at §1.1 rule 1's
> menu-row/F11-toggle granularity for the first time, with report families **still compressed**, so this is not
> "expand everything"), an **absent column that was provably too small** (zero absent allowed in four whole
> areas against evidenced zero-hit searches), and least of all **work shipped since**. As above, the digits are
> not re-copied here: **§1.2a is the list, §1.2 is its column sum, §1.3's anchor block holds the fidelity
> figures, and a copy that disagrees with them is the defect.** **Method limit 1 below ("no build and no test was run") still applies to
> this register and applied again to the 2026-08-15 re-verification: every claim here is read from source.**

### What was actually examined

- **Every occurrence of "Tally"/"TallyPrime" in `src/`** — 141 hits across 67 files, plus the `.axaml` comments — and all 241 comment lines asserting a default. This is the one dimension with genuinely complete coverage.
- **The money engines, read line by line:** `GstService`, `TdsService`, `TcsService`, `StockValuationService`, `AdditionalCostApportionment`, `InterestCalculation`, `Outstandings`, `ReorderStatus`, `PriceResolver`, `BillSettlementService`.
- **The voucher lifecycle:** `LedgerService`, `InventoryPostingService`, `VoucherValidator`, `VoucherNumberFormatter`, `EInvoiceService`, `Company` (e-invoice records).
- **The whole ~700-line key dispatcher** in `MainWindow.axaml.cs`, the button-bar and status-bar templates, `MainWindowViewModel`'s menu construction, and the master-alteration machinery.
- **Masters:** `GroupService`, `MasterAlterationRules`, `LedgerMasterViewModel`, and the sibling master ViewModels' Under pickers.
- **Dates:** `ApexDate` and its ladder, plus `BatchMasterViewModel`'s consumption of it.
- **Project record:** `plan.md`, `memory.md`, `docs/tally-fidelity-defects.md` (all 19 rows), `docs/tally-gap-decisions.md`, `docs/voucher-entry-specification.md`, `docs/ca-audit-backlog.md`, `docs/phase3-inventory-requirements.md`.

### What had little or NO direct examination

Treat every one of these as **unmeasured**, not as clean:

| Surface | Coverage |
|---|---|
| **Printing and print layouts** | Touched only where `VoucherPrintProjector` consumed something else (IV-22, IV-35). **No print layout, no column set, no page furniture was compared to Tally.** |
| **Report layouts generally** | Only Outstandings, Reorder Status and Interest were read for *arithmetic*. Column sets, ordering, totals rows, condensed/detailed behaviour and F12 report options across ~50 report screens: **unexamined**. IV-19 was found by reading the *drill dispatcher*, not the reports. |
| **GST returns** | GSTR-1/3B/4/9/9C, CMP-08, QRMP, ITC set-off, GSTR-2B reconciliation, e-Way bill: **content correctness never examined.** Only their *absence from the drill contract* (IV-19) is recorded. IV-1 and IV-14 reach these returns, so the returns are known to be affected — but not audited. |
| **Payroll** | Pay-head taxonomy, PF/ESI/PT/gratuity/bonus computation, attendance, payslips: **essentially untouched.** One uncited comment (U-20) is the entire payroll finding. Note the standing open user decision on the **unverified 4% cess** for TY2026-27 — a live payroll deduction — which is outside this register and still unresolved. |
| **Company creation, F11/F12 company features** | **Company creation and alteration WERE examined against the corpus on 2026-08-16/17** — see `docs/full-clone-census.md` §1.3 row 9, which is PARTIAL and enumerates its unsourced half, and `docs/w0-2-company-screen-grounding.md` §9 items 12-23. **F11/F12 company features remain not examined**, and F11 still appears here only as a Gateway-row placement question (IV-29) — plus one recorded departure: the corpus and the feature catalog both say a save hands off to the Company Features screen, and we go to the Gateway (grounding §9 item 21). |
| **Backup / restore, import / export, data migration** | **Not examined.** |
| **POS, budgets, scenarios, banking/BRS, forex, manufacturing journal, job work** | Read only far enough to record U-7, U-8 and U-15 as open questions. **No fidelity comparison was made.** |
| **Security, users, audit trail, multi-user** | ~~**Not examined at all.**~~ 🔴 **PARTLY SUPERSEDED 2026-09-07.** **Users and security levels (census 16.2) WERE examined** against `help.tallysolutions.com/manage-users-in-tallyprime/` and the data-security FAQ, and shipped — see **IV-65** for the two divergences and the irrecoverable-lockout consequence. **Company data encryption (16.1), Change User, and multi-user remain not examined**; the audit trail (16.4) is PARTIAL with its "who" still open. |
| **Multi-currency beyond `ForexGainLoss`** | **Not examined.** |

### Method limits that apply to every row above

1. **No build and no test was run** (a gate was live in sibling worktrees). **Every code claim is read from source, not executed.** The `TwoDigitYearMax` consequence in IV-17 in particular is derived from documented framework behaviour plus the ladder contents; a single xunit theory row would confirm it in seconds.
2. **No live TallyPrime was observed.** Every Tally-side fact is from the licensed corpus or from `help.tallysolutions.com`/GST-portal pages. **Twenty open questions in §6 need a real TallyPrime**, and four of them block a fix.
3. **The corpus cannot settle whole domains.** Verified negative results: **0 hits** across all ten PDFs for FIFO, LIFO, "costing method", "Average Cost", "Last Purchase", "Market Valuation", "ageing"/"aging" as a receivables report, "negative stock"/"allow negative", "restart numbering"/"starting number", "Ratio Analysis", and any report-side bill-settlement gesture. **The corpus is exam material.** Where it is silent, the only sources are official pages — and five of them (the stock-valuation, GST-hierarchy, §194Q, round-off and reorder pages) are load-bearing for six CRITICAL/HIGH rows.
4. **`SHORTKEY` (`659947760-Tally-Prime-Short-Key.pdf`) is machine-untrustworthy** — its voucher-key block is shifted by one and it contains plainly wrong rows ("F11 Switch Company", "Ctrl+A Zoom"). It is used only as corroboration; **nothing in this register rests on it alone.**
5. **`C:\Users\dkpho\Downloads\Tally7.2` was not opened, listed or launched** — and per decision D1 it would not be evidence if it had been.

### What that means for scheduling

The **eleven Class-A rows are the ones that are certainly there and certainly wrong.** The unmeasured surfaces above are the ones most likely to hold the *next* eleven — and printing, GST returns and payroll are the three that reach the customer's counterparties, their tax filings and their staff's pay respectively. **A sweep of those three, with the same four lenses, is the obvious next piece of work.**

---

*No code was modified, no build was run, no test was run and no git operation was performed in producing this
register. The four hunt outputs it merges are in the session scratchpad.*

*† **Re-verified 2026-08-15** against HEAD `c56e5c3`. All 35 rows plus §5 were re-opened file-by-file; four rows
(IV-5, IV-7, IV-9, IV-10) were found FIXED IN CODE, three (IV-4, IV-8, IV-20) half-fixed, and ~30 carried drifted
citations, all corrected in place and marked **†**. The corpus was re-extracted with `pdftotext -layout` for the
IV-1 sourcing correction only. **Again: no code was modified, no build was run, no test was run and no git
operation was performed.** Corrections owed to `plan.md` and `memory.md` were collected and handed back rather
than applied — those two files were owned by other work at the time.*
