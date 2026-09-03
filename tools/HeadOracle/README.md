# HEAD-ORACLE HARNESS — HANDOVER

**Status: the negative-stock valuation work was STOPPED by the user on 2026-07-29 after eight attempts.
`src/Apex.Ledger/Services/StockValuationService.cs` has been reverted to HEAD byte-for-byte. This harness
and the eight measured failure modes below are what was banked. Nothing is in flight.**

This document is written for a session that has never seen this work. Read it before touching stock
valuation. In particular, read **[The eight measured failure modes](#the-eight-measured-failure-modes)** —
they cost eight review rounds to find, every one of them passed the full test suite, and a ninth attempt
that does not already know them will rediscover them one at a time.

---

## What the harness is for

The negative-stock defect is a **valuation** defect, not a crash: the numbers are wrong, the app runs
fine, and every existing test passes. An ordinary test suite cannot gate that, because a wrong number
that nobody has written down looks exactly like a right one.

So the harness does three things at once:

1. **It replays two engines side by side.** A pristine HEAD arm (from `.oracle-baseline/`, pinned to
   commit `9c2bded`) and a live arm copied out of `src/Apex.Ledger` *at run time, never cached*. Every
   scenario × item × method × as-of date is emitted to a TSV from both arms.
2. **It carries an independent reference implementation** (`Reference.cs`) — a second, hand-written
   valuation model that shares no code with the engine. Where the reference is a validated oracle, the
   engine is compared against it point-wise and convicted on disagreement.
3. **It audits itself.** The reference is calibrated against HEAD on books where the debt clauses are
   dead code, pinned by hand-derived goldens where they are not, and its coverage is recorded cell by
   cell so a shrinking corpus cannot masquerade as a pass.

The separation of `HARNESS INTEGRITY` from `ENGINE VERDICT` is the whole design. An oracle that can
silently drift produces a confident wrong equality, which is worse than no oracle.

---

## How to run it

```bash
bash tools/HeadOracle/run-oracle.sh
```

Run it from anywhere; it resolves its own paths. It needs `dotnet` on `PATH`
(`export PATH="$HOME/.dotnet:$PATH"`). It runs **no git at all** — the pristine arm is proved pristine by
a recorded hash manifest plus a sorted path list, not by a git command. It never builds inside `src/` or
`tests/`; all output lands in the git-ignored `.oracle-work/`, and the full report is
`.oracle-work/report.txt`.

| Exit | Meaning |
|---|---|
| `0` | CLEAN — harness sound **and** the engine under test accepted |
| `1` | **ENGINE REJECTED** — the harness is sound and it convicts the engine |
| `2` | harness could not run (IO, build, provenance, usage) |
| `3` | **HARNESS BROKEN** — the oracle cannot be trusted; judge nothing until it is fixed |
| `4` | **SANDBOX** — a `bite/` run against a deliberately mutated engine; never a verdict on `src/` |

`3` outranks `1`. If the harness is broken the report says nothing whatsoever about the engine.

---

## What it reports right now, and why that is correct

At the current commit, with `src/` reverted so **live == head**, the expected and actual result is:

```
HARNESS INTEGRITY : SOUND
ENGINE VERDICT    : REJECTED        (exit 1)
    CHECK 2:    12 AverageCost closing values disagree with the debt-aware reference on single-key books.
    CHECK 3:    82 closing values disagree with the reference on single-key books.
    CHECK 10:  192 issue values disagree with the reference on single-key books.
    CHECK 9(b): 86 company totals disagree with the reference on all-single-key scenarios.
```

**This is not a harness defect. It is the harness convicting the defect it was built to gate.** HEAD
mis-values negative stock; the reference says so; the run fails. Every engine-vs-engine check (1, 1M, 11,
6/7/8, ROW-SET) passes trivially in this state because both arms are the same engine — they only become
meaningful once a candidate engine is dropped into `src/`.

The `HEAD vs REFERENCE` table in the report is the rupee-by-rupee statement of the defect. A real fix must
drive those four numbers to zero **without moving anything under checks 1, 1M or 5.**

---

## The eight measured failure modes

Eight attempts, eight rejections. Every one of these **passed the entire test suite** at the time it was
found; most were caught by a review probe, not by a test. Figures are as measured, with the book that
reproduces them. Sources are the review reports kept in the session scratchpad and, for mode 8, the probe
output in `a10r11b/probe-live.txt` and `probe-head.txt`.

### 1 — A "defensively unreachable" arm that was reachable on a never-negative book
The `LayerValue` arm the design called unreachable was reached on a book that never went short, via the
item-level/per-key desync (see [pre-existing defects](#two-pre-existing-defects-found-along-the-way)).
**FIFO Rs 130 → Rs 211.25.** The gate was green at 3505 tests.

### 2 — AverageCost debt is never repriced when the replenishing lot lands
On the moving-average path an uncovered draw is expensed and *nothing reprices that debt* when the inward
that covers it arrives. A StandardCost does not remedy it.
Book: sell 1,000 with nothing on hand, then buy 1,001 @ Rs 100.
**1 unit valued at Rs 100,100** (HEAD and FIFO both say Rs 100). Mirror case: 20 units of genuine Rs 240
→ **Rs 0.00**. Gate green at 3520.

### 3 — A second over-draw re-rates the whole debt at the newest movement's rate
`layers.Clear(); layers.Add(new Layer(-(outstanding + remaining), refRate(date)))` discards the existing
debt layer's own rate, and the reconcile arm then multiplies that rate across the entire positive holding.
Book: one 5-unit delivery note on an item with Rs 26,100 ever spent; a single 1-unit Rs 1,000 purchase
became the rate for 476 units. **Stock-in-Hand Rs 24,050 → Rs 476,000** — 18× more asset than was ever
purchased. Gate green at 3529.

### 4 — A forward-looking cost chain: a later purchase re-prices a count that preceded it
The count-up arm resolved its unit cost from a `CostContext` built over the **whole as-of window**, so
units a physical count created were priced at a *future* rate. Unbounded, and it restates closed periods.
Book (FIFO, no StandardCost): `In 10 @ Rs 100.13 (5 Apr) → Out 25 (10 Apr, debt 15) → Count 8 (15 Apr) →
In 1 @ Rs 1,000,000.03 (18 Apr)`; on-hand 9, ever spent Rs 1,001,001.33.
**Closing Rs 9,000,000.27 on 9 units** — Rs 7,999,199.20 of phantom asset, 9× everything ever spent.
HEAD said Rs 1,000,000.03; honest is Rs 1,000,801.07. As-of instability on the same shape: the same
8 counted units are **Rs 801.04 on a 16-Apr Balance Sheet and Rs 40,004.40 on a 20-Apr one.**
*(`sa-lens1.md`)*

### 5 — Never-negative books repriced; export bytes and persisted rates move with them
The running average is 0 whenever the layer stack is **empty**, which happens on an ordinary guard-legal
**drain to exactly zero** — no over-draw needed — so the chain fired on books the slice promised not to
touch.
Book: `In 10 @ Rs 13.07 (5 Apr) → Out 10 (10 Apr) → Count 5 (15 Apr)`; minimum per-key on-hand across all
dates = 0, i.e. nothing is negative anywhere.
**HEAD Rs 0.00 → Rs 65.35** on Fifo/Lifo/AverageCost, and `TotalClosingStockValue` — the Balance-Sheet
Stock-in-Hand line *and* the P&L closing-stock credit — moves with it, so reported Net Profit changes on a
book that never went short. With StandardCost Rs 9.77: **Rs 0.00 → Rs 48.85**.
Downstream: Stock-Summary CSV **93 B `48ECD030D8C61568` → 104 B `938D96E6E7A0B34A`**; XLSX
`FA43946C67ADA312` → `71A138909D9F07F0`; persisted manufacturing consumption rate **Rs 0.00 → Rs 13.07**
and finished-good inward rate **Rs 0.00 → Rs 26.14**. *(`sa-lens2.md`, `sa-lens3.md`)*

### 6 — Item-level debt wipes a guard-legal, never-negative multi-godown book to Rs 0.00
The debt is created from the **item-level** flattened replay while the guard and `InventoryLedger` work per
`(item, godown, batch)`. A per-key-legal count truncates the merged stack below what other godowns hold, a
per-key-legal outward over-draws it, and the next inward is eaten repaying a debt that should not exist.
Book, all six vouchers accepted by the real `InventoryPostingService.Post`:
`G1 In 30 @ Rs 100.13 (5 Apr) → G2 In 30 @ Rs 100.13 (6 Apr) → G1 Count 30 (7 Apr) → G1 Out 30 (8 Apr) →
G2 Out 20 (9 Apr) → G2 In 20 @ Rs 7.91 (10 Apr)`. Minimum on-hand across G1, G2 **and** the item, over
every date = 0.
**Engine Rs 0.00** on Fifo, Lifo *and* AverageCost; HEAD **Rs 158.20** (Fifo/Lifo) and **Rs 237.30**
(AverageCost); honest **Rs 1,159.50**. Raising only the first lot rate to Rs 10,000.19 makes the honest
figure **Rs 100,160.10** and the engine still reports **Rs 0.00** — the wipe is unbounded.
Same round: unbounded count-up imputation (`In 1 @ Rs 1,000,000.07 → Out 2 → Count 1000` →
**Rs 1,000,000,070.00** on Rs 1,000,000.07 ever spent, HEAD Rs 0.00); and the engine disagreeing with its
own reference (`In 10 UNRATED → In 5 @ Rs 100.13`, no StandardCost → engine **Rs 1,501.95** vs reference
**Rs 500.65**). *(`sa-rereview2.md`)*

### 7 — Per-key replay: a fresh key's zero average, and cost stops flowing across a transfer
Round 9 re-keyed the cost-layer replay to `(item, godown, batch)`. **This is the most important negative
result in the set**, because re-keying is the obvious fix and on its own it breaks ordinary bookkeeping:

- **A transfer re-prices its units off an empty pool.** Book, all posted through the real guard, nothing
  negative, no count, no debt: `Main Receipt 10 @ Rs 0.37 (5 Apr) → Stock Journal transfer 4 to Store B
  (10 Apr, destination rate blank) → Main Receipt 1 @ Rs 1,000,000.03 (15 Apr)`.
  **Rs 5,000,002.37 on 11 units against Rs 1,000,003.73 ever spent** — Rs 3,999,998.64 of phantom
  Balance-Sheet asset, on all three methods, unbounded in the later rate. The pre-round-9 engine reported
  exactly what was spent. Reversed direction wipes instead: **Rs 602.63 where Rs 1,001.67 is due.**
  **Identical on ONE godown when the transfer is a batch re-pack.** A blank destination rate is the
  natural entry for a pure transfer — `BuildStockJournal` passes `null`.
- **A count in a key that has never held stock books every counted unit at Rs 0.00**, because the pool is
  now that one key's, so link 1 of the best-available-cost chain answers 0 and StandardCost is never
  consulted. Book: `Main Receipt 5 @ Rs 100.13; Store B Physical Stock count 30`, StandardCost Rs 9.77 →
  **Rs 500.65** against Rs 293.10 at the master's standard cost. At lot rate Rs 10,000.19,
  **Rs 250,004.75 of real stock wiped.**

*(`pk-review.md`)*

### 8 — A predicate-gated scope creates a valuation cliff at its own boundary
The final attempt confined the debt rule to items living on exactly one `(godown, batch)` key. That
scoping is *correct where it applies* and was measured *inert where it does not* — the round's own
engine-vs-engine sweep of 20,736 rows moved **0 of 15,552** two-key rows. It was abandoned anyway, on a
**structural** result rather than another bug.

One ordinary internal **godown transfer** — which moves nothing in or out of the company and leaves the
destination empty — makes an item multi-key, and so flips its **whole history** between the two valuation
models. Probe family `P-T`, closing quantity 25 units on every arm, destination godown empty at the end
(`g2after=0`), Fifo and Lifo:

| lot rate | no transfer | with one transfer | jump |
|---|---|---|---|
| Rs 1,000,000.03 | Rs 25,000,000.75 | **Rs 40,000,001.20** | Rs 15,000,000.45 |
| Rs 10,000.19 | Rs 250,004.75 | **Rs 400,007.60** | Rs 150,002.85 |
| Rs 7.91 | Rs 197.75 | **Rs 316.40** | Rs 118.65 |

It is unbounded in the lot rate and **survives a same-day round trip** — transferring out and straight back
still reports Rs 40,000,001.20. It also moves closing stock against opening stock with no economic event:
`P-ASOF` at rate Rs 1,000,000.03 reports **20 Apr = Rs 25,000,000.75** and
**25 Apr = Rs 40,000,001.20**, a delta of **Rs 15,000,000.45**.

**HEAD is continuous on all of it** — `jump=0.00` and `delta=0.00` at every rate and every method. The
discontinuity was *created by the change*, not found in the product. That is why the work was stopped: the
problem was no longer a bug to fix but a property of the shape of the solution.

---

## The design the real fix needs

Every failure above is a symptom of **one** root cause: **quantity is keyed by `(item, godown, batch)` and
value is not.** `StockValuationService.MovementEvents` flattens every godown and batch into a single
item-level stream; `InventoryLedger` and the posting guard both work per key. Any debt rule laid on top of
that mismatch is deciding "is this item short?" from a walk that cannot answer the question.

A fix therefore needs **both** of these, together:

1. **Value stock on the same `(item, godown, batch)` key that quantity uses.** Then a layer shortfall
   really is a negative key, `Σ layers − debt == on-hand` holds per key, and the debt rule needs no
   predicate — which removes the cliff of mode 8 at the root, because there is no boundary left to cross.

2. **Make a Stock-Journal transfer CARRY its cost layers from the source key to the destination key**,
   rather than re-deriving them at the destination.

**Say this plainly: (1) without (2) was tried, and it broke ordinary transfers.** That is mode 7. Re-keying
alone makes each key derive its own pool independently, so a transfer arrives at an empty pool and invents
a rate — Rs 5,000,002.37 of stock on Rs 1,000,003.73 ever spent, on a book with nothing negative in it.
The cost has to *move with the goods*. Anyone who implements (1) and stops has reproduced mode 7 exactly.

Two smaller consequences to plan for:

- A physical count must checkpoint **its own key only**, on the value side as well as the quantity side.
- The corpus currently has **no scenario combining two godowns with a physical count**, and **no scenario
  emitting a Stock-Journal transfer at all** — the corpus builder only emits ReceiptNote, DeliveryNote,
  PhysicalStock and item-invoice lines. Both gaps must be closed **before** a re-keyed engine is judged, or
  the oracle will be silent on precisely the books that break.

---

## The limits of the reference — read this before trusting a number

`Reference.cs` implements the intended debt semantics, **ungated**: an outward takes what the stack holds
and any shortfall becomes a **debt**; a later inward **repays it first** at the incoming lot's rate, and
repaid units go to COGS rather than to a layer; a physical count **writes the debt off** and reconciles the
stack to the counted quantity; where a debt is settled by a movement carrying no rate, the point-in-time
chain supplies it. There are no predicates on the rule.

**It is a validated oracle on SINGLE-KEY books only.** On an item that lives on exactly one
`(godown, batch)` key, item-level *is* per-key arithmetically, so a layer shortfall really is a negative
key and the debt rule is sound. That scope carries the evidence: **198** hand-derived goldens
(133 closing + 65 issue, every one of them clause-verified on the current run), plus an exhaustive
6,144-row single-key sweep recorded by the earlier rounds and two independent reviewer re-derivations.

*(The golden count was 215 before the cleanup. 17 goldens that pinned the abandoned single-key-gated model
on multi-key books were removed and 3 restored to their pre-gate derivations — see `Goldens.cs` and the
`Census.cs` recording log for the full accounting.)*

**It is NOT a valid oracle for multi-key books, and that is proven, not suspected.** Mode 7 showed the
per-key model gives wrong answers for transfers; flattening to item level loses the key distinction the
quantity register keeps. *Neither* model is right for multi-key, so the reference must not sentence one.

**Therefore the comparator scopes its ENGINE VERDICT to single-key subjects.** The reference-backed checks
(2, 3, 3b, 10, 9(b)) convict only on subjects proven single-key by the spec-derived `FactSingleKey`
predicate. Multi-key subjects are still replayed, still compared and still **printed** — as lines tagged
`INFO-…` under `MULTI-KEY mismatches (INFORMATIONAL, NO VERDICT)`. Anything the predicate cannot classify
is treated as not judged, the conservative direction. An aggregate row qualifies only when *every* item in
that scenario is single-key at that date, so a multi-key item cannot hide inside a convicted company total.

The same scoping applies to golden **coverage**: demanding a hand-derived constant for a subject asserts
that a correct answer for it is knowable. Multi-key INVENTED subjects are therefore listed as
informational rather than failing the run. **A harness that demands a number it cannot justify is the
failure mode this whole exercise exists to prevent.**

The engine-vs-engine checks (1, 1M, 11, 6/7/8) and the spec-derived quantity oracle (CHECK 5) are
unaffected — none of them consults the reference's cost arithmetic.

---

## Two pre-existing defects found along the way

**Do not fix these as part of a valuation change.** They are recorded here because they were measured, and
because both will confuse anyone who re-derives these books by hand.

### A. `MovementEvents` drops allocations on a Physical-Stock-typed voucher
`src/Apex.Ledger/Services/StockValuationService.cs:175-181`. When `IsPhysicalStock(v)` is true the method
emits the count events and then `continue;` (**line 180**), skipping the `v.Allocations` and
`v.DestinationAllocations` loops entirely. `InventoryLedger.ApplyToKey`
(`src/Apex.Ledger/Services/InventoryLedger.cs:193-207`) applies `PhysicalLines` **and then applies
`v.Allocations` and `v.DestinationAllocations` unconditionally, for every voucher type**. So a
Physical-Stock voucher that also carries allocations moves the quantity register but not the valuation
replay, and the two walks diverge with no diagnostic.

### B. The item-level / per-key desync itself
`MovementEvents` emits a per-key `PhysicalStockLine` as an **item-level** `MovementEvent.Count`;
`InventoryLedger` checkpoints only the counted key; and `LayerValue` discards the reconciliation outright
(`_ = closingQty;`). The reported closing **quantity** and the quantity the layer stack **values** are
therefore different numbers on any multi-key book carrying a count.

Measured magnitude, on the book `G1 In 30 @ Rs 100.13, G2 Out 40, G1 Count 30, G1 In 20 @ Rs 12.00`, which
reports **10 units** on hand: HEAD values them at **Rs 240.00** (Fifo/Lifo) and **Rs 48.00**
(AverageCost) — Rs 24.00/unit for units that cost Rs 12.00. Under the round-8 candidate the same book
reached **Rs 3,243.90**, an implied Rs 324.39/unit and 13.5× HEAD; AverageCost **Rs 648.78**.

This desync is the root cause of failure modes 1 and 6, and it is why `FlatNetMicro` exists in `Facts.cs`:
the reference's self-consistency invariant compares its layer stack against the *flattened* net rather than
the reported closing quantity, so the desync is reported as a measured delta instead of being swallowed as
a harness failure.

---

## What is in the harness

| File | Role |
|---|---|
| `run-oracle.sh` | the single entry point; provenance, both arms, build, emit, compare |
| `Corpus.cs` | **62 scenarios** across 28 families (`N1`–`N11` never-negative, `G1`–`G16` negative/edge, `E1` ordering) |
| `Reference.cs` | the independent valuation model — see [its limits](#the-limits-of-the-reference--read-this-before-trusting-a-number) |
| `Facts.cs` | SPEC-derived facts: a pure **quantity** walk that audits the reference without sharing its cost arithmetic |
| `Goldens.cs` | **133 closing + 65 issue** hand-derived constants, each with its printed derivation and a clause tag |
| `Census.cs` | **363** recorded coverage cells, asserted against the head arm every run |
| `Program.cs` | the emitter and the comparator (the checks below) |
| `bite/` | mutation scripts that prove each check actually bites |

### The checks

- **CHECK 1** — never-negative byte identity (engine vs engine).
- **CHECK 1M** — byte identity on every multi-key subject (engine vs engine; the reference supplies only
  the scope, so the check cannot be satisfied by the reference agreeing with the engine about the wrong
  thing).
- **CHECK 2 / 3 / 3b** — point oracles on closing value: AverageCost against the debt-aware reference,
  Fifo/Lifo, and the flat methods. *Single-key scope.*
- **CHECK 4 / 4b** — reference calibration against HEAD on books where the debt clauses are dead code.
  *Harness check.*
- **CHECK 4c** — the hand-derived goldens, closing and issue, with their clause labels verified against a
  pure quantity walk. **The only validation the debt branch has.** *Harness check.*
- **CHECK 5** — quantity oracle against the spec (catches a "fix" that returns quantity 0 and value 0).
- **CHECK 6 / 7 / 8** — closing-rate band, total-spend containment, COGS conservation.
- **CHECK 9** — `TotalClosingStockValue`: (a) equals the sum of per-item values on the same arm; (b)
  equals the reference total *(single-key scope)*.
- **CHECK 10** — issue value against the reference *(single-key scope)*. Audited by nothing in v1: a
  change with a correct Balance Sheet and a wrong P&L once shipped clean.
- **CHECK 11** — exception asymmetry.
- **Census gate** — every check must evaluate the same subjects it evaluated before.

### Proving the checks bite

`bite/` holds mutation scripts that install a deliberately wrong engine and assert the harness convicts it.
`bite-NN-*` target the engine checks, `hbite-NN-*` target the harness's self-audit (a poisoned reference, a
lost golden, an edited constant, a lying clause label), and `wrong-NN-*` reproduce real defect shapes.
`accept-probe*.sh` proves the ACCEPT state is reachable at all — a gate that can never pass is not a gate.
Bite runs are stamped and exit `4`; they are never a verdict on `src/`.

### Two rules that were learned the hard way

- **Never edit a golden's constant to match the code.** The constant is the hand derivation; the code is
  the thing on trial. This rule was vindicated in the final cleanup: the abandoned round had edited
  GT-25 / GT-43 / GI-26 from **Rs 197.75** to Rs 316.40 to match its gated reference. Removing the gate
  restored the reference to Rs 197.75 — the original derivation had been right all along, and the edit had
  destroyed the evidence.
- **A derivation must END in its constant.** CHECK 4c enforces it, because prose and constant drifting
  apart is how a reviewer gets adjudicated into the wrong answer.

### Re-recording the census

A deliberate act, never a side effect: run the oracle, take the `FULL CENSUS (head arm)` block from
`.oracle-work/report.txt` (every line beginning `    CENSUS  `), replace `Data` in `Census.cs`, and write
**why coverage changed** in the recording log at the top of that file. A census that shrinks without a
reason is the defect the file exists to catch.

### The self-audit ledger — read this before trusting any check here

Audits **#3–#6** found nine ways this harness certified something it had not measured. Every one is fixed,
and every one has an `hbite-NN-*.sh` that re-proves the fix bites — but the findings themselves are written
up in **`plan.md` → Phase 10.8 → `NS-9`**, because a code comment beside a fix does not tell you what the
harness used to get wrong.

The one worth knowing before you add a scenario: **a scenario that throws on *both* arms is invisible to a
symmetric-exception check, and a census recorded from that state blesses its own hole.** `G11-002` — the
purchase-invoice half of the invoice seam — did exactly that for its whole life: no engine row existed, the
point oracle iterates *live* keys so it judged nothing there, CHECK 11 saw a symmetric exception and passed,
and the recorded census had been taken from that state. The string `G11-002` appeared **zero** times in the
report while family G11 was presented as covering the seam. `BuildOutcome` is asserted in PART A now, on
both arms — but the shape is general, so **check that a scenario you add actually appears in the report.**

---

## Reading the verdict

`HARNESS INTEGRITY: SOUND | BROKEN` and `ENGINE VERDICT: ACCEPTED | REJECTED` are separate lines on
purpose. **BROKEN means the report says nothing about the engine.**

At the baseline commit, with `live == head`, the expected result is **exit 1: HARNESS SOUND, ENGINE
REJECTED**, with checks 2, 3, 9(b) and 10 failing on their single-key scope. That is the harness working.

When you put a candidate engine in `src/`, the bar is:

- checks 2, 3, 3b, 9(b), 10 → **0 judged mismatches**;
- checks 1, 1M, 5, 11 → **unchanged and passing** — this is what catches a fix that trades one defect for
  another, which is what happened seven times out of eight;
- `HARNESS INTEGRITY: SOUND` — if a golden or the census had to move, justify it in writing first;
- and the multi-key **informational** lines read and understood, not skipped. They are informational
  because no oracle can judge them, *not* because they do not matter.
