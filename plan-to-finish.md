# Plan to Finish — Apex Solutions

> Built 2026-10-12 from a **measurement**, not an estimate. Four read-only agents sized all 83 remaining
> filing-core rows by reading each census cell and checking **every named gap clause against the code**.
> Measured at `origin/main` **ccb1339**. Schema 65.
> Governed by `CLAUDE.md` R1–R14. This document is a release plan; `plan.md` remains the execution plan and
> is amended from here under R6.

---

## 1. The answer up front

**12–16 waves. Realistically 6–10 weeks. Three rows can never reach COMPLETE without credentials you do not
currently have.**

That is *not* the sum of the four areas' estimates (35–44 waves). A wave runs **four tracks in parallel**, one
per area, so campaign length is bounded by the **slowest area, not the total**:

| Area set | Rows left | Agent's own estimate |
|---|---|---|
| Masters (Areas 2, 3) | 23 | 6–8 waves |
| Vouchers (Areas 4, 5) | 21 | 8–9 waves |
| **GST (Area 6)** | **19** | **12–15 waves ← the long pole** |
| Reports + restore (11, 12, 13.2) | 20 | 9–12 waves |

**GST is the critical path.** Every plan decision below is about either shortening it or keeping the other
three areas off it.

---

## 2. What the measurement actually found

### 2.1 Forty-five already-closed clauses

The single most important result. Across the four sets, **45 gap clauses that the census still lists as open
are already closed in the code**. This is the third time this has happened (a truth pass once found 20
mis-graded rows and refuted 14 of an audit's own claims).

**Rows whose *every* named gap is already closed or is a phantom:** `3.2`, `3.5`, `4.10`, `4.14`, and
probably `2.3`, `2.5`, `3.8`, `3.9`, `3.12`.

> **Consequence: the real remaining work is smaller than 83 rows implies, and nobody currently knows by how
> much.** Phase 0 exists to find out before we spend a single build wave.

### 2.2 Size distribution

| Size | Count | Meaning |
|---|---|---|
| **S** | 28 | view-model change + tests; no XAML, no schema |
| **M** | 20 | screen wiring or a report path + tests |
| **L** | 20 | schema migration, new persisted entity, or >10 files |
| **XL** | 15 | **blocked on a ruling or a credential — not on engineering** |

**15 XL rows cannot be built at any number of waves.** 33 distinct open questions were named. Spending waves
on them produces refusals — which is a large part of why waves 44–46 moved zero rows.

### 2.3 Three rows are credential-blocked, not effort-blocked

`6.10` (portal JSON), `6.14` (e-Invoice IRN), `6.15` (e-Way Bill) are each capped by something the project
cannot retrieve: the **authenticated GST developer portal schema**, an **IRP/GSP account**, a **live NIC
connection**. No amount of engineering closes them.

**These three stay PARTIAL unless you obtain access.** They are budgeted at **zero waves and one decision.**

### 2.4 Twenty-three clusters

One fix advancing many rows is the proven lever here (one shared column-band declaration took print captions
from 0 of 37 report kinds to 37 of 37). The highest-value ones:

| Cluster | Advances | Size |
|---|---|---|
| Shared-surface re-home (give ~22 dedicated report screens a report context) | 11.5, 11.11, 11.15, 11.16, 12.1 | L |
| One Area-2/3 storage migration | 2.4, 2.7, 2.9, 2.10, 2.11, 3.7 | L |
| Voucher registration attribution | 6.23, 6.24, 6.19, T2-121 | L |
| SEZ / export machinery | 6.21, 6.8, 6.9 | L |
| End-to-end UI test theory | 4.9, 4.11, 4.12, 4.15, 4.16 | M |
| Drill-switch widening | 11.12, 11.13, 11.14, 11.15 | M |
| Canonical round-trip | 6.23, 6.25, 6.24 | M |

---

## 3. The plan

### Phase 0 — two waves, **zero code**, run first

Nothing here builds anything, and it is the highest-leverage work available.

**Wave 0a — Truth pass + census refresh.**
Verify all 45 already-closed clauses first-hand, promote every row whose cell is merely stale, and strike the
stale sentences. Deliverable: a census that matches the code, and **a corrected count of what actually
remains**.

**Wave 0b — One A14 vendor pass (read-only).**
Settle the questions that are blocking rows without needing any build: is the numbering method offered on the
F12 screen; does TallyPrime refuse a compound-unit conversion change on alter; does Batch/BOM have a
documented alter verb at all; the currency formatting captions (two agents hit a 404); the reorder page.
This alone can settle `2.5`, `3.5`, `3.8`, `3.9` outright and unblock `2.11`, `3.7`, `3.12`, `3.13`.

> **After Phase 0 the row count will be lower and better-grounded. Every number below should be re-derived
> then.** This plan deliberately does not pretend to know the post-Phase-0 figure.

### Phase 1 — one user sitting, **zero waves**

33 questions, but they collapse into roughly four sittings. Until they are answered, 15 rows are unbuildable.

1. **The print panel** (T2-117 i–v + IV-63) — unblocks `12.1`, `12.4`, `11.11`
2. **GST scope and credentials** — is online submission (IRP/NIC/portal) in scope at all? This decides whether
   `6.10`, `6.14`, `6.15` can ever be COMPLETE
3. **The master alias model** — one `master_aliases` table across eight masters, or one alias each? Settles
   `2.7`, `2.8`, `2.3`, `3.7`
4. **Scope calls** — voucher-type Printing/Sharing blocks (`2.4`); `5.10` int→string across 90 src files;
   `6.26` Kerala Flood Cess, which lapsed 2021-07-31 and computes zero by construction

### Phase 2 — build waves, four tracks in parallel

Sequenced so **GST gets a track in every single wave** (it is the critical path) and the other three areas
fill the remaining slots.

| Wave | GST track (critical path) | Masters | Vouchers | Reports |
|---|---|---|---|---|
| 1 | v66 migration: attribution + the 5 queued items | Area-2/3 storage migration (v67) | e2e UI theory + numbering discard | the six S rows, batched |
| 2 | 6.23 / 6.24 registration scope on top of v66 | multi-master families | return-note alteration arm | drill-switch widening |
| 3 | SEZ / export machinery | voucher class | provisional-state verbs | shared-surface re-home |
| 4 | 6.9 GSTR-3B RCM/ITC fields | alias model (post-ruling) | order retirement the vendor's way | comparative engine |
| 5 | 6.8 GSTR-1 remaining tables | 3.4 perpetual valuation | Area 5 schema rows | 12.8 PdfWriter floor |
| 6–8 | 6.12 GSTR-9/9C, 6.35 TDS long tail, 6.20, 6.21 | finish + re-grade | finish + re-grade | 13.2 restore, 12.6, 12.7 |
| 9–12 | GST tail and the sweep finishers | — | — | — |

**Rules carried forward from what has actually worked:**
- One migration per wave, **one owner**, stated up front. v66 now carries **five** things.
- **Never two GST tracks on the same files** — two branches once landed opposing sign rules at one location.
- Land in its own run. The land agent is the one repeatedly killed by session limits.
- Rescue before merge, every time.

### Phase 3 — release hardening, 2–3 waves

Run the app end-to-end on both fixtures (**Robert**, **Bright**), real printer output, backup *and* restore,
a full GST cycle on a multi-registration book. Then the user documentation and the honest published gap list.

---

## 4. Honest risks

| Risk | Evidence | Mitigation |
|---|---|---|
| **Waves move zero rows** | 44, 45, 46 each moved zero | Phase 0 first; every wave names the rows it must move and is judged on them |
| **Correctness debt eats build capacity** | three waves spent on wrong money | Debt is now largely paid: the four-times-repeated unscoped-collection shape is closed; sign correctness is "ALREADY SPENT — nothing left" |
| **Usage limits** | weekly limit hit twice | One wave at a time; land separately; commit before build |
| **Migration overhead** | one took 46 call sites across 17 files; v64 needed 22 Sqlite test files | Batch migrations; never two live tracks taking one version |
| **The census understates** | 4–5 vendor-attested capabilities have **no row at all**; true denominator ~223–224 | Phase 0 adds the missing rows — the number may go *up* before it goes down |

---

## 5. What "finished" will mean

At the end of Phase 3, on the ruling-29/30 filing core of **131 rows**:

- Every row that is **closable** is COMPLETE and vendor-grounded
- **~3 rows remain PARTIAL by necessity** (credential-blocked), labelled as ours in a published gap list
- Payroll (Area 7, 26 rows) and Areas 1, 8–10, 14–16 are **out of scope** and documented as such — the 4%
  cess question went out of scope with them
- The app runs, prints to a real driver, backs up and restores, and files a GST return for a
  multi-registration book

**This is a complete filing product with a published, honest list of what it does not do — not a 100% clone.**
Reaching 100% of all 219 rows was never achievable in this timeframe, and saying so plainly is part of the plan.
