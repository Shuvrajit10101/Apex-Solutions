# ▶▶ NEXT-SESSION KICKOFF — Apex Solutions

**Paste the COPY-PASTE PROMPT below as your first message in the new session.**
Rewritten **2026-08-17**, in full.

> 🔴 **WHAT THE PREVIOUS VERSION OF THIS FILE (2026-08-14/15) GOT WRONG — recorded, because this file has now
> gone stale twice and the pattern is the point.** It said schema **v50** (it is **v51**), gate
> **1555 / 389 / 215 / 2013** (it is **1668 / 414 / 231 / 2195**), *"nothing pushed, no PR, no upstream"*
> (**everything is pushed, the branch has an upstream, and PR #34 is OPEN**), and *"64+ commits ahead"*
> (**83 as of `3e968b3`; ≥85 as of `bdd3389`** — and that drift is the lesson, not the number: see the
> commit-count rule below). It also told the reader to work `plan.md` **Phase 10.12** next — superseded by
> **ruling 6**, which
> puts **Phase 10.11** next. And one claim was **already false when written**: it said `plan.md` still carried
> the wrong voucher-type count *"in 9 places"* — `grep -oic` for that phrase over `plan.md` returns **0**;
> W0-6's count half was paid on 2026-08-15 and every live present-tense count there reads 23. Only the
> deliberate quote-to-correct sites still carry the old figure, and they are pinned by a **counted** allow-list
> (see HOW THIS PROJECT WORKS). **⇒ A STATE FILE IS A MEASUREMENT WITH
> A TIMESTAMP, NOT A DESCRIPTION. Re-measure before you believe any line of it — including this one.**
>
> **▶ 🔴 AND ONE FIGURE IS STALE BY CONSTRUCTION, NOT BY NEGLECT — THE COMMIT COUNT (added 2026-08-17).**
> *"83 commits ahead"* was **correct when written and wrong within the day**, because the count moves with
> **every push** and with any move of `origin/main`. Correcting it to 85 would be wrong by the same mechanism.
> ⇒ **A `.md` FILE CAN NEVER NAME ITS OWN COMMIT COUNT.** Every commit- and file-count in this document is now
> written as an **as-of pinned to a sha**, or as a **floor**, and never as a bare measurement — the sha is what
> makes the number checkable, and the "≥" is what keeps it true as the branch grows. The same treatment is
> owed to any figure that changes without anyone editing the document. **The GitHub PR body is exempt and is
> NOT edited here — that artefact is A12's alone (R4).**

---

## COPY-PASTE PROMPT (paste verbatim)

> Continue Apex Solutions. Read `memory.md` (last entry first — it is the session-close record), then
> `plan.md` **§5** (the eight standing rulings, ahead of every phase block), then `CLAUDE.md`, `agents.md`,
> then `docs/full-clone-census.md` (the denominator) and this file.
>
> **▶ VERIFY STATE, DON'T TRUST IT.** Everything below is a measurement with a timestamp, not a description.
> Re-measure before you plan on it. Branch `claude/apex-wrong-figures-bc45f4`, HEAD **at or after `bdd3389`**
> (as of 2026-08-17 — a floor, and necessarily one: the commit that carries this very line moves HEAD past
> `bdd3389` as it lands, so **this file can no more name its own tip than its own commit count**.
> `git rev-parse HEAD`), schema **v51**,
> **≥85 commits ahead of `origin/main` (`c655dc2`) as of `bdd3389` — a floor pinned to a sha; verify, do not
> quote**, pushed and in sync, **PR #34 OPEN and NOT merged**.
> **THE THING TO RE-MEASURE IS THE FOUR PER-PROJECT COUNTS, NEVER THE TOTAL:**
> build **0W/0E** · `Apex.Ledger.Tests` **1668** · `Apex.Ledger.Io.Tests` **414** ·
> `Apex.Persistence.Sqlite.Tests` **231** · `Apex.Desktop.Tests` **2195**. Run each project separately and read
> the number off the final tree. A truncated Desktop run once reported "Passed! 610" against a real 1635 and
> looked exactly like success.
>
> **▶ TRAP 1 — THE SDK.** Bare `dotnet` on PATH is **runtime-only** and reports *"No .NET SDKs were found"*.
> That is a PATH artefact, not the machine. **Use `C:\Users\dkpho\.dotnet\dotnet.exe` for every build and
> test.** **Never pipe a build or test through `tail`/`head`** — that is how a truncated run gets mistaken for
> a pass. Let it finish and read the summary line.
>
> **▶ TRAP 2 — AGENT LIVENESS.** An agent's **real transcript is `…/<session-id>/subagents/agent-<id>.jsonl`**
> (with an `agent-<id>.meta.json` beside it), **NOT the tasks output path**. Checking the wrong one made the
> main loop **declare a LIVE agent dead** and nearly put two agents into one worktree. An empty or absent
> tasks-output file proves nothing. Check transcript mtimes before you relaunch anything.
>
> **▶ TRAP 3 — WORKTREES.** **`isolation: 'worktree'` cuts from `main`, NOT from the current branch.** A
> parallel track created that way starts at `c655dc2` and silently lacks **every commit on the branch** (≥85
> as of `bdd3389`) — **schema v51 among
> them** — so it would build a v50 database and every migration fixture in it would be a lie. **A12, and only
> A12 (R4), cuts the worktree explicitly from the branch tip, and `Schema.CurrentVersion` is verified INSIDE
> it before any build** (`src/Apex.Persistence.Sqlite/Schema.cs:159` must read
> `public const int CurrentVersion = 51;`). **A worktree that comes up at v50 was cut from `main` — re-cut it;
> do not debug the difference.**
>
> **▶ THE EIGHT STANDING USER RULINGS (R12) — all in `plan.md` §5; do not re-litigate:**
> **1.** Build order = census order: Wave 0 remainder → Wave 1 correctness → Wave 2 structural → Wave 3 breadth.
> **2.** Schema authority is FULL — one version bump per slice, each with a forward migration, round-trip tests
> and the migration-equivalence check, every bump recorded in `plan.md`.
> **3.** Negative-stock valuation is built on the sourced formula **without waiting for T3**; the refuted
> `AverageCost` goldens are re-derived from the formula, never edited to match the code.
> **4.** Merge cadence: accumulate on the branch, pushed after every slice *(partly superseded by 8)*.
> **5.** Fidelity is measured **per slice** — a definition-of-done change: every slice ends with a fidelity row,
> or with a written record of why the corpus cannot settle the question.
> **6.** Voucher lifecycle jumps the queue — **Phase 10.11 lands next**; W0-3 and W0-5 slip behind it.
> **7.** ~~The print engine starts **now**, as a parallel long-pole track (census S5).~~ **🔴 SUPERSEDED
> 2026-08-18 — STAY SEQUENTIAL THROUGH S5, THEN START THE PRINT ENGINE.** Reasons, both recorded at ruling 7's
> block in `plan.md` §5: **S5a rewrites the engine's `Replace` contract**, the riskiest work in the phase; and
> **a parallel track needs its own worktree cut from the branch tip**, whose cost a crashed agent demonstrated.
> Ruling 7's exception to ruling 1 **lapses**; the engine returns to Wave 4. **Ruling 6 is untouched.**
> **8.** Merge now, then keep accumulating — **supersedes 4**: one PR, then a fresh branch.
>
> **▶ WHAT IS NEXT.** **(a) Phase 10.11 — voucher lifecycle (alter · delete · cancel).** Ruling 6. **Its W0-7
> prerequisite is ALREADY DISCHARGED** — `1de940e` (2026-08-10) extended `PopulatedCompanyFixture` to post
> 23 of 23 seeded base kinds; the ruling that said W0-7 must ship first was built on a census figure ten days
> stale and is superseded in place. **A COMPLETE design already exists — BUILD FROM IT, DO NOT REDO IT:**
> `docs/design-records/phase-10-11-voucher-lifecycle-design.md`, **1,377 lines, all 12 sections**. **(b) The
> print engine, ~~in parallel~~ **AFTER S5c** — 🔴 amended 2026-08-18, ruling 7 superseded; it is sequential
> now, and still under Trap 3's worktree rule when it does start. **(c) Then W0-3** (Restore reachable from Company
> Select) **and W0-5** (negative-stock warn toggle + e-Way config editor), which were deferred behind the
> lifecycle slice.
>
> **▶ WHAT IS BLOCKED ON ME (the user) — nothing here proceeds without it.** **PR #34 is OPEN and awaiting my
> review** (<https://github.com/Shuvrajit10101/Apex-Solutions/pull/34>) — nothing merges until I act on it.
> And the **TallyPrime T3 and T8 measurements** in `docs/tallyprime-valuation-test-books.md`, which nobody can
> substitute for. **Ask me; do not guess, and do not work around them.**
>
> **▶ THE PRESERVED DESIGNS ARE IN `docs/design-records/`** — the W0-2b design, the W0-7 audit, and the
> **Phase 10.11 design, which is COMPLETE** — `phase-10-11-voucher-lifecycle-design.md`, **1,377 lines, all
> 12 sections** (§0–§11 plus the R12 appendix), as its own opening line declares. **Build from it; do not redo
> it.** *(An earlier `…-design-PARTIAL.md` was the main loop snapshotting that same agent's part-written output
> mid-run; the finished file REPLACED it, and no `PARTIAL` token survives in the committed filename.)* All
> three are **historical snapshots**: their pointers were rewritten to `file.ext line NN` on purpose and are
> **not maintained**. Re-derive before use.
>
> **▶ Clone, never invent.** The corpus is readable from every worktree via a `tally` junction. Use
> `pdftotext -raw` as the second pass on any tabular page — `-layout` scrambles multi-column tables.

---

## STATE (measured 2026-08-17 on the worktree, by direct command — not relayed)

| | |
|---|---|
| Branch | `claude/apex-wrong-figures-bc45f4`, **in sync with its upstream** |
| HEAD | **at or after `bdd3389`** — a floor pinned to a sha, as of 2026-08-17, and stale by construction the instant it is written: the commit that carries this table moves HEAD past `bdd3389`. `git rev-parse HEAD`. The previous revision of this file asserted `3e968b3` as a bare fact and was two commits wrong by the time anyone read it. |
| Ahead of `origin/main` | **≥85 as of `bdd3389`** — a floor pinned to a sha, never a live figure. Re-run `git rev-list --count origin/main..HEAD`; this cell goes stale on the next push and on any move of `origin/main`. |
| `origin/main` | **`c655dc2`** — unmoved for the whole run |
| Schema | **v51** (`src/Apex.Persistence.Sqlite/Schema.cs:159`) |
| Gate | build **0W/0E** · Ledger **1668** · Io **414** · Sqlite **231** · Desktop **2195** |
| PR | **#34 — OPEN, NOT MERGED**, mergeable, not a draft |

**PR #34 carried 83 commits, 354 files, +80,010 / −1,795 AS OF `3e968b3` WHEN IT WAS OPENED — and FIVE schema
migrations, not one.** *(Commit, file and line counts on an open PR move with every push — the branch is
already at ≥85 commits as of `bdd3389`. Read those four numbers as a snapshot of the PR at open, and get the
live figures from the PR itself. **The PR body is A12's artefact and is not edited from here (R4).**)*
`origin/main` is at `CurrentVersion` **46**; HEAD is at **51**, so the PR spans `MigrateV46ToV47` through
`MigrateV50ToV51`. The main loop's brief said "one migration"; A12 re-derived it from
`git show origin/main:…/Schema.cs` and corrected it **before it reached the PR body**. Record that as a save.

---

## THE METHOD FINDING — the most transferable output of the run

**Three slices shipped GREEN and were then found to contain 17, 34 and 42 defects. Not one was found by the
suite.** The figures carry their derivation: **W0-13 → `938530a`** (17, three sequential lenses, none
rejected) · **WF-1's owed review → `31c476b`** (34; per-lens breakdown in `docs/wf1-owed-review-findings.md`)
· **W0-2b → `f66253c`** (42 — 4 BLOCKER, 18 MAJOR, 20 MINOR).

Three defect classes, **in ascending nastiness**:

1. **OVERSTATED CLOSURE** — a record claiming more than the code shipped. **Caught by READING.** Cheapest.
2. **DEAD GUARDS** — code that runs, looks defensive, and that **nothing pins**. Invisible to reading *and* to
   the suite. **Caught only by MUTATION.**
3. **A DOCTORED TEST** — green, correctly named, **asserting the OPPOSITE of its name.** The worst of the
   three, because **from any distance it looks HEALTHIER than the other two**: it adds to the count, it reads
   as coverage, and it certifies the very defect it was written to prevent.

🔴 **The escalation, and it is the figure to remember: on W0-2b, NINE GUARDS WERE DELETABLE *SIMULTANEOUSLY*
and all 3,828 tests of the two affected projects stayed green** (Ledger + Desktop at that slice's pre-fix
baseline, 1,665 + 2,163; **the full suite was 4,473**). And the test file's own header claimed the mutations
had been run — **three named mutations did not redden the test they were named on.**
⇒ **"THE MUTATION WAS RUN" IS A CLAIM, AND A CLAIM IN A TEST HEADER IS EXACTLY AS CHECKABLE AS ANY OTHER.**

---

## WHAT LANDED THIS SESSION (every sha confirmed an ancestor of HEAD)

| sha | what |
|---|---|
| `9dfb317` | the R5/R6 catch-up for a **34-commit unrecorded range** |
| `fa651ae` | **W0-12** — a sub-paisa figure poisoned the open company, so every LATER save threw |
| `938530a` | **W0-13** — five catch filters turned a `DbException` into a crash; seven money paths persisted unguarded |
| `85f82dd` | **W0-15/W0-16** — the GST routing / place-of-supply fix, **and the first doc-vs-code CI check** |
| `e49b88e` | **WF-1** GST five-level masters (INERT, v51) + **W0-2a** the printed supplier address |
| `31c476b` | **WF-1's owed review, paid late** — 34 findings, fixed forward |
| `f66253c` | **W0-2b** — the Company Create/Alter screen |

**The four defects closed that were LIVE IN THE PRODUCT:** a sub-paisa figure **poisoned the open company** so
every later save threw · **database exceptions crashed instead of reporting** · **GSTR-1 filed the SUPPLIER'S
OWN State against an IGST-bearing voucher** (NIC validation 24 makes that pair self-refuting) and an issued
invoice **could not be reprinted** once the home State was cleared · the **CGST Rule 46(a) supplier address
was blank on every invoice, with nowhere in the product to type it**.
**None of these closures is retroactive** — a book already on disk carries no address until someone opens
Alter and types one.

---

## 🔴 A SOURCING FINDING THAT MAY OVERTURN A STANDING REJECTION

**`pdftotext -layout` emits a multi-column PDF table as INDEPENDENT TOP-TO-BOTTOM STREAMS.** The Book's
three-column shortcut table therefore arrives as three separate lists and the pairing must be reconstructed by
counting. On p.435 the counts match. **On pp.436–437 they do NOT** — 20 keys against 21 function-fragments on
one page, 10 against 11 on the other — **so any pairing read off a `-layout` dump of those pages is a guess.**
`pdftotext -f <p> -l <p> -raw` emits the table **cell by cell in true reading order** and resolves it.

- **Consequence already measured:** `plan.md` Phase 10.11's R7 line says TallyPrime *"reserves Ctrl+Enter for
  display-only drill-down."* The corpus read with `-raw` says the opposite — `Ctrl+Enter` →
  *"To alter a master during voucher entry or from drilldown of a report."* **That R7 line is owed a
  correction**, and our binding is a *smaller* divergence than the plan records.
- **🔴 RE-TEST A REJECTED SOURCE.** The corpus PDF `659947760-Tally-Prime-Short-Key.pdf` was rejected earlier in
  this project because its table was *"misaligned by ~2 rows"*. **That rejection may itself be a `-layout`
  artefact.** Re-test it with `-raw` before the source stays discarded.
- **Proposed standing instruction:** `-raw` is the mandatory second pass for any tabular corpus page, and a
  `-layout` pairing whose column counts disagree is **not evidence**.

---

## THE DENOMINATOR — read it before "fix everything" sounds like a plan

🔴 **DO NOT READ A DENOMINATOR OUT OF THIS FILE. `docs/full-clone-census.md` §1.2a is the named capability
list, §1.2 is its column sum, and §1.3's anchor block holds the four fidelity figures — those are the only
places any of it is maintained.** This section deliberately carries **no digits**, because the digits it used
to carry are exactly what went stale.

▶ **THE DENOMINATOR MOVED ON 2026-08-18, so if you remember the old one, it was not a typo.** This section
read *"~115 named capabilities: 42 complete, 44 partial, 21 absent, 8 undetermined"* and *"9 of 115 have had
their behaviour compared to a source; 106 have not"* — the **2026-08-10 snapshot at HEAD `468a96e`**, which
census §1.2 now keeps in place under a *(superseded)* heading precisely because outside documents still quote
it. Census §1.2b keeps the **three causes apart** and they must not be conflated:

1. **GRANULARITY — most of the move.** §1.1 rule 1 defines a capability as a Tally menu row or an F11 toggle,
   and the rows had never been written out at that granularity; the old figure was assembled area by area as
   bare integers. **Report families are still compressed to one row each (§1.1 rule 2 is retained)**, so the
   new figure is *not* the "expand everything" number, and the ~14 registers hiding inside those families are
   still hidden.
2. **THE ABSENT COLUMN WAS PROVABLY TOO SMALL** — the old split allowed **zero** absent capabilities in
   Statutory, Payroll, Inventory masters and Reports, against absences evidenced on zero-hit searches in all
   four. That is what makes the old split *wrong* rather than merely coarse.
3. **WORK SHIPPED SINCE THE SNAPSHOT — the smallest part** (W0-2b, S3, S4, W0-1, W0-7, W0-12/13/14/15,
   schema v51).

A `PRESENT` row means *reachable*, not *correct*. **Ruling 5 exists to close the uncompared fidelity
denominator as a by-product of ordinary work** rather than leaving it to a dedicated campaign that never gets
funded; §1.3's anchor block carries its current width with an as-of date, and **if any copy anywhere disagrees
with that block, the block wins and the copy is a defect.**

---

## NEGATIVE STOCK — read before touching it

**Built:** posting (the old unconditional hard block is gone). **Not built:** the control surface (that is
W0-5) and the **valuation**.

🔴 **Valuation has been attempted EIGHT times and reverted every time**, each producing a *different* unbounded
Balance-Sheet error, each passing a full green suite. All eight failure modes are measured in
`tools/HeadOracle/README.md`.

**Ruling 3 settles the approach.** TallyPrime's Average Cost has **no repayment model at all**:
`Average Cost = Total Inward Value ÷ Total Inward Qty`, sales never touch the pool, a purchase return does
shrink it. All eight attempts invented a repayment/lot-matching mechanism **Tally does not have** — so the fix
is to **delete** machinery, not write a ninth version. Our `RunAverage`
(`src/Apex.Ledger/Services/StockValuationService.cs:329`) is a *perpetual moving average*: its Outward arm
reduces both qty and cost. On T3 the formula predicts **₹1,333.33**; we give **₹1,500**.

**Do NOT rebuild the oracle harness — `tools/HeadOracle/` already exists.** But its AverageCost oracle
implements the refuted repayment model, so it will **reject a correct stateless engine**: those goldens are
**re-derived from the formula, never edited to match code**. Other earned constraints: the conservation/band
check is a **tautology** against a stateless pool; the divergence is **not** a negative-stock phenomenon (T3
never goes negative and HEAD is still wrong); **never re-rate an existing value**; **no floors or clamps** (a
positive-qty floor once *hid* a real error, turning a diagnosable −₹120 into a plausible ₹0); **Average Cost
only**; **do not change the godown dimension in the same slice**.

---

## BLOCKED ON THE USER (nobody can substitute for these)

- **PR #34 — OPEN and awaiting review.** <https://github.com/Shuvrajit10101/Apex-Solutions/pull/34>
  **Nothing merges until the user acts on it.** 83 commits / 354 files **as of `3e968b3`, when it was opened**
  (≥85 commits as of `bdd3389`; read the live figures off the PR) — and **five schema migrations**, which is
  the number that does not drift.
- **The TallyPrime T3 and T8 measurements** — run in legitimate **Educational Mode**;
  `docs/tallyprime-valuation-test-books.md` has the books.
  - **T3 — falsifies the whole Average Cost design.** Buy 10 @ ₹100 · sell 5 · buy 5 @ ₹200; closing value on
    31-May. **₹1,333.33** ⇒ proceed. **₹1,500.00** ⇒ the premise is dead, stop.
  - **T8 — unfreezes the interest divisors.** ₹44,000 at 10%, Per = 30-Day Month, 30-day window. **₹4,400** ⇒
    per period. **~₹366** ⇒ per annum. The `DaysInMonth × 12` defect is live *deliberately*, pending this.
  - **T1 / T2 / T4** — negative valuation, recovery, per-godown-vs-item. **T4 is the question that stopped the
    work eight times and no document answers it.** **T7 needs a third option added before it is run.**

---

## PRESERVED DESIGNS — `docs/design-records/`

The session scratchpad does not survive the session, so three records were copied into the repo:

| file | what | state |
|---|---|---|
| `w0-2b-company-screen-design.md` | the Company Create/Alter design | shipped as `f66253c` |
| `w0-7-fixture-audit.md` | the `PopulatedCompanyFixture` audit | its **R-5** is assigned to W0-6's open remainder |
| `phase-10-11-voucher-lifecycle-design.md` | the voucher-lifecycle design | **COMPLETE — 1,377 lines, all 12 sections; Phase 10.11 is built FROM it** |

**All three are HISTORICAL SNAPSHOTS.** Two mechanics are deliberate and must not be "tidied": every
`file.ext:NN` pointer was rewritten to `file.ext line NN` so the citation invariant does not read them as live
pointers (those numbers were accurate when captured and are **NOT maintained**), and each carries a header
saying so. The live, maintained pointers are in `plan.md` and `memory.md`, which are re-anchored on edit.

---

## HOW THIS PROJECT WORKS (do not relearn these)

- **R1–R14 in `CLAUDE.md`.** Agentic-first: the main loop decides and synthesises; agents do the work.
- **Only A12 touches git.** **RE-RUN THE FULL GATE YOURSELF every slice; never relay an agent's numbers.**
- **A gate is the four per-project counts, never the total**, and it is read off the **final** tree. When two
  records disagree, **the one that CANNOT be re-measured is not the tie-breaker.**
- **A GREEN SUITE HIDES BUGS.** See THE METHOD FINDING above. **The reviews are not optional.**
- **Run review lenses SEQUENTIALLY.** Parallel lenses die together and lose everything; sequential ones
  journal as they complete.
- **Pass the COMPLETE finding list to the fix agent, never a summary.**
- **Agents die constantly and always leave files on disk.** Check `git status` after every death. Matching test
  counts do **not** prove a tree is unmutated — a dead reviewer once left a `±₹0.50` mutation in a production
  file and the re-gate showed identical counts, because nothing covered that line.
- **Worktree directories vanish.** Commit early; committed work survives, uncommitted work does not.
- **Odd-paisa fixtures always.** A ±₹0.50 defect survived this project's whole life under six round-number
  assertions.
- **The doc-vs-code CI check reads every `.md` in the repo.** Any `file.ext:NN` you write must resolve, and its
  **counted allow-lists must stay exact** — an entry left behind after the document was corrected fails the
  suite just as loudly as a new violation. The phrase "24 predefined voucher types" appears in this file for
  exactly that reason: this file is one of the pinned quote-to-correct sites, its entry is counted at **one**,
  and adding or removing an occurrence turns the suite red. (TallyPrime really has 24; we seed 23 — a recorded
  fidelity gap, decision D24-B, not a typo.)
- **WebFetch gets 403 from some Tally/NIC hosts and a TLS error from `taxinformation.cbic.gov.in`** — a real
  browser retrieves them, and `curl` fetched the INV-01 schema xlsx directly. State which method you used.
- **Do NOT open `C:\Users\dkpho\Downloads\Tally7.2`** — cracked, and the wrong product. The 9 obsolete-by-law
  pre-GST capabilities (VAT, CST, Service Tax, Excise) **will not be built** — user ruling, 2026-08-10.

---

## THE EIGHT DIVERGED RULES — the register this file deleted on 2026-08-17

🔴 **The 2026-08-17 rewrite of this file DROPPED the eight-row list, and both `memory.md` and `plan.md` recorded
that the list lived *only* here — so the rewrite destroyed the sole register of eight known divergences.** It is
restored below from **`plan.md`'s W0-11 entry, which is the authoritative record** (its own words: *"this entry
… is now their only surviving register"*). **Read W0-11 before acting on any row** — every row there carries the
file:line evidence this table deliberately does not repeat.

**Never cite a bare D-number.** Three registers in this repo number rows D1…D8 and they collide —
`docs/tally-fidelity-defects.md` and `docs/tally-gap-decisions.md` are the other two — and `plan.md` also
numbers the **drift locks** D1…D8, which is a fourth use. The letters below are the stable key; the D-numbers
are only those `plan.md` itself attaches.

| row | the rule | verdict, as `plan.md` W0-11 states it |
|---|---|---|
| **(a)** *D1* | pro-rata apportionment | **CLOSED.** One home, `src/Apex.Ledger/ProRata.cs`; the three private copies are one-line delegations. The premise it opened on — a live divide-by-zero in a filed return — is **REFUTED**: the caller-side guards `continue` first, so D1 changed no answer and is a pure de-duplication. |
| **(b)** *D2* | Indian digit grouping (3;2;2) vs Western | **CLOSED, with one surviving site.** `src/Apex.Ledger/IndianMoneyFormat.cs`, its culture frozen read-only so the rule cannot be rewritten from anywhere. **Carry-forward (d):** one money site in `ForexReportViewModel` still prints Western-grouped and escapes all four locks — and whether a FOREIGN-currency balance should be Indian-grouped at all was never asked. |
| **(c)** *D3* | rupees → integer paisa | **CLOSED.** `src/Apex.Ledger/PaisaConversion.cs`, deliberately **one rule with two named answers**: `ToPaisaExact` REFUSES a sub-paisa amount at a persist/export boundary, `ToPaisaRounded` quantises a derived report or set-off figure. Truncation is banned outright. |
| **(d)** | `IsInterState` | **UNTOUCHED by W0-11, and one of the two rows where the copies genuinely disagree today** — two live implementations differing on the null-home case, no drift lock over either. It is **carry-forward (b)**, whose R12 ruling is **(B) refuse at the routing call**, and `plan.md` assigns the discharge to **W0-15**. |
| **(e)** | place of supply | **UNTOUCHED by W0-11** — three derivations, so a B2C inter-state supply with no recorded party State gets the home code from one and a blank from another. Deciding it is **statutory, not de-duplication**. Same carry-forward (b), same W0-15 discharge. |
| **(f)** *D6* | `ApplyRounding` | **REFUSED WITH REASONS, and the refusal is only partly complete.** The two implementations genuinely differ on negatives — interest rounds the MAGNITUDE, payroll is SIGNED — across different enums and different statutory domains, so unifying them would have silently changed one domain's arithmetic. **Carry-forward (e):** only the INTEREST half is pinned, so converting the payroll side would still pass green. |
| **(g)** *D7* | HSN/SAC sentinel | **CLOSED BY DESIGN.** The resolution ORDER is single-homed in `GstReportSupport`; the **sentinel is kept different per consumer on purpose** — the GSTR-1 bucket label, the INV-01/EWB-01 payloads and the printed invoice each need a different absence — and each is now pinned by its own test. |
| **(h)** *D8* | basis-point rate format | **REFUSED WITH REASONS, two-thirds discharged.** The reported defect is a **FALSE POSITIVE** — every `RateBasisPoints` is an `int`, so the un-representable rate it was raised on cannot occur. **Carry-forward (c):** ten host-culture-bound `$"{x:0.##}"` rate renderings survive, and one half-rate site genuinely CAN carry a third decimal, which the exhaustive proof never covered. |

**W0-11's own tally: 3 CLOSED · 1 CLOSED BY DESIGN · 2 REFUSED (both refusals partial) · 2 UNTOUCHED** — its
summary sentence is *"3 of 8 closed, and the two most consequential rows are still open."*
⚠️ **That sentence pre-dates W0-15.** `plan.md` is internally inconsistent on rows (d)/(e): carry-forward (b) is
marked **IN PROGRESS** while W0-15's own row reads **✅ DONE 2026-08-15**. **Re-derive from W0-15 before you rely
on either**, and note W0-15 explicitly did NOT unify the INV-01 limb, left the printed blank blank, and took no
schema version — the party-State snapshot it spawned is a separate, still-open slice.

---

## OPEN, NEEDS DECIDING OR SCHEDULING

- **The R9 real-app run for the whole campaign is STILL OUTSTANDING.** Nothing in this run has been exercised
  in the running app. Launch with `dotnet run --project src/Apex.Desktop -c Release` (using the real SDK path).
  Two things to drive by hand: F11 statutory config → type `7000.50` into the gratuity cap and confirm it is
  refused with a message naming the field; and Gateway → Masters → **Alter Company** → type a supplier address
  and confirm it reaches a printed invoice.
- **`plan.md`'s Phase 10.11 R7 Ctrl+Enter line is owed a correction** — see the sourcing finding above.
- **W0-6's remainder is NOT started** — the false Phase 1 / 2 / 5 / 9 claims, IV-19's drill-down number, the
  `Schema.cs` doc comment, and the W0-7 audit's **R-5**. The count half was paid on 2026-08-15.
- **CGST Rule 138(14) goods-relief lists are unmodelled** — the engine over-generates e-way bill requirements.
  Pinned by a `PINNED_GAP` test, shaped so exactly one test fails when the data slice lands. Unscheduled.
- **`IndianState.All` carries state code 97 but not 96 or 99** — adding them is **unsafe** because
  `Gstin.Validate` shares the list and would accept nonexistent GSTIN prefixes. Needs designing.
- **`CostAllocationStrictness` is misnamed** for what it now gates (~13 files to rename).
- **The 30 `StarvedStarAllowList` waivers** → runtime locks (61 sites, zero measurement).
- **GSTR return JSON keys are invented** (`GstReturnJson.cs`) — the third instance of the invented-payload
  class. Currently dead code (no production caller), so no live filing harm; the fix method is proven twice.
