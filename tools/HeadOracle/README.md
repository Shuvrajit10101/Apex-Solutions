# HEAD-ORACLE HARNESS

The gate on any change to negative-stock valuation. It is built and validated **before** the production
change, and it runs on **every** iteration of that change.

```bash
bash tools/HeadOracle/run-oracle.sh
```

Exit codes: `0` clean · `1` **ENGINE REJECTED** · `2` could not run · `3` **HARNESS BROKEN**
(if it is 3, judge nothing — fix the harness first) · `4` **SANDBOX** (a bite run against a mutated
engine; never a verdict on `src/`).

---

## What the SECOND audit changed (read this before trusting an earlier report)

The reworked harness was audited again and returned **NOT-READY**. The three v1 CRITICALs stayed fixed,
but ten further findings landed. Every one is closed here; the two that mattered most:

**1. Checks 3 and 8 were mutually unsatisfiable on `G6-001`, so the harness REJECTED the exact engine
its own point oracle prescribes.** `G6-001` is `In 10 @ ₹100.13 → Out 25 → physical Count 8`. Check 8
asks "could the money spent have covered the units issued, at a rate this item has actually seen?" On a
book where **25 units were issued but only 10 were ever bought** the answer is *no for every possible
closing value* — and the magnitude **increases** with the closing value. So moving `G6-001` from HEAD's
**₹0** (8 units physically counted on the shelf, valued at nothing: a **wiped asset**, and the actual
defect) to the reference's demanded **₹78.16** raised check 8's magnitude 70,064p → 77,880p and scored
as `WORSENED … FAIL`. **A builder who got the fix exactly right saw REJECTED, and the obvious way to go
green again was to wipe the asset a second time** — the failure mode this whole programme exists to
prevent, and one this project has already shipped once (a positive-quantity floor turned a diagnosable
−₹120 into a plausible ₹0).
→ Each subject's check premise is now tested for **structural satisfiability** before its magnitude is
allowed to mean anything. Unsatisfiable subjects go to a named bucket, printed **in full**, excluded
from the introduced/worsened classification. `bite/accept-probe.sh` then demonstrates that
**ENGINE VERDICT: ACCEPTED is reachable** with a reference-conformant engine — *an oracle whose ACCEPT
state has never been observed is not known to have one.*

**2. The AverageCost "0 disagreements" was a tautology — and CHECK 2 has now been INVERTED.**
`Reference.RunAverage` reproduces HEAD's moving average including the pool reset, so
`RefClosingValuePaisa` for AverageCost used to be an **echo of HEAD**, check 3 excluded AverageCost, and
check 2 byte-**locked** it. The harness was configured so AverageCost could never be shown wrong — and it
*is* wrong: on `G2-004` (`In 5 @ ₹1000.07 → Out 25 → In 20 @ ₹1000.07 → In 30 @ ₹0.37`) HEAD closes at
**₹12,007.50** where the debt-aware answer is **₹11.10** — ₹11,996.40 of phantom asset that no check saw.
→ **2026-07-27, user scope decision: AverageCost IS to be fixed.** A byte-lock to HEAD would forbid the
authorised fix, so **CHECK 2 is now a first-class point oracle** against `RefClosingValueDebtAwarePaisa`,
exactly as CHECK 3 is for FIFO/LIFO. HEAD is CONVICTED there on a clean run (8 subjects).
→ That column was itself validated by **nothing** — CHECK 4 derives its engine twin by stripping the
`Ref` prefix, so `RefClosingValueDebtAwarePaisa` mapped to a measure no engine emits and the lookup
silently `continue`d. **CHECK 4b** now calibrates it: a never-negative book carries no debt, so the
debt-aware value must equal HEAD's AverageCost exactly (71 subjects, 20 scenarios, exit 3 on failure).
→ `Reference.Value`'s AverageCost branch moved to the debt-aware average **in the same change**, because
`RefIssueValue` and `RefTotalClosingPaisa` are derived from it — otherwise CHECK 10 and CHECK 9(b) would
have convicted the very engine CHECK 2 prescribes.
→ **CHECK 4b is not enough, and cannot be made enough** (audit #4 finding [0], CRITICAL). It calibrates only
`FactNeverNegative=1` books — and a never-negative book **never carries a debt**, so on exactly those books
every clause distinguishing `RunAverageDebtAware` from `RunAverage` is **dead code**. The clauses deciding
all of CHECK 2's convictions were validated by nothing, and no amount of further calibration can help:
**HEAD has no correct debt behaviour to calibrate against.** That recursion is terminated by **CHECK 4c**.
→ Round 4 also printed a "REFERENCE INTERNAL CONSISTENCY … 187 subjects, 0 divergences → PASS" gate as
evidence. It was a **tautology** — both columns are `Paisa(RunAverageDebtAware(…) * closingQty)`, the same
pure function with the same arguments — and poisoning that function moved both together while the gate still
printed PASS. It no longer carries a verdict or a census cell; PART A states the identity as a construction
fact and points at CHECK 4c, which anchors **both** columns to external constants.

### CHECK 4c — the hand-derived debt-branch goldens (`Goldens.cs`)

**132 literal expected paisa constants** — 88 closing-value + 44 issue-value — for subjects where the debt
clauses actually fire. Each was (a) **derived by hand**, movement by movement, and written up so a reviewer
can check the arithmetic without trusting any code here; (b) **cross-checked** by an out-of-band Python
replay written from the corpus movement lists alone, sharing no line of code with `Reference.cs` or
`Program.cs`; (c) compared against the C# reference — with any disagreement to be resolved by **hand
arithmetic**, never by picking a side. All 118 agree three ways.

Stated honestly: **this does not make the reference provably right.** It makes it wrong only if a human
derivation and two independent implementations are all wrong *the same way*. That is the terminal state of
this argument, and it is the honest one.

Coverage is **asserted**, not assumed — a shortfall on any of these is exit 3:

* every subject tagged `RefProvenance=INVENTED` must carry **both** a closing-value golden and an
  issue-value golden — a rule nothing calibrates, anchored on the Balance Sheet alone, is exactly the shape
  audit #5 found;
* the `INVENTED` population itself is **derived from the spec** (`Facts.InventedByRule`, a pure quantity
  walk emitted as `FactInventedByRule`) and asserted equal to the emitted tags **in both directions**, so a
  partial retag cannot quietly shrink the population the harness claims to have pinned. Its size is pinned
  as census cell `CHECK4c.inventedSubjects`;
* every `G*` family with any `BRIEF`/`INVENTED` subject must carry at least one golden;
* every clause in `Goldens.RequiredClauses` must be exercised — repayment by a rated inward, across multiple
  lots, by an unrated inward; a count with a debt outstanding; a count after repayment; a debt still
  outstanding at the as-of date; a debt created from an empty stack; two successive over-draws; the
  AverageCost debt path; a **no-debt control** where the clauses must *not* fire; and, on the issue arm, an
  issue under an outstanding debt, an issue after recovery, an issue drawn across a repaid layer, the
  AverageCost issue path, and a FIFO-vs-LIFO ordering control.

### Round 6 — the issue arm, and the constants themselves (audit #5)

**The issue arm.** Round 5's table pinned closing values only, so `RefIssueValue` — the column CHECK 10's
155 convictions come from — had no external anchor on the debt branch. The adversary rewrote 68 of the 120
reported CHECK 10 demands (₹197.75 → ₹7,910.00 on the crux, 40×) with PART A printing SOUND. Two things now
convict that, either alone sufficing: the 44 issue goldens, and a **constant-free structural assertion** —
on any FIFO/LIFO subject a probe at or above the closing quantity must cost **exactly** the closing value,
because units a debt repayment settled went to COGS when it was settled. 738 (subject, probe) pairs sit at
or above on-hand and are pinned as census cell `CHECK4c.issueStructurePairs`. It deliberately does not cover
`AverageCost`, whose issue arm prices at the closing unit rate and is uncapped by design; that arm is pinned
by constants instead.

**The constants themselves.** The census used to pin how *many* goldens there were, never what they *said* —
so editing a constant to match the code, the one thing `Goldens.cs` forbids, was undetectable. Now:
`CHECK4c.goldenDigest` is a census cell computed over the ordered constants, and the comparator parses the
**last rupee figure out of each printed derivation** and requires it to equal the constant ÷ 100, so prose
and constant cannot drift apart silently.

**Coverage of what is actually convicted.** All 74 CHECK 3 convictions and all 8 CHECK 2 convictions are now
directly pinned (round 5: 19 and 5). The block prints the ratio, computed from the run's own rows.

For an `AverageCost` golden **both** reference columns are asserted against the same constant, which is what
makes the retired tautology unnecessary. A failure is a **HARNESS** failure: these constants judge the
oracle, never `src/`.

Also closed: a value-only poison of the reference's debt branch that passed every PART A assertion (now
convicted by the **reference value invariant**); `evaluated > 0` assertions that missed a 332 → 134
collapse (now the **census gate**, pinned both against a recorded census and against the head arm, cell
by cell); `ReadTsv` silently dropping 60 duplicate rows per run; bite reports that printed a passing
provenance line while measuring a mutated engine; corpus gaps (invoice-borne stock lines, batches,
compound units on a negative book, cancelled vouchers); an unbounded check-6 tolerance at micro
quantities; `G9-001`'s missing at-on-hand probe; and `E1` being outside byte identity and calibration.

---

## Why this exists

Three previous attempts at negative-stock valuation each **passed the entire test suite** (3505, 3520,
3529 green) while overstating the Balance Sheet by an **unbounded** amount:

- once valuing 1 unit at ₹100,100 on an item whose only rate was ₹100;
- once inflating Stock-in-Hand to ₹476,000 on an item with ₹26,100 ever spent (18× more asset than was
  ever purchased);
- once making an "unreachable" defensive arm reachable on a **never-negative** book (FIFO ₹130 → ₹211.25).

A green suite proved nothing, three times. Two of the three were caught only because a reviewer built an
oracle by hand.

Then **v1 of this harness was itself adversarially audited and returned NOT-READY** — 3 CRITICAL, 5 HIGH,
5 MEDIUM, 1 LOW, each CRITICAL proven by running a mutated engine through the real harness and getting
CLEAN. What follows is the rework.

---

## THE HEADLINE: absolute bands are not enough

`G1-001`, the scenario the corpus labels **THE CRUX**, is `In 10 @ ₹100.13 → Out 25 → In 40 @ ₹7.91`.

- HEAD reports **25 units @ ₹316.40**.
- FIFO-correct is **25 × ₹7.91 = ₹197.75**.

A **60% overstatement** — and it appeared in **none** of v1's violation lists, because the rate band
`[₹7.91, ₹100.13]` is **12.7× wide** and ₹316.40 sits comfortably under the ₹1,317.70 ever spent.

**A band check cannot convict a wrong-but-plausible value.** Hence the point oracle.

---

## The point oracle (`Reference.cs`) and how it earns trust

`Reference.cs` is an independent, textbook cost-layer implementation computed **only from the scenario
spec**. It references nothing from `Apex.Ledger` — not a type, not an enum, not a service; costing
methods are plain strings. If it ever compiled against the engine it would stop being an oracle and
become an echo.

### The calibration gate (check 4, hard-fail)

> On every **never-negative** scenario × all **6** costing methods × every as-of date, the reference
> must equal **HEAD exactly**. HEAD is trusted on never-negative books — that is the premise of the
> byte-identity check. **If the reference disagrees there, the reference is wrong: fix the reference,
> never the engine.**

Only once that gate is green does the reference become the oracle on the `G*` books, where HEAD is not
trusted. The report prints the calibration population every run (currently **2,946 subjects across 20
scenarios** and all 6 methods).

"Never-negative" is a **spec-derived predicate** (`FactNeverNegative`), not the letter `N`: it means no
`(item, godown, batch)` on-hand ever goes negative *and* the company-wide layer stack never drains into
a debt, anywhere in the scenario's history. Scoping by the letter left `E1` (`10 + 20 − 15 = 15`, never
negative) outside **both** byte identity and calibration, so the reference was used as an oracle on `E1`
having never been calibrated there, and a change perturbing the equal-date tie-break identically in both
`E1` scenarios would have tripped neither. `E1-001`/`E1-002` are now inside both.

### What calibration CANNOT validate — and how the report says so

Calibration only validates the code paths an `N*` book reaches, and **the debt branch is by construction
not one of them**. Every `Ref*` subject therefore carries a `RefProvenance` tag, tallied per family in
PART A:

| tag | meaning |
|---|---|
| `CALIBRATED` | only paths an `N*` book reaches; check 4 asserts these equal HEAD exactly |
| `BRIEF` | a debt repaid by a **rated** inward — stated verbatim in the rework brief, but no `N*` book reaches it |
| `INVENTED` | a rule **no calibrated path reaches**: a physical count taken with a debt outstanding, or a debt settled by an inward carrying no purchase rate |

`ECHO-OF-HEAD` **was retired on 2026-07-27** (audit #4 finding [3]). It was applied unconditionally to all
187 `AverageCost` subjects and became false the moment `Reference.Value`'s AverageCost arm moved to
`RunAverageDebtAware` — at which point that column started issuing CHECK 2's *engine verdicts* while the
census still described it as "not a reference at all". It also kept the AverageCost subjects resting on the
settled rule below **out of** the `INVENTED` count, so ratifying the rule for FIFO/LIFO would silently have
ratified live AverageCost convictions too. AverageCost is now tagged from the **same debt flags** as the
layer methods, recorded by `RunAverageDebtAwareTraced` as it replays.

**`INVENTED` is now SETTLED POLICY, decided by the user on 2026-07-27.** A debt settled by something that
carries no purchase rate — an unrated inward, or a physical count taken while a debt is outstanding — is
valued by the **engine's own existing best-available-cost chain** (`CostContext.NoRateInwardCost`: running
average → `StandardCost` → last rated inward → 0). Nothing is invented; the reference applies the rule the
engine already applies to an unrated inward. HEAD diverges by using the running average *alone*, which is 0
straight after an over-draw, so HEAD values genuinely-held units at ₹0.00.

The consequence is **computed, never asserted in prose**. PART A's `SETTLED POLICY` block reads
`RefClosingValuePaisa` / `RefClosingQtyMicro` off the emitted rows of *that run* and prints the derived
per-unit rate for every `INVENTED` subject — and an `INVENTED` subject that cannot be named is itself a
harness failure. (Round 4 printed the sentence as a string literal; a poisoned reference demanded
`8 × ₹100.13` while the same document kept asking the user to ratify `8 × ₹9.77`.)

### The reference VALUE invariant (PART A)

Self-consistency binds the reference's **quantity**. Nothing bound its **value**, and the audit proved
the gap: setting the surviving remainder of a repaying lot to `unit = 0` leaves every quantity untouched
(self-consistency passes), never touches an `N*` book (calibration passes), prints
`HARNESS INTEGRITY : SOUND` — and demands **₹0** on the crux where the pristine reference says ₹197.75.

So every FIFO/LIFO subject now emits its **layer breakdown** (`qty@rate`), each layer's **rate source**,
and the **admissible rate set** the spec permits, and the *comparator* — separate code from the
arithmetic it audits — asserts that the layers sum to the reported quantity, that they sum to the
reported value, and that **every layer rate is one the spec actually contains**. A rate produced by a
running-average *blend* is legitimately outside that set and is reported in its own bucket rather than
convicted. `bite/hbite-04-value-only-reference-poison.sh` reproduces the poison: **68 lot-origin/wrong-rate**
failures plus 4 genuinely inadmissible rates, exit 3, while calibration and self-consistency both still
print PASS. (Round 7 corrected the wording here: all 68 used to be described as "inadmissible rates",
which is not the bucket the harness reports them in.)

### The semantics the reference implements

Cost layers, plus one rule the textbook does not cover because over-drawn stock is not in the textbook:

1. An outward that asks for more than the layers hold takes everything there is, and the **shortfall
   becomes a debt quantity**. A debt carries no rate — nothing has been bought to cost it.
2. A later inward **repays the debt first, at that lot's own rate**. Repaid units never become a layer:
   they were already issued, so their cost belongs to COGS, not to the Balance Sheet.
3. **An existing debt is never re-rated.** This is the rule whose absence produced the 18× error.
4. Invariant: a debt can only exist while the layer stack is empty, so net book quantity is always
   `Σ layers − debt` and at most one of the two is non-zero.
5. A **physical count supersedes the book**: the debt is written off and the stack is reconciled to the
   counted quantity, with topped-up units costed by the engine's own best-available-cost chain
   (running average → standard cost → last rated inward → 0). HEAD uses the running average alone, which
   is 0 after an over-draw, so HEAD values those real units at ₹0. On a never-negative book the running
   average is positive and the two rules coincide — which is why this cannot break calibration.
6. An issue larger than the layers hold costs only what the layers hold. Same as HEAD.

`Reference.RunAverage` remains HEAD-aligned, including its reset-when-the-pool-empties rule, but it is
now used **only** for the `LastPurchaseCost` fallback (which the engine does too). `AverageCost` itself
routes through `RunAverageDebtAware`.

### AverageCost: no longer an echo (2026-07-27)

`RefClosingValuePaisa` for `AverageCost` **used to be** HEAD's own number, so its "0 disagreements" on
every `G*` family was a tautology carrying no correctness evidence: check 3 excluded `AverageCost` and
check 2 byte-locked it, and nothing in the harness could ever convict it. The user has since decided
AverageCost is to be FIXED, so the whole AverageCost path — closing value, issue value and company total
— is now the debt-aware reference, **CHECK 2 convicts against it**, and **CHECK 4b calibrates it**.

It is, in fact, wrong. `RefClosingValueDebtAwarePaisa` applies the same debt semantics to the moving
average — an over-draw is a debt, a later inward repays it at its own rate and only the surplus joins
the pool, a count writes the debt off — and the report prints the gap per family. HEAD **resets** the
pool at the over-draw and re-averages every later inward, so the sign of its error is the sign of the
rate trend across recovery lots:

| subject | HEAD | debt-aware | gap |
|---|---|---|---|
| `G2-004/Widget/2024-04-25` | ₹12,007.50 | **₹11.10** | +₹11,996.40 (+108,076%) |
| `G6-001/Widget/2024-04-20` | **₹0.00** (8 counted units) | ₹78.16 | −₹78.16 (−100%) |
| `G2-001`, `G2-002` (cheap→dear) | understates | | −12.92% |
| `G2-003` (dear→cheap) | overstates | | +32.87% |

Six of 184 AverageCost subjects disagree. **This block never fails the run** — whether AverageCost is
fixed is the user's scope decision — but shipping "FIFO/LIFO fixed, AverageCost deferred" is now
visibly a *knowingly accepted unbounded overstatement on the default costing method*, not a claim that
AverageCost is fine.

---

## The 11 checks

| # | check | fails when |
|---|---|---|
| 1 | byte-identity on every **never-negative** scenario (spec-derived scope), live vs head | any diff |
| 2 | **point oracle**: live `AverageCost` `ClosingValue` == `RefClosingValueDebtAwarePaisa` (INVERTED 2026-07-27; it was a byte-lock to head, which would have forbidden the authorised fix) | any diff |
| 3 | **point oracle**: live `ClosingValue` == calibrated reference (FIFO/LIFO, all families) | any diff |
| 4 | **calibration**: reference == head on all never-negative × all 6 methods | any diff → **harness broken** |
| 5 | **quantity oracle**: `ClosingQty` and `OnHand` == spec-computed | any diff |
| 6 | closing implied unit rate inside the band of rates actually paid | introduced, **or worse in magnitude** than head's |
| 7 | `ClosingValue` ≤ spend ceiling | as #6 |
| 8 | implied COGS/unit inside the band of rates paid | as #6 |
| 9 | `TotalClosingStockValue` == Σ per-item closing == reference total, and subject to #6–#8 | any diff |
| 10 | `IssueValue` == reference issue value | any diff |
| 11 | **exception asymmetry**: `EXC:` on one arm where the other has a value | always |

Plus, in PART A (harness integrity): **row-set symmetry**, **emitted-row accounting**, corpus integrity,
reference self-consistency, the **reference value invariant**, the **provenance census**, and the
**census gate**.

### The census gate — `evaluated > 0` was never enough

A check that quietly evaluated *much less* is as dangerous as one that evaluated nothing. Make the
engine refuse the voucher at posting time — the "just add the guard" change — and `Corpus.Build` throws
for every `G*`/`E1` scenario, `Emit` skips them, and the point oracle (which iterates the **live** arm's
keys) neither evaluates nor faults the missing rows. Check 3 went **332 → 134 subjects and printed
PASS**; checks 5, 9 and 10 passed; checks 6/7/8 printed `live 0/0` for every `G` family and still passed,
because the assertion only fired when the whole-arm sum was zero.

Two independent pins, both **exit 3** (the oracle has lost coverage — judge nothing):

1. **RECORDED** — the head arm's counts must equal the census recorded in `Census.cs`. This catches a
   corpus or emitter regression that shrinks **both** arms identically, which no head-vs-live comparison
   can see. Re-recording is a deliberate edit to a source file; the procedure is in `Census.cs`.
2. **LIVE vs HEAD** — cell by cell, including every `(check, family, method)` triple for checks 6/7/8.

`bite/bite-12-census-collapse.sh` reproduces the exact collapse: `subjects evaluated : 134`, 189 shrunken
cells, 5,544 rows named as missing, `HARNESS INTEGRITY : BROKEN`, exit 3.

### Structural satisfiability (checks 6/7/8)

Before any magnitude is compared, each subject's check **premise** is tested: is there *any* feasible
value that would satisfy this check on this subject? For checks 6 and 7 there always is (computed, not
assumed). For check 8 there is **not**, whenever more units were issued than were ever bought — and
because its magnitude then rises with the closing value, comparing arms points the wrong way. Such
subjects are classified `STRUCTURALLY-UNSATISFIABLE`, excluded from introduced/worsened, and printed in
full with their numbers. This is what makes `ENGINE VERDICT: ACCEPTED` reachable at all.

Violations of 6/7/8 are keyed by **(check, subject) and compared by MAGNITUDE**, with head and live
printed side by side. v1 discarded any live violation whose key already existed at head, regardless of
size, and certified a mutation producing ₹100,000 of phantom stock as CLEAN.

A head violation counts as **RESOLVED only if the live arm produced a real value there**. An engine that
throws is never credited with fixing anything.

### Three subtleties worth knowing

- **Checks 7 and 8 no longer stand down** on physical-count / unrated-inward scenarios (audit H1). The
  spend ceiling **imputes** the dearest rate signal the item has for units nobody bought, which is still
  a hard upper bound; subjects using it are marked `[imputed]`.
- **Check 8 uses an interval, not a point.** True spend is only known to lie in
  `[rated spend, spend ceiling]`, so it convicts only when the whole implied-COGS interval falls outside
  the rate band. Using the ceiling alone produced a false positive on `N5` — a never-negative book the
  engine values correctly — and a check that fires on correct behaviour trains readers to ignore it.
- **Check 7 cannot be demonstrated in complete isolation.** Holding more asset than was ever bought
  forces implied COGS negative, so check 8 necessarily fires with it. Measured, not assumed.

---

## Corpus

`N1`–`N9` never-negative (the trusted side), `G1`–`G15` over-drawn (the side under test), `E1` ordering
determinism. Deterministic throughout: no clock, no RNG, no `Guid.NewGuid()` for vouchers, every voucher
number pinned so the spec-side reference can reproduce the engine's `(Date, PhysicalLast, Number, Id)`
order. **Every rate and quantity is odd-paisa** — a ±₹0.50 print defect once survived this project's
entire life underneath six assertions that all used 1,180 / 1,300 / 5,900.

Shapes the audit found missing and that are now present:

- `G9` — the classic real shape: **positive company-wide, negative in ONE godown**. The engine's
  valuation replay is company-wide while its on-hand replay is per `(item, godown, batch)` key; `G9`
  pins that asymmetry, and any fix keyed off "the item went negative" rather than "the company-wide
  layer stack went dry" will change it.
- `N2-003`, `G10-001`, `G10-002` — **multi-item**, so `TotalClosingStockValue` accumulates for real.
  `G10-001`'s items are wrong in opposite directions, so the aggregate cannot be right by cancellation.
- `G1-004`, `G3-002`, `G10-002` — **opening balances on negative books**.
- `G1-003`, `G2-002`, `G8-003` — **issues AFTER recovery**, so post-recovery COGS is exercised. Without
  these, a change with a correct Balance Sheet and a wrong P&L ships clean.
- `N8`, `N9` — two-godown never-negative books, so the multi-key on-hand replay is calibrated too.

Shapes the **second** audit found missing, added here (all additive — they only add subjects):

- `G11` — **the over-draw arrives on a SALES INVOICE**. Every earlier `G` family built its over-draw
  from a Delivery Note, yet in the shipped app negative stock overwhelmingly arises from sales
  invoicing, and `StockValuationService.MovementEvents` merges item-invoice stock lines explicitly — a
  path no scenario reached. Post order matters and the movement list *is* the post order:
  `LedgerService.Post` runs the company-wide no-negative guard for any voucher carrying item lines, so
  the invoice is posted while the book is still clean and the guard-bypassing Delivery Note that
  retro-drives it negative is added afterwards. `G11-002` puts the *recovery* on a Purchase invoice, so
  both directions of the seam are exercised.
- `G12` — **batches**. Every allocation used to pass `batchLabel: null`, but `InventoryLedger.Key` is
  `(item, godown, BATCH)` while valuation is batch-blind. `G12-001` is the batch analogue of `G9`
  (negative in one batch, positive company-wide — it **must not move**); `G12-002` recovers into a
  *different* batch from the one that was over-drawn.
- `G13` — **compound units on a negative book**. `Doz-Nos` appeared only in `N7`, so
  `RateInBase` × debt-repayment arithmetic — the exact shape of a 12× understatement — was untested.
  `G13-002` uses a rate that does not divide exactly by 12.
- `G14` — **a cancelled voucher and a post-dated voucher**. A cancelled voucher must contribute nothing
  to on-hand or valuation in either engine; if it ever counted here, on-hand would read −25 instead of
  +25 and check 5 would fire. Post-dating reduces to the same date bound in both engines, so the
  post-dated recovery lot must behave exactly like a plain one — asserted, not assumed.
- `G15` — **the only book where FIFO and LIFO genuinely disagree** (audit #6 [2], round 7). Every other
  debt scenario leaves exactly **one** surviving layer, and a single layer has no oldest and no newest,
  so FIFO and LIFO produced identical closing *and* issue values on all 76 debt subjects and the LIFO
  debt path could not be exercised independently of FIFO at all — `Reference.Consume` differs between
  the methods in one place (index `0` vs `Count-1`) and swapping them moved no golden. That is a live
  risk for the production slice, which must be verified on LIFO. `G15-001` is the smallest book that
  separates them: a debt created and repaid, **two** surviving layers at different rates
  (`25 @ ₹7.91`, `20 @ ₹12.07`), then an outward of 13 — the only event that consults an end of the
  stack. FIFO closes at **₹336.32**, LIFO at **₹282.24**; the issue probes split ₹9.89/₹15.09 and
  ₹282.24/₹336.32. Fourteen hand-derived constants pin it (`GT-61/61L/62/62L`, `GI-35…GI-44`).
  `bite/wrong-02-lifo-swapped-on-debt-books.sh` measures the gap it closes: a FIFO/LIFO swap confined
  to debt books would have passed the **entire** harness before, and is now convicted 14 times — every
  one of them on `G15-001`.

Two probe defects the audit found are also fixed: `G2-002` listed `1.25` **twice** (60 emitted rows
vanished on every run and the two printed row counts disagreed), and `G9-001`'s "exactly at on-hand"
probe read `23.5` on a book whose on-hand is `23.25`, so the one family pinning the
company-positive/godown-negative asymmetry had no at-on-hand probe at all.

`G1-001` is deliberately frozen: it is THE CRUX and the rework brief quotes its numbers.

---

## Provenance — asserted, not printed

Every run:

1. verifies `.oracle-baseline/manifest.sha256`;
2. verifies the baseline **file count and sorted path list** — a manifest cannot see a file that was
   *added*, and the negative-stock fix is likely to arrive as a new file (audit H5);
3. verifies the recorded commit pin;
4. **normalises line endings to LF in both arms** before comparing anything. `git archive` writes LF
   while the working tree carries CRLF on some files; four of 291 files differed for that reason alone,
   which made v1's provenance claim false on day zero (audit M1);
5. computes a **whole-tree digest** (per-file hash + sorted path list) for each arm and **asserts**
   head arm == pristine baseline and live arm == working `src/` — non-zero exit otherwise (audit H4);
6. greps each build log to prove the arm compiled **its own** engine copy.

`$ApexLedgerProject` is **unset** at start and passed **explicitly** as `-p:ApexLedgerProject=<abs>` on
every build (audit C1): a command-line property beats the environment. With the variable set and no
override, MSBuild really does silently compile a foreign engine — verified, not assumed.

**Honest caveat:** the pin proves the *snapshot* is intact. It does not prove the commit it came from was
innocent. The script runs **no git at all** (R4); `.oracle-baseline/` is materialised once by A12.

---

## Proving the checks bite

A check that has never been observed failing is not known to work.

```bash
bash tools/HeadOracle/bite-test.sh    <label> tools/HeadOracle/bite/bite-NN-*.sh    # mutate the ENGINE
bash tools/HeadOracle/harness-bite.sh <label> tools/HeadOracle/bite/hbite-NN-*.sh   # mutate the HARNESS
```

| mutation | proves |
|---|---|
| `bite-01` | check 1 — `N*` byte identity |
| `bite-02` | check 2 — the `AverageCost` **point oracle** (poisons the average pool reset, which only fires on `G*`) |
| `bite-03` | **check 3** — a 3% understatement invisible to checks 6/7/8 on 52 of 56 subjects |
| `bite-04` | check 5 — quantity 0 / value 0: a real asset leaves the Balance Sheet |
| `bite-05` | check 6 — the ₹100,100-for-one-unit shape |
| `bite-06` | check 7 — ₹90.11/unit: inside the band, above total spend |
| `bite-07` | check 8 — ₹48.03/unit: inside the band, under spend, COGS below the cheapest rate ever paid |
| `bite-08` | check 9 — 7 paisa of phantom aggregate with every per-item figure correct |
| `bite-09` | check 10 — perfect Balance Sheet, 50%-wrong P&L |
| `bite-10` | check 11 — the throw-everywhere engine (audit C3) |
| `bite-11` | audit C2 — existing violations made 100× worse must be convicted, not deduped away |
| `bite-12` | **the census gate** — "just add the guard": 332 → 134 subjects, previously PASS, now exit 3 |
| `hbite-01` | reference self-consistency (a bug calibration cannot see) |
| `hbite-02` | "a check that evaluated nothing FAILS" (audit H1) |
| `hbite-03` | the calibration gate itself — a poisoned reference is named as the wrong party |
| `hbite-04` | **the reference VALUE invariant** — a value-only poison that passes calibration AND self-consistency |
| `hbite-05` | **a duplicate emitted key is fatal** — the collision that silently dropped 60 rows per run |
| `hbite-06` | **CHECK 4b** — the debt-aware AverageCost oracle poisoned: 148 of 184 magnitudes rewritten, defects invented on never-negative books, and PART A used to print SOUND |
| `hbite-07` | **the ORIGIN BINDING** — a re-rating poison that uses an *admissible* rate (₹2,503.25 demanded on the crux where the brief says ₹197.75). Self-consistency, both decompositions, CHECK 4, CHECK 4b and "INADMISSIBLE layer rates" all read 0 in that run; only the lot lookup convicts |
| `hbite-08` | **self-attestation is not evidence** — the same poison plus a `RunningAverage` tag on the layer it corrupts, which used to waive the rate test entirely. Convicted twice over |
| `hbite-09` | **the BUILD OUTCOME gate** — a scenario that does not construct, symmetrically on both arms. CHECK 11 still prints PASS beside it |
| `hbite-10` | **the STRUCTURAL COVER assertion** — a family in the checks-6/7/8 exclusion bucket left with no point oracle. Checks 6, 7 and 8 all print PASS over it |
| `hbite-11` | **CHECK 4c** — the AverageCost debt-repayment clause poisoned (`add -= repay` dropped). CHECK 4 **PASS**, CHECK 4b **PASS**, and the poisoned reference demands *exactly HEAD's* ₹12,007.50 on `G2-004`, so CHECK 2 stops convicting the phantom asset and it is **acquitted**. Only the golden `GT-07 = 1110p` convicts, and it does — `FAIL GT-07 … = 1110p`, reference 1200750p |
| `hbite-12` | **the count-up exemption** — a count-up taken with a debt outstanding re-priced at an *admissible* rate. Value invariant **0/0/0/0**, CHECK 4 **PASS**, CHECK 4b **PASS**, and the crux moves 7816p → 80104p (10.25×). Only `GT-11/GT-11L/GT-12` convict |
| `hbite-13` | **the ORDERING assertion** — the drained lot's units resurrected after repayment, every layer *truthfully* bound to a real lot at that lot's real spec rate, total quantity preserved. `0 qty, 0 value, 0 lot-origin/wrong-rate, 0 inadmissible-rate` — and **66 ordering** failures |
| `hbite-14` | **the AGGREGATE per-lot bound** — an over-claim split across layers from one lot, each half inside the lot size. 69 aggregate failures, including `G9-001`/`G9-002`/`G12-001`, which CHECK 4 cannot reach and the ordering rule does not constrain |
| `hbite-15` | **CHECK 4c coverage** — a golden quietly deleted, leaving an `INVENTED` subject pinned by nothing. Every remaining golden still passes; the coverage assertion convicts |
| `hbite-16` | **CHECK 4c issue arm** — audit #5's own mutation: once a debt has existed, price the issue at the debt-aware pool average instead of walking the layers. CHECK 4 **PASS**, CHECK 4b **PASS**; 9 issue goldens and 78 structural failures convict, including `G1-001 Fifo @1000 = 791000p` against a ₹197.75 stack |
| `hbite-17` | **the forbidden shortcut** — the count-up reference poison PLUS every `G6-001` constant *and its printed derivation* edited to agree with it. CHECK 4c prints `mismatches : 0`, coverage 0, prose 0 — and the **only** thing that fires is `CHECK4c.goldenDigest` |
| `hbite-18` | **partial retag** — `CountWithDebtOutstanding` stops being raised. No number moves; `G6-001`'s three subjects silently fall from `INVENTED` to `BRIEF`. The spec-derived population convicts it 3 ways |
| `hbite-19` | **the clause LABELS** (audit #6 [1], round 7) — `GI-27` re-tagged from `issue:no-debt-control` to `issue:debt-outstanding` on `G9-002`, a book with no debt at all. No constant is touched, so CHECK 4c prints `mismatches : 0`, `INVENTED subjects with no golden : 0`, `debt families with no golden : 0` and `required debt clauses not exercised : 0` — every pre-existing gate stays silent, exactly as it did before round 7. Only `labels that are FALSE of their subject : 1` convicts, naming `GI-27` and quoting `FactDebtShape = none` |
| `wrong-01` | **an ENGINE bite, not a harness one** — the ACCEPTED engine plus one defect: a positive-quantity floor that wipes any book that ever ran short to **₹0**. Quantity stays right so CHECK 5 passes, and ₹0 is under every absolute ceiling; CHECK 3 (76), CHECK 9(b) (68) and checks 6/7/8 convict |
| `wrong-02` | **an ENGINE bite** — the ACCEPTED engine with FIFO/LIFO **inverted on debt books only**. Invisible on every never-negative book, and invisible on every debt book before `G15-001`. 14 convictions, **every one on `G15-001`** — the measurement of what audit #6 [2] was about |
| `accept-probe` | **that `ENGINE VERDICT: ACCEPTED` is reachable at all**, now for a **three-method** reference-conformant engine (see below) |

### `accept-probe` — the ACCEPT-state gate

*An oracle whose ACCEPT state has never been observed is not known to have one.* Before the check-8
structural-satisfiability fix, the harness had **no reachable ACCEPT state**: checks 3 and 8 contradicted
each other on `G6-001`, so the engine the point oracle prescribes was rejected. `accept-probe.sh`
rewrites the **sandbox** copy of `StockValuationService.cs` to implement the reference's debt semantics
and runs the real comparator. All 11 checks PASS, `HARNESS INTEGRITY : SOUND`, `ENGINE VERDICT :
ACCEPTED`, and the report states `the verdict above, taken alone, would exit 0`. It is **not** a fix and
it never touches `src/`.

### Bite reports are stamped and can never exit 0

A bite driver used to pass the mutated tree's own digest into both the "live arm" and "working tree"
slots, so the comparator's equality held and the report printed *"provenance assertion: live arm IS the
working tree ⇒ PASS"* — a self-certifying document, formatted exactly like a real verdict, asserting a
mutated engine's provenance. Drivers now pass the literal sentinel `BITE-MUTATED`; the comparator stamps
`*** BITE TEST — MUTATED ENGINE — NOT A VERDICT ON THE WORKING TREE ***` as the report's **first line**,
makes **no** provenance claim, and **exits 4 rather than 0** even when every check passes. A genuine
conviction still surfaces as 1 or 3, so a bite that bites still reads as a bite.

Every mutation is applied to a **private third copy under `.oracle-work/`** (git-ignored, wiped every
run). `src/` and `tests/` are never touched, at all, for any reason — a previous agent on this project
left a mutation on disk as the final state and reported it as a working fix, the costliest failure this
project has had. Keeping mutations out of the tree makes that structurally impossible rather than merely
forbidden.

`bite/_patch.py` refuses to apply a replacement that does not match **exactly once**, and both drivers
abort if the mutation changed no bytes — a no-op mutation proves nothing.

---

## Reading the verdict

`HARNESS INTEGRITY: SOUND | BROKEN` and `ENGINE VERDICT: ACCEPTED | REJECTED` are separate lines on
purpose. **BROKEN means the report says nothing about the engine** — an oracle that can silently drift
produces a confident wrong equality, which is worse than no oracle.

**At the baseline commit, with `live == head`, the expected result is exit 1: HARNESS SOUND, ENGINE
REJECTED**, with checks 3, 9(b) and 10 failing. That is not a defect in the harness — it is the harness
convicting the negative-stock defect it was built to gate. The `HEAD vs REFERENCE` table in the report is
the rupee-by-rupee statement of that defect, and the fix must drive every one of those numbers to zero
without moving anything under checks 1, 2 or 5.
