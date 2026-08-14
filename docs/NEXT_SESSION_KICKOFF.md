# ▶▶ NEXT-SESSION KICKOFF — Apex Solutions

**Paste the COPY-PASTE PROMPT below as your first message in the new session.**
Written **2026-08-14**. This file was previously dated 2026-07-05 and claimed schema v13 / 570 tests /
a branch that no longer exists — it was recorded as an active defect (census P7) and is now rewritten.

---

## COPY-PASTE PROMPT (paste verbatim)

> Continue Apex Solutions. Read `memory.md`, `plan.md`, `CLAUDE.md`, `agents.md`, then
> `docs/full-clone-census.md` (the denominator) and `docs/NEXT_SESSION_KICKOFF.md` (this file).
>
> **VERIFY STATE, DON'T TRUST IT.** Branch `claude/apex-wrong-figures-bc45f4`, schema **v50**,
> gate **Ledger 1555 · Io 389 · Sqlite 215 · Desktop 2013**, build 0W/0E,
> 64+ commits ahead of `main`, **nothing pushed, no PR, no upstream**. Re-run the gate yourself before
> believing any of it. A gate is the FOUR PER-PROJECT COUNTS, never the total.
>
> Work the queue in `plan.md` Phase 10.12 with the gated loop: design → build → three **sequential**
> adversarial lenses → fix agent (pass the COMPLETE finding list, never a summary) → my own four-count
> gate → A12 commits. Then keep going through the census's ranked list.
>
> **Clone, never invent.** The corpus is now readable from every worktree.

---

## STATE (verified 2026-08-14 by direct measurement, not relayed)

| | |
|---|---|
| Branch | `claude/apex-wrong-figures-bc45f4` |
| HEAD | the diverged-copies commit (on top of `23e0df1`) |
| Gate | Ledger **1555** · Io **389** · Sqlite **215** · Desktop **2013** · build 0W/0E |
| Schema | **v50** (`Schema.cs` `CurrentVersion`) |
| Pushed | **nothing** — no PR, no upstream; `main` = `origin/main` = `c655dc2` |

⚠️ `origin/main..HEAD` carries ~20 commits of **earlier unpushed Phase 10.7/10.8 work** as well as this
session's. A PR body must account for both.

---

## THE TWO THINGS THAT MATTER MOST

### 1. The corpus is now readable from every worktree — this was the root cause of everything

The git-ignored `tally/` PDFs lived **only in the main checkout**. Every agent ever dispatched into a
worktree was told to clone TallyPrime and was **physically unable to open it**. That is the mechanical
cause of this project's entire invent-rather-than-clone pattern — corroborated by the census: only
**13 of 489 source files cite the corpus**, while 331 cite Indian statute, because statute was reachable
on the web and the corpus was not. Agents cited what they could read.

**Fixed:** a junction at `<worktree>/tally` → `…/Apex Solutions(end)/tally` in all eight worktrees.
10 PDFs, `pdftotext -layout` works, **invisible to git** (`.gitignore:73`), so R4 holds.
**If a new worktree appears, create the junction:**
`New-Item -ItemType Junction -Path "<worktree>\tally" -Target "…\Apex Solutions(end)\tally"`

### 2. The census gave "fix everything" a denominator

`docs/full-clone-census.md` — **~115 capabilities: 42 complete, 44 partial, 21 absent, 8 undetermined.**
But the number that governs the product: **only 8 of 115 have ever had their behaviour compared to a
source. ~104 have not.** A `PRESENT` row means *reachable*, not *correct*.

Open-defect census: **30 functionality · 11 UI rows (~890 sites) · 19 process = 60 OPEN**, all closable by
ordinary engineering (6–10 weeks) — which would leave the ~104 untouched. **Zero is reachable only by
measuring first, then fixing.** A green suite here has repeatedly proved self-consistency and nothing more.

---

## WHAT LANDED THIS SESSION (9 commits, all reviewed)

| sha | what |
|---|---|
| `23e0df1` | INV-01 e-invoice payload files **NIC field names**, not ours (15 invented keys; `GstRt` was outside its declared range) |
| `439220d` | four omitted W0-9 review findings + the 13-file BOM sweep |
| `50fa892` | **one bill-of-supply rule** (5 copies → 1) + the printed total = the posted debt (closed a **₹8,513.41** gap) |
| `4263045` | plan: W0-9 / W0-10 |
| `4223996` | e-Way Part-A files **NIC master codes**, not descriptions |
| `7540d84` | plan: W0-8 |
| `ef8f24a` | the POS twin + the §10(4) posting guard |
| `b12b8cb` | Bill of Supply routing + `DocumentTitle` |
| `1de940e` | fixture extended to every voucher family (was 8 of 23 base types) |
| `82d72cb` | `Schema.cs` class doc said v46 while the constant was v50 |

Earlier in the session, merged from parallel streams: interest running-balance accrual, reorder Nett
Available (HARD GATE PR-8 retired), order-fulfilment + the party root-cause fix, Ctrl+B no longer posting
irreversible vouchers, and the Alt+D modifier hole.

---

## THE QUEUE (ranked, from the census §6)

1. **The 8 already-diverged rule copies** — may be partly done; **7 partial files are parked at**
   `…\scratchpad\diverged-partial` (5 src + 2 tests). They were written by an agent that never compiled
   them: **triage them, don't assume they're an asset.** The eight: `Apportion` divide-by-zero in a filed
   return · Indian vs Western digit grouping **from the same assembly** · sub-paisa throws in 2 places and
   rounds silently in 6 · `IsInterState` answers two ways · place of supply derived two ways · `ApplyRounding`
   inverts on negatives · HSN sentinel diverges · basis-points format renders 3 ways.
2. **Negative-stock valuation** — see the dedicated section below. Highest money-at-risk.
3. **GSTR return JSON keys are invented** (`GstReturnJson.cs:19-24`) — the *third* instance of the
   invented-payload class. Currently dead code (no production caller), so no live filing harm. The method
   is proven twice; reuse it.
4. **Wave 0's unbuilt items** — W0-2 Company Create/Alter (every invoice carries a **blank seller address**,
   a Rule 46 breach, unfixable from the UI), W0-3 Restore from Company Select, W0-5 the `WarnOnNegativeStock`
   and e-Way config surfaces (**live behaviour with zero UI**), W0-6 doc corrections.
5. **W0-6 + a doc-vs-code test** — `plan.md` still claims "24 predefined voucher types" in 9 places while
   `SeedVoucherTypes.cs` seeds **23**. No test in the repo reads a `.md` file; add the CI check.
6. **The 30 `StarvedStarAllowList` waivers** → runtime locks (61 sites, zero measurement).
7. Register hygiene: IV-9, D7 and IV-20 now read the **opposite** of the code; ~16 drifted line numbers.

---

## NEGATIVE STOCK — read before touching it

**Built:** posting. The old unconditional hard block is gone; `Company.cs:268` carries
`WarnOnNegativeStock = true` and `InventoryPostingService.cs:185` honours it.
**Not built:** the control surface (zero `src/Apex.Desktop` hits) and the **valuation**.

🔴 **Valuation has been attempted EIGHT times and reverted every time**, each producing a *different*
unbounded Balance-Sheet error, each passing a full green suite. See
`tools/HeadOracle/README.md:84-195` for all eight measured failure modes.

**A sourcing pass has now found why, and it is decisive.** TallyPrime's Average Cost has **no repayment
model at all** — official formula: `Average Cost = Total Cost [Inward Value] / Total Qty [Inward qty]
{Annual}`, `Closing Value = pool rate × closing qty`. Sales **never touch the pool**
(*"The Average Cost continues to be Rs.122.50 since there is no change in the Inward Cost"*); a
**Rejection Out / purchase return does** shrink it. All eight attempts invented a repayment/lot-matching
mechanism **that Tally does not have**. The fix is to **delete** machinery, not write a ninth version.

Our `RunAverage` (`StockValuationService.cs:329`) is a *perpetual moving average* — its Outward arm reduces
both `qty` and `cost`. On T3 (buy 10 @ ₹100, sell 5, buy 5 @ ₹200) Tally gives **₹1,333.33**, we give **₹1,500**.

**Do NOT rebuild the oracle harness — `tools/HeadOracle/` already exists** (8,051 lines, 62 scenarios,
198 goldens, 40 mutation scripts). But its AverageCost oracle is `RunAverageDebtAware`, which
*implements the refuted repayment model*, so it will **reject a correct stateless engine**. **30 of 36
AverageCost goldens must be re-derived from the formula, never edited to match code.**

Other binding constraints, all earned: the conservation/band check is a **tautology** against a stateless
pool (a pool rate is a convex combination of the inward rates, so it is always in band); the divergence is
**not** a negative-stock phenomenon (T3 never goes negative and HEAD is still wrong), so the harness's
"HEAD is trusted on never-negative books" premise is false for AverageCost; **never re-rate an existing
value**; **no floors or clamps** (a positive-qty floor was tried and *hid* a real error, turning a
diagnosable −₹120 into a plausible ₹0); **Average Cost only** — FIFO under negative quantity is undocumented
by every source; **do not change the godown dimension in the same slice**.

---

## BLOCKED ON USER MEASUREMENT (nobody can substitute for these)

Run in legitimate **TallyPrime Educational Mode** — `docs/tallyprime-valuation-test-books.md`.

- **T3 — falsifies the whole Average Cost design.** Buy 10 @ ₹100 · sell 5 · buy 5 @ ₹200; closing value
  on 31-May. **₹1,333.33** ⇒ proceed. **₹1,500.00** ⇒ the premise is dead, stop.
- **T8 — unfreezes the interest divisors.** ₹44,000 at 10%, Per = 30-Day Month, 30-day window.
  **₹4,400** ⇒ per period. **~₹366** ⇒ per annum. The `DaysInMonth × 12` defect is live in `c408037`
  *deliberately*, pending this.
- **T1 / T2 / T4** — negative valuation, recovery, and per-godown-vs-item. T4 is the question that stopped
  the work eight times and **no document answers it**.
- **T7 needs a third option added before it is run** — item-level stateless predicts **₹3,504.55**, which is
  not among its two choices, so as written it cannot discriminate the model.

---

## HOW THIS PROJECT WORKS (do not relearn these)

- **R1–R14 in `CLAUDE.md`.** Agentic-first: the main loop decides and synthesises; agents do the work.
- **Only A12 touches git.** **RE-RUN THE FULL GATE YOURSELF every slice; never relay an agent's numbers.**
- **A gate is the four per-project counts, never the total.** A truncated Desktop run once reported
  "Passed! 610" against a real 1635 and looked exactly like success.
- **A GREEN SUITE HIDES BUGS.** Review found a real defect on essentially every slice this session,
  several of which passed a full green suite. **The reviews are not optional.**
- **Run review lenses SEQUENTIALLY.** Parallel lenses die together and lose everything; sequential ones
  journal as they complete. This directly saved 8 findings this session.
- **Pass the COMPLETE finding list to the fix agent, never a summary.** Summarising left four findings
  unaddressed — two of them vacuous-test defects.
- **Agents die constantly** (13 times this session: session limits, process exits, two 529s) **and always
  leave files on disk.** Check `git status` after every death. One left the tree non-compiling.
- **Worktree directories vanish.** `stream-b`'s was deleted outright; its work survived only because it had
  been **committed**. Commit early.
- **Odd-paisa fixtures always.** A ±₹0.50 defect survived this project's whole life under six round-number
  assertions.
- **`isolation:'worktree'` cuts from `main`, not the current branch.** Have A12 create worktrees explicitly
  and verify `CurrentVersion` in each.
- **WebFetch gets 403 from some Tally/NIC hosts and a TLS error from `taxinformation.cbic.gov.in`** — a real
  browser retrieves them, and `curl` fetched the INV-01 schema xlsx directly. State which method you used.
- **Do NOT open `C:\Users\dkpho\Downloads\Tally7.2`** — cracked, and the wrong product. TallyPrime is the
  fidelity target; 7.2 is a checklist only. The 9 obsolete-by-law pre-GST capabilities (VAT, CST, Service
  Tax, Excise) **will not be built** — user ruling, 2026-08-10.

---

## OPEN, NEEDS DECIDING OR SCHEDULING

- **Push / open a PR** — none exists. Body must cover the earlier unpushed Phase 10.7/10.8 commits too.
- **The R9 real-app run.** The app launches cleanly via
  `dotnet run --project src/Apex.Desktop -c Release`. `WarnOnNegativeStock` is the first company flag to
  default **TRUE**, so "column absent" no longer means "default" — only a genuine **pre-v50 company file**
  tests the read path.
- **`memory.md` has not been updated this session** (R5) — deferred to the post-merge documentation slice
  along with `docs/invented-vs-cloned.md`, `docs/tally-fidelity-defects.md` and
  `docs/phase6-advanced-inventory-requirements.md` (PR-8 retirement).
- **CGST Rule 138(14) goods-relief lists are unmodelled** — the engine over-generates e-way bill
  requirements. Pinned by a `PINNED_GAP` test, deliberately shaped so exactly one test fails when the data
  slice lands. Unscheduled.
- `IndianState.All` carries state code 97 but not 96 or 99 — adding them is **unsafe** because
  `Gstin.Validate` shares the list and would accept nonexistent GSTIN prefixes. Needs designing.
- `CostAllocationStrictness` is misnamed for what it now gates (~13 files to rename).
