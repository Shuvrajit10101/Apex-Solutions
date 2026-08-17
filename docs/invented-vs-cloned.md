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
| **IV-1** | CRITICAL | A | TAX | GST rate hierarchy runs backwards; the missing levels now exist as masters, nothing reads them (corrected 2026-08-16) | `GstService.cs:396` |
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
| **IV-13** | HIGH | B | ENT | No Voucher No. field; Manual and None are unreachable | **†** `VoucherEntryViewModel.cs:984` *(was `:935`)* |
| **IV-14** | HIGH | A | TAX | Intra-state CGST and SGST deliberately differ by one paisa | `GstService.cs:526` |
| **IV-15** | HIGH | B | TAX | e-invoice number guard is not FY-scoped; FY restart deferred on a circular rationale | **†** `Company.cs:681-682` *(was `:660`)* |
| **IV-16** | HIGH | C | ENT | Alt+X means the opposite of Tally's Alt+X | `MainWindowViewModel.cs:4831` |
| **IV-17** | HIGH | A | ENT | 2-digit years resolve on .NET's 1930–2029 pivot | `ApexDate.cs:36` |
| **IV-18** | HIGH | B | ENT | Alt+G "Go To" does not exist | `MainWindow.axaml.cs:182` |
| **IV-19** | HIGH | B | RPT | Drill-down stops at two screens; **† 71 of 77 report surfaces** are dead ends *(the "~50" below is the census-corrected figure)* | `MainWindowViewModel.cs:2083` |
| **IV-20** | MEDIUM | C | ENT | Entry mode: ~~wrong default~~ **† (a) FIXED `f277318`**, and (b) never remembered — **still open** | **†** `VoucherEntryViewModel.cs:100` + `:141-144` *(was `:91`)* |
| **IV-21** | MEDIUM | B | MST | No Alt+H Multi-Masters — one form per ledger | `MainWindowViewModel.cs:1180` |
| **IV-22** | MEDIUM | A | TAX | Invoice round-off hardcoded to the rupee, and never switched on | `GstService.cs:729` |
| **IV-23** | MEDIUM | C | RPT | Ageing buckets are ours; no age-by-bill-date | `Outstandings.cs:85` |
| **IV-24** | MEDIUM | C | ENT | Automatic numbering is `max+1`: gaps, and not date-ordered | **†** `LedgerService.cs:154` *(was `:171`, past EOF — the file is 162 lines)* |
| **IV-25** | MEDIUM | B | ENT | Three of TallyPrime's five numbering methods | **†** `NumberingMethod.cs:9` *(was `:8`)* |
| **IV-26** | MEDIUM | C | MST | Predefined groups cannot be renamed | `MasterAlterationRules.cs:211` |
| **IV-27** | MEDIUM | C | ENT | "Accept? Yes/No" exists on masters only; vouchers save silently | **†** `MainWindowViewModel.cs:4882` *(was `:4873`)* |
| **IV-28** | MEDIUM | C | ENT | Three TallyPrime report keys squatted app-wide | **†** `MainWindow.axaml.cs:331` *(was `:290`)* |
| **IV-29** | MEDIUM | C | ENT | Gateway sections and vocabulary are ours; **"Alter Company" landed under Masters 2026-08-17 — partially fixed** | **†** `MainWindowViewModel.cs:1047` *(was `:912`)* |
| **IV-30** | MEDIUM | C | ENT | Bare-letter menu hotkeys auto-assigned by row position | `GatewayColumn.cs:381` |
| **IV-31** | MEDIUM | C | ENT | The button bar paints seven non-keys in the accelerator colour | **†** `MainWindowViewModel.cs:6742` *(was `:6756`)* |
| **IV-32** | LOW | C | ENT | Report-line gestures absent: Alt+2, Ctrl+U, Alt+U, Ctrl+N | **†** `MainWindow.axaml.cs:182` — a pointer into the dispatcher, not a positive citation *(was `:875`)* |
| **IV-33** | LOW | A | VAL | A by-value additional-cost pool silently becomes a by-quantity spread | `AdditionalCostApportionment.cs:217` |
| **IV-34** | LOW | C | ENT | Ctrl+F7 = Physical Stock is attributed to a source with no locator | `SeedVoucherTypes.cs:33` |
| **IV-35** | LOW | C | RPT | "Tally-faithful blank-at-zero" is uncited and governs 100 call sites | **†** `IndianFormat.cs:30` *(was `:37`)* |

---

### IV-1 · GST rate resolution runs TallyPrime's hierarchy backwards; the missing levels now EXIST as masters and nothing READS them
**CRITICAL** · Class **A** · Area **TAX** · relates to `tally-fidelity-defects.md` D8

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
| **Fix** | Make the voucher drill open the matching entry view model pre-filled in an alteration mode, re-using the master pattern already proven at `MainWindowViewModel.cs:5804-5842` (one screen serves Create and Alter; Ctrl+A runs whichever verb it was opened for). Re-post through `LedgerService` so the voucher is **replaced** rather than a second one added, keeping its number. |

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
> (`docs/NEXT_SESSION_KICKOFF.md:154-156`; §6 U-2 below). **(3) One sub-claim is now FALSE:** *"the code contradicts
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
> `docs/NEXT_SESSION_KICKOFF.md:113-146`: eight reverted attempts, and the fix is now believed to be *deleting*
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
> voucher-number `TextBox` exists anywhere. **Corrected:** `VoucherEntryViewModel.cs:935` → **`:984`** ·
> `MainWindow.axaml:3879` → **`:3882`** · `:4104` → **`:4107`** · `LedgerService.cs:48` → **`:51-52`** ·
> `InventoryPostingService.cs:53` → **`:91-92`** · `plan.md:611-613` → **`plan.md:677-679`** ·
> `docs/tally-gap-decisions.md:563` → D23's heading is now **`:560`**.

| | |
|---|---|
| **What the customer experiences** | An operator migrating a live book **cannot key the invoice number their existing series uses**. They cannot enter the pre-printed number on a manual bill book, cannot correct a wrong number, and cannot override one voucher without changing the whole type. A firm whose numbers look like "APX/25-26/0147" can only approximate it with a type-wide prefix, which applies to every voucher of that type, cannot vary per voucher, and still cannot express a non-numeric core. On the fidelity target this is **one field and one keystroke**. |
| **What we invented** | Three choices compound. **(1)** `Voucher.Number` is an `int` (`src/Apex.Ledger/Domain/Voucher.cs:20`) and the human number is an unpersisted projection (`VoucherNumberFormatter.Render`, `src/Apex.Ledger/Services/VoucherNumberFormatter.cs:22`). **(2)** The entry screen exposes it as a **read-only preview**: `[ObservableProperty] private int _voucherNumber` (`src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:935`), bound in a non-editable `<Run Text="{Binding FormattedVoucherNumber}"/>` (`MainWindow.axaml:2056`, `:3544`, `:3879`, `:4104`). **There is no TextBox for a voucher number anywhere.** **(3)** The method cannot be changed: `MethodDisplay` is a string getter marked "DISPLAY-ONLY this slice … editing it is deferred (S5+)" (`VoucherNumberingConfigViewModel.cs:114-115`), no code in `src/Apex.Desktop/` assigns `Numbering`, and there is **no Voucher Type master at all** (D9). The engines branch only on `Automatic` (`LedgerService.cs:48`, `InventoryPostingService.cs:53`), so `Manual` and `None` exist in the enum and the schema but are unreachable. **[code]** |
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
| **What we invented** | **(a)** `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:91` — "the classic Dr/Cr grid (…AsVoucher, **the default** and the only mode on every non-Purchase/Sales type)", enforced at `:96` by the unconditional literal `private VoucherEntryMode _mode = VoucherEntryMode.AsVoucher;`. Repeated uncited at `AccountingInvoiceLineViewModel.cs:15` and `:23`. Single Entry exists and works (`:1176` `CanBeSingleEntry`, `:1184` `IsSingleEntry`) but is reachable only by Ctrl+H. **(b)** `:93-94` — "the mode is transient screen state, **never persisted**", repeated at `AccountingInvoiceLineViewModel.cs:12-13`. `_mode` is an instance field on a per-voucher ViewModel with **no company/voucher-type backing store**. Neither claim carries a Tally citation. **[code]** |
| **What Tally does** | Records Payment/Receipt/Contra in **Single Entry** mode (one Account field plus a Particulars list, no Dr/Cr labels) unless the operator turns it off; double-entry is the opt-in. And it **remembers**: "when you switch a voucher to double-entry mode, this setting will be saved, and all future vouchers will open in double-entry mode by default". Ctrl+H is a preference change, not a per-voucher toggle. |
| **Citation** | **[corpus]** GSTN extracted lines 330/334 — the exercises instruct the reader to set "Use single entry mode for payment/receipt/contra vouchers? **NO**", which is only meaningful if the shipped state is Yes; BOOK pp.26-27, 29, 31-32 ("In Single Entry Mode Dr & Cr not Show"); SG p.76. **[web]** `help.tallysolutions.com/payments-and-receipts-tally/` — single entry as the mode for users not versed in Dr/Cr, plus the saved-setting sentence. |
| **How it got in** | Never questioned. "The default" was asserted in the enum's own XML doc and in the VM's doc, with a Tally reference attached to the **mechanism** (Ctrl+H Change Mode) but not to the **default** — which reads as sourced on a skim. On persistence, two different concerns were conflated: not persisting the mode *on the voucher* is correct (it is inferable from the posted legs at print/GSTR-1 time); not persisting it *as a preference* is the divergence. |
| **Fix** | Seed the mode per type instead of a single literal: `SingleEntry` when `CanBeSingleEntry` (Contra/Payment/Receipt), `AsVoucher` otherwise — `ShowPlainDrCrGrid` (`:132`) already stops the two grids co-rendering. Keep the mode off the posted voucher, but **persist the last-used mode as a per-company, per-base-type preference** and seed `_mode` from it. Reword `:91` and `:93-94` (and `AccountingInvoiceLineViewModel.cs:12-13,15,23`) with the GSTN and TallyHelp citations, distinguishing "never persisted on the voucher" from "remembered as a preference, as TallyPrime does". |

---

### IV-21 · No Alt+H Multi-Masters — every ledger, group and stock item is created one form at a time
**MEDIUM** · Class **B** · Area **MST** · relates to `tally-fidelity-defects.md` D4

> **† 2026-08-15 — verdict STANDS; but the D4 cross-reference is now STALE.** "Alt+H", "MultiCreate",
> "Multi Create" and "Multi-Master" still return zero source hits, and `MainWindowViewModel.cs:1180` still emits
> one `Page` row per master. **However `tally-fidelity-defects.md` D4 (no Opening Balance field) was FIXED by
> `c8b44cf`** — `LedgerMasterViewModel.cs:1053-1054` now writes `OpeningBalance`/`OpeningIsDebit` from a real
> form control (`MainWindow.axaml:4356-4387`). **So the closing sentence "together with D4 … the initial setup is
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
> `VoucherPrintProjector.cs:606-607` now states the defect outright: *"Zero for every voucher this app posts:
> nothing in `src/` ever posts a Round-Off leg."* Corroborated by `docs/full-clone-census.md:139`
> (*"No path posts a Round-Off leg at all"*). **Corrected:** `VoucherEntryViewModel.cs:3450` → **`:3664`** ·
> `:3638` → **`:3852`** · `:4070` → **`:4284`** · `VoucherPrintProjector.cs:331` (FIX-F10) → **`:338`**
> (also `:357`, `:567`, `:601`) · **`VoucherPrintProjector.cs:341` is no longer a call site at all** — the print
> path was moved onto the posted legs, so it no longer calls `ComputeInvoiceTax`. `PosBillingViewModel.cs:400`
> and `CreditDebitNoteService.cs:129` are unchanged and still omit the argument.

| | |
|---|---|
| **What the customer experiences** | A retailer who rounds every bill to the nearest ₹10 **cannot**. Taxable ₹1,235.60 + 18% = ₹1,458.01: TallyPrime with an Invoice Rounding ledger at Downward Rounding / limit 10 prints ₹1,450 and books ₹8.01 to Round Off; we print ₹1,458.01. Worse, **since no screen ever passes the flag we do not round at all** — a "Round Off" ledger appears in the customer's chart of accounts the day GST is enabled and never receives a single posting, which reads as a broken feature. |
| **What we invented** | `src/Apex.Ledger/Services/GstService.cs:729` `var rounded = Math.Round(grand, 0, MidpointRounding.AwayFromZero);` — Normal rounding, limit 1, not configurable. The "Round Off" ledger is auto-created under Indirect Expenses with **no ledger type and no rounding settings** (`:226-233`, name constant at `:61`). `applyInvoiceRoundOff` defaults to `false` (`:610`) with **no production call site passing `true`** — `VoucherPrintProjector.cs:341`, `PosBillingViewModel.cs:400`, `VoucherEntryViewModel.cs:3450`, `:3638`, `:4070` and `CreditDebitNoteService.cs:129` all omit the argument; `VoucherPrintProjector.cs:331` already records this as **FIX-F10**. **[code]** |
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

| | |
|---|---|
| **What the customer experiences** | The sales register develops **holes the operator cannot close**, and a GST officer or auditor reading a series that jumps 44, 45, 47 asks where invoice 46 went — every time, for the life of the book. Separately, an accountant who keys Friday's invoices on Monday and then a back-dated Thursday one gets **invoice 51 dated before invoice 50**; the sales register sorted by number is not in date order, which is exactly what a serial number is for. |
| **What we invented** | `LedgerService.NextNumber` (`src/Apex.Ledger/Services/LedgerService.cs:153-160`) — and its twin `InventoryPostingService.NextNumber` (`src/Apex.Ledger/Services/InventoryPostingService.cs:148-155`) — returns the maximum existing `Number` for the type plus one, scanning all vouchers with **no date scoping and no renumbering**. `plan.md:624` records the consequence as settled and unremarkable: "the Renumber / Retain delete-behaviour toggle (today's only behaviour — Cancel keeps the number, Delete leaves a gap — is retained)", **without noting that gap-on-delete is Tally's non-default choice**. **[code]** |
| **What Tally does** | Under Automatic, offers two sub-options and **defaults to Renumber Vouchers**: deleting voucher 2 from the sequence 1,2,3 results in 1,2. "Retain Original Voucher No." — which preserves the gap, leaving 1,3 — is the **opt-in** alternative. The same pair is offered under Multi-User Auto. |
| **Citation** | **[web]** `help.tallysolutions.com/use-voucher-numbering-methods/` — "Renumber Vouchers (default) … results in 1,2"; "Retain Original Voucher No. (preserves gaps) … leaves 1,3". The **date-ordering half is inferred** from the renumbering semantics, not separately quoted (§6 U-10). |
| **How it got in** | An orchestrator/plan ruling at `plan.md:624` that framed the existing implementation as a **deferral of a toggle** rather than as a choice between two Tally behaviours. Nobody recorded which side Tally defaults to, so the app's incidental behaviour became the shipped policy by omission. |
| **Fix** | Add the Renumber / Retain Original toggle to the voucher type with **Renumber as the default**, and make Automatic sequence by (voucher date, entry order) rather than by `max`. **Both engines must change together** — `LedgerService.cs:153-160` and `InventoryPostingService.cs:148-155` are duplicate implementations and have already been a source of drift. Lock it with a test that deletes a middle voucher and asserts contiguity, and one that back-dates an insert and asserts the numbers follow the dates. |

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
> the status-bar overlay `MainWindow.axaml:16949-16953` → **`:16987`/`:16991`** ·
> `docs/voucher-entry-specification.md:96` → **`:93`**.

| | |
|---|---|
| **What the customer experiences** | The one screen where a Tally operator expects a last look before committing — **the voucher, which moves money** — commits silently on Enter, while the ledger master they barely care about stops and asks. The asymmetry teaches them to distrust Enter everywhere, which is corrosive in a product whose entire entry model is Enter-through-the-fields. |
| **What we invented** | `IsMasterAcceptScreen` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:4873-4885`) enumerates ~24 **master** screens; no voucher-entry screen is in the list. The WI-11 confirmation arm (`MainWindow.axaml.cs:808-811`) never fires on a voucher and Enter falls through to `ActivateSelected()` (`:815-818`), which saves outright. The prompt itself is also a letter-key question — "Accept Ledger? (Y/N)" (`:4899`) — painted over the status bar (`MainWindow.axaml:16949-16953`), not a Yes/No field on the form. **[code]** |
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
> `:6753-6755` → **`:6799-6801`** · the red-bold template `MainWindow.axaml:16914-16917` → **`:16953-16956`** ·
> `plan.md` NFR-2 `:94-95` → **`plan.md:115`**. **Uncredited improvement:** `f2abdbb` removed an **eighth**
> offender — `:6796-6798` now reads *"there is deliberately NO 'Ctrl+B' row here … leaving the badge would paint a
> red accelerator for a key that fires nothing, which is register defect IV-31."* So "seven" is correct at HEAD,
> but it was eight when this row was written.

| | |
|---|---|
| **What the customer experiences** | A keyboard-only operator — which is what a Tally operator is — reads **seven red badges as keys, presses them, and nothing happens**. Those seven features (Scenarios, Outstandings, Bank Recon, Import Statement, Interest, Stock Summary, SMTP) are **mouse-only** in a product whose whole premise is that the mouse is optional. |
| **What we invented** | `BuildButtonBar` (`src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:6750-6772`) adds bar items whose `Key` is an invented badge rather than a keystroke — "Scn", "Outs", "BRS", "Imp", "Int", "SS", "SMTP" — and the template renders `Key` in bold AlertRed exactly like a real accelerator (`MainWindow.axaml:16914-16917`). The comment at `:6753-6755` says so outright: "Outs" is used "so the Outstandings quick-button uses a non-key mnemonic badge and is reached by click, never by a colliding 'O' keystroke". **[code]** |
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

## 3. Grouped by area

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
> **Locator drift, claims unaffected:** §5.2 `VoucherEntryViewModel.cs:2563` (`Accept()`) → **`:2766`** ·
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

**Masters and defaults.** Cost Category "Allocate Revenue Items = Yes / Non-Revenue = No" (SG p.100, BOOK p.99, GSTN line 2003) · Employee Category carrying the same two flags (BOOK p.323) · the **28 predefined groups** count (SG p.66, BOOK line 539) · "Method of voucher numbering = Automatic" on the seeded types (four corpus files) · party ledgers defaulting to bill-by-bill (`LedgerMasterViewModel.cs:496`; ten corpus walkthroughs) · master alteration reusing the creation form pre-filled with Ctrl+A running whichever verb it was opened for (`MainWindowViewModel.cs:5804-5842`).

**Money rules.** Bill due-date derivation (`BillAllocation.EffectiveDueDate:68-69`; one narrow gap — Tally's credit period accepts Days/Weeks/Months, ours is an int of days) · interest rounding methods Downward/Normal/Upward (`InterestParameters.cs:82-104`, matching BOOK p.118 clause for clause) · interest On-balance and Applicability semantics (`:225-231`) · **TCS §206C(1H) chargeable base** (`TcsService.cs:139-146` — correct, and the contrast that convicts IV-2) · place-of-supply default for an unrecorded party state (`GstService.cs:326-338`, statutory §10(1)(b) proviso, and it **corrects** an earlier project ruling that had it backwards) · price-list slab resolution (`PriceResolver.cs:36-54`) · the reorder **master** model — Item > Group > Category specificity and Simple/Advance (`ReorderStatus.cs:114-154`, BOOK pp.158-162; only the report *arithmetic* diverges, IV-10) · additional-cost apportionment **weights** (`:142-160`) · FY boundary 1 April – 31 March · compensation-cess valuation modes.

**Shortcuts that are right, for the right reason.** Ctrl+A accept/save on ~40 screens · **Ctrl+H Change Mode** (SHORTKEY item 65 + TallyHelp) · Ctrl+T Post-Dated · Ctrl+L Optional · Alt+F1 detailed/condensed · Alt+F12 report filter · F12 report configuration · F11 Company Features · F4–F9 voucher keys with **F4 = Contra** · Alt+F5 Debit Note / Alt+F6 Credit Note · **Alt+C in both its Tally meanings**, correctly prioritised by focus context (`MainWindow.axaml.cs:316-343`) · Alt+N Auto Columns · **Alt+A "Add Voucher" on the Day Book** (BOOK p.431) · spacebar row select/deselect with arrow highlight (BOOK p.432).

---

## 6. UNVERIFIED — open questions, not findings

**Nothing in this section is claimed as a defect.** Each is a place where the Tally-side fact could not be settled from the licensed corpus or from official documentation. Several **block** a fix in §2 and are listed against it.

> **† 2026-08-15 — four of these now have a formal home, and one is being acted on.** The blocking measurements
> are scheduled as named test books in `docs/tallyprime-valuation-test-books.md` and listed under
> **"BLOCKED ON USER MEASUREMENT"** in `docs/NEXT_SESSION_KICKOFF.md:148-161`:
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
| **U-17** | Whether TallyPrime's **Save View** matches ours in scope or gesture (`Screen.SaveView`, `Screen.SavedViews`). | Corpus silent; not chased on the web. | — | **not raised in either direction** |
| **U-18** | BOOK pp.431-432's shortcut table is **column-misaligned**; pairings were read by offset and cross-checked against the web page. Four survived both and are used (IV-32); several ambiguous rows were dropped. | — | A clean extraction or a page image. | confidence in **IV-32**'s citation, not its claim |
| **U-19** | Does a **zero *balance*** in a TallyPrime report column print blank or 0.00? The zero-valued **invoice line** is corpus-supported; the zero balance is a different claim. | — | A Trial Balance or Ledger page showing a nil-balance row, or the report-configuration page on suppressing zero balances (if Tally has a "Show zero balance" toggle, the blank is **configurable**, not intrinsic). | **IV-35** |
| **U-20** | Uncited "Tally-faithful" comments too small to rank, listed so they are not lost: `PayHeadMasterViewModel.cs:596` (attendance/production link a conscious choice) · `LedgerMasterViewModel.cs:353` ("Tally's mailing block does not sit behind an F11 flag" — note the corpus says the **opposite** about a neighbouring field: BOOK lines 3952/4305, "If Maintain Bill by Bill Option not show in ledger screen then press **F12** & Enable") · `VoucherPrintProjector.cs:746` + `LedgerMasterViewModel.cs:358` ("Mailing Name (auto, editable)" — confirmed for the **Company** master at SG p.62, **not** for the **ledger** master where the code applies it) · `MainWindow.axaml:2891-2895` (the batch knob "On by default", justified by making a corpus walkthrough work, which is not evidence of Tally's shipped state) · `JobWorkService.cs:39` (Material In/Out flags "matching TallyHelp's semantics", cited by name only, no URL; job work is outside the corpus entirely) · `StockValuationMethod.cs:7` (a new item defaults to Average Cost — correctly attributed to **DP-1, user-approved**, so not a false claim, but the corpus has zero occurrences of "costing method"). | — | Per-item, as noted. | — |

---

## 7. Coverage — read this before treating any count as a count

**This register is a floor, not a census.** The previous register in this project (`docs/tally-fidelity-defects.md` §3) said the same thing of itself, and saying so is what made it useful. Thirty-five is the number of items **four lenses found in the surfaces they looked at**, not the number that exists.

> **† 2026-08-15 — §7's unmeasured list is now PARTLY CLOSED, by a document written after this one.**
> `docs/full-clone-census.md` (2026-08-10) took a denominator over the whole product — **~115 capabilities: 42
> complete, 44 partial, 21 absent, 8 undetermined** — and its §6 item 10 states which of §7's rows it closed:
> **printing and print layouts (structurally, not fidelity-wise), report layouts (existence only), company
> creation and F11/F12, backup/restore, import/export, POS, banking, security.** **Still genuinely unmeasured:
> GST return *content*, payroll *entry-surface* fidelity, budgets, scenarios, forex, manufacturing, job work,
> multi-currency.** The census's own headline is the one to carry forward, and it is harsher than §7's:
> **only 8 of 115 capabilities have ever had their behaviour compared to a source; ~107 have not** — a `PRESENT`
> row means *reachable*, not *correct*. **Method limit 1 below ("no build and no test was run") still applies to
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
| **Security, users, audit trail, multi-user** | **Not examined at all.** |
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
