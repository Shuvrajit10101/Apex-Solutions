# Apex Solutions — Feature Census and Prioritised Gap Register

**Scope:** every Tally 7.2 feature, plus the Tally.ERP 9 additions, judged against TallyPrime as the fidelity target.
**Baseline:** worktree `…\.claude\worktrees\recursing-swirles-3138c6`, HEAD `468a96e`, schema v50. Read-only; nothing built, run, or edited.
**Date:** 2026-08-10.

> **▶ 🔴 2026-08-20 — THE DENOMINATOR DID *NOT* MOVE; ONE STATE DID, AND IT IS THE STATE THIS DOCUMENT WAS
> MOST WRONG ABOUT. READ THIS BEFORE THE 2026-08-19 BANNER BELOW IT.** Phase 10.11's **S5d** (`a34d989`) and
> **S5e** (`b89213e`) shipped **voucher alteration** into the product, and neither slice touched this file —
> so §1.2a **row 5.1** still graded the capability `ABSENT` on four searches that are now three-quarters false,
> and §1.3 **item 12** still read *"GROUNDED, NOT YET BUILT"*. **Both are corrected in place, with the original
> text quoted, and §1.2's integers were RE-SUMMED by re-running §1.2a's own command rather than edited.**
> 1. **§1.2's split is now `216 · 47 / 97 / 72 / 0`.** Exactly `+1 partial, −1 absent`, from row 5.1 alone.
>    The 2026-08-19 `216 · 47 / 96 / 73 / 0` is kept in place below as the record of that day.
> 2. ⚠️ **SUPERSEDED LATER THE SAME DAY — the anchor block reads `12 · 13 · 204 · 203` from the T0-11 slice-S0
>    pass, which added item 14 (graded `[GRADE: GROUNDED-AHEAD]`) and separated the two halves again. This
>    banner is the
>    record of the FIRST pass of 2026-08-20 and is kept as one; §1.3 wins.** It read:
>    **§1.3's anchor block is `12 · 12 · 204 · 204`**, and the two halves now COINCIDE for the first time —
>    item 12 was the only header that was grounded and not yet compared, so closing it makes
>    *"uncompared as shipped"* and *"no sourced verification of any kind"* the same number. Say it out loud
>    rather than letting a reader think one of them was mis-derived.
> 3. **Item 12 now carries the ruling-9 step-5a fidelity record for S5a–S5e**, in the two R7 categories
>    ruling 9 requires, and it **names the one family still refused after S5e — the SALES ITEM INVOICE** —
>    which no record at HEAD did.
> 4. 🔴 **This correction is the FOURTH consequence of a mechanism, not a one-off tidy.** `plan.md` §2.2
>    step 5a says the count *"is maintained"* in §1.3 and *"do not copy the digits into this file"*; **S5d
>    wrote a full R7 record into `plan.md` anyway and S5e wrote none at all**, so a compliant author
>    discharged the gate in substance and left every maintained figure here stale. The gate is prose-checked,
>    not derivation-checked. That is recorded as **T3** below, not smoothed over.
>
> **▶ 🔴 2026-08-19 — THE DENOMINATOR MOVED AGAIN, AND THIS TIME IT IS A SCOPE DECISION, NOT A RE-COUNT.
> READ THIS BEFORE THE 2026-08-18 BANNER BELOW IT.** **User ruling 10 (R12 — `plan.md` §5,
> `FOUR FURTHER USER RULINGS (R12, 2026-08-19)`) brought BOTH held-out sets into scope**, so the two sections
> this document has always kept outside its net figure — **§3 obsolete-by-law (9)** and **§4
> excluded-by-decision (7)** — are now **Areas 15 and 16 of §1.2a**, with states derived from the code the
> same way every other row's is. **`200 + 9 + 7 = 216`, and the machine check agrees: `TOTAL rows=216 C=47
> P=96 A=73 U=0 sum=216`.**
> 1. **§1.2's split is `216 · 47 / 96 / 73 / 0`.** The 2026-08-18 `200 · 47 / 95 / 58 / 0` is kept in place
>    below as the record of that day.
> 2. **ONE OF THE SIXTEEN IS NOT ABSENT.** Row **16.6** (Repair / Rewrite / Verify) is **PARTIAL** — a real
>    `PRAGMA integrity_check` with callers on both the backup and the restore path. This document recorded it
>    as ABSENT on 2026-08-18 and that was wrong; **the area-13 held-out note is corrected in place.** It is
>    the whole reason the ruling required these states to be measured rather than assumed.
> 3. **§1.3's anchor block is restated against 216 — `11 · 12 · 205 · 204`.** Only the last two moved; the
>    derivation is unchanged and stays a property of the item headers.
> 4. **§1.1 rule 5, §3 and §4 are marked in place, not deleted** — each now points at its area.
> **The 2026-08-10 decision NOT to build the nine is REPEALED**, and `plan.md` marks it repealed in place.
> **Do NOT re-derive the architecture-excluded count (rule 4's withdrawn "13"), and do NOT resurrect the
> retired top-down reconciliation to "check" 216** — those are the two hazards ruling 10 names by measurement.

> **▶ 🔴 REFRESHED 2026-08-18 AT HEAD `6fb5fe5`, SCHEMA v51 — READ THIS BEFORE THE 2026-08-10 TEXT BELOW IT.**
> Five independent read-only surveys re-derived every capability state against source. **Four defects in this
> document were fixed and the fixes changed its headline numbers:**
> 1. **§1.2a now exists** — the per-capability list this census has never had. Before today a reader could
>    learn that twenty-one capabilities were absent and could **not learn which twenty-one**.
> 2. **§1.2's split is re-derived FROM that list** and is no longer a parallel assertion.
>    **115 · 42 / 44 / 21 / 8 → 200 · 47 / 95 / 58 / 0.** §1.2b says exactly what moved and why, and the old
>    table is kept in place rather than overwritten.
> 3. **§1.3's anchor block is re-derived and made self-maintaining.** It had hard-coded the condition
>    *"until S3 / S4 / S5c land"*; S3 and S4 landed and nobody re-derived it, so it contradicted its own rows.
>    The derivation is now a property of those rows.
> 4. **§1.2c retires the top-down reconciliation**, which never closed: `129 − 9 − 7 − 1 = 112`, not 115.
>
> **Everything dated 2026-08-10 below is preserved as written.** Superseded claims are marked in place with a
> dated note and the original text quoted; nothing was silently rewritten to today's truth.

Markers used below: **[V]** = re-verified by me against source at this HEAD during this census. Unmarked rows are relayed from the three mapping agents with their `file:line` evidence intact. **GUESS** where I am inferring.

---

## 1. THE DENOMINATOR

### 1.1 The counting rule (argue with this first)

A **capability** is one thing a user would name when asking "can it do X" — the granularity of a Tally menu row or an F11 toggle, not a field and not a code file. Rules applied:

1. **Voucher types count individually** (18 for 7.2). They are the atoms of the product.
2. **Report families count as one** (`Account Books`, `Statements of Accounts`, `Inventory Books`, `Exception Reports`). This is the largest deliberate compression in the count and it flatters us: `Account Books` scores as one PARTIAL row while hiding six missing registers. Expanding families to individual reports would push the denominator past 200. **▶ 2026-08-18: this rule is RETAINED unchanged, and §1.2a keeps every one of those families as a single row.** The denominator nonetheless moved to **200** because the rest of the product was finally written out at rule 1's granularity — see §1.2b. **That is a coincidence of magnitude, not a repeal of this rule**: the ~14 registers hiding inside the four families are still hiding, and each family row in §1.2a now names what is inside it.
3. **A capability is counted once**, in the earliest product that shipped it. ERP 9 rows the source census marks "IN 7.2" are folded into the 7.2 baseline. **▶ 2026-08-18: §1.2a enforces this explicitly.** Where two surveys named the same capability under different areas, the row lives in one area and the other carries an **uncounted cross-reference**; every such row says which and why.
4. **Excluded from the denominator entirely** (not gaps, not progress): pure licensing (Silver/Gold, multi-site, rental), edition/subscription features (Tally.NET, Remote Access, Control Centre, Support Centre, TRiB, SMS, Auditors' Edition, Tally.Server 9, Data Synchronisation), the 7.2 data-format migration tool, the 7.2 character-grid UI (superseded by our fidelity target), international statutory packs, TDL, and multilingual. ~~13 rows.~~ **▶ 🔴 CORRECTED 2026-08-18: "13" IS UNSOURCED AND NOT DERIVABLE FROM EITHER LIST.** This enumeration and §4's closing paragraph are **two different lists**, and neither totals 13 — §4's has **12** distinct names and this one adds **3** more (Data Synchronisation, the 7.2 data-format migration tool, the 7.2 character-grid UI), so the **union is 15**. **§4's closing paragraph is the canonical list and this rule points at it.** Nothing downstream moves: these rows are outside the denominator either way. See §1.2c.
5. ~~**Held out of the net figure pending a user decision:** obsolete-by-law statutory (9 rows, §3) and excluded-by-decision (7 rows, §4).~~ **▶ 🔴 THE DECISION WAS TAKEN 2026-08-19 AND IT WAS TO INCLUDE THEM — user ruling 10 (R12, `plan.md` §5). NOTHING IS HELD OUT PENDING A USER DECISION ANY MORE.** Both sets are now **build rows**: §3's nine are **Area 15** of §1.2a and §4's seven are **Area 16**, each with a state derived from the code the way every other row's is. The denominator moves **200 → 216** (`200 + 9 + 7`). §3 and §4 are **retained below as the record of what was held out and why**, and both now point at their areas. **▶ 2026-08-18, and still true: these two counts are correct as stated, and they are what proved §1.2's old top-down reconciliation never closed** — that check subtracted **8** and **5** against them. See §1.2c, which retires the check rather than bending either count to fit it; **do not resurrect it to "check" 216 either.**

### 1.2 The number

> **▶ 🔴 RE-DERIVED 2026-08-18. EVERY INTEGER BELOW IS A COLUMN SUM OF §1.2a AND NOTHING ELSE.**
> Until today this table was a **parallel assertion**: a **2026-08-10 snapshot taken at HEAD `468a96e`, schema
> v50**, whose rows existed only as integers. Its columns and rows summed correctly — the arithmetic was never
> the problem. **The problem was that the twenty-one ABSENT capabilities were never named anywhere.** §5 below
> referred to *"the absent list"* as if one existed; it did not. Reconstructing the names from §2
> **over-supplied** several buckets — two distinct absent capabilities competed for Configuration's single
> slot, and the Accounting-masters candidates exceeded its three — which is what proved that any reconstruction
> was **not the census's own classification**. **§1.2a is now that list.** If a row there changes state,
> **this table is re-summed. Never edit an integer here directly.**

| # | Area | In scope | Complete | Partial | Absent | Undetermined |
|---|---|---:|---:|---:|---:|---:|
| 1 | Company creation & configuration (F11/F12) | 9 | 1 | 6 | 2 | 0 |
| 2 | Accounting masters | 13 | 0 | 8 | 5 | 0 |
| 3 | Inventory masters | 15 | 2 | 11 | 2 | 0 |
| 4 | Voucher types (7.2's classic eighteen) | 18 | 5 | 13 | 0 | 0 |
| 5 | Voucher behaviours & edit verbs | 15 | 6 | 6 | 3 | 0 |
| 6 | Statutory, current law (GST, TDS/TCS, salary IT) | 42 | 18 | 15 | 9 | 0 |
| 7 | Payroll | 21 | 6 | 11 | 4 | 0 |
| 8 | Banking | 10 | 1 | 4 | 5 | 0 |
| 9 | Inventory / manufacturing / job work (post-7.2) | 9 | 3 | 2 | 4 | 0 |
| 10 | Accounting features (post-7.2) | 2 | 0 | 0 | 2 | 0 |
| 11 | Reports | 17 | 5 | 12 | 0 | 0 |
| 12 | Printing | 9 | 1 | 5 | 3 | 0 |
| 13 | Data management (import/export/backup/e-mail) | 10 | 3 | 7 | 0 | 0 |
| 14 | TallyPrime-only capabilities | 10 | 1 | 2 | 7 | 0 |
| 15 | Statutory, obsolete by law (pre-GST) — **was §3** | 9 | 0 | 0 | 9 | 0 |
| 16 | Formerly excluded by decision (security, audit, data structure) — **was §4** | 7 | 0 | 3 | 4 | 0 |
| | **TOTAL** | **216** | **52** | **105** | **59** | **0** |

**A full clone requires 216 named capabilities. We have 52 complete, 105 partial, 59 absent, 0 undetermined.**

> 🔴 **RE-SUMMED 2026-09-05 (THE b1/b3/b4/b5 LANDING). NINE ROWS MOVED OFF `ABSENT` AND NOTHING ELSE MOVED.**
> The table read ~~*"TOTAL … 46 · 102 · 68"*~~, and areas 1, 5, 7, 11, 12 and 13 read ~~`0|6|3`~~, ~~`5|6|4`~~,
> ~~`6|10|5`~~, ~~`2|12|3`~~, ~~`1|4|4`~~ and ~~`2|6|2`~~. **To `COMPLETE`:** 1.4, 5.4, 11.6, 11.7, 11.8, 13.10.
> **To `PARTIAL`:** 7.16 (4 of 8), 12.4, 13.6. Delta `+6 complete, +3 partial, −9 absent`; the denominator and
> every other area are untouched. **Every integer above was re-derived by re-running §1.2a's own command over
> this file, not edited to fit: `TOTAL rows=216 C=52 P=105 A=59 U=0 sum=216`**, and all six changed areas match
> their own headings. **Areas 11 and 13 reach `0 absent` for the first time.**
> 🔴 **AND THE HONEST PART: it would have been ELEVEN.** Rows **6.10** (GSTR-1/3B offline JSON) and **6.13**
> (GSTR-9A) are built, gate-green and reviewed on branch `claude/apex-b2-gst-artefacts` — but that branch was
> **held on a red ubuntu CI leg (PR #48) and is NOT on `main`**. On `main` those capabilities remain unreachable,
> so **the rows did not move.** Area 6 is unchanged at `18 / 15 / 9`. **Do not move them until b2 lands.**

> 🔴 **RE-SUMMED 2026-09-04 (WAVE-3 FOLD-IN). ONE ROW MOVED AND IT MOVED DOWN.** The table read
> ~~*"| 9 | … | 9 | 4 | 1 | 4 | 0 |"*~~ and ~~*"TOTAL … 47 · 101 · 68"*~~. **Row 9.3 (Job Work registers) went
> `COMPLETE` → `PARTIAL`** when §1.3 item 23 gave those rows their first comparison and counted **four shipped
> registers against the vendor's eleven**. **Nothing was un-built. The census was wrong about 9.3, and this is
> the sixth evidence/state cell caught wrong on this project** (after 16.6, 5.1, 12.8, 16.3, 16.4). Delta:
> `+0 rows, −1 complete, +1 partial, +0 absent`. Every integer above was re-derived by re-running §1.2a's own
> awk, not edited: its literal output is `TOTAL rows=216 C=52 P=105 A=59 U=0 sum=216`
> *(🔴 re-summed again 2026-09-05 in the b1/b3/b4/b5 landing; it read `C=46 P=102 A=68` earlier the same day)*.
Every `COMPLETE` still means **present and reachable**, never *correct*; §1.3 holds the fidelity figures and is
the only place they are maintained.

> **▶ 🔴 RE-SUMMED 2026-09-04 — THREE ROWS MOVED, ALL IN THE SAME DIRECTION, AND THE INTEGERS WERE RE-DERIVED BY
> RE-RUNNING §1.2a's OWN COMMAND, NOT EDITED.** The sentence above previously read ~~*"We have 47 complete, 98
> partial, 71 absent, 0 undetermined"*~~; area 12's row previously read ~~`| 12 | Printing | 9 | 1 | 3 | 5 | 0 |`~~
> and area 16's ~~`| 16 | Formerly excluded by decision (security, audit, data structure) — **was §4** | 7 | 0 | 1
> | 6 | 0 |`~~. **Rows 12.8, 16.3 and 16.4 moved `ABSENT` → `PARTIAL`.** The delta is exactly `+3 partial, −3
> absent`; the denominator, every other area and every other column are untouched. **Machine check re-run
> 2026-09-04 over this file: `TOTAL rows=216 C=47 P=101 A=68 U=0 sum=216`**, and areas 12 and 16 both match their
> own headings. Anything still quoting `47 / 98 / 71` is quoting the 2026-09-03 snapshot.
> 🔴 **AND THE HONEST SHAPE OF THIS MOVE, BECAUSE IT LOOKS LIKE PROGRESS AND IS NOT.** No capability was built on
> 2026-09-04 — the wave-2 passes were **read-only**. All three rows were **already** PARTIAL in the product and
> the census had not noticed: the voucher edit log shipped at schema v52 and raster images ship with a production
> caller on the invoice path. **A census row that is wrong about its own state is a defect in the census**, and
> three of them are recorded as such in §2 TIER 3 rather than absorbed silently into a better-looking number.
> ⚠️ **AND WHAT DID NOT MOVE: the fidelity anchor is a different figure and these three rows do not touch it.**
> §1.2a measures existence; §1.3 measures comparison. 12.8, 16.3 and 16.4 remain **uncompared** — indeed 16.1,
> 16.2 and 16.3 were compared to an official source on the same day and all three came back **DIVERGES**.
> **▶ ⚠️ AND IT RIPPLES INTO `plan.md`'s BREADTH ARITHMETIC AGAIN:** *"71 absent rows"* (the Wave-3 breadth
> figure) **is now 68.** Corrected in place there rather than left to be discovered. **The wave-2 breadth design
> enumerated 71 and is not re-cut** — it was written against the 2026-09-03 state, it says so, and three of its
> slices (W2-21 and the 12.8 residual) close a *residual* rather than an absent row. See `plan.md` §Wave 2.

> **▶ 🔴 RE-SUMMED 2026-09-03 — ONE ROW MOVED, AND THE INTEGERS WERE RE-DERIVED BY RE-RUNNING §1.2a's OWN
> COMMAND, NOT EDITED.** The sentence above previously read ~~*"We have 47 complete, 97 partial, 72 absent, 0
> undetermined"*~~ and area 6's row previously read ~~`| 6 | Statutory, current law (GST, TDS/TCS, salary IT) |
> 42 | 18 | 14 | 10 | 0 |`~~. **Row 6.4 (the GST rate hierarchy above the Stock Item) moved `ABSENT` →
> `PARTIAL`**, because T0-4 slices **S1/S2a/S2b** shipped the resolution half: `GstService` now walks all five
> rungs in the order the book's `SourceOfGstRate` selects, so that column has a reader outside persistence and
> Io for the first time. The delta is exactly `+1 partial, −1 absent`; the denominator, every other area and
> every other column are untouched. **Machine check re-run 2026-09-03 over this file:
> `TOTAL rows=216 C=47 P=98 A=71 U=0 sum=216`**, and area 6's own heading matches. Anything still quoting
> `47 / 97 / 72` is quoting the 2026-08-20 snapshot.
> 🔴 **AND THE MOVE IS HALF A CAPABILITY, NOT ONE — DO NOT READ IT AS T0-4 CLOSING.** Row **3.13**, the CAPTURE
> half of the same defect, is still `ABSENT` and was re-measured the same day: the Group and Stock Group master
> screens still carry no GST view-model property and no XAML field, and `SourceOfHsnSacDetails` still has no
> reader at all. `PARTIAL` here means *present and reachable with a named missing piece*, and row 6.4 names
> three of them.
> **▶ ⚠️ AND IT RIPPLES INTO `plan.md`'s BREADTH ARITHMETIC AGAIN:** *"72 absent rows"* (the Wave-3 breadth
> figure) **is now 71**. Corrected in place there rather than left to be discovered.

> **▶ 🔴 RE-SUMMED 2026-08-20 — ONE ROW MOVED, AND THE INTEGERS WERE RE-DERIVED BY RE-RUNNING §1.2a's OWN
> COMMAND, NOT EDITED.** The sentence above previously read ~~*"We have 47 complete, 96 partial, 73 absent, 0
> undetermined"*~~ and area 5's row previously read ~~`| 5 | Voucher behaviours & edit verbs | 15 | 5 | 5 | 5 |
> 0 |`~~. **Row 5.1 (voucher alteration) moved `ABSENT` → `PARTIAL`**, because Phase 10.11 slices **S5a–S5e**
> shipped it and the row's four zero-hit searches are now three-quarters false at HEAD (the row itself carries
> the measurement). The delta is exactly `+1 partial, −1 absent`; the denominator, every other area and every
> other column are untouched. **Machine check re-run 2026-08-20 over this file:
> `TOTAL rows=216 C=47 P=97 A=72 U=0 sum=216`**, and area 5's own heading matches. Anything still quoting
> `47 / 96 / 73` is quoting the 2026-08-19 snapshot.
> **▶ ⚠️ AND IT RIPPLES INTO `plan.md`'s BREADTH ARITHMETIC:** *"73 absent rows"* (the Wave-3 breadth figure,
> stated four times there) **is now 72**. That is corrected in place there rather than left to be discovered.

> **▶ 🔴 THE DENOMINATOR MOVED AGAIN ON 2026-08-19: 200 → 216, AND THIS ONE IS NOT A RE-COUNT OF THE SAME
> PRODUCT. IT IS A SCOPE DECISION.** User ruling 10 (R12 — `plan.md` §5,
> `FOUR FURTHER USER RULINGS (R12, 2026-08-19)`) brought **both held-out sets into scope**: §3's nine
> obsolete-by-law capabilities and §4's seven excluded-by-decision ones. They are no longer held out of the
> net figure pending a user decision (§1.1 rule 5) — **the decision was taken, and it was to include them.**
> **THE ARITHMETIC, WRITTEN OUT SO IT IS CHECKABLE RATHER THAN ASSERTED: `200 + 9 + 7 = 216`**, where 200 is
> the 2026-08-18 column sum, and 9 and 7 are §3's and §4's own counts, both of which this document
> re-affirmed as *"correct as stated"* on 2026-08-18. The rows themselves are **Areas 15 and 16 of §1.2a**,
> and this table's integers are still nothing but their column sums.
> **▶ 🔴 THE SIXTEEN NEW STATES WERE MEASURED, NOT ASSUMED — AND ONE OF THEM IS NOT ABSENT.** Row **16.6**
> (Repair / Rewrite / Verify) is **PARTIAL**: a real `PRAGMA integrity_check` exists, is called on both the
> backup and the restore path, and is reachable — so the row cannot rest on a zero-hit search, which is the
> ABSENT bar. That single row is why *"held out, therefore absent"* was not an acceptable shortcut: it would
> have written a falsehood into the denominator on the first day it counted. The other fifteen are ABSENT,
> each on a **named regex that returned zero**.
> **▶ 🔴 DO NOT RESURRECT THE TOP-DOWN RECONCILIATION TO "CHECK" 216.** §1.2c retired it — **retired, not
> repaired** — and the two counts it could never reconcile were **these very 9 and 7**. 216 is derived
> **bottom-up from §1.2a's rows and by nothing else.** Re-run the awk.

#### 1.2 (superseded) — the 2026-08-10 snapshot, kept because other documents still quote it

> **▶ THIS IS THE OLD TABLE. IT IS NOT DELETED, AND IT IS NOT CURRENT.** `plan.md`'s census banner, the
> kick-off and `memory.md` **quoted** *"~115 named capabilities: 42 complete, 44 partial, 21 absent, 8
> undetermined"*. That sentence describes **this** table, taken at HEAD `468a96e` on **2026-08-10** under a
> header that said *"nothing built, run, or edited"*. It has been superseded since **2026-08-18**.
>
> **▶ 🔴 THE PROPAGATION WAS CLOSED THE SAME DAY, and this line records where — because the last three
> corrections on this branch each left live copies behind.** As of **2026-08-18** the in-repo copies are
> repaired: `plan.md`'s census banner and its *"held OUT of the 115"* clause, `docs/NEXT_SESSION_KICKOFF.md`'s
> DENOMINATOR section, `docs/invented-vs-cloned.md` §7's dagger note and `docs/tally-fidelity-defects.md` §7's.
> **None of them restates the digits** — each points at §1.2a / §1.2 / §1.3's anchor block instead, so the next
> move of these numbers cannot strand a copy. `memory.md`'s 2026-08-10 log entry is **deliberately left
> standing** with a dated supersession marker beneath it: it is a log of what was true that day, and rewriting
> it would falsify the record rather than correct a claim. **This table is retained for exactly that reason** —
> outside documents, and this project's own history, still quote it.

| # | Area | In scope | Present | Partial | Absent | Cannot tell |
|---|---|---:|---:|---:|---:|---:|
| 1 | Company creation & configuration (F11/F12) | 4 | 0 | 3 | 1 | 0 |
| 2 | Accounting masters | 10 | 0 | 7 | 3 | 0 |
| 3 | Inventory masters | 12 | 0 | 10 | 0 | 2 |
| 4 | Voucher types (7.2's classic eighteen) | 18 | 18 | 0 | 0 | 0 |
| 5 | Voucher behaviours & edit verbs | 7 | 4 | 1 | 1 | 1 |
| 6 | Statutory, current law (GST, TDS/TCS, salary IT) | 10 | 3 | 6 | 0 | 1 |
| 7 | Payroll | 5 | 2 | 2 | 0 | 1 |
| 8 | Banking | 9 | 2 | 1 | 5 | 1 |
| 9 | Inventory / manufacturing / job work (post-7.2) | 5 | 2 | 0 | 1 | 2 |
| 10 | Accounting features (post-7.2) | 2 | 0 | 0 | 2 | 0 |
| 11 | Reports | 12 | 6 | 6 | 0 | 0 |
| 12 | Printing | 5 | 0 | 2 | 3 | 0 |
| 13 | Data management (import/export/backup/e-mail) | 5 | 1 | 3 | 1 | 0 |
| 14 | TallyPrime-only capabilities | 11 | 4 | 3 | 4 | 0 |
| | **TOTAL** | **115** | **42** | **44** | **21** | **8** |
### 1.2a 🔴 THE NAMED CAPABILITY LIST — added 2026-08-18, and this document has never had one before

**Why this exists.** Before today a reader could learn that **twenty-one capabilities are absent** and could not
learn **which twenty-one**. That made the document unactionable in the one direction it exists to serve. Every
row below carries a state, and §1.2's integers are the column sums of these rows.

**How to read a state.** `COMPLETE` = a type exists, a route reaches it, and something calls it — **present and
reachable, never *correct***. `PARTIAL` = present and reachable with a **named** missing piece. `ABSENT` = no
type, no route, no caller; every absent row rests on a search that returned zero. `UNDETERMINED` = nobody has
checked; there are none today and §1.2b says why that is a fact about today and not a property of the list.

**Conventions, so a later reader is not misled by the shape of the table.**
- **A capability is counted ONCE** (§1.1 rule 3). Where two surveys named the same capability under different
  areas, the row lives in one area and the other area carries a **cross-reference that is explicitly not
  counted**. Each such row says so.
- **Where the surveys DISAGREED on a state, the row says so and gives both readings** before recording one.
  Nothing was reconciled silently.
- **Family compression survives** (§1.1 rule 2): Account Books, Inventory Books, Statements of Accounts and
  Exception Reports remain one row each, and each row names what is hiding inside it.
- **Evidence is written as `file` + `member`, never as a live line citation**, because a line number in this
  document goes stale on the next edit and a stale citation is the defect this project has now caught three
  times. The five 2026-08-18 surveys hold the line-level evidence.

**▶ 🔴 CHECK THE ARITHMETIC RATHER THAN TRUSTING IT — every state below is machine-countable, and it was
machine-counted before §1.2 was written.** Each area heading states its own split, every row carries a
single bare state token in its third column, and the totals in §1.2 are the column sums. Re-derive with:

```
awk '
function flush(){ if(area!=""){printf "%-52s rows=%2d  C=%2d P=%2d A=%2d U=%2d\n",substr(area,1,52),n,c,p,a,u; tc+=c;tp+=p;ta+=a;tu+=u;tn+=n} }
/^#### Area /{ flush(); area=$0; c=0;p=0;a=0;u=0;n=0; inarea=1; next }
/^#### 1\.2b/{ flush(); area=""; inarea=0 }
inarea && /^\| [0-9]+\.[0-9]+ \|/{ n++;
  if ($0 ~ /\| COMPLETE \|/) c++; else if ($0 ~ /\| PARTIAL \|/) p++;
  else if ($0 ~ /\| ABSENT \|/) a++; else if ($0 ~ /\| UNDETERMINED \|/) u++;
  else print "  !! UNPARSED:", substr($0,1,80) }
END{ printf "\nTOTAL rows=%d C=%d P=%d A=%d U=%d sum=%d\n",tn,tc,tp,ta,tu,tc+tp+ta+tu }
' docs/full-clone-census.md
```

**Run 2026-08-18: `TOTAL rows=200 C=47 P=95 A=58 U=0 sum=200`, and every area matched its own heading.** If
this ever disagrees with §1.2, **the rows are right and §1.2 is the defect** — that is the whole point of
deriving one from the other.

**▶ 🔴 EXPECTED RUN FROM 2026-09-05 (THE b1/b3/b4/b5 LANDING) ONWARD: `TOTAL rows=216 C=52 P=105 A=59 U=0 sum=216`.**
This supersedes the 2026-09-04 expectation restated immediately below, which was `216 C=46 P=102 A=68 U=0`.
**NINE rows moved off `ABSENT` and nothing else moved: `+6 complete, +3 partial, −9 absent`, `+0 rows`.**
To `COMPLETE`: **1.4** (company rename + delete), **5.4** (Alt+2 duplication), **11.6** (the five registers),
**11.7** (Group Summary / Group Vouchers), **11.8** (Statistics), **13.10** (the file/folder chooser).
To `PARTIAL`: **7.16** (payroll alter/delete, **4 of 8**), **12.4** (print config: reports yes, documents
copies-only), **13.6** (six of the vendor's seven export formats; JPEG still missing).
🔴 **EVERY ONE OF THE NINE WAS MOVED ONLY AFTER THE CAPABILITY WAS DRIVEN TO BY A USER GESTURE** — a menu row,
a dispatch case and a realised control, not a service method with a test. That is the whole standard of this
pass, and it is why **rows 6.10 and 6.13 did NOT move even though the code that closes them is written,
reviewed and gate-green**: the branch carrying them (b2) was held on a red ubuntu leg and **is not on `main`**,
so on `main` the capability is still unreachable. Rows **14.9**, **5.5**, **12.6** and **12.7** likewise stayed
`ABSENT` against their own branches' initial claims — see each row. The command is unchanged and was not
weakened to fit; **§1.2's integers were RE-SUMMED by re-running it, never edited.**

**▶ The superseded 2026-09-04 expectation, kept as the record it was:** `TOTAL rows=216 C=46 P=102 A=68 U=0 sum=216`.
This supersedes the earlier-same-day expectation quoted in the next paragraph. **ONE row moved and nothing else
did, and IT MOVED DOWN — the first downward state move this year:** row **9.3** (Job Work registers) went
`COMPLETE` → `PARTIAL` when §1.3 item **23** gave areas 9 and 10 their first comparison and counted **four
shipped registers against the vendor's eleven**, on a per-name zero-hit grep for each of the missing seven.
**Nothing was un-built** — the census was wrong about 9.3, exactly as it had been about 16.6, 5.1, 12.8, 16.3
and 16.4. The command is unchanged and was not weakened to fit — the same two patterns parse the same rows;
only one state token differs. Delta: `+0 rows, −1 complete, +1 partial, −0 absent`. **Run actually performed
2026-09-04 against this file after the fold-in; its literal output is the line above, and area 9's own heading
matches it (`rows= 9 C= 3 P= 2 A= 4 U= 0`).** 🔴 **Four OTHER rows had their evidence corrected in the same
pass without moving a state — 3.4, 2.4, 2.12 and 2.5 — and row 9.9's TITLE was found to name a Tally.ERP 9
feature TallyPrime does not have. A false evidence cell under a correct grade is still a defect;** see §1.3
items 19–23 and §2 TIER 3.

**▶ 🔴 EXPECTED RUN FROM 2026-09-04 (EARLIER, WAVE-2 FOLD-IN) — SUPERSEDED BY THE PARAGRAPH ABOVE:
`TOTAL rows=216 C=47 P=101 A=68 U=0 sum=216`.** This supersedes the
2026-09-03 expectation quoted in the next paragraph. **Three rows moved and nothing else did:** **12.8** (print-engine
capability floor), **16.3** (Tally Audit / Edit Log) and **16.4** (attribution on the lifecycle verbs) went `ABSENT`
→ `PARTIAL` when the wave-2 breadth pass re-measured them and the integrator re-ran each grep independently. **Nothing
was built** — all three were already PARTIAL and the census was wrong about them; see §2 TIER 3. The command is
unchanged and was not weakened to fit — the same two patterns parse the same rows; only three state tokens differ.
Delta: `+0 rows, +0 complete, +3 partial, −3 absent`. **Run actually performed 2026-09-04 against this file; its
literal output is the line above, and areas 12 and 16 both match their own headings (`rows= 9 C= 1 P= 4 A= 4 U= 0`
and `rows= 7 C= 0 P= 3 A= 4 U= 0`).** 🔴 **Row 6.20 was ALSO re-measured that day and did NOT move: its `ABSENT`
grade is correct and only its evidence sentence was false. A falsifiable evidence cell under a correct grade is
still a defect — it invites a reader to distrust the grade — and it is fixed in place rather than left.**

**▶ 🔴 EXPECTED RUN FROM 2026-09-03 ONWARD (SUPERSEDED 2026-09-04 — see the paragraph above):
`TOTAL rows=216 C=47 P=98 A=71 U=0 sum=216`.** This supersedes the
2026-08-20 expectation quoted in the next paragraph. **One row moved and nothing else did:** row **6.4** (the GST
rate hierarchy above the Stock Item) went `ABSENT` → `PARTIAL` when T0-4 slices **S1/S2a/S2b** shipped the
resolution half. The command is unchanged and was not weakened to fit — the same two patterns parse the same rows;
only one row's state token differs. Delta: `+0 rows, +0 complete, +1 partial, −1 absent`. **Run actually performed
2026-09-03 against this file; its literal output is the line above, and area 6's own heading matches it
(`rows=42 C=18 P=15 A= 9 U= 0`).** 🔴 **Row 3.13 — the CAPTURE half of the same defect — did NOT move and is still
`ABSENT`;** if a later reader finds 6.4 `PARTIAL` and assumes T0-4 is closed, 3.13 is the row that says otherwise.

**▶ 🔴 EXPECTED RUN FROM 2026-08-20 ONWARD (SUPERSEDED 2026-09-03 — see the paragraph above):
`TOTAL rows=216 C=47 P=97 A=72 U=0 sum=216`.** This supersedes the
2026-08-19 expectation quoted in the next paragraph. **One row moved and nothing else did:** row **5.1**
(voucher alteration) went `ABSENT` → `PARTIAL` when Phase 10.11 **S5a–S5e** shipped the capability. The command
is unchanged and was not weakened to fit — the same two patterns parse the same rows; only one row's state
token differs. Delta: `+0 rows, +0 complete, +1 partial, −1 absent`. **Run actually performed 2026-08-20
against this file, and its output is the line above.**

**▶ 🔴 EXPECTED RUN FROM 2026-08-19 ONWARD (SUPERSEDED 2026-08-20 — see the paragraph above):
`TOTAL rows=216 C=47 P=96 A=73 U=0 sum=216`.** Areas **15** and
**16** were added that day by user ruling 10, and they parse under the same two patterns this command already
uses — the `^#### Area ` heading and the `^| N.N |` row — so **the command is unchanged and was not
weakened to fit.** The delta is exactly `+16 rows, +0 complete, +1 partial, +15 absent`.

#### Area 1 — Company creation & configuration (F11/F12) · 9 rows · 1 complete / 6 partial / 2 absent

| # | Capability | State | Evidence · gap · disagreement |
|---|---|---|---|
| 1.1 | Company Creation — the profile capture screen | PARTIAL | Twelve bound fields on `CompanyProfileViewModel`, with corpus-matched labels. **Gap:** the five contact fields, the three base-currency formatting toggles, "decimal places for amount in words" (no domain property), the whole Security Control heading, Directory, and Group Company / Alt+R. Post-save hands off to the Gateway, not to F11 — a departure recorded in §1.3 row 9. |
| 1.2 | Company Alteration (Gateway → Masters → Alter Company) | PARTIAL | Same view model; eleven editable fields; accept path verified. **Gap:** company Name is read-only (a storage constraint of ours). **No Alt+K company menu** — that chord is bound to Saved Views and only in report context (14.9). ⚠️ §1.3 row 9 cites that binding at `MainWindow.axaml.cs` line 653; **at HEAD it is line 757** — content drift, not a dangling citation. |
| 1.3 | Company Select / open an existing company | PARTIAL | Enumerates stored companies plus Create Company (F3) and Load Robert Demo. **Gap:** no named **Shut Company** — zero `Shut` hits in `src/Apex.Desktop`; closing is a side effect of Esc collapsing the cascade. |
| 1.4 | Company Rename and Company Delete | COMPLETE | 🔴 **MOVED `ABSENT` → `COMPLETE` 2026-09-05 (b4 landing, PR #47).** The old evidence — *"`CompanyStorage.Delete(CompanyEntry)` … has zero callers … dead code. No rename code at all, no Screen member, no menu row"* — is now false in every clause. **Both halves were driven end-to-end by keystroke, not inferred from the service layer. Rename:** Gateway → Masters → Alter Company → type in the Name box → `Ctrl+A`; the `.db` is MOVED (the old file is gone), the picker then shows exactly one book, and the status line re-syncs. `CompanyProfileViewModel.IsNameEditable` / `TryRename`, plus **the `TextBox` in `MainWindow.axaml` that makes it typeable at all** — the control IS the feature; without it the storage-level rename was unreachable. **Delete:** `Alt+D` on that screen names the company in the confirmation and removes nothing until `Y`; `Y` deletes the `.db`, releases the open aggregate and lands on Company Select, `N` keeps the book and stays put. `MainWindowViewModel.PerformOpenCompanyDeletion` behind `DeletionTarget.Company`, with a test pinning that `Alt+D` elsewhere still deletes the MASTER, not the company. ⚠️ **This closes the row AS TITLED and nothing more.** Row **14.9** (the Alt+K company menu) stays `ABSENT`, and `Shut Company` is still unbuilt — it survives only as a private `ReleaseOpenCompany()` with a single caller (the delete, which cannot leave the shell holding an aggregate whose file is gone). ⚠️ **Knock-on: T2-37(f) — *"Company Alteration's read-only Name is a storage constraint of ours"* — is now STALE and is corrected in place there.** |
| 1.5 | F11 Company Features — the Statutory Configuration page | PARTIAL | `GstConfigViewModel`, 21 observable switches, titled "Statutory Configuration (F11)"; hosts GST, TDS, TCS, PF/ESI/PT, salary TDS, gratuity, bonus. **Gap:** one flat page, not Tally's F11 group structure. |
| 1.6 | F11 Company Features — Inventory & Payroll feature toggles | PARTIAL | Batch-wise details, BOM + component type, multiple Price Levels, Job Order Processing, Maintain Payroll + Payroll Statutory, each applied live. **Gap:** 🔴 `Company.WarnOnNegativeStock` is persisted and honoured by `InventoryPostingService` with **zero** hits in `src/Apex.Desktop` — shipped behaviour with no control anywhere (the W0-5 row, still unshipped). `UseSeparateActualBilledQuantity` is toggled from the **voucher-entry** screen, not from here (see 3.14). No Integrate Accounts with Inventory, no maintain mode. |
| 1.7 | F11 Company Features — the Accounting Features group | ABSENT | Zero `IntegrateAccountsWithInventory` and `MaintainMode` hits in `src/` or `tests/`. No per-company switch for bill-wise, interest, cost centres, multi-currency, budgets, credit limits, cheque printing or multi-address. **= T1-15.** |
| 1.8 | F12 Configure — the global configuration tree | ABSENT | `F12Configure()` has three real arms and then a fall-through that literally sets a stub message string. No configuration-tree type, no Screen member, no menu row. **= T1-16.** |
| 1.9 | F12 Configure — per-screen context panels | PARTIAL | Four real panels exist off the key tunnel: print-preview config, report config, Alt+F12 report sort/filter, and the Ledger-master / voucher-numbering arms. **Gap:** every other screen — all master screens except Ledger, every voucher screen except through the numbering column, every report surface with no `Reports` object — falls through to the stub. |

#### Area 2 — Accounting masters · 13 rows · 0 complete / 8 partial / 5 absent

| # | Capability | State | Evidence · gap · disagreement |
|---|---|---|---|
| 2.1 | Accounting Group master — create / alter / delete | PARTIAL | All three verbs verified: create from the Create column, `ForAlter` from a Chart-of-Accounts row, Alt+D delete guarded by `MasterDeletionRules.EnsureGroupDeletable`. **Gap:** no Display verb, no multi-group create, and the v51 group-level GST block has no capture field (3.13). |
| 2.2 | Group behavioural flags — sub-ledger, nett debit/credit, used for calculation, allocation method | ABSENT | All four identifiers return zero hits over `src/`. `Group` carries only Id, Name, Nature, ParentId, Alias, IsPredefined and Gst. ~~⚠️ Tally's field names for these are themselves **UNVERIFIED** against this corpus (§6 item 6) — the absence from our code is confirmed; what Tally calls them is not.~~ 🔴 **CAVEAT DISCHARGED 2026-09-04 (§1.3 item 19). All four names are VENDOR-VERBATIM on the official Groups page — the guesses were right — and a FIFTH attested flag, *"Does it affect Gross Profits"*, joins this row.** ⚠️ **But note what blocks it by construction:** a new **PRIMARY** group cannot be created at all in our product (`ParentOptions` holds only existing groups; both `Create()` and `GroupService.CreateGroup` refuse a null parent), and *Nature of Group* / *Does it affect Gross Profits* live on the primary-group screen — so **this row cannot be closed without fixing that first** (= **T1-31**). In T2-3. |
| 2.3 | Ledger master — create / alter / delete | PARTIAL | ~30 bound fields; all three verbs, delete guarded by `EnsureLedgerDeletable`. **Gap:** Alias is deliberately not capturable (named in the view model's own "not written, on purpose" list); no credit limit (10.1); no multi-address (10.2); no multi-ledger create (2.12); no Display verb. |
| 2.4 | Voucher Type master — create / alter / display / delete | ABSENT | No `VoucherTypeMasterViewModel` among the ~110 view models; no Screen member; no Create-menu row; `MasterCreateKind` has no member for it. Exactly **one** of `VoucherType`'s ~20 configurable properties is settable anywhere in the UI (`TrackAdditionalCosts`, from the purchase-invoice screen). ⚠️ **Area assignment ambiguous** — two surveys named this capability, one under Accounting masters and one under Voucher behaviours. Counted **once, here**; area 5 carries an uncounted cross-reference. 🔴 **EVIDENCE CORRECTED 2026-09-04 (§1.3 item 19) — the grade `ABSENT` is right and the prose under it was FALSE AT HEAD.** The cell said ~~*"exactly **one** … is settable anywhere in the UI"*~~; `grep -n "AllowZeroValuedTransactions" src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs` returns **994/997/998** — a live get/set on the persisted type. **It is two on that screen and six across two screens.** **= T1-3.** |
| 2.5 | Voucher numbering configuration (F12 per voucher type) | PARTIAL | Prevent-duplicate, number width, prefill-with-zero and the prefix/suffix affix rows, on their own Screen. **Gap:** `MethodDisplay` is a get-only expression-bodied string with no setter and no picker in the XAML, so no method is selectable and every seeded type stays Automatic (5.10, **T1-5**). 🔴 **THE TARGET OPTION-SET IN THIS CELL WAS ITSELF WRONG, CORRECTED 2026-09-04 (§1.3 item 19).** It measured us against `{Automatic, Manual, None}` and said ~~*"Manual and None are unreachable"*~~. **The vendor's set is `{Automatic, Automatic (Manual Override), Manual, Multi-User Auto}`** — *None* is not in it and **two of the four have no domain member at all** (see T2-16, which this confirms). **A gap measured against the wrong target list cannot be closed.** Related defect: our prefix/suffix affixes and Prevent-duplicate are offered on numbering methods the vendor **excludes** them from (= **T2-21**). |
| 2.6 | Voucher Class | ABSENT | Zero `VoucherClass` hits in `src/Apex.Ledger` and zero in `src/Apex.Desktop`. No domain type, no persistence table, no view model, no Screen, no menu row. Interest auto-posting via a Debit/Credit-Note class is unreachable in consequence. ⚠️ Same area ambiguity as 2.4; counted once here. |
| 2.7 | Cost Category master | PARTIAL | Create only (name + the two allocate flags). No `ForAlter` and no highlighted-row route, so the existing rows carry no route; no delete service exists in `src/Apex.Ledger/Services`; no Display verb. |
| 2.8 | Cost Centre master | PARTIAL | Create only (name, category, parent). `CostCentre.Alias` is never captured. No Alter, no delete service, no delete route, no Display verb. |
| 2.9 | Budget master | PARTIAL | Create only, with lines targeting a Group or a Ledger. **Gap:** no cost-centre target (the target option carries no cost-centre id); no nested budget (the `UnderId` has no picker); no Alter; no Delete service. |
| 2.10 | Scenario master | PARTIAL | Create only (name, include-actuals, a tick-list of voucher kinds). **Gap:** `Scenario.ExcludeType` has zero Desktop callers, so an exclusion can arrive only through import; no period; no Alter; no Delete. |
| 2.11 | Currency master and Rates of Exchange | PARTIAL | Create for a currency and for a dated rate, with Existing and Rates lists; per-ledger currency selection exists. **Gap:** no Alter and no Delete for either (`RemoveCurrency` / `RemoveExchangeRate` have no Desktop caller); none of the four Tally currency formatting options exists here or on the company base-currency block. |
| 2.12 | Multi-master create (Multi Ledger / Multi Group) | ABSENT | ~~Zero `Multi Ledger` / `MultiLedger` hits over `src/`.~~ 🔴 **GREP CORRECTED 2026-09-04 (§1.3 item 19) — the grade `ABSENT` is right and the search targeted a Tally.ERP 9 name the reference product no longer uses.** Replacement, which also returns zero: `grep -rniE "multi[- ]?master|multi create|multi alter|Alt\+H" src/ --include=*.cs --include=*.axaml`. The Create column contains only single-master rows and the label dispatch has no multi-create case. In T2-3. |
| 2.13 | Show Inactive / hidden masters | ABSENT | Zero `Show Inactive` / `ShowInactive` hits over `src/` except one comment in `VoucherTypeResolver.cs` recording that the gesture "meant nothing". Every master's Existing list is an unconditional enumeration. Overlaps the Show-Inactive element of 5.11; the master-level capability is counted here, the voucher-type flag there. |

#### Area 3 — Inventory masters · 15 rows · 2 complete / 11 partial / 2 absent

| # | Capability | State | Evidence · gap · disagreement |
|---|---|---|---|
| 3.1 | Stock Group master | PARTIAL | Create only (name, alias, under, add-quantities). No Alter; `InventoryService.DeleteStockGroup` has **zero** hits in `src/Apex.Desktop`. |
| 3.2 | Stock Category master | PARTIAL | Create only. No Alter; `DeleteStockCategory` has zero Desktop hits. |
| 3.3 | Stock Item master — create / alter / delete | PARTIAL | The **only** inventory master with all three verbs; delete guarded by `EnsureStockItemDeletable`. **Gap:** no Display verb, no multi-item create, plus 3.4 and 3.6. |
| 3.4 | Stock item valuation method — Standard Cost | PARTIAL | 🔴 The method **is** selectable on the master screen — the dropdown is populated with all six methods and **is rendered** — and the create path passes the selection through unguarded, ~~but **there is no bound input for the `StandardCost` value**, so valuation silently falls back to last purchase rate.~~ 🔴 **THAT SENTENCE IS FALSE AT HEAD — CORRECTED 2026-09-04 (§1.3 item 19).** `grep -n "StandardCostText" src/Apex.Desktop/Views/MainWindow.axaml` → **6612**, a two-way-bound `TextBox`, with save-blocking validation at `StockItemMasterViewModel.cs:441-461`. **The grade `PARTIAL` is still right and is now re-based on the real gap:** we offer **six** costing methods where the vendor documents **nine**, and the whole **Market Valuation** field is absent — and `LastSaleCost` is offered as a *closing-stock costing* method when the vendor files *Last Sales Price* under **Market Valuation**, i.e. as a selling-price default (that is **T0-2**, and this is its fidelity grounding). **This CORRECTS T0-3's "reachable only through JSON/XML import" caveat**; see the T0-3 row and §1.3's anchor block. |
| 3.5 | Unit of Measure master (simple and compound) | PARTIAL | Create for both shapes, persisted. **Gap:** no Alter and no Delete — the list row type carries no Guid, so no row can address a unit, and `DeleteUnit` has zero Desktop hits. |
| 3.6 | Alternate units per stock item | ABSENT | Zero `AlternateUnit` / "Alternate Unit" hits over `src/`. `StockItem` carries a single base unit and `VoucherInventoryLine` has no alternate-unit quantity. In T2-3. |
| 3.7 | Godown / Location master | PARTIAL | Create only (name, alias, under, third-party). **Gap:** no "Allow storage of materials" (zero hits — ~~⚠️ the Tally field name is UNVERIFIED per §6 item 6~~ 🔴 **CAVEAT DISCHARGED 2026-09-04 (§1.3 item 19): the field name is vendor-verbatim on the official Godown page; the guess was right**), no address block, no Alter, no Delete route. |
| 3.8 | Batch / Lot master | PARTIAL | Create with manufacturing/expiry dates or expiry period, opening quantity and rate; menu row gated on the F11 batch flag. No Alter, no Delete route. |
| 3.9 | Bill of Materials master | PARTIAL | Create with component lines, By-Product/Co-Product/Scrap typing and carve-out rate/percent; gated on the F11 BOM flag. No Alter, no Delete route. ⚠️ **Counted once here**; a second survey named the same capability under area 9, which carries an uncounted cross-reference. |
| 3.10 | Price Level master | PARTIAL | Create (name only) with an Existing list, gated on the F11 price-level flag. No Alter, no Delete route. |
| 3.11 | Price List — dated slab rates per level and item | PARTIAL | Slab rows (from/to quantity, rate, discount), an applicable-from date and a version history. **Gap:** revision is by saving a **new dated version**, not by altering one; no route deletes a list or a version. |
| 3.12 | Reorder Levels master | PARTIAL | Create with scope (item / group / category), simple or advanced quantities, consumption period and Higher/Lower criteria. **Gap:** alteration is an **upsert only** — creating for an existing scope+target replaces it; no Alter screen, no Delete route. |
| 3.13 | GST details capture on the Stock Group and accounting Group masters (the v51 hierarchy levels) | ABSENT | 🔴 The **storage** shipped — `MasterGstDetails` on `Group` and `StockGroup`, `DefaultGst` on `GstConfig`, the v51 columns in `Schema` — and **`MasterGstDetails` has exactly ONE hit in `src/Apex.Desktop`, a doc comment**: `Services/CompanyStorage.cs` line 95 names `MasterGstDetails.EnsureValid` while explaining why the validation floor sits in the storage choke point. **There is no view-model property and no XAML field**, and both master screens show only name/alias/under. The only writer is the importer. **This is the UI half of T0-4** and the reason that defect is still open. *(🔴 **Wording corrected 2026-08-18. The GRADE is unchanged and correct** — ABSENT is about no view-model property, no route and no caller, and a doc comment is none of those. This cell said **"zero hits"**, which is falsifiable by one grep and was false; a reader who ran it would have had grounds to distrust the row's evidence rather than its wording. The single hit is a comment, so nothing about the capability changes.)* 🔴 **RE-MEASURED 2026-09-03, AND THE GRADE IS UNCHANGED — READ THIS BEFORE INFERRING ANYTHING FROM ROW 6.4's MOVE.** T0-4 slices S1/S2a/S2b shipped the **resolution** half and row **6.4** moved `ABSENT` → `PARTIAL` on that strength. **This row did not move and must not be read as having moved with it.** Re-run this pass: `AccountGroupMasterViewModel.cs` and `StockGroupMasterViewModel.cs` contain **zero** occurrences of `Gst` or `GST` in any casing, and there is no GST field on either master template in `MainWindow.axaml`. The only writer of `Group.Gst` / `StockGroup.Gst` is still the canonical importer. **The consequence is now sharper than it was, not softer:** before S2 those blocks were written and never read, so the gap was inert; the walk now READS them at transaction time, so a rate an operator cannot type is a rate the resolver will honour only for imported books. **T0-4 is not closed while this row is `ABSENT`** — capture is slices S3 (company) and S4 (Stock Group + accounting Group, which must also add the Stock Group ALTER route that does not exist at all).)* |
| 3.14 | Actual vs Billed quantity | COMPLETE | 🔴 **SURVEY DISAGREEMENT, RECORDED NOT RECONCILED AWAY.** One survey graded **PARTIAL** because the enabling switch is a checkbox on the **voucher-entry** screen that mutates the Company rather than an F11 Company Features row; another graded **COMPLETE** on domain member + rendered control + consuming caller. **COMPLETE is recorded, because §1.2's criterion is existence and reachability and the switch is reachable** — the placement objection is a **§1.3 fidelity** finding and is logged as one in 1.6's gap column. Was one of the eight cannot-tell rows. |
| 3.15 | Additional Cost of Purchase (landed cost) | COMPLETE | Same disagreement and the same resolution. Apportionment service with per-purchase and per-transfer entry points, landed value and landed unit rate, UI rows, canonical IO and SQLite all present. The enabling flag is `VoucherType.TrackAdditionalCosts`, toggled **in place on the purchase-invoice screen** precisely because there is no Voucher Type master (2.4) — recorded as a fidelity departure, not as a missing capability. Was one of the eight cannot-tell rows. |

#### Area 4 — Voucher types (7.2's classic eighteen) · 18 rows · 5 complete / 13 partial / 0 absent

> **▶ READ THE CRITERION BEFORE READING THE SPLIT.** On pure **existence and reachability** — §1.2's stated
> criterion — all eighteen are seeded and every one has a menu row and a shortcut hint. That is exactly what the
> superseded table's `18 / 0 / 0 / 0` recorded, and it was **not wrong on its own terms**. Graded on whether the
> kind **does what a user names it for**, five are COMPLETE and thirteen are PARTIAL, and **eight of the thirteen
> fail for one shared structural reason** — the two-collection defect now filed as **T1-17**. The PARTIALs below
> are therefore "present and reachable, behaviour defective", not a contradiction of the old count.
> ⚠️ One survey's prose said *twelve* of the eighteen are PARTIAL while its own rows list **thirteen**; the rows
> are used and the prose is not.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 4.1 | Contra (F4) — entry, posting, Single-Entry mode | COMPLETE | Seeded, menu row with the F4 hint, entry screen, Ctrl+H single entry reachable. |
| 4.2 | Payment (F5) — entry, posting, Single-Entry mode | COMPLETE | As above. |
| 4.3 | Receipt (F6) — entry and posting | COMPLETE | As above. |
| 4.4 | Journal (F7) — entry and posting | COMPLETE | As above. |
| 4.5 | Sales (F8) — three entry modes and tax-invoice print | COMPLETE | Mode switching and the invoice print gate both verified. |
| 4.6 | Purchase (F9) — three entry modes and supplier-document print | PARTIAL | Entry and posting present; the print routing asks the *entitlement* predicate a *rendering* question, so a Purchase item-invoice silently falls back to a Dr/Cr voucher print **with zero item detail**. **= T0-11.** 🔴 **LOCATOR AND CAUSE CORRECTED 2026-08-20 — see T0-11's evidence cell.** This cell used to say *"the print gate refuses anything whose base kind is not Sales"*, which reads as though the gate were the defect. It is not: Sales-only is the **correct** answer to *"may we ISSUE a Rule-46 tax invoice?"* (CGST §31(1)). The defect is the **call site** at `src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107`. ✅ **THE ITEM-INVOICE HALF IS CLOSED 2026-08-20 (Phase 10.13 slice S2).** A Purchase item invoice now prints as a recipient-side **RECORD**: item detail from `voucher.InventoryLines`, the SUPPLIER heading the document (CGST Rule 46(a)), the tax he charged stated and captioned as his, and place of supply, our declaration and our signature suppressed. 🔴 The title `PURCHASE RECORD` and the legend are **OURS (ruling 9)** and can never join the corpus-verified set. **STILL PARTIAL:** a purchase **accounting (service)** invoice takes the other projection pass and still prints the plain voucher — slice S3. |
| 4.7 | Credit Note (Alt+F6) — sales return | PARTIAL | §34 original-invoice capture present. **Moves no stock** (**T0-10**). 🔴 **RE-ATTRIBUTED 2026-08-20 — THE T0-11 HALF OF THIS ROW IS REFUTED.** It read ~~*"and **never prints in invoice format** (**T0-11**)"*~~, which blames the print gate. **A Credit Note cannot carry inventory lines AT ALL**, so the print gate is not what stops an item table appearing: `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` throws *"Item-invoice stock lines are only valid on a Purchase or Sales voucher"* on **every** post (reached from `src/Apex.Ledger/Services/VoucherValidator.cs:150-151`), and `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` makes the item-invoice chord inert on this family. **That wall is T0-10, not T0-11** — and flipping the print gate alone would route a note into the invoice projection and emit a **ZERO-ROW document**. ✅ **AND THE NOTE DOCUMENT DOES NOT NEED THE WALL REMOVED:** CGST **Rule 53** is value-level (nature of the document · corresponding invoice serial and date · value, rate and amount credited/debited — no HSN, no quantity, no per-item lines), so the legally complete note is **RQ-11b** and it ships with **no dependency on T0-10**. *(Verified first-hand at those exact lines on 2026-08-20 before being written down.)* ✅ **THE NOTE DOCUMENT IS SHIPPED (Phase 10.13 slice S4)**: a sales-return credit note linked to an original **Sales** invoice prints as a value-level `CREDIT NOTE` — nature of the document, the corresponding invoice serial and date under the caption *"Original Invoice No."*, and the value, rate and amount credited — with `Items` legitimately empty. **Entitlement is THREE-valued:** an absent discriminator (a consolidated-party reference, which ER-12 supports) is titled **nothing**, because guessing *recorded* there would title our own §34(1) note as our customer's. **Still PARTIAL for the stock half (T0-10) only.** |
| 4.8 | Debit Note (Alt+F5) — purchase return | PARTIAL | ~~Same two gates, same two defects.~~ 🔴 **CORRECTED 2026-08-20, WITH 4.7 AND FOR THE SAME REASON:** the stock half is **T0-10**; the print half is **NOT T0-11** — the same validator throw and the same inert chord apply. **AND THIS FAMILY CARRIES A DISTINCTION 4.7 DOES NOT:** entitlement is **not** decided by the base type. CGST **§34** puts the note on *"the registered person **who has supplied**"*, so a Debit Note raised for a **purchase return** is a **RECORD** of our supplier's credit note, while one raised for an **upward revision of our own sale** is a document we are obliged to issue. See **RQ-11b** and `docs/adr/0002-printed-document-three-axis-split.md`. |
| 4.9 | Stock Journal (Alt+F7) — inter-godown transfer | PARTIAL | Posts to the **separate** `InventoryVoucher` aggregate: never appears in the Day Book, not drillable, and cannot be cancelled or deleted from any surface. **= T1-17.** |
| 4.10 | Physical Stock (Ctrl+F7) — physical count | PARTIAL | Same aggregate, same three consequences; the Physical Stock Register exists. |
| 4.11 | Sales Order (Ctrl+F8) | PARTIAL | Order Register exists; absent from the Day Book, no lifecycle verb. |
| 4.12 | Purchase Order (Ctrl+F9) | PARTIAL | As above. |
| 4.13 | Delivery Note (Alt+F8) | PARTIAL | Register exists; no Day Book row, no lifecycle verb, and no Tracking Number link to the Sales voucher (**T1-8**, 9.8). |
| 4.14 | Receipt Note / GRN (Alt+F9) | PARTIAL | As above, against Purchase. |
| 4.15 | Rejection Out (Ctrl+F5) | PARTIAL | Rejection Register exists; no Day Book row, no lifecycle verb. |
| 4.16 | Rejection In (Ctrl+F6) | PARTIAL | As above. |
| 4.17 | Memorandum — off-books entry, register, and conversion to a real voucher | PARTIAL | Type, menu row and register all present, and the engine's convert method exists. **Gap:** 🔴 the catalog's **conversion verb is unreachable** — the shell method and its own gate each have **zero production consumers**; the only callers are tests. A memo can be posted and never regularised. Filed as **T2-9**. |
| 4.18 | Reversing Journal — off-books entry with Applicable-Upto, register, scenario inclusion | PARTIAL | Capture and parse verified. **Gap:** no seeded shortcut, so it is menu-only; and with no voucher alteration the Applicable-Upto date can never be corrected after posting. |

#### Area 5 — Voucher behaviours & edit verbs · 15 rows · 6 complete / 6 partial / 3 absent

> **▶ 🔴 HEADING RE-DERIVED 2026-08-20.** It previously read ~~*"15 rows · 5 complete / 5 partial / 5
> absent"*~~. **Row 5.1 alone moved** (`ABSENT` → `PARTIAL`, Phase 10.11 S5a–S5e). The ABSENT set for this area
> is now **5.4, 5.5, 5.10, 5.11** — four, not five. Re-derived by re-running §1.2a's counting command, not by
> editing a digit; §1.2's area-5 row is the column sum of these fifteen rows and was re-summed with it.

> **Uncounted cross-references** (counted in the area named): **Voucher Type master → 2.4**; **Voucher Class →
> 2.6**; **the Day Book itself → 11.4**. The two-collection finding that governs eight of area 4's rows is
> carried in 11.4's gap column and filed as **T1-17**.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 5.1 | Voucher alteration — open a posted voucher, change it, re-save | PARTIAL | 🔴 **BUILT — S5a…S5e, and this row graded it ABSENT for a day after it shipped.** Engine `Replace` (S5a), `ForAlter` rehydration (S5b), the carve inversions and CARRY table (S5c), the `Ctrl+Enter` wiring on three surfaces (S5d, `a34d989`), and the narrowing that opened purchase item invoices and gave POS its own door (S5e, `b89213e`). **Fidelity record: §1.3 item 12** — that is where the comparison and its two R7 categories live, and this cell deliberately does not duplicate them. **Gap, named:** the **SALES ITEM INVOICE is still refused by name on every key**, against a corpus route that attests altering one from the Day Book and the Sale Register (§1.3 item 12); several families stay `DEFER-DEFERRED` (service GST advance receipt — a user ruling, not a slice; purchase accounting invoice). ~~*"and a re-accept still silently destroys a `BankAllocation` and a bill-wise `BillAllocations` on the legs named in T1-22 / T1-23"*~~ — **SUPERSEDED 2026-09-04: T1-22 and T1-23 are CLOSED**, the `BankAllocation` and the non-party bill-wise split are now CARRIED, a leg that moves is refused BY NAME, and the whole optional payload is pinned by a canonical-export byte comparison (`ItemInvoiceOptionalPayloadCarryTests`). **▶ 🔴 THE SUPERSEDED CELL, QUOTED SO THE CORRECTION IS CHECKABLE:** ~~*"ABSENT — Searched four ways, all zero: the detail view model exposes no alter or save member; `ForAlter` exists in exactly three master view models and no voucher one; the entry view model has zero `Alter`/`Duplicate`/`Insert` occurrences; Ctrl+Enter is bound to stock-item alteration and nothing else. No Screen member."*~~ **Three of those four limbs are FALSE at HEAD** (measured 2026-08-20): `ForAlter` is declared in **five** view models — `AccountGroupMasterViewModel`, `LedgerMasterViewModel`, `StockItemMasterViewModel`, **`VoucherEntryViewModel`** and **`PosBillingViewModel`** — so *"three master view models and no voucher one"* is wrong twice over; the entry view model has **70** `Alter`/`Duplicate`/`Insert` occurrences at `b89213e` (53 at `a34d989`), not zero; and `Ctrl+Enter` is bound to voucher alteration on three surfaces through `MainWindowViewModel.RequestAlterHighlightedVoucher`, not to stock-item alteration alone. Only the first limb (the detail view model exposes no alter or save member) is still true, and it is true **by design** — `VoucherDetailViewModel` is the read-only column. **T1-1's alteration half is CLOSED by this row; its duplication and insertion halves stand (5.4, 5.5).** |
| 5.2 | Voucher cancellation (Alt+X) on a posted voucher | PARTIAL | 🔴 **BUILT — S3.** Key arm, gate, confirmation, engine cancel, greyed Day Book row, CANCELLED over-print, live-IRN/e-Way refusal. **Gap:** armed on **one** surface (the live Day Book) where the corpus scopes it to "Vouchers & Reports"; resolves only through the accounting aggregate, so no stock/order voucher can be cancelled; no un-cancel and no Cancelled Voucher register. |
| 5.3 | Voucher deletion (Alt+D) on a posted voucher | PARTIAL | 🔴 **BUILT — S4.** Key arm, five surfaces, `MasterDeletionRules` guards, engine delete. **Gap:** cannot delete a stock/order voucher (same aggregate boundary); deleting the highest-numbered **unfiled** voucher reuses its number — a known and accepted residual, not a silent one; no numbering floor. |
| 5.4 | Voucher duplication (Alt+2) | COMPLETE | 🔴 **MOVED `ABSENT` → `COMPLETE` 2026-09-05 (b4 landing, PR #47).** `Alt+2` is a real arm in `MainWindow.axaml.cs` (`Key.D2` with `KeyModifiers.Alt`) routed through `MainWindowViewModel.RequestDuplicateHighlightedVoucher` → `VoucherEntryViewModel.ForDuplicate` → `OpenDrillColumn` → post. **13 tests drive real `Alt+2` keystrokes** on the Day Book, the register drill and the voucher-detail column, post a SECOND voucher leaving the original standing, and assert the button-bar badge is enabled on exactly those three surfaces and dimmed everywhere else. ⚠️ **AUTHORSHIP, recorded so the census credits the right pass:** this was written by the breadth agent that was killed mid-slice; the b4 finishing pass **verified it end-to-end and protected it, and did not build it**. The row moves on the strength of the verification, not of the authorship. |
| 5.5 | Insert Voucher (Alt+I) | ABSENT | Alt+I is spent on the POS tender-mode toggle. No insert-at-position code of any kind, no Screen member, no menu row. Corpus-attested and not built. |
| 5.6 | Add Voucher from a report (Alt+A) | PARTIAL | 🔴 **BUILT — and §1.3 item 12's grouping of Alt+A with the unbuilt Insert verb is wrong.** The key arm opens its own picker column beside the live report so the report survives, and the picker preserves the exact series. **Gap:** scoped to the Day Book alone, and the picker lists only active types, so an inactive series cannot be added. |
| 5.7 | Optional voucher (Ctrl+L) | PARTIAL | Flag, toggle, checkbox, key arm and balance exclusion all present. **Gap:** dispatched only on the accounting entry screen — inventory/order, POS, manufacturing-journal and job-work entries cannot be Optional, and `InventoryVoucher` has no Optional member at all; **a posted Optional voucher can never be regularised** (zero post-construction writers, no alteration screen); no Optional Voucher register. Filed as **T1-18.** |
| 5.8 | Post-dated voucher (Ctrl+T) | PARTIAL | Flag on both aggregates, dispatched to both entry screens, honoured by the balance walk. **Gap:** **zero post-construction writers**, so the flag can never be cleared when the cheque clears; no post-dated register or PDC summary (8.8). Filed as **T1-18.** |
| 5.9 | Automatic voucher numbering — date-effective affixes, width, prefill, prevent-duplicate | COMPLETE | Config screen, formatter, and enforcement on both posting services. |
| 5.10 | Voucher numbering method **Manual** / **None** | ABSENT | The method display is a get-only string, self-described "DISPLAY-ONLY this slice"; there is no setter and no bound control. The Voucher No. on all four entry screens is a `<Run>` inside a `TextBlock`, not a TextBox. The seed hard-codes Automatic throughout. **= T1-5.** 🔴 **THE ROW UNDERSTATES THE GAP, MEASURED 2026-09-04 (wave-2 core-accounting pass) — AND THIS RESIZES THE WORK, NOT JUST THE WORDS.** The official *"Method of Voucher Numbering"* field offers **five** methods (vendor page, verbatim): *Automatic* · **Automatic (Manual Override)** · *Manual* · **Multi-User Auto** · *None*. `src/Apex.Ledger/Domain/NumberingMethod.cs` has **exactly three** members — `Automatic`, `Manual`, `None` — so **two attested methods do not merely lack a picker, they have NO DOMAIN MEMBER AT ALL.** Building this row is therefore an **enum + persistence + migration** change, not "add a picker", and row 2.5's `MethodDisplay` gap inherits the same correction. ⚠️ **And one negative result recorded because it prevented a false divergence:** `None` **IS** attested — the Voucher Type master page omits it, the dedicated numbering-methods page carries it (*"Select this option to disable the voucher numbering"*), and stopping at the first page would have filed `None` as ours. |
| 5.11 | Voucher-type user flags — Use Common Narration, Print after saving, Show Inactive → activate | ABSENT | `VoucherType` has no common-narration and no print-after-saving member. "Show Inactive" returns exactly one hit in `src/`, a comment recording that the gesture meant nothing. The two inactive families are flipped only by `JobWorkService`; the other write site is a rollback restore inside a catch, not an activation route. |
| 5.12 | Voucher entry modes — As Voucher / Item Invoice / Accounting Invoice, and Single vs Double Entry | COMPLETE | 🔴 **CORRECTED 2026-09-04 (wave-2 core-accounting pass): "verified" IS THE WRONG WORD FOR THE `Ctrl+I` ARM, AND THE GRADE STILL STANDS.** This cell read ~~*"Ctrl+I and Ctrl+H arms verified"*~~. The `Ctrl+H` arm verifies — officially *"To change mode – open vouchers in different modes"* (Right button), and `src/Apex.Desktop/Views/MainWindow.axaml.cs:759` binds exactly that. **The `Ctrl+I` arm is a divergence:** officially `Ctrl+I` is *"To add more details to a master or voucher for the current instance"* — the **More Details** panel, which we do not have — and we spend the chord on the item-invoice toggle at `src/Apex.Desktop/Views/MainWindow.axaml.cs:744-750`. So mode switching is bound **twice**, once on its own chord and once on a chord belonging to a different feature, and the real `Ctrl+I` feature is thereby unreachable. **`COMPLETE` may stand — §1.2a's criterion is existence and reachability, and mode switching IS reachable via `Ctrl+H`** — but the fidelity defect belongs here and in the gap register (**T2-14**). The original evidence follows: Ctrl+I and Ctrl+H arms present; the change-mode gate was widened to Contra/Payment/Receipt so Single Entry is reachable on the three kinds that have it; the accounting-invoice mode is persisted structurally. |
| 5.13 | Bill-wise details on a voucher line (New Ref / Agst Ref / Advance / On Account) | COMPLETE | Per-line model with the sum-to-line-amount rule; the sub-panel renders on the plain grid, on Single Entry and on both invoice modes. |
| 5.14 | Cost-centre allocation on a voucher line | COMPLETE | Allocations drive the sub-panel, feed the posted entry line and are consumed by the cost reports. |
| 5.15 | Batch / lot allocation on a voucher line (FEFO/FIFO default, expiry warning) | COMPLETE | Its own cascade column, wired from both the accounting item-invoice path and the inventory entry path, with the engine's default issue selection. |

#### Area 6 — Statutory, current law (GST, TDS/TCS, salary IT) · 42 rows · 18 complete / 15 partial / 9 absent

> **🔴 THE OLD TABLE ALLOWED THIS AREA ZERO ABSENT CAPABILITIES.** Ten are evidenced below, each on a zero-hit
> search. That is the single largest correction in §1.2b item 2.
> **Uncounted cross-references:** *Connected GST — online e-Invoice* and *online e-Way Bill* were also named
> under area 14 by a different survey; both are counted here, at 6.14 and 6.15, because the offline artefact and
> the live submission are one capability with one gap. *IMS* likewise is counted here at 6.16.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 6.1 | F11 Statutory & Taxation — GST enable, GSTIN, home State, registration type, periodicity, composition sub-type + opt-in date | COMPLETE | Bound fields, the F11 route and the enable path that seeds slabs and the six tax ledgers. |
| 6.2 | GST rate / taxability / HSN-SAC on the Stock Item and on the sales-purchase ledger | COMPLETE | Both capture surfaces and the engine's resolver that consumes them. |
| 6.3 | Dated GST rate history and dated Compensation-Cess windows (the GST 2.0 rate framework) | COMPLETE | Its own setup screen with Ctrl+R, and dated resolvers for rate and cess. |
| 6.4 | GST rate hierarchy above the Stock Item — company default / Accounting Group / Stock Group, and the source-of-HSN and source-of-rate order options | PARTIAL | 🔴 **MOVED `ABSENT` → `PARTIAL` on 2026-09-03 — the RESOLUTION half shipped (T0-4 slices S1/S2a/S2b); the CAPTURE half and the HSN half did not.** This cell previously read, verbatim: *"Persistence exists; **no UI writes it and no service, report or view model reads it**. Resolution is still item → ledger → unresolved. **= T0-4**, and 3.13 is its capture half."* **The second clause of that sentence is now false and the first and third are still true.** What landed: `GstService` walks all five rungs — `Hierarchy` + `WalkFor` + the two `IReadOnlyList<HierarchyLevel>` order tables (`LedgerFirstWalk` / `StockItemFirstWalk`), transcribed from the two published order strings — with stop-at-first-hit, Company last, and the ER-5 unresolved sentinel moved from two rungs in to behind Company; `MasterAncestry.NearestGroupGst` / `NearestStockGroupGst` supply the two new ancestry rungs under a cycle guard; `ResolveDetailBlock` gives cess and reverse charge the same winning rung as the rate; and `SourceOfGstRate` finally has a reader outside persistence/Io (`GstService.Hierarchy`). **The named missing pieces — this is what keeps it PARTIAL, and none of them is a rounding:** (i) **no capture** — row **3.13** is still `ABSENT`, the Group and Stock Group masters still have no view-model property and no XAML field, and the importer is still the only writer, so an operator cannot type a rate at the rungs the walk now reads; (ii) `SourceOfHsnSacDetails` **still has no reader** — the HSN half is slice S5, `GstReportSupport.HsnSacOf` still takes only a stock item; (iii) ~~the five master-block rate bypasses pinned by drift lock D9 still read ONE hard-wired rung each and **nothing asserts they agree with `ResolveRate`** — two of them feed statutory payloads (see **T0-17**)~~ ✅ **CLOSED 2026-09-04: all five now resolve through `GstReportSupport.BucketingRateOf`, and the agreement assertion exists (**T0-17**). This clause is struck rather than deleted because it was the reason 6.4 stayed PARTIAL on 2026-09-03; the row remains PARTIAL on (i) and (ii) alone.** **= T0-4**, which is therefore NOT closed; see also **T0-18**, **T0-19** and **T0-20**, three hierarchy- or date-blind rate paths this chain unmasked and did not fix — ✅ **all three CLOSED 2026-09-04** (the import-of-services RCM rate and both POS resolutions now go through the dated `ResolveRate`; the dated override is keyed by `ResolveHsnSac`, which follows the same walk; the date-blind two-argument overload and the `?? 1800` RCM floor are both deleted). ⚠️ **That does not move this row: (i) and (ii) above are both still true.** **Fidelity record: §1.3 item 15.** |
| 6.5 | GST computation on a voucher — CGST/SGST vs IGST routing, per-rate line tax, cess, round-off leg | COMPLETE | Engine entry points with both desktop callers (voucher entry and POS). |
| 6.6 | Reverse charge (RCM) — inward dual leg, import of services, outward RCM flag, 3B tables 3.1(d) / 4(A)(2) / 4(A)(3) | COMPLETE | Service, live panel, supply-kind picker and the 3B projection. |
| 6.7 | GST on advance receipts — tax on advance, adjustment against invoice, GSTR-1 tables 11A / 11B | COMPLETE | Service, entry wiring and both projections. |
| 6.8 | GSTR-1 outward return on screen (period-scoped, printable and exportable) | PARTIAL | B2B 4A, rate-wise B2C, HSN 12, a single exempt bucket, 4B outward RCM, 9B credit/debit notes, 11A/11B. **Gap:** ~~seven~~ **NINE** form tables unmodelled — 5, 6A, 6B, 6C, 7, 8, 13, **and the e-Commerce Summary (14, 14A, 15, 15A)**; the B2C row type carries **no Place-of-Supply member at all**, which is what blocks 5 and 7. 🔴 **GAP LIST CORRECTED AND EXTENDED 2026-09-04 (§1.3 item 20), each on a measurement:** (i) the **e-Commerce Summary** was missing from this list entirely — `grep -rin "ecommerce\|e-commerce" src --include=*.cs` → **0**, and the vendor documents it as a live report section; (ii) ~~*"8's four-way split"*~~ **understates Table 8 — it is a 4 rows × 3 categories grid**, not a four-way split; (iii) **HSN Table 12 is listed among what is PRESENT while filing two blank statutory cells** — `Total value` and `Cess`, columns 6 and 11 of the statutory form, are absent from `Gstr1HsnRow` (the shipped set is 8 of 11). **The vendor's report presents 20 sections; we present 9.** **= T1-12.** |
| 6.9 | GSTR-3B summary return on screen | PARTIAL | 🔴 **EVIDENCE MISATTRIBUTED — CORRECTED 2026-09-04 (§1.3 item 20), AND THIS IS THE ONE THAT MATTERS.** The cell credited ~~*"3.1 by head, 3.1(d) RCM, 4(A)(2)/(3), 4(B)(1)/(2), 4(D)(1)"*~~ to a row whose capability is the return **ON SCREEN**. **Those live on the PROJECTION, not on the screen.** `BuildGstr3b` (`src/Apex.Desktop/ViewModels/ReportsViewModel.cs:2132-2190`) renders **six** properties and **none** of the twenty RCM / reversal fields; `grep -rn "RcmOutwardCgst\|ItcReversed4B1Cgst\|RcmItcOtherCgst" src/Apex.Desktop` returns **one** hit and it is `RunSetOffViewModel.cs:246`, a different screen. **Gap:** 3.1 is a single taxable-outward value, not the four-way split; **3.1(b) Zero Rated is absent** (`grep -ril "zerorated" src --include=*.cs` hits one file, the print projector); **Table 6.1 is absent**; and zero hits for tables 3.1.1, 3.2, 5 and 5.1. **The screen omits every RCM and every ITC-reversal figure while labelling its partial sums "Total output tax" and "Total eligible ITC" — see T0-26.** **= T1-12.** |
| 6.10 | GSTR-1 / GSTR-3B portal JSON — the artefact that actually gets filed | ABSENT | The JSON writer class exposes exactly five writers (CMP-08, GSTR-4, 9, 9A, 9C) and **no GSTR-1 or 3B emitter anywhere**; the class itself has **zero production callers** — the only references in `src/` are two doc comments. **= T1-11.** |
| 6.11 | Composition returns CMP-08 (quarterly) and GSTR-4 (annual) | PARTIAL | Both engine projections and both screens, gated on the composition flag. **Gap:** **no output of any kind** — neither view model writes a file and neither is a report page, so no print and no export. The matching JSON writers exist and are never called. **In T1-10.** |
| 6.12 | Annual returns GSTR-9 and GSTR-9C | PARTIAL | Both projections and both screens, reachable for a regular dealer. **Gap:** identical to 6.11 — no print, no export, dead JSON writers. **In T1-10.** |
| 6.13 | GSTR-9A (composition annual return) | ABSENT | The only `Gstr9a` hits are engine-side: an uncalled JSON writer and two report files that mention it. No Screen member, no view model, no menu label case. |
| 6.14 | e-Invoice (IRN) — coverage decision, offline INV-01 JSON, recording the IRP response, cancellation | PARTIAL | Coverage, prepare, record-response, cancel and reporting-age all present with desktop callers. **Gap:** **no live IRP submission** — every online connector throws from every member and one has zero construction sites — and the **IRN and signed QR never reach the printed document**, structurally, because the PDF writer has no image primitive. **= T0-9.** |
| 6.15 | e-Way Bill — Part-A/Part-B, EWB-01 offline JSON, portal response, cancel, extend, close | PARTIAL | Eight engine entry points, all with desktop callers. 🔴 **THIS CELL OVER-CREDITED THE ARTEFACT — CORRECTED 2026-09-04 (§1.3 item 20). Grade unchanged.** *"EWB-01 offline JSON"* reads as a filed artefact. **It is a faithful structured emission of OUR OWN DESIGN**: measured against the NIC EWB-01 **v1.03 JSON Schema** (retrieved from `docs.ewaybillgst.gov.in` through the browser pane — it 403s WebFetch and curl), it is missing **6 of the schema's 17 `required` keys**, shares **zero** `itemList` key names with the schema, omits the 7 mandatory main-object value fields, and fails the schema's own `docDate` regex (`yyyy-MM-dd` against `DD/MM/YYYY`). **The portal would reject it.** = **T1-29**, and it closes the file's open `R7 (A14 to confirm)` flag **with a negative answer**. **Gap:** no live NIC submission (same stub connectors); the **Consolidated e-Way Bill (EWB-02) is engine-only** — zero Desktop callers. |
| 6.16 | GSTR-2B import, reconciliation, and IMS (accept / reject / pending) | COMPLETE | Reconciler, IMS service, JSON parser, and three routes with callers. 🔴 **FIDELITY CAVEAT ADDED 2026-09-04 (§1.3 item 20) — the `COMPLETE` grade is correct on §1.1's existence test and DOES NOT MOVE; what it must not be read as is parity.** We ship **4** reconciliation buckets against the vendor's **9**; there is no *Excluding Party GSTIN* near-match bucket; and **reverse-charge portal lines are excluded symmetrically (correctly) and then land in NO bucket at all**, so a deliberately-set-aside 2B line is indistinguishable from one that was never in the file — the vendor has a named bucket for exactly this, *"Excluded, but available on Portal."* = **T2-22**. |
| 6.17 | ITC set-off (Rule 88A with the §49(5)(c)/(d) proviso) and cash discharge via a PMT-06 challan | COMPLETE | Both services, the GST-Actions route and the posting caller. §1.3 item 6 is its fidelity row. |
| 6.18 | ITC reversal posting (Rules 37/37A/38/42/43, §17(5)) and the reversal report | COMPLETE | Service, the 3B reversal tables, and both routes. |
| 6.19 | Advanced-GST read-only screens — Electronic Ledgers, ITC Set-Off view, ITC Gate, QRMP/IFF, GST Amendments, e-Invoice/e-Way Status | PARTIAL | All six exist and all six dispatch. **Gap:** all six are **output dead ends** — none writes a file and none is a report page, so none can be printed or exported. QRMP is a PMT-06 advisory only; its IFF rows are a window view, not an upload artefact. **In T1-10.** |
| 6.20 | DRC-03 voluntary payment / demand discharge | ABSENT | 🔴 The **engine verb exists and is complete** — a deposit-service posting method with its own record type — ~~*"and `Drc03` returns **zero hits across all of `src/Apex.Desktop`**"*~~ 🔴 **THAT CLAUSE IS FALSE AND WAS CORRECTED 2026-09-04 (wave-2 breadth pass); THE GRADE IS RIGHT AND MUST NOT CHANGE.** There are two hits, both in one file: `src/Apex.Desktop/ViewModels/VoucherAlterationEligibility.cs:305` (a doc comment) and `:312` (`|| company.GstDrc03s.Any(d => d.VoucherId == id)`). That is a **read-only alteration guard** — it refuses to amend a voucher linked to a DRC-03 — **not a creation route**. There is still no Screen member, no view model and no menu case, so `ABSENT` stands on the census's own test. Reachable only by JSON/XML import. Filed as **T2-9.** ⚠️ **Recorded rather than quietly fixed because this is the exact defect the census already had to correct once, on row 3.13: an evidence sentence falsifiable by one grep invites a reader to distrust the row's grade, which here is correct.** |
| 6.21 | Zero-rated supplies — exports, SEZ and deemed export | PARTIAL | The enum models all six e-invoice supply categories and the INV-01 writer maps every one; export is resolved from an overseas place-of-supply code. **Gap:** the resolver never mints SEZ-with-payment, SEZ-without-payment or deemed export — the party GST block has no SEZ or deemed-export flag; no LUT/bond master; no shipping-bill capture; GSTR-1 has no 6A/6B/6C. ⚠️ **T1-13's sub-claim that "Export hard-maps to EXPWP so there is no without-payment path" is STALE** — the writer now branches EXPWP/EXPWOP on the IGST amount. |
| 6.22 | Bill of Supply for a composition or exempt supplier on the **printed** document (§31(3)(c), Rule 5(f) declaration) | COMPLETE | 🔴 **CLOSES T0-7.** The invoice PDF branches on the bill-of-supply flag, takes its title from the shared predicate with a structural case-insensitive refusal of a TAX INVOICE title, suppresses every tax head and renders the declaration; the print projector supplies the flag and the title. **Two independent surveys measured this**, with counts (30 hits in the IO project, 34 in the print projector) against the census's "zero". |
| 6.23 | Multiple GSTIN registrations for one company (branch / second-State) | ABSENT | Zero `MultiGstin` / `AdditionalGstin` / `BranchGstin` hits in any of the four projects; the config carries exactly one GSTIN field. In T2-2. |
| 6.24 | Input Service Distributor (ISD) — credit distribution and the ISD return | ABSENT | The only `Isd` token in `src/` is a GSTR-2B inbound document-type enum member and the parser branch that reads it. No distribution service, no ISD invoice, no ISD return, no Screen, no menu row. In T2-2. |
| 6.25 | GST Classification / Nature-of-Transaction master | ABSENT | No such master exists. The ledger GST classification is an engine-managed value object explicitly excluded from user editing. No Screen member, no view model, no Create-menu row. In T2-3. |
| 6.26 | Kerala Flood Cess | ABSENT | 🔴 **Resolved 2026-08-18 — this was one of the eight cannot-tell rows and no survey covered it.** Zero `flood` hits over `src/`; the only `Kerala` hit is a state-name row in `IndianState.cs`. No cess type, no rate, no computation. **Recommend the user consider moving this row to §3 (obsolete by law)** — the levy is believed to have lapsed, and that lapse is **not verified here**; it is a user call under §3's own rule, not a unilateral move. |
| 6.27 | F11 TDS enablement + deductor profile (TAN, deductor type, responsible person, surcharge and cess flags) | COMPLETE | Bound fields and the enable path that seeds the Nature-of-Payment masters and the payable ledger. |
| 6.28 | Nature of Payment (TDS section) master — create / alter / delete | PARTIAL | Create exists and eight sections are seeded. **Gap:** 🔴 **CREATE ONLY, and the seeded masters are immutable by design** — the screen's own doc says it does not edit a seeded nature. **Consequence worth naming: T0-6's blog-sourced rates cannot be corrected by a user in-app**, so the seed's claim that a Finance-Act change is "a data edit, not a code change" is true only of the C# source. Filed as **T1-21.** |
| 6.29 | TDS deduction at voucher entry — carve-out, PAN vs §206AA no-PAN rate, single-transaction and FY-cumulative thresholds | PARTIAL | Engine computation with the live advisory panel and two accept-path callers. **Gap:** **§194Q deducts on the whole transaction value** rather than the excess over the threshold — nothing subtracts the threshold from the base. **= T0-1.** |
| 6.30 | TDS Stat Payment (deposit of accrued TDS Payable against a challan) | COMPLETE | Service, screen, Ctrl+F route and dispatch. |
| 6.31 | TDS Challan Reconciliation (deposits vs deductions per section) | PARTIAL | Engine report and screen with an Alt+R route. **Gap:** **no output** — the view model has no export path and is not a report page, so it can be neither printed nor exported. **In T1-10.** |
| 6.32 | Form 26Q quarterly TDS return and the FVU flat file | COMPLETE | Report, writer, screen with folder/name knobs, and a save-and-return arm. |
| 6.33 | Form 16A (TDS certificate) and Form 27A (TDS cover) | COMPLETE | Both reports, both PDF writers, both screens with export knobs, both routes. |
| 6.34 | TDS exception and outstanding reports — Outstandings, Not Deducted, interest u/s 201(1A), Nature-of-Payment Summary | COMPLETE | Four report kinds on the report base, therefore printable and exportable. |
| 6.35 | The TDS long tail — ~14 further sections, Form 27Q, §197 lower/nil certificates, §234E, Form 16B / 26QB | ABSENT | Per-term greps return zero for each section identifier; the two hits for "27Q" are a GSTIN inside a comment and a form-number rename entry; §197 exists only as an always-null field with **no writer anywhere**. **In T2-2.** |
| 6.36 | F11 TCS enablement + Nature of Goods (§206C) master | PARTIAL | Enable path and the master, with eight collection codes seeded. **Gap:** same create-only immutability as 6.28; and **§206C(1G)** (LRS remittances / overseas tour packages) is entirely absent — zero hits. |
| 6.37 | TCS collection at entry, TCS Stat Payment, Forms 27EQ / 27D / 27A(TCS), and the four TCS exception reports | COMPLETE | Collection service with three callers, deposit service and screen, three certificate screens each with export, four report kinds. |
| 6.38 | TCS Challan Reconciliation | PARTIAL | Report and screen exist. **Gap:** no print and no export, exactly as 6.31. **In T1-10.** |
| 6.39 | §192 salary TDS — annual estimate, old/new regime slabs, §87A rebate, surcharge, §206AA no-PAN floor | PARTIAL | Slab engine consumed inside the monthly payroll run, gated by an F11 flag. **Gap:** 🔴 the **4% Health & Education Cess it applies to a live salary deduction is unsourced for FY 2026-27 by the code's own remark**, which records that a full-text search of the Finance Act found no cess levy in the relevant Part. **= T0-5, a standing user decision.** |
| 6.40 | Form 12BB per-employee income-tax declaration master | COMPLETE | Create **and edit** — it loads an existing declaration for the picked employee. The "other fields captured but ignored" note is regime-correct behaviour, not a gap. |
| 6.41 | Form 24Q (quarterly salary-TDS return) and Form 16 (salary TDS certificate) | COMPLETE | Both reports, both screens with deterministic offline export, both routes gated on the salary-TDS flag. |
| 6.42 | Form 12BA (statement of perquisites) | ABSENT | Exactly one `12BA` hit in `src/` and it is a form-number rename entry in the vocabulary map. No report file, no Screen member, no view model, no menu case. In T2-2. |

#### Area 7 — Payroll · 21 rows · 6 complete / 11 partial / 4 absent

> **🔴 THE OLD TABLE ALLOWED THIS AREA ZERO ABSENT CAPABILITIES TOO.** Five are evidenced below.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 7.1 | F11 Maintain Payroll / Enable Payroll Statutory | COMPLETE | Both flags, the enable/disable service pair, and the three menu surfaces they gate. |
| 7.2 | Employee master (identity, group/category, PAN/Aadhaar/UAN/ESI, PF, bank, elected regime) | PARTIAL | 24 domain members and 24 bound fields, reachable under Payroll Masters. **Gap:** **create only** (7.16); and 🔴 **`Employee.DateOfLeaving` is unreachable from the UI** — zero hits across all of `src/Apex.Desktop` — while **three engines read it** (gratuity provision, the bonus register's year clip, the ESI last-working-day). A leaver keeps accruing indefinitely. Filed as **T0-13.** |
| 7.3 | Employee Category and Employee Group masters (hierarchical) | PARTIAL | Both domain types, both services, both screens, both routes. **Gap:** create only (7.16). |
| 7.4 | Payroll Unit master (simple and compound) | PARTIAL | Type, screen, route. **Gap:** create only (7.16). |
| 7.5 | Attendance / Production Type master | PARTIAL | Four kinds modelled; screen and route present. **Gap:** create only; and **nothing seeds the standard types** — zero `AttendanceType` hits in `src/Apex.Ledger/Seed` — so Present / Absent / Leave must be typed by hand on every new company. |
| 7.6 | Pay Head master (ten pay-head kinds × five calculation kinds, slab bands, statutory tagging) | PARTIAL | Both enums complete; a 717-line screen; consumed by the computation service. **Gap:** create only — a mistyped rate or a wrong Under-group on a pay head is permanent. |
| 7.7 | Salary Details — the per-employee salary structure | PARTIAL | Domain, service and a 512-line screen with amount gating; read at payroll time as the structure in force. **Gap:** create only. |
| 7.8 | Attendance / Production voucher | PARTIAL | Record path with a screen that writes every non-blank row and persists. **Gap:** 🔴 **a wrong-money route not in any register before today.** The engine's delete method has **zero Desktop callers**; the record method **always appends a new entry** with no dedupe on employee+type+period; and the computation **sums every matching entry**. Re-recording a period silently **doubles** the attended units behind an On-Attendance or On-Production pay head, with no in-app undo. Filed as **T0-12.** |
| 7.9 | Payroll voucher — compute the period breakdown and post the balanced integrated entry | COMPLETE | 🔴 **THIS CONTRADICTS T1-4 AND THE CONTRADICTION IS MEASURED.** The opener gates **only** on the payroll-enabled flag; the posting service selects the type by base kind and **never calls the type resolver and never tests `IsActive`**, so the inactive seed cannot block it. Ctrl+F4 is genuinely bound, intercepted before bare F4. **Residual:** the seeded type is still inactive, so Payroll is excluded from the Day-Book Alt+A picker, which filters on active types — a menu-surface gap, not an unreachable posting path. |
| 7.10 | Provident Fund — EPF/EPS split, wage ceiling and higher-wages opt-in, ECR 2.0 file, the A/c 1/2/10/21/22 challan | COMPLETE | Service, config, enrolment, monthly evaluation, report, ECR writer, route and config UI. §1.3 item 5 is its fidelity row. |
| 7.11 | ESI — coverage and contribution-period logic, EE/ER rates, monthly contribution report and offline file | COMPLETE | Service, computation callers, report, writer, route and config UI. |
| 7.12 | Professional Tax — state slab tables, the February over-charge, gender-scoped exemption, the annual cap, and the deduction register | COMPLETE | Service, config types, monthly-then-capped computation against the prior-FY total, register with export, route and config UI. |
| 7.13 | Gratuity provision register | PARTIAL | Service, config, the posting role, and the report, surfaced when the config is set. **Gap:** **no output** (not a report page, no export path); and its active-employee filter reads `DateOfLeaving`, which no UI can set (7.2) — so it keeps provisioning for staff who have left. |
| 7.14 | Statutory Bonus register (8.33% floor, calculation ceiling, pro-rating) | PARTIAL | Service, config, report, route and config UI. **Gap:** identical pair — no print, no export; and the eligibility window is clipped on `DateOfLeaving`, which no UI can set. |
| 7.15 | Payroll presentation reports — Payslip (with a dedicated PDF), Pay Sheet, Payroll Register, Attendance Register, Payment/Bank Advice | COMPLETE | Five report kinds on the shared report base, so all five print and export; the Payslip additionally renders its own de-branded PDF. |
| 7.16 | Alter and Delete on the payroll masters — all eight kinds | PARTIAL | 🔴 **MOVED `ABSENT` → `PARTIAL` 2026-09-05 (b1 landing, PR #49) — AND IT IS 4 OF 8, NOT CLOSED. The finishing agent reported it partial itself and refused to claim the row.** **DONE end-to-end, by real keystrokes** — arrows into the list, `Ctrl+Enter` to alter, `Ctrl+A` to accept, `Escape`, re-drill, `Alt+D` then `Y` to delete, with the alteration asserted by **Guid identity** rather than by field equality: **employee category · employee group · payroll unit · attendance/production type.** **STILL OPEN, and each for a different reason:** **(a) Employee** — the cheapest, since `PayrollService.AlterEmployee` and `DeleteEmployee` both already exist and the row now carries a real `MasterId`; it needs the six `IPayrollMasterList` members, a `ForAlter` factory, the `Ctrl+A` `IsAltering` branch, the two `Screen.EmployeeMaster` shell arms and the highlight bar on `vm:EmployeeListRow`. **(b) Pay head — blocked FURTHER BACK: `PayHeadService` has NO `Alter` method at all**, so an engine slice must land before any view-model work can finish it; this is filed as **T2-38**. **(c) salary structure master** and **(d) tax declaration master** — never scoped; someone must decide what "alter" even means for them before code is written. 🔴 **All four gaps are pinned by the new `PayrollMasterHalfWiredKindsTests` so the row cannot be quietly over-claimed later** — which is the discipline this census has most often lacked. ▼ *Original ABSENT evidence, kept because it is what the row moved FROM:* 🔴 **Stated once, as a capability in its own right rather than eight coincidences.** `ForAlter` exists in exactly three master view models tree-wide and **none is a payroll master**; every one of the eight payroll master view models returns zero for `Alter` and `Delete`. The payroll service **advertises** create/alter/delete in its own doc comment and nothing reaches the last two. Sole exception on the alter side: the income-tax declaration reloads an existing declaration — and it too has no delete. |
| 7.17 | Payroll job rates and cost-centre allocation of payroll cost | PARTIAL | 🔴 **Resolved 2026-08-18 — one of the eight cannot-tell rows, uncovered by any survey.** Job/piece rates **exist**: the On-Production calculation kind is offered on the Pay Head master and the computation sums production entries. Cost-centre allocation of payroll cost is **absent** — zero `CostCentre` and `CostAllocation` hits in `PayrollComputationService.cs`, `PayrollVoucherService.cs` and `Employee.cs`. |
| 7.18 | NPS pay head (employee §80CCD(1B) / employer §80CCD(2) as a payroll component) | ABSENT | No NPS member on the pay-head kind enum, no statutory-component analogue, no computation. The three `nps` hits are incidental: one doc comment and two places where employer-NPS is a **declared** figure feeding the §192 estimate — not a pay head that posts. In T2-2. |
| 7.19 | Labour Welfare Fund deduction | ABSENT | Case-insensitive greps for the full phrase and the abbreviation return zero across all four projects. No domain type, no component, no config field, no report. In T2-2. |
| 7.20 | PF statutory returns beyond the ECR — Forms 3A, 5, 6A, 10, 12A | ABSENT | Zero hits for each form identifier. The reports directory holds the ECR projection and nothing else PF-shaped; no Screen member, no menu case. In T2-2. |
| 7.21 | ESI statutory returns — Forms 3, 5, 6 | ABSENT | Zero hits. The only ESI artefacts are the monthly contribution projection and its offline writer; no return form exists. In T2-2. |

#### Area 8 — Banking · 10 rows · 1 complete / 4 partial / 5 absent

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 8.1 | Bank Reconciliation (BRS) — pick a bank ledger, key a Bank Date per line, Books-vs-Bank balances | PARTIAL | Engine build/transactions/set-date with uncleared movements subtracted; screen, menu row, button-bar entry and the editable Bank Date column. **Gap:** the reconciliation date is derived and not settable; **no print and no export** (a page column, so it is not a report context); no reconciled/unreconciled toggle; no BRS opening date on the bank-ledger master and no Alt+R-from-the-ledger route. **In T1-10.** |
| 8.2 | Bank Allocation on a bank line — transaction kind, instrument number and date | COMPLETE | Domain type carried on the entry line, captured per line with the panel auto-shown for a bank ledger, posted, and consumed by the reconciliation. |
| 8.3 | Import a bank statement and auto-reconcile against the book | PARTIAL | CSV parse plus signed-amount-and-instrument matching with a date tolerance, first-fit, stamping the Bank Date; screen, route and menu row. **Gap:** CSV only — no bank-specific formats and no saved format configuration; no per-row manual match/unmatch or un-reconcile; no print or export of the result. ⚠️ **The arithmetic is still unverified** (§6 item 8). |
| 8.4 | Cheque printing — print a cheque from a bank payment | ABSENT | 🔴 **Textbook dead field, unchanged at HEAD.** The two ledger properties exist, persist and round-trip through the canonical and SQLite layers — 17 hits, **every one of them Domain, Io or Sqlite** — with **zero** hits in `src/Apex.Desktop` and zero in any print path. No cheque layout, template or renderer of any kind. **= T1-14.** ⚠️ **Area assignment ambiguous** — one survey placed this under Printing; counted **once, here**, because Tally's own Banking menu carries it. Area 12 holds an uncounted cross-reference. |
| 8.5 | Cheque Register | ABSENT | Zero hits for the identifier and the phrase. No report kind, no Screen member, no menu row — the Banking column holds exactly two rows. **= T1-14.** |
| 8.6 | Deposit Slip (the bank pay-in slip) | ABSENT | Zero hits for the identifier and the phrase — no type, no report kind, no Screen, no menu row, no print projector. **= T1-14.** |
| 8.7 | Banking Payment Advice (the supplier-payment advice letter) | ABSENT | The only payment advice in the tree is the **payroll** bank advice, with employee/bank/IFSC/net-pay columns, surfaced under Payroll Reports. Nothing under Banking produces a supplier advice. **= T1-14.** |
| 8.8 | Post-dated cheque management (PDC summary, PDC-to-cleared transition) | PARTIAL | ⚠️ **The old table's integers imply ABSENT; PARTIAL is recorded.** The flag half is real — the Ctrl+T flag on both entry screens, the balance exclusion, and the instrument date on the bank allocation. **Gap:** no post-dated summary or register anywhere; **no route to clear the flag when the cheque matures**; no PDC cheque printing. Shares 5.8's one-way-flag defect (**T1-18**). |
| 8.9 | A unified Banking menu (Gateway → Banking) | PARTIAL | 🔴 **Resolved — one of the eight cannot-tell rows.** The menu member, its column builder, the show method and both dispatch sites all exist. **Gap:** it carries exactly **two** rows (Bank Reconciliation, Import Bank Statement); the other Tally banking rows have no row because they have no code. |
| 8.10 | e-Payments / bank payment-instruction file | ABSENT | Zero hits for the identifier and the phrase over `src/`. No exporter, no Screen member, no menu row. |

#### Area 9 — Inventory / manufacturing / job work (post-7.2) · 9 rows · 3 complete / 2 partial / 4 absent

> 🔴 **RE-SPLIT 2026-09-04 (wave-3 fold-in). The heading previously read** ~~*"9 rows · 4 complete / 1 partial /
> 4 absent"*~~. **Row 9.3 moved `COMPLETE` → `PARTIAL`** on the first comparison these rows have ever had
> (§1.3 item 23). **Nothing was built and nothing was un-built — the census was wrong about 9.3.**

> **Uncounted cross-references:** *Bill of Materials master → 3.9*; *Additional Cost of Purchase → 3.15*;
> *Actual-vs-Billed → 3.14*. All three were named here by one survey and under Inventory masters by another.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 9.1 | Job Order Processing — the F11 toggle and its four voucher kinds | COMPLETE | The service flips the company flag **and** activates the four seeded-inactive kinds, stamping the job-work and consumption flags; the F11 handler drives it with a rollback on failure; four menu rows gated on the flag. |
| 9.2 | Job Work order entry and Material In/Out movement entry | PARTIAL | Three entry view models posting through the inventory posting service, with the movement valuation in the job-work service. **Gap:** entry only — ~~*"with no voucher alteration anywhere (5.1)"*~~ 🔴 **CORRECTED 2026-09-04 (§1.3 item 23): that reason is STALE — 5.1 is `PARTIAL`, alteration shipped in S5a–S5e, so *"anywhere"* is false. The gap SURVIVES on a different and sharper mechanism:** voucher alteration resolves through `Company.FindVoucher` → `_vouchers`, the **accounting** collection, while Job Work orders and Material movements are `InventoryVoucher`s in `_inventoryVouchers`; `InventoryVoucher` appears nowhere in the alteration path, so **Ctrl+Enter on any of the four Job Work registers is a silent no-op with not even a NAMED refusal** (= **T1-27**). A mis-keyed order or movement can still be neither corrected nor (per T1-17) cancelled or deleted. Nine attested Dispatch-Details / Order-Details fields are absent from the whole product, and the vendor's **"Order No(s)"** is captured as exactly one order. |
| 9.3 | Job Work registers — In Order Book, Out Order Book, Material In Register, Material Out Register | PARTIAL | 🔴 **RE-GRADED 2026-09-04, `COMPLETE` → `PARTIAL`, on the first comparison this row has ever had (§1.3 item 23). The cell previously read** ~~*"COMPLETE … Existence and reachability only — content never compared to Tally."*~~ **It has now been compared, and four of eleven is not complete.** One engine report file with per-component pending arithmetic; four menu rows under their own header, surfaced only while the F11 flag is on. **Gap: four of the vendor's ELEVEN Job Work reports.** Absent on zero-hit greps over all of `src/`: *Job Work Orders Summary*, *Components Order Summary*, **Material Movement Register** (the statutory dispatch/receipt reconciliation carrying Shortages, Wastage/Scrap and Duty Paid — and the only Job Work register for which the vendor publishes a column list), *Stock With Job Worker* / *Stock from Party*, *Issue Variance Analysis*, *Receipt Variance Analysis*, *Stock Ageing Analysis*. The family is also nested one level too deep — inside Inventory Reports, where the vendor makes Job Work Reports a **sibling** of Accounts Books. **What AGREES:** the order-book pending arithmetic matches the vendor's Balance Quantity. = **T1-28**. |
| 9.4 | Manufacturing Journal (BoM-driven production voucher) | COMPLETE | Service, entry screen, menu row with the Alt+F7 hint gated on the BOM flag, and an opener that auto-creates the user type over the Stock Journal parent. |
| 9.5 | POS invoicing (multi-mode tender, POS register, POS receipt) | COMPLETE | Tender service, register projection, receipt PDF and data, billing screen, two menu rows, and an opener also reached when a POS-flagged Sales type is chosen. |
| 9.6 | Job Costing | ABSENT | 🔴 **Resolved — one of the eight cannot-tell rows.** Case-insensitive search of every `.cs` and `.axaml` in `src/` for the phrase and the identifier returns **zero**. No service, no view model, no Screen member, no report file, no menu row. |
| 9.7 | Item Cost Tracking | ABSENT | 🔴 **Resolved — one of the eight cannot-tell rows.** Case-insensitive search returns exactly one hit and it is unrelated (a comment about *additional*-cost tracking). No type, no screen, no report, no menu row. |
| 9.8 | Tracking Numbers linking Receipt Note ↔ Purchase and Delivery Note ↔ Sales | ABSENT | Zero `TrackingNumber` identifiers anywhere in `src/`; the two "Tracking No" strings are doc comments quoting the corpus. Order fulfilment is **inferred** by a FIFO walk over candidate movements, so there is no operator-entered tracking datum. **= T1-8.** |
| 9.9 | **Voucher Classes on Stock Journal** — the Transfer Class (*"Use Class for Inter-Godown Transfers"*) and the Job-Costing Consumption Class | ABSENT | 🔴 **RE-TITLED 2026-09-04 (§1.3 item 23) — THE STATE IS UNCHANGED AND THE ROW'S OLD PREMISE WAS NOT A TALLYPRIME FACT. It previously read** ~~*"Transfer Journal as a **named** voucher kind"*~~**, which is a Tally.ERP 9 artefact:** TallyPrime's own inventory-voucher page lists nine kinds and does not include it; the mechanism is a user-defined **voucher class**, of which this product has exactly one and it is the hard-wired POS tender pre-map (`Domain/PosConfig.cs`, `Schema.cs:1498`). **A row that names the wrong target cannot be closed by building the right thing.** **Shares its single missing mechanism with 9.6.** Zero hits for the phrase and the identifier. **Read this precisely:** the *function* is partly covered — an inventory line carries a godown with an in/out direction and the posting service handles the Stock Journal base kind with its own balance guard, so inter-godown movement **is** expressible. What is absent is the separately named kind. |

#### Area 10 — Accounting features (post-7.2) · 2 rows · 0 complete / 0 partial / 2 absent

> **This is the one area where the new list AGREES with the superseded table exactly** (`2 · 0 / 0 / 2 / 0`).
> The agreement is on a reconstruction, though — the old table named neither row.

| # | Capability | State | Evidence |
|---|---|---|---|
| 10.1 | Credit Limits on a ledger, with the over-limit block on voucher save | ABSENT | Case-insensitive search of every `.cs` and `.axaml` in `src/` for the identifier and the phrase returns **zero**: no domain property, no persistence column, no view-model field, no guard in the validator. ⚠️ Also named under Accounting masters by a second survey; counted **once, here**, per §1.1 rule 3 (earliest product that shipped it). In T2-3. |
| 10.2 | Multi Address (multiple mailing / shipping addresses per company and per ledger) | ABSENT | Zero hits for every spelling and for an address-book type. The party address is a single flat block of four columns and the company address a single block; no address-list type, no per-voucher address picker. In T2-3. |

#### Area 11 — Reports · 17 rows · 5 complete / 12 partial / 0 absent

> **🔴 THE OLD TABLE ALLOWED THIS AREA ZERO ABSENT CAPABILITIES.** Three are evidenced below, each on a
> zero-hit search, and they survive §1.1 rule 2's family compression rather than hiding inside it.
> **Uncounted cross-reference:** the *report button-bar options* and the *graphical dashboard* → **14.5** and
> **14.3**; one survey placed both under Reports.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 11.1 | Balance Sheet (Liabilities/Assets, net-profit fold, closing-stock basis) | COMPLETE | Type, report kind, Gateway row, dispatch, drill to the ledger book, Alt+F1 group roll-up, comparative columns, print and export. |
| 11.2 | Profit & Loss A/c (Trading + P&L with Gross Profit) | PARTIAL | Type with opening/closing stock and both profit figures; route and builder verified. **Gap:** the **Alt+F1 summary is degenerate** — the line record carries no group name, so the roll-up keys on the section header and collapses each side to **one** row, where the reference product rolls up to group level. No vertical form. |
| 11.3 | Trial Balance (ledger-wise with group roll-up) | COMPLETE | Type with both overloads, route, builder and roll-up. **Divergence noted, not a gap:** our default is detailed (ledger-wise) where the reference product opens group-wise and Alt+F1 expands. |
| 11.4 | Day Book | PARTIAL | Type, Gateway row, dispatch, builder, drill to voucher detail, cancelled rows flagged in text and colour. **Gap (a):** a single Amount column — the row record has one money field and no Dr/Cr split; no voucher-kind filter; no show-narration or show-inventory-details toggles. **Gap (b), and it is the biggest structural finding of the 2026-08-18 re-derivation:** the builder iterates the **accounting** voucher collection only, and eight of area 4's kinds post to a **second, separate** collection — so they never appear here. Because the Day Book is the only surface that sets the drill target and the only Alt+X surface, that one fact is also why those eight have no cancel, no delete and no drill. Filed as **T1-17.** |
| 11.5 | Account Books family — Cash Book / Bank Book / Ledger | PARTIAL | Column builder, three pickers, three show methods, an opener, the ledger-book projection and cash/bank classification. **Gap:** the family ships three books and **none of its registers** — see 11.6 and 11.7, which are counted separately rather than hidden inside this row. 🔴 **COMPARED 2026-09-04 (§1.3 item 21); grade `PARTIAL` CONFIRMED CORRECT; two gaps added.** (a) **The family's FIRST LEVEL is wrong** — the vendor opens a **Ledger Monthly Summary** and we open the voucher list; that missing primitive also blocks 11.6, 11.7, 11.10 and 11.12 (= **T1-32**, and building it once moves five rows). (b) **The whole family has no print, no export, no period and no configuration** because it opens as a **drill column, not a report**: `OpenAccountBook` never sets `Reports`, and `IsReportContext` excludes `Screen.LedgerVouchers` by name, which also makes `IsPrintablePage` false — while the vendor documents `F12` options on this report (= **T2-23**). |
| 11.6 | Sales / Purchase / Journal / Credit Note / Debit Note Registers | COMPLETE | 🔴 **MOVED `ABSENT` → `COMPLETE` 2026-09-05 (b3 landing, PR #46).** **All five** are built — `ReportKind.SalesRegister` / `PurchaseRegister` / `JournalRegister` / `CreditNoteRegister` / `DebitNoteRegister` over `src/Apex.Ledger/Reports/VoucherRegister.cs` — and all five are **reachable**: a "Registers" header inside Reports → Account Books carries one page row each, and each has its own case in the menu dispatch calling `OpenReport`. **Each opens MONTH-WISE and drills to that month's voucher-wise listing, then to the voucher** — a register is NOT a filtered Day Book, and building one that way is the trap this row sat on for three census passes. Locked by `ReportFamiliesTests` (engine, expected values hand-derived — month boundaries, `"Apr-2024"` labels, per-month counts) and `ReportFamiliesViewModelTests` including `Activating_a_register_row_opens_that_register` and `Account_books_nests_the_books_the_registers_and_the_group_reports_under_named_sections`. ⚠️ **Verified by the integrator rather than taken on trust: b3 had no finishing agent, so reachability was re-derived from the menu column, the dispatch cases and the realised nesting before this row was moved.** |
| 11.7 | Group Summary / Group Vouchers | COMPLETE | 🔴 **MOVED `ABSENT` → `COMPLETE` 2026-09-05 (b3 landing, PR #46).** Both reports exist (`src/Apex.Ledger/Reports/GroupSummary.cs`, `GroupVouchers.cs`) and both are reachable through **their own group-picker cascade column** under Reports → Account Books → Groups — one page item per accounting group, name-sorted and data-driven, so a bare letter filters rather than activating. Group Summary drills a **sub-group into its own summary** and a **ledger into its Ledger Monthly Summary**; Group Vouchers lists the group-touching vouchers and drills each to its voucher. Locked by `Group_summary_is_reachable_through_a_group_picker_and_opens_scoped` and `Group_vouchers_is_reachable_through_its_own_group_picker`. ⚠️ **`LedgerMonthlySummary` (+ `MonthAxis`) landed with this row deliberately — it is carry-forward T1-32, the level the whole Account Books family was missing, and it is Group Summary's documented drill target (group → ledger → MONTHLY SUMMARY → vouchers). A flat group→ledger→voucher jump would have shipped row 11.5's defect a second time.** **Row 11.5 is NOT moved on the strength of it** — the Account Books family's own gap is unre-measured. |
| 11.8 | Statistics (voucher and master counts) | COMPLETE | 🔴 **MOVED `ABSENT` → `COMPLETE` 2026-09-05 (b3 landing, PR #46).** `src/Apex.Ledger/Reports/Statistics.cs` with `ReportKind.Statistics`, reached from Reports → **Statements of Accounts** → Statistics via its own dispatch case. Shows the two sections the row names — vouchers entered and masters created — with their counts. Locked by `Statistics_shows_the_two_sections_with_counts` (engine figures hand-derived) and `Statistics_is_reachable_under_statements_of_accounts` (the reachability half, which is the half this pass exists to prove). |
| 11.9 | Statements of Accounts — Outstandings (Receivables / Payables) with ageing buckets | PARTIAL | Bill record with overdue days, bucket type, default buckets and the build; column builder, two dispatch cases and an opener. **Gap:** it is a **dedicated page Screen**, so the report context is null and that single fact switches off **print, export, drill, F2/Alt+F2 period, F12 config, Alt+F12 sort/filter and Alt+K saved views at once**. Also no ledger-wise or group-wise view, no reminder letter, no confirmation of accounts (12.7). 🔴 **COMPARED 2026-09-04 (§1.3 item 21); grade `PARTIAL` CONFIRMED.** Added: the **ageing method is fixed to by-due-date** where the vendor offers **by-bill-date** as well, and the buckets are a `static readonly` field with no parameter (= **T2-24**). ⚠️ **AND A CORRECTION THE PASS MADE TO ITS OWN DRAFT, RECORDED BECAUSE IT IS THE EXACT OVERSTATEMENT THIS CENSUS EXISTS TO PREVENT:** it first wrote *"no Settle Bill from the report"*. **That is FALSE — Settle Bills ships**, on `Alt+A` and on a visible button. The vendor's chord is `Alt+B`. **It is a chord divergence, not a missing capability**, `Alt+A` is not free here either (Day Book's Add Voucher), and it belongs to the single chord-map ruling **U-6**. **In T1-9 and T1-10.** |
| 11.10 | Statements of Accounts — Cost Centre reports (Category Summary, Cost Centre Break-up) | PARTIAL | Engine reports, column builder, two dispatch cases, view model. **Gap:** the same dead end as 11.9 — dedicated Screen, no print, no export, no drill, no period or config panel. 🔴 **COMPARED 2026-09-04 (§1.3 item 21); grade `PARTIAL` CONFIRMED; and one SHIPPED report in this family is UNREACHABLE.** `CostReports.BuildLedgerBreakup` is fully implemented and fully tested with **zero `src/` callers** — `CostReportKind` has two members and the builder is the third (= **T2-25**, the T1-14 dead-field shape one layer up). No Cost Centre **Monthly Summary**, because that primitive does not exist (T1-32). |
| 11.11 | Statements of Accounts — Interest Calculation, Forex Gain/Loss, Budget Variance | PARTIAL | Three engine reports, three routes, three view models. **Gap:** all three are dedicated Screens with the same six gestures off; none carries a bespoke export. 🔴 **COMPARED 2026-09-04 (§1.3 item 21); grade `PARTIAL` CONFIRMED; and the BUDGET-VARIANCE GAP IS REFRAMED, WHICH CHANGES WHAT FIXING IT MEANS.** ~~The six missing gestures are written here as features to add to a dedicated screen.~~ **The vendor has NO dedicated Budget Variance screen — it is `Alt+B` ON Trial Balance / Group Summary.** Fixing the **shape** delivers five of the six gestures for free; adding them to a dedicated screen would ship a surface the reference product does not have (= **T2-26**). **Forex Gain/Loss is VENDOR SILENT on its report surface** — the vendor page describes a Balance-Sheet head, not a report; **our accounting AGREES**, and the surface is ours by necessity (`docs/invented-vs-cloned.md` IV-59). |
| 11.12 | Inventory Books / Statements of Inventory (Stock Summary, Godown Summary, Stock Movement, Reorder Status, Batch-wise, Batch Age Analysis, Price List, five inventory registers, Order Register, POS Register, four Job Work books) | PARTIAL | Ten engine report files, one column builder with conditional sub-sections, and the report-kind builders. **Gap:** absent from the family, each on a zero-hit grep — **Stock Query, Movement Analysis, Stock Ageing** (the batch report is an **expiry** report, not an age-of-stock bucket report), **Stock Category summary** (every "Category Summary" hit is a **cost** category), **Sales/Purchase Order Summary, Bills Pending**. Only two of the inventory kinds drill; the other fourteen are dead ends. 🔴 **COMPARED 2026-09-04 (§1.3 item 21); grade `PARTIAL` CONFIRMED and the row's own gap list CONFIRMED EXACT.** Add to the absent list: **`F7` Show Profit**, **`Ctrl+J` Exception Reports** (which does not exist anywhere in the product — `grep -rn "Ctrl+J"` over `src/` → 0, though the vendor documents it as the in-report entry to the exception family from Stock Summary, Funds Flow and Interest Calculation, = **T2-27**), and three **Statements of Inventory** members counted once under 9.7 — *Stock Item Cost Analysis*, *Stock Group Cost Analysis*, *Item Cost Track Breakup* (**no figure moves**; they are named here so the enumeration is honest). **New defect:** Stock Summary's `Alt+F1` **summary rollup silently blanks the Quantity and Rate columns** — the detailed branch fills Col2–Col6, the group branch fills Col1 and Col6 only, on a keystroke that is meant to *roll up*, not *narrow* (= **T2-28**). ⚠️ **AND THE HONEST BOUNDARY: twelve-plus members of this family were NOT compared** — Stock Item Movement, Reorder Status, Batch-wise, Batch Age Analysis, Price List, the five inventory registers, Order Register, POS Register, the four Job Work books. **This row is *the* place a family-row grade would lie.** **In T2-1.** |
| 11.13 | Exception Reports (Negative Stock, Negative Cash/Bank, Memorandum Register, Reversing Journal Register) | PARTIAL | Four engine reports, a column builder, four dispatch cases and two builders. **Gap:** four of the reference product's ~nine. Absent on zero-hit greps: **Optional Voucher register, Post-Dated Voucher register, Cancelled Voucher register** (the flag now exists on the voucher and in the Day Book row, and nothing lists them), overdue receivables/payables exception views. **Dead field:** the memorandum row record carries a voucher id that the builder never assigns to the drill target, so Enter on a memo row is inert. **In T2-1.** |
| 11.14 | Cash Flow / Funds Flow / Ratio Analysis | PARTIAL | Three engine reports, a column builder, three dispatch cases and three builders. **Gap:** no drill (not in the drill switch), no comparative columns (the comparative map covers four kinds only); **Cash Flow Projection is absent** on a zero-hit grep. 🔴 **COMPARED 2026-09-04 (§1.3 item 21); grade `PARTIAL` CONFIRMED; and THIS GAP SENTENCE UNDERSTATES THE FINDING BY ONE WHOLE REPORT LEVEL.** Cash Flow and Funds Flow **both ship the vendor's *drilled* (Summary) level as their TOP level**; the vendor's **month-wise default is absent from both**. ~~*"no drill"*~~ is a **consequence** of that, not an independent gap (= **T2-29**). **New defect:** Ratio Analysis's `Sundry Debtors` uses the **Balance-Sheet closing** where the vendor uses the **due-till-today outstanding**, and `ReceivablesTurnoverDays` is computed from it — a correct formula over a divergent input, so the ratio on screen is wrong for any book with unmatured bills (`RatioAnalysis.cs:100-107`, consumed at `:159`) (= **T0-27**). The file's own *"verified against official help"* comment is **only two-thirds true**. |
| 11.15 | Report drill-down (Enter / double-click on a row) | PARTIAL | The drill switch handles exactly **6 of the 45** report kinds; for the **32** dedicated report Screens the string "Drill" occurs in only four files under the view-model directory and **none of them is a dedicated report view model**, so **0 of 32** drill. **= T1-9, CONFIRMED unchanged at HEAD.** |
| 11.16 | Report parameters — F2 as-of, Alt+F2 period, Alt+F1 detailed/summary, F12 configure, Alt+F12 sort & filter, Alt+K saved views | PARTIAL | Four option types and three view models; every entry point gated on the report context. **Gap:** available on the 45 report kinds only. The report context requires a non-null `Reports`, which the sub-screen clear nulls for all 32 dedicated report screens — so GSTR-4/9/9C, ITC, both challan recons, BRS, Outstandings, Cost, Budget, Interest, Forex and the payroll and TDS certificate screens have no period control, no configuration and no saved views. |
| 11.17 | Multi-period / multi-column comparison (Alt+C New Column, Alt+N Auto Columns) | PARTIAL | Comparative type, two view models, two Screens, both gated on a supports-comparative predicate. **Gap:** the comparative map covers **4 of the 45** kinds and **0 of the 32** dedicated screens; Auto Columns offers a monthly axis and a scenario axis only. |

#### Area 12 — Printing · 9 rows · 1 complete / 5 partial / 3 absent

> **Uncounted cross-reference:** *cheque printing* → **8.4**. The five-document group one survey wrote as a
> single Printing row is split here: deposit slip → **8.6**, banking payment advice → **8.7**, and the
> remaining three are 12.7.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 12.1 | Print Preview of a report and Save-to-PDF (P / Ctrl+P) | PARTIAL | Route, key binding, Screen, preview view model, report projector, print model, report PDF and the PDF writer. **Gap, four of them, and two are new findings:** (a) reachable only on the 45 report kinds plus a drilled voucher — the 32 dedicated screens are excluded (**T1-10**); (b) 🔴 **every wide report prints with BLANK column headings** — the print projector hard-labels column 1 and emits an **empty caption** for columns 2..n, while the real captions exist only in the **export** twin, so a printed Stock Summary or Order Register has no headings while its CSV of the same data does (filed as **T1-19**); (c) Save PDF has **no file dialog** — it writes to Documents under a title-derived name and silently overwrites (**T1-20**); (d) all text is ASCII-folded (every character above code point 126 becomes a hyphen) and cells are ellipsis-clipped rather than wrapped. ⚠️ One survey additionally **predicts** crore-scale figure truncation on an 8-column A4-portrait report from the writer's own width table; it says explicitly it did **not** render a PDF to confirm it, and it is recorded here as a prediction, not a measurement. |
| 12.2 | Print a voucher / tax invoice from a drilled voucher | PARTIAL | The detail view model selects an invoice or a plain voucher projection; invoice PDF, voucher PDF, print projector and print data all present. **Gap:** **Sales-only** — the tax-invoice predicate returns false unless the base kind is Sales, so Purchase item-invoices, Credit Notes and Debit Notes fall back to the plain Dr/Cr print (**T0-11**); and no IRN or signed QR on an e-invoiced supply (**T0-9**), structurally impossible while the writer has no image primitive (12.8). *(The Bill-of-Supply half of this path is counted at 6.22 and is COMPLETE.)* 🔴 **CORRECTED 2026-08-20 IN TWO PLACES, BOTH INSIDE THE STRUCK CLAUSE ABOVE.** **(i) THE CREDIT / DEBIT NOTE HALF IS REFUTED AND RE-ATTRIBUTED TO T0-10** — a note cannot carry inventory lines at all (`src/Apex.Ledger/Services/VoucherValidator.cs:257-259`, reached from `src/Apex.Ledger/Services/VoucherValidator.cs:150-151`; the chord is inert at `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68`), so the print gate is not the wall; see rows 4.7 and 4.8. **(ii) "THE TAX-INVOICE PREDICATE RETURNS FALSE UNLESS THE BASE KIND IS SALES" IS TRUE BUT IS NOT THE DEFECT** — it is the **correct** answer to *"may we ISSUE?"* (CGST §31(1)); the defect is the **call site** using it to answer *"should this RENDER with item detail?"* at `src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107`. **What remains under T0-11 here is the PURCHASE half alone.** ✅ **AND SINCE 2026-08-20 NOTHING REMAINS UNDER T0-11 AT ALL** — slice S3 closed the purchase **accounting (service)** invoice and slice S4 shipped the Rule-53 note document (rows 4.7 / 4.8), so the only gap left in this row is **T0-9**'s IRN / signed QR. ✅ **AND THAT HALF'S ITEM-INVOICE SHAPE IS CLOSED 2026-08-20 (Phase 10.13 slice S2)** — see row 4.6. The drilled Purchase item invoice now routes to the invoice projection through the classification seam and prints a `PURCHASE RECORD`. **Outstanding on this row:** the purchase **accounting-invoice** shape (S3), the Rule 53 note document (S4), and T0-9's IRN / signed QR. |
| 12.3 | Print configuration (F12 title override, narration on/off, copy marking) and page setup | PARTIAL | Config types with the Rule 46 copy labels, page size/orientation/margins/font sizes, route, Screen, key binding and view model. **Gap:** the config is **voucher/invoice only**, so a **report** print has no configuration beyond the page-size and orientation toggles; no margin control in the UI; company logo explicitly deferred. |
| 12.4 | Print format selector (`F8`: Dot Matrix Format / Neat Mode / Quick-Draft Format), number of copies, page range | PARTIAL | 🔴 **MOVED `ABSENT` → `PARTIAL` 2026-09-05 (b5 landing, PR #50) — complete on REPORTS, copies-only on DOCUMENTS.** The panel is reachable (`MainWindowViewModel` builds a `PrintConfigViewModel` and sets `Screen.PrintConfig`), and `ReportPdf` **honours every knob end-to-end**; that half genuinely works. **The four document renderers (`InvoicePdf`, `VoucherPdf`, `PayslipPdf`, `PosReceiptPdf`) honour only the COPY COUNT.** 🔴 **The panel was fixed by WITHDRAWING the inert knobs — the honest half — NOT by implementing them and not by leaving them advertised.** Remaining: teach the four document renderers page range and starting number, after which `SupportsPageKnobs` widens and the new lock keeps guarding the pairing. ⚠️ **The print-config captions no longer name `F8`/`F9`/`F5`/`F10`, because nothing routes those keys.** Binding them needs a source for what they should do; under RULING 14 the corpus is gone, so inventing bindings would be a fidelity guess dressed as a feature. **This is a live user decision — see IV-63.** The new lock is a SUBSET rule (advertised ⊆ routed) so it permits the real work later rather than blocking it. ▼ *Original ABSENT evidence, kept because it is what the row moved FROM, and because its target-list correction still stands:* 🔴 **THE ROW'S OWN TARGET LIST WAS WRONG AND WAS CORRECTED 2026-09-04 (wave-2 reports/printing pass). The grade does not move; the thing to build does.** The heading read ~~*"Print format selector (Neat / Quick / **Condensed** / Dot-Matrix / **Pre-Printed**)"*~~ — a five-item list, **two of whose items are not print formats at all.** Against the official vendor print-configuration page the `F8` **Print Format** selector offers **three** values, quoted: *"Dot Matrix Format"* · *"Neat Mode"* · *"Quick/Draft Format"*. **"Condensed" does not appear in that list — it is a Tally.ERP 9-era term carried in from the 7.2 baseline.** **"Pre-Printed" is not a format either** — it is the `F9` **paper** toggle (*Plain Paper* ↔ *Pre-Printed Paper*), a different axis, and listing it as a format would put a paper setting in a format dropdown. The corrected target is the three-value `F8` selector **plus three separate controls**: `F9` paper, `F5` number of copies **and Type of Copy** (*Original / Duplicate / Triplicate / Quadruplicate / Extra Copy*), and `F10` starting page number and page ranges. The row's *"number of copies, page range"* clause was always right. ⚠️ **Knock-on for row 12.3:** that row records our config as carrying *"the Rule 46 copy labels"*; the source's copy-type vocabulary is a **product** list of five, not the statutory list — **whether ours matches those five is UNREACHED** and is the cheapest follow-up in this area. Zero grep hits over `src/` for every one of those identifiers and phrases. ⚠️ One survey named this as a distinct absent capability in its prose while folding it into 12.3's gap column; it is given its own row here so the absent count is not understated. |
| 12.5 | Physical printer output (printer selection, print job, spooler) | ABSENT | Zero lines over `.cs` and `.axaml` for the dialog, settings, printing namespace, spooler, queue and ticket identifiers. There is no printer abstraction, no device enumeration and no spool call anywhere. "Print" means render a PDF into a byte array and write it to a file. **= T2-5.** |
| 12.6 | Multi-account printing / multi-voucher (range) printing | ABSENT | Zero grep hits for the identifiers and phrases. Nothing iterates a set of accounts or vouchers into one print job — the opener builds exactly one preview from exactly one report or one drilled voucher. **In T1-14.** |
| 12.7 | Delivery challan, reminder letter, confirmation of accounts | ABSENT | Zero hits for the reminder-letter and confirmation-of-accounts phrases; the nine "Delivery Challan" hits are all e-Way-bill document-kind prose, and no printable challan exists. 🔴 **THE EVIDENCE CELL DESCRIBES THE WRONG SHAPE FOR TWO OF THE THREE — CORRECTED 2026-09-04 (§1.3 item 22). Grade `ABSENT` unchanged.** The cell reads as *"no printable challan/letter/confirmation exists"*, which scopes the work as three new documents. **It is not.** In the reference product the **reminder letter** and the **confirmation of accounts** are not standalone documents at all — they are **multi-account print/export outputs reached from the `Alt+P` / `Alt+E` menus** (*Print Multi-Account Reports* → *Print All Ledger Accounts*, *Print Confirmation of Accounts*; *Export Reminder Letters*, *Export Confirmation of Accounts*). **They are therefore blocked behind the missing menu shell (T2-20), not behind three document templates**, and the **delivery challan** is the **Delivery Note voucher** printed — our projector is inventory-blind and no drill route reaches it. **In T1-14.** *(Deposit slip and banking payment advice are 8.6 and 8.7.)* |
| 12.8 | Print engine capability floor — raster images, embedded fonts, colour | PARTIAL | 🔴 **RE-GRADED 2026-09-04 (wave-2 breadth pass), AND IT RETIRES A PLANNING GATE. The cell below is quoted rather than deleted, because its image half is now FALSE and the plan was sequenced on it.** ~~*"The PDF writer's entire public surface is begin-page, text, line, page-count and build. Zero hits in that file for any image, compression or font-embedding identifier; fonts are the standard-14 faces with no embedding. **Consequence: no logo, no QR, no barcode, no non-Latin script and no colour fill, ever, without replacing the writer.***"~~ **Raster images ship and have a PRODUCTION CALLER.** Re-measured independently by the integrator on 2026-09-04: `src/Apex.Ledger.Io/PdfWriter.cs:238` emits `<< /Type /XObject /Subtype /Image`, `src/Apex.Ledger.Io/PdfWriter.cs:93` is `public void Image(double x, double y, double width, double height, PdfBitmap bitmap)`, `src/Apex.Ledger.Io/PdfBitmap.cs` supplies `FromQr`/`FromPredicate`, and `src/Apex.Ledger.Io/InvoicePdf.cs:359` calls `PdfBitmap.FromQr(symbol)` on the shipped invoice path. Type + route + caller + emitted PDF operator is exactly this list's `ABSENT` test, and it fails. **Named missing pieces (this is what keeps it PARTIAL rather than COMPLETE):** no font embedding (standard-14 faces only, so still no non-Latin script), no colour operator, and **mono only** — 1-bit `/DeviceGray`, unfiltered, no greyscale, no RGB, no compression. **= T2-4** on its residual. 🔴 **T0-9 IS NO LONGER BLOCKED ON THE IMAGE PRIMITIVE** — see §5 and the wave-2 note in `plan.md`. |
| 12.9 | Payslip / POS receipt / TDS-TCS certificate document printing | COMPLETE | Five renderers, each with a verified caller: the payslip PDF from the preview construction, the POS receipt PDF, and the Form 16A / 27A / 27D PDFs from their screens. **Narrow scope:** COMPLETE for those five documents, not for document printing generally. |

#### Area 13 — Data management (import / export / backup / e-mail) · 10 rows · 3 complete / 7 partial / 0 absent

> ~~**Held out of the denominator by §4 (excluded by decision), listed so the reader knows they were measured and
> not overlooked:** Split Company Data — **ABSENT** (zero hits); Group Company consolidation / merge —
> **ABSENT** (the only "Group Company" hits are three doc comments); Repair / Rewrite / Verify — **ABSENT** (the
> one integrity check in the tree runs inside the backup payload and is never a user command); TallyVault —
> **ABSENT** (the single case-insensitive "vault" hit is a comment saying it stays excluded).~~
> **▶ 🔴 NO LONGER HELD OUT — user ruling 10 (2026-08-19) moved all four into the denominator as rows 16.5,
> 16.7, 16.6 and 16.1 of Area 16.** They are **NOT counted here**; this note is kept only so a reader
> arriving from the old text finds where they went. **▶ AND ONE OF THE FOUR STATES ABOVE WAS WRONG:
> Repair / Rewrite / Verify is PARTIAL, not ABSENT.** The integrity check is a real `PRAGMA integrity_check`
> with a type, callers on **both** the backup and the restore path, and a Gateway route to those paths — so
> the row cannot rest on a zero-hit search, which is the ABSENT bar. Row 16.6 carries the corrected state and
> the two named missing pieces. **The 2026-08-18 reasoning — *"never a user command"* — is a correct
> observation that was applied to the wrong token:** it is the *named gap*, not the grounds for ABSENT.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 13.1 | Backup the open company to a versioned archive | COMPLETE | A real SQLite **Online Backup API** snapshot — not a file copy — verified with an integrity check, zipped with a schema-stamped manifest; screen flushes the aggregate first; route, menu row and an Alt+Y button-bar row. ⚠️ The version-gap audit's *"Backup / Restore — absent as such"* row is **STALE** and must not be cited. 🔴 **SCOPE SENTENCE ADDED 2026-09-04 (§1.3 item 22). THE GRADE DOES NOT MOVE — §1.2 grades existence and reachability, and a backup exists and is reachable — BUT "Backup — COMPLETE" READS AS PARITY AND IT IS NOT. This row is COMPLETE for the OPEN COMPANY, not for company selection and not for multi-company backup.** The vendor's Backup screen **lists companies** and you pick one; it offers **All Items** to back up several in one action; and it persists a **Company Backup Path** via `Alt+Y > Data Path Configuration`. Ours takes whichever company is open (`OpenBackupCompany` returns early when `Company is null`; the ctor takes one `Company`), has no picker, and defaults `Folder` to Documents **every session** with nothing persisting it. = **T2-30**. *(Row 12.9 already models this wording.)* |
| 13.2 | Restore an archive over a company | PARTIAL | Staged beside the target with format, schema and checksum refusals before anything is touched; a two-step examine-then-apply screen with its own post-restore validity check and a pre-restore safety copy. **Gap, and it is WIDER than T1-7 states:** it can only ever restore **into the company already open**. Two independent locks — the opener returns early with no company and the Data menu bounces to Company Select, so on a machine with zero companies there is no route in at all; **and** the target-name property has **zero bindings in the XAML**, so even with a company open the target cannot be redirected. The engine signature would allow it. **T1-7 widened.** |
| 13.3 | Whole-company canonical export (JSON / XML) for interchange and re-import | PARTIAL | Both exporters, a screen with a format choice, an opener and a bare-key arm. **Gap: reachability only, and it is a trap.** There is **no menu row** — the Gateway's "Data" header carries exactly one child, Backup / Restore — and the Gateway header hint reads "Y: Data" while **bare Y opens Export Data and Alt+Y opens Backup/Restore**, so the one hint the screen gives points at the wrong screen. Filed as **T2-10.** |
| 13.4 | Import into the open company (canonical JSON / XML, flat CSV) | PARTIAL | Three parsers through a validate-before-apply, transactional import service with a duplicate policy. **Gap:** (a) **no Tally-XML reader** (zero `TALLYMESSAGE` hits) and no SDF reader, so no third-party Tally data can be ingested, and no Excel reader — the XLSX support is write-only; (b) imports only **into an already-open company**, so recovering a lost book means Create Company first; (c) no menu row — a bare-key arm on the Gateway root only. **In T2-6.** |
| 13.5 | Report and master-list export (bare `E` / `Alt+E`) to CSV, XLSX and PDF — 🔴 **and the chord pairing in this row's own title was wrong** | PARTIAL | 🔴 **NEW GAP (d), 2026-09-04 (wave-2 reports/printing pass): `Ctrl+E` DOES NOT EXPORT ANYTHING, ANYWHERE, AND `Alt+E` IS DOING `Ctrl+E`'s JOB.** Officially, quoted from the vendor shortcut page, these are **two chords with two different functions**: `Ctrl+E` = *"To export the current voucher or report"* and `Alt+E` = *"To open the export menu for exporting masters, transactions, or reports"* (one current object versus one bulk set — corroborated on the Export-Data page). Shipped, by **enumerating both `Key.E` handlers in `src/Apex.Desktop`** rather than by finding one: `src/Apex.Desktop/Views/MainWindow.axaml.cs:858` guards `!e.KeyModifiers.HasFlag(KeyModifiers.Control)` and says nothing about Alt, so it fires on **bare `E` and on `Alt+E`** and opens the **current-object** export; `src/Apex.Desktop/Views/MainWindow.axaml.cs:889` binds `Ctrl+E` to `ExamineRestore()`, and **only on the Restore screen**. So on a report page `Ctrl+E` matches neither arm and is **inert** — the single most-documented export gesture in the reference product does nothing — the attested **bulk** export menu does not exist, and its chord has been consumed. **Bare `E` is a third binding of ours with no attestation** (see `docs/invented-vs-cloned.md`). There is no recorded decision to re-map these chords, so this is a defect, not a divergence-by-design. **This row's title previously read *"(E / Alt+E)"*, propagating the wrong pairing into the census itself; it now names the shipped chords and the defect.** *(Not asserted: the running app was not driven — the claim is about the handler chain as written.)* Route, gate, key binding, Screen, three writers and two projectors, with 26 master view models implementing the export source. **Gap:** (a) the **same report-context gate as print**, so the 32 dedicated report screens are excluded — exactly **10** of those 32 carry a bespoke per-screen export, leaving **22 with no egress in any form** (**T1-10**, figures re-verified at HEAD); (b) 🔴 **17 report kinds export with BLANK column headers** — the header map covers only 16 kinds and falls through to an empty array, so Batch-wise, Batch Age Analysis, Price List, the nine TDS/TCS kinds and the five payroll kinds emit a header row of empty strings (**T1-19**); (c) no folder browse dialog (13.10). |
| 13.6 | Report export in HTML, XML, JSON, ASCII or JPEG | PARTIAL | 🔴 **MOVED `ABSENT` → `PARTIAL` 2026-09-05 (b5 landing, PR #50) — SIX OF THE VENDOR'S SEVEN FORMATS NOW SHIP, AND THE ROW STILL DOES NOT CLOSE.** `enum ExportFormat` is now `{ Csv, Xlsx, Pdf, Html, Xml, Json, Ascii }` and **a user can actually pick one**: seven real `RadioButton`s in the `ExportFormat` group in the shipped `MainWindow.axaml`, driven from `OpenExport()` on a live Trial Balance through `ExportViewModel.Format` into `HtmlReportWriter` / `XmlReportWriter` / `JsonReportWriter` / `AsciiReportWriter`, emitting bytes with the right extension (`.html` / `.xml` / `.json` / `.txt`). `Ascii` is a member **of its own** rather than a rename of `Csv`, so the source's *ASCII (Comma Delimited)* `.txt` and our `.csv` both exist. 🔴 **PROVEN BY A REALISED-CONTROL TEST AND MUTATION-VERIFIED: deleting the HTML radio made that format unreachable by any user while all 8 view-model tests STAYED GREEN.** That is the exact shape of this project's most-repeated defect, caught here rather than shipped. **WHY IT IS STILL NOT `COMPLETE`: JPEG (Image) `.jpg` is not among ours** — it needs a rasteriser (a new dependency or a hand-rolled encoder), which is an **architecture/scope call for the user**, not an implementation detail. `ExportConfig.cs` states this itself rather than hiding it. ▼ *Original ABSENT evidence, kept because it is what the row moved FROM and because its seven-name mapping is what made the gap exact:* The export-format enum has exactly three members and the extension switch covers only those three. No other writer, no other UI option. XML and JSON exist only on the **different** whole-company surface (13.3), which exports a company file and never a report. **= T2-6.** 🔴 **THE TARGET IS NOW EXACT, AND STATED AS A MAPPING RATHER THAN A COUNT, because the count alone hides two of the three findings (2026-09-04, wave-2 reports/printing pass; two independent official vendor pages agree).** The official File Format list is **seven**, quoted with the vendor's own parentheticals: *ASCII (Comma Delimited)* `.txt` · *Excel (Spreadsheet)* `.xlsx` · *HTML (Web-Publishing)* `.html` · *JPEG (Image)* `.jpg` · *JSON (Data Exchange)* `.json` · *PDF (Read-only Document)* `.pdf` · *XML (Data Interchange)* `.xml`. Ours (`src/Apex.Ledger.Io/ExportConfig.cs`, re-read by the integrator 2026-09-04 — `enum ExportFormat { Csv, Xlsx, Pdf }`) maps: `Xlsx` and `Pdf` **match**; `Csv → .csv` is the source's *ASCII (Comma Delimited)* `.txt` **renamed, not missing**; HTML, JPEG, JSON and XML are **absent**. **So it is four of seven absent and a fifth under the wrong extension.** ⚠️ **And the row's own note about 13.3 makes the fix cheaper than its `= T2-6` grading implies:** the **XML and JSON writers already exist in the tree** on the whole-company surface, so for two of the four missing formats the gap is a **projector/route** gap, not a **writer** gap. **JPEG is the outlier — it needs a rasteriser, i.e. a dependency or a hand-rolled encoder — and it is a user ruling (see §7).** |
| 13.7 | E-mail a report or invoice | PARTIAL | Compose view model, EML composer and message, mailto builder, SMTP profile types and a settings screen; a button-bar row gated on printability. **Gap:** **nothing is sent and nothing can be** — zero `SmtpClient` / `System.Net.Mail` hits anywhere in `src/`, and the view model's own notice says so. Of the two documented offline hand-offs only one is reachable: the **mailto URI is computed and bound nowhere** in the XAML — a dead field of the same species as 8.4. The `.eml` goes to a fixed Documents path with no save dialog; the attachment is always PDF. 🔴 **COMPARED 2026-09-04 (§1.3 item 22), AND A NEW LIVE DEFECT THIS ROW DID NOT HOLD.** `Ctrl+M` **AGREES** with the vendor. But **`Alt+M` — the vendor's Share menu, the parent of `Ctrl+M`, WhatsApp and the multi-account `Others` list — is INERT APPLICATION-WIDE**: `grep -rn "Key\.M\b" src/Apex.Desktop` returns exactly one line and it excludes Alt (= part of **T2-20**). And **every `.eml` this app has ever written carries `From: Apex Solutions <no-reply@apexsolutions.example>`** — an RFC 2606 `.example` domain no mail client can send from — **even when the operator has typed their real address into SMTP Settings and saved it**: `EmailComposeViewModel.cs:123` hard-codes the fallback, the `from` parameter exists only on the *testable* ctor, both production call sites use the convenience ctors, and `SmtpProfile.FromAddress` has **no reader in `src/` outside the settings screen re-loading it for editing**. **This is not the row's *"nothing is sent"* gap — the offline hand-off that DOES exist is broken, and it stays broken the day SMTP is built.** = **T1-33**. **In T2-6.** |
| 13.8 | SMTP profile capture (outgoing-mail server settings) | PARTIAL | Profile type, repository interface and a persisted table; a settings screen and a button-bar row. **Gap:** capture-only dead field — nothing in `src/` reads the saved profile to open a socket, and the screen says so itself. No password is captured, by the R13 decision. |
| 13.9 | Automatic forward migration of an older company data format on open | COMPLETE | The schema check reads the stored version and walks the migrations upward, bumping the row each step, to the current version; it runs on every load, so any older database opens and upgrades in place. Downgrade scripts exist for round-trip tests only. 🔴 **SCOPE SENTENCE ADDED 2026-09-04 (§1.3 item 22). THE GRADE DOES NOT MOVE — the row's own title says *"automatic … on open"* and that is exactly what we ship, completely. But THE ROW IS COMPLETE AGAINST A CAPABILITY STATEMENT THAT IS OURS, NOT THE VENDOR'S.** The reference product's migration is a deliberate, **operator-run menu action** that is **pausable and resumable**, runs **beside** the old data, has a **pre-migration exception check**, a **Migrate Configuration** screen and a closing **Migration Summary** — and it **retains** the pre-migration copy. We have none of those, and **no pre-migration copy at all** (a `.apex-prerestore` copy exists only on the *restore* path). = **T2-31**. ⚠️ **And there is no census row for Migrate-as-an-operator-action, nor for Extract/Share (ODBC/FTP/Pivot); adding one would move the 216 denominator, so it is user ruling U-11, not an edit.** |
| 13.10 | File / folder chooser for any data path (backup destination, restore source, import source, export destination, `.eml` path) | COMPLETE | 🔴 **MOVED `ABSENT` → `COMPLETE` 2026-09-05 (b1 landing, PR #49).** `Alt+B` is a real chord in `MainWindow.OnKeyDown`; **all SEVEN path panels** — backup destination, restore source, import source, export destination, export, `.eml` hand-off and print-preview PDF — carry a real "Browse... (Alt+B)" button routed through one `OnBrowseForPathClick` entry point, and `MainWindow` constructs a real `StorageProviderFilePathPicker`. The affordance test was widened from 4 panels to all 7 and each is confirmed to open, return a non-null `BrowseRequest` and realise a Browse button. **The only faked seam is `IFilePathPicker` itself, because an OS dialog cannot open headlessly.** ⚠️ 🔴 **THIS DOES NOT CLOSE T1-20, and the integrator refused to upgrade it.** T1-20's second half — *"Restore cannot target any company but the one already open"* — is **verified still open at HEAD**: `TargetCompanyName` has **zero** bindings in `MainWindow.axaml` and "Restore into" is still a read-only `{Binding TargetPath}` display. **The chooser half is done; the targeting half is untouched.** ▼ *The original ABSENT evidence is kept below, because it is what the row was moved FROM:* 🔴 **A cross-cutting fact the census has never stated.** Searched `src/Apex.Desktop` for the storage provider, both file dialogs, the folder dialog and the picker options type — **zero hits for all five**. Every path is a typed string or a silent default to Documents. **A user restoring from a backup must type the full archive path from memory.** 🔴 **COMPARED AND SETTLED 2026-09-04 (§1.3 item 22). §1.3 item 18 had DECLINED this row as UNREACHED; the vendor documentation answers it, so the row is moved OUT of §6a and into the compared set.** **This is the first time §6a has delivered on what it promises — an unreached row re-opened the moment its source became retrievable.** ~~Grade `ABSENT` confirmed.~~ **← that confirmation was correct on 2026-09-04 and was SUPERSEDED on 2026-09-05 by the b1 landing described at the head of this cell; the grade is now `COMPLETE`.** Filed as **T1-20**, which **remains OPEN on its targeting half**. |

#### Area 14 — TallyPrime-only capabilities · 10 rows · 1 complete / 2 partial / 7 absent

> ~~**Held out by §4:** Edit Log / audit trail — **ABSENT** (zero hits for all four spellings).~~
> **▶ 🔴 NO LONGER HELD OUT — user ruling 10 (2026-08-19) moved it into the denominator as row 16.3, and the
> ABSENT state is re-confirmed there against five named regexes.** **NOT counted here.** ⚠️ **And it is now
> the NEXT thing built after the voucher lifecycle** — user ruling 11 (`plan.md` §5).
> **NOT COUNTED, and deliberately so: the Miller-column cascade.** It is **built, reachable and universal** —
> menu-versus-page columns with the bare-letter rule, and every screen opened as a new column. It is **ours, a
> divergence from TallyPrime's single-screen + Go To model**, not a TallyPrime capability, so counting it would
> inflate this area's COMPLETE column. One survey counted it and then said in its own notes that counting it
> honestly gives 2 rather than 4; ⚠️ its own rows give **3** with it and **2** without. It is excluded here.
> **Uncounted cross-references:** *online e-Invoice* → **6.14**; *online e-Way Bill* → **6.15**; *IMS* → **6.16**.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 14.1 | Go To (Alt+G) — the jump-anywhere overlay | ABSENT | **Zero occurrences of `Key.G`** in the whole of `src/Apex.Desktop`, and zero for the identifiers and phrases. No view model, no Screen member, no menu row, no key arm. **In T2-7.** |
| 14.2 | Switch To (Ctrl+G) — between simultaneously loaded companies | ABSENT | Absent **and structurally impossible**: the shell exposes a single nullable `Company` and the open path **replaces** it. There is no collection of loaded companies to switch between — zero hits for either candidate field. **In T2-7.** |
| 14.3 | Graphical dashboard / any chart at all | ABSENT | Zero chart identifiers in `src/`, and **zero `<Polyline>`, `<Path>`, `<Canvas>` or `<PathGeometry>` elements in the ~16,000-line main view**. The only "dashboard" hits are IMS prose and a comment describing Ratio Analysis as a flat label/value dashboard. **Directly contradicts `plan.md`'s claim of a delivered graphical dashboard** (already a Tier 3 row). **In T2-7.** |
| 14.4 | More Details (Ctrl+I) — the optional-field side panel | ABSENT | Zero identifiers and zero phrase hits. **The chord is taken**: Ctrl+I is the item-invoice toggle and Alt+I the POS payment-mode toggle. **In T2-7.** |
| 14.5 | The standard report button-bar options — Change View, Basis of Values, Monthly Summary, Value Range, Scale Factor, Vertical Balance, number-of-decimals, Alt+U Unhide | ABSENT | 🔴 **SURVEY DISAGREEMENT, RECORDED.** Both surveys agree **none of the eight exists**: per-term greps return zero, and the only "Basis of Values" hits are three doc comments explaining why Ctrl+B was deliberately **not** bound. Alt+U is unbound; the Ctrl+H report arm does not exist (that chord is voucher-mode only). One survey graded the row **PARTIAL** on the ground that a context-rebuilt button bar **does** exist and that two adjacent options have real counterparts — **but those two are counted separately at 11.16 (F12 report config) and 11.17 (Alt+N Auto Columns)**, and the bar's actual contents are our own app-wide quick-jumps, not the open report's options. **ABSENT is recorded for the eight named options**, with the disagreement stated rather than reconciled away. **In T2-7.** |
| 14.6 | Save View (capture a report's configuration under a name) | PARTIAL | Persisted per company, config-only capture (never a computed figure), on a Ctrl+S arm guarded by the report context. **Gap:** the same context gate as export and print — a view cannot be saved on any dedicated report Screen. |
| 14.7 | Saved Views list (open or delete a saved view) | PARTIAL | Per-company list; open re-applies the config and recomputes; delete removes. **Gap:** **no menu row anywhere** — the only two hits for the label are doc comments — so the list is reachable **only** by pressing Alt+K while already standing on a report-kind surface. You cannot reach your saved views from the Gateway. And this binding is what **consumes Alt+K**, which in TallyPrime is the Company menu (14.9). |
| 14.8 | Chart of Accounts (TallyPrime's renamed master browser) | COMPLETE | View model, Screen member and backing field, a Gateway → Masters page row, and it implements the master-list export source so it exports. Existence and reachability only — its column set has never been compared to Tally's (§6 item 2). |
| 14.9 | Company menu (TallyPrime's Alt+K: Create / Alter / Select / Shut Company) | ABSENT | No company-menu column is built anywhere in the root builder, and **Alt+K is already consumed by Saved Views** (14.7). Create and Alter are reached instead from Company Select and a Masters row. §1.3 row 9 records the company menu as **owed, not refused**; this confirms it is still owed at HEAD. |
| 14.10 | WhatsApp sharing of a document | ABSENT | Case-insensitive search for the name across every `.cs` and `.axaml` in `src/` returns zero. No service, no button-bar row, no Screen member. |


#### Area 15 — Statutory, obsolete by law (pre-GST) · 9 rows · 0 complete / 0 partial / 9 absent

> **▶ 🔴 THIS AREA WAS §3, A HELD-OUT SET, UNTIL 2026-08-19.** User ruling 10 (R12 — `plan.md` §5,
> `FOUR FURTHER USER RULINGS (R12, 2026-08-19)`) brought both held-out sets into scope and **repealed the
> 2026-08-10 decision not to build these**. §3 is retained below as the record of what was decided and then
> reversed, and it now points here. **The basis for including them is §3's own note 1** — real TallyPrime
> still ships these as downloadable tax-extension modules.
> **▶ EVERY STATE BELOW IS A MEASUREMENT TAKEN 2026-08-19, NOT AN ASSUMPTION.** *"Held out, therefore
> ABSENT"* would have been the natural guess and the census has already been caught once by exactly that
> shape — §1.2's old absent column was provably too small because zero-hit searches were never run. So each
> row rests on a **named regex that returned zero**, over `src/` and `tests/`.
> **▶ 🔴 THE ONE TRAP A LATER RE-RUN WILL FALL INTO, RECORDED SO IT DOES NOT: `TIN` IS A SUBSTRING OF
> `GSTIN`.** A case-insensitive `TIN` grep returns **367** hits of which **362** are `GSTIN` / `GSTINs`, and
> a re-run that skips the case-sensitive identifier-shape check will report State VAT as PARTIAL on 367
> phantom hits. The same class of trap applies to `vat` inside *passivation* / *starvation* (5 real
> residuals, all false) and to `composition`, which is **920 hits of GST §10**, not one hit of VAT
> Composition.
> **▶ THE FOUR NEAR-COLLISIONS, REJECTED WITH REASONS**, because each is a live GST capability that would
> read as a pre-GST one: **GSTIN** ≠ VAT TIN; **GST §10 composition** (`CompositionSubType`, CMP-08, GSTR-4)
> ≠ VAT Composition; the seeded **GST slabs 0/5/18/40** ≠ the 2005 four-slab structure; and GST's
> **base-by-dealer-type** rule ≠ VAT dealer type.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 15.1 | State VAT — enable, dealer type, TIN, registration date | ABSENT | `\bVAT\b` case-sensitive over `src/` → **zero**; `EnableVat`, `VatEnabled`, `VatDealer`, `VatTin`, `VatRegistration` → **zero**; the identifier shapes `\bTin[A-Z]`, `[A-Za-z]Tin\b`, `\bTin\b` → **zero**. The F11 statutory screen carries **GST / TDS / TCS / Payroll only**; the sole TIN-shaped field in the product is `GstConfig.Gstin`. |
| 15.2 | VAT & Tax Classifications (`Input VAT @ 4%`, `Output VAT @ 4%`) | ABSENT | Four independent regexes — `tax ?classification`, `taxclassification`, `input ?vat`, `output ?vat` — all **zero**. The product's tax-head masters are the six GST ledgers `GstService.EnableGst` auto-creates. |
| 15.3 | The 2005 four-slab VAT rate structure (1% / 4% / 12.5% / exempt, ~550 commodity categories) | ABSENT | `four ?slab` / `fourslab` → **zero**; `commodit` → **1**, an e-way-bill exempt-commodity doc comment in a test. Every `12.5` in the tree is an Avalonia `FontSize`, a GST half-rate money amount, or a payroll pay-head percentage — **none is a commodity rate slab**. |
| 15.4 | VAT Composition scheme | ABSENT | `composition` returns **920 hits across 83 files and 100% of them carry a GST section reference** (§10, §10(2A), Sch-II 6(b), CMP-08, GSTR-4). Zero reference a State VAT composition scheme. Adjacent-but-different, **not counted**: `CompositionTaxService`, `CompositionThreshold`, `CompositionSubType`, `Cmp08`, `Gstr4` — all current law. |
| 15.5 | VAT Reports (VAT Computation + state return forms) | ABSENT | `vat ?computation` → **zero**; `state ?return` → **zero**; and the `ReportKind` enum — the **single dispatch point for every report in the product** — was enumerated in full and carries **no VAT member**. No route can reach a VAT report. |
| 15.6 | Central Sales Tax — 2% interstate, C/F/H declaration forms | ABSENT | `\bcst\b` case-insensitive over `src/` and `tests/` → **zero**; `c-?form`, `form ?[cfh]\b`, `concessional` → **zero**; the one `declaration ?form` hit describes a **C# method declaration form**. The **432** `interstate` hits are IGST place-of-supply routing, not CST against a C-form. |
| 15.7 | Service Tax + Form ST3 | ABSENT | `servicetax` → **zero**; `\bst-?3\b` → **zero**. The three `service tax` hits all parse as *"a service **tax invoice**"* under `IsServiceAccountingInvoice` — a GST concept, a word-boundary accident. |
| 15.8 | Excise — the F12 invoice-format route; Excise for Dealers (RG23D / Form 2); Excise for Manufacturers | ABSENT | `excise`, `rg23`, `cenvat`, `modvat`, `form ?2\b` → **five hard zeros**. The word does not appear even in a comment in `src/` or `tests/`. `tariff` → 4, all GST rate-notification Schedule citations; `duty` → 3, all the "Duties & Taxes" group or §200's *duty to deposit*. |
| 15.9 | Fringe Benefit Tax | ABSENT | `\bfbt\b` and `fringe` over `src/` and `tests/` → **both zero**. ⚠️ Note the standing caveat, which ruling 10 does **not** repeal: FBT was **abolished by the Finance Act 2009 and was never in 7.2 anyway** — it is a row so that nobody adds it "for completeness", and it is the one row in this area with no reference-product behaviour to clone at all. |

#### Area 16 — Formerly excluded by decision (security, audit, data structure) · 7 rows · 0 complete / 3 partial / 4 absent

> **▶ 🔴 THIS AREA WAS §4, A HELD-OUT SET, UNTIL 2026-08-19.** User ruling 10 brought it into scope. §4 is
> retained below as the record and now points here.
> **▶ 🔴 ONE OF THE SEVEN PARTLY EXISTS — 16.6 — AND THAT IS THE WHOLE REASON THE RULING REQUIRED THESE
> STATES TO BE MEASURED RATHER THAN ASSUMED.** A real `PRAGMA integrity_check` is implemented, is reachable,
> and runs on **both** the backup and the restore path. Assuming ABSENT would have written a falsehood into
> the denominator on the first day it counted.
> **▶ ROW 16.4 IS A DELIBERATE NEAR-DUPLICATE OF 16.3 AND IS COUNTED ANYWAY, WITH THE OVERLAP DECLARED.**
> §4 listed both, and §4's count of **7** is one of the two figures the census re-affirmed as *"correct as
> stated"*; **re-deriving a different number here is exactly what §1.2c forbids.** 16.3 is the Edit Log as a
> **feature over masters and vouchers**; 16.4 is **attribution on the three lifecycle verbs** specifically —
> a narrower surface, separately measured, and the one ruling 11 names.
> **▶ ⚠️ A NAMING DIVERGENCE ON THE SEVENTH ROW, RECORDED RATHER THAN RESOLVED SILENTLY — IT MOVES NO
> COUNT.** The instruction that carried ruling 10 named §4's seven with *"the legacy indirect-tax stack"* in
> place of 16.4. **§4's own text puts the legacy stack in §3, not in §4** — its basis line reads *"excluded
> twice over"* — so counting it here would **double-count it against Area 15's nine** and make 216 wrong by
> one. These rows follow **§4's own seven**, which keeps `200 + 9 + 7 = 216` exact. Flagged for the user to
> overturn if the other reading was meant.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 16.1 | TallyVault — company-data encryption behind a user passphrase | ABSENT | `TallyVault` and `passphrase` over `src/` and `tests/` → **both zero**. The 24 crypto hits are **one** non-matching capability: `SqliteNicCredentialStore` AES-CBC over the four `nic_*_enc` columns, keyed from a **hard-coded application pepper** its own file calls an *"obfuscation-grade placeholder"* — **no passphrase parameter exists in the type**. The company `.db` is opened with no `Password=`, and `CompanyBackup.Create`/`Restore` take no passphrase: the archive is a plain ZIP. |
| 16.2 | Security Control — users, roles, security levels, password policy | ABSENT | `SecurityLevel`, `PasswordPolicy`, `IUserRepository`, `UserId`, `security level`, `user account`, `LoginUser`, `Authenticat` → **all zero**; and **zero of the 182 tables** in the schema names a user, role, permission or security concept. Every `role` hit is the Stock-Journal source/destination role or a statutory PF/ESI/PT pay-head role; every `password` hit is either a comment stating none is stored (R13) or the remote NIC portal API credential. |
| 16.3 | **Tally Audit** *(the auditor's review listing)* **and Edit Log** *(the compliance-grade activity trail)* — TWO features, not one; a persisted record of who changed what, and when, on masters and vouchers | PARTIAL | 🔴 **RE-GRADED 2026-09-04 (wave-2 breadth pass). The original cell is quoted, not deleted, because every clause in it was true on 2026-08-19 and every clause is now false.** ~~*"`AuditTrail`, `EditLog`, `ModifiedBy`, `CreatedBy`, `ActorId` → **all zero**; the single `ChangedBy` hit is the substring inside a test name. **No audit, log or history table among the 182.**"*~~ The **voucher** edit log shipped at schema **v52**. Re-measured independently by the integrator on 2026-09-04: the type at `src/Apex.Ledger/Domain/VoucherEditLogEntry.cs:61` with `enum VoucherEditVerb`; the collection at `src/Apex.Ledger/Domain/Company.cs:435`; the routes at `src/Apex.Ledger/Services/LedgerService.cs:177` (`Cancel`), `:203` (`Delete`) and `:989` (`RecordEdit`); persistence at `src/Apex.Persistence.Sqlite/Schema.cs:1864` (`CREATE TABLE voucher_edit_log`) with `src/Apex.Persistence.Sqlite/Schema.cs:168` at `CurrentVersion = 52`. **Named missing pieces:** (a) **no viewer** — no report kind, no Screen member, no menu case; the only `src/Apex.Desktop` hits are the discard-on-rollback path at `MainWindowViewModel.cs:5222` and `VoucherEntryViewModel.cs:3477`; (b) **no actor** — deliberate, see 16.4; (c) **masters are not covered** — the log records vouchers only; (d) **`InventoryPostingService`'s own Cancel and Delete do not log**, stated in that file's own comment at `src/Apex.Ledger/Services/InventoryPostingService.cs:137`, which interacts with T1-17's two-voucher-collection finding. 🔴 **AND THE ROW'S TITLE WAS WRONG BEFORE THIS EDIT — it merged two different features behind a slash.** Official vendor documentation describes **Edit Log** (introduced in TallyPrime Release 2.1, *"a trail of each activity in transactions and masters"* across creation/alteration/deletion, and the only edition meeting audit-trail compliance) and **Tally Audit** (an older auditor's review feature whose listings are *"categorised into voucher types, masters, and users"*) as **two features with different scopes, different sources and different amounts of work**. The title now names both. **⚠️ This matters now, not academically: user ruling 11 builds the edit log next.** |
| 16.4 | Attribution on the three lifecycle verbs — who altered / deleted / cancelled a posted voucher, when, and from what | PARTIAL | 🔴 **RE-GRADED 2026-09-04 (wave-2 breadth pass). TWO OF THE ROW'S THREE QUESTIONS ARE NOW ANSWERED IN CODE; ONLY "WHO" IS OUTSTANDING.** The original cell said ~~*"**None of the three takes an actor or a timestamp parameter**"*~~ — **the timestamp half is false.** Re-measured by the integrator on 2026-09-04 at `src/Apex.Ledger/Domain/VoucherEditLogEntry.cs:61-66`: the record carries `RecordedAt` (*"When the verb ran, from the clock the caller handed `LedgerService`"*) and `BeforeSnapshot` (*"The pre-change voucher"*). So **"when" and "from what" both ship**; **"who" does not, and its absence is an ARGUED DECISION rather than an oversight** — recorded at `src/Apex.Ledger/Domain/VoucherEditLogEntry.cs:40-46`: there is no user, actor, login or session identity anywhere in this application, so a `ModifiedBy` column *"would have exactly one honest value and one dishonest one."* **Named missing piece: the actor, pending an identity model (row 16.2).** The original evidence follows, kept because its schema measurements are still the record: `Cancel(Guid)`, `Delete(Guid)` and `Replace(Guid, Voucher)` all exist, all have routes and all persist; `vouchers.cancelled` and `inventory_vouchers.cancelled` round-trip. The `vouchers` table's 14 columns include no user, actor or change-timestamp — **still true, and it is now the "who" half alone, because the "when" and "from what" live on `voucher_edit_log` rather than on `vouchers`.** Of the whole schema, only `itc_reversals.created_at` and `gst_drc03.created_at` carry a timestamp at all — GST statutory rows, no user, unrelated to voucher change. Do **not** count `EInvoiceRecord.CancelledOn` / `EWayBillRecord.CancelledOn`: those record the **IRP/NIC portal's** cancellation of an e-document, not the local Alt+X. **Cross-reference: 16.3.** |
| 16.5 | Split Company Data by financial year | ABSENT | `SplitCompany` → **zero**; `split.{0,40}(financial year\|company\|book\|data)` → **zero**. Every `\bsplit\b` in `src/` is a different concept: Actual/Billed quantity, bill and cost allocation splits, the ESI employee/employer split, batch split, POS tender split, `string.Split`, `SplitAddress`. All Split-Company-Data mentions are **docs-only**. |
| 16.6 | Repair / Rewrite / Verify company data | PARTIAL | 🔴 **SURVEY DISAGREEMENT, RECORDED, AND THE STATE MOVED.** §1.2a's area-13 held-out note said **ABSENT** on 2026-08-18, reasoning *"never a user command"*. The 2026-08-19 measurement finds a real `CompanyBackup.IntegrityCheck` running `PRAGMA integrity_check`, **called on both write paths** — `Create` refuses to archive an already-corrupt database, and `Restore` validates the payload with it plus a SHA-256 digest and a schema-version cross-check — reachable through the Gateway's Data → Backup / Restore rows. **The ABSENT bar is *"no type, no route, no caller; every absent row rests on a search that returned zero"*, and this search did not return zero**, which is what decides it. **NAMED MISSING PIECES, two: (a) no standalone Verify verb** — `verify.{0,25}(company\|data\|book)` over `src/` → **zero**; the check is only ever a gate *inside* backup and restore. **(b) Nothing repairs or rewrites** a database found bad — `RepairCompanyData` → **zero**, no `VACUUM` or rebuild path; the only `Repair*` member is `VoucherTypeResolver.RepairSupersededSeedShortcuts`, a load-time seeded-shortcut fixup its own doc calls *"not a schema migration"*. **The area-13 note is corrected in place.** |
| 16.7 | Group Company consolidation | ABSENT | `GroupCompany` → 2 hits, both a **test helper building ONE ordinary company whose account *groups* net to zero** — a false positive, and named here because it is the kind of hit a re-run would count. `parent compan`, `member compan`, `multi-?compan` → **zero**. Every `consolidat` in `src/` is **e-Way-Bill EWB-02** (`ConsolidatedEWayBill`, `SubmitConsolidatedEway`) or the §34 consolidated credit note. The four *"Group Company"* phrases in the shell are doc comments citing the study guide's delete-confirmation prompt and **explicitly declining to copy it**. |

#### 1.2b WHAT MOVED BETWEEN THE TWO TABLES, AND WHY — 🔴 THE DENOMINATOR CHANGED, SAY IT OUT LOUD

**115 → 200 is not the product growing. It is three separate things, and they must not be conflated.**

1. **GRANULARITY — the largest part of the move, and it is the rule's own fault.** §1.1 rule 1 defines a
   capability as *"the granularity of a Tally menu row or an F11 toggle"*. Nobody had ever written the rows out
   at that granularity; the 115 was assembled area by area as integers. Four of the five surveys measured
   against rule 1 explicitly and returned **200** rows for the same product — for example Company
   configuration, scored **4**, has nine nameable rows (creation, alteration, select, rename+delete, the F11
   statutory page, the F11 inventory/payroll toggles, the missing F11 Accounting group, the missing global F12
   tree, and the four per-screen F12 panels). **§1.1 rule 2's family compression is RETAINED** — Account Books,
   Inventory Books, Statements of Accounts and Exception Reports are still one row each — so 200 is *not* the
   "expand everything" figure. §1.1's own caveat predicted this: *"Counting them out gives a denominator near
   200 and a worse present-ratio."* It was right.
2. **THE ABSENT COLUMN WAS PROVABLY TOO SMALL AT ITS OWN GRANULARITY.** This is the part that makes the old
   split *wrong*, not merely coarse. The old table allows **0** absent capabilities in Statutory, **0** in
   Payroll, **0** in Inventory masters and **0** in Reports. Against those zeros the surveys evidence, each on
   a zero-hit search: GSTR-1/3B portal JSON, DRC-03, GSTR-9A, multi-GSTIN, ISD, the GST Classification master,
   the TDS long tail, Form 12BA, Kerala Flood Cess and the GST rate hierarchy (Statutory); payroll master
   Alter/Delete, the NPS pay head, Labour Welfare Fund, the PF return forms and the ESI return forms (Payroll);
   per-item Alternate Units and GST capture on the two group masters (Inventory masters); the five voucher-type
   Registers, Group Summary and Statistics (Reports). **The old §2 T2-2 alone names roughly thirty such items,
   and with zero absent slots in two whole areas every one of them had to be hiding inside a row scored
   PARTIAL.** That is defect 3 seen from the other end.
3. **WORK SHIPPED SINCE 2026-08-10, and it is the smallest part of the move.** **W0-2b** (company profile
   capture + Alter Company), **S3** (voucher cancellation, Alt+X), **S4** (voucher and master deletion, Alt+D),
   **W0-1** (Bill of Supply on the printed document — see the T0-7 row), **W0-7** (the populated fixture),
   **W0-12/13/14/15**, and schema **v51**. Concretely: area 5's cancellation and deletion moved ABSENT →
   PARTIAL; area 1's creation and alteration moved ABSENT → PARTIAL; area 12's Bill-of-Supply half of the
   voucher print moved.

**THE EIGHT "CANNOT TELL" ROWS ARE NOW ZERO, and six of them cost one grep each.** §6 item 7 named them:
Actual-vs-Billed, Additional Cost of Purchase, Transfer Journal, Kerala Flood Cess, payroll job-rates /
cost-centre allocation, the unified Banking menu, Job Costing and Item Cost Tracking. Resolved 2026-08-18 —
Actual-vs-Billed **COMPLETE**, Additional Cost **COMPLETE**, Transfer Journal **ABSENT as a named type**
(inter-godown movement is expressible as a Stock Journal), unified Banking menu **PARTIAL** (it exists and
carries two rows), Job Costing **ABSENT**, Item Cost Tracking **ABSENT**. The last two were never covered by
any survey and were resolved here: Kerala Flood Cess **ABSENT** (zero `flood` hits in `src/`; the only
`Kerala` hit is a state-name row in `IndianState.cs`) and payroll job-rates / cost-centre allocation
**PARTIAL** (On-Production pay heads exist; zero `CostCentre` or `CostAllocation` hits in
`PayrollComputationService.cs`, `PayrollVoucherService.cs` or `Employee.cs`). **The undetermined column being 0
is a statement about today, not a property of the document** — the moment a capability is added whose existence
nobody checks, it goes back above zero.

#### 1.2c 🔴 THE TOP-DOWN RECONCILIATION IS RETIRED. IT NEVER RECONCILED.

The sentence that stood here read, verbatim:

> *"Reconciles top-down: 90 (7.2 baseline) + 28 (ERP 9 additions in scope) + 11 (TallyPrime-only) = 129, less 8
> obsolete-by-law, less 5 excluded-by-decision folded into the baseline, less 1 (ODBC, out of scope) = 115."*

**It does not reconcile, and it never did.** §1.1 rule 5 and the two held-out sections state the held-out
counts as **9** (§3) and **7** (§4). `129 − 9 − 7 − 1 = 112`, not 115.

- **The 9-vs-8 gap IS explicable.** §3's ninth row is Fringe Benefit Tax, which §3 itself flags *"not in 7.2
  anyway"* — so FBT was never inside the 129 and must not be subtracted from it. Subtracting **8** is correct.
- **The 7-vs-5 gap is NOT explicable, and no explanation is invented here.** §4's own count is **7**. At most
  one of those seven is demonstrably not a capability at all — *"Alter / Delete / Cancel shipping with no audit
  trail"*, which is a **decision about** the absence of the three rows above it rather than a member of the
  129. Removing it gives **6**, and `129 − 8 − 6 − 1 = 114`. Removing all seven gives `129 − 8 − 7 − 1 = 113`.
  **Neither is 115.** No arrangement of the document's own stated counts produces 115.
- **CONSEQUENCE, stated plainly: the 115 was never a reconciled figure.** It was an area-by-area sum with a
  top-down check bolted on that did not close, and the discrepancy sat unread for eight days across every
  document that quoted it. **The check is retired rather than repaired** — §1.2 is now derived bottom-up from
  the 200 named rows in §1.2a, so there is nothing left for a top-down identity to corroborate. The three
  inputs (90 / 28 / 11) are themselves unverified assertions of the source census and are **not** carried
  forward.

**§1.1 rule 4's "13 rows" is UNSOURCED too, and is corrected here rather than quoted onward.** Rule 4
enumerates the architecture-excluded set inline; §4's closing paragraph enumerates it again. **The two lists
are not the same list, and neither totals 13.** §4's list has **12** distinct names (Tally.NET, Remote Access,
Control Centre, Support Centre, TRiB, SMS, Auditors' Edition, Tally.Server 9, multi-site/rental licensing, TDL,
multilingual, international statutory packs). Rule 4 names three more that §4's list omits — **Data
Synchronisation, the 7.2 data-format migration tool and the 7.2 character-grid UI**. The **union is 15**.
Nothing downstream moves, because these rows are excluded from the denominator entirely; the mismatch is
recorded because it is the same species of defect as the reconciliation above — a count nobody could derive
from the list beside it. **§4's closing paragraph is the canonical list; rule 4 points at it.**

### 1.3 The honest "cannot tell" bucket — and it never was 8

~~The 8 in the table are capabilities whose **existence** nobody has checked. That is the small number.~~
**AMENDED 2026-08-18:** §1.2's undetermined column is now **0** — all eight of those rows were resolved in
§1.2b, six of them by one grep each. **That does not change this section's point; it sharpens it.** The
undetermined column was never the honest cannot-tell number, and now that it is zero the distinction is
impossible to blur. The real one:

**Existence was measured. Fidelity was not.** All three mapping agents measured *does the code exist and can a user reach it*. Almost nothing was measured against *does it behave the way Tally behaves*. Capabilities with any sourced behavioural verification at all:

> **▶ 🔴 METHOD NOTE — 2026-09-04. WHAT "COMPARED" IS GROUNDED ON FROM THIS DATE. USER RULING 14 (R12).**
> *(This is a method note about the section, deliberately **NOT a numbered item**: it adds no compared
> capability, carries no grade token, and **the anchor block below is untouched by it**. Do not number it —
> `CensusFidelityDerivationTests` counts numbered items and would demand a grade this note must not have.)*
>
> **THE CORPUS IS GONE.** `tally/` exists and is **empty** — zero entries, hidden files included, measured
> independently by three agents and the main loop on 2026-09-04 across the base tree and every live worktree,
> and re-verified first-hand when this note was written. **Git has never tracked it:** `git log --all --
> tally/` returns no commits on any ref and `git ls-files tally/` returns zero, because R4 correctly
> git-ignored the folder as third-party IP (`.gitignore`, line 73). **There is nothing to restore from, by
> anyone.** This is unreachable **by construction**, not by effort — so the honest word is **GONE**, not
> *unreached*, and no future pass should re-open the question or file it under §6a.
>
> **THE COMPARED SET IS NOW GROUNDED ON THE VENDOR DOCUMENTATION.** From this date an item joins figure (1)
> when its **shipped behaviour has been compared** to, in this order: **(1) `help.tallysolutions.com`**, the
> vendor's own published product documentation — the fidelity ground truth for behaviour, navigation and
> shortcuts; **(2) official statutory sources** for law and rate facts (`cbic-gst.gov.in`,
> `incometaxindia.gov.in`, `epfo.gov.in`, `esic.gov.in`, `indiacode.nic.in`); **(3) `docs/tally-feature-catalog.md`
> and its verification report**, which are **INTERNAL** and admissible only where (1) and (2) are silent —
> an internal restatement is never an independent source.
>
> 🔴 **NO EXISTING ITEM IS DOWNGRADED, AND THIS NOTE MOVES NO DIGIT.** The anchor block's four figures are
> unchanged, because **no item is being added and none is being removed**. Ruling 14 settles **U-3** — the
> open R12 question item 14 states and declines to answer — **YES, by necessity**: an item verified against
> official vendor or primary-legal sources **with no corpus page belongs in the compared set**. That was
> already the load-bearing precedent here (items 1, 3, 5 and 15 rest on official pages with no corpus page at
> all, and item 15 says so in terms; items 16, 17 and 18 were folded in on it), so the ruling **ratifies the
> existing practice rather than changing it**. ⚠️ Had it gone the other way, seven items would have come out
> and the anchor would have fallen to single digits — which is why it is recorded here and not assumed.
>
> **WHAT DOES NOT CHANGE, AND IT IS THE WHOLE POINT.** **Nothing is marked COMPARED that was not actually
> COMPARED.** A citation must still resolve **by content**; a blog, cleartax, taxguru or an undated rate chart
> is **not** a source. Where **no** admissible source speaks, the capability still ships as a **documented
> divergence labelled as ours** and **never joins figure (1)** — ruling 9's honest limit survives ruling 14
> with its subject changed and its floor still above zero. **The corpus-silence caveats already written into
> individual items below stand as written**; they are now vendor-silence caveats, and the distinction they
> draw — sourced versus ours — is exactly the one this section exists to keep.

1. Chart of accounts — 28 predefined groups (OFFICIAL help.tallysolutions.com, verification report A1) `[GRADE: COMPARED]`
2. Double-entry posting — Robert and Bright fixtures reproduce to the paisa `[GRADE: COMPARED]`
3. Voucher shortcut keys (OFFICIAL keyboard-shortcuts page) `[GRADE: COMPARED]`
4. PO/SO/GRN/DN stock-vs-accounts effect rules (corpus BOOK p.67) `[GRADE: COMPARED]`
5. EPS/EPF split (OFFICIAL epfindia.gov.in) `[GRADE: COMPARED]`
6. Rule-88A ITC set-off with the §49(5)(c)/(d) proviso `[GRADE: COMPARED]`
7. GSTR-1 amendment section-to-table map (A14-confirmed in-file) `[GRADE: COMPARED]`
8. Cost category/centre worked example (corpus SG pp.101-102) `[GRADE: COMPARED]`
9. **Company creation & alteration - the profile screen (added 2026-08-16 with W0-2b; row rewritten
   2026-08-17 after review).** **PARTIAL, and the partial is the point.** `[GRADE: COMPARED]`

   **What IS sourced - each with the page it comes from, and nothing else claimed:**
   - **The field set and its three section headings**, reproduced verbatim: *Primary Mailing Details* ->
     *Books and Financial Year Details* -> *Base Currency Information* (Study Guide PDF pp.58-60).
   - **The field labels**, now matched to the corpus word-for-word rather than shortened: *Financial year
     begins from*, *Books beginning from*, *Base Currency symbol*, *Formal Name*, *Number of decimal places*,
     *Word representing amount after decimal* (Book PDF pp.13-14; Study Guide pp.59-60). They shipped as
     "Year begins from" / "Books begin from" / "Symbol" / "Decimal places" / "Decimal unit", which matched
     neither source - the fifth being the one a Tally operator would genuinely fail to recognise, since the
     value in it is "Paisa".
   - **`Alter`'s stated purpose** - Book p.15, verbatim: companies *"will alter or edit their information when
     they have changed company **address** or **contact number** or **email** and other any information."*
     **One of those three ships**: the address. Contact number and e-mail are out of scope (grounding section 9
     item 9), so this sentence supports the *existence* of Alter and one of its three named uses, not all of it.
   - **The `Alt+K` route to Alter** - Book p.15 (*"Gateway of Tally > Alt+K > Alter"*) and Study Guide p.61
     (*"press Alt+K and open the company menu -> Click ALTER option in it"*), corroborated by SG p.267 step 2
     (*"press Alt+K (Company) Create"*). **We did not ship it** - see the "ours" list below for the real
     reason, which is not the one first recorded.

   **What is OURS, or unsettled - separated from the sourced column deliberately**, each logged in
   `docs/w0-2-company-screen-grounding.md` section 9 items 12-22:
   - **The State-before-Country order is ONE PRIMARY SOURCE AGAINST ANOTHER, not a resolved conflict.**
     Book p.13 lists Company Creation as Address -> **State** -> **Country** -> Pin Code; Study Guide pp.58-59
     lists it as Address -> **Statutory Compliance for** (the country) -> **State** -> Pin Code. We follow the
     Book. WARNING - **CORRECTION 2026-08-17:** this row previously said the Study Guide's *"own worked example
     (p.268) sides with the Book against its own prose"*. **It does not.** Re-read this session
     (`pdftotext -layout -f 267 -l 268`): p.267 step 3 presses **Alt+R**, and step 4 reads *"In **Group
     Company Creation** screen, provide required informations as follows:"* - the State/Country/Pincode list
     under it belongs to the **Group Company Creation** screen, which the grounding doc itself treats as a
     different screen (section 3, section 5.1). There is no proven self-contradiction inside the Study Guide,
     and the same correction applies to grounding section 2.1's two "one source disagreeing with itself"
     claims. The shipped order is **defensible on the Book alone**; it is not "resolved on evidence".
   - **The GST-home-State inheritance being a DISPLAY default is OUR inference, not the corpus's.** The corpus
     sentence - Book p.177, *"by default **shows** the State name as selected in the Company Creation
     screen"* - attests **that the GST State defaults from the postal State** and nothing more; "shows" in a
     user manual carries no display-versus-store semantics. The real reason the seed is a display default is
     **internal to our store**: it binds `gst_home_state` whenever a `GstConfig` object exists but rebuilds
     that config only when `gst_enabled = 1`, so a code stamped onto a GST-off company is discarded by the
     very next load and nulled by the save after it. That reasoning is at `GstConfigViewModel.cs:565-583` and
     is pinned by `A_GST_home_State_is_never_written_onto_a_GST_off_company`. Sourced: the defaulting. Ours:
     the display-not-stamp shape.
   - **The divergence warning** and its wording (item 12) - no corpus source describes any such advisory.
   - **The postal State list being the GST state-code list**, which means a **non-Indian postal State cannot
     be recorded** (item 13).
   - **The book-date advisory** on a book that already carries vouchers (item 14).
   - **The read-only company Name on Alter** - a storage constraint of ours (the `.db` is named after the
     company), not a fidelity finding (item 15).
   - **The `Accept Company? (Y/N)` prompt** (item 16) - and with it a **behaviour change on Creation**: Enter
     used to create the company outright, and now raises that confirmation first. Ctrl+A is unchanged.
   - **No `Alt+K` accelerator, and the first reason recorded for it was wrong** (item 17). It said the chord
     "is already bound in this application". Measured: `MainWindow.axaml.cs` **line 757** binds it as
     `Key.K && Alt && vm.IsReportContext` - **report context only**, and unbound on the Gateway root column
     where this row lives. The honest reason is that the attested route is Alt+K -> a **company menu** ->
     Alter, and we have no company menu; binding the chord straight to one page would be an invented shortcut
     wearing an attested chord. The company menu is owed, not refused.
     *(🔴 **Citation re-pointed 2026-08-18.** This sentence cited **line 653** of that file, which now holds
     `vm.TogglePostDated();` — the Ctrl+T post-dated toggle, a different chord entirely. Re-located **by
     content**: the only `Key.K` in the file is the `if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
     && vm.IsReportContext)` at line 757, which is what this sentence describes. **The claim is unchanged; only
     its address moved.** Note WHY this survived: the citation test checks only that the path resolves and the
     line is inside the file, **never that the target says what the citing sentence claims**.)*
   - **The Gateway placement** (item 17). The row shipped as a NEW "Company" section placed **above** Masters,
     which is three moves in the direction `docs/invented-vs-cloned.md` **IV-29** already catalogues as wrong
     for this menu - and it silently moved the Gateway's default keyboard highlight off Masters -> Create.
     **Corrected 2026-08-17 to IV-29's own prescribed fix: "Alter Company" now sits under MASTERS.** The
     divergence that remains is recorded in IV-29: the reference product's Masters -> Alter is a *master*
     alteration submenu, whereas our row alters the *company*.
   - **Capture order versus print order now differ** (item 18): the screen captures Address -> State ->
     Country -> Pin, the printed supplier block renders Address -> Country -> PIN -> State (W0-2a, grounding
     section 9 item 11).
   - **The post-save hand-off departs from both the catalog and the corpus** (item 21). Study Guide p.60 [V]:
     *"After saving the company, takes you to the **Company Features** screen"*, and
     `docs/tally-feature-catalog.md` says the same. We go to the Gateway - `MainWindowViewModel.OpenCompany`
     calls `ShowGateway()`; there is no F11 hand-off anywhere. Recorded as a departure, not fixed here.

   **Deliberately NOT built - the complete list, with the reason for each:**
   - the five **contact fields** (Telephone, Mobile, Fax, E-Mail, Website): their order is contested between
     the two primary sources and they are fidelity fields, not compliance ones;
   - the three **base-currency formatting toggles** (*Suffix Symbol to Amount*, *Add space between amount and
     symbol*, *Show amount in Millions*): their defaults are undocumented in both sources;
   - **"No of decimal places for amount in words"** - a FOURTH base-currency field, not one of those three
     toggles, and its default IS documented (Book p.14 *"type '2'"*, Study Guide p.60). It is unbuilt because
     the domain has no such property, which is a schema change and out of this slice;
   - the whole **Security Control** heading - *Tally Vault Password* and *User Access Control* (Study Guide
     p.59, Book p.13). Two security features, unbuilt and un-designed;
   - **Directory** (the data-storage location, Study Guide p.58): a deliberate architectural difference -
     companies live in one managed folder (grounding section 7.4) - not an omission;
   - **Group Company / `Alt+R`** (Study Guide pp.267-268, Book p.14): a whole separate screen;
   - company **RENAME** (the `.db` file is named after the company, so a rename without a file move forks the
     book) and company **DELETE** (destructive; split out by an earlier ruling, `plan.md` VL-2).

> **▶ ITEMS 10-12 ARE GROUNDED AHEAD OF THE SLICES THAT BUILD THEM — READ THAT BEFORE COUNTING THEM.** They
> were drafted with the **Phase 10.11 voucher-lifecycle design** (2026-08-17) and landed with the R6 plan
> amendment that adopted its ten decisions, **before** S3 / S4 / S5a-c exist. Their **sourced** columns are
> real corpus findings and stand on their own; their **ours/unsettled** columns are the design's decisions,
> which the slices implement. **A row here is a fidelity measurement of a DESIGN until its slice ships, and
> then of the shipped behaviour.** Each says which it currently is. Pre-landing them is deliberate: it is what
> stopped the Alt+X/Alt+D provenance defect (item 10) from being written into the code first and questioned
> afterwards.

10. **Voucher cancellation (Phase 10.11 slice S3).** `[GRADE: COMPARED]` **BUILT** — shipped in slice S3 of Phase 10.11 (Alt+X, the single confirmation, the greyed Day Book row and the CANCELLED overprint are all live).

    **What IS sourced:**
    - `Alt+X` = *"To cancel a voucher"* / *"To cancel a voucher from a report"*, with the "Where does it work"
      column reading **"Vouchers & Reports"** — corpus BOOK PDF **p.437** [printed p.433], **re-extracted with
      `pdftotext -raw`** because `-layout` scrambles this table (see the method note at item 13).

    **What is OURS, or unsettled - and this is most of the row:**
    - **What cancellation MEANS is not in the corpus at all.** `grep -oic cancel` over all nine admissible PDFs
      returns **2 hits**, one of them an EPF *"cancelled cheque"* (BOOK PDF p.320). `struck`, `strike through`
      and `strike-through` return **zero**.
    - **Retaining the number, zero effect on balances, the greyed row and the "CANCELLED" overprint are OURS.**
      🔴 **And the belief that TallyPrime behaves that way is `[model-knowledge]` by the project's own
      verification report** — that report's item 14 is self-labelled `[model-knowledge]` and is listed again in
      its section 5 as *"Alt+X vs Alt+D numbering behavior"*, a claim *"needing a Tally spot-check"*. `plan.md`
      cited it as *"(verification §A14)"*, **a section identifier that does not exist**. Two independent lines
      of evidence — the tag and the corpus silence — now agree it is unsourced. **We keep the behaviour on its
      merits and stop crediting it to TallyPrime.**
    - The **confirmation wording** is ours, **UNVERIFIED-BY-DESIGN**; the delete wording is published, the
      cancel wording is not.
    - **Un-cancel is unsettled** and is not built.
    - **Our report-only scope for `Alt+X` is OUR decision, not fidelity.** `plan.md` said Tally *"scopes Alt+X
      to cancelling from a report"*; the corpus cell says **both** forms and scopes it **"Vouchers &
      Reports"**. Corrected in `plan.md` 2026-08-17.

11. **Voucher and master deletion (Phase 10.11 slice S4).** `[GRADE: COMPARED]` **BUILT** — shipped in slice S4 of Phase 10.11 (Alt+D on the five surfaces, the single confirmation, and the numbering / referential / bill-wise / master guards).

    **What IS sourced:**
    - `Alt+D` = *"To delete an entry from a report"* — BOOK PDF **p.435** [printed p.431].
    - The per-family register recipe *"For Delete Entry Press `Alt+D' on Selected Entry"* — BOOK PDF **pp.32,
      34, 37, 42, 47, 49, 64, 71**, and the inventory families at **pp.74, 77, 81, 83, 87, 92, 94, 99, 101**.
    - Master deletion is `Alt+D` from the **Alter** screen — BOOK PDF **p.15** (company), **p.21** (ledger),
      **p.23** (the voucher-type master), each *"Press Two times Enter"*.
    - The confirmation wording, verbatim, STUDY-GUIDE PDF **p.277**: *"Delete Yes or No?"* then *"Are you sure
      Yes or No?"*.
    - The guard, verbatim, STUDY-GUIDE PDF **p.67**: *"You cannot delete any ledger, if any transaction(s) has
      been already made with that ledger."*
    - Multi-master screens offer alter but **not** delete — BOOK PDF **pp.104-105**.

    **What is OURS, or unsettled:**
    - 🔴 **The SINGLE prompt, on ALL FIVE routes — and this row's earlier reading of it was WRONG.** It said the
      double prompt is *"attested for a group company and for masters, not for a voucher"* and filed the whole
      slice under a **decline-to-extend from silence**. Re-extracted first-hand (`pdftotext -raw`, S4 review
      2026-08-17) that is not supportable in either half:
      - **A MASTER is attested BOTH ways, in conflict.** BOOK PDF **p.21** gives the ledger recipe as
        *"… > Alt+D > Press Two times Enter"* (double); STUDY-GUIDE PDF **p.67** gives the same object as
        *"Press Alt+D supply Yes to confirm Deletion"* (single). The three MASTER routes S4 ships are therefore a
        **divergence from an attested scope**, not a decline-to-extend.
      - **A voucher is not silent either.** BOOK PDF **pp.22-23** carries a heading reading *"How to Delete
        Voucher …?"* over the same *"Alt+D > Press Two times Enter"* recipe, under a path that then reads
        `Alter > Voucher type` — the source contradicts itself inside one entry. Low-quality attestation is still
        attestation.
      - **What ships, and on what basis — 🔴 SETTLED BY THE USER 2026-08-18, IN TWO RULINGS, AND THEY ARE TWO
        DIFFERENT R7 CLAIMS THAT MUST NEVER BE MERGED.** The **BEHAVIOUR is unchanged** — a SINGLE prompt on all
        five routes, exactly as S4 shipped it. Only the RECORD changes, and it changes into two records:
        - **(A) THE VOUCHER ROUTES — OUR DECISION AGAINST WEAK, SELF-CONTRADICTORY ATTESTATION.** BOOK PDF
          **pp.22-23** carries a heading reading *"How to Delete Voucher …?"* directly over
          *"Alt+D > Press Two times Enter"*, and the same entry then contradicts itself — its path reads
          `Alter > Voucher type`. **The attestation is poor, and it EXISTS.** We keep one prompt, and we record
          it as **a decision taken AGAINST a weak, self-contradictory attestation** — explicitly **not** as
          "corpus silent" and **not** as a decline-to-extend-an-unattested-behaviour. **The whole earlier D-6
          record rested on that attestation's absence, and the absence was false.**
        - **(B) THE THREE MASTER ROUTES (ledger, group, stock item) — A DELIBERATE DIVERGENCE FROM AN ATTESTED
          SCOPE.** Here the double prompt IS cleanly attested: BOOK PDF **p.21** for a ledger and STUDY-GUIDE
          PDF **p.277**, with its wording, for a group company. We ship one prompt anyway, and we record it as
          **a divergence from an attested scope** — a different claim, resting on different evidence, from (A).
          *(Recorded beside it, because it narrows the divergence without changing its category: STUDY-GUIDE
          **p.67** attests a SINGLE prompt for the same ledger object. The ruling categorises this route as a
          divergence rather than as "a conflict resolved in favour of p.67", which is the conservative reading —
          we do not get to pick the friendly source and call the result fidelity.)*
        - **▶ WHY THE SEPARATION IS THE POINT, not pedantry.** Conflating the two is the exact R7 defect a
          review lens caught on S3. (A) and (B) would be defended with different pages, would be falsified by
          different findings, and would be re-opened by different evidence. **Anything that states one of them
          must state both, or state which one it means.**
        - **▶ THIS CLOSES THE OPEN ITEM THIS ROW USED TO CARRY:** ~~*"D-6's wording, which is voucher-scoped,
          should be amended to name the three master routes explicitly (open item for the user)."*~~ The user
          ruled on 2026-08-18; ruling (B) is that amendment. The superseded basis this row previously
          recorded — *"a CONFLICT RESOLVED IN FAVOUR OF ONE ATTESTED SOURCE … a third R7 category"* — is kept
          here quoted, and is **replaced by (A) and (B)**, which are two categories rather than one.
    - **The bill-wise blast radius, and it is ours.** A bill reference is a free string with no foreign key, so
      deleting the invoice that OPENED a bill while a later receipt still settles it produced a wrong FIGURE with
      a successful save — the party's money on neither Outstandings total. S4 refuses it. The corpus says nothing
      about bill-wise references and deletion; the guard and its wording are ours.
    - **What happens to a deleted voucher's NUMBER is not in the corpus.** Our behaviour is ours:
      `LedgerService.NextNumber` is `max+1` by scan, so **deleting the highest-numbered voucher reuses its
      number** and deleting a mid-sequence one leaves a permanent gap.
    - **Our refusal to delete a FILED statutory document, offering Cancel instead, is ours** — taken from the
      project's own numbering doctrine in `VoucherNumberingConfigViewModel`'s `IsFiledDocument`, whose cited
      source `numbering-design-v2 §2.5/§5.4` **is not in the repository** (a plan item is open to land it or
      restate its rule in-repo). **No numbering floor and no counter table is built.**
    - 🔴 **THE RESIDUAL, STATED AS A KNOWN AND ACCEPTED BEHAVIOUR RATHER THAN A SILENT ONE: deleting the
      highest-numbered voucher that is NOT filed still REUSES its number.** Defensible — an unfiled document
      number has no statutory life — and it is what *"may leave a gap"* implies for the mid-sequence case. It
      is written here so a reader meets it as a decision, not as a surprise.
    - **Company deletion is out of scope** (split out by an earlier ruling).

12. **Voucher alteration (Phase 10.11 slices S5a / S5b / S5c / S5d / S5e).** 🔴 **SHIPPED AND COMPARED —
    RE-GRADED 2026-08-20. THE HEADER PREVIOUSLY READ** ~~*"(Phase 10.11 slices S5a / S5b / S5c.) **GROUNDED,
    NOT YET BUILT.**"*~~ **and it was false for a day: `a34d989` (S5d) wired `Ctrl+Enter` on three surfaces and
    `b89213e` (S5e) opened purchase item invoices and gave POS its own door, and neither slice touched this
    file.** This item is now the **ruling-9 step-5a fidelity record for the whole alteration verb**, written by
    A14 on 2026-08-20 and covering S5a…S5e together, because the corpus does not slice the verb the way our
    diffs do. **§1.2a row 5.1 is `PARTIAL` on the strength of this record and carries no digits of its own.** `[GRADE: COMPARED]`

    **▶ 🔴 WHY THIS ROW IS THE ONE THAT WENT STALE — the mechanism, not the excuse.** `plan.md` §2.2 step 5a
    says *"§1.3 is where that count is maintained; do not copy the digits into this file"*. **S5d wrote a full,
    correctly-categorised R7 record — into `plan.md`.** So the gate was discharged in substance and every
    maintained figure here stayed wrong, because nothing derives one from the other. **S5e wrote no record at
    all**: `git diff --stat a34d989 b89213e -- plan.md` is empty, its 2,926-line `src`/`tests` diff carries
    **zero** corpus citations, and no artefact failed. **The fidelity gate is prose-checked, not
    derivation-checked** — filed as **T3** in §2 so it is fixed rather than re-discovered.

    **What IS sourced:**
    - **The register drill-down IS the alteration screen** — *"How to Show/Edit \<X> Voucher Entry in Tally
      Prime? … Select Month & **Show/Edit Entry**"*, BOOK PDF **pp.32, 34, 37, 42, 47, 49, 64, 71** and the
      inventory families; saved with **`Ctrl+A`** (BOOK PDF **pp.51, 53, 56, 58**).
    - **TallyPrime has no separate read-only voucher screen** — one action is named, not two.
    - `Ctrl+Enter` = *"To alter a master during voucher entry or from drilldown of a report"* — BOOK PDF
      **p.436** [printed p.432].
    - `Ctrl+D` removes a **line** inside voucher entry (same page) — a different granularity from `Alt+D`.
    - 🔴 **ADDED 2026-08-20, RE-EXTRACTED BY A14 WITH `-raw`, AND IT IS THE STRONGEST ATTESTATION IN THIS ITEM
      BECAUSE IT NAMES AN *INVOICE* RATHER THAN A VOUCHER FAMILY:** STUDY GUIDE PDF **printed p.281**,
      verbatim — *"Gateway of Tally Day Book select any Sale Invoice and press Enter"* / *"Sales Invoice
      alteration screen will appear"*. **Its purpose on that page is a Company-Logo print walkthrough, not a
      correction workflow** — recorded so nobody later reports it as a dedicated alteration section — but the
      sentence is unambiguous about the route and about the object. This is the page the SALES-item-invoice
      divergence below rests on, and it was NOT in this item before today.
    - 🔴 **ADDED 2026-08-20 — THE SECOND `Ctrl+Enter` SOURCE, AND ITS STANDING IS ITSELF AN OPEN USER
      DECISION.** SHORT-KEY PDF item **24**, verbatim: *"Ctrl+Enter View in Alter Mode"* — **with no object
      named**, sitting inside a run of ENTRY verbs (22 *"Shift+ENTER View in Details of Any Entry"*, 23
      *"Alt+F1 View Detail at Once"*, 25 *"Space Select any Entry"*, 26 *"Ctrl+Space Select All"*). Read
      against that source, S5d binding the chord to the highlighted posted **entry** is at least as well
      attested as the master reading, **so relabelling S5d a divergence, or unbinding the chord, would be the
      WRONG correction.** ⚠️ **BUT standing ruling X5 rejects this whole PDF as a corpus source**, on stated
      evidence (*"F6 = Contra"*, *"F8 = Stock Journal"*, *"Ctrl+A = Zoom"*, *"shifted by two rows"*) that a
      `-raw` re-extraction shows is a `-layout` artefact — items 17 `Ctrl+A` Save, 18 `Alt+D` Delete, 27 `F4`
      Contra, 28 `F5` Payment, 30 `F7` Journal, 33 `F8` Sales, 40 `F9` Purchase all agree with the Book and
      with the shipped contract. **Reinstating an excluded corpus source is an R12 user decision, not an agent
      call** (§6 and `plan.md` carry it as one). This item cites the source and flags its status rather than
      quietly promoting it.

    **What is OURS, or unsettled:**
    - **Our key bindings are a deliberate divergence:** plain Enter keeps a read-only VoucherDetail column and
      **`Ctrl+Enter` opens voucher alteration**, to preserve the Miller-column cascade (a settled user
      decision, with a follow-up to reconsider).
    - 🔴 **`plan.md`'s R7 line that Tally *"reserves `Ctrl+Enter` for display-only drill-down"* was WRONG and is
      amended.** The corpus makes `Ctrl+Enter` an **alteration** key for a **master**; our extension of it to
      **vouchers** is therefore a **smaller** divergence than the plan recorded, not a larger one. The claim
      appears to have been read off a `-layout` dump — see item 13.
    - ~~The **five refused families**, the~~ 🔴 **CORRECTED 2026-08-20 — "five refused families" IS STALE AND
      WAS ALREADY STALE WHEN WRITTEN.** The eligibility predicate is not a five-item list and has not been one
      for two slices: `VoucherAlterationEligibility` now composes roughly **fifteen** named refusal arms across
      `OffLineSideEffectRefusal`, `StatutoryDocumentRefusal`, `ProvisionalShapeRefusal`, `BaseKindRefusal`,
      `EntryModeRefusal`, `DerivedLegRefusal` / `ItemGridDerivedLegRefusal` and the payroll arm, with
      `PosAlterationEligibility` adding four more on the POS door. **Do not quote a family COUNT from this
      item** — the arms are the derivation and they are in the code. The **warn-and-proceed date change** and
      the **e-invoice / e-Way interlocks** remain **ours**.
    - 🔴 **THE ONE FAMILY STILL REFUSED AFTER S5e, NAMED HERE BECAUSE NO RECORD AT HEAD NAMED IT — THE
      *SALES ITEM INVOICE*. THIS IS A RULING-9 CATEGORY (b) DIVERGENCE: THE CORPUS ATTESTS THE ROUTE AND WE
      DELIBERATELY SHIP A NARROWER PRODUCT.** It is refused by name on the accounting door
      (`VoucherAlterationEligibility.EntryModeRefusal`) and refused again on the POS door (a `Sales` type is
      not a POS type), so **it is alterable by no key on any screen** — plain Enter reaches only the read-only
      column and `Ctrl+Enter` puts a sentence on the notice bar and opens nothing. **Against it stand two
      corpus pages**: the Study-Guide p.281 line above (*"select any Sale Invoice and press Enter … Sales
      Invoice alteration screen will appear"*) and the Book's section-terminal *"How to Show/Edit Sale Voucher
      Entry … Sale Register > Select Month & Show/Edit Entry"*, which closes a Sale (F8) section that
      explicitly covers **Item Invoice**, Accounting Invoice and As Voucher modes. **THE REASON IT IS REFUSED,
      STATED AS OURS RATHER THAN AS A NEUTRAL TECHNICAL LIMIT:** a Sales item-invoice line posts the
      **effective** rate and `voucher_inventory_lines` carries no list-rate and no discount column, so the
      keyed state is not recoverable from what was stored. ⚠️ **AND THE TEMPTING NARROWING IS THE TRAP:** the
      arm was NOT narrowed to *"the multiple-price-levels flag is on"*, because that flag is **live** and
      reading today's flag to judge a voucher posted months ago is the master-drift defect this phase has
      already shipped twice. **Lifting this needs a schema column, which is the user's to authorise (§5, FULL
      schema authority).** Recorded as **T2-11** in §2.
    - **`Duplicate` (`Alt+2`) and `Insert` (`Alt+I`/`Alt+A`) are corpus-attested (BOOK PDF p.435) and NOT
      BUILT** — a named carry-forward, not a silent omission. **§1.2a rows 5.4 and 5.5 stay ABSENT**, which is
      why row 5.1 moving to PARTIAL closes only T1-1's *alteration* half.
    - 🔴 **AND TWO ATTESTED `Ctrl+Enter` MASTER LIMBS ARE BUILT ON NEITHER SURFACE THE CORPUS NAMES — ADDED
      2026-08-20.** The Book's master reading is *"To alter a master during voucher entry **or from drilldown
      of a report**"*. **Neither** exists: the only `Ctrl+Enter` master arm is gated on the stock-item master
      screen (a master-creation list, not a report drilldown), and there is **no `Ctrl+Enter` arm on the
      voucher-entry screen at all** — so **no inline master alteration from a voucher field exists anywhere in
      the product.** That second limb is the substantive missing feature. ✅ **Master alteration itself is NOT
      unreachable** — plain Enter on the Chart of Accounts opens Ledger or Group Alteration
      (`MainWindowViewModel.AlterHighlightedChartRow`) — so this is a missing **route on an attested chord**,
      not a missing verb. **S5d's binding is left alone** for the reason in the Short-Key bullet above; the
      chord is still free on exactly the rows a master arm would claim, because the S5d arm returns
      `NoVoucherHere` and does not consume the key on a non-voucher row. Recorded as **T2-12**.

    **🔴 WHAT S5d AND S5e SHIP, IN THE TWO R7 CATEGORIES RULING 9 REQUIRES — THE STEP-5a RECORD PROPER.
    A row that does not name a category is not a discharged step 5a, so every line below names one.**

    **(A) ATTESTED AND FOLLOWED** — no divergence to defend.
    - **The register/Day-Book drill-down leads to the alteration screen and `Ctrl+A` saves it.** Both doors end
      in `Replace` and both bind `Ctrl+A`; the accept path branches on `IsAltering` rather than inventing a
      second save chord. Sources: the Book's eight `Show/Edit` sections and its `Ctrl+A`-to-save pages, above.
    - **The document keeps its number and its position.** `Replace` holds id, number, type and the provisional
      vector identical, which is what *"Show/Edit"* means in a numbered register.

    **(B) A DELIBERATE WIDENING OF AN ATTESTED BEHAVIOUR** — attested chord, wider object.
    - **`Ctrl+Enter` opens the highlighted posted VOUCHER for alteration on three surfaces** (the live report
      page, the register drill and the read-only voucher-detail column). Under the Book's master reading this
      is a widening from **master** to **voucher**; under the Short-Key reading (*"View in Alter Mode"*, no
      object) it is not a widening at all. **Both readings are recorded; neither is asserted as settled**,
      because the second source's admissibility is the open X5 ruling.
    - **S5e widens the ELIGIBLE SET, not the route:** the item-invoice refusal was written as a whole-family
      refusal and was only ever true of **Sales**, so **Purchase item invoices** now open on the accounting
      screen and **POS bills** open on their own screen with their own door and accept path. **Corpus status:
      the corpus attests altering a purchase entry from the Purchase Register in the same section-terminal
      form as the sales one, so the widening moves TOWARD the corpus, not away from it.**

    **(C) A DELIBERATE DIVERGENCE FROM AN ATTESTED BEHAVIOUR** — attested and deliberately not followed.
    - **Plain Enter keeps a read-only VoucherDetail column.** The corpus's own route is plain Enter to
      Show/Edit and it names **one** action, not two. Ours is a settled user decision (VL-1) taken to preserve
      the Miller-column cascade, **with a follow-up to reconsider**. This is the divergence, not the chord.
    - **The SALES ITEM INVOICE is refused entirely** — the bullet above, and the one gap in this row.
    - **The type F-keys do not CONVERT an altering voucher.** The corpus attests one conversion — memorandum →
      payment by **F5** on the memorandum alteration screen (BOOK, verbatim: *"Click on Payment (F5) button
      provided at memorandum alteration screen"* / *"The voucher will converted as payment voucher with same
      entry"*). `ConvertMemorandum` exists in `MainWindowViewModel` with **zero production callers**, so the
      attested verb is built and unreachable. **Only the work-loss half was fixed** (the keys no longer discard
      an unsaved entry silently); **the conversion half is owed** and is recorded as **T2-13**.

    **(D) OURS, CORPUS SILENT** — not verifiable from the sources this project admits, shipped as a documented
    divergence labelled as ours, and **never counted toward parity**.
    - The three alteration surfaces and the notice bar the refusals are shown on.
    - Every refusal SENTENCE, and the choice to refuse by name rather than fail in the engine.
    - The **tax-head shape pin**, the **cess magnitude pin** and the **tax magnitude pin** on both accept paths —
      the corpus says nothing about what happens when a master moves between posting and amendment. The two
      measured blind axes (**T0-14**, **T0-15**) are **CLOSED 2026-09-03**; both were OURS, and neither was a
      fidelity gap.
    - The POS alteration door as a distinct screen with its own eligibility list.

13. **METHOD NOTE — not a capability row.** `[GRADE: METHOD-NOTE]` 🔴 **`pdftotext -layout` silently scrambles the Book's three-column
    shortcut tables on pp.436-437.** The Key / Function / "Where does it work" columns come out as three
    independent top-to-bottom streams, so the reader must re-pair them by counting. On p.435 the counts happen
    to match (15 keys : 15 functions) and the pairing is recoverable; **on p.436 and p.437 they do not** — 20
    keys against 21 function-fragments, and 10 against 11 — so **any pairing read off a `-layout` dump of those
    pages is a guess**. `pdftotext -f <p> -l <p> -raw` emits the table cell by cell in true reading order and
    resolves all three pages unambiguously. **At least one shipped R7 claim was read off a scrambled dump** (the
    `Ctrl+Enter` claim corrected in item 12). ⇒ **Any fidelity claim sourced from BOOK pp.435-437 must be
    re-derived with `-raw`, and a `-layout` key/function pairing is UNVERIFIED unless the key count and the
    function count match exactly.**

14. **Printed documents for recipient-side vouchers — the entitlement / rendering / orientation split
    (Phase 10.13, T0-11 slice chain S0–S5; census rows 4.6, 4.7, 4.8, 12.2 and gap-register T0-11).**
    **GROUNDED; PARTLY BUILT — the PURCHASE ITEM-INVOICE half shipped 2026-08-20 (slice S2).**
    `[GRADE: GROUNDED-AHEAD]` Written by A14 + A13 on **2026-08-20** with slice **S0** (requirements
    amendment + ADR, docs only). It is the
    fidelity record **ruling 5** owes for this chain, landed **ahead** of the slices that build it, for the
    same reason items 10–12 were: pre-landing it is what stops a corpus-silent title string being written
    into the code first and questioned afterwards.

    🔴 **SLICE S2 MOVES NO ANCHOR FIGURE IN §1.3, AND THAT IS THE POINT RATHER THAN AN OMISSION.**
    S2 shipped a real, user-visible document — `PURCHASE RECORD`, with item detail, the supplier heading it
    and his tax captioned as his — but **every string and every layout decision in it is corpus-silent**, so
    **ruling 9** bars all of it from the shipped-and-compared set. It is therefore still **UNCOMPARED**, and
    figures (1) and (2) are unchanged **by S2**: this item keeps the grade `[GRADE: GROUNDED-AHEAD]`, which is
    exactly what puts it inside figure (2) and keeps it out of figure (1).

    **▶ 🔴 WHAT HAPPENS TO FIGURE (1) WHEN S3 AND S4 LAND IS AN OPEN RULING-9 QUESTION, AND IT IS STATED
    ONCE HERE RATHER THAN ANSWERED TWICE IN OPPOSITE DIRECTIONS (T0-11 review C19/L3-05, 2026-08-21).** This
    item used to assert BOTH that when they land ~~*"the anchor block still does not move"*~~ AND, sixty lines
    lower, that ~~*"figure (1) moves by one"*~~ — two opposite instructions for the next pass, inside one
    item, neither of them struck at the time. **The question neither of them had answered:** may an item
    whose **statutory** grounding is sourced, but every one of whose **strings** is corpus-silent, ever be graded
    `[GRADE: COMPARED]` at all? **Item 9 is the precedent for YES** — it is PARTIAL, its unsourced half
    enumerated rather than glossed, and it sits inside figure (1). **This item's own ruling-9 paragraph above
    is the argument for NO.** It is an **R12 question and this pass does not settle it**; until it is ruled the
    grade does not move, so none of the four figures moves either.

    **What IS sourced — statute, and it is the whole grounding of this chain:**
    - **CGST Act §31(1)/(2)** — the tax invoice is issued by *"a registered person **supplying**"*. This is
      the sentence that makes RQ-11 as shipped wrong and makes `IsTaxInvoice`'s Sales-only rule right.
    - **CGST Act §31(3)(c)** (bill of supply) and **CGST Rule 49** — *issued by the **supplier***. This is
      why a wholly-exempt **purchase** may not be titled BILL OF SUPPLY either.
    - **CGST Act §31(3)(f)** with **Rule 47A** — the reverse-charge **self-invoice**, the one document on a
      purchase the **recipient** is obliged to issue. **DEFERRED**, see below.
    - **CGST Act §31(3)(g)** — the **payment voucher** on every reverse-charge payment. **NOT BUILT.**
    - **CGST Act §34(1)/(3)/(4)** — the credit/debit note belongs to *"the registered person **who has
      supplied**"*, which is what makes debit-note entitlement bidirectional under one base type.
    - **CGST Rule 46** — the supplier particulars (name/address/GSTIN first; place of supply, address of
      delivery, reverse-charge flag and signature) that a recipient-side record must suppress.
    - **CGST Rule 53** — the credit/debit-note particulars, which are **value-level**: nature of the
      document, the corresponding tax-invoice serial and date, and value, rate and amount credited/debited.
      **No HSN, no quantity, no per-item lines.** This is what decouples the note document from T0-10.

    **What the CORPUS affirms — and it affirms only that the behaviour is WANTED, never how it LOOKS:**
    BOOK PDF **p.33** (Purchase F9), **p.39** (Sales F8), **p.54** (Credit Note), **p.61** (Debit Note) —
    *"item wise bills can be printed"*; **p.122** — a debit-note printout is sent to the party;
    **pp.211-212** — self-invoicing is entered through F9 > Ctrl+H > Item Invoice. **It SHOWS NO PRINTED
    OUTPUT for any of the four documents**, names no title, and evidences no law-driven title derivation.

    **What is OURS — every item below is ruling-9 category (a), CORPUS SILENT / OURS BY DESIGN, and NONE of
    them can ever join the shipped-and-compared set.** *(Category (b) — "the corpus attests X and we
    deliberately ship a narrower Y" — has **no** occupant in this chain, and saying so is part of the record:
    the two categories are kept strictly apart, per this section's own rule.)*
    1. **The four title strings — `PURCHASE RECORD`, `PURCHASE RETURN RECORD`, `CREDIT NOTE`, `DEBIT
       NOTE`.** The only title mechanism the corpus evidences is a free-text per-voucher-type default,
       carrying four unrelated values across four PDFs. The word "Tally" is in none of ours (ER-11).
    2. **The three-axis split itself** (entitlement / rendering / orientation) —
       `docs/adr/0002-printed-document-three-axis-split.md`. The corpus documents no such distinction.
    3. **Suppressing the tax-charged columns, our declaration and our signature on a recipient-side record.**
    4. **Rendering item detail on a note above Rule 53's value-level minimum**, if we ever do it.
    5. **Using the persisted original-invoice link as the debit-note entitlement discriminator.** The corpus
       supplies **both** debit-note directions as facts (BOOK PDF **p.60**) and **no rule for telling them
       apart at print time**.
    6. **The §31(3)(f) conjunction as an implemented predicate**, and **7. the 30-day / per-supply
       self-invoice discipline.**
    8. **Rule 53(3)'s "input tax credit not admissible" legend — NOT BUILT**, no field exists. Recorded as a
       known gap, not as a divergence.

    **▶ 🔴 THE CAVEAT THAT MUST TRAVEL WITH THIS ITEM — DO NOT DROP IT ON TRANSCRIPTION.**
    `taxinformation.cbic.gov.in` **fails TLS chain verification** for both a fetch tool and plain `curl`, and
    the CBIC bare-Act text underpinning §31 / §34 / Rules 46 / 47A / 49 / 53 was retrieved with certificate
    validation **bypassed** — an official source, read over a server presenting an incomplete chain. The
    `cbic-gst.gov.in` consolidated rules PDF that **does** read cleanly is updated only to **30-09-2020** and
    therefore contains **neither Rule 47A nor the 2024 §31(3)(f) Explanation**;
    `cbic-gst.gov.in/pdf/CGST-Rules-2017-Part-A.pdf` returns **404**. **CONSEQUENCE, AND IT IS BINDING:** the
    **SUBSTANCE** of the Rule 53(1A) particulars is verified, **the CLAUSE LETTERING IS UNREACHED**, and **no
    clause letter may be written into a requirement, a test name, a code comment or a printed legend** until a
    second reader re-verifies it. **Rule 54** (tax invoice in special cases, to which Rule 46 is expressly
    *"subject"*) was **NOT READ** in this pass and may add particulars for ISD / banking / GTA documents.

    **▶ FIDELITY ARITHMETIC EFFECT, STATED SO NOBODY READS THIS ITEM AS PROGRESS ON FIGURE (1).** This chain
    moves census rows **4.6, 4.7, 4.8, 12.2** from *uncompared with no sourced verification* to *uncompared
    but **GROUNDED***. It adds **ZERO** to shipped-and-compared **now**, and it can ever add **at most one** —
    never four — because this is **one item header**, not four. **Whether it adds that one when S3 and S4 land
    is the open ruling-9 question stated at the head of this item**, and it is deliberately not answered here:
    ruling 9 bars corpus-silent behaviour from the verified set, and every title on every one of these four
    documents is corpus-silent, while the chain's **statutory** grounding is sourced throughout. What is
    settled either way, and is all this paragraph ever needed to say: it does **not** move by four, and it does
    **not** move now.
15. **GST rate resolution above the Stock Item — the five-level walk and the two source-order options
    (T0-4, slices S1 / S2a / S2b; capture is S3/S4, the HSN half is S5; census rows 6.4 and 3.13, gap-register
    T0-4 and T0-17 … T0-20; register `docs/invented-vs-cloned.md` IV-1).** **BUILT — the RATE half shipped 2026-09-03.
    PARTIAL, and the partial is named rather than glossed.** `[GRADE: COMPARED]` Written by A14 + A13 on
    **2026-09-03**, the R5/R6/R11 documentation gate the S1/S2a/S2b chain had not paid.

    🔴 **READ THE SOURCE CLASS BEFORE THE ROW — IT IS THE WHOLE OF RULING 9's DISTINCTION AND THIS ITEM IS THE
    first to sit squarely on it.** The order the engine now walks is **VENDOR-attested and CORPUS-SILENT.**
    Those are not the same claim, and the chain's own design records the measurement: **zero hits for
    `hierarch*` in a GST-rate sense across all ten PDFs**, in both `-layout` and `-raw`. The corpus names four
    of the five *levels* (see IV-1's 2026-08-15 † correction, which withdrew an earlier "corpus silent" that
    was overstated) but **states no order anywhere**. The order comes from one vendor page. Under ruling 9 that
    is enough to be COMPARED — items **1**, **3** and **5** rest on official vendor/authority pages in exactly
    the same way — but it is **not** corpus verification, and this item never claims it is.

    ⚠️ **AND ONE LIMIT ON THIS PASS, STATED RATHER THAN HIDDEN: A13/A14 COULD NOT RE-MEASURE THE CORPUS TODAY.**
    The git-ignored `tally/` directory is **EMPTY** in this environment (`ls` returns nothing; the ten PDFs are
    not on disk), so **every corpus claim in this item is RELAYED from the T0-4 design pass of 2026-08-20, which
    did have them, and is not independently re-run here.** The vendor URLs were likewise **not re-fetched** this
    pass. Nothing below is weakened by that — but a later reader must not mistake a relayed measurement for a
    fresh one, and the next agent with the corpus mounted should re-run the `hierarch*` grep before this item is
    quoted as settled.

    **What IS sourced, and followed** — VENDOR [web], `help.tallysolutions.com`, *"HSN/SAC & GST Rate Hierarchy
    in TallyPrime"*, both strings transcribed verbatim into the engine as data:
    - **The shipped default `Ledger → Accounting Group → Stock Item → Stock Group → Company`** and the
      selectable alternative **`Stock Item → Stock Group → Ledger → Accounting Group → Company`**. They live in
      `GstService` as `LedgerFirstWalk` and `StockItemFirstWalk` — **two ordered lists driving ONE loop**, never
      two hand-written code paths, because two hand-written walks are exactly how the D9 bypass readers came to
      disagree with `ResolveRate` in the first place. The vendor's own name for the second rung is **"Accounting
      Group"**, and that is the name the enum member carries.
    - **Stop at the first level that carries the detail** — *"TallyPrime first checks the ledger for the
      details. If not found there, it will move to the Group, then Stock Item, and so on."*
    - **Company terminates BOTH strings.** That is why the ER-5 unresolved sentinel moved from two rungs in to
      **behind** Company: the old position hard-blocked a book that had set its rate exactly where the reference
      product tells a single-rate business to set it.
    - **The two rungs above the Stock Item are read at transaction time, not decoration** — corpus GSTN PDF
      **pp.130-135** works "GST on Stock Group Level" end to end (Stock Groups at 12% and 18%, items under them
      with no item rate, vouchers computing 12%/18%). *(Relayed — see the limit above.)*
    - 🔴 **R12 — USER RULING, THIS SESSION (2026-09-03), quoted because it is the one decision here that moves
      money:** on books created from **v51 onward the SALES/PURCHASE LEDGER OUTRANKS THE STOCK ITEM** — honour
      the `LedgerFirst` order the column already defaults to, flipping the item-first walk that shipped through
      Phase 4. Pre-v51 books are back-filled to `StockItemFirst` and keep resolving exactly as before. The
      ruling was taken on the recommendation in the design's open question 1; the alternative offered was to
      treat `LedgerFirst` as a stored-but-unused label.

    **What is OURS, or unsettled — every line names its ruling-9 category, because a row that does not name one
    is not a discharged step 5a.** Category **(a)** = *corpus silent, ours by design*; category **(b)** = *the
    corpus attests X and we deliberately ship a narrower Y*. Each of these carries a labelled row in
    `docs/invented-vs-cloned.md` (IV-36 … IV-43); this item is the index, that register is the detail.
    - **(a) ANCESTRY — the biggest one, and it changes the tax.** The Accounting Group and Stock Group rungs
      **climb the parent chain to the nearest ancestor bearing a non-null block**, not the immediate parent
      only. **Unattested in corpus AND in vendor — grounding UNREACHED in both directions.** We climb because
      both parent chains are real trees and a rate typed on a grandparent group is an ordinary book setup that
      the immediate-parent reading would silently drop. **The two readings give different tax**, so the choice
      is pinned by named tests (`GstHierarchyAncestryTests`) rather than left to whichever line got written.
      `MasterAncestry` carries a visited-set **cycle guard**; the only comparable walk in the tree,
      `ReorderStatus.ResolveDefinition`, has none, and a cyclic chain can already arrive through canonical
      import, which does not go through `InventoryService.EnsureStockGroupParentValid`. **IV-36.**
    - **(a) WHICH LEDGER.** The "Ledger" rung is the **sales/purchase** ledger, and the Accounting Group rung is
      **that ledger's** group ancestry, never the party's. The vendor says *"Ledger (sales/purchase)"* once and
      never resolves the party case; the corpus sets GST details on party ledgers too. Chosen because
      `GstReportSupport.ResolveValueLedger` already excludes the party ledger under a rule the project has
      locked. **IV-37.**
    - **(a) THE TAXABILITY SHORT-CIRCUIT.** An Exempt / Nil-Rated / Non-GST taxability declared at one rung
      **stops the walk** and the line is non-taxable, even if a lower rung carries a rate. **No source says
      whether it should.** We preserve the pre-T0-4 short-circuit, which is what keeps the existing exempt-item
      tests meaningful instead of quietly redefining them. **IV-38.**
    - **(a) PARTIAL-BLOCK SEMANTICS.** The two lookups walk **independently**, so a rung carrying an HSN but no
      rate does not stop the rate walk. Implied by two separately-selectable toggles and by Rule 46 making
      (g) HSN and (l) rate distinct mandatory particulars; **stated nowhere.** Only exercisable once S5 makes
      the HSN half hierarchical. **IV-39.**
    - **(b) CESS, REVERSE CHARGE AND ITC-ELIGIBILITY DO NOT WALK THE HIERARCHY — a NARROWING against an
      attested screen, not corpus silence.** `MasterGstDetails` carries four fields (HSN/SAC, Taxability, Rate,
      Supply Type); the cess, reverse-charge and ITC fields live only on `StockItemGstDetails`. So a rate
      resolved at the Accounting Group, Stock Group or Company rung bears **no cess and never fires reverse
      charge**. The corpus's own GST Classification screen carries Cess, *"Is reverse charge applicable"* and
      *"Is ineligible for input credit"* (BOOK PDF p.234, printed 230), so the reference product does not narrow
      this way. Widening it is a schema change and therefore an escalation, not a design decision. **IV-40 —
      and see the first of the two OPEN R12 questions below, which is this narrowing's measured bite.**
    - **(b) THE PER-MASTER SOURCE SELECTOR IS NOT BUILT.** TallyPrime's per-master field takes four values
      (*Specify Details Here* / *As per Company/Group* / *Use GST Classification* / *Specify in Voucher*); our
      nullable block collapses three into `null` and cannot express the fourth at all. **IV-41.**
    - **(b) NO GST CLASSIFICATION MASTER EXISTS** — one of the corpus's own five *methods* of applying GST.
      Correctly **out** of the five-level walk (it is a template applied *into* a master, not a rung), but its
      total absence from the product is a separate divergence and was unrecorded until now. **IV-42.**
    - **(b) NO DATED RATE HISTORY AT THESE LEVELS, AND NO GST RATE SETUP REPORT.** TallyPrime keeps a
      per-master *"GST Rate & Related Details (History)"* with an *"Applicable from"* date; our four fields
      carry one undated rate, and the company-level `GstRateHistory` override keys on a hard-coded item-first
      HSN pick. The vendor's bulk-edit *GST Rate Setup* surface, with its *"GST Rate Details Not Specified"*
      section, is not built. **IV-43.**
    - **(a) THE FAIL-FAST WHEN EVEN COMPANY CARRIES NOTHING.** The sentinel's **position** behind Company is
      attested; **hard-block versus zero-rate versus warn is ours**, and unchanged by this chain.
    - **(a) THE CORPUS'S OWN FIVE IS A LIST OF METHODS, NOT AN ORDER**, and it **omits the accounting Group**
      while **including GST Classification**. Our five and the corpus's five overlap in four. The accounting
      Group rung has **zero** corpus support and is **[web] only**. *(Relayed — see the limit above.)*

    🔴 **TWO LIVE BEHAVIOUR CHANGES ARE PINNED BY TEST AND NOT DECIDED — THEY ARE OPEN R12 QUESTIONS AND THIS
    ITEM DOES NOT SETTLE EITHER.** Both are stated with their measured figures so a ruling can be taken on
    numbers rather than on prose:

    > 🟡 **▶ STATUS 2026-09-04 — BOTH NOW SHIP A BUILT, LABELLED ASSUMPTION. NEITHER R12 QUESTION IS SETTLED, AND
    > NEITHER ASSUMPTION IS A RULING.** The user directed the track to proceed rather than stop, so each stated
    > assumption was built with a **one-line reversal switch**; flipping it restores, exactly, the behaviour the two
    > numbered paragraphs below describe, and all four assumption-pinning tests were verified to be the *only*
    > tests that move when both switches are off. **The two paragraphs are therefore kept verbatim as the record of
    > what was measured** — read them, then read the two ▶ blocks that follow each one.
    > - **(1) cess** → assumption **A-QA**, switch `GstService.CessWalksIndependentlyOfTheRate`, owner
    >   `tests/Apex.Ledger.Tests/GstCessIndependentWalkTests.cs`. **Register `docs/invented-vs-cloned.md` IV-40 is
    >   updated: PARTLY closed, not closed.**
    > - **(2) document title** → assumption **A-QB**, switch `GstReportSupport.AnchorIssuedDocumentCharacter`, owner
    >   `tests/Apex.Ledger.Tests/GstIssuedDocumentCharacterTests.cs`.
    >
    > 🔴 **NO SCHEMA COLUMN WAS TAKEN FOR EITHER** (three sibling tracks share this v52 base). What each one still
    > needs a column for is named in its own ▶ block, and neither was quietly widened.
    1. **THE STATUTORY-CESS NARROWING.** On a `LedgerFirst` book, a sales ledger whose block declares a rate but
       **no cess fields** wins the walk, and it therefore supplies the cess too — which means **no cess** — even
       when the stock item declares one. Measured, in
       `GstWinningBlockTests.The_source_order_decides_which_master_supplies_the_cess`
       *(renamed 2026-09-04 by A-QA to `…The_source_order_no_longer_decides_which_master_supplies_the_cess`, its
       `LedgerFirst` row inverted — grep for the NEW name)*: an item declaring
       ad-valorem cess at 1200 bp under a ledger declaring 18% and no cess yields, on a taxable value of
       **₹10,000.00**, cess of **₹1,200.00 under `StockItemFirst`** (every pre-v51 book, unchanged) and
       **₹0.00 under `LedgerFirst`** (every v51+ book). The rate is 1800 bp either way. **This is one walk and
       one winning block working as designed — the alternative is a line RATED off the ledger while its cess is
       read off the item — but whether the reference product narrows the same way is unsourced.** Two shipped
       Desktop fixtures had to declare the same cess on **both** masters to keep their money literals; that is a
       fixture fix, and the book shape they no longer cover is exactly the shape above.

       > 🟡 **▶ A-QA — BUILT 2026-09-04 AS AN ASSUMPTION, NOT A RULING. THE ₹0.00 IS GONE; THE ROW IS NOT CLOSED.**
       > **The assumption:** *cess walks INDEPENDENTLY of the rate — a rung silent on cess does not suppress a
       > lower rung's declared cess*, the same way HSN and rate already walk independently (IV-39). **The same
       > fixture now yields ₹1,200.00 under BOTH orders**, derived to the paisa (10,000.00 × 1200/10000). Reversal:
       > `GstService.CessWalksIndependentlyOfTheRate = false`.
       >
       > **What it did NOT do, and this is the larger half.** (a) The three **NARROW** rungs still carry no cess —
       > `MasterGstDetails` has four fields and widening it is a **schema change**, so a rate resolved at a Stock
       > Group, accounting Group or Company rung still bears none; still pinned by
       > `GstWinningBlockTests.A_rate_resolved_at_a_narrow_rung_bears_no_cess_even_on_a_cess_bearing_HSN`.
       > (b) **Reverse charge and §17(5) ITC-eligibility do not walk at all** — they read `ResolveDetailBlock`,
       > which A-QA deliberately did not touch, so IV-40's narrowing against the attested GST Classification screen
       > (BOOK PDF p.234, printed 230) stands for both. (c) The **RATE** walk is unchanged and asserted so in the
       > same test — independent means "cess does not stop where the rate stops", never "cess and rate read
       > different masters by default".
       >
       > ⚠️ **A FORCED LIMIT OF THE FIELD SHAPE, not a design choice.** `CessApplicable` is a non-nullable `bool`,
       > so a rung asserting *"cess does NOT apply here"* is indistinguishable from one that never mentioned cess;
       > under A-QA both read as silent. Separating them needs a nullable column — the same escalation as (a).
       > ⚠️ **"Declares cess" is `CessApplicable` ALONE.** A rung carrying `CessApplicable` with no
       > `CessValuationMode` is saying *"cess applies, take the figures from the dated master by my HSN"*, which is
       > an answer and stops the walk; pinned at 600.00 by
       > `GstCessIndependentWalkTests.A_rung_declaring_cess_without_figures_still_wins_the_cess_walk`.
    2. **THE DOCUMENT-TITLE FLIP ON AN UNTAXED VOUCHER.** No taxability is stamped on a posted line, so
       `GstReportSupport.IsBillOfSupply` re-resolves every stock line **live**. A voucher that posted **no** tax
       therefore has **no anchor at all**: with the item Exempt and the sales ledger Taxable at 18%, the same
       posted paper is a **BILL OF SUPPLY** under `StockItemFirst` and a **TAX INVOICE** under `LedgerFirst` —
       re-titled by a master option, months later, with no tax on it because none was ever posted. Pinned by
       `GstSourceOrderExistingBookTests.Flipping_the_source_order_DOES_move_the_document_title_on_an_untaxed_voucher`
       *(renamed 2026-09-04 by A-QB to `…An_issued_untaxed_document_keeps_its_title_when_the_source_order_flips`,
       its final assertion inverted — grep for the NEW name)*.
       **Posted MONEY is immune by construction** and that is separately pinned; the statutory *title* is not.
       Anchoring the title to posted data is **unavailable at this schema** — a zero-rated LUT/export supply is
       `IsTaxable = true` at 0 bp and also posts no tax legs, so *"no tax legs"* cannot tell the two apart; it
       needs a posted taxability marker, i.e. a column, i.e. an escalation.

       > 🟡 **▶ A-QB — BUILT 2026-09-04 AS AN ASSUMPTION, NOT A RULING. AND THE LAST SENTENCE ABOVE IS NARROWED
       > RATHER THAN REFUTED — READ THIS BEFORE QUOTING IT.** **The assumption:** *an issued document must not
       > change its statutory character retroactively.* The measured flip is gone: the same paper stays a **BILL OF
       > SUPPLY** under both orders, and the test that pinned the drift is renamed and **inverted** to
       > `GstSourceOrderExistingBookTests.An_issued_untaxed_document_keeps_its_title_when_the_source_order_flips`.
       > Reversal: `GstReportSupport.AnchorIssuedDocumentCharacter = false`.
       >
       > 🔴 **NO COLUMN WAS TAKEN, BECAUSE THE STAMP ALREADY EXISTS IN POSTED DATA FOR THE MEASURED DEFECT.** The
       > investigation the assignment asked for came back **yes, derivable** — but only for part of the space, and
       > the paragraph above is right about the rest. `GstLineTax` cannot help (an exempt line and a 0%-taxable
       > line both post **no** tax line at all — `GstService.ComputeInvoiceTax`'s `AddHead` returns early on a zero
       > amount), so "no tax legs" is indeed blind between Exempt and zero-rated. **What the paragraph did not
       > separate is that the ambiguity only bites where the taxable reading carries NO RATE TO POST.** Where the
       > taxable reading carries a **POSITIVE** rate the posted ledger is decisive by arithmetic: 18% of anything
       > posts tax legs, so a voucher recording no GST tax at all cannot have been issued under that reading. The
       > anchor therefore fires **only** when (i) the two published order strings disagree on taxability, (ii)
       > neither leaves the line unresolved, (iii) the taxable reading's rate is > 0, and (iv) the voucher records
       > no GST tax whatsoever — `GstService.TaxabilityIsSourceOrderDependent` plus `RecordsAnyGstTax`.
       >
       > 🔴 **THE RESIDUAL IS REAL AND IS THE ONE THING THAT STILL NEEDS A COLUMN.** Item Exempt against a sales
       > ledger Taxable at **0 bp** (LUT/export): both readings post nothing, the anchor deliberately declines to
       > guess, and **that title still moves with the master option**. Pinned, not hidden, by
       > `GstIssuedDocumentCharacterTests.The_zero_rate_versus_exempt_residual_still_moves_with_the_option_and_that_needs_a_column`.
       > **The escalation stands: a posted taxability marker on the stock line.**
       >
       > **Scope guards, asserted rather than argued.** A supply whose masters AGREE it is taxable is never
       > re-titled, taxed or not (`An_unambiguously_taxable_untaxed_voucher_is_never_re_titled`) — the anchor cannot
       > manufacture an exemption. The opposite direction was already anchored by `CarriesForwardTax`, the
       > predicate's first gate (`An_issued_tax_invoice_keeps_its_character_when_the_source_order_flips`). And
       > because the same `IsWhollyExemptItemSupply` serves `IsInwardBillOfSupply`, an inward movement's NIC e-Way
       > `docType` is now **order-independent** too, which is the intent — it follows the recorded tax, not an F11
       > option.

    **What this item does NOT cover, so the row is not read as more than it is.** The **capture** half (census
    row **3.13**, still `ABSENT`), the **HSN** half (`SourceOfHsnSacDetails` still has no reader), and the five
    D9 master-block rate bypasses — **nothing asserts they agree with `ResolveRate`**, and two of them feed
    statutory payloads. That last one is gap-register **T0-17**, opened by this pass — as are **T0-18** (the
    import-of-services RCM rate, hierarchy-blind AND date-blind), **T0-19** (both POS resolutions use the
    date-blind overload) and **T0-20** (the dated override's hard-coded item-first HSN pick contradicts the walk
    it sits on). ~~None of the four is fixed;~~ all four are recorded so the next pass cannot inherit them
    silently. 🔴 **AMENDED 2026-09-04: THREE OF THE FOUR ARE NOW FIXED.** **T0-18**, **T0-19** and **T0-20** are
    ✅ CLOSED — see their rows in the Tier-0 register for what shipped, what was DELETED (the date-blind
    `ResolveRate` overload; the RCM `?? 1800` floor) and the tail the change itself surfaced. ~~**T0-17 remains
    OPEN** and is still the most serious item the T0-4 chain left behind.~~ 🔴 **CORRECTED IN THE
    2026-09-05 MERGE: ALL FOUR ARE NOW CLOSED, AND THIS SENTENCE CONTRADICTED ITS OWN REGISTER ON `main`.**
    The Tier-0 row for **T0-17** reads ✅ CLOSED 2026-09-04 - all five master-block readers now resolve
    through ONE rule, `GstReportSupport.BucketingRateOf`, and the agreement assertion D9 declined to make was
    seen RED first. **A row's state is whatever its REGISTER row says; this prose was stale, and it is struck
    rather than deleted so the contradiction stays visible.**
16. **Voucher-type chords, the Day Book's voucher reach, and the report edit verbs — the wave-2 core-accounting
    and vouchers verification pass (census areas 1–5; rows 2.4, 2.5, 3.15, 4.1–4.18, 5.1, 5.2, 5.4–5.8, 5.10,
    5.11, 5.12; gap-register T1-17, T1-5, T1-18, T2-14 … T2-18).** **24 capabilities compared; 12 AGREE; the
    rest diverge and are named.** `[GRADE: COMPARED]` Written by A14 + A13 on **2026-09-04**, folding in the
    wave-2 verification pass.

    🔴 **READ THE SOURCE CLASS FIRST, BECAUSE IT IS NOT THE CORPUS AND THIS ITEM NEVER PRETENDS IT IS.**
    `tally/` — the git-ignored PDF corpus R7 names as the fidelity authority, and which A14 is the sole reader
    of — **was EMPTY on 2026-09-04.** Measured three independent ways by three separate agents that day: `ls -la`
    returns `.` and `..` only; PowerShell `(Get-ChildItem -Force -Recurse).Count` → **0** (`-Force` deliberately,
    so a hidden file would still have counted); and a recursive `*.pdf` sweep of the whole Desktop found 29 PDFs,
    **not one of them a corpus document**. All seven live worktrees are empty too. `pdftotext` itself is present
    and healthy — **the tool is fine, the inputs are gone**, and `.gitignore` is doing exactly what it was designed
    to do, so git cannot restore them. **Consequence, stated rather than worked around: "corpus first" could not
    be executed for a single row in this pass, and the `-layout` vs `-raw` method note this project requires is
    UNANSWERABLE here because no PDF was extracted at all.** Every comparison below therefore rests on
    **OFFICIAL VENDOR DOCUMENTATION** (`help.tallysolutions.com`). Under ruling 9 that is enough to be compared —
    items **1**, **3**, **5** and **15** rest on official vendor/authority pages in exactly the same way, and item
    15 says so in terms — but it is **not corpus verification**, and any row here can be **overturned or
    strengthened** when the PDFs come back. **See the UNREACHED register in §6a; restoring `tally/` is the single
    highest-value unblocking action left in this project.**

    **What IS sourced, and AGREES** — each compared against the vendor's own keyboard-shortcut page, action text
    quoted verbatim:
    - **The sixteen seeded voucher-type chords, one by one, against the official key for that type.**
      `src/Apex.Ledger/Seed/SeedVoucherTypes.cs` read in full: Contra F4 · Payment F5 · Receipt F6 · Journal F7 ·
      Sales F8 · Purchase F9 · Credit Note Alt+F6 · Debit Note Alt+F5 · Stock Journal Alt+F7 · **Physical Stock
      Ctrl+F7** · Sales Order Ctrl+F8 · Purchase Order Ctrl+F9 · Delivery Note Alt+F8 · Receipt Note Alt+F9 ·
      Rejection Out Ctrl+F5 · Rejection In Ctrl+F6 · Payroll Ctrl+F4. **Sixteen of sixteen agree**, and the four
      null-shortcut families (Memorandum, Reversing Journal, Material In/Out, Job Work In/Out Order) are
      consistent with a page that does not list them. **This EXTENDS item 3 rather than repeating it:** item 3
      recorded the *page* as checked; this checks the **shipped seed strings against it**, which item 3 did not do.
    - **Physical Stock = `Ctrl+F7`, and it settles a live contradiction between two in-repo records that cite the
      SAME url.** `SeedVoucherTypes.cs`'s own comment says the official reference gives `Ctrl+F7` — **correct**.
      `docs/tally-feature-catalog-verification-report.md` **item A12** says Physical Stock has *"no dedicated
      function key"* and routes it through `F10 (Other Vouchers)` — **wrong**, and that report's own §(C) item 1
      already hedged it as a *"residual uncertainty"*. **The uncertainty is closed in favour of the code.** ⇒ a
      defect against `docs/`, not against `src/` (**T3**, filed).
    - **`Ctrl+H` Change Mode** — *"To change mode – open vouchers in different modes"* (Right button) — bound at
      `src/Apex.Desktop/Views/MainWindow.axaml.cs:759`, gated on the change-mode entry screens. **Agrees on chord,
      function and surface**, and this is the corroboration that makes the `Ctrl+I` finding below a defect rather
      than a naming quibble: **we already have the right chord for mode-changing.**
    - **`Ctrl+A` accept/save**, across the create and alter paths.
    - **`Ctrl+L` Optional and `Ctrl+T` Post-Dated — chord AND surface both correct.** Both are officially *Right
      button*, i.e. **during entry**, not report verbs; both are dispatched from our entry screens. ⇒ this
      **narrows what T1-18 means**: the placement was never in question, so T1-18 is entirely about family
      coverage and about the flags being one-way. Neither row 5.7's nor row 5.8's stated gap is touched.

    **What DIVERGES — shipped behaviour against an attested source. These are defects and each has a register row:**
    - 🔴 **THE DAY BOOK OMITS EVERY INVENTORY AND ORDER VOUCHER, AND T1-17 NOW HAS A SOURCE BEHIND IT.** This is
      the most consequential row in the pass; **eight census rows (4.9–4.16) rest on it**, and until now T1-17 was
      an internally-noticed structural defect with no attestation at all. Shipped, read not inferred:
      `src/Apex.Ledger/Reports/DayBook.cs:36` is `foreach (var v in company.Vouchers)` and that is the **only**
      collection the report iterates, while `Company` carries **two** — `src/Apex.Ledger/Domain/Company.cs:421`
      (`Vouchers`) and `src/Apex.Ledger/Domain/Company.cs:511` (`InventoryVouchers`), the second of which
      `DayBook.Build` never reads. So Stock Journal, Physical Stock, Delivery Note, Receipt Note, Rejection In,
      Rejection Out, Sales Order and Purchase Order **cannot appear in the Day Book by construction** — not behind
      a filter a user could clear, but because the loop never visits them. Officially the Day Book shows *"all the
      vouchers, irrespective of the type of voucher"* via its **All Vouchers** option, and its narrowing filters
      are *accounting entries only* versus **inventory entries only**, the latter naming **Delivery Note** and
      **Physical Stock Voucher** explicitly — **so two of our eight are named by the source individually, and this
      is not an argument from a general sentence.** ⇒ **T1-17 is upgraded from a structural observation to a
      sourced fidelity divergence.** Aggravating and worth naming: `src/Apex.Ledger/Reports/DayBook.cs:25-26`'s own
      doc comment claims the report is *"all vouchers within a date range"* — **false for the product as built**,
      and exactly the kind of sentence a later reader trusts instead of measuring.
    - 🔴 **`Ctrl+I` IS BOUND TO THE WRONG VERB.** Officially *"To add more details to a master or voucher for the
      current instance"* — the **More Details** panel — and independently corroborated by a second official-domain
      result pairing *"Ctrl+H for Change Mode"* with *"Ctrl+I for More Details"* (checked twice because it is
      load-bearing). We spend it on the item-invoice toggle at `src/Apex.Desktop/Views/MainWindow.axaml.cs:744-750`.
      So mode switching is bound **twice** and the real `Ctrl+I` feature is unreachable. That More Details is a
      genuine separate TallyPrime feature we lack is corroborated by the verification report's own enrichment list.
      ⇒ census row 5.12's *"verified"* was the wrong word and is corrected there; **T2-14**.
    - 🔴 **`Ctrl+Enter`'s VOUCHER LIMB IS OFFICIALLY *DISPLAY*, AND §1.3 ITEM 12'S AMENDMENT WENT THE WRONG WAY.**
      The official chord carries **two limbs split by object**, both quoted verbatim from the *Bottom bar* section:
      *"To drill-down and **open a voucher for display**"* and *"To **alter a master** during voucher entry or
      from drill-down of a report"*. We open the highlighted posted voucher for **ALTERATION** on three surfaces
      (`src/Apex.Desktop/Views/MainWindow.axaml.cs:191`, `:268`, `:281`). **Item 12 struck `plan.md`'s line that
      Tally *"reserves `Ctrl+Enter` for display-only drill-down"* as WRONG; against the vendor's own page that
      line was substantially RIGHT, and our divergence is LARGER than item 12 records, not smaller.** Item 12's
      amendment appears to rest on a corpus cell carrying only the *master* limb, generalised to the whole chord.
      **And the same source refutes a second sentence in item 12** — its claim that the corpus *"names one action,
      not two"*: the reference product has **two** actions on a report row (plain Enter → alter; `Ctrl+Enter` →
      display) and **so do we, with the two chords SWAPPED.** That is a sharper statement of the divergence and it
      makes the fix cheap (exchange two bindings) rather than architectural. ⇒ **two corrections owed to a
      `COMPARED` item**; filed as **T3** because a wrong fidelity record is worse than a missing one.
      ⚠️ **Honest limit:** the *conflict* is established from the official page; whether the BOOK cell really
      carries only the master limb, or was mis-transcribed, **could not be checked — the corpus is gone.**
    - 🔴 **TWO ATTESTED CHORDS ARE OCCUPIED BY UNATTESTED FUNCTIONS, WHICH PRE-EMPTS THE ATTESTED VERB RATHER THAN
      MERELY OMITTING IT.** `Alt+I` is officially *"To insert a voucher in a report"* (confirmed again on the Day
      Book page); we spend it on the POS Single/Multi tender toggle at
      `src/Apex.Desktop/Views/MainWindow.axaml.cs:766-774`, and `InsertVoucher`/`RequestInsert` over `src/` returns
      **zero** (the only matches are unrelated persistence method names). `Alt+A` is officially *"To add a voucher
      in a report"*; we ship **three** `Alt+A` arms and the attested one is **third**, behind POS tax-analysis
      (`:778`) and Outstandings settlement (`:803`), reaching the real verb only at `:815` and only on the Day
      Book. **Building Insert later is therefore a chord CONFLICT, not a free addition**, and widening `Alt+A` to
      Outstandings collides with an arm that already owns that screen. Row 5.6 recorded the narrowing but not the
      shadowing, and the shadowing is the half that bites. ⇒ **T2-15**, and user ruling **U-6**.
    - **`Alt+2` Duplicate Voucher is attested and does not exist** — *"To create an entry in the report, by
      duplicating a voucher"*, confirmed a second time on the Day Book page. Measured, not taken from the row:
      `Key.D2`/`Key.NumPad2` over `src/Apex.Desktop/` → **zero**; `DuplicateVoucher`/`RequestDuplicate` over
      `src/` → **zero**. **Row 5.4's evidence cell is accurate and its attestation is upgraded from an
      unretrievable corpus page to a live official one, twice.**
    - **`Alt+X`'s scope is UNSETTLED and is deliberately NOT graded AGREES.** The official page gives **one**
      form, report-scoped — *"To cancel a voucher from a report"* — which is what `plan.md` originally said and
      what we ship, whereas §1.3 **item 10** records a corpus cell giving **both** forms scoped *"Vouchers &
      Reports"* and on that basis disclaims our scope as *"OUR decision, not fidelity"*. **The two sources
      conflict, and our scope may well be fidelity after all.** It is not resolved here because BOOK p.437 could
      not be re-opened — and item **13** is a standing warning that pp.435-437 are exactly the pages `-layout`
      scrambles. **What would settle it:** re-open BOOK p.437 with `-raw` once `tally/` is restored and count keys
      against functions per item 13's own test. **Note the pattern this is the second instance of** — an in-repo
      correction that struck a `plan.md` claim the official source supports, both times on the strength of a
      single corpus cell from the shortcut pages. **That is a mechanism, not two coincidences.**
    - **Two attested numbering methods have NO DOMAIN MEMBER AT ALL**, resizing T1-5 from a UI job to an
      enum + persistence + migration job. Detail in row 5.10; **T2-16**.
    - **The Voucher Type master's field set is now enumerable** — ~20 attested fields against exactly one settable
      in our UI, and that one (`TrackAdditionalCosts`) hangs off the **purchase-invoice screen** rather than a
      Voucher Type master, which does not exist. Row 3.15's recorded departure is thereby **sourced**, not a
      quibble. Row 5.11's *"Print voucher after saving"* is attested verbatim; its *"Use Common Narration"* and
      *"Show Inactive"* are **not on this page and are UNREACHED** — the nearest attested field, *"Provide
      narration for each ledger in voucher"*, is arguably the **inverse** feature, and **a row will not be graded
      on a name that could not be sourced.**
    - **Our `F10` is NARROWER than the attested one.** Officially `F10` is *"view list of all vouchers or
      masters"*; ours opens an **"Other Vouchers" menu** — attested chord, narrower object. **OURS by narrowing**,
      logged in `docs/invented-vs-cloned.md`.

    **What is CORPUS SILENT, and can therefore NEVER join the compared set (ruling 9, category (a)).** The
    **Attendance** voucher type is deliberately not seeded (decision D24 option B) while its enum member is
    deliberately **retained**, because `voucher_types.base_type` persists the **ordinal** and removing it would
    renumber `Payroll` and silently reinterpret every stored Payroll type. The shortcut page does not list
    Attendance; the verification report's claim about it is `[model-knowledge]`-adjacent and, on the Physical
    Stock evidence above, **that report item is not reliable**. ⇒ **corpus silent on whether a predefined
    Attendance voucher type must exist; the decision and its ordinal-safety reasoning are OURS**, documented
    in-code at the point of decision and registered in `docs/invented-vs-cloned.md`. Recorded here because the
    in-code comment is exemplary and must not be mistaken for a sourced claim.

    **What this item does NOT cover, so it is not read as more than it is.** **Area 1 was untouched** (F11/F12
    structure, Company Select/Shut, rename/delete — rows 1.5–1.9 not compared; the official F11/F12 pages were
    not fetched). **Area 2: only 2.4 and 2.5 compared** — 2.6 Voucher Class is the highest-value unreached row and
    is one fetch away, since the official Voucher Type page names *"Name of Class"* as a master field. **Area 3:
    only 3.15 touched**, and only via that field list; the whole inventory-master set is unverified and 3.13 (the
    T0-4 capture half) is the highest-value unreached row in it. **Area 4: 4.17 (Memorandum conversion) and 4.18
    (Reversing Journal Applicable-Upto) are UNREACHED ON BEHAVIOUR** — their chords verify, their semantics do
    not, and the memorandum/reversing-journal page **404'd**. **Area 5: 5.3, 5.9, 5.13, 5.14, 5.15 not compared.**
    The Robert/Bright fixtures (item 2) were **not reached**, so this item makes no claim about them either way.
17. **Statutory rates and payroll contributions against their PRIMARY INSTRUMENTS — the wave-2 statutory and
    payroll verification pass (census areas 6–8; rows 6.28, 6.29, 6.35, 6.36, 6.37, 6.39, 7.10, 7.11;
    gap-register T0-5, T0-6, T1-25, T1-26, T2-19, T3).** **31 capabilities compared against primary or official
    instruments; BOTH known-open R7 gaps are CLOSED, and a third nobody asked about.** `[GRADE: COMPARED]` Written
    by A14 + A13 on **2026-09-04**, folding in the wave-2 verification pass.

    🔴 **WHY THIS IS THE SLICE LEAST DAMAGED BY THE CORPUS BEING GONE, AND WHAT IT THEREFORE DOES *NOT* VERIFY.**
    GST / TDS / TCS / salary-IT / PF / ESI are **law**, and R7 already routes law facts to official sources —
    never to the corpus and never to memory. So the areas with the highest wrong-money risk are precisely the ones
    the empty `tally/` costs least. ⚠️ **But say the limit out loud: this item verifies LEGAL correctness, not
    Tally-BEHAVIOURAL fidelity. A rate can be lawful and still not be what the reference product does.** Do not
    read any row below as the latter. Method, because this project requires it: the EPFO contribution PDF is a
    **multi-column table** — the exact shape `-layout` scrambles — so it was run **both `-layout` AND `-raw` and
    the outputs compared**; they **agreed** (`-raw` renders the header column-by-column, `-layout` as a grid; same
    figures, same footnote markers), and it is quoted with that confidence. `incometaxindia.gov.in` 403s WebFetch
    and curl as recorded, so it was read through the **browser pane** via a same-origin fetch, which allowed ~60
    slugs to be probed cheaply. `esic.gov.in` fails WebFetch TLS validation; its HTML pages load in the browser
    pane but **its PDFs trigger a save dialog and could not be read** (see §6a).

    **What IS sourced, and AGREES:**
    - 🔴 **§194A's 10% — KNOWN-OPEN GAP (a), CLOSED.** **Finance Act 2025, First Schedule, PART II**, *"RATES FOR
      DEDUCTION OF TAX AT SOURCE IN CERTAIN CASES"*, item 1(a)(i), at `/w/first-schedule-100`, **Year 2025**. Its
      opening sentence names *"sections 193, **194A**, 194B … tax is to be deducted at the rates in force"* and
      item 1(a)(i) reads *"on income by way of interest other than \"Interest on securities\" — **10 per cent.**"*
      Shipped `1000` bp. **This is the exact document `src/Apex.Ledger/Seed/SeedTdsTcsRates.cs` says could NOT be
      retrieved.** The §194A(1) → §2(37A) → First Schedule Part II route is closed end to end, and the rate no
      longer rests on a commercial chart. ⚠️ **The THRESHOLD's vintage caveat is UNCHANGED**: `/w/section-194a`
      serves **Year 2026**, no Year-2025 slug exists (probed), and the page renders footnote **markers** but not
      **definitions** — so ₹10,000 is confirmed *current* and **not** confirmed to have been in force for
      FY 2025-26 by any source reached.
    - 🔴 **EVERY WITH-PAN TCS RATE — KNOWN-OPEN GAP (b), CLOSED**, against the **bare §206C, Year 2025**, at
      `/w/section-206c-36`: alcoholic liquor **1%** (Table (i)) · tendu leaves **5%** ((ii)) · timber under forest
      lease **2%** ((iii)) · timber otherwise **2%** ((iv)) · scrap **1%** ((vi)) · minerals coal/lignite/iron ore
      **1%** ((vii)) · **§206C(1F)** motor vehicle **1% of the value *"exceeding ten lakh rupees"*** — rate **and**
      threshold, previously chart-only · **§206C(1H)** *"a sum equal to **0.1 per cent of the sale consideration
      exceeding fifty lakh rupees**"*, the excess-only base being in the operative sentence · its **legacy
      year-gate**, third proviso, *"nothing contained in the provisions of this sub-section shall apply **from the
      1st day of April, 2025**"* — an exact match to the shipped cutoff date · and 🔴 **the §206C(1H) NO-PAN 1%,
      first proviso — THE ONE FIGURE IN THAT FILE WITH NO PRIMARY CITATION AT ALL, now cited.** **`[CHART-TCS]` is
      no longer load-bearing for anything.** The **§206CC** no-PAN computation (*"at **twice** the rate … or … at
      the rate of **five per cent** … shall **not exceed twenty per cent**"*, `/w/section-206cc-8`, Year 2025) was
      re-checked arithmetically against every shipped no-PAN row, and the **Year-2025 vintage of the existing
      citation is now measured rather than asserted.**
    - **Scrap stays at 1%** — recorded because it **retires the removed "2% from FY 2026-27" rumour**: the
      Year-2025 statute says One.
    - **Salary IT (row 6.39), against `/w/tax-rates-2` and the FA 2025 First Schedule:** the **4% Health &
      Education Cess** on *"income-tax **plus surcharge**"*, applied last with nothing charged on cess itself
      (and the old-regime §87A note's *"deductible from income-tax **before** calculating education cess"*
      independently confirms our **rebate-then-cess order**) · **§87A old regime** ₹12,500 cap, ₹5,00,000 cliff,
      **no marginal relief** — the absence included · **§87A new regime** ₹60,000 cap to ₹12,00,000 **with the
      marginal-relief FORMULA**, not just the cap · **all SEVEN new-regime §115BAC bands for AY 2026-27** ·
      **all three old-regime age bands**, against the **Act** rather than only the summary page, including the
      super-senior case where the 5% band correctly vanishes · the **surcharge ladder** 10/15/25/37%.
    - 🔴 **THE NEW-REGIME 25% SURCHARGE CAP — A THIRD GAP, UNASKED, AND THE DEPARTMENT'S OWN CHART IS THE THING
      THAT IS WRONG.** This shipped behaviour previously carried **no citation of any kind**. It is now sourced to
      the FA 2025 First Schedule proviso, verbatim: *"**Provided further that where the income of such person is
      chargeable to tax under sub-section (1A) of section 115BAC of the Income-tax Act, the rate of surcharge
      shall not exceed twenty-five per cent.**"* — and our `SurchargeBands(New)` omits the 5cr/37% tuple entirely.
      ⚠️ **The Department's own `tax-rates` chart prints a 37% band under its §115BAC heading**, and its only
      qualifying note is about §111A/112/112A/115AD special-rate income, **not** about §115BAC. **The Act governs;
      we are right and the chart is not.** Recorded so that a later reviewer reading the chart does not "fix"
      correct code.
    - **PF (row 7.10), against EPFO's *"PRESENT RATES OF CONTRIBUTION"*** — twelve figures, including: EPF
      **12% / reduced 10%** · EPS **8.33%** · EDLI **0.5%** · **EDLI base capped at ₹15,000 even when PF is paid
      on higher wages** (*"Contribution to be paid on **up to maximum wage ceiling of 15000/- even if PF is paid
      on higher wages**"*) — a subtle rule the code gets exactly right and which the higher-wages opt-in does not
      disturb · the **EDLI ₹75 cap**, stated rather than merely derivable · **EPF admin 0.50% w.e.f. 01-06-2018**
      · **admin minimum ₹500, and ₹75 with no contributory member**, applied **once per establishment** on the
      aggregate, exactly as the footnote's *"Monthly payable amount"* requires · **EDLI admin NIL w.e.f.
      01-04-2017** (another shipped figure that previously had no citation) · the **₹15,000 wage ceiling** · and
      **administrative charges on the higher wages for an opted-in member** (*"a **joint request** … In such case
      employer has to pay **administrative charges on the higher wages**"*).
    - 🔴 **AND THE ENGINE'S SINGLE MOST IMPORTANT DESIGN DECISION IS NOW SOURCED WORD-FOR-WORD.** The `##`
      footnote reads: *"Contribution is **rounded to the nearest rupee** for each employee, for the employee
      share, pension contribution and EDLI contribution. **The Employer Share is difference of the EE Share
      (payable as per statute) and Pension Contribution.**"* That is the **anti-3.67% rule** our code implements
      as `employerEpf = employeeEpf − pension`, not re-rounded, with the invariant `EPS + EmployerEpf ==
      EmployeeEpf`. The nearest-rupee-per-member-per-account rounding is attested by the same sentence.
      **The pre-existing item 5 (EPS/EPF split) is re-checked against this PDF and STANDS.**
    - **ESI (row 7.11), against ESIC's own pages:** EE **0.75%** / ER **3.25%** w.e.f. 01.07.2019 · 🔴 the
      **≤ ₹176 average daily wage exemption where the EMPLOYER STILL PAYS** (*"Employees in receipt of a daily
      average wage **upto Rs.176/-** are exempted … **Employers will however contribute their own share** in
      respect of these employees"*) — including the **inclusive "upto"** and the employer-still-pays half, which
      is the part most implementations get wrong · the two **contribution periods** 1 Apr–30 Sep and 1 Oct–31 Mar,
      **including the Jan–Mar wrap back to the previous October** · the **₹21,000 coverage ceiling and ₹25,000 for
      a person with disability**, both effective 01.01.2017.
    - **Two ABSENT rows corroborated against the instrument rather than only against our own grep:** **§206C(1G)**
      genuinely exists (Liberalised Remittance Scheme; overseas tour programme package) and we genuinely do not
      implement it (row 6.36); §206C's sub-sections (1-I)/(1J)/(3A) and the half-yearly return machinery exist and
      are unmodelled (row 6.35). **No state change proposed for either.**

    **What DIVERGES or is a sourcing DEFECT — each has a register row:**
    - 🔴 **EPS IS DEDUCTED FOR MEMBERS AGED 58+, WHERE EPFO SAYS IT MUST NOT BE.** Attested verbatim: *"**Pension
      contribution not to be paid:** When an employee **crosses 58 years of age and is in service** … In both the
      cases the **Pension Contribution @8.33% is to be added to the Employer Share of PF**."* Shipped:
      `PfContribution.ComputeMember` takes **no age, no date of birth and no date** — confirmed by zero hits for
      `58` / `pensionable` / `ceases` / `dateOfBirth` across the PF files, and both callers pass nothing
      age-related. **Blast radius stated precisely so it is neither over- nor under-sold: net pay and total
      employer cost are UNCHANGED** (the invariant holds either way); what is wrong is the **A/c 10 vs A/c 1 split
      on the challan and in the ECR file** — a **statutory-filing** defect misallocating up to **₹1,250 per member
      per month** to the pension fund. (The same PDF notes EDLI *is* still payable past 58, which we satisfy by
      accident.) ⇒ **T1-25**, filing-correctness, not wrong-net-pay.
    - **The two timber TCS master names disagree with the Year-2025 statute IN OPPOSITE DIRECTIONS.** Item (iii)
      is *"Timber **or any other forest produce (not being tendu leaves)** obtained under a forest lease"* and
      item (iv) is *"**Timber** obtained by any mode other than under a forest lease"*, item (v) now being omitted.
      Our `6CB` **understates** (iii) by dropping the forest-produce limb; our `6CC` **overstates** (iv), which
      covers timber only. **No money moves — both are 2%** — but an operator collecting on non-timber forest
      produce under a lease has no master whose *name* tells them `6CB` is right, and one collecting outside a
      lease is invited by `6CC`'s name to use a code the statute does not extend there. **Cheap fix, no schema
      change: rename to the statutory words.** ⇒ **T2-19**.
    - 🔴 **CITATION ROT ON A LIVE PAYROLL DEDUCTION.** `https://www.incometaxindia.gov.in/w/tax-rates` **returns
      404 today**, and it is the **only** source cited for the 4% cess the product applies to every salary TDS
      computation on the default path. `/w/tax-rates-1` also 404s. The page is alive and textually **unchanged**
      at **`/w/tax-rates-2`**, and the rate was re-verified there verbatim. **The rate is fine; the citation is
      dead.** Why this is worse than a broken link: this project's citation test *"checks only that the path
      resolves and the line is inside the file, never that the target says what the citing sentence claims"*
      (item 9) — **and it does not check web citations at all. T0-5 was closed on a URL that has since died and
      nothing in the repo would ever have noticed.** ⚠️ **Systemic:** the same reorganisation moved
      `tax-rates` → `tax-rates-2`; the other load-bearing incometaxindia citations in the seed file
      (`tds-rates-1`, `tcs-rates`, `section-194a`, `section-206cc-8`) were re-checked and are **still live**. **To
      the file's existing plain-slug roll-forward warning must now be added: WHOLE PAGES GET RENUMBERED.**
      ⇒ **T3**, and it re-points **T0-5**'s citation.
    - 🔴 **THE §192 SALARY-TAX ENGINE IS DATE-BLIND — FY 2025-26 TABLES ARE APPLIED IN FY 2026-27.**
      `SalaryIncomeTax.ComputeAnnual` takes **no date parameter**; the slabs, surcharge bands, cess rate, both
      standard deductions and both §87A ceilings are bare `const`s with **no effective-from** (zero hits for
      `effectiveFrom|financialYear|FyStart` in that file). The callers **do** hold the payroll date but use it
      only for the age band and months-remaining, **never to select a table**. **Today is FY 2026-27, and a
      September 2026 payroll run silently gets the FY 2025-26 tables.** **Is it currently wrong money? NOT PROVEN
      EITHER WAY, and that is the finding** — the Department publishes **no AY 2027-28 column at all**, so the
      forward figures could not be retrieved to compare (see §6a). **What is new is that the engine has no
      mechanism to ever hold two years**: the moment FY 2026-27 rates are published and differ, the product is
      wrong with no gate, no warning and no version to switch. Compare `SeedTdsTcsRates`, which **does** carry an
      effective-from and a legacy cutoff. ⇒ **T1-26**. ⚠️ **And census row 6.39's gap sentence is now imprecise in
      a way that matters:** the cess **is** sourced for the year the tables encode and was re-verified today; what
      is unsourced is the **forward** year, and the real defect is not the cess's sourcing but the **absence of any
      year dimension**. **Row 6.39's gap text should be re-cut along this finding.**
    - 🔴 **THREE STATES' PROFESSIONAL-TAX SLABS SHIP AS LIVE MONEY UNDER AN "A14-VERIFIED" LABEL WITH NO CITATION
      WHATSOEVER.** `ProfessionalTax.SeedSlabTables()` ships five slab tables that directly drive a monthly salary
      deduction — Maharashtra men, Maharashtra women (*"(2023 amendment)"*), Karnataka and West Bengal, including
      a **February over-charge** and a ₹25,000 women's exemption — under a doc comment reading *"A14-verified
      FY 2025-26"*. **A14 wrote this pass, and there is no citation here to verify against**: no URL, no Act
      section, no page, for any of the five tables. The **only** figure in that file that IS sourced is the
      **₹2,500 annual cap, correctly attributed to Article 276(2)** of the Constitution. **This is the T0-6
      pattern one step worse** — T0-6's rates at least cited *something* that could be seen to be inadmissible;
      these cite **nothing**, while carrying an agent's name as warrant. **A14 does not endorse that label and it
      should be removed or sourced.** Mitigations are real but partial: the tables are per-company editable and
      the annual cap bounds even a mis-configured table, so exposure is capped at **₹2,500 per employee per
      year**, not unbounded. ⇒ **T0-25**.
    - **EPFO has migrated domain.** `epfindia.gov.in` now **301s** to `epfo.gov.in`, and **item 5 cites the old
      host**. The redirect works today, so this is latent rot rather than a live break; the canonical document is
      now the `epfo.gov.in` contribution PDF. ⇒ **T3**.

    **What is CORPUS SILENT or OURS, and can therefore never join the compared set (ruling 9).** Each carries a
    labelled row in `docs/invented-vs-cloned.md`: **the ESI contribution BASE** (we charge on actual wages with no
    ₹21,000 cap on the base; ESIC's pages state the **coverage** ceiling and say nothing about the base once a
    covered member's wages rise mid-period — **and a secondary summary points AGAINST our rule, so this row must
    not be treated as verified in EITHER direction**) · **ESI rounding** (ceiling, each side independently, cited
    in-code to *"ESI Central Rules 1950, Rule 51"* — **the Rule could not be retrieved, so that citation is
    currently an assertion**) · **statutory bonus, every figure** · **gratuity, every figure**, including a
    provision that accrues **before** vesting, which the code itself labels as our own decision · **the five
    professional-tax slab tables** · **the four EPFO conditions for the reduced 10% EPF rate**, which we accept as
    a free config toggle with no rule — a deliberate, harmless simplification.
    🔴 **AND THE WHOLE OF AREA 8 (BANKING) CONTRIBUTED ZERO VERIFIED ROWS AND COULD NOT HAVE CONTRIBUTED ANY.**
    Rows 8.1–8.10 are **reference-product behaviour** — BRS mechanics, cheque layout, deposit slip, PDC handling,
    the Banking menu's row set. Nothing in the area is law-shaped, so with the corpus gone there is **no admissible
    source for any of them and no official-web substitute exists.** Stated so its absence is not read as oversight.

    **Where this item stopped.** Of 73 rows in areas 6–8, **31 capabilities across 7 rows** were opened and
    compared, chosen deliberately for where a wrong figure moves real money. **Not reached:** area 8 in full
    (structurally unverifiable, above); area 7 rows 7.1–7.9 and 7.12–7.21; **GST rows 6.1–6.26, left untouched ON
    PURPOSE** as the parallel slice's territory in a three-way split; and rows 6.27, 6.30–6.35, 6.38, 6.40–6.42 —
    the form/report/screen-shaped ones (Form 26Q, 16A, 27A, 24Q, 16, challan reconciliations), whose authority is
    the **NSDL/Protean FVU file specifications and CBDT form notifications**. **That last block is the single
    largest TRACTABLE piece of verification left and is recommended as the next A14 slice.**
18. **Reports, printing, data management and the long tail — the wave-2 breadth verification pass (census areas
    9–16; rows 11.6, 11.7, 11.8, 11.16, 11.17, 12.1, 12.3, 12.4, 12.5, 12.6, 13.5, 13.6, 13.10, 14.1–14.5, 14.9,
    15.1–15.9, 16.1–16.4; gap-register T1-14, T1-20, T2-5, T2-6, T2-7, T2-17, T2-18).** **25 capabilities
    compared; 4 AGREE and the smallness of that four IS the finding.** `[GRADE: COMPARED]` Written by A14 + A13
    on **2026-09-04**, folding in the wave-2 verification pass.

    🔴 **THIS ITEM USES "UNREACHED", NEVER "CORPUS SILENT", AND THE DISTINCTION IS THE WHOLE POINT OF RULING 9.**
    *Silent* is a claim about the corpus's **contents** — it means a reader opened the source and the source says
    nothing. **The corpus could not be opened at all** (see item 16). The honest label for *"the corpus might
    settle this and I could not look"* is **UNREACHED**, and writing `CORPUS SILENT` here would have manufactured
    a **permanent, unfalsifiable** verdict out of a **temporary** tooling failure — the single worst thing this
    pass could leave behind, because a corpus-silent row can never be re-opened under ruling 9 while an UNREACHED
    row is re-opened the moment the files come back. Every comparison below rests on **official vendor
    documentation**, which for *product behaviour* has one property the corpus does not: **it documents TallyPrime
    as it ships now**, whereas the corpus books are undated compilations. **What the web source cannot replace is
    the corpus's page-cited verbatim screen text** — register recipes, field-order lists, on-screen prompts — and
    rows needing those are UNREACHED in §6a. **No `pdftotext` was run in either mode; no finding here rests on an
    extraction of any mode**, recorded explicitly so a later reader does not assume one.

    **What IS sourced, and AGREES — four rows, and they are all small:**
    - **`Ctrl+P` prints the current report.** Officially *"To print the current voucher or report"* (Top menu).
      `src/Apex.Desktop/Views/MainWindow.axaml.cs:846` guards **Alt** only and does not exclude Control, so
      `Ctrl+P` reaches the arm and opens print preview. **This is the printing subsystem's ONE point of confirmed
      fidelity.** *(Recorded beside it as OURS, not as fidelity: **bare `P`** also opens it — no official source
      attests a bare-`P` print. Logged in `docs/invented-vs-cloned.md`. The second bare-`P` arm, a menu quick-jump
      to Profit & Loss, does not collide because the print arm is checked first and requires a printable page.)*
    - **`Alt+F1`** — *"To view the report in detailed or condensed format"* — bound to the detailed toggle.
    - **`F2`** — *"To change the date of voucher entry or date/period for reports"* — bound to the report as-of date.
    - **The FILTER half of `Alt+F12`** — *"To filter data in a report, with a selected range of conditions"*.
      *(The **sort** half is OURS and is registered as such.)*

    **What DIVERGES — chords bound elsewhere, chords unbound, and rules inverted. Every chord claim below was
    verified by ENUMERATING ALL HANDLERS for that key, not by finding one, which is what makes the inert-`Ctrl+E`
    claim safe to assert as *inert* rather than merely *not-found-yet*:**
    - 🔴 **`Ctrl+E` IS INERT AND `Alt+E` IS DOING `Ctrl+E`'s JOB (row 13.5) — the most actionable finding in the
      pass.** Full detail in row 13.5. **T2-17.**
    - **Export offers three formats where the source names seven** (row 13.6), with a fourth present under the
      wrong extension — **and the XML and JSON writers already exist in the tree** on the whole-company surface,
      making two of the four a route gap rather than a writer gap. **That is materially cheaper than the row's
      `= T2-6` grading implies.**
    - **Four attested chords are bound to different functions:** `Alt+K` — officially *"To open the company menu
      with the list of actions related to managing your company"* — is spent on **Saved Views** at
      `src/Apex.Desktop/Views/MainWindow.axaml.cs:835`, in report context only (rows 14.9 / 14.7) · `Ctrl+I` →
      item-invoice toggle, should be **More Details** (row 14.4; also item 16) · `Ctrl+H` — officially, on a
      **report**, *"To change view – display report details in different views"* — is bound only for **voucher**
      change mode (row 14.5) · **`Alt+F2`, and the census's own wording HIDES this one.** Row 11.16 calls it
      *"Alt+F2 period"*, which reads as a match. The source says *"the period of the **company**"* — in the
      reference product that changes the company's active period and every report inherits it. Ours changes **the
      current report's** window. **Same chord, same word "period", different scope of effect** — exactly the kind
      of near-match a prose-checked gate waves through, which is the failure mode item 12 diagnoses.
    - **Six attested chords are unbound:** `Alt+G` Go To (*"To primarily open a report, and create masters and
      vouchers in the flow of work"*) and `Ctrl+G` Switch To — **zero `Key.G` handlers in `src/Apex.Desktop`,
      re-run** · `Alt+U` (*"To display all hidden line entries, if they were removed"*) — **zero `Key.U` handlers,
      re-run** · `Ctrl+B` Basis of Values · `Ctrl+F12` Value Range · `Alt+P` (*"To open the print menu"*).
    - 🔴 **`Alt+C` / `Alt+N` INVERT THE REFERENCE'S RULE: ours is an INCLUSION list, the source's is an EXCLUSION
      list.** Officially both work on reports generally *"(**except in Day Book and GST Returns**)"* — **two named
      exclusions**. Ours fire only on a supports-comparative predicate whose own code comment names the set as
      *"(TB / BS / P&L / Stock Summary)"* — **four kinds**. Row 11.17's *"the comparative map covers 4 of the 45
      kinds"* is **CONFIRMED**, and the source supplies a target that is **not "add more kinds to the map" but
      "invert the map to a two-item exclusion list"**. **A concrete instance proving the inversion matters:** the
      official Statistics report page documents `Alt+N` working **on Statistics** — neither a Day Book nor a GST
      Return, and a report our inclusion list would still exclude even if it existed (it does not — row 11.8).
    - **Absent-vs-attested, now with real targets where before there were none:**
      **11.6 the accounting registers** — the substantive new information is the **shape**: *"you can drill down
      from the selected month to view the voucher-wise listing of sales"*, i.e. **month summary → voucher list**.
      Our Day Book (11.4) is a flat chronological list, **so a register is NOT a filtered Day Book and cannot be
      built by adding a voucher-kind filter to 11.4** — that is the trap this row was sitting on.
      ⚠️ **FAMILY-ROW HONESTY: row 11.6 covers FIVE registers and exactly ONE — Sales — was compared. The other
      four are named by the source as existing; their column sets and whether they share the month-wise shape are
      UNREACHED. This row must not be marked verified on the strength of Sales alone.**
      **11.8 Statistics** — fully specified and now buildable: path, content (*"a snapshot of all the masters
      created and the number of voucher types entered"*), a default **two-section side-by-side columnar** layout
      (*"Types of Vouchers"* / *"Types of Accounts"*), three named `F12` options (*Show Vertical Statistics* /
      *Sort by Default Vouchers* / *Show Voucher Types having entries only*), and `Alt+N` comparing **Quarterly**
      or **across Companies**. ⚠️ **AND IT CARRIES A NUMBER THE INTEGRATOR MUST NOT PASTE BLINDLY: the source says
      the Types-of-Vouchers list shows all *twenty-two* default voucher kinds, while census Area 4 is titled for
      7.2's classic *eighteen*.** Those are two different products' defaults; a Statistics report built to the
      smaller list would be wrong against the source while matching our own Area 4. **This is a scope question for
      the user (U-1), upstream of Area 4, not of Area 11.**
      **12.4 print formats · 12.5 physical printer · 12.6 multi-account and multi-voucher printing** — 12.6's
      target is now `Alt+P > Others`, a report-support list behind **Show More**, a per-voucher-type
      **Multi-Voucher** printer taking a period, and an **alphabetical From/To ledger range** (*"print the ledgers
      as per alphabetical order (0 to 9 and A to Z)"*). **This duplicates T1-14 rather than opening a new defect,
      but the row had NO sourced target at all before today and "build multi-account printing" was therefore
      unbuildable as written.** 🔴 **SEQUENCING THE INTEGRATOR MUST CARRY: 12.4's three format values are
      PRINTER-DRIVER concepts (dot-matrix vs laser) and cannot be meaningfully implemented before 12.5 exists, and
      both sit behind 12.8's residual. The Area-12 build order is 12.8 → 12.5 → 12.4, NOT the row order.**
      **15.1–15.8** — see the Area-15 finding below. **16.1 TallyVault · 16.2 Security Control · 16.3 Edit Log and
      Tally Audit** — all DIVERGE, all with specified targets.
    - 🔴 **TALLYVAULT COLLIDES HEAD-ON WITH OUR STORAGE LAYOUT, AND NOBODY HAD RECORDED IT.** The official
      description is that a TallyVault password *"encrypts your company and all the transaction details,
      **including the company name**."* Our companies are stored in a `.db` **named after the company** — which is
      the very constraint item 9 records as the reason company **rename** is out of scope. **A faithful TallyVault
      therefore cannot be bolted on: the filename itself leaks the plaintext the feature exists to hide.** That
      belongs in row 16.1 before anyone estimates it. **U-9.**
    - **Security Control has a fully specified target:** one company flag (*"Control User Access to Company
      Data"*), **two default security levels named "owner" and "data entry operator"** with the quoted division of
      rights, and administrator-defined levels above them. Against the census's own structural finding that **zero
      of the 182 tables names a user, role or permission**, this is the identity model everything else waits on.

    **🔴 TWO STRUCTURAL FINDINGS THAT CHANGE WHAT THE PLAN ASKS US TO BUILD:**
    - **AREA 15's SPLIT IS *REPORTS vs EVERYTHING ELSE*, NOT *MODULE vs BASE PRODUCT*, AND §3 NOTE 1 IS ONLY HALF
      RIGHT.** The vendor is unambiguous: *"To view **reports** of VAT, Service Tax, and Excise, you need to
      download the **Extension for Tax** installer. … However, keeping those users in mind who are still impacted
      by old stat regime, **the masters and transactions will continue as is in the product.**"* ⇒ **rows 15.1
      (enable / dealer type / TIN / registration date), 15.2 (VAT & Tax Classifications), 15.3 (the rate slabs)
      and 15.4 (VAT Composition) are BASE-PRODUCT fidelity**, while **15.5 and the return-form halves of
      15.6–15.8 are extension capabilities.** **Anyone scoping Area 15 as "one optional module, defer it" would be
      scoping it against a source that says otherwise.** Each of 15.1/15.2/15.3/15.5 (State VAT), 15.6 (a
      dedicated *"What are CST Declaration Forms"* page), 15.7 (Service Tax) and 15.8 (**both** halves — Excise for
      Dealer including **Export Form 2**, and Excise for Manufacturer) is attested by a **current** TallyPrime
      page, so these are live documented capabilities, **not 7.2 archaeology**. **15.4 is UNREACHED** (not
      separately attested in anything retrieved). **15.9 Fringe Benefit Tax is UNREACHED and expected to STAY so**
      — nothing in current vendor documentation mentions it, and it is the one Area-15 row with **no
      reference-product behaviour to clone**; recommended for a user ruling (**U-5**) rather than further research,
      because **a build agent handed this row will invent a feature.** 🔴 **AND NO RATE, SLAB OR THRESHOLD WAS
      VERIFIED**: row 15.3's *"1% / 4% / 12.5% / exempt, ~550 commodity categories"* is **UNREACHED** and, given
      this project's history with seeded statutory rates, **must be web-verified at an official source before any
      figure is written into code** — and repealed State VAT schedules are exactly the kind of number that no
      longer has a live official home. **Flagging that now, at target-definition time, is far cheaper than
      flagging it at seed time.**
    - **EDIT LOG AND TALLY AUDIT ARE TWO DIFFERENT FEATURES AND ROW 16.3 MERGED THEM BEHIND A SLASH.** Detail and
      the corrected row title are at 16.3. The two would be **defended with different sources, falsified by
      different findings and scoped as different amounts of work** — the same reason item 11 keeps its (A)/(B)
      separation. ⚠️ **Ruling 11 builds this next, so it is urgent rather than academic: building it against a row
      with Tally Audit folded into it is how the wrong feature ships under the right name.**

    **🔴 A DEFECT IN THIS PASS'S OWN TASK BRIEF, RECORDED BECAUSE IT CHANGES AN ARITHMETIC CLAIM.** The brief said
    *"§1.3 item 15 was just added for [printing]"*. **It was not.** Item 15 is the **T0-4 / GST rate hierarchy**
    record; the printing item is **item 14**, and item 14 is graded **GROUNDED-AHEAD, not COMPARED** — it sits in
    figure (2), never figure (1). **Anyone acting on the brief's wording would have gone looking for a compared
    printing row, not found one, and been at risk of "correcting" the grade upward**, moving the anchor and
    reddening `tests/Apex.Ledger.Tests/CensusFidelityDerivationTests.cs`. **Item 14 was checked on its own terms
    and is accurate** — two things were tried and could not be falsified: its own arithmetic paragraph forbids
    reading it as progress on figure (1) and its grade token matches that instruction; and its CBIC TLS caveat's
    binding consequence (*"the CLAUSE LETTERING IS UNREACHED"*) holds — `src/` was grepped for Rule-53 clause
    letters and **the discipline held**. **What item 14 does NOT do, and this item now supplies:** item 14 grounds
    the printing chain entirely in **statute** and never compares the print **SUBSYSTEM** — formats, printer,
    copies, ranges, bulk printing. **Rows 12.4, 12.5 and 12.6 were completely ungrounded before today.**

    **Where this item stopped, stated as a boundary rather than buried.** **Area 9 (9 rows) and Area 10 (2 rows)
    were not compared to any source** — depth was spent on areas 11–16, where the absent-row density is highest
    and the official source turned out richest. Two candidates identified but not pursued, recorded so the next
    pass does not re-derive them: **row 9.3 (Job Work registers) is graded COMPLETE on *"existence and
    reachability only — content never compared to Tally"*, making it the highest-value COMPLETE row in these areas
    to attack**, and **row 10.1 (Credit Limits) has a well-documented official page and an unambiguous behavioural
    target (the over-limit block on save), making it probably the cheapest single verified row left.** Within
    area 11, rows **11.5, 11.9–11.14** were not compared to any source and between them hide roughly a dozen
    individual registers under §1.1 rule 2's family compression — **nothing here verifies any of them.** Rows
    **11.7** (Group Summary / Group Vouchers), **13.10** (file/folder chooser), **16.5**, **16.6** and **16.7** are
    **UNREACHED and are declined rather than guessed**; see §6a for what would settle each.

19. **AREAS 1–3 — company configuration, accounting masters and inventory masters, compared against the
    vendor's master pages (wave-3 pass 1, 2026-09-04). 37 rows opened, 23 capabilities compared, 14 DIVERGE,
    9 AGREE. NO ROW STATE MOVED — and the finding is that four EVIDENCE cells were false at HEAD while every
    grade token was right.** `[GRADE: COMPARED]` Written by A14 + A13 on **2026-09-04** under ruling 14,
    folding in `Apex-Review-Artifacts/wave3-areas-1-3.md`. **This item is one pass, not 23 capabilities** —
    the counting unit is the §1.3 ITEM (see U-2); the 23 comparisons are the evidence inside it.

    **Vendor pages, each retrieved as RAW HTML and stripped to text locally, then grepped — not read from a
    WebFetch summary.** That method distinction is load-bearing and the pass recorded why: the first WebFetch
    summary of the Company Creation page returned a **tidied, reordered** field list, and the page's own prose
    carries a different order and, on State-vs-Country, **both orders**. *"A summary that tidies a source is
    exactly how an unsourced claim enters a fidelity record."*
    `help.tallysolutions.com/set-up-company-tally/` · `/tally-prime/accounting/groups-in-tallyprime/` ·
    `/ledgers-in-tallyprime/` · `/tally-prime/set-up-tally-prime/company-features-f11-tally/` ·
    `/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/` · `/voucher-types-tally/` ·
    `/manage-stock-item-tally/` · `/tally-prime/inventory/godown-location/` ·
    `/stock-valuation-methods-tallyprime/` · `/cost-centre-or-profit-centre-tally/` ·
    `/charts-of-accounts-tally/`

    **What AGREES (9) — recorded because an `ABSENT` grade confirmed against a source is a comparison, not a
    non-result.** Rows **1.4** (Company Rename/Delete), **1.7** (F11 Accounting Features group), **2.2**
    (Group behavioural flags), **2.6** (Voucher Class), **2.12** (Multi-master create), **3.6** (Alternate
    units) are `ABSENT` and the vendor page confirms both what the thing IS and that we do not have it;
    **2.3**'s ledger delete guards, **3.5**'s unit field set and decimal cap, and **3.14/3.15**'s *placement*
    half agree with the vendor.

    **Two `⚠️ UNVERIFIED field name` caveats are DISCHARGED, permanently.** §6 item 6's caveat on row **2.2**
    (the four Group behavioural flags) and on row **3.7** (`Allow storage of materials`) both said the Tally
    field names were guesses. **All are vendor-verbatim; the guesses were right.** A **fifth** Group flag,
    *"Does it affect Gross Profits"*, is attested and should join row 2.2. **Retiring a caveat closes a
    question permanently, which is why depth went here rather than into another row saying "looks right".**

    **One claim MOVES OUT of the "ours" column.** `MasterDeletionRules.cs:99-102`'s referential guard was
    recorded as *"ours, corpus silent"*. The vendor's ledger page **attests it verbatim**. It is now sourced.

    **What is OURS or unsettled — kept strictly apart from the sourced column (ruling 9).** Four items, filed
    as labelled divergences in `docs/invented-vs-cloned.md` **IV-56 · IV-57 · IV-58**: the exact Cost Category labels
    *Allocate Revenue Items* / *Allocate Non-Revenue Items* (the vendor attests the **concept**, not the field
    strings); the **placement** of our two BOM switches on the F11 page (the vendor's Inventory list has no BOM
    toggle and puts *Set Components List* on the stock-item F12); our PF / ESI / PT / salary-TDS / gratuity /
    bonus switches as **top-level F11 rows** (the vendor names only *Maintain Payroll* and *Enable Payroll
    Statutory* at that level); and item 9's existing "ours" list, which stands unchanged.

    🔴 **THE FOUR FALSE EVIDENCE CELLS — the 6.20 defect class, four more times, under CORRECT grades.**
    Row **3.4**'s *"there is no bound input for the `StandardCost` value"* is **FALSE at HEAD**
    (`MainWindow.axaml:6612` binds `StandardCostText`, and `StockItemMasterViewModel.cs:441-461` validates it);
    row **2.4**'s *"exactly ONE … is settable anywhere in the UI"* is **FALSE at HEAD** (two on that screen, six
    across two); row **2.12**'s grep targets a **Tally.ERP 9** name the product no longer uses; row **2.5**'s
    target option-set `{Automatic, Manual, None}` is **itself wrong** — the vendor's is `{Automatic, Automatic
    (Manual Override), Manual, Multi-User Auto}`. **Every grade survived; the prose under four of them did
    not.** Corrections applied to §1.2a in this pass.

    ⚠️ **The `Key.K` citation has now drifted three times — 653 → 757 → 835.** Rows 1.2 and item 9 cite it by
    line. **It should be cited as *"the file's only `Key.K` arm"*.** A citation test that checks only that a path
    resolves cannot catch this, and has not, twice.

    **Where this pass stopped, stated as a boundary.** **15 of its 37 rows are UNREACHED** — 1.8, 1.9, 2.9,
    2.10, 2.11, 2.13, 3.2, 3.3, 3.8, 3.9, 3.10, 3.11, 3.12, and the *behaviour* halves of 3.14 and 3.15 — each
    with a named page that would settle it; they are filed in **§6a rows U-M and U-N**. Nothing there is claimed.

20. **AREA 6, THE GST HALF — what a business FILES: GSTR-1, GSTR-3B, e-Way Bill, e-Invoice, GSTR-2B/IMS and
    CMP-08, compared against the vendor pages AND two primary national sources (wave-3 pass 2, 2026-09-04).
    12 capabilities compared. ONE agrees. TEN DIVERGE. One holds on prior grounding and says so.**
    `[GRADE: COMPARED]` Written by A14 + A13 on **2026-09-04**, folding in
    `Apex-Review-Artifacts/wave3-gst-returns.md`.

    **Sources, in ruling-14 order.** (1) `help.tallysolutions.com` GSTR-1 / GSTR-3B / GSTR-2B-IMS / composition
    pages. (2) **Primary:** `https://cbic-gst.gov.in/pdf/cgst-rules-30122017.pdf` (3.9 MB, HTTP 200 — the URL
    this project already records as the working one) for the FORM GSTR-1 **column structure**, extracted with
    **`pdftotext -raw`** because `-layout` scrambles those column headers; and
    `docs.ewaybillgst.gov.in` for the **normative EWB-01 JSON Schema**, retrieved through the **browser pane**
    because it 403s both WebFetch and curl. (3) Internal docs: **not used.**

    🔴 **A SOURCE TRAP FOUND AND AVOIDED, RECORDED SO IT IS NOT WALKED INTO.** The reachable CBIC consolidation
    is dated **30-12-2017**. Its FORM GSTR-1 Table 5 says *"where the invoice value is more than Rs 2.5 lakh"*;
    the vendor's current page says **₹1 lakh**. The threshold moved by later notification, so **the PDF's number
    is superseded text and must never be cited as current** — the same failure mode as the income-tax footnotes
    that quote repealed law. **The PDF is cited ONLY for column structure, never for a threshold or a rate.**
    For the same reason `Rule 88A` returns **0 occurrences** in it (inserted 2019, after this consolidation).

    **The twelve, with verdicts.** **A** GSTR-1 section set (20 vendor sections vs 9 shipped) — DIVERGES.
    **B** Table 12 HSN columns (11 statutory vs 8 shipped) — DIVERGES. **C** Table 8 nil/exempt/non-GST (a
    **4 rows × 3 categories** grid vs one `Money`) — DIVERGES. **D** Table 13 Documents issued — DIVERGES
    (absent). **E** GSTR-1 amendment section→table map — **AGREES, and is now EXTERNALLY grounded** where
    existing item 7 rested on an in-file A14 confirmation. **F** Rule-88A / §49(5) set-off — **HOLDS ON PRIOR
    GROUNDING, NOT RE-VERIFIED**, and the pass says so rather than re-claiming it: Rule 88A postdates the only
    reachable consolidation and no substitute document was accepted. **G** GSTR-3B section set — DIVERGES.
    **H** GSTR-3B on screen — DIVERGES **+ DEFECT**. **I** EWB-01 payload vs NIC v1.03 — DIVERGES **severely**.
    **J** e-Invoice INV-01 — DIVERGES (documented, pinned). **K** GSTR-2B recon + IMS — DIVERGES (4 buckets
    against the vendor's 9). **L** CMP-08 surface and shortcuts — DIVERGES.

    🔴 **THE OPEN `R7 (A14 to confirm)` FLAG IN `EWayBillJson` IS NOW ANSWERED, AND THE ANSWER IS NEGATIVE.**
    `BuildEwb01` emits a payload the NIC EWB-01 v1.03 schema **would reject**: **6 of 17** `required` keys
    absent, **zero** of its 10 `itemList` key names in the schema, the 7 mandatory main-object value fields
    absent, `docDate` in `yyyy-MM-dd` against a `DD/MM/YYYY` pattern, and `transMode`/`transDistance` typed as
    numbers where the schema says string. **This is independent of T0-17.** Filed as **T1-29**.

    **What is OURS by necessity.** Item **J** — the e-Invoice INV-01 divergence is already documented and pinned
    in-tree; it is recorded in `docs/invented-vs-cloned.md` and does not join the sourced column.

    **Where this pass stopped.** **6.10** (portal JSON — nothing to compare; items A–D now supply much of the
    spec whoever builds it will need), **6.12 / 6.13** (GSTR-9 / 9C / 9A — untouched, and they deserve their own
    pass), **6.11**'s GSTR-4 table structure (4A/4B/4C/4D, 5, 6 — **not** compared; only the CMP-08 report
    surface was), **6.1–6.7, 6.17–6.20, 6.22–6.26** (not examined), and **Rule 88A primary text** (declined
    rather than substituted). Filed in **§6a row U-O**.

21. **AREA 11 — the eight report FAMILY rows nobody had ever opened, compared against the vendor's report pages
    (wave-3 pass 3, 2026-09-04). 15 capabilities compared: 14 DIVERGE, 1 is VENDOR SILENT. ALL EIGHT ROW STATES
    CONFIRMED CORRECT — the first pass on this project to test a block of states and move none.**
    `[GRADE: COMPARED]` Written by A14 + A13 on **2026-09-04**, folding in
    `Apex-Review-Artifacts/wave3-reports-families.md`. Rows **11.5, 11.7, 11.9, 11.10, 11.11, 11.12, 11.13,
    11.14** — the rows §1.3 item 18 named in its own closing boundary as *"not compared to any source"*.

    **Vendor pages.** `help.tallysolutions.com/cash-bank-book-tally/` ·
    `/ledgers-and-groups-in-tallyprime/` · `/manage-receivables-outstanding-tally/` ·
    `/cost-centre-or-profit-centre-tally/` · `/tally-prime/accounting/interest-calculation-tally/` ·
    `/budgets-tally/` · `/account-for-forex-gain-or-loss/` · `/track-your-inventory-stock-summary-tally/` ·
    `/movement-analysis-tally/` · `/stock-query-tally/` · `/cash-flow-and-projection-report-tally/` ·
    `/funds-flow-report-tally/` · `/ratio-analysis-tally/`. **One comparison (#14, the exception-report family)
    rests on a Tally.ERP 9 page and SAYS SO in place** — no TallyPrime enumeration of that family was found.

    🔴 **THE SINGLE HIGHEST-LEVERAGE FINDING IN THE AREA, AND IT IS A MISSING PRIMITIVE, NOT A MISSING REPORT.**
    **Ledger Monthly Summary** (`grep -rn "Monthly Summary" src/` → **0**) is the required intermediate level of
    **11.5**, **11.6**, **11.7** and **11.12**, and its absence is also why **11.10** has no Cost Centre Monthly
    Summary. **Build it once and five rows move.** The census currently writes these as five independent gaps.

    **Two reframings that change what "fixing it" means.** (a) Budget Variance has **no dedicated screen** in
    the reference product — it is `Alt+B` **on** Trial Balance / Group Summary; row 11.11 reads its six missing
    gestures as features to add to a dedicated screen, and **fixing the SHAPE delivers five of the six for
    free** while adding them to a dedicated screen would ship a surface the vendor does not have. (b) Rows 11.14
    Cash Flow and Funds Flow both ship the vendor's **drilled** level as their **top** level; *"no drill"* is a
    **consequence** of that, not an independent gap.

    ⚠️ **AN OVERSTATEMENT THE PASS CAUGHT IN ITS OWN DRAFT, RECORDED BECAUSE IT IS THE EXACT FAILURE THIS
    SECTION EXISTS TO PREVENT.** It first wrote *"no Settle Bill from the report"* for row 11.9. **That is
    false — Settle Bills ships**, on `Alt+A` and on a visible button; the vendor's chord is `Alt+B`. It is a
    **chord divergence, not a missing capability**, and `Alt+A` is not free in our product either (Day Book's
    Add Voucher). **A capability wrongly called absent is the same class of error as one wrongly called
    present.** Folded into the chord-map ruling **U-6**, not filed as a features gap.

    **VENDOR SILENT (1).** The **Forex Gain/Loss REPORT SURFACE** — the vendor page describes a Balance-Sheet
    head, not a report. The **accounting AGREES**. Filed as `docs/invented-vs-cloned.md` **IV-59**; it can
    never join the compared set on the pages read.

    **Where this pass stopped, named row by row.** 11.9's Payables screen and the Bills Receivable/Payable print
    layouts; 11.10's **Group Break-up** (three vendor URLs returned navigation indexes, not article bodies) and
    Cost Centre Class; 11.11's interest slabs/grace periods and the forex settlement path; **11.12's large
    remainder — twelve-plus members untouched, and this row is *the* place a family-row grade would lie**;
    11.13's column layouts; 11.14's Cash Flow Projection columns and the Funds Flow `Ctrl+B` Scale Factor.
    `…/inventory-reports/stock-summary-tally/` **404s**. Filed in **§6a row U-P**.

22. **AREAS 12 AND 13 — printing and data management, compared against the vendor's print / export / import /
    e-mail / backup pages (wave-3 pass 4, 2026-09-04). 19 rows examined, 17 capabilities compared. Two rows
    (12.6, 12.9) were read and are recorded as *corroborated, nothing new* rather than counted — that
    self-denial is the reason the count is 17 and not 19.** `[GRADE: COMPARED]` Written by A14 + A13 on
    **2026-09-04**, folding in `Apex-Review-Artifacts/wave3-printing-data.md`.

    **Vendor pages.** `help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/` ·
    `/configure-for-print-export-share/` · `/print-invoices-reports/` · `/print-documents/` ·
    `/backup-restore-company-data-tally/` · `/export-data-in-tally/` ·
    `/tally-prime/data-exchange-tally-prime/import-data-in-tally/` · `/e-mailing-in-tallyprime/` ·
    `/tally-prime/data-management/migrate-company-data-tally/` · `/manage-your-company-data-tally/` ·
    `/tallyvault-for-company-tally/`.
    ⚠️ **A RETRIEVAL TRAP RECORDED IN PLACE:** searching for delivery-challan print configuration returns pages
    under `/article/Tally.ERP9/…` and `/docs/te9rel…/` — **a different product**, and exactly the species of page
    that put *"Condensed"* into row 12.4's target list. **Every quotation in this pass is from a TallyPrime page**;
    where only an ERP-9 page exists the pass says so and does **not** quote it as a target.

    🔴 **THE CROSS-CUTTING FINDING, AND IT REORGANISES THE PLAN FOR TWO WHOLE AREAS.** The reference product has
    **three top-level output menus** and **we are 0-for-3**: `Alt+P` (print menu) **unbound**; `Alt+E` (export
    menu) **bound to the wrong function** — it fires the *current-object* export, the job of `Ctrl+E`, which is
    therefore inert; `Alt+M` (Share menu) **unbound** (`grep -rn "Key\.M\b" src/Apex.Desktop` returns exactly one
    line and it excludes Alt). **Nine census rows — 12.3, 12.4, 12.5, 12.6, 12.7, 13.3, 13.5, 13.6, 13.7 — are
    currently written as nine independent builds. They are one shared menu shell + one shared `Others` report
    list + per-leaf configuration.** The wave-2 build order 12.8 → 12.5 → 12.4 is correct and **gains a
    predecessor**: the menu shell is upstream of all of it. Filed as **T2-20**.

    🔴 **TWO `COMPLETE` ROWS WHOSE VENDOR CAPABILITY IS A DIFFERENT SHAPE — AND NEITHER GRADE MOVES.** **13.1**
    (Backup) is complete **for the open company**; the vendor's screen **lists companies**, offers **All Items**
    multi-company backup, and persists a **Company Backup Path** (ours defaults to Documents every session).
    **13.9** (migration) is complete **as an automatic-on-open side effect**; the vendor's is an operator-run,
    **pausable and resumable** menu action with a **pre-migration check**, a **Migrate Configuration** screen, a
    **Migration Summary**, and a **retained pre-migration copy** — we keep no such copy on the migration path
    (a `.apex-prerestore` copy exists only for *restore*). §1.2 grades existence and reachability, so both stay
    `COMPLETE` — **but "Backup — COMPLETE" and "data migration — COMPLETE" read as parity and are not.** Row
    12.9 already models the right wording; both cells now carry the same scope sentence.

    **13.10 IS SETTLED, AND IT WAS DECLINED AS UNREACHED BY ITEM 18.** Item 18's closing paragraph lists 13.10
    among rows *"UNREACHED and … declined rather than guessed"*. **The vendor documentation answers it**, and
    the row is moved out of §6a into the compared set. 🔴 **This is the shape §6a promises and had not yet
    delivered: an unreached row RE-OPENED the moment its source became retrievable.**

    **VENDOR SILENT (1).** **12.2's residual** — the T0-9 IRN / signed-QR **rendering-eligibility** question is
    all that is left of the row, and the vendor is **silent** on it. Recorded as vendor-silent, **not** as
    unreached, and filed as `docs/invented-vs-cloned.md` **IV-60**. Also recorded as OURS and defensible: our
    backup file naming (`Name[_yyyyMMdd-HHmm].apexbak` against the vendor's `TBK1800_******.***` plus an
    overwrite prompt) — **invented, not a defect** — filed as **IV-61**.

    🔴 **A CENSUS-COMPLETENESS OBSERVATION THE PASS OFFERED RATHER THAN INSERTED, AND IT IS ACCEPTED HERE.** The
    vendor's `Alt+Y` Data estate is **Backup & Restore · Import · Migrate · Synchronise · Repair · Export ·
    Split · Extract/Share**. The census has rows for Repair (16.6), Split (16.5) and Group Company (16.7) and
    excludes Synchronisation by architecture — but there is **no row for Migrate-as-an-operator-action** and none
    for **Extract/Share (ODBC / FTP / Pivot)**. **13.9 is the closest thing to a Migrate row and it describes a
    different mechanism.** ⚠️ **This would ADD to the 216 denominator, so it is a user ruling, not an edit** —
    filed as **U-11**. **No row was added and no digit moved.**

    **Where this pass stopped.** 12.7's delivery-challan **print configuration** (only ERP-9 pages exist for it,
    and they are not quoted as targets); the backup archive internals; and the running app was not driven.

23. **AREAS 9 AND 10 — post-7.2 inventory / manufacturing / job work, and the two accounting-features rows.
    The FIRST comparison these eleven rows have ever had. 11 examined, 11 compared, 11 cited. ONE ROW MOVES
    DOWN ON MEASUREMENT (9.3 `COMPLETE` → `PARTIAL`) AND ONE ROW'S PREMISE IS NOT A TALLYPRIME FACT AT ALL.**
    `[GRADE: COMPARED]` Written by A14 + A13 on **2026-09-04**, folding in
    `Apex-Review-Artifacts/wave3-areas-9-10.md`. Wave 2 said in terms: *"Areas 9 and 10 — NOT COMPARED AT ALL."*

    **Vendor pages.** `help.tallysolutions.com/tally-prime/job-work/job-work-tally/` ·
    `/tally-prime/job-work/masters-for-job-work-tally/` ·
    `/tally-prime/job-work/principal-manufacturer-job-work-out-tally/` ·
    `/tally-prime/job-work/job-worker-job-work-in-tally/` · `/inventory-entry-tally/` ·
    `/tally-prime/accounting/voucher-types-tally/` · `/manage-inventory-in-manufacturing-tally/` ·
    `/point-of-sale-tally/` · `/tally-prime/inventory/job-costing-tally/` ·
    `/tally-prime/inventory/track-item-cost-tally/` · `/sales-order-tally/` ·
    `/manage-receivables-outstanding-tally/` ·
    `/set-up-tallyprime-auto-login-language-multiple-addresses/` ·
    `/tally-prime/set-up-tally-prime/company-features-f11-tally/`.

    🔴 **ROW 9.3 `COMPLETE` → `PARTIAL`, AND THE PROOF IS A COUNT, NOT AN OPINION.** We ship **4** Job Work
    reports; the vendor documents **11**. Zero-hit greps over all of `src/` for each of *Job Work Orders
    Summary*, *Components Order Summary*, *Material Movement Register*, *Stock With Job Worker*, *Stock from
    Party*, *Issue Variance*, *Receipt Variance*. **4 of 11 is `PARTIAL`.** The substantive loss is the
    **Material Movement Register** — the statutory dispatch/receipt reconciliation carrying Shortages,
    Wastage/Scrap and Duty Paid — and it is the **only** Job Work register for which the vendor publishes a
    column list. **This is the sixth census evidence/state cell caught wrong on this project (after 16.6, 5.1,
    12.8, 16.3, 16.4), and the first this year to move a state DOWNWARD.**

    🔴 **ROW 9.9 MEASURES US AGAINST THE WRONG PRODUCT.** *"Transfer Journal as a named voucher kind"* is a
    **Tally.ERP 9** artefact. TallyPrime's inventory-voucher page lists nine kinds and does not include it; the
    mechanism is a **Stock Journal Voucher Class** (*"Use Class for Inter-Godown Transfers"*). **The state stays
    `ABSENT`** — we ship neither — but the row is re-titled to name the real gap, **Voucher Classes**, which it
    shares with 9.6. **A row that names the wrong target cannot be closed by building the right thing.**

    **What AGREES.** The **Manufacturing Journal** BoM-driven production voucher agrees (one gating divergence);
    the **POS engine** agrees; and the **Job Work order-book pending arithmetic** agrees with the vendor's
    Balance Quantity — the first substantive AGREE inside area 9.

    🔴 **10.1 AND 10.2 ARE NOT THE SIMPLE ADDITIONS THEIR `ABSENT` CELLS IMPLY.** **10.1 Credit Limits** is
    **not one boolean**: the vendor has a per-ledger enable, the amount, a **post-dated override** that changes
    *which transactions count*, a save-time **error naming both the limit and the excess**, and a **Multi Ledger
    Limit Alteration** screen; it applies to **Sundry Creditors as well as Debtors**, so it is a payables control
    too. A naive "add a decimal column" slice would ship the wrong rule. **10.2 Multi Address** carries a
    **GST-correctness dependency the cell does not record**: the vendor's per-address block holds **Statutory &
    Taxation Information — a GSTIN per address** — and a voucher picks **Bill to** and **Ship to**
    *independently*. Place of supply for goods follows **ship-to**, so **a party billed in one state and shipped
    to another cannot be represented in our product at all**, which is exactly the case that decides CGST+SGST
    versus IGST. **Read alongside T0-18 / T0-19 / T0-20.** Filed as **T1-30**.

    **Where this pass stopped, stated as a boundary rather than buried.** It did **not** compare the four shipped
    registers' column sets line by line — the vendor describes those books by **behaviour**, not by a fixed
    column list, so *"anyone claiming a full column-level comparison of these four registers would be
    overstating"*. It did **not** open the POS receipt format against the vendor's POS print options (that is
    area 12, item 22's territory). It did **not** verify the F11 *Show More / Show All Features* progressive
    disclosure, which places *Enable Job Order Processing* and *Enable Cost Tracking* behind **Show All** while
    our F11 panel shows every row at once — **that is a row 1.6 concern and is left there rather than
    double-counted here.** **Nothing in this scope is unreached.**

**12 of 216 capabilities have had their SHIPPED behaviour compared to a source — the ninth is PARTIAL, with its unsourced half enumerated rather than glossed; the tenth and eleventh became shipped-and-compared when S3 and S4 landed; and the twelfth became shipped-and-compared on 2026-08-20, when S5a–S5e's step-5a record was written into item 12 above. ~~NO ITEM HEADER WAS ADDED, so the GROUNDED count stays at 12 — what changed is that the last grounded-but-unbuilt header is now built and compared. That leaves 204 uncompared as shipped behaviour, and 204 with no sourced verification of any kind.~~** 🔴 **AMENDED LATER THE SAME DAY (2026-08-20), BY THE T0-11 SLICE-S0 PASS, AND THE STRUCK SENTENCE IS WHY THE AMENDMENT IS NOT A CONTRADICTION.** A header WAS added afterwards — **item 14**, graded `[GRADE: GROUNDED-AHEAD]` (its header reads *"GROUNDED; PARTLY BUILT"*) — so **the grounded count is 13, and figures (3) and (4) SEPARATE AGAIN at 204 and 203.** The struck sentence predicted exactly this: *"if a later slice grounds a capability ahead of building it, they separate again."* **Figure (1) did not move**; nothing new was compared. Every "COMPLETE" in §1.2 means *present and reachable*, not *correct*. A previous sweep on this project reported CANNOT TELL 256 and the 256 was the honest part; the equivalent honest number here is ~~**204**~~ **203**. 🔴 **AMENDED AGAIN 2026-09-03 — THE OPENING COUNT OF THIS PARAGRAPH IS SUPERSEDED AND IS LEFT STANDING ONLY AS THE RECORD OF WHAT 2026-08-20 FOUND.** It opens ~~*"12 of 216 capabilities have had their SHIPPED behaviour compared to a source"*~~; **it is 13**, because **item 15** (the T0-4 rate hierarchy) was added and graded `[GRADE: COMPARED]`. The authoritative statement is the four-figure block immediately below — ~~**13 · 14 · 203 · 202**~~ — and `tests/Apex.Ledger.Tests/CensusFidelityDerivationTests.cs` re-derives it from the grade tokens on every run, so this paragraph can never again be the thing a reader quotes. 🔴 **AMENDED AGAIN 2026-09-04 — AND THE DIGITS IN THE PRECEDING SENTENCE ARE NOW STRUCK RATHER THAN RE-TYPED, WHICH IS THE POINT.** Items **16**, **17** and **18** were added, folding in three read-only wave-2 verification passes, and all three are graded `[GRADE: COMPARED]`. **It is 16, and the four-figure block below is 16 · 17 · 200 · 199.** 🔴 **AND THE SENTENCE THIS PARAGRAPH OPENS WITH IS NOW WRONG TWICE OVER AND IS KEPT ONLY AS THE 2026-08-20 RECORD:** the opening count is superseded, and so is *"the ninth is PARTIAL"* as a description of the whole — **items 9, 12, 15, 16, 17 and 18 are ALL "compared" in the PARTIAL sense**, i.e. compared with the unsourced half enumerated rather than glossed. **That is now the normal shape of a compared item on this project, not the exception it was when this paragraph was written.** 🔴 **AMENDED A THIRD TIME, LATER ON 2026-09-04, BY THE WAVE-3 FOLD-IN — AND EVERY DIGIT IN THIS PARAGRAPH IS NOW SUPERSEDED TWICE OVER.** Items **19, 20, 21, 22** and **23** were added, folding in five read-only wave-3 verification passes, and all five are graded `[GRADE: COMPARED]`. **It is 21, and the four-figure block below is 21 · 22 · 195 · 194.** **This paragraph is kept ONLY as the 2026-08-20 record; do not quote any figure from it.** The authoritative statement is the four-figure block, and `tests/Apex.Ledger.Tests/CensusFidelityDerivationTests.cs` re-derives it from the grade tokens on every run.

> **▶ 🔴 THE PREVIOUS SENTENCE, QUOTED SO THE MOVE IS CHECKABLE (2026-08-19 → 2026-08-20):** ~~*"11 of 216
> capabilities have had their SHIPPED behaviour compared to a source … Item 12 alone is still grounded ahead of
> the slices that build it, which keeps the GROUNDED count at 12, leaves 205 uncompared as shipped behaviour,
> and leaves 204 with no sourced verification of any kind."*~~ **Only figure (1) moved, by one, and figure (3)
> follows it.** ⚠️ **AND THE TWO HALVES NOW COINCIDE AT 204 — that is arithmetic, not a mistake.** Figure (3)
> is `216 − shipped-and-compared` and figure (4) is `216 − grounded`; item 12 was the ONLY header that was
> grounded and not compared, so closing it collapses the two. **If a later slice grounds a capability ahead of
> building it, they separate again.** ⚠️ **Item 12 is compared in the sense item 9 is: PARTIAL, with its
> unsourced half enumerated** — categories (A)–(D) there name what is attested, what diverges and what is ours,
> and (D) is the half no source can settle.

> **▶ 🔴 THESE FOUR FIGURES ARE MAINTAINED HERE AND NOWHERE ELSE. §1.3 IS THE SINGLE DERIVATION.**
> **As of 2026-09-04 (the WAVE-3 fold-in, later the same day than the wave-2 one below), against §1.2's 216 denominator:
> 21 shipped-and-compared · 22 grounded · 195 uncompared as shipped · 194 with no sourced verification of any
> kind.**
> **▶ 🔴 MOVED 2026-09-04 BY FIVE — LARGER AGAIN THAN THE MOVE OF THREE RECORDED BELOW, AND THE SAME DISCIPLINE
> APPLIES WITH MORE FORCE, NOT LESS.** The earlier 2026-09-04 block read ~~*"16 shipped-and-compared · 17 grounded · 200 uncompared as shipped · 199 with no sourced verification of any kind"*~~ *(kept on one line deliberately: the derivation test strips `~~…~~` LINE BY LINE, so a struck quotation wrapped across two lines is read as a LIVE statement of the figures)*. **Five items were added — 19, 20, 21, 22 and 23 — each graded
> `COMPARED`, each folding in one of five read-only wave-3 verification passes that between them examined
> **98 census rows** and compared **78 capabilities** against cited sources.** **(1)** goes 16 → 21, **(2)**
> 17 → 22, **(3)** 200 → 195 and **(4)** 199 → 194. **(3) and (4) stay separated by exactly one**, which is
> item 14 — still the only grounded-but-not-compared header.
> 🔴 **THE COUNTING UNIT IS UNCHANGED AND THE TEMPTATION WAS LARGER THIS TIME, NOT SMALLER.** The five passes
> compared **23 + 12 + 15 + 17 + 11 = 78** capabilities across roughly **98 distinct §1.2a rows**. **Counting
> the 78, or the 98, would have put figure (1) above 90 and made this document's one honest number dishonest
> in a single edit.** The precedent set on 2026-09-04 by the wave-2 fold-in — **one item per pass** — is
> followed exactly. Four of the five passes recommended or assumed that unit; the fifth (item 23) stated it
> explicitly and said *"I follow that precedent and do not invent a new one."* **If the user rules the unit
> differently (U-2), the rule changes HERE and every item is re-counted under it — no digit is edited.**
> 🔴 **AND THE HONEST SHAPE OF THIS MOVE, WHICH IS NOT PROGRESS.** Of the 78 capabilities compared, **63
> DIVERGE**, 13 AGREE and 2 are VENDOR SILENT. **One row moved DOWN** (9.3 `COMPLETE` → `PARTIAL`), one row's
> stated target was found to be the wrong product's feature (9.9), **four evidence cells were false at HEAD**
> under correct grades, and **21 new defects and divergence rows** were filed into §2. **Figure (1) counts
> comparison, never completeness.** Adding five items makes the honest number honest about **78 more
> capabilities, most of which we do not match.** It does not make the product five items better.
> ⚠️ **AND WHAT DID NOT MOVE:** §1.2's states moved by exactly one row on this fold-in (`C=47 → 46`,
> `P=101 → 102`) and **that move is unrelated to this one**. §1.2a measures existence; §1.3 measures
> comparison.
> **▶ 🔴 MOVED 2026-09-04 BY THREE — THE LARGEST SINGLE MOVE THIS FIGURE HAD MADE UP TO THAT POINT — AND EVERY WORD OF WHY IS
> BELOW, BECAUSE A JUMP THIS SIZE IS EXACTLY WHAT AN INFLATED NUMBER LOOKS LIKE.** The 2026-09-03 block read
> ~~*"13 shipped-and-compared · 14 grounded · 203 uncompared as shipped · 202 with no sourced verification of any kind"*~~ *(kept on one line deliberately: the derivation test strips `~~…~~` LINE BY LINE, so a struck quotation wrapped across two lines is read as a LIVE statement of the figures — a trap the older blocks below are only saved from by their position)*. **Three items were added — 16, 17 and 18 — each graded `COMPARED`, each folding in one of three
> read-only wave-2 verification passes that between them opened and compared 80 capabilities.** **(1)** goes
> 13 → 16, **(2)** 14 → 17, **(3)** 203 → 200 and **(4)** 202 → 199. **(3) and (4) stay separated by exactly
> one**, which is item 14 — still the only grounded-but-not-compared header.
> 🔴 **THE COUNTING UNIT IS THE ITEM, AND THIS IS THE ONE PLACE THE TEMPTATION TO INFLATE WAS REAL.** The three
> passes compared **24 + 31 + 25 = 80** capabilities across roughly **40 distinct §1.2a rows**. **Counting those
> 80, or those 40, would have overstated this figure against every previous entry in this section**, all of which
> are counted as one item each regardless of how many rows they span (items 1, 3 and 4 each span many; items 1
> and 5 sit *below* a row). **So the move is +3, one per pass, and the 80 comparisons are recorded as the
> evidence INSIDE those three items.** Two of the three passes explicitly declined to propose a number and said
> the integrator must choose the unit; the third recommended exactly one item for its own pass. **This is that
> choice, made in the open. If the user rules the unit differently, the rule changes HERE and every item is
> re-counted under it — no digit is edited.** *(See the user-ruling block, **U-2**.)*
> 🔴 **AND THE HONEST QUALIFIER ON ALL THREE, WHICH IS LARGER THAN ITEM 15'S WAS: THE CORPUS WAS EMPTY.**
> `tally/` held **zero PDFs** on 2026-09-04 — measured independently by three agents, in the base tree and in all
> seven worktrees — so **not one row in items 16, 17 or 18 is corpus-verified.** They rest on **official vendor
> documentation** (`help.tallysolutions.com`) and on **primary legal instruments** (the Finance Act 2025 First
> Schedule, the bare Income-tax Act sections read at their Year-2025 slugs, EPFO and ESIC). Under ruling 9 that is
> enough to be `COMPARED` — items **1**, **3**, **5** and **15** rest on official vendor/authority pages in
> exactly the same way and item 15 says so in terms — **but it is a WEAKER evidentiary base than a corpus page for
> product-behaviour questions, and any of these rows can be overturned or strengthened ~~when the PDFs return.~~
> ~~**Restoring `tally/` is user ruling U-0 and it is the highest-value unblocking action in the project.**~~
> 🔴 **SUPERSEDED 2026-09-04 BY USER RULING 14 — AND THIS SENTENCE IS THE EXACT TRAP THE RULING EXISTS TO
> CLOSE. THE PDFs ARE NOT RETURNING: `tally/` was never git-tracked, so nothing can restore it and U-0 is
> CLOSED, not pending.** Do not treat these rows as provisional pending a corpus check that can never happen.
> **Under ruling 14 the vendor documentation IS the ground truth**, so items 16, 17 and 18 rest on the
> project's primary source rather than on a substitute for it, and the *"weaker evidentiary base"* framing
> above is now the wrong comparison — the right one is vendor page versus **no source at all**. They can
> still be overturned, but only by a **better reading of an admissible source** (see §1.3's METHOD NOTE).
> ⚠️ **AND WHAT DID NOT MOVE, STATED BECAUSE THE TWO ARE CONSTANTLY CONFUSED:** §1.2's states moved on the same
> day (`P=98 → 101`, `A=71 → 68`) and **that move is unrelated to this one.** §1.2a measures existence; §1.3
> measures comparison. Rows 16.3 and 16.4 became `PARTIAL` **and** were compared to a source that says they
> **DIVERGE**. **Figure (1) counts comparison, never completeness.**
> **▶ 🔴 MOVED 2026-09-03 BY ONE, AND THE OTHER THREE FOLLOW IT.** The 2026-08-20 block read
> ~~*"12 shipped-and-compared · 13 grounded · 204 uncompared as shipped · 203 with no sourced verification of
> any kind"*~~. **Item 15 was added** — the T0-4 rate-hierarchy fidelity record, graded `[GRADE: COMPARED]`
> because the shipped resolution order was compared, term for term, against the vendor's two published order
> strings via an oracle table that computes the expectations rather than reading them off the resolver. **(1)
> goes 12 → 13, (2) 13 → 14, (3) 204 → 203 and (4) 203 → 202.** **(3) and (4) stay separated by exactly one**,
> which is item 14 — still the only grounded-but-not-compared header. ⚠️ **Item 15 is compared in the sense
> items 9 and 12 are: PARTIAL, with its unsourced half enumerated** — its ruling-9 categories (a) and (b) name
> what is vendor-attested, what is ours and what is a deliberate narrowing, and its two OPEN R12 questions are
> stated with measured figures rather than resolved. ⚠️ **AND NOTE WHAT DID NOT MOVE:** the capability itself
> is `PARTIAL` in §1.2a, not `COMPLETE`; row **3.13** is still `ABSENT`. **Figure (1) counts comparison, never
> completeness** — that separation is the point of maintaining the two tables apart, and this is the first item
> where they visibly disagree.
> 🔴 **AND THE HONEST QUALIFIER ON THIS ONE MOVE:** item 15's corpus measurements are **relayed** from the
> 2026-08-20 design pass, because the git-ignored `tally/` corpus was **not on disk** when the item was
> written. The vendor page was **not re-fetched** either. The comparison that earns the grade is against the
> vendor strings **as transcribed into the oracle**, which is checkable in-tree; the corpus-silence claim
> beneath it is not re-run here and says so.
> **▶ 🔴 MOVED TWICE ON 2026-08-20, IN OPPOSITE HALVES OF THE BLOCK, AND BOTH MOVES ARE RECORDED.** The first
> pass (S5a–S5e's step-5a record) moved **(1)** from 11 to 12 and read ~~*"12 shipped-and-compared · 12
> grounded · 204 uncompared as shipped · 204 with no sourced verification of any kind"*~~. The second pass —
> this one — **added item 14** (the T0-11 chain's fidelity record, graded `[GRADE: GROUNDED-AHEAD]`),
> which moves
> **(2)** from 12 to 13 and **(4)** from 204 to 203. **(1) and (3) did NOT move: nothing new was compared, and
> a grounded-ahead header is not a shipped one.** **(3) and (4) therefore separate again**, exactly as the
> first pass's own note said they would the moment a capability was grounded ahead of being built.
> **▶ 🔴 MOVED 2026-08-20 BY ONE, AND ONLY BY ONE.** The 2026-08-19 block read ~~*"11 shipped-and-compared · 12
> grounded · 205 uncompared as shipped · 204 with no sourced verification of any kind"*~~. Item **12**
> (voucher alteration) became shipped-and-compared when S5a–S5e's step-5a record was written into it; **no item
> header was added or removed**, so (2) is unchanged and (3) follows (1). **(3) and (4) now coincide** — see
> the note under the derivation sentence above for why that is arithmetic rather than a slip.
>
> **▶ 🔴 RESTATED 2026-08-19 AGAINST 216 — AND NOTE WHICH TWO MOVED AND WHICH TWO DID NOT.** User ruling 10
> brought both held-out sets into scope, so the denominator went **200 → 216** (§1.2). **(1) and (2) did NOT
> move**: 11 and 12 are counts of the item headers below, and no item was added, so nothing about what has
> been compared changed. **(3) and (4) moved by exactly 16**, because they are the denominator minus (1).
> **The gap between what is built and what is verified widened by sixteen capabilities the same day the goal
> became verifying all of them** — that is the honest shape of ruling 9, and it is written here rather than
> smoothed over.
>
> **▶ 🔴 THE FLOOR RULING 9 PUT UNDER THIS BLOCK, AND IT IS NOT ZERO. `216 − 11 = 205` IS NOT A BACKLOG THAT
> CAN BE FULLY CLEARED.** Ruling 9 (R12, 2026-08-19 — `plan.md` §5) makes **done = full parity AND corpus
> verification**. The user accepted an explicit limit when choosing it: **the corpus is SILENT on some
> behaviour entirely**, and those capabilities **cannot be verified by anyone, ever, from the sources this
> project admits**. They ship as a **documented divergence labelled as OURS** and are **never counted toward
> (1)**. **Nobody has measured how many rows sit under that floor** — that measurement does not exist, and it
> is not invented here. **▶ AND THE TWO R7 CATEGORIES STAY STRICTLY APART IN EVERY ROW BELOW:** *"corpus
> silent, ours by design"* is a **different claim** from *"the corpus attests X and we deliberately ship a
> narrower Y"*. Item 11's two rulings are the worked example of why — see D-6 in `plan.md` §5, where a record
> resting on *"NOT ATTESTED"* was false because the attestation existed and was merely poor.
>
> **▶ 🔴 HOW THESE FOUR ARE DERIVED — READ THIS BEFORE QUOTING ANY OF THEM. THE DERIVATION IS A PROPERTY OF THE
> ROWS BELOW, NOT AN EVENT ANYWHERE ELSE, AND THAT IS DELIBERATE.**
> **▶ 🔴 THESE FOUR BULLETS WERE THEMSELVES STALE UNTIL 2026-08-20 (second pass), AND THAT IS WORTH ONE
> SENTENCE BECAUSE OF WHAT THIS BLOCK CLAIMS ABOUT ITSELF.** The block says the derivation *"is a property of
> the rows below"* and *"re-count the headers; never carry a digit forward"* — yet when item 12 became BUILT on
> the first pass of 2026-08-20, the **stated figures above were updated and these bullets were not**, so the
> derivation read ~~*"items 1–11 → 11"*~~ and ~~*"item 12 alone → 11 + 1 = 12"*~~ directly beneath a block
> asserting 12 · 12. **The digits were right and the derivation that is supposed to produce them was wrong** —
> the same class of defect as a green suite proving nothing. Re-derived below by actually re-counting.
> **▶ 🔴 AND SINCE 2026-08-21 THE DERIVATION IS A COMMAND, NOT A DESCRIPTION — T0-11 review C19/L3-05.**
> Every numbered item below carries **exactly one** machine-readable grade inside its header:
> `[GRADE: COMPARED]` (built **and** compared to a source), `[GRADE: GROUNDED-AHEAD]` (grounded from a source,
> **not** yet compared as shipped) or `[GRADE: METHOD-NOTE]` (not a capability row — item 13 alone).
> **THE COUNTING COMMAND, in the shape §1.2a has carried since 2026-08-18 — re-run it, never re-read it:**
>
> ```
> awk '/^### 1\.3 /{s=1} s&&/^## 2\./{exit} s&&/^[0-9]+\. /{h=1} s&&/^$/{h=0} \
>      s&&h&&match($0,/\[GRADE: [A-Z-]+\]/){print substr($0,RSTART,RLENGTH)}' \
>   docs/full-clone-census.md | sort | uniq -c
> ```
>
> **Its literal output, re-run 2026-09-04 (wave-3 fold-in) — twenty-three tokens for twenty-three numbered items:**
>
> ```
>      21 [GRADE: COMPARED]
>       1 [GRADE: GROUNDED-AHEAD]
>       1 [GRADE: METHOD-NOTE]
> ```
>
> *(Superseded — the earlier 2026-09-04 run, before items 19–23 existed, was* ~~`16 [GRADE: COMPARED]` / `1
> [GRADE: GROUNDED-AHEAD]` / `1 [GRADE: METHOD-NOTE]`~~*; the 2026-09-03 run, before items 16, 17 and 18
> existed, was* ~~`13 [GRADE: COMPARED]` / `1
> [GRADE: GROUNDED-AHEAD]` / `1 [GRADE: METHOD-NOTE]`~~*; the 2026-08-21 run, before item 15 existed, was*
> ~~`12 [GRADE: COMPARED]` / `1 [GRADE: GROUNDED-AHEAD]` / `1 [GRADE: METHOD-NOTE]`~~*. Only the COMPARED token
> has ever moved.)*
>
> `tests/Apex.Ledger.Tests/CensusFidelityDerivationTests.cs` is that command in C#, and it additionally
> asserts that the four bullets below reproduce the four figures above against §1.2's denominator — so a
> grade that moves without the block being re-derived is now RED rather than silent.
> **⚠️ WHY THE OLD RULES WERE REPLACED RATHER THAN RE-WORDED: they could not be run at all.** Bullet 1 read
> *"whose own header records the surface as BUILT / shipped"*, which matches **neither** items 1–8 (one-line
> entries naming a source, carrying no grade word) **nor** item 9 (*"PARTIAL"*), and **does** match item 14
> (*"PARTLY BUILT — the PURCHASE ITEM-INVOICE half shipped"*), which it must not. Bullet 2 counted the literal
> **GROUNDED, NOT YET BUILT**, which **no live item has ever carried**.
> Item 14 was born *"GROUNDED; PARTLY BUILT"* in the very commit (`96db1c0`) that wrote the bullet, and the
> only §1.3 header text containing the bullet's literal is item 12's **struck-out** quotation of its own
> superseded grade — an item already inside figure (1). So a reader obeying *"re-count the headers; never
> carry a digit forward"* re-derived
> **grounded = 12** and **(4) = 204** against the stated 13 and 203. **The digits were right and the derivation
> that is supposed to produce them was wrong — for the second time in two days, in the block that had just
> diagnosed exactly that failure in itself.** Re-derived below by re-running the command, not by editing a digit.
> 1. **shipped-and-compared** = the number of numbered items in this section graded `[GRADE: COMPARED]`.
>    Re-count them: items **1–12**, **15**, **16**, **17**, **18**, **19**, **20**, **21**, **22** and **23**.
>    → **21**. *(Items 19–23 joined on 2026-09-04 with the wave-3 fold-in; items 16, 17 and 18 earlier the same
>    day; item 15 on 2026-09-03; item 12 on 2026-08-20; the superseded counts were* ~~*items 1–12, 15–18 → 16*~~*,*
>    ~~*items 1–12 and 15 → 13*~~*,* ~~*items 1–12 → 12*~~ *and* ~~*items 1–11 → 11*~~*. Note the run
>    is not a contiguous range any more — items 13 and 14 sit inside it and are graded otherwise, which is
>    exactly why the rule counts TOKENS and not spans.)*
> 2. **grounded** = that number, plus the items graded `[GRADE: GROUNDED-AHEAD]`. Today that is
>    item **14** alone. → **21 + 1 = 22**. *(Superseded:* ~~*16 + 1 = 17*~~*,* ~~*13 + 1 = 14*~~*,* ~~*12 + 1 = 13*~~
>    *and* ~~*item 12 alone → 11 + 1 = 12*~~*.)*
> 3. **uncompared as shipped** = §1.2's denominator minus (1). → **216 − 21 = 195**. *(Was `216 − 16 = 200`
>    earlier on 2026-09-04, `216 − 13 = 203` before that, `216 − 12 = 204` until 2026-09-03, `216 − 11 = 205`
>    until 2026-08-20, and `200 − 11 = 189` until 2026-08-19; the denominator moved, the derivation did not.)*
> 4. **no sourced verification of any kind** = (3) minus the grounded-ahead items, i.e. (2) − (1). →
>    **195 − 1 = 194**. *(Was `200 − 1 = 199`; `203 − 1 = 202`; `204 − 1 = 203`; `205 − 1 = 204`; and
>    `189 − 1 = 188` until 2026-08-19.)*
> ⚠️ **AND THE TRAP THESE BULLETS HAVE NEVER NAMED, WHICH IS WHY A RE-COUNTER GETS IT WRONG: item 13 is a
> METHOD NOTE, not a capability row.** Its own header says so. It is counted in **neither** (1) nor (2), and
> the old *"items 1–11"* phrasing dodged it only by accident. **Numbered ≠ capability — read each header.**
>
> **▶ 🔴 THE CONDITION THAT USED TO SIT HERE IS DELETED, AND WHY IT WAS DELETED IS THE POINT.** This block
> previously read *"the shipped-and-compared figure stays at 9 **until S3 / S4 / S5c land**"*. **S3 and S4
> landed and nobody re-derived it**, so the block asserted 9/12/106/103 while its own rows already said item 10
> was *"BUILT — shipped in slice S3"* and item 11 *"BUILT — shipped in slice S4"* — a document contradicting
> itself in one section, under a rule that says this block wins. **A named external event must never appear in
> this derivation again.** The four figures now depend only on what the item headers below say, which is the
> one thing that cannot fall out of step with them: **re-count the headers; never carry a digit forward.**
>
> **▶ WHAT THE OLD FIGURES WERE, so a copy elsewhere can be identified rather than half-believed.** The
> **2026-08-17** block read **9 · 12 · 106 · 103** against the then-current 115 denominator. Applying the
> derivation above to the *same* rows at the *same* denominator gives **11 · 12 · 104 · 103** — i.e. **two of
> the four were already wrong on 2026-08-17**, before the denominator moved. Against §1.2's new 200 they are
> **11 · 12 · 189 · 188** — **and those are themselves superseded as of 2026-08-19, when the denominator went
> to 216 and they became `11 · 12 · 205 · 204`.** **Any document quoting 9 · 12 · 106 · 103 is quoting a
> figure that was stale the day it was written; any document quoting 11 · 12 · 189 · 188 is quoting the
> 2026-08-18 denominator.** The first two figures are the same in all three sets, which is exactly what the
> self-maintaining derivation is for.
>
> **▶ ONE HONEST LIMIT ON (3) AND (4), stated because the old block never stated it.** The subtraction treats
> each fidelity item as covering exactly one §1.2a capability row. Three items span several rows (the voucher
> shortcut keys, the PO/SO/GRN/DN effect rules, and double-entry posting) and two sit **below** a row (the
> predefined-group set, the EPS/EPF split). **205 is therefore an UPPER bound on what is uncompared**, and the
> true figure is a little lower. Nobody has done the row-by-row mapping; when someone does, it is done **here**.
>
> **▶ THE COLLISION WARNING SURVIVES, and it survives BECAUSE the number it warned about is now zero.** These
> four are a DIFFERENT figure from §1.2's **undetermined** column, which counts capabilities whose *existence*
> nobody checked and is unaffected by anything in this section. That column read **8** on 2026-08-17 and reads
> **0** today (§1.2b). **Do not read "0 undetermined" as agreement between the two figures** — they measure
> different things and they will diverge again the moment a capability is added whose existence nobody checks.
> **▶ THE RULE, and it exists because it has now been broken three times on this branch.** When these numbers
> move they move HERE, and every other document — `plan.md`, the registers, the kick-off — restates them by
> **pointing at §1.3, not by copying the digits**. A corrected figure on this project has three times left
> live copies behind, once in this very file. The single deliberate exception is the closing **"Bottom line
> for the user"** paragraph, which earns its digits because it is the sentence a reader quotes; it therefore
> carries an **as-of date**, so the next drift is visible rather than silent. If any copy anywhere disagrees
> with this block, **this block wins and the copy is a defect**.
>
> **▶ THE SECOND FIGURE THAT PARAGRAPH QUOTES IS DERIVED FROM §2 TIER 0, AND IT DRIFTS EVERY TIME A ROW OPENS
> OR CLOSES.** It is pinned here for the same reason and in the same shape, and it is derived the same way —
> **re-walk the §2 TIER 0 table and count its row markers; never carry a digit forward from a prose sentence.**
>
> **As of 2026-08-18: TIER 0 holds 13 rows, of which 11 are OPEN.** Two are CLOSED — **T0-8** (the blank
> supplier address block, closed 2026-08-17) and **T0-7** (the composition dealer's illegal tax invoice, closed
> **2026-08-18**; see that row for the evidence and for what the old evidence cell got wrong). Two are NEW,
> added 2026-08-18 from the five-survey re-derivation — **T0-12** (attendance is append-only and summed) and
> **T0-13** (`DateOfLeaving` is unreachable while three engines read it).
>
> **The open set and the "confirmed wrong money or invalid document" set are NOT the same set, and the
> difference is worth stating rather than rounding away: 9 of the 11 are confirmed** — T0-1, T0-2, T0-3, T0-4,
> T0-9, T0-10, T0-11, T0-12, T0-13 — **and 2 are not. T0-5 (the 4% cess) and T0-6 (TDS rates cited to
> commercial blogs) are confirmed UNSOURCED, not confirmed WRONG**: the defect in each is that nobody can stand
> behind the figure the product applies to money, which is why T0-5 is a standing user decision rather than a
> fix. **T0-6 got worse rather than better on 2026-08-18** — the seeded statutory masters are immutable by
> design, so a user cannot correct those rates in-app at all (T1-21).
>
> **▶ 🔴 THE T0-3 CAVEAT THAT USED TO SIT HERE IS WITHDRAWN.** It read: *"One caveat inside the 8, recorded so
> the count is not read as more alarming than it is: T0-3 is reachable only through JSON/XML import, not
> through the UI."* **That is not supportable.** The Standard Cost option is populated into a dropdown that
> **is rendered on the Stock Item master screen**, and the create path passes the selection through unguarded —
> so an operator can select Standard Cost in the UI and get the silent fallback. The census's own evidence
> ("zero `StandardCost` hits in the main view") was true and its conclusion did not follow: the control binds
> to the *method list*, not to a literal. **T0-3 is a UI-reachable wrong-valuation route** and is counted
> without a caveat. See §1.2a row 3.4.
>
> **▶ ONE ROW IN THE CONFIRMED SET WAS NOT RE-MEASURED ON 2026-08-18 AND IS SAID SO RATHER THAN IMPLIED:
> T0-2** (closing stock valued at selling price). No survey covered stock valuation; it is carried forward on
> its 2026-08-10 evidence.

> **▶ HOW THIS LIST GROWS (R12, 2026-08-16; the standard tightened 2026-08-17).** Fidelity is measured **per slice**: a slice is not done until it adds a row here for the surface it touched, in the shape of the ones above, **or records why the corpus cannot settle the question**. Row 9 is the first row added under that rule and every later slice copies it, so what its first draft got WRONG is part of the template:
> 1. **An inference is not a source.** It presented the display-versus-stamp shape as attested by Book p.177. The page attests the defaulting; the shape is ours, and its real evidence is an asymmetry in our own store. Cite the store.
> 2. **A worked example only settles the screen it is on.** It resolved State-before-Country on Study Guide p.268 - which is the **Group Company Creation** screen, not Company Creation. One primary source against another is a CHOICE, recorded as one; it is not "resolved on evidence".
> 3. **Labels are part of the field set.** It claimed the field set and screen order as sourced and said nothing about labels, six of which matched neither source.
> 4. **"Deliberately not built" means ALL of it.** Its first list named eight fields; the corpus lists seven more omissions, including two security features and a documented base-currency field.
> 5. **Name every surface the slice TOUCHED, not only the one it was about.** The first draft was silent on a new Gateway section and on a keyboard behaviour change to Company Creation.
> A row that separates *sourced* from *ours*, enumerates its unsourced half, and lands on PARTIAL is the right SHAPE. These five are what make it true as well.

Two further caveats on the denominator itself:

- **Granularity dominates.** Compressing four report families into four rows hides ~14 missing reports. Counting them out gives a denominator near 200 and a worse present-ratio. ~~The 115 is the *most favourable defensible* count.~~ **▶ 2026-08-18 — this caveat was RIGHT and it is now the record of how the move happened, so it is kept.** The denominator did reach 200, but **not** by expanding the four report families: §1.1 rule 2 is retained and they are still one row each. It reached 200 because the rest of the product was written out at rule 1's granularity for the first time (§1.2b). **So the ~14 reports this caveat names are still hidden, and 200 is still the most favourable defensible count** — the caveat now applies to 200 exactly as it applied to 115.
- **The 7.2 baseline is partly unsourced.** The source census marks many 7.2 rows UNVERIFIED — presence asserted from era-ambiguous course syllabi and blogs, because no official 7.2 documentation is reachable and the cracked install is off limits by standing instruction. Roughly 20 of the 90 baseline rows rest on SECONDARY sourcing.

---

## 2. THE GAP REGISTER

Ranked by what a business suffers. Wrong money first, then invalid documents, then impossible tasks, then permanence, then missing capability, then cosmetics.

> ### 🔴 2026-08-18 — WHAT THE FIVE-SURVEY RE-DERIVATION DID TO THIS REGISTER
> Every row below was written on **2026-08-10** at HEAD `468a96e`. Five independent read-only surveys re-walked
> the product at HEAD `6fb5fe5` on 2026-08-18. **The rows are NOT rewritten to today's truth** — that would
> destroy the record. They are marked, in place, here and at each row.
>
> **CLOSED since it was written:** **T0-7** (see its row — the evidence clause is the half that went stale) and
> **T0-8** (closed 2026-08-17). **T1-6** was already marked closed.
>
> **STALE IN PART, and each is stale in a *named* half rather than wholesale:**
> - **T1-1** *"No voucher alteration, deletion, cancellation, duplication or insertion."* **Cancellation (S3)
>   and deletion (S4) have SHIPPED and are live.** Alteration, duplication and insertion are still absent, so
>   the row's headline claim — *"this is the master defect"* — stands on those three. See §1.2a rows 5.1–5.5.
> - **T1-2** the **delete** half is stale: Alt+D master deletion ships on **three** kinds (ledger, group, stock
>   item), each pre-guarded. The **alter** half holds **exactly**: `ForAlter` exists in precisely three master
>   view models tree-wide and nowhere else, and **seven** engine delete services still have zero Desktop
>   callers. One census-era concern is CLOSED rather than stale: the stock-item delete service no longer carries
>   its own weaker rule — it delegates to the shared rules, so the two routes cannot diverge.
> - **T1-4** *"Payroll cannot post."* 🔴 **FALSE at HEAD, and the mechanism the row names does not gate the
>   route.** The opener gates only on the payroll-enabled flag, and the posting service selects the type by base
>   kind — it never calls the type resolver and never tests the active flag. The **true residual** is that the
>   seeded type is still inactive, so Payroll is missing from the Day-Book Alt+A picker. A menu-surface gap, not
>   an unreachable posting path. See §1.2a row 7.9.
> - **T1-4's evidence sentence** *"the only writer of that property in the entire tree is `JobWorkService`"* now
>   has a second writer — but it is a **rollback restore inside a catch**, not an activation route, so the
>   row's substance stands and only the sentence is stale.
> - **T1-7** is **CORRECT but too narrow** — see §1.2a row 13.2 and **T1-20**.
> - **T1-13's** export sub-claim (*"Export hard-maps to EXPWP so there is no LUT/without-payment path"*) is
>   **stale**; the SEZ and deemed-export halves hold. See §1.2a row 6.21.
> - **T2-7** is confirmed, with one nuance recorded at §1.2a row 14.5: a context-rebuilt button bar **does**
>   exist; **none of the eight named options** on it does.
>
> **RE-CONFIRMED OPEN at HEAD, re-measured rather than relayed:** T0-1, T0-3 (with its caveat withdrawn), T0-4,
> T0-5, T0-6, T0-9, T0-10, T0-11, T1-3, T1-5, T1-8, T1-9, T1-10, T1-11, T1-12, T1-14, T1-15, T1-16, T2-1, T2-2,
> T2-3, T2-4, T2-5, T2-6, T2-8. **T0-2 was NOT covered by any survey** and is carried forward on its 2026-08-10
> evidence.
>
> **NEW ROWS added by the re-derivation:** **T0-12**, **T0-13** (above); **T1-17**…**T1-21** and **T2-9**,
> **T2-10** (below). None of them was in any register before today.

> ### 🔴 2026-08-20 — WHAT THE S5d+S5e THREE-LENS REVIEW AND ITS COMPLETENESS CRITIC ADDED, AND THE FOUR FIX AGENTS AFTER THEM
> **STATUS UPDATE 2026-09-04:** of the rows below, **T0-14, T0-15, T0-16, T1-22 and T1-23 are CLOSED** (each with
> a quoted RED-then-GREEN transition and a mutation check on every new guard, each re-measured independently on
> 2026-09-04). **TWO rows were added on 2026-09-04 — `T0-21` AND `T0-22`** — both found while closing T0-16, the
> pattern this very paragraph predicts, once more. Both are **OPEN**; neither is wrong money (T0-21 is a refusal
> in the wrong words, T0-22 a crash instead of a refusal), and both are deliberately left to their own slice.
>
> **NEW ROWS:** **T0-14**, **T0-15**, **T0-16** (Tier 0); **T1-22**, **T1-23**, **T1-24** (Tier 1); **T2-11**,
> **T2-12**, **T2-13** (Tier 2); **six Tier-3 rows**. **Seven of these were found WHILE FIXING, not while
> reviewing** — five of them wrong-money or data-loss, each reproduced with literals through the real screens.
> They are written down here because the last time a defect of exactly this shape was *"recorded as routed to
> `plan.md` when it was not"* (the §194C deductee-type branch), it was lost for weeks and shipped wrong money.
>
> **✅ CLOSED IN THE SAME PASS, and recorded so nobody re-opens them:**
> - **The window-level notice bar truncated EVERY Phase 10.11 lifecycle refusal at one line** — measured by
>   render at **1280×720 DIP** (the 372-character GST shape refusal cut at *"…Alter re-computes the A"*) and
>   still at **1920×1080 DIP** (*"…read the stamped figures. C"*). **Every one of those sentences ends with the
>   operator's instructions, so the discarded half was always the actionable half**, on the one channel that
>   exists precisely because these refusals are otherwise invisible. **FIXED** (`MinHeight` + padding,
>   `TextWrapping`, `MaxLines="4"` derived from the longest reachable refusal). 🔴 **It is the FIRST defect of
>   the UI-truncation class ever found on this surface, and no review lens hunted it** — the completeness
>   critic named the class as unhunted and was right.
> - **The residue claim attached to that fix — *"8 other unwrapped `{Binding Message}` TextBlocks remain"* — is
>   FALSE and is withdrawn**; see the Tier-3 row. Do not open campaign work off it.
>
> **✅ TWO POSITIVE RESULTS THAT CLOSE CRITIC ITEMS RATHER THAN OPENING THEM. A LATER READER MUST NOT
> RE-DERIVE THESE, AND A FIXER TOLD TO "CLOSE THEM" WOULD HAVE WRITTEN DEAD GUARDS.**
> - **Three of the five limbs the critic said *"nobody enumerated"* are ALREADY refused at the door, by name,
>   with a shipped test.** `VoucherAlterationEligibility.ItemGridDerivedLegRefusal` refuses: any **TDS** on any
>   leg; any **TCS** on any leg — its predicate is *"has a TCS"*, so the **below-threshold** detail the critic
>   singled out is covered too, not only the collecting case; a **reverse-charge pair**; and a **GST statutory
>   adjustment**. **Payroll** is refused separately in the same file. ⇒ **THE COMPLETE CENSUS OF `EntryLine`'s
>   EIGHT OPTIONAL FIELDS after the 2026-08-20 fixes, which is the artefact this review was missing:**
>   `BillAllocations` **RE-KEYED on the party leg, CARRIED on every other leg (T1-23 CLOSED)** · `CostAllocations`
>   **CARRIED (fixed)** · `BankAllocation` **CARRIED (T1-22 CLOSED)** · `Forex` **CARRIED (fixed)** · `Gst`
>   **RE-DERIVED, shape-pinned and magnitude-pinned (T0-14 / T0-15 CLOSED)** · `Tds` **REFUSED AT THE DOOR** · `Tcs` **REFUSED
>   AT THE DOOR** · `Payroll` **REFUSED AT THE DOOR**. ~~*"T1-22 and T1-23 are the entire residue."*~~ **SUPERSEDED
>   2026-09-04 — THERE IS NO RESIDUE LEFT:** all eight limbs are now carried, re-keyed, re-derived or refused by
>   name. And the enumeration is no longer the only thing holding it: `ItemInvoiceOptionalPayloadCarryTests` runs a
>   canonical-export byte comparison over an invoice carrying the FULL optional payload on the two legs the screen
>   has no panel for, so a NINTH field added to `EntryLine` and dropped by the rebuild reddens without anyone
>   re-reading this list.
> - **The POS screen has NO discount field and NO round-off field at all** — a zero-hit grep over
>   `PosBillingViewModel`, a `PosConfig` that carries neither, and a round-off parameter POS never passes. **So
>   two critic worries about what the POS rehydration might lose are VOID: there is nothing to lose and nothing
>   to test.** ✅ And an IMPORTED POS bill that *did* carry a round-off leg is already refused by name by the
>   existing leg partition.
>
> **⚠️ ONE NARROWING SHIPPED IN THE SAME PASS AND IS STATED PLAINLY RATHER THAN LEFT TO BE DISCOVERED:** a POS
> bill carrying **two tenders of one kind** is now refused at the POS door, and POS bills were already refused
> at the accounting door — **so such a bill is alterable on NO screen.** That is the correct answer for a shape
> no screen can represent (preserving N tenders of one kind is a new payment-panel design, an R6 plan row, not
> a defect fix), but it is a real narrowing and the refusal sentence names the two routes left.

### TIER 0 — WRONG MONEY AND LEGALLY INVALID DOCUMENTS

| ID | Gap | Evidence | Harm |
|---|---|---|---|
| **T0-1** | **§194Q TDS deducted on the whole transaction value, not the excess over ₹50 lakh.** Once `ThresholdCrossed` returns true, TDS = `assessableValue.Amount * rateBp / 10_000m` on the full amount. | **[V]** `src/Apex.Ledger/Services/TdsService.cs:71-75`. WF-2 is planned in Phase 10.10 but has **not landed** at `468a96e`. | Over-deducts ₹5,000 on the first qualifying transaction and compounds. Deductor liable to the deductee. Register IV-2. |
| **T0-2** | **Closing stock valued at SELLING price.** `LastSaleCost` returns `FlatValue(closingQty, LastSaleRate(...))`. | **[V]** `src/Apex.Ledger/Services/StockValuationService.cs:85`. | Overstates closing stock → overstates gross profit → overstates taxable income. Balance Sheet and P&L both wrong. Register IV-6. |
| **T0-3** | **`StandardCost` is offered as a valuation method whose input field does not exist**, and silently falls back to `LastPurchaseRate`. | Dropdown at `StockItemMasterViewModel.cs:333`; zero `StandardCost` hits in `MainWindow.axaml`; fallback at `StockValuationService.cs:86-87`. ~~Reachable only via JSON/XML import.~~ 🔴 **CORRECTED 2026-08-18 — that last sentence is WITHDRAWN and it made the row look milder than it is.** The dropdown **is rendered on the Stock Item master screen** (its items source and selected item are both bound there) and the create path passes the selection through **unguarded**, so an operator can pick Standard Cost in the UI and get the silent fallback. The zero-hit grep was true and the conclusion did not follow — the control binds to the *method list*, not to the literal. See §1.2a row 3.4. | Silent wrong valuation with no warning to the operator — **on the ordinary UI path, not only on import**. |
| **T0-4** | **GST rate hierarchy inverted; the missing resolution levels now EXIST as masters but nothing READS them.** 🔴 **RE-GRADED 2026-09-03 — THE RESOLUTION HALF SHIPPED (T0-4 slices S1 / S2a / S2b) AND THE ROW STAYS OPEN. READ THIS BEFORE THE 2026-08-15 EVIDENCE BELOW, WHICH IS NOW HALF FALSE AND IS LEFT STANDING AS THE RECORD OF WHAT THAT DATE FOUND.** The header sentence *"nothing READS them"* is **superseded**: `GstService` now walks all five rungs as **data** — one ordered `IReadOnlyList<HierarchyLevel>` per `GstDetailSource` (`LedgerFirstWalk` / `StockItemFirstWalk`), transcribed from the two published order strings, driving ONE loop — with stop-at-first-hit, **Company last**, and the ER-5 unresolved sentinel moved from two rungs in to **behind Company** (so the old hard block can no longer fire on a book that set its rate where the reference product says to). `MasterAncestry` adds the two ancestry rungs under a **cycle guard**; `ResolveDetailBlock` gives cess and reverse charge the **same winning rung** as the rate, closing a defect the fix itself would otherwise have opened. **R12 user ruling, 2026-09-03:** `LedgerFirst` is honoured — on v51+ books the sales/purchase ledger outranks the stock item; pre-v51 books are back-filled to `StockItemFirst` and are unchanged. **A live defect closed as a side effect:** canonical import already PARSED the Group / StockGroup / Company GST blocks and **silently discarded** them; they are now read. 🔴 **WHY THE ROW IS STILL OPEN — three named pieces, none of them cosmetic:** **(1) CAPTURE DID NOT SHIP.** Census row **3.13** is still `ABSENT` — zero `Gst` hits in `AccountGroupMasterViewModel` and `StockGroupMasterViewModel`, no XAML field on either master, the importer still the only writer. **The gap is now sharper than before S2, not softer:** the walk READS those blocks at transaction time, so a rate an operator cannot type is a rate that governs imported books only. Slices S3 (company) and S4 (Stock Group + accounting Group — which must also add the Stock Group ALTER route, which does not exist at all). **(2) THE HSN HALF DID NOT SHIP.** `SourceOfHsnSacDetails` still has **no reader**; `GstReportSupport.HsnSacOf` still takes only a stock item. Slice S5. ~~**(3) FIVE BYPASS READERS ARE UNRECONCILED — see T0-17**, and two of them feed statutory payloads.~~ ✅ **(3) IS CLOSED 2026-09-04 — see T0-17.** All five resolve through `GstReportSupport.BucketingRateOf`; the agreement assertion was seen RED (₹16,800 of filed tax on the wrong HSN; ₹1,000 of Table-12 tax dropped outright) then GREEN. **T0-4 stays OPEN on (1) and (2).** **Fidelity record: §1.3 item 15** (`[GRADE: COMPARED]`, PARTIAL, with the ruling-9 categories and two OPEN R12 questions). **Divergences: `docs/invented-vs-cloned.md` IV-36 … IV-43.** Census row 6.4 moved `ABSENT` → `PARTIAL` on this; **3.13 did not move.** | Register IV-1. **[V] 2026-08-15 — SUPERSEDED IN PART, see the re-grade above:** `MasterGstDetails` is carried by `Group`, `StockGroup` and `GstConfig.DefaultGst`, and `GstConfig` holds the two source-order options (`SourceOfHsnSacDetails`, `SourceOfGstRate`) — but those two have **no reader outside the persistence and Io layers**, and `GstService.cs` / `RcmService.cs` / `Reports/Gstr1.cs` are **unmodified**, so every rate still resolves item-first. See `plan.md` slice S4 (WF-1) for the R6 deviation this half shipped under, and — **added 2026-08-16** — for the **three-lens review that half owed, now PAID** (34 findings; the migration back-fill was being erased by the ordinary save path on non-GST books and is fixed; the missing **design** gate is not retroactively granted). | Wrong tax rate on invoices → wrong GSTR-1/3B → wrong liability. |
| **T0-5** | **4% Health & Education Cess applied to live payroll deductions on a rate the code itself says it could not verify.** | `src/Apex.Ledger/Services/SalaryIncomeTax.cs:50-54` — the comment states the rate must be verified before the FY 2026-27 tables are relied on. | Real money deducted from real salaries on an unsourced statutory figure. **Standing user decision, highest priority.** |
| **T0-6** | **Shipped TDS rates and thresholds cited to commercial blogs** (cleartax, disytax). | `src/Apex.Ledger/Seed/SeedTdsTcsRates.cs:7-8`. | R7 violation on figures the product applies to money. |
| **T0-7** | ~~**A composition dealer's every printed document is an illegal tax invoice.**~~ 🔴 **CLOSED 2026-08-18 (W0-1).** The invoice PDF now branches on the bill-of-supply flag, takes its title from the shared predicate with a **structural, case-insensitive refusal of a TAX INVOICE title**, suppresses every tax head and renders the §10 / Rule 5(f) declaration; the print projector supplies the flag and the title. **What is NOT retroactive:** nothing here re-prints a document already issued. | **[V] 2026-08-10 (the original finding):** `GstReportSupport.cs:110-123`, `VoucherDetailViewModel.cs:36-43`, `MainWindow.axaml:1990` — and **zero** `BillOfSupply` hits in `Apex.Ledger.Io` or `VoucherPrintProjector.cs`. 🔴 **THAT LAST CLAUSE IS THE ONE THAT WENT STALE, AND IT WAS THE WHOLE EVIDENCE FOR THE ROW.** Two of the five 2026-08-18 surveys measured it independently and both counted the opposite: **30 hits in `Apex.Ledger.Io`** and **34 in `VoucherPrintProjector.cs`**. The row survived only because **T0-8 was updated on 2026-08-17 and T0-7 beside it was not** — the same fix pass touched both halves of the printed document. See §1.2a row 6.22. | ~~Non-compliant document issued to customers.~~ **Closed.** The residual is historical documents already issued, which no code change reaches. |
| **T0-8** | **Every printed invoice carried a blank seller address block.** **CLOSED 2026-08-17 - both halves have shipped and the creation path's crash is fixed.** The PRINT half (W0-2a, 2026-08-15) made `SellerBlock` read `MailingName`, `Address`, `Country` and `Pin`, so a captured address prints in full and matches the recipient block. The **WRITE half (W0-2b)** is the company profile screen: the Rule 46(a) address is typeable on creation and on alteration. **What is NOT retroactive, and must not be read as closed:** books already on disk carry no address until someone opens Company Alteration and types one - the fix makes the field reachable, it does not populate history. | **[V] 2026-08-17:** `VoucherPrintProjector.cs:758-764` (`SellerBlock`), `:747-751` (`SupplierPostalAddressText` - the guard that keeps an uncaptured company byte-identical), `Company.cs:67-97`; the capture side is `CompanyProfileViewModel.cs` and `MainWindowViewModel.cs`. Pinned end-to-end by `A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block`. **The structural pin is `CompanyCaptureReachTests`, and its own claim was corrected 2026-08-17:** the reach test that merely counts assignment sites had THREE independent satisfiers (creation, alteration, and the alter screen's private rollback helper), so deleting either real capture left it green. It is now two tests - a floor that says the block is typeable at all, and `Both_company_capture_methods_still_assign_every_postal_member`, which names the two capture methods and fails if either stops assigning any of the four members. **The floor that made the write half safe - `CompanyStorage.cs:142`** is `company.EnsureValid()`, the desktop layer's single validation choke point; it now also holds the books-begin invariant, so a company Save accepts is a company Load can reopen. Its one carve-out - a file-level backup RESTORE, which cannot pass through it - is checked in `RestoreCompanyViewModel.Apply` and stated in the `Save` doc. **And the inheritance is a DISPLAY default, not a stamp - `GstConfigViewModel.cs:583`** seeds the GST home State from the postal one only when nothing is stored and no GSTIN was typed, because a code written onto a GST-off company is discarded by the very next load. *(Previously cited `VoucherPrintProjector.cs:745-750` at census baseline `468a96e`.)* | CGST Rule 46 requires the supplier address on a tax invoice. **Fixable from inside the UI at last** - and still absent on every historical book until it is typed. |
| **T0-9** | **IRN and signed QR are never printed on an e-invoiced supply** — and structurally cannot be. `PdfWriter` exposes only `Text` and `Line`; there is no image primitive. | `PdfWriter.cs:30-70`; zero `Irn`/`QrCode` hits in `InvoicePdf.cs`/`InvoicePrintData.cs`/`VoucherPrintProjector.cs`. | A printed e-invoiced supply is non-compliant. Blocked behind a print-engine rewrite. |
| **T0-10** | **Credit and Debit Notes move no stock.** `ItemInvoiceStock.Counts()` returns true only for Purchase and Sales. 🔴 **WIDENED 2026-08-20 — THIS ROW ALSO OWNS THE CN/DN *PRINT-SHAPE* WALL, WHICH T0-11 USED TO BE BLAMED FOR.** A note cannot carry inventory lines **at any point in its life**: `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` throws on every post and `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` makes the item-invoice chord inert, so there is nothing for a printer to draw. **The re-attribution does NOT enlarge the fix**, and it must not be read as doing so: the legally complete note (**RQ-11b**, CGST **Rule 53**) is **value-level** and ships without this row moving at all. What the re-attribution buys is honesty about the cause. | `src/Apex.Ledger/Services/ItemInvoiceStock.cs:53`. plan.md 10.9 NEXT-1, decision D3 approved behind an oracle. **The print-shape half:** `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` (the throw), `src/Apex.Ledger/Services/VoucherValidator.cs:150-151` (the call), `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` (the inert chord) — all three re-measured first-hand 2026-08-20 before the re-attribution was written down. | Every goods return leaves inventory permanently overstated. **And** an item table can never appear on a printed note while this row is open — which is a commercial-presentation gap, **not** a compliance one, because Rule 53 does not require one. |
| **T0-11** | **A Purchase item-invoice prints as a Dr/Cr voucher with ZERO item detail.** 🔴 **RE-SCOPED AND RE-CAUSED 2026-08-20 (T0-11 grounded design pass, slice S0). THE ORIGINAL ROW READ** ~~*"Purchase item-invoices, Credit Notes and Debit Notes never print in invoice format — they silently fall back to a Dr/Cr voucher print."*~~ **It named the right symptom, the wrong cause, and bundled two different defects under one id.** The **Credit / Debit Note half is REFUTED and moved to T0-10** (rows 4.7 / 4.8). What remains here is the **PURCHASE** half — and it is **worse** than the row said: the plain voucher projection never reads `voucher.InventoryLines` at all, the voucher print DTO has nowhere to put them, and the voucher PDF can only draw a Particulars / Debit / Credit table. **This is a MISSING PROJECTION at three layers, not a predicate flip.** | 🔴 **CORRECTED 2026-08-20. THE ORIGINAL EVIDENCE CELL READ** ~~*"`VoucherPrintProjector.IsTaxInvoice` requires `BaseType == Sales` (`:48`). Contradicts `docs/phase5-reports-io-requirements.md:217` RQ-11."*~~ **All three of its claims are wrong, and each was re-measured first-hand at HEAD before being replaced.** **(1) THE LOCATOR `:48` IS STALE.** `src/Apex.Desktop/Services/VoucherPrintProjector.cs:48` is **prose inside an XML doc comment** (a §206C TCS carry-forward note). The wrapper is a **pure forward** at `src/Apex.Desktop/Services/VoucherPrintProjector.cs:116-117`, and **the rule lives at `src/Apex.Ledger/Reports/GstReportSupport.cs:1695`** — `if (type?.BaseType != VoucherBaseType.Sales) return false;` — where it moved when the §31(3)(c) exempt limb began serving the e-Way engine as well as the printer. **(2) "CONTRADICTS RQ-11" IS BACKWARDS: RQ-11 WAS ITSELF WRONG AND THIS ROW INHERITED THE ERROR.** RQ-11 as shipped commanded a **tax-invoice** format for a *"sales / **purchase** item-invoice"* — a document CGST **§31(1)** puts on *"a registered person **supplying**"*, i.e. one we have no right to issue on the purchase half. **RQ-11 is amended in place to SALES-ONLY, and RQ-11a (recipient-side record) and RQ-11b (Rule 53 note) are added** — `docs/phase5-reports-io-requirements.md:217`. **(3) THE DEFECT IS THE CALL SITE, NOT THE PREDICATE.** Sales-only is the **correct** answer to the question `IsTaxInvoice` is named for; it is **used** at `src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107` to answer a different one — *"should this render with item detail?"* ⚠️ **AND WIDENING THE PREDICATE WOULD BE DANGEROUS, NOT MERELY WRONG — THIS IS THE HAZARD THE ROW NEVER SAW.** `src/Apex.Ledger/Reports/GstReportSupport.cs:1399` gates `IsBillOfSupply`'s limb 2 on `IsTaxInvoice`, and `IsBillOfSupplyForFiling` (`src/Apex.Ledger/Reports/GstReportSupport.cs:1449`) feeds the NIC e-Way `docType` at `src/Apex.Ledger/Services/EWayBillService.cs:482`. So the naive fix would **also** title a wholly-exempt purchase **"BILL OF SUPPLY"** — which CGST **Rule 49** likewise puts on the supplier — **and silently move a code we file with a government portal.** Three consumers move together; the method's NAME is the conflation. **The resolution is the three-axis split in `docs/adr/0002-printed-document-three-axis-split.md`; the slice chain is `plan.md` Phase 10.13.** | A supplier's document is unusable as a document — a purchase item-invoice prints no items, so it cannot be used to verify the input tax credit being claimed. ✅ **THE ITEM-INVOICE SHAPE IS CLOSED 2026-08-20 (Phase 10.13 slice S2)**: it prints a recipient-side `PURCHASE RECORD` carrying its item detail, headed by the SUPPLIER (CGST Rule 46(a)), stating the tax he charged under a caption naming him, with place of supply, our declaration and our signature suppressed, and our voucher number under its own caption *"Our Record Ref."* rather than *"Invoice No."*. **`IsTaxInvoice` and `IsBillOfSupply` were NOT edited** — the classifier consults them — so the NIC e-Way `docType` is unmoved, and the byte golden shows this slice moved **one** printed document and no other. 🔴 Every string on it is **OURS (ruling 9)**. ✅ **AND THE ACCOUNTING (SERVICE) PURCHASE IS CLOSED TOO (slice S3)**: a purchase **accounting** invoice now prints the same recipient-side `PURCHASE RECORD` through the other projection pass, its SAC legs as the line table, via `GstReportSupport.IsRecordedServiceAccountingInvoice` — the inward mirror of the Sales-only service gate, added beside it rather than widening it, so the e-Way `docType` is again unmoved. **NOTHING REMAINS OPEN UNDER THIS ROW.** |
| **T0-12** | 🔴 **NEW 2026-08-18. Recording the same attendance period twice silently DOUBLES the pay.** The attendance service's record method **always appends a new entry** with no dedupe on employee + type + period; its delete method has **zero callers in `src/Apex.Desktop`**; and the payroll computation **sums every matching entry**. | Survey-measured at HEAD `6fb5fe5`: `PayrollAttendanceService` (record appends, delete uncalled), `AttendanceVoucherEntryViewModel` (writes every non-blank row, zero `duplicat` hits), `PayrollComputationService` (the attendance sum). §1.2a row 7.8. | An On-Attendance or On-Production pay head pays twice, and **the operator has no in-app way to undo it** — the recorded entry can be neither altered nor removed. Real money, real salaries. |
| **T0-13** | 🔴 **NEW 2026-08-18. A leaver accrues gratuity provision and statutory bonus for ever.** `Employee.DateOfLeaving` has **zero hits across all of `src/Apex.Desktop`** — no field, no XAML — while **three engines read it**: the gratuity provision skips an employee who has left, the bonus register clips the eligibility year on it, and the ESI contribution emits it as the last working day. | Survey-measured at HEAD: `Employee`, `GratuityProvision`, `BonusRegister`, `EsiContribution`; settable only through JSON/XML import. §1.2a row 7.2. | Provisions and bonus keep accruing for staff who left, and the ESI file never carries a last working day. Both are wrong figures in a filed or auditable artefact. |
| **T0-14** | 🔴 **NEW 2026-08-20. The alteration screen's tax-head shape pin is BLIND to an intra-state GST rate master moved between an EVEN basis-point figure and the ODD one above it, so the ITC and the supplier's credit silently restate on an amendment that touched nothing.** The CGST and SGST legs are stamped with `integratedBp / 2`, an **integer** division, so **500 and 501 both stamp 250** and `TaxHeadSignature` — which compares `ledger｜side｜head｜rate` — sees no change. An INTER-state invoice is safe: the IGST leg carries the full basis points. | **[V] 2026-08-20**, reproduced through the REAL purchase item-invoice screen by the agent fixing the cess blocker: moved the item's rate 5.00% → 5.01% with the alteration screen open and `AcceptAlteration` returned TRUE with *"Purchase No. 1 altered."*; signature identical on both sides; **ITC moved 92.60 + 92.59 = 185.19 → 92.78 + 92.78 = 185.56 and the supplier's credit 3,888.90 → 3,889.27.** The halving is in `GstService.ComputeInvoiceTax`'s rate-group loop; the pin is `VoucherAlterationDerivedLegs.TaxHeadSignature`, whose doc comment now carries these literals under its *"WHAT THIS SIGNATURE IS BLIND TO"* enumeration. | Rs 0.37 on the measured fixture, **unbounded in principle** — the drift scales with the invoice. It was written into the book and into the filed return under the guard's own claim that *"a rate master moved since posting"* is exactly what it refuses. 🔴 **CLOSED 2026-09-03** by `VoucherAlterationDerivedLegs.TaxMagnitudeDriftRefusal`, wired LAST on BOTH accept paths (`AcceptItemInvoiceAlteration` and `AcceptAlterationCore`). **Pinned by AMOUNT, not by rate**, because the integrated bp is NOT recoverable from a posted leg — 250 is 500 and it is also 501 — so "stamp the integrated bp into the signature" was not available without a schema change that could not reach already-posted vouchers. Tests: `An_intra_state_rate_moved_to_the_odd_bp_above_is_refused_at_accept_by_name` + its POS twin (2,100.49 base, 189.05 + 189.04 = 378.09 stamped against 378.30 re-derived at 1801 bp). §1.3 item 12 category (D). |
| **T0-15** | 🔴 **NEW 2026-08-20. The same pin is BLIND to a TAXABILITY FLIP that another line of the same rate group masks.** The signature deliberately excludes the stamped `GstLineTax.TaxableValue` — right for an ordinary amendment, and it also hides a moved master. Only a flip that empties the WHOLE rate group is caught, because only then does a leg disappear. | **[V] 2026-08-20**, reproduced through the real screens: two items both at 18% (one rate group), one posted invoice; flipping ONE item Taxable → Exempt with the screen open was ACCEPTED (*"Purchase No. 1 altered."*) with the signature identical, while **the stamped taxable base fell 7,654.15 → 3,950.44, the ITC fell 688.88 + 688.87 → 355.54 + 355.54 and the supplier's credit fell 9,031.90 → 8,365.23.** | **Rs 666.67 measured on one two-line invoice**, on an alteration that touched nothing. Same class as T0-14 and as the cess blocker the same review found. 🔴 **CLOSED 2026-09-03** by the SAME `TaxMagnitudeDriftRefusal`, which pins the stamped `TaxableValue` alongside the amount. It follows the cess pin's shape — a **re-derivation over the POSTED rows** — so it cannot become a blanket refusal: holding the rows fixed removes them as a variable, and only a moved master can trip it. Two **negative controls** ship with it (`An_ordinary_quantity_amendment_of_a_same_rate_invoice_is_still_accepted` and `An_ordinary_quantity_amendment_of_a_pos_bill_is_still_accepted`), which is the dead-guard half of the proof. Test: `A_taxability_flip_masked_by_a_same_rate_sibling_is_refused_at_accept_by_name` + its POS twin (base 3,451.48 → 2,100.49, tax 621.27 → 378.09, supplier credit would have fallen 4,072.75 → 3,829.57). §1.3 item 12 category (D). |
| **T0-16** | ✅ **CLOSED 2026-09-04.** *(Found 2026-08-20: a cess-bearing item sold over the counter collected ZERO Compensation Cess while the identical item on a Sales item invoice collected it — `PosBillingViewModel.ComputeGst` built its taxable line with **no cess argument at all**, where the accounting item-invoice screen resolves the cess master and passes one. A feature gap, not a regression: true since the POS screen was built.)* **The fix is four coupled edits, not one**, because collecting a cess touches every figure the cess is part of: `ComputeGst` now calls the same `GstService.ResolveCess(item, salesLedger, Date, billedQuantity)` the accounting screen calls; `BillTotal` and `BuildPosBill` add `TotalCess` (which `InvoiceTax.TotalTax` ring-fences OUT, ER-2) so the tender debits FUND the Cess leg instead of leaving the voucher short by exactly the cess; `PosReceiptData` gained `TotalCess` (in `GrandTotal`, zero on a bill of supply) and the receipt PDF a "Compensation Cess" line, or the printed slip would have stated a grand total short of its own tender lines; and `ReDerivedTaxOnPostedRows` — the mirror the drift pin compares against — resolves it too, **without which the fix would have REFUSED every narration-only alteration of a cess-bearing bill.** **The cess drift pin also had to be HOISTED above `BuildPosBill`** (census **T0-21**), and the mirror call had to be **WRAPPED**: `ResolveCess` fails fast on an RSP-factor cess with no Retail Sale Price, so wiring it in put a THROW on a line that had none and the counter would have gone down on Ctrl+A instead of refusing. | **[V] 2026-09-04.** RED first, quoted: `A_cess_bearing_item_sold_over_the_counter_collects_the_cess` → *"Assert.Equal() Failure: Expected: 725.57, Actual: 525.32"* (the bill total missing the 200.25 cess) and `The_counter_and_the_sales_item_invoice_collect_the_same_cess` → *"Expected: 200.25, Actual: 0"*. Then GREEN. **6** tests in `tests/Apex.Desktop.Tests/PosCompensationCessTests.cs`: collection · counter-vs-invoice parity · the printed receipt footing to what was tendered · a narration-only alteration still ACCEPTED · a moved cess master REFUSED by name · an unvaluable cess master refused rather than crashed. **Mutation-checked, each re-measured independently on 2026-09-04:** removing `ComputeGst`'s cess argument reddens **all 6**; deleting the MIRROR's cess argument alone reddens **3** — the three alteration tests, while the three collection tests stay green, which is the asymmetry that proves the mirror is load-bearing on its own; un-hoisting the cess pin reddens **1**, with the verbatim `Assert.Contains() Failure … String: "Cash tendered is less than the cash payable. The c"···` that T0-21 records. ⚠️ *An earlier draft of this cell said "5 tests" and "reddens 2 of them"; both were miscounts and are corrected here rather than quietly overwritten.* | **R7 — WHAT WAS VERIFIED AND WHAT WAS NOT.** The instrument was confirmed at an **official CBIC source**: [cbic-gst.gov.in Compensation Cess (Rate) Notifications](https://cbic-gst.gov.in/hindi/compensation-tax.html) lists **Notification No. 1/2017-Compensation Cess (Rate) dated 28-06-2017**, *"Seeks to notify Rates of goods and services tax compensation cess under Goods and Services Tax (Compensation to States) Act, 2017 (15 of 2017)"*, as the base instrument. The amending notifications that page lists against it, read off the source on 2026-09-04 rather than recalled: **03/2017 (18-07-2017), 02/2018 (26-07-2018), 01/2019 (29-06-2019), 02/2019 (30-09-2019), 01/2021 (30-09-2021), 02/2021 (28-12-2021)**. ⚠️ *An earlier draft of this cell said "amended by 02–07/2017 and later notifications through 2021", which that CBIC page does **not** support; corrected in place, and flagged because an unsupported statutory aside is exactly what this project has had to strip from shipped code before.* 🔴 **NO CESS RATE IS ASSERTED BY THIS CHANGE AND NONE MAY BE INFERRED FROM IT.** The fix wires the resolver that reads the OPERATOR'S OWN cess master (a per-item override or a dated HSN `CessRates` row); it ships no figure. Every number in the test fixture is a declared nonce (₹40.05/unit, moved to ₹90.05/unit), not a statutory per-unit cess. Seeding a rate table from the notification is a separate, unstarted item. |
| **T0-22** | 🔴 **NEW 2026-09-04, found while closing T0-16 — PRE-EXISTING, not introduced by it. `VoucherEntryViewModel.AcceptItemInvoiceAlteration` calls `ReDerivedTaxOnPostedRows(existing)` with NO try/catch, and that method resolves the Compensation Cess, which FAILS FAST.** `GstService.BuildCess` throws `InvalidOperationException` on an RSP-factor cess whose item declares no Retail Sale Price (deliberately — it refuses to value a cess-bearing good at a silent ₹0, ER-5). Both of the accounting screen's OTHER cess sites are wrapped — `RecalculateItemInvoice` and `BuildItemInvoice` each catch `InvalidOperationException or ArgumentException` — **this third one is not.** | **[V] 2026-09-04**, read at HEAD. **The window is narrow and real:** `BuildItemInvoice()` runs first and catches its own throw, but it computes over the **AMENDED** rows while `ReDerivedTaxOnPostedRows` computes over the **POSTED** ones — so an operator who DELETES the offending item row (or short-bills it to zero) gets a clean build and then an unhandled throw from the mirror. The POS twin of exactly this line was measured throwing in this slice (`System.InvalidOperationException : RSP-factor Compensation-Cess requires a declared Retail Sale Price on the item…`) and **was fixed there**; the accounting door was left alone deliberately, being outside the three defects this slice owns. | An unhandled exception out of Ctrl+A on the accounting item-invoice alteration screen — a crash, not a refusal. **The fix is three lines and its shape is already written**: the wrap now standing at the head of `PosBillingViewModel.AcceptAlterationCore`. **OPEN.** |
| **T0-21** | 🔴 **NEW 2026-09-04, found while closing T0-16. On the POS screen the tax SHAPE and MAGNITUDE drift pins are structurally unreachable: `BuildPosBill` refuses first, on the TENDER reconciliation, in the engine's words.** A master that drifts moves the live bill total while the POSTED tenders stay where they were, so the operator amending a narration is told **"Cash tendered is less than the cash payable"** on a bill nobody touched — instead of "a rate master moved". The accounting door does not have this: its party leg is DERIVED, so a drift moves it rather than refusing, which is why the identical pins are reachable there. | **[V] 2026-09-04**, measured while writing `A_moved_cess_master_is_refused_by_name_on_a_counter_bill`: the first run of that test failed with `Assert.Contains() Failure … String: "Cash tendered is less than the cash payable. The c"···  Not found: "Compensation Cess"`. **The CESS pin was fixed by hoisting it above `BuildPosBill`** (it reads only the posted voucher and today's masters). The SHAPE pin genuinely needs `built.EntryLines` and cannot be hoisted the same way, and the MAGNITUDE pin's documented position — AFTER the shape pin, so a drift that moved a head or a rate gets the shape sentence — must survive whatever closes this. | A moved GST rate master under a posted counter bill produces a refusal about the customer's cash rather than about the master, and the operator has no way to tell the two situations apart. Wrong sentence, right refusal — so **no wrong money**, which is why it is filed rather than fixed in passing. **OPEN.** |
| **T0-24** | 🔴 **NEW 2026-09-04 — ONE RE-DERIVATION RESOLVED AT TWO DATES, created by a CLEAN AUTO-MERGE of two parallel tracks.** ✅ **CLOSED 2026-09-04, in the merge commit that created it.** `PosBillingViewModel.ReDerivedTaxOnPostedRows` is the POS master-drift pin: it re-prices the POSTED rows under today's masters and compares the result against the tax STAMPED on the bill, so a master that moved under a bill nobody touched is refused by name. One track gave its RATE a date (it had been calling the date-blind two-argument `ResolveRate`, census **T0-19**); a different track gave the same line a CESS and resolved that on `existing.Date`. Git merged both cleanly — no conflict, no warning — and the merged method resolved its two halves **at two different dates**. | **[V] 2026-09-04, read on the merged tree.** The divergence is REACHABLE, not theoretical: the POS date field is editable (`DateText`, `Mode=TwoWay` in `MainWindow.axaml`) and `RehydrateFrom` only SEEDS `Date` from the voucher being altered, so an operator who touches the date before Ctrl+A gets a rate resolved at one date and a cess at another, inside a single comparison against a single stamp. **The tie was broken by evidence rather than preference:** the ACCOUNTING DOOR'S TWIN, `VoucherEntryViewModel.ReDerivedTaxOnPostedRows`, which BOTH tracks cite as the reference and NEITHER changed, passes `Date` to `ResolveRate` **and** to `ResolveCess`. The POS cess now does the same. | 🟡 **No wrong money was shipped** — neither branch alone had this, and it existed only between the merge and the fix. What it cost was the truth of the method's own doc comment, which claimed *“it mirrors `ComputeGst` line for line”* and *“as on the accounting door”* while doing neither; the fix makes both claims true. 🔴 **Second stale artefact from the same merge, corrected with it:** `ComputeGst`'s doc still carried a paragraph explaining that the cess is dated *“and the RATE deliberately is not”*, naming T0-19 as an open row — a premise the other track had deleted along with the date-blind overload. Struck in place with the original quoted, not removed, because it was the stated reason for a deliberate asymmetry that no longer exists. **Both are the same lesson as T0-23: two individually correct, individually green branches, merged without conflict.** |
| **T0-17** | ✅ **CLOSED 2026-09-04. All five readers now resolve through ONE rule — `GstReportSupport.BucketingRateOf` — and the agreement assertion D9 declined to make now exists and was seen RED first.** The per-site decision the row demanded was taken and it went the same way for all five: each answers "which posted rate group is this line in?", the posting engine answered that question with `ResolveRate`, so any other answer mis-buckets. **The "a filed document must restate what was POSTED, not what masters say today" objection was weighed and does not save the old code:** the raw master read was *itself* a read of today's masters, and a blinder one — it could not see the dated `RateHistory` window at all — so routing through the dated resolver strictly *reduces* the live-master surface. The genuinely posted-rate fix is a persisted per-line rate, which is a schema change and remains a carry-forward. **R7, verified at NIC before touching either payload:** `GstRt` is *"The GST rate, represented as percentage that applies to the invoiced item"*, validated by `CGST Value = Taxable Value × GST Rate ÷ 2` and `IGST Value = Taxable Value × GST Rate` (`https://einv-apisandbox.nic.in/version1.01/generate-irn.html`); the e-Way item rate is the SAME quantity — `igstRate = Item.GstRt`, `cgstRate = sgstRate = Item.GstRt/2`, `taxableAmount = Item.AssAmt` (`https://einv-apisandbox.nic.in/Mapping_of_ewaybill_schema.html`); and the portal schema states `TotItemVal = AssAmt × [1 + (CGST Rate + SGST Rate + …)]` (`https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf`). So the field is a property of the **supply as invoiced**, and a rate that does not reproduce the line's own posted tax from its own assessable amount is a false declaration, not a cosmetic mismatch. 🔴 **AS FOUND, 2026-09-03 — THE MOST SERIOUS OPEN ITEM LEFT BY THE T0-4 CHAIN. Five master-block rate readers bypass `GstService.ResolveRate` entirely, and NOTHING ASSERTS THEY AGREE WITH IT — while TWO OF THEM FEED STATUTORY PAYLOADS.** Drift lock **D9** pins that the five exist and how many there are; it deliberately pins **nothing** about whether they answer the same question the resolver does. Each is hard-wired to **one** rung and returns `0` where `ResolveRate` returns the ER-5 sentinel: `Gstr1.LineIntegratedRate` (Stock Item only) · `Gstr1.LedgerIntegratedRate` (sales/purchase Ledger only) · **`EInvoiceJson.LineIntegratedRate` (Stock Item only — INV-01)** · `EInvoiceJson.ServiceLegsByRate` (Ledger only) · **`EWayBillJson.LineIntegratedRate` (Stock Item only — EWB-01)**. **Before T0-4 S2b these agreed with the resolver by coincidence, because the resolver was item-then-ledger too. On a `LedgerFirst` book — every book created from v51 onward — they can now disagree**, and the two item-only ones carry that disagreement into a payload filed with a government portal. | **[V] 2026-09-03**, read at HEAD: `tests/Apex.Ledger.Tests/OneRuleDriftLockTests.cs` `TheMasterRateBypassReadersAreExactlyTheFiveKnownOnes` asserts the exact inventory `{Gstr1.cs: 2, EInvoiceJson.cs: 2, EWayBillJson.cs: 1}` and its own doc comment states the open decision verbatim: *"making them hierarchy-aware is a decision S2 must take explicitly, per bypass, and record. This lock exists so that decision cannot be taken by omission."* **S2 did not take it.** All five call sites re-located by content this pass and all five are unchanged. **[V] 2026-09-04 — CLOSING EVIDENCE, RED THEN GREEN.** `tests/Apex.Ledger.Io.Tests/RateReaderResolverAgreementTests.cs` (7 tests, one fixture so all five readers are proved against the SAME book). The fixture is a `LedgerFirst` book on 06-Oct-2025 using the **shipped seeded** GST 2.0 windows — cement HSN 2523 (28% → 18%) and car HSN 8703 (28% → 40%) — with the item blocks set to the exact swap, so the divergence is what an ordinary GST 2.0 book does rather than a contrivance. Two of the seven are non-vacuity guards asserting the fixture really diverges (masters 4000/1800 vs resolver 1800/4000); they passed **before** the fix, which is what proves the other five failures were the defect and not a broken fixture. **Measured RED at HEAD:** GSTR-1 HSN 2523 tax `expected 7200.00, actual 24000.00` (₹16,800 of filed tax on the wrong HSN) · GSTR-1 SAC 998311 tax `expected 1800.00, actual 600.00` (and the unmatched 5% group's ₹1,000 dropped from Table 12 entirely — Σ HSN tax 1,800 against ₹2,800 posted) · INV-01 `GstRt` for 2523 `expected 18, actual 40` · EWB-01 `GstRt` for 2523 `expected 1800, actual 4000` · INV-01 service items `expected 2, actual 3` (a synthetic `HsnCd ""` line invented for the unmatched group). **GREEN after: 7/7.** | An INV-01 or EWB-01 payload can state a rate the book did not post, on a v51+ book whose sales ledger and stock item declare different rates. **This was not a footnote to T0-4 — it was the half of the one-rule-several-places defect that the resolver fix left standing, and it was live rather than latent.** ✅ **CLOSED.** The lock moved with it: **D9 is no longer a count but a prohibition** (`NoMasterBlockRateBypassSurvivesAnywhereInTheShippedTree` — the idiom must appear nowhere in `src/`, so a sixth fails without anyone noticing a number moved), and a **new D9b** widens the pattern to the shapes D9's property-pattern could not see — a null-conditional chain, or a read via an intermediate local. 🔴 **D9b's widening independently re-derived a SIXTH bypass that D9's narrower pattern never counted — and it is NOT a new defect: it is `RcmService.cs:82`, already open as row T0-18.** That row was found by reading the two RCM limbs side by side; D9b now catches the same line *mechanically*, which is the stronger guarantee. It is **not fixed here**: unlike the five it **computes tax**, and its `?? 1800` floor is an **unsourced statutory claim** about the import-of-services rate, so closing it needs an R7 verification of that rate and belongs to T0-18, not to T0-17. It is carried as a named, reasoned entry in D9b's exact inventory so it is countable and cannot move silently. |
| **T0-18** | 🔴 **NEW 2026-09-03. The import-of-services RCM rate is HIERARCHY-BLIND and DATE-BLIND**, while the domestic limb fifteen lines below it is neither. `src/Apex.Ledger/Services/RcmService.cs:82` reads `supplyGst?.RateBasisPoints ?? spLedger?.SalesPurchaseGst?.RateBasisPoints ?? 1800` — a hand-written two-rung item-then-ledger pick with a hard-coded 18% floor, **no `ResolveRate` call and no `supplyDate`** — where the domestic goods limb calls `_gst.ResolveRate(item, spLedger, supplyDate)` and gets the five rungs and the dated history. **UNMASKED, NOT CAUSED, BY THE T0-4 CHAIN:** the line is untouched by it; before S2b its two-rung pick happened to match the resolver, and now it does not. | **[V] 2026-09-03**, both limbs read at HEAD in one file: the import limb at `src/Apex.Ledger/Services/RcmService.cs:82`, the domestic limb's resolver call fifteen lines below it in the same method. | A reverse-charge import of services is self-assessed at a rate resolved off a different master than every other line in the same book, and at the **undated** rate even when the company carries a `GstRateHistory` row that moved it. Reverse charge is the recipient's own liability — the wrong figure is paid, and claimed as ITC, by us. ✅ **CLOSED 2026-09-04.** ⚠️ **Every `:NN` in the two cells to the left is a PRE-FIX locator captured at 2026-09-03 HEAD and has since moved — read them as the record of what was found, never as a current pointer.** The limb now calls `_gst.ResolveRate(item, spLedger, supplyDate)` — the same resolver, the same five rungs and the same dated override as the domestic limb. 🔴 **The `?? 1800` floor is DELETED, not re-sourced** (R7: a rate constant with no citation, applied to the recipient's own cash liability). Nothing replaces it: where no rung declares a rate the resolution carries the ER-5 unresolved sentinel (`RcmResolution.IsRateUnresolved`), `BuildReverseCharge` **refuses to post**, and `UpdateRcmPanel` names the rateless ledger and the date instead of arithmetic-ing on the `-1` sentinel. `Resolve` stays **pure and total** (the entry screen re-resolves on every keystroke), which is why the sentinel is a value and not a throw. Drift lock **D10**'s `RcmService.cs` count was deliberately raised 1 → 2. Tests: `tests/Apex.Desktop.Tests/RateResolutionOneRuleTests.cs` — `Import_of_services_resolves_its_rate_through_the_hierarchy` (a rate declared only at the accounting-Group rung: 500 bp, was 1800), `…_as_of_the_supply_date` (2800 on 20-Sep, 1800 on 25-Sep, was 2800 on both), `…_with_no_rate_anywhere_fails_fast_instead_of_defaulting_to_18pct`, `The_screen_names_an_unresolved_reverse_charge_rate_and_refuses_the_voucher`, and `The_reverse_charge_rate_equals_the_ordinary_resolved_rate_for_the_same_masters`. |
| **T0-19** | 🔴 **NEW 2026-09-03. Both POS rate resolutions use the DATE-BLIND two-argument overload, so the dated `GstRateHistory` override never fires at the counter — while every accounting screen passes the date.** `src/Apex.Desktop/ViewModels/PosBillingViewModel.cs:428` and `src/Apex.Desktop/ViewModels/PosBillingViewModel.cs:465` call `_gst.ResolveRate(item, SelectedSalesLedger)`; all four `VoucherEntryViewModel` sites — `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:5073`, `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:5119`, `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:5304` and `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:5760` — call the three-argument form with `Date`. ⚠️ **Every locator here is written in full deliberately: the bare `` `:NN` `` shorthand this document used elsewhere is checked by NEITHER citation guard** (both key on `File.ext:NN`), which is how a section of stale pointers survived a repair pass on 2026-08-21. | **[V] 2026-09-03**, all six call sites enumerated at HEAD by one grep over `src/Apex.Desktop`. The two-argument overload forwards `voucherDate: null`, and the dated override is gated on `voucherDate is { } d`, so it is skipped entirely. | The same item sold over the counter and on a Sales item invoice on the same day carries **different tax** whenever a dated rate-history row is in force — and the counter takes the pre-revision rate for ever. Sits beside **T0-16** (the POS also collects zero cess), so the POS is now known to diverge from the accounting screen on **two** tax dimensions. ✅ **CLOSED 2026-09-04.** ⚠️ **Every `:NN` in the two cells to the left is a PRE-FIX locator captured at 2026-09-03 HEAD and has since moved — read them as the record of what was found, never as a current pointer.** Both POS sites now pass `Date`, and 🔴 **the date-blind two-argument overload is DELETED outright** rather than patched: its entire observable behaviour was to drop the date silently, it left no trace at the call site for a reader or a grep, and it would have re-caught the next caller. `voucherDate` is now a required parameter — a caller with genuinely no date must write `voucherDate: null` and mean it (**22** test call sites were updated to say so explicitly — the compiler enumerated them; **zero `src/` callers remained** once the two POS sites were dated). ⚠️ **T0-16 is NOT closed by this** — the counter still resolves no cess; the two defects shared a screen, not a cause. Tests: `RateResolutionOneRuleTests.Every_surface_resolves_the_same_rate_for_the_same_item_on_the_same_day` (one Car, one book, both sides of 22-Sep-2025: counter and item invoice must agree — the counter previously billed CGST+SGST ₹2,00,000+₹2,00,000 on 20-Sep where the invoice billed ₹1,40,000+₹1,40,000) and `The_counter_POSTS_the_dated_rate_not_the_items_undated_scalar` (asserted on the posted Output CGST/SGST ledger closings, not the preview). |
| **T0-20** | 🔴 **NEW 2026-09-03. The dated rate-history override is itself HIERARCHY-BLIND — it keys on a hard-coded ITEM-FIRST HSN pick.** `GstService`'s dated overload resolves the base rate through the full five-rung walk and then looks its override up on `item?.Gst?.HsnSac ?? salesPurchaseLedger?.SalesPurchaseGst?.HsnSac` — a two-rung item-then-ledger choice that **ignores `SourceOfGstRate` entirely**. On a `LedgerFirst` book the base rate can come from the LEDGER while the override that replaces it is matched on the **ITEM's** HSN. | **[V] 2026-09-03**, read at HEAD inside `GstService.ResolveRate(StockItem?, Ledger?, DateOnly?)`: `ResolveBase` walks the hierarchy, and the override's `(item ?? ledger).HsnSac` pick sits directly beneath it, unchanged by the T0-4 chain. The T0-4 design named this as an "ours" item (*"our four fields carry one undated rate, and `GstService`'s date-aware layer keys its override on `(item ?? ledger).HsnSac` only"*); **what the design did not say is that the pick contradicts the walk it now sits on top of.** | A rate-revision row fires off the wrong master's HSN and **replaces a correctly-resolved rate with one that belongs to a different classification** — the override is not a refinement of the walk, it is a second, inconsistent resolution. Related to **IV-43** (no per-master dated history at all). ✅ **CLOSED 2026-09-04.** The key is now `GstService.ResolveHsnSac` — the HSN/SAC of the **first rung along the same `Hierarchy` walk that declares one**, so `SourceOfGstRate` steers the override exactly as it steers the rate. `Rung` carries `HsnSac`, so the **three narrow rungs the old two-rung pick could never see** (accounting Group, Stock Group, company default) now supply a key when the walk resolves there. Rule is *"first rung DECLARING an HSN"*, not *"the rung that supplied the rate"* — the same rule `ResolveDetailBlock` already applies to cess and reverse charge, and for the same reason (the rate walk falls through a taxable, rate-less block). 🔴 **TAIL FOUND AND FIXED DURING THE CHANGE, recorded because a green suite hid it:** the classification walk asks one question further than the rate walk, so on a book carrying dated rows it reached rungs `Hierarchy`'s laziness deliberately never builds — which would have resurrected the unpostable-book shape `A_cycle_below_an_answering_item_rung_is_never_reached` exists to prevent. Guarded by a **narrow** catch that can only ever see a cycle strictly *below* the answering rung (`ResolveBase` runs first and has already thrown for any cycle at or above it), and the `RateHistory` test was moved ahead of the walk so a book with no dated rows pays nothing (ER-13). Tests: `RateResolutionOneRuleTests.The_dated_override_is_keyed_by_the_master_that_supplied_the_rate` (ledger HSN 2523 vs item HSN 8703 on 25-Sep-2025: 1800 bp under `LedgerFirst`, 4000 bp under `StockItemFirst` — was 4000 under both), `A_rate_resolved_at_a_group_rung_is_overridden_on_that_rungs_own_HSN`, `A_cycle_below_the_answering_rung_stays_unreachable_on_a_book_with_dated_rows` and its mirror `A_cycle_the_rate_walk_reaches_still_fails_fast_with_a_date`. |
| **T0-23** | 🔴 **NEW 2026-09-04 — A DEFECT NEITHER BRANCH HAD, CREATED BY THE MERGE OF THE TWO, WHICH COMPILED AND LEFT BOTH SIDES' SUITES GREEN.** ✅ **CLOSED 2026-09-04, in the merge commit that created it.** T0-20 replaced the dated override's hard-coded HSN pick with `GstService.ResolveHsnSac`, which walks the SAME `Hierarchy` in the SAME order as the rate. A **separate parallel branch** added `GstService.TaxabilityIsSourceOrderDependent` (assumption A-QB) and threaded an explicitly named `source` through a new `ResolveRateUnder`, so the counterfactual arm can ask what the OTHER published order would say. Merged, `ResolveRateUnder` resolved the BASE under its named `source` and then called `ResolveHsnSac`, which **re-read the order from the config** — so on the counterfactual arm the rate walked one way and the HSN walked the other. **That is T0-20's own defect, in T0-20's own words (*“a second, inconsistent resolution”*), reintroduced three commits after it was closed.** | **[V] 2026-09-04, RED THEN GREEN, measured by reverting the single `source` argument:** `tests/Apex.Ledger.Tests/GstSourceOrderCounterfactualTests.cs` — a `LedgerFirst` book, item Taxable 1800 bp under HSN `ITEMHSN`, sales ledger Exempt under SAC `LEDGHSN`, and ONE dated row keyed on `LEDGHSN` at 0 bp. The `StockItemFirst` arm keeps its own 1800 bp when the override is keyed by the walk it resolved under, and is replaced by 0 bp when it is keyed by the configured walk. RED: `Assert.True() Failure — Expected: True, Actual: False`. GREEN after threading `source` through. A second test with the dated row removed is the control and passed in BOTH states, which proves the failure is the KEY and not the fixture's taxability shape. | 🟡 **HONEST SCOPE — LATENT, NOT LIVE, and said so rather than inflated.** No shipped screen or report can reach it today: the only consumer, `GstReportSupport.IsWhollyExemptItemSupply`, consults the counterfactual **after** the live resolution has already said TAXABLE, which means the taxable arm is always the CONFIGURED one — and there the named source and the config agree, so the two spellings cannot differ. Fixed anyway because `TaxabilityIsSourceOrderDependent` is **public**, a second caller costs one line, and “wrong but currently unreachable” is exactly the state a later slice turns into wrong money without re-deriving why it was safe. 🔴 **The process lesson is the row's real value: both branches were individually correct and individually green, git merged the file with no conflict, and the defect exists only in the combination.** |
| **T0-25** | 🔴 **RENUMBERED FROM T0-21 IN THE 2026-09-05 MERGE - two parallel tracks each minted T0-21..T0-23 for DIFFERENT defects; the rows already on `main` keep their numbers and these three moved past T0-24.** 🔴 **NEW 2026-09-04. THREE STATES' PROFESSIONAL-TAX SLAB TABLES SHIP AS A LIVE MONTHLY SALARY DEDUCTION UNDER AN "A14-VERIFIED" LABEL WITH NO CITATION OF ANY KIND.** `ProfessionalTax.SeedSlabTables()` ships five slab tables — Maharashtra men, Maharashtra women (annotated *"(2023 amendment)"*), Karnataka and West Bengal — including a **February over-charge** and a ₹25,000 women's exemption, under a doc comment reading *"A14-verified FY 2025-26"*. | **[V] 2026-09-04**, wave-2 statutory pass, `src/Apex.Ledger/Services/ProfessionalTax.cs`. **A14 wrote that pass and reports there is nothing here to verify against:** no URL, no Act section, no page, for any of the five tables, for the February over-charge or for the women's exemption. The **only** sourced figure in the file is the **₹2,500 annual cap, correctly attributed to Article 276(2)** of the Constitution. | **This is the T0-6 pattern one step worse.** T0-6's rates at least cited *something* (commercial blogs) that could be seen to be inadmissible; **these cite nothing while carrying an agent's name as warrant.** **A14 does not endorse the label and it should be removed or sourced.** Mitigations are real but partial — the tables are per-company editable and `ApplyAnnualCap` bounds even a mis-configured table — so **exposure is capped at ₹2,500 per employee per year, not unbounded**. **OPEN.** |
| **T0-26** | 🔴 **RENUMBERED FROM T0-22 IN THE 2026-09-05 MERGE - two parallel tracks each minted T0-21..T0-23 for DIFFERENT defects; the rows already on `main` keep their numbers and these three moved past T0-24.** 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 20). THE GSTR-3B A USER ACTUALLY SEES OMITS EVERY REVERSE-CHARGE AND EVERY ITC-REVERSAL FIGURE THE ENGINE COMPUTES — AND STILL LABELS ITS PARTIAL SUMS "Total output tax" AND "Total eligible ITC", SO "Net payable" IS WRONG IN BOTH DIRECTIONS.** The engine holds the twenty RCM / reversal fields; the **screen** renders six properties and none of them. | **[V] 2026-09-04.** `BuildGstr3b` at `src/Apex.Desktop/ViewModels/ReportsViewModel.cs:2132-2190` renders six properties; `grep -rn "RcmOutwardCgst\|ItcReversed4B1Cgst\|RcmItcOtherCgst" src/Apex.Desktop` → **one** hit, `RunSetOffViewModel.cs:246`, **a different screen**. 🔴 **And the product CONTRADICTS ITSELF:** the ITC Set-Off screen computes the same period **correctly** and carries a written comment warning against exactly this mistake. Row 6.9's evidence cell credited the **projection** to a row whose capability is the return **on screen** — corrected in §1.2a in the same pass. | Any company with reverse-charge liability or a posted ITC reversal reads a wrong net-payable off the statutory return screen, and the number it should be compared against is one screen away and right. **OPEN.** |
| **T0-27** | 🔴 **RENUMBERED FROM T0-23 IN THE 2026-09-05 MERGE - two parallel tracks each minted T0-21..T0-23 for DIFFERENT defects; the rows already on `main` keep their numbers and these three moved past T0-24.** 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). RATIO ANALYSIS COMPUTES `Sundry Debtors` FROM THE BALANCE-SHEET CLOSING WHERE THE VENDOR USES THE DUE-TILL-TODAY OUTSTANDING — AND `ReceivablesTurnoverDays` IS DERIVED FROM IT: A CORRECT FORMULA OVER A DIVERGENT INPUT.** | **[V] 2026-09-04.** `RatioAnalysis.cs:100-107` (source) and `:159` (consumer), against the vendor's *"Sundry Debtors (due till today)"*. 🔴 **The file's own comment claiming the report is *"verified against official help"* is only two-thirds true**, which is why this survived. | ⚠️ **Scope stated precisely rather than over-sold: NO posting, NO ledger and NO statutory filing is affected — the defect is confined to the on-screen ratio.** But a business reads receivables turnover as a financial fact, and for any book with unmatured bills ours is wrong. It is the mildest row in this tier and is filed here because it *is* a wrong figure, not a missing one. **OPEN.** |

### TIER 1 — ROUTINE TASKS IMPOSSIBLE, OR DAMAGE PERMANENT

| ID | Gap | Evidence | Harm |
|---|---|---|---|
| **T1-1** | **No voucher alteration, deletion, cancellation, duplication or insertion.** `VoucherDetailViewModel` is display-only. Alt+D is unbound. Alt+X abandons an *in-progress* entry; it does not cancel a posted voucher. | `VoucherDetailViewModel.cs:31-43`; `MainWindow.axaml.cs:875` (a bare-letter menu jump, not delete); `:309-314`. Phase 10.11 PLANNED, not built. | **This is the master defect.** Every error in Tier 0 is permanent once posted. A real book cannot be kept. |
| **T1-2** | **No master Delete anywhere in the UI, and no Alter for 24 of 27 master kinds.** The engine already has 16 delete services with **zero** Desktop callers; 8 more delete services do not exist at all. | **[V]** `ForAlter` exists in exactly 3 master VMs (Ledger, Group, Stock Item) plus the dispatcher; **[V]** zero Desktop callers for any master-delete service. One `Delete` button exists in 16,988 lines of `MainWindow.axaml` and it deletes a Saved View. | A typo in a master is permanent. Tally has Alter on at least 13 master kinds (corpus BOOK, 19 distinct `GOT > Alter > …` step lines). |
| **T1-3** | **No Voucher Type master.** No ViewModel, no `Screen` enum member, no Create-menu row. Consequently: no custom voucher types, no numbering-method selection, no way to activate an inactive type. | No `VoucherTypeMasterViewModel` among 120 ViewModel files; zero `"Voucher Type"` hits in the label dispatch. Corpus BOOK pp.17-18 has all four verbs. | Blocks a whole configuration layer, and directly causes T1-4. |
| **T1-4** | **Payroll cannot post.** The Payroll voucher type ships `IsActive = false` and `PayrollService.EnablePayroll` never flips it — the only writer of that property in the entire tree is `JobWorkService.cs:51`. `VoucherTypeResolver.ResolveForEntry` returns null with a message telling the operator to activate a type there is no UI to activate. | `SeedVoucherTypes.cs:67`; `PayrollService.cs:36-40`; `VoucherTypeResolver.cs:58`. Also excluded from the Day-Book Alt+A picker (`MainWindowViewModel.cs:3007`) and the Scenario picker. | An entire declared-complete phase (Phase 8) has an unreachable posting path. |
| **T1-5** | **Voucher numbering Manual and None are unreachable.** `MethodDisplay` is a read-only string with no setter; the Voucher No. on the entry screen is a `<Run>` inside a `TextBlock`, not a TextBox. | `VoucherNumberingConfigViewModel.cs:115`; `MainWindow.axaml:2056, 3544, 3879, 4104`; seed hard-codes Automatic for all 23 types. Confirms IV-13. | Cannot match a pre-printed book, cannot continue an existing numbering series. |
| **T1-6** | ~~**Company creation captures one field: Name.**~~ **CLOSED 2026-08-17 (W0-2b).** Creation now captures the eleven profile fields - mailing name, the postal block, both book dates and the four base-currency fields - and an **Alter Company** screen (Gateway -> Masters) edits them on an open book. **The prior-FY path named in the impact column crashed when this row was first marked closed:** typing only a books date earlier than 1-Apr of the current year - the input the field's own placeholder invites - threw an unhandled `ArgumentException` at the Avalonia dispatcher, because the screen guard could not see the default the factory was about to substitute. Fixed by exposing `CompanyFactory.DefaultFinancialYearStart` and reading the guard's fallback from it, and by making `CreateCompany` report a domain refusal instead of throwing. | `MainWindow.axaml` (the creation form and the alteration page); `CompanyProfileViewModel.cs`; `MainWindowViewModel.cs`. Pinned by `CompanyProfileScreenTests` - in particular `Creating_with_only_a_books_date_before_the_default_year_start_is_refused_not_crashed` and `Every_one_of_the_eleven_fields_altered_on_the_screen_survives_a_save_and_a_reload` (the alteration leg, without which eight of the alter screen's eleven writes had no test at all). | **A company can now be created for a prior financial year**, so a historical book can be entered - and the FY is no longer hard-coded to 1-Apr of the current year. **Still absent, each for a stated reason - the full list is in 1.3 row 9**, and it is longer than this row first claimed: the five contact fields, the three base-currency formatting toggles, "No of decimal places for amount in words", the whole Security Control heading (Tally Vault Password, User Access Control), Directory, Group Company / Alt+R, company RENAME and company DELETE. |
| **T1-7** | **Restore is unreachable on a fresh install.** The engine supports restoring a company this machine never had; the screen is gated on an open company. | `MainWindowViewModel.cs:2826-2842`, `:6776`; engine capability at `CompanyBackup.cs:268-270`. | The exact disaster-recovery case backup exists for is the one case it cannot serve. **~half a day.** |
| **T1-8** | **No Tracking Numbers.** Receipt Note↔Purchase and Delivery Note↔Sales cannot be linked. | Zero `TrackingNumber` hits in `src/`. | Order fulfilment cannot be tracked correctly — a named prerequisite for WF-8. |
| **T1-9** | **71 of 77 report surfaces are dead ends.** 6 of 45 `ReportKind` values drill; 0 of 32 dedicated report Screens drill. | `ReportsViewModel.cs:1093-1120`; `MainWindowViewModel.cs:2083-2100`. **This corrects IV-19, which says "~50" and counts only the separate Screens — it understates its own defect by ~40%.** | Drill-down is the single most-used gesture in Tally. It is absent from 92% of our report surface. |
| **T1-10** | **32 of 77 report surfaces cannot be printed at all; 22 of those have no export either.** | `IsPrintablePage = IsReportContext \|\| VoucherDetail`, and `IsReportContext` requires `Reports != null`, which is null on all 32. **[V]** `MainWindowViewModel.cs:2214, 2227`. | A user cannot get Outstandings, BRS, Cost, Budget Variance, GSTR-4/9/9C, ITC or challan-recon out of the app in any form. |
| **T1-11** | **No GSTR-1 or GSTR-3B JSON — the two returns that actually get uploaded.** And the five return writers that *do* exist are dead code. | **[V]** `GstReturnJson` has zero production callers: the only references in `src/` are two doc comments (`Gstr1.cs:93`, `EInvoiceJson.cs:13`). No GSTR-1/3B emitter exists anywhere. | GST filing is impossible from the app. Contradicts plan.md:85 and :428. |
| **T1-12** | **GSTR-1 is missing 7 form tables** (5 B2CL, 6A exports, 6B SEZ, 6C deemed, 7 B2CS keyed on Place of Supply, 8 nil/exempt four-way split, 13 documents issued); **GSTR-3B is missing 5** (the 3.1 four-way split, 3.1.1, 3.2, 5, 5.1). `Gstr1B2CRow` has no Place-of-Supply member at all. | `Gstr1.cs:42, 171-180`; `Gstr3b.cs:22-31`. Our own A14-confirmed comment at `Gstr1Amendments.cs:70` proves tables 5 and 6A exist on the form — the *amendment* rows are modelled while the originals are not. | Returns are structurally incomplete, not merely unverified. |
| **T1-13** | **Export/SEZ/deemed-export: 3 of 5 supply-category enum values are dead.** `ResolveSupplyCategory` never mints SEZ or DeemedExport; Export hard-maps to EXPWP so there is no LUT/without-payment path. None feeds GSTR-1/3B. | `GstEnums.cs:341-346`; `EInvoiceService.cs:86-106`; `EInvoiceJson.cs:156-161` — all three files say so in their own doc comments. | Zero-rated supplies cannot be handled correctly. |
| **T1-14** | **No cheque printing, deposit slip, banking payment advice, cheque register, multi-account printing, delivery challan, reminder letter, or confirmation of accounts.** Cheque printing is *configurable* and cannot be *performed*. | `Ledger.EnableChequePrinting` / `ChequePrintingBankName` exist, persist and round-trip through Canonical XML/JSON, with zero UI and zero consumers in any print path (`Ledger.cs:54-66`; `SqliteCompanyStore.cs:4978-4979`). | Seven standard Tally documents have no code at all. Textbook dead-field trap. |
| **T1-15** | **No F11 Accounting Features group at all.** Bill-wise, interest, cost centres, multi-currency, budgets, credit limits, cheque printing, multi-address have no per-company switch. No `Integrate Accounts with Inventory`. No Accounts-only vs Accounts-with-Inventory maintain mode. | `GstConfigViewModel.cs` — 21 booleans, all statutory plus six inventory. Zero hits for `IntegrateAccountsWithInventory`, `MaintainMode`. | Every one of those capabilities is always-on with no way to turn it off. `Integrate Accounts with Inventory` in particular changes how the Balance Sheet sources closing stock — it belongs in wrong-figures territory, not a checkbox. |
| **T1-16** | **No global F12 configuration tree.** Four context panels only; everywhere else F12 sets a stub string. | `MainWindowViewModel.cs:6628-6653` — the fall-through is literally `Message = "F12 Configure — display options (Phase 1 defaults)."` | Blocks the entire display/entry configuration layer. Entangled with gap-decision D10. |
| **T1-17** | 🔴 **NEW 2026-08-18, AND IT IS THE LARGEST STRUCTURAL FINDING OF THE RE-DERIVATION. The product has TWO voucher collections and every lifecycle surface reaches only one.** The Day Book builder iterates the **accounting** collection alone; Alt+X and all three of Alt+D's voucher routes resolve through a finder that searches the same collection; and the inventory posting service's own cancel and delete methods — documented as the Alt+X / Alt+D of the stock aggregate — have **zero production callers**. | Survey-measured at HEAD: `Company` (two collections), `DayBook`, `Company.FindVoucher`, `InventoryPostingService` (cancel and delete uncalled; the only Desktop references are post-path constructions). §1.2a rows 4.9–4.16 and 11.4. | **Eight of the classic eighteen voucher kinds — Purchase Order, Sales Order, Receipt Note, Delivery Note, Rejection In, Rejection Out, Stock Journal and Physical Stock — can be posted and then never listed in the Day Book, never drilled, never cancelled and never deleted.** S3 and S4 shipped green against the accounting aggregate; the stock aggregate was never in their scope and **nothing recorded that**. |
| **T1-18** | 🔴 **NEW 2026-08-18. `Optional` and `Post-Dated` are ONE-WAY flags.** Zero post-construction writers of either exist in `src/`, and there is no voucher alteration. | Survey-measured at HEAD: `Voucher`, both toggle methods, and the balance walk that honours them. §1.2a rows 5.7 and 5.8. | **A voucher marked Optional can never be regularised and a post-dated cheque can never be marked cleared.** Additionally the Optional toggle dispatches only on the accounting entry screen, and the inventory voucher carries no Optional member at all, so the eight kinds in T1-17 cannot be Optional even at entry. No Optional and no Post-Dated register exists to find them with. |
| **T1-19** | 🔴 **NEW 2026-08-18. Printed wide reports carry BLANK column headings, and 17 report kinds EXPORT with blank headers.** The print projector hard-labels the first column and emits an **empty caption** for every other column; the real captions live only in the **export** twin. Separately, the export header map covers 16 kinds and falls through to an empty array for the rest. | Survey-measured at HEAD: `ReportPrintProjector` (the empty-caption emission) against `ReportTabularProjector` (the captions). The 17 are Batch-wise, Batch Age Analysis, Price List, the nine TDS/TCS kinds and the five payroll kinds. §1.2a rows 12.1 and 13.5. | A printed Stock Summary, GSTR-1 or Order Register **has no headings at all** while its CSV of the same data does — the two projectors diverged and nothing noticed. The exported subset is the mirror defect. |
| **T1-20** | 🔴 **NEW 2026-08-18. There is not a single file or folder dialog in the product, and Restore cannot target any company but the one already open.** Zero hits in `src/Apex.Desktop` for the storage provider, both file dialogs, the folder dialog and the picker options type. The restore screen's target-name property has **zero bindings in the XAML**. | Survey-measured at HEAD. §1.2a rows 13.10 and 13.2. **This WIDENS T1-7** — the engine signature accepts any target path; the screen cannot express one, fresh install or not. | Backup destination, restore source, import source, export destination and the `.eml` path are typed strings or a silent default to Documents, overwriting in place. **A user restoring from a backup must type the full archive path from memory**, into a screen that will only ever restore over the company they already have open. |
| **T1-21** | 🔴 **NEW 2026-08-18. The seeded TDS and TCS statutory masters are immutable, so T0-6's rates cannot be corrected in-app.** Both Nature-of-Payment and Nature-of-Goods screens are **create-only** and say so in their own doc comments: the predefined masters are an add-only domain and the screen does not edit a seeded nature. | Survey-measured at HEAD: `NatureOfPaymentMasterViewModel`, `NatureOfGoodsMasterViewModel`, `SeedTdsTcsRates`. §1.2a rows 6.28 and 6.36. | The seed's claim that a Finance-Act change is *"a data edit, not a code change"* is true **only of the C# source**. A user facing a wrong rate has no remedy inside the product. This makes T0-6 worse than it reads. |
| **T1-22** | 🔴 **NEW 2026-08-20. A `BankAllocation` on the PARTY leg of an item invoice is DESTROYED on re-accept — the instrument detail AND the reconciliation date — and the warning rides on the SUCCESS message.** `BuildItemInvoice` constructs the party line bare, so the cheque/DD number, its type, its instrument date and its bank date all vanish. `Replace`'s `CarryBankDatesForward` does not carry it; it raises a warning which is then **appended to the "… altered." message**, so the operator is told the amendment succeeded and the loss is on the same line. **The party picker really does offer a bank ledger** — the party list is *"(none)" + every ledger*. | **[V] 2026-08-20**, scratch xUnit fact posted through the REAL item-invoice screen, then a `Replace`-stamped `BankAllocation`, then the REAL `ForAlter`/`AcceptAlteration`. Verbatim before: `bank=True instr='CHQ-90210' type=ChequeOrDD instrDate=03-04-2026 bankDate=05-04-2026`; after: `bank=False instr='' type= instrDate= bankDate=`. 🔴 **AND THIS CORRECTS THE S5d/S5e VERIFIER, WHICH IS THE PART THIS PROJECT LOSES:** the verifier told the fixer to DROP this limb and asserted *"only the instrument detail, not the reconciliation date, would be at risk there"*. **The reconciliation is lost too.** The fixer probed instead of assuming, and the verifier was wrong. | ✅ **CLOSED 2026-09-04, by CARRYING the allocation** through the mechanism this row named: `BankAllocation` joins `CarriedLegChildren` and is put back by `BuildItemInvoice` on all three leg kinds. 🔴 **THE CARRY RULE IS DELIBERATELY NOT THE COST/FOREX RULE.** Those refuse when the leg's AMOUNT moves, because a split must foot and a forex pair must reproduce the leg to the paisa. A `BankAllocation` carries **no amount at all** (`EnsureBankAllocationValid` does no split-sum check — it only demands a bank ledger), so applying the amount gate to it would have made a cheque-paid invoice **permanently un-amendable**. It is therefore carried whenever the **LEDGER** is unchanged, and a re-point is refused by name (the cheque was drawn on the bank it names). ⚠️ **THE DESIGN QUESTION, SETTLED AND LABELLED AS OURS (Ruling 9 — the corpus is silent on an alteration that un-reconciles).** ① **Carry, not refuse at the door**: refusing would block a narration-only amendment of any invoice ever paid by cheque, and the operator has no other route to make it. ② **The `CarryBankDatesForward` warning STAYS where it is** — but the loss it warned about is gone, so on this path it can now only be the `BankDateCleared` arm, which is a **recoverable** consequence of an amount the operator just keyed: the instrument is still on the line, so the BRS still lists the row and they can re-tick it. The `BankDateLineRemoved` arm was **not** recoverable — the allocation itself was gone, the row vanished from BRS and `SetBankDate` would throw — and recoverability is exactly the line between "warn on a success" and "refuse". The bank DATE is not re-decided by the screen: putting the allocation back is what gives §3.4's own machinery a line to pair against. | **[V] 2026-09-04.** RED first, quoted: `Amending_the_quantity_keeps_the_instrument_and_clears_only_the_reconciliation` → *"Assert.NotNull() Failure: Value is null"*. Then GREEN. 4 tests in `tests/Apex.Desktop.Tests/ItemInvoiceOptionalPayloadCarryTests.cs` (carry · clear-the-tick-keep-the-instrument · re-point refused by name · the full-payload byte comparison). **Mutation-checked both ways**: forcing `HasFootingChildren` true reddens the carry-across-an-amendment test, forcing it false reddens the T1-23 refusal test. |
| **T1-23** | 🔴 **NEW 2026-08-20. `BillAllocations` on a bill-wise VALUE leg are destroyed on re-accept with NO warning at all — not even the one T1-22 gets.** Bill-wise is properly re-keyed on the PARTY leg; on the value leg it is dropped. A Purchase Accounts ledger with `MaintainBillByBill` set is legal — the validator gates only on that flag and on the split footing the line, neither of which is party-specific. **Nobody had enumerated this: the finding, its verifier and the completeness critic all discuss bill-wise only on the party leg.** | **[V] 2026-08-20**, same scratch fact: value ledger with `billWise: true`, item invoice 2 @ 1234.57, value leg 2469.14 carrying one `BillAllocation(NewRef, 'VALUE-LEG-REF', 2469.14)`. Verbatim before: `bills=1 'VALUE-LEG-REF'`; `AcceptAlteration -> True : Purchase No. 1 altered.`; after: `bills=0`. | ✅ **CLOSED 2026-09-04. ⚠️ THE DESIGN QUESTION IS SETTLED AS *BOTH*, AND THE SPLIT IS THE POINT — labelled as OURS (Ruling 9).** ① **CARRY while the value leg is unmoved.** The split is captured into `CarriedLegChildren` for the value leg and the additional-cost legs — **but deliberately NOT for the party leg**, whose split is RE-KEYED by the invoice Bill-wise panel; capturing it there too would post it twice (mutation-checked: flipping that one flag reddens **7** existing tests). ② **REFUSE BY NAME when the leg MOVES.** Unlike a bank instrument, a bill-wise split must sum EXACTLY to its line (`EnsureBillAllocationsValid`), and the value leg is precisely what an item amendment moves — so a carried split would be refused by the ENGINE, in words the operator never saw, after they had re-keyed the whole invoice. The screen's one bill-wise panel is bound to the PARTY, so there is nothing to re-cut it on. ③ **REFUSE AT THE DOOR for the flag drift.** The value ledger's `MaintainBillByBill` turned off after posting is refused before the screen opens, mirroring `RehydrateInvoiceBillWise`'s party check — one direction only, and the asymmetry is stated where it is written: the party's panel can ACQUIRE a split the posted leg never had, the value leg has no panel and can only lose one. **Justified from recoverability:** at the door nothing has been re-keyed yet, so there is nothing to lose; at accept the operator has spent the work and gets a refusal about a panel they cannot see. | **[V] 2026-09-04.** RED first, quoted: `A_bill_wise_split_on_the_value_leg_survives_a_narration_only_re_accept` → *"Assert.Single() Failure: The collection was empty"*. Then GREEN. 3 tests in `tests/Apex.Desktop.Tests/ItemInvoiceOptionalPayloadCarryTests.cs`, each mutation-checked to redden on its own guard. |
| **T1-24** | 🔴 **NEW 2026-08-20. The type F-keys destroy an in-progress POS bill AND an unsaved POS ALTERATION, with no prompt and no notice.** Same root as the accounting-screen work-loss defect fixed in the same review: the F4–F9 button-bar rows are enabled on *has a company* alone, and `OpenVoucher` → `OpenPageColumn` → `ClearSubScreens` nulls `PosBilling` and `Reports` unconditionally. **The fix that shipped is scoped to `Screen.VoucherEntry` per its brief and does NOT cover this.** | **[V] 2026-08-20**, throwaway `[AvaloniaFact]` driving the REAL MainWindow tunnel handler. Verbatim: one plain **F8** replaced a keyed bill of 3 × Rs 849.37 (cash tendered Rs 3,000) with a blank Sales entry — `notice='' message='' promptOpen=False`. The ALTERING half is worse because it also tears the report down: `isAltering=True rate='999.11' columns=3 reportsNull=False billTotal=1298.74` → `posNull=True notice='' promptOpen=False columns=2 reportsNull=True` — the amendment and the Day Book column both gone to one keystroke. | Unsaved keying and the operator's place in the report, lost to a key they pressed for another purpose. **FIX SHAPE, already named in the shipped guard's own doc comment:** a `HasUnsavedWork` on `PosBillingViewModel` plus a second arm in `MainWindowViewModel.OpenVoucherFromTypeKey`. **OPEN.** |
| **T1-25** | 🔴 **NEW 2026-09-04. EPS IS DEDUCTED FOR MEMBERS AGED 58 AND OVER, WHERE EPFO SAYS IT MUST NOT BE — A STATUTORY-FILING DEFECT, NOT A WRONG-NET-PAY ONE.** EPFO, verbatim: *"**Pension contribution not to be paid:** When an employee **crosses 58 years of age and is in service** (EPS membership ceases on completion of 58 years)… In both the cases the **Pension Contribution @8.33% is to be added to the Employer Share of PF**."* | **[V] 2026-09-04**, wave-2 statutory pass, EPFO *"PRESENT RATES OF CONTRIBUTION"* read with `pdftotext -layout` **and** `-raw`, outputs compared and in agreement. Shipped: `PfContribution.ComputeMember(decimal pfWages, bool contributeOnHigherWages, int epfRateBasisPoints)` takes **no age, no date of birth and no date**; zero hits for `58` / `pensionable` / `ceases` / `dateOfBirth` in the PF files, and both callers pass nothing age-related. | **Net pay and total employer cost are UNCHANGED** — the `EPS + EmployerEpf == EmployeeEpf` invariant holds either way — **so this is stated precisely rather than over-sold. What is wrong is the A/c 10 vs A/c 1 split on the challan and in the ECR file**, misallocating up to **₹1,250 per member per month** to the pension fund. (EDLI *is* still payable past 58 and we satisfy that by accident, never stopping it.) **Fidelity record: §1.3 item 17. OPEN.** |
| **T1-26** | 🔴 **NEW 2026-09-04. THE §192 SALARY-TAX ENGINE IS DATE-BLIND AND HAS NO MECHANISM TO EVER HOLD TWO YEARS — FY 2025-26 TABLES ARE APPLIED IN FY 2026-27.** `SalaryIncomeTax.ComputeAnnual(decimal, TaxRegime, AgeBand)` takes **no date parameter**; the slabs, surcharge bands, cess rate, both standard deductions and both §87A ceilings are bare `const`s with **no effective-from** (zero hits for `effectiveFrom\|financialYear\|FyStart` in that file). The callers **do** hold the payroll date and use it only for the age band and months-remaining — **never to select a table**. | **[V] 2026-09-04**, wave-2 statutory pass, `src/Apex.Ledger/Services/SalaryIncomeTax.cs`. **Is it currently WRONG MONEY? NOT PROVEN EITHER WAY, AND THAT IS THE FINDING** — the Department publishes **no AY 2027-28 column at all**, so the forward figures could not be retrieved to compare (UNREACHED, §6a). The code's own remark already says FY 2026-27 onward is unconfirmed. | **Today is FY 2026-27 and a September 2026 payroll run silently gets the FY 2025-26 tables.** The moment FY 2026-27 rates are published and differ, the product is wrong **with no gate, no warning and no version to switch**. Compare `SeedTdsTcsRates`, which **does** carry an effective-from and a legacy cutoff; salary IT has neither. ⚠️ **And row 6.39's gap sentence is now imprecise in a way that matters** — the cess *is* sourced for the year the tables encode; what is unsourced is the **forward** year, and the real defect is the missing year dimension. **Re-cut 6.39's gap text along this row. OPEN.** |

| **T1-27** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). FOUR WHOLE VOUCHER FAMILIES FALL THROUGH THE FLOOR OF THE NAMED-REFUSAL MACHINERY: `Ctrl+Enter` ON ANY JOB WORK OR MATERIAL REGISTER ROW IS A SILENT NO-OP, NOT A NAMED REFUSAL.** `RequestAlterHighlightedVoucher` resolves the row through `Company.FindVoucher(id)`, which searches `_vouchers` — the **accounting** collection — only; Job Work orders and Material movements are `InventoryVoucher`s in `_inventoryVouchers`. The method returns `NoVoucherHere`, which the file's own documentation defines as *"a quiet no-op."* | **[V] 2026-09-04.** `Company.cs:1091` (`_vouchers`) vs `:1130` (`_inventoryVouchers`); `InventoryVoucher` appears nowhere in `MainWindowViewModel`'s alteration path. 🔴 **This is the DEAD-GUARD class exactly**: the same file spends thirty lines (`:5791-5797`, `:5811-5817`) explaining that a refusal must be **named** on the notice bar precisely so a refused keystroke is not mistaken for a dead key — **and these four families never reach that machinery at all.** The refusal code is present, correct, tested and unreachable for them. | An operator pressing the alteration chord on a mis-keyed job-work order gets **nothing** — no correction, and no sentence telling them why. **Fix direction:** resolve inventory vouchers too and return `Refused` with a family-named sentence. **This also CORRECTS row 9.2's gap cell**, which blamed *"no voucher alteration anywhere (5.1)"* — stale since S5a–S5e. **OPEN.** |
| **T1-28** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). WE SHIP FOUR OF THE VENDOR'S ELEVEN JOB WORK REPORTS, AND THE MISSING ONE THAT MATTERS IS THE STATUTORY RECONCILIATION.** Census row **9.3 moved `COMPLETE` → `PARTIAL`** on this. | **[V] 2026-09-04.** Four builders in `JobWorkReports.cs` and five `MenuItemViewModel`s (one header + four rows). Per-name case-insensitive grep over all of `src/` → **0** for each of *Job Work Orders Summary*, *Components Order Summary*, *Material Movement Register*, *Stock With Job Worker*, *Stock from Party*, *Issue Variance*, *Receipt Variance*. | The **Material Movement Register** is the dispatch/receipt reconciliation carrying **Shortages, Wastage/Scrap and Duty Paid** — and it is the only Job Work register for which the vendor publishes a column list. **We have no shortage/wastage/scrap concept on a job-work movement at all.** Secondary: the family is nested inside Inventory Reports where the vendor makes Job Work Reports a **sibling** of Accounts Books. ✅ **What AGREES: the order-book pending arithmetic matches the vendor's Balance Quantity.** **OPEN.** |
| **T1-29** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 20). THE e-WAY BILL JSON WE WRITE CANNOT BE FILED — MEASURED AGAINST THE NIC EWB-01 v1.03 JSON SCHEMA, NOT INFERRED.** `EWayBillJson.BuildEwb01` emits a faithful structured payload **of our own design**. | **[V] 2026-09-04**, schema retrieved from `docs.ewaybillgst.gov.in` **through the browser pane** (it 403s WebFetch *and* curl) including the `required` array verbatim. Measured: **6 of 17** `required` keys absent · **0 of 10** `itemList` key names in the schema · all **7** mandatory main-object value fields absent · `docDate` written `yyyy-MM-dd` against a `DD/MM/YYYY` pattern · `transMode` / `transDistance` typed as numbers where the schema says string. **Independent of T0-17.** | A business that generates the offline JSON and takes it to the portal is rejected. 🔴 **This CLOSES the open `R7 (A14 to confirm)` flag written into that file and addressed to A14 by name — with a NEGATIVE answer.** Row 6.15's evidence cell over-credited the artefact by calling it *"EWB-01 offline JSON"*; corrected in §1.2a. **OPEN.** |
| **T1-30** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). MULTI ADDRESS IS A GST-CORRECTNESS DEPENDENCY, NOT A CONVENIENCE FEATURE — A PARTY BILLED IN ONE STATE AND SHIPPED IN ANOTHER CANNOT BE REPRESENTED IN THIS PRODUCT AT ALL.** | **[V] 2026-09-04.** `LedgerMasterViewModel.cs:429` `_mailingAddress` — **one string**, bound once at `MainWindow.axaml:4621`, written once at `:966`; grep for `multiaddress\|multi address\|multi-address\|addressbook` → **0**. The vendor's per-address block carries **Statutory & Taxation Information — a GSTIN per address** — and a voucher picks **Bill to** (Buyer) and **Ship to** (Consignee) **independently**. | Under GST the **place of supply for goods follows the ship-to address**, so bill-to ≠ ship-to is precisely the case that decides **CGST+SGST versus IGST**. Our single flat block cannot express it, so that supply is taxed on the wrong head with no way to correct it. **Raises the priority of census row 10.2 out of "master-layer convenience".** Read alongside **T0-18 / T0-19 / T0-20**. **OPEN.** |
| **T1-31** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 19). A NEW *PRIMARY* ACCOUNTING GROUP CANNOT BE CREATED AT ALL, WHICH MAKES CENSUS ROW 2.2 UNCLOSEABLE BY CONSTRUCTION.** | **[V] 2026-09-04.** `AccountGroupMasterViewModel.cs:210-219` — `ParentOptions` holds only **existing** groups; `:178-182` and `GroupService.cs:53-55` both **refuse a null parent**. The two flags row 2.2 is graded `ABSENT` on — *Nature of Group* and the newly-attested *Does it affect Gross Profits* — live on the primary-group screen. | **Row 2.2 can never be closed without changing this first**, so any slice written against 2.2 alone will fail. ⚠️ **And it is an inconsistency inside one application:** our own Stock Group and Godown masters **do** offer `Primary`. **OPEN.** |
| **T1-32** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). *LEDGER MONTHLY SUMMARY* IS A MISSING PRIMITIVE, NOT A MISSING REPORT — AND FIVE CENSUS ROWS ARE EACH INDEPENDENTLY BLOCKED ON IT.** | **[V] 2026-09-04.** `grep -rn "Monthly Summary"` over `src/` → **0**. It is the required intermediate level of **11.5** (Cash/Bank/Ledger — the vendor opens it where we open the voucher list), **11.6** (the register shape), **11.7** (Group Summary's drill path) and **11.12** (Godown Summary's `Ctrl+H`), and its absence is why **11.10** has no Cost Centre Monthly Summary. | 🔴 **The census currently writes this as five independent per-row gaps. Build it once and five rows move.** Recorded here as a single tracked item so a planner does not schedule five builds for one primitive. **OPEN.** |
| **T1-33** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 22). EVERY `.eml` THIS APP HAS EVER WRITTEN CARRIES AN UNDELIVERABLE `From:` — AND THE OPERATOR'S REAL ADDRESS IS SITTING IN THE DATABASE, UNREAD.** | **[V] 2026-09-04.** `EmailComposeViewModel.cs:123`: `_from = from ?? new EmlAddress("no-reply@apexsolutions.example", "Apex Solutions")`. The `from` parameter exists **only** on the *testable* ctor (`:111-116`); **both** production call sites (`MainWindowViewModel.cs:2868`, `:2870`) use the convenience ctors and never pass it. `SmtpProfile` carries `FromAddress`/`FromName`, is persisted, and its **only** reader in `src/` is the settings screen re-loading it for editing (`SmtpSettingsViewModel.cs:58`). | `apexsolutions.example` is an **RFC 2606 reserved domain no mail client can send from**, so the hand-off file is unusable *even when the operator has typed and saved their real address*. ⚠️ **This is NOT census row 13.7's *"nothing is sent"* gap** — the offline hand-off that **does** exist is broken, **and it stays broken the day live SMTP is built**, because the same value flows into the send path. **OPEN.** |
| **T1-34** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). A JOB-WORK ROLE FLAG IS STAMPED WITH THE WRONG ANSWER FOR ONE OF THE TWO SUPPORTED ROLES, AND NOTHING IN THE PRODUCT CAN UN-STAMP IT.** `JobWorkService.cs:53-54` sets `UseForJobWork = true` on **both** Material types whenever the feature is switched on; the vendor's rule is **Yes for the job worker, No for the principal manufacturer**. | **[V] 2026-09-04**, vendor *Set Up TallyPrime for Job Work Orders*. Persisted at `SqliteCompanyStore.cs:5456` and round-tripped through `CanonicalXml.cs:462`. | **Every principal-manufacturer book created in this app is stamped with the job worker's answer**, and there is **no remedy inside the product** — no voucher-type master screen exists (T1-3), and the only other writer is the canonical importer. It reaches **exported** books too, so the wrong flag travels. **OPEN.** |

### TIER 2 — CAPABILITY GAPS WITH NO WRONG FIGURE

| ID | Gap | Evidence |
|---|---|---|
| T2-1 | **Missing report families:** Group Summary; Sales / Purchase / Journal / Credit Note / Debit Note Registers; Statistics; Stock Query; Movement Analysis; Bills Pending; Stock Ageing; Optional / Post-Dated / Cancelled Voucher registers; Cash Flow Projection. | Per-name greps return 0 files each. The three `JournalRegister` hits are all `ReversingJournalRegister`. Note D20 records the CN/DN Register as the corpus-prescribed review path. |
| T2-2 | **Statutory long tail:** ~15 further TDS sections (193, 194, 194B/BB, 194D/DA, 194G, 194IA/IB/IC, 194K, 194LA, 194M, 194N, 194O, 194R, 194S, 195); §206C(1G); Form 27Q; §197 certificates; §234E and §201(1A); Form 16B/26QB; ISD; multi-GSTIN; PF Forms 3A/5/6A/10/12A; ESI Forms 3/5/6; NPS pay head; Labour Welfare Fund; Form 12BA; LUT / shipping bill; Bill of Entry; GST refunds (RFD-01/11). | Per-term greps in the statutory mapping brief. This is tonnage, not difficulty — and it is most of the tonnage. |
| T2-3 | **Master-layer gaps:** Credit Limits; Multi Address (company and ledger); per-item Alternate Units; cost-centre Budgets; Group behavioural flags (sub-ledger, nett debit/credit, used-for-calculation, allocation method — Tally field names **UNVERIFIED** against this corpus); GST Classification master; Show Inactive; multi-master create (Multi Ledger / Multi Group / Multi Stock Item); ledger Alias in the UI; `Inventory values are affected?`. | Zero grep hits for each identifier. |
| T2-4 | **Print engine floor.** `PdfWriter` has only `Text` and `Line`, standard-14 Helvetica, WinAnsi/Latin1. No image or raster primitive, no font embedding. | `PdfWriter.cs:30-70, 83-84, 97, 128-135`. **Consequence: no logo, no QR, no barcode, no non-Latin script, no JPEG export — ever — without replacing the writer.** Collides with the no-NuGet DP-2 constraint. |
| T2-5 | **No physical printing.** Zero hits for `PrintDialog`, `PrinterSettings`, `System.Drawing.Printing`, `winspool`. "Print" means render-a-PDF-and-save-a-file. | Disclosed at plan.md:378, but the P key, the `Screen.PrintPreview` name and the button label all read as printing. |
| T2-6 | **Export/import format gaps:** report export is Csv/Xlsx/Pdf only (Tally offers 7); no HTML, JPEG, XML, JSON or ASCII for a report; no Tally-XML or SDF reader so no third-party Tally data can be ingested; no Excel import; no live SMTP send. | `ExportConfig.cs:5-10`; `ImportDataViewModel.cs:14-23`. HTML and SMTP disclosed as deferred at plan.md:388-391. |
| T2-7 | **Missing TallyPrime UX:** Go To (Alt+G) and Switch To (Ctrl+G); graphical dashboard / any chart at all; More Details; the standard report button bar (Change View, Basis of Values, Monthly Summary, Value Range, Scale Factor, Vertical Balance, number-of-decimals, Alt+U Unhide). | Zero `Key.G` occurrences in `src/Apex.Desktop`; zero `BarChart`/`PieChart`/`graphical`/`MoreDetails` hits; per-term greps return 0 for the button bar. |
| T2-8 | **Keyboard: KB-3 prefix type-to-filter is not built.** Three design rounds NOT-READY; no filtering code in `src/` at all. S5 shipped type-to-*jump*. | plan.md:551-557. Lowest business harm in the register, and correctly last. |
| T2-9 | 🔴 **NEW 2026-08-18. Complete engine verbs with ZERO production callers — the T1-14 dead-field shape, one layer up.** (a) The **memorandum conversion verb** (the catalog's "a memo becomes a real voucher"): the shell method has zero production callers and **its own gate — whose doc comment says it "drives the Convert action" — has zero consumers anywhere**: no button-bar item, no key arm, no XAML binding. The only callers are a test file, so **a dead feature has a live test**. (b) **DRC-03**: the posting method and its record type are complete and `Drc03` returns zero hits across all of `src/Apex.Desktop`. (c) **Consolidated e-Way Bill (EWB-02)**: the prepare method has zero Desktop callers. (d) **The inventory cancel and delete** methods (see T1-17). (e) The e-mail **mailto URI**, computed and bound nowhere. (f) **Seven inventory master delete services**, zero Desktop callers each. | Survey-measured at HEAD. §1.2a rows 4.17, 6.20, 6.15, 11.4, 13.7 and 3.1–3.12. **The pattern, not the list, is the finding:** a verb that exists, is tested, and cannot be reached is indistinguishable from a shipped feature in every artefact except the running app. |
| T2-10 | 🔴 **NEW 2026-08-18. Three surfaces exist and cannot be found from a menu, and one Gateway hint points at the wrong screen.** **Import** and **Export Data** have **no menu row** — the Gateway's "Data" header carries exactly one child, Backup / Restore — and are reachable only by a bare-key arm on the Gateway root. **Saved Views** has no menu row either: the only two hits for the label in the shell are doc comments, so the list is reachable only by pressing Alt+K while already standing on a report-kind surface. And the Gateway header hint reads **"Y: Data"** while **bare Y opens Export Data** and **Alt+Y opens Backup / Restore** — the one hint the screen gives for the data surface names the wrong one. | Survey-measured at HEAD. §1.2a rows 13.3, 13.4 and 14.7. | 
| T2-11 | 🔴 **NEW 2026-08-20. The SALES ITEM INVOICE is alterable by no key on any screen, against an attested corpus route** — a ruling-9 **category (b)** divergence, and the first record anywhere that names it as such. Refused by name on the accounting door and again on the POS door; plain Enter reaches only the read-only column. **Blocked on a schema column** for the list rate and the price-level discount (the posted rate is the *effective* rate). ⚠️ **The tempting narrowing is a trap and is recorded as one:** the arm was NOT narrowed to *"the multiple-price-levels flag is on"*, because that flag is LIVE and reading it to judge a voucher posted months ago is the master-drift defect this phase has already shipped twice. | **[V] 2026-08-20.** Corpus: STUDY GUIDE printed **p.281** (*"select any Sale Invoice and press Enter"* / *"Sales Invoice alteration screen will appear"*, `-raw`) and the Book's section-terminal *"How to Show/Edit Sale Voucher Entry … Sale Register > Select Month & Show/Edit Entry"* closing a Sale (F8) section that covers Item Invoice, Accounting Invoice and As Voucher modes. Code: `VoucherAlterationEligibility.EntryModeRefusal`. Full record: §1.3 item 12. **The user has FULL schema authority (§5 ruling), so lifting this is theirs to authorise.** |
| T2-12 | 🔴 **NEW 2026-08-20. Both attested `Ctrl+Enter` MASTER limbs are unbuilt — *"to alter a master during voucher entry or from drilldown of a report"*, and the product has neither.** The only `Ctrl+Enter` master arm is gated on the stock-item master screen (a creation list, not a report drilldown), and there is **no `Ctrl+Enter` arm on the voucher-entry screen at all**, so **no inline master alteration from a voucher field exists anywhere**. That second limb is the substantive missing feature. | **[V] 2026-08-20.** BOOK PDF p.436 [printed p.432], `-raw`. ✅ **Master alteration is NOT unreachable** — plain Enter on the Chart of Accounts opens Ledger or Group Alteration (`MainWindowViewModel.AlterHighlightedChartRow`), so this is a missing route on an attested chord, not a missing verb. ✅ **And S5d does not shadow it:** its arm returns `NoVoucherHere` and does not consume the key on a non-voucher row, pinned by its own shipped test, so the chord is still free on exactly the Trial-Balance / Chart rows a master arm would claim. **Do not "fix" this by unbinding S5d's chord** — see §1.3 item 12's Short-Key bullet. |
| T2-13 | 🔴 **NEW 2026-08-20. The corpus's one attested F-key CONVERSION on an alteration screen is built and unreachable, and only the work-loss half of the defect was fixed.** BOOK, verbatim: *"Click on Payment (F5) button provided at memorandum alteration screen"* / *"The voucher will converted as payment voucher with same entry."* `ConvertMemorandum` exists in `MainWindowViewModel` with **zero production callers** and no key or button route. | **[V] 2026-08-20**, corpus re-extracted with `-raw`. The 2026-08-20 fix made plain F4–F9 stop silently discarding an unsaved entry or alteration on `Screen.VoucherEntry`; it deliberately did **not** implement the conversion, which is a feature. **This is the same verb T2-9(a) already names** — recorded again here because T2-9 files it as a dead verb and this row files it as an **owed corpus behaviour on the alteration screen**, which is what makes it a step-5a obligation rather than dead code. |
| T2-14 | 🔴 **NEW 2026-09-04. `Ctrl+I` IS BOUND TO THE WRONG VERB, AND THE CHORD IT SHOULD CARRY IS THE ONE FEATURE THE BINDING BLOCKS.** Officially `Ctrl+I` = *"To add more details to a master or voucher for the current instance"* (Right button) — the **More Details** panel. We spend it on the item-invoice mode toggle at `src/Apex.Desktop/Views/MainWindow.axaml.cs:744-750`. | **[V] 2026-09-04**, wave-2 core-accounting pass, official vendor shortcut page, action text verbatim, **corroborated a second time on the vendor domain** because the claim is load-bearing. **The mode toggle already has its correct chord** — `Ctrl+H`, *"To change mode – open vouchers in different modes"*, shipped correctly at `src/Apex.Desktop/Views/MainWindow.axaml.cs:759`. | **So the binding buys nothing and costs a feature**: mode switching is bound twice and the real `Ctrl+I` verb is unreachable. Census row 5.12 is graded COMPLETE on the sentence *"Ctrl+I and Ctrl+H arms **verified**"* — **the grade may stand (mode switching IS reachable) but "verified" was the wrong word and is corrected there.** Row 14.4 is the More Details capability itself. **Chord ruling required — see U-6. OPEN.** |
| T2-15 | 🔴 **NEW 2026-09-04. TWO ATTESTED REPORT VERBS ARE PRE-EMPTED RATHER THAN MERELY OMITTED, WHICH MAKES BUILDING THEM A CHORD CONFLICT INSTEAD OF AN ADDITION.** `Alt+I` = *"To insert a voucher in a report"* is spent on the POS Single/Multi tender toggle (`src/Apex.Desktop/Views/MainWindow.axaml.cs:766-774`); `Alt+A` = *"To add a voucher in a report"* is **third in arbitration** behind POS tax-analysis (`:778`) and Outstandings settlement (`:803`), reaching the attested verb only at `:815` and only on the Day Book. | **[V] 2026-09-04**, wave-2 core-accounting pass; both action texts verbatim from the vendor shortcut page and **each confirmed a second time on the Day Book page** (*"Inserting transactions in chronological order (Alt+I)"*). `InsertVoucher`/`RequestInsert` over `src/` → **zero** (the only matches are unrelated persistence method names). **`Alt+2` Duplicate is likewise attested twice and wholly absent** — `Key.D2`/`Key.NumPad2` over `src/Apex.Desktop/` → zero; `DuplicateVoucher`/`RequestDuplicate` over `src/` → zero. | Census row 5.5 records that *"Alt+I is spent on the POS tender-mode toggle"*, which is exact, **but the consequence is not recorded and it is the part that bites**: the attested verb is not merely unbuilt, its chord is taken by something no source knows about. Row 5.6 records `Alt+A`'s narrowing but not its **shadowing** — widening it to Outstandings collides with an arm that already owns that screen. **Three separate chord collisions were found in one wave (this, T2-14, and Alt+K at 14.9), which argues for ONE chord-map ruling rather than three piecemeal answers — U-6. OPEN.** |
| T2-16 | 🔴 **NEW 2026-09-04. TWO ATTESTED VOUCHER-NUMBERING METHODS HAVE NO DOMAIN MEMBER AT ALL, WHICH RESIZES T1-5 FROM A UI JOB TO A MIGRATION.** The official *"Method of Voucher Numbering"* field offers five: *Automatic* · **Automatic (Manual Override)** · *Manual* · **Multi-User Auto** · *None*. `src/Apex.Ledger/Domain/NumberingMethod.cs` has exactly three. | **[V] 2026-09-04**, wave-2 core-accounting pass, the vendor's dedicated numbering-methods page **and** its Voucher Type master page, both read. Enum read in full. | **T1-5 says Manual and None are *unreachable*, which is accurate as far as it goes.** The two further methods are **not unreachable, they do not exist** — so building row 5.10 is an **enum + persistence + migration** change, not "add a picker", and row 2.5's `MethodDisplay` gap inherits the same correction. ⚠️ **One negative result recorded because it PREVENTED a false divergence:** `None` **is** attested — the master page omits it, the numbering-methods page carries it — and stopping at the first page would have filed a correct behaviour as ours. **OPEN.** |
| T2-17 | 🔴 **NEW 2026-09-04. `Ctrl+E` — THE SINGLE MOST-DOCUMENTED EXPORT GESTURE IN THE REFERENCE PRODUCT — IS INERT ON EVERY REPORT, AND `Alt+E` IS DOING ITS JOB.** Officially two chords, two functions: `Ctrl+E` = *"To export the current voucher or report"*; `Alt+E` = *"To open the export menu for exporting masters, transactions, or reports"* (one current object, one bulk set). | **[V] 2026-09-04**, wave-2 reports/printing pass, **by ENUMERATING BOTH `Key.E` handlers in `src/Apex.Desktop`, not by finding one** — which is what makes *inert* safe to assert rather than merely *not-found-yet*. `src/Apex.Desktop/Views/MainWindow.axaml.cs:858` excludes **Control** and says nothing about **Alt**, so it fires on bare `E` and on `Alt+E` and opens the **current-object** export; `src/Apex.Desktop/Views/MainWindow.axaml.cs:889` binds `Ctrl+E` to `ExamineRestore()` **and only on the Restore screen**. Corroborated on the vendor Export-Data page. | On a report page `Ctrl+E` matches neither arm and falls through the chain. **The attested bulk-export menu does not exist and its chord has been consumed; bare `E` is an unattested third binding of ours.** There is **no recorded decision** to re-map these, so this is a defect, not a divergence-by-design — and **census row 13.5's own title propagated the wrong pairing into the census** until it was corrected. `Ctrl+E` for *Examine Restore* is a genuinely useful local binding **sitting on a globally-attested chord**. *(Not asserted: the running app was not driven.)* **OPEN.** |
| T2-18 | 🔴 **NEW 2026-09-04. `Alt+C` / `Alt+N` INVERT THE REFERENCE RULE — OURS IS AN INCLUSION LIST OF FOUR, THE SOURCE'S IS AN EXCLUSION LIST OF TWO — SO THE TARGET IS "INVERT THE MAP", NOT "ADD MORE KINDS TO IT".** Officially both work on reports generally *"(**except in Day Book and GST Returns**)"*. Ours fire only on a supports-comparative predicate whose own code comment names the set *"(TB / BS / P&L / Stock Summary)"*. | **[V] 2026-09-04**, wave-2 reports/printing pass, both action texts verbatim. Census row 11.17's *"the comparative map covers 4 of the 45 kinds"* is **CONFIRMED** by this pass. **A concrete instance proving the inversion matters:** the official **Statistics** report page documents `Alt+N` (Auto Column) working **on Statistics** — a report that is neither a Day Book nor a GST Return, and one our inclusion list would still exclude even if it existed (it does not — row 11.8). | Also in this family and recorded here so the register holds them: **`Alt+F2` changes the COMPANY's period officially and only the REPORT's window in ours** — census row 11.16's *"Alt+F2 period"* wording reads as a match and hides the difference, which is exactly the near-match failure mode item 12 diagnoses; **`Ctrl+H` on a report is officially *"change VIEW"* and ours is bound only for voucher change MODE**; and **six attested chords are unbound** — `Alt+G`, `Ctrl+G`, `Alt+U`, `Ctrl+B`, `Ctrl+F12`, `Alt+P`. **Fidelity record: §1.3 item 18. OPEN.** |
| T2-19 | 🔴 **NEW 2026-09-04. THE TWO TIMBER TCS MASTER NAMES DISAGREE WITH THE YEAR-2025 STATUTE IN OPPOSITE DIRECTIONS. NO MONEY MOVES; SELECTION GUIDANCE DOES.** §206C(1) Table item **(iii)** is *"Timber **or any other forest produce (not being tendu leaves)** obtained under a forest lease"*; item **(iv)** is *"**Timber** obtained by any mode other than under a forest lease"*; the old standalone forest-produce entry (v) is now omitted. | **[V] 2026-09-04**, wave-2 statutory pass, bare §206C at its Year-2025 slug. Shipped in `src/Apex.Ledger/Seed/SeedTdsTcsRates.cs`: `6CB` = *"Timber obtained under forest lease"* — **understates (iii)** by dropping the forest-produce limb; `6CC` = *"Timber/forest produce (other than forest lease)"* — **overstates (iv)**, which covers timber only. Both are 2%, which the same pass verified. | An operator collecting on **non-timber forest produce under a lease** has no master whose *name* tells them `6CB` is the right one; one collecting on it **outside** a lease is invited by `6CC`'s name to use a code the statute does not extend there. **Cheap fix, no schema change: rename to the statutory words.** **OPEN.** |

| T2-20 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 22). THE REFERENCE PRODUCT HAS THREE TOP-LEVEL OUTPUT MENUS AND WE ARE 0-FOR-3 — AND THIS REORGANISES THE PLAN FOR TWO WHOLE AREAS.** `Alt+P` (print menu) **unbound** — enumerated: two `Key.P` handlers, neither matches Alt. `Alt+E` (export menu) **bound to the wrong function** — it fires the *current-object* export at `MainWindow.axaml.cs:858`, which is `Ctrl+E`'s job, leaving `Ctrl+E` inert (that is T2-17). `Alt+M` (Share menu) **unbound** — `grep -rn "Key\.M\b" src/Apex.Desktop` returns **exactly one line** and it excludes Alt. | **[V] 2026-09-04**, vendor shortcut table parsed **cell-by-cell** (Action · Key · Location · Parent), not off a flattened line — a flattened read of that table shifts every key one row and would have mis-attributed all six chords. **The pattern is now fully characterised: we built the three CURRENT-OBJECT verbs (`Ctrl+P`, `Ctrl+E`, `Ctrl+M`), none of the three BULK menus, and spent one bulk chord covering for a missing current-object chord.** 🔴 **PLANNING CONSEQUENCE: nine census rows — 12.3, 12.4, 12.5, 12.6, 12.7, 13.3, 13.5, 13.6, 13.7 — are written as nine independent builds. They are ONE shared menu shell + ONE shared `Others` report list + per-leaf configuration.** The wave-2 build order 12.8 → 12.5 → 12.4 is correct and **gains a predecessor: the menu shell is upstream of all of it.** Row 12.7's reminder letters and confirmations of accounts are **multi-account outputs of these menus**, not three standalone documents. |
| T2-21 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 19). PREFIX/SUFFIX AFFIXES AND PREVENT-DUPLICATE ARE OFFERED ON NUMBERING METHODS THE VENDOR EXCLUDES THEM FROM.** | **[V] 2026-09-04.** `VoucherNumberingConfigViewModel.cs:119` and the `PreventDuplicate` comment at `:117-119`, against the vendor's Voucher Type page: affixes are restricted to *Automatic* / *Automatic (Manual Override)* / *Multi-User Auto*, and Prevent-duplicate to the latter two. **Harmless TODAY only because no method is selectable at all (row 2.5 / T1-5) — it becomes live the moment T1-5 is fixed, so it must be fixed with it.** |
| T2-22 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 20). GSTR-2B RECONCILIATION SHIPS FOUR BUCKETS AGAINST THE VENDOR'S NINE, AND REVERSE-CHARGE PORTAL LINES LAND IN NO BUCKET AT ALL.** | **[V] 2026-09-04.** `Gstr2bReconciler` excludes RCM portal lines symmetrically — **which is correct** — but they then appear nowhere, so a user cannot distinguish a **deliberately set-aside** 2B line from one that **was never in the file**. The vendor has a named bucket for exactly this: *"Excluded, but available on Portal."* There is also no *Excluding Party GSTIN* near-match bucket. **Census row 6.16 stays `COMPLETE` on §1.1's existence test; a fidelity caveat was added to its cell instead.** |
| T2-23 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). CASH BOOK / BANK BOOK / LEDGER HAVE NO PRINT, NO EXPORT AND NO REPORT PARAMETERS — BECAUSE THE FAMILY OPENS AS A DRILL COLUMN, NOT A REPORT.** | **[V] 2026-09-04.** `MainWindowViewModel.cs:1767` (`OpenAccountBook` never sets `Reports`), `:2264`, `:2366-2367` (`IsReportContext` excludes `Screen.LedgerVouchers` **by name**, which also makes `IsPrintablePage` false), `:2398`. The vendor documents `F12` options on this report. **The family's FIRST LEVEL is also wrong** — the vendor opens a Ledger Monthly Summary where we open the voucher list (T1-32). |
| T2-24 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). OUTSTANDINGS AGEING IS HARD-WIRED TO BY-DUE-DATE; THE VENDOR OFFERS BY-BILL-DATE AS WELL, AND THE BUCKETS ARE NOT A PARAMETER.** | **[V] 2026-09-04.** The bucket set is a `static readonly` field with no parameter. ⚠️ **And a correction to a first draft of this finding, kept because it is the failure this register exists to prevent:** *"no Settle Bill from the report"* is **FALSE** — Settle Bills ships on `Alt+A` and on a visible button; the vendor's chord is `Alt+B`, and `Alt+A` is not free here either (Day Book's Add Voucher). **That half is a chord-map question for ruling U-6, not a features gap.** |
| T2-25 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). `CostReports.BuildLedgerBreakup` IS A FULLY IMPLEMENTED, FULLY TESTED REPORT THAT NO PRODUCTION CODE PATH CAN REACH.** | **[V] 2026-09-04.** `grep -rn "BuildLedgerBreakup" --include=*.cs src tests` → **1 definition, 4 test references, 0 `src/` callers**; `CostReportKind` has two members and the builder is the third. **The T1-14 dead-field shape, one layer up — and a green suite proves nothing about it, because the tests exercise the builder directly.** |
| T2-26 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). BUDGET VARIANCE HAS NO DEDICATED SCREEN IN THE REFERENCE PRODUCT — IT IS `Alt+B` *ON* TRIAL BALANCE / GROUP SUMMARY — SO ROW 11.11's SIX MISSING GESTURES ARE A SHAPE PROBLEM, NOT SIX FEATURES.** | **[V] 2026-09-04**, vendor Budgets page. **Fixing the SHAPE delivers five of the six gestures for free; adding them to our dedicated screen would ship a surface the vendor does not have.** Filed because building the row as written would move the product *away* from parity while closing a census gap. |
| T2-27 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). `Ctrl+J` — THE VENDOR'S IN-REPORT ENTRY POINT TO THE EXCEPTION-REPORT FAMILY — DOES NOT EXIST ANYWHERE IN THE PRODUCT.** | **[V] 2026-09-04.** `grep -rn "Ctrl+J"` over `src/` → **0**. The vendor documents it from Stock Summary, Funds Flow and Interest Calculation. **Row 11.13's exception family is reachable, if at all, only by its own menu rows.** ⚠️ **Honest source limit: the family MEMBERSHIP comparison (four of ~nine) rests on a Tally.ERP 9 page; no TallyPrime enumeration was found.** |
| T2-28 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). STOCK SUMMARY'S `Alt+F1` ROLLUP SILENTLY BLANKS THE QUANTITY AND RATE COLUMNS ON A KEYSTROKE THAT IS MEANT TO ROLL UP, NOT NARROW.** | **[V] 2026-09-04.** `ReportsViewModel.cs:1452-1462` (detailed branch fills `Col2`–`Col6`) versus `:1479` (group branch fills `Col1` and `Col6` only). **Four populated columns go blank and nothing says why.** |
| T2-29 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 21). CASH FLOW AND FUNDS FLOW BOTH SHIP THE VENDOR'S *DRILLED* LEVEL AS THEIR TOP LEVEL — THE MONTH-WISE DEFAULT IS ABSENT FROM BOTH, AND ROW 11.14's "no drill" IS A CONSEQUENCE OF THAT, NOT AN INDEPENDENT GAP.** | **[V] 2026-09-04**, vendor Cash Flow and Funds Flow pages. **A whole report LEVEL is missing from each**, which is why the drill has nowhere to come from. Row 11.14's gap sentence understated the finding by exactly that level; corrected in §1.2a. |
| T2-30 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 22). BACKUP IS `COMPLETE` FOR THE OPEN COMPANY ONLY — NO COMPANY PICKER, NO MULTI-COMPANY "All Items", NO PERSISTED BACKUP PATH.** | **[V] 2026-09-04.** `BackupCompanyViewModel` takes **one** `Company`; `OpenBackupCompany` (`:2978-2987`) returns early when `Company is null`; `DefaultFolder()` (`:130-134`) resets to Documents **every session** and nothing persists it. The vendor lists companies, offers **All Items**, and configures a **Company Backup Path** under `Alt+Y > Data Path Configuration`. **The grade correctly does not move — §1.2 grades existence — but "Backup — COMPLETE" reads as parity and is not.** |
| T2-31 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 22). OUR DATA MIGRATION IS A SILENT SIDE EFFECT OF OPENING A BOOK; THE VENDOR'S IS AN OPERATOR-RUN, PAUSABLE, RESUMABLE MENU ACTION WITH A PRE-FLIGHT CHECK, A CONFIGURATION SCREEN, A SUMMARY REPORT AND A RETAINED PRE-MIGRATION COPY.** | **[V] 2026-09-04**, vendor *Migrate Company Data* and *Manage Your Company Data*. Ours walks migrations upward on **every load**, in place. 🔴 **And there is NO retained copy on the migration path** — a `.apex-prerestore` safety copy exists only for *restore* (`RestoreCompanyViewModel.cs:189`) — **so a migration that goes wrong has nothing to go back to.** Row 13.9 stays `COMPLETE` against a capability statement that is **ours, not the vendor's**; a scope sentence was added to its cell. |
| T2-32 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). AN ATTESTED, ACCOUNTING-CORRECT LEDGER PLACEMENT IS REFUSED BY A THROW: `PosTenderService.cs:90-94` REQUIRES A GIFT-VOUCHER TENDER LEDGER UNDER *SUNDRY DEBTORS* AND RAISES `InvalidVoucherException` OTHERWISE; THE VENDOR PERMITS SUNDRY DEBTORS *OR* SUNDRY CREDITORS.** | **[V] 2026-09-04**, vendor POS page. **An unredeemed gift voucher is an obligation of the seller, so Sundry Creditors is the placement a competent accountant will choose — and our product blocks the sale at save with a message asserting a rule the reference product does not have.** Not a wrong figure; a correct entry made impossible. |
| T2-33 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). THE ENGINE SUPPORTS MULTI-ORDER FULFILMENT AND THE SCREEN THROWS IT AWAY.** `InventoryVoucher.OrderLinks` is a list, `JobWorkReports.MaterialRegisterRow` carries `IReadOnlyList<string> LinkedOrderNumbers`, and `ResolveLinkedOrderNumbers` loops over it — but `MaterialMovementEntryViewModel.cs:367` can only ever produce `new[] { ov.Id }`. | **[V] 2026-09-04.** The vendor spells the field **"Order No(s)"** — plural by design. **Dead capability below a narrower UI**, the T2-9 shape again. |
| T2-34 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 19). `F3` MEANS TWO DIFFERENT THINGS IN OUR OWN APPLICATION, AND THE VENDOR GIVES IT ONE.** | **[V] 2026-09-04.** `MainWindowViewModel.cs:825` = **Create Company**; `:1128` and `:8055` = **Change Company**. The vendor's shortcut table gives Create Company **no F-key** (it is `Alt+K > Create`) and gives `F3` one meaning. **Two meanings on one chord is a keyboard-first regression independent of fidelity** — folded into the single chord-map ruling **U-6**. |
| T2-35 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 19). UQC IS FREE TEXT, AND A TYPO PROPAGATES INTO A STATUTORY SUMMARY.** | **[V] 2026-09-04.** `UnitMasterViewModel.cs:67`, `:132` — an unconstrained string. The vendor's Stock Item page picks it *"from the list provided… as declared by the Government or GSTN"*. A mistyped UQC flows straight into the **GSTR-1 HSN/SAC summary** (`ReportsViewModel.cs:2094`). ⚠️ **Filed at TIER 2 rather than TIER 0 deliberately: no figure is wrong — a LABEL on a statutory summary is, and only if the operator mistypes.** |

| T2-36 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 23). AREA 9's REMAINING DIVERGENCES, GROUPED BECAUSE THEY SHARE ONE SHAPE: WE ANSWER FOR THE OPERATOR, OR WE ADD A PRECONDITION THE VENDOR DOES NOT HAVE.** (a) The vendor asks **three per-voucher-type questions** when Job Order Processing is switched on; **we answer all three ourselves** — the role question is answered *wrong* for the principal manufacturer (that is **T1-34**), and **`Allow consumption` is FORCED ON** although it is load-bearing on screen. (b) The **Manufacturing Journal** is gated behind a **company-level precondition the vendor does not impose**. (c) Two navigation placements diverge: **POS Register** is filed under Inventory where the vendor files it under **Accounts Books**, and **Job Work Reports** is nested inside Inventory Reports where the vendor makes it a **sibling** of Accounts Books. (d) One label-only divergence: our `"Tracking Components"` checkbox. | **[V] 2026-09-04**, vendor Job Work masters / manufacturing / POS / inventory-voucher pages. **Grouped rather than filed as five rows because they share one fix surface — the F11 job-work block and the menu tree — and because five rows would overstate the register against every earlier entry.** ⚠️ **None of them is a wrong figure; every one of them is a choice the reference product leaves to the operator and we make for them.** |
| T2-37 | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 19). AREAS 1–3's REMAINING MASTER-SCREEN DIVERGENCES, GROUPED FOR THE SAME REASON.** (a) **No named `Shut Company`** — zero `Shut` hits in `src/Apex.Desktop`; closing is a side effect of Esc collapsing the cascade, and the vendor's exact chord is now named. (b) **F11's six vendor section headings are not our structure** — ours is a flat page, which also collides with the standing "professional UI hierarchy" preference and with the vendor's *Show More / Show All Features* progressive disclosure (see `docs/invented-vs-cloned.md` **IV-58**). (c) **Ledger and Stock Group ALIAS** diverges, and one Stock Group label is *close but not the vendor's*. (d) **The Cost Category / Cost Centre and Unit-of-Measure masters ship fewer VERBS than the vendor documents** (Alter / Display / Delete). (e) **Our UoM master omits the vendor's UQC list constraint** (that is **T2-35**). (f) ~~**Company Alteration's read-only Name** is a storage constraint of ours, already in §1.3 item 9's "ours" list.~~ 🔴 **STRUCK 2026-09-05 (b4 landing, PR #47): THE NAME IS NO LONGER READ-ONLY.** `CompanyProfileViewModel.IsNameEditable` plus a real `TextBox` in the shipped `MainWindow.axaml` make it typeable, and `Ctrl+A` moves the `.db` — see census row **1.4**, now `COMPLETE`. **The storage constraint that justified the read-only field was removed, not worked around.** ⚠️ **§1.3 item 9's "ours" list still carries this and is now stale there too.** Sub-items (a)–(e) of this row are **unaffected and still open** — in particular (a), no named `Shut Company`, which the b4 work deliberately did **not** close: `ShutCompany()` survives only as a private `ReleaseOpenCompany()` with the company-delete as its single caller. | **[V] 2026-09-04**, vendor company / groups / ledgers / F11 / stock-item / godown / cost-centre pages, each **read as raw HTML and grepped, not from a WebFetch summary**. **Grouped rather than filed as six rows, on the same principle as T2-36.** ✅ **And what AGREES is recorded with them, because it is the other half of the measurement: nine of the 23 capabilities compared in these three areas AGREE, including six `ABSENT` grades confirmed against the vendor page — and `MasterDeletionRules`'s referential guard MOVED OUT of the "ours" column because the vendor attests it verbatim.** |

| T2-38 | 🔴 **NEW 2026-09-05 (b1 landing, PR #49). `PayHeadService` HAS NO `Alter` METHOD AT ALL — an ENGINE gap that blocks a capability from the view-model side no matter how much view-model work is done.** Census row **7.16** reached 4 of its 8 payroll-master kinds and stopped here: employee category, employee group, payroll unit and attendance/production type all alter and delete end-to-end, and **pay head cannot, because there is nothing behind the screen to call.** | **[code] 2026-09-05.** Distinguish this from the other three open kinds in 7.16: **employee** is merely unwired (`PayrollService.AlterEmployee` and `DeleteEmployee` both exist), while **salary structure** and **tax declaration** are *unscoped* (nobody has decided what "alter" means for them). **Pay head is the only one blocked FURTHER BACK than the UI**, so it needs an engine slice first and should not be estimated alongside the other three. Pinned by `PayrollMasterHalfWiredKindsTests`. |
| T2-39 | 🔴 **NEW 2026-09-05 (b4 landing, PR #47). A TREE-WIDE ALTER-FLOW DIVERGENCE, SURFACED AND DELIBERATELY NOT FIXED ON ONE BRANCH.** The vendor's Alter flow is **list → alter → accept → back to the list**. **All four** of our master families — ledger, account group, stock item and payroll — instead **leave the alteration screen open in Alter mode until `Escape`.** | **[V] 2026-09-05**, vendor master pages. 🔴 **The reason it was NOT fixed in the branch that found it is the point of the entry: changing it for payroll alone would create exactly the "one kind gated differently" divergence that `IPayrollMasterList` exists to PREVENT.** It needs one tree-wide decision on its own branch, applied to all four families at once. Filing it per-family would produce four rows for one fix and would invite exactly the piecemeal change that makes it worse. |
| T2-40 | 🔴 **NEW 2026-09-05 (b5 landing, PR #50). ~432 LINES OF MULTI-ACCOUNT PRINT CODE THAT NO USER CAN REACH, LEFT UNREACHED ON PURPOSE.** `MultiAccountPrintViewModel` has **zero references anywhere** — no `MainWindowViewModel` member, no menu route, no XAML template, no test — and `MultiAccountPrintProjector`'s only caller is that dead view model. Census rows **12.6** and **12.7** therefore stay `ABSENT`. | **[code] 2026-09-05.** ⚠️ **This is the same shape as `CompanyStorage.Rename()` and `CostReports.BuildLedgerBreakup` — careful, correct-looking, fully unreachable — and it is the third instance found on this project, so it is a PATTERN and not an accident.** The engine half is real and covered (`ReportPdf` over a document set, `MultiDocumentPrintTests.cs`). Remaining to close 12.6/12.7: a screen and panel member, a menu entry **nested under Reports → Statements of Accounts** (never a flat dump), a `DataTemplate`, key routing and a realised-control lock. **It was deliberately not wired in the landing pass rather than wired in a hurry**, because a row moved on a rushed wiring is exactly the over-claim this census is trying to stop. |

### TIER 3 — REGISTER AND PLAN FALSEHOODS

These harm the project rather than the business, but they are the reason nobody knew the size of the gap. Each needs correcting in place.

> ### 🔴 † 2026-08-15 — TWO FIGURES ATTRIBUTED TO THIS SECTION ARE NOT IN IT
> `plan.md:359` and `:365` state that **"the census found 34 false claims"** and **"nine internal
> contradictions"**, and point at *"Census §2 Tier 3"*. **Neither figure exists in this document.** Measured
> 2026-08-15: `grep -nE "\b34\b" docs/full-clone-census.md` → **zero hits**; `grep -i contradict` → **two**
> incidental hits inside T0-11 and T1-11, neither a count; **the table below has fifteen data rows, not 34 and
> not 9.** The *phase list* `plan.md` gives alongside those numbers **is** real and verifiable in this table
> (Phase 1, 2, 5, 9, 10.9) — **it is only the counts that are unsourced.** ⇒ **Do not repeat either number.**
> `plan.md` is the file that needs correcting, not this one; the exact replacement text is held for whoever owns
> that file. Also recorded at `memory.md:2164` and `memory.md:2185`.
>
> **† Three rows below have been acted on since this census was written (2026-08-10):**
> the **`docs/invented-vs-cloned.md` IV-19 "~50"** row and the **negative-stock "STOPPED AND BANKED"** row were
> both carried into the registers' 2026-08-15 re-verification and are corrected there. The **"24 predefined
> voucher types"** row is untouched — `SeedVoucherTypes.cs:71` still reads `public const int Count = 23`,
> re-measured 2026-08-15, and `plan.md` said 24 in nine places when this was written.
>
> **‡ 2026-08-15 (W0-15 review):** `plan.md`'s live, present-tense counts have since been corrected to 23, and the
> sites that remain at 24 are deliberate — one quotation made in order to retire it, and **CLOSED phases' historical
> records of the count as it stood when they shipped** (the Attendance seed row was deleted on 2026-08-03 by
> `7bfc2c6`, after Phase 10.7 had already shipped against it). Rewriting those would make the record false. Each is
> now exempted per-site, with its occurrence count, in
> `tests/Apex.Ledger.Tests/DocumentCodeAgreementTests.cs`, which fails on any NEW one.

| Claim | Reality |
|---|---|
| "24 predefined voucher types" — plan.md:40, 243, 260, 307, 607, 969, 1076; catalog:187, 519; `VoucherType.cs:6` | **[V]** `SeedVoucherTypes.cs:71` is `public const int Count = 23`. The Attendance row was deliberately removed (decision D24-B). The corpus says 24 for TallyPrime (BOOK p.17) — so this is a **real fidelity gap the docs are hiding**, not a typo. |
| `VoucherType.cs:6` "24 are seeded; custom types may be added" | Both halves false. 23 seeded, and there is no UI anywhere to add a custom type. |
| plan.md:334 Phase 1 (COMPLETE): "multi-create", "delete guards", "Alt+D/Alt+X" | Three falsehoods in one line. Multi-create: 0 hits. No ledger/group delete exists, so there are no delete guards. Alt+D unbound; Alt+X abandons an in-progress entry. |
| plan.md:54, 347 Phase 2 (COMPLETE): "cheque printing" | No renderer exists. Two persisted fields only. |
| `docs/phase5-reports-io-requirements.md:209` RQ-9: "SHALL render ANY REPORT" | **[V]** False by 42% — 32 of 77 surfaces are unprintable. |
| plan.md:378-388: "export PDF/XLSX/CSV/JSON/XML" | Conflates two unrelated surfaces. Report export is `{Csv, Xlsx, Pdf}`; JSON/XML belong to whole-company canonical backup. |
| plan.md:377: "graphical dashboard, More Details" delivered | Zero chart code, zero `MoreDetails` hits. Not previously in any register. |
| plan.md:376: "Account/Inventory Books" families | Ships Cash/Bank/Ledger only. Six registers and five inventory reports absent. |
| plan.md:423-428 Phase 9: "BoE / LUT / shipping bill / SEZ / deemed exports", "GSTR-9A", "per-tax-ledger rounding", "multi-GSTIN", "GSTR JSON" | None exist. GSTR-9A has an engine projection and no Screen, no ViewModel, no menu row. No path posts a Round-Off leg at all (`VoucherPrintProjector.cs:350-353`). |
| plan.md:1076 Phase 10.9: "every one of the 24 voucher types reachable by menu AND shortcut" | 23 types, and Payroll is unreachable on the two IsActive-filtered surfaces. |
| `docs/tally-version-and-voucher-gap-audit.md` §4.1: "masters present and wired to UI" | True for **Create only**. 24 of 27 have no Alter; none of the 27 has Delete or Display. **The single most misleading line in the existing registers.** |
| `docs/invented-vs-cloned.md` IV-19: "~50 reports are dead ends" | 71 of 77. Understates itself by ~40%. |
| plan.md 10.8: negative stock "STOPPED AND BANKED" | **A false claim of absence — the rarer and more dangerous kind.** `Company.WarnOnNegativeStock` shipped, persists and is honoured, with zero UI toggle. Behaviour changed and the register says nothing shipped. |
| gap-audit §4.6: "CN/DN have no menu row", "Ctrl+F7 unbound" | Both STALE/FIXED. Menu rows at `MainWindowViewModel.cs:1002-1003`; Ctrl+F7 bound at **†** `MainWindow.axaml.cs:765` *(was cited `:681`; corrected 2026-08-15 — `:681` is now the bare-E Export arm, and the Ctrl+F7 arm with its grounding comment is `:758-765`)*. |
| `PopulatedCompanyFixture` described as "51 vouchers of every type" | 51 is right; "every type" is not — 8 of 23 base types, zero inventory/order/job-work/POS/payroll vouchers. ⚠️ **CORRECTED 2026-08-17: this was true when the census was written and has been FALSE since `1de940e` (2026-08-10), which extended the fixture to post 23 of 23 SEEDED base kinds** — 8 accounting, 12 stock/order, 2 provisional, Payroll, plus a POS-flagged second Sales type and `AttendanceEntry` rows, with a `PopulatedFixtureCoverageTests` beside it. Re-derive from that test, never from this row. |
| 🔴 **NEW 2026-08-20 — THE FIDELITY GATE IS PROSE-CHECKED, NOT DERIVATION-CHECKED, AND THAT IS THE MECHANISM, NOT A LAPSE.** `plan.md` §2.2 step 5a says the count *"is maintained"* in §1.3 and *"do not copy the digits into this file"*. **S5d wrote a full, correctly-categorised R7 record — into `plan.md`.** **S5e wrote none at all** (`git diff --stat a34d989 b89213e -- plan.md` is empty; zero corpus citations across a 2,926-line diff). | A compliant author discharged the gate in substance and left every maintained figure in §1.3 and §1.2a stale. **No artefact failed, because no artefact exists that could.** Corrected in this pass (§1.2a row 5.1, §1.3 item 12, the anchor block, §1.2's column sums); **the mechanism is NOT corrected** — a slice can still do exactly this tomorrow. ⇒ **Owed: a step-5a artefact that fails.** |
| 🔴 **NEW 2026-08-20. `BookLevelRefusalFor`'s two doc comments asserted a call graph the code does not have** — *"named once here and consumed by `RefusalFor(Company, Voucher, VoucherType)` and by `PosAlterationEligibility` alike"*, and *"one list, consumed by both doors, so this one cannot quietly grow a weaker copy"*. | **Both false.** `BookLevelRefusalFor` had exactly ONE caller tree-wide; `RefusalFor` consumes the three private methods **directly**. What is single-sourced is the **ARMS**, not the **COMPOSITION**. ✅ **CORRECTED IN THE CODE 2026-08-20** — the comments now state arms-vs-composition and record the property a later author must keep (a fourth book-level arm goes INSIDE one of the three methods, never as a fourth method wired only into `RefusalFor`'s chain). ⚠️ **The sole call site was entirely unpinned** — on the one POS shape every existing test builds it returns null, so deleting it reddened nothing. Three constructed cases (live IRN, §34 CDN link, `ApplicableUpto`) exist and **should be added as tests**; that half is **still owed**. |
| 🔴 **NEW 2026-08-20. `InventoryVoucherLineViewModel`'s "backstop" doc comment claims a safety the code does not provide.** It says the closing effective-rate comparison *"would catch it here too"* if the door were widened, and that *"no assumption about which screens carry a discount is load-bearing in this method"*. | **Both false as stated.** `RehydrateFrom` never writes `DiscountText` and its caller suppresses the only code that would, so the parsed discount is 0 by construction and the comparison reduces to `posted.Rate == posted.Rate` on every input a discount could produce. The method's discount safety rests entirely on the **door** one file away (`VoucherAlterationEligibility.EntryModeRefusal`'s Sales arm). ⚠️ **But "dead in ALL futures" is ALSO wrong and must not go into a fix list that way** — a control run that pre-seeded a discount on the row got the refusal **by name**, so the comparison is live in exactly the future its own refusal sentence proposes (a schema column carrying the list rate and the discount). **Zero present money impact** — the harm path is unreachable while the door refuses Sales. **Owed: correct the comment; do not delete the guard.** |
| 🔴 **NEW 2026-08-20. The reachability invariant's "independent by-NAME cross-check" is not independent on the axis that can erode it.** Both its queries run through the same `Namespace == …ViewModels` gate, so a screen-shaped `ForAlter` declared outside that namespace is invisible to the lock AND to the cross-check simultaneously, and the non-vacuity assertion stays satisfied by the in-namespace factories regardless. | **Measured: 75 of 763 shipped `Apex.Desktop` types sit in that blind region** (`<global>`, `Apex.Desktop`, `.Converters`, `.Services`, `.Views`). Live escapes today: **zero**, so this is an **undisclosed limit**, not a live hole — the limit is absent from the class's own *"WHAT IT DOES NOT COVER"* list and from the mirrors of it in `plan.md` and `memory.md`, all three of which name only non-transitivity, reflection blindness and the one-assembly scope. ⚠️ **The one-line fix is NOT semantically free:** sourcing the by-name query from all `Apex.Desktop` types makes it red for any public static named `ForAlter` outside the namespace **including one that returns nothing screen-shaped**, so it needs its OWN assertion and its OWN message, not a reuse of the shape-erosion one. |
| 🔴 **NEW 2026-08-20. The only shell-driven POS accept-alteration proof cannot tell a COMMIT from a REFUSAL.** It closes on *one voucher, same id* — true on both paths, because the replacement is constructed with the existing id. | **Measured both ways in one run:** refused → `Message="Item … needs a rate greater than zero." SavedNumber=0 CurrentScreen=PosBilling vm.PosBilling=bound`; committed → `Message="Sales (POS) No. 1 altered." SavedNumber=1 CurrentScreen=Report vm.PosBilling=<null>`. **Both shipped assertions passed in both runs.** ⚠️ **THE ORIGINALLY-SUGGESTED FIX IS ITSELF BROKEN:** `Assert.Null(vm.PosBilling!.Message)` **NREs on the passing path**, because a successful alteration runs `BackFromPage()` and unbinds the screen. **Use `Assert.Null(vm.PosBilling)`, or capture the view model before the key and assert `SavedNumber`** — better still, amend a figure first and assert it moved. |
| 🔴 **NEW 2026-08-20 — A UI-CAMPAIGN CLAIM MEASURED AND FOUND FALSE, RECORDED SO IT IS NOT PROPAGATED.** The agent that fixed the notice-bar truncation also reported *"8 [`{Binding Message}` TextBlocks in `MainWindow.axaml`] carry neither [`TextWrapping` nor `TextTrimming`] (lines 10526, 11523, 11621, 11767, 11907, 11976, 12081, 12182)"*. | **FALSE, measured 2026-08-20 by parsing every `<TextBlock …>` opening tag in the file** (attribute-level, not line-level — the difference is the whole error): **59 `Text="{Binding Message…}"` TextBlocks, 59 of them carry `TextWrapping`, ZERO carry neither** — at HEAD `b89213e` and in the working tree alike. **All eight named lines carry `TextWrapping="Wrap"` verbatim.** The *"51 wrap / 8 neither"* split appears to be a line-level grep counting continuation lines. ⚠️ **The truncation defect the agent FIXED was real and is separately recorded**; only this residue claim is withdrawn. **Do not open UI-campaign work off it.** |
| 🔴 **NEW 2026-09-04 — AND IT IS THE MOST SERIOUS KIND HERE, BECAUSE IT IS A `[GRADE: COMPARED]` ITEM THAT SAYS THE WRONG THING. §1.3 item 12 contains TWO claims the official source refutes.** (a) It struck `plan.md`'s line that Tally *"reserves `Ctrl+Enter` for display-only drill-down"* as **WRONG** and concluded our divergence was *"a **smaller** divergence than the plan recorded"*. The vendor's own page carries **both** limbs — *"To drill-down and **open a voucher for display**"* **and** *"To **alter a master** during voucher entry or from drill-down of a report"* — so on a **voucher** the official chord is **display**, `plan.md` was substantially right, and **our divergence is LARGER, not smaller.** Item 12's amendment appears to rest on a corpus cell carrying only the master limb, generalised to the whole chord. (b) Its claim that the corpus *"names **one** action, not two"* is refuted by the same page: the reference product has **two** actions on a report row — plain Enter → alter, `Ctrl+Enter` → display — **and so do we, with the two chords SWAPPED**, which is both a sharper statement of the divergence and a much cheaper fix. | **Two corrections owed to item 12**, and they matter more than an ordinary row because **a wrong fidelity record is worse than a missing one** — it is the thing a later reader trusts instead of measuring. ⚠️ **Honest limit: the CONFLICT is established from the official page; whether the BOOK cell really carries only the master limb, or was mis-transcribed, could not be checked — `tally/` is empty.** Both corrections are written into §1.3 **item 16** rather than silently overwriting item 12, per the mark-in-place convention. |
| 🔴 **NEW 2026-09-04. `docs/tally-feature-catalog-verification-report.md` item A12 is WRONG about Physical Stock, and the SHIPPED CODE IS RIGHT — a rare direction and worth naming as such.** A12 says *"Physical Stock: `Ctrl+F7` → `F10 (Other Vouchers) > Physical Stock` (no dedicated function key)"*, sourced to the vendor shortcut page. **That page lists Physical Stock — `Ctrl+F7` under Inventory Vouchers**, and confirms `F10` is *"view list of all vouchers or masters"*, **not** a voucher key. | The seed's own comment at `src/Apex.Ledger/Seed/SeedVoucherTypes.cs` citing that reference for `Ctrl+F7` is **correct**; the report is wrong. **The report half-knew** — its §(C) item 1 already hedged the Physical Stock shortcut as a *"residual uncertainty"*. **The uncertainty is now closed in favour of the code:** strike A12's Physical Stock line and close §(C) item 1. ⚠️ **And the knock-on the integrator must carry: A12's ADJACENT claim about Attendance is `[model-knowledge]`-adjacent and, on this evidence, that report item is NOT RELIABLE and must not be used as a source elsewhere.** |
| 🔴 **NEW 2026-09-04. `src/Apex.Ledger/Reports/DayBook.cs:25-26`'s own doc comment claims the report is *"all vouchers within a date range"*.** It is false for the product as built: `src/Apex.Ledger/Reports/DayBook.cs:36` iterates `company.Vouchers` alone, while `Company` carries **two** voucher collections (`src/Apex.Ledger/Domain/Company.cs:421` and `:511`) and `InventoryVouchers` is never read. | Filed here as well as under **T1-17** because it is a *documentation* falsehood with its own harm: **it is exactly the kind of sentence a later reader trusts instead of measuring**, and eight census rows (4.9–4.16) sit on the behaviour it mis-describes. |
| 🔴 **NEW 2026-09-04. THREE §1.2a ROWS WERE WRONG ABOUT THEIR OWN STATE, AND A CENSUS ROW THAT IS WRONG ABOUT ITSELF IS A DEFECT IN THE CENSUS.** **12.8** claimed *"Zero hits in that file for any image … identifier"* — `src/Apex.Ledger.Io/PdfWriter.cs:238` emits `<< /Type /XObject /Subtype /Image`, `:93` is `public void Image(…, PdfBitmap bitmap)`, and `src/Apex.Ledger.Io/InvoicePdf.cs:359` is a **production caller**. **16.3** claimed *"`AuditTrail`, `EditLog`, `ModifiedBy`, `CreatedBy`, `ActorId` → all zero … No audit, log or history table among the 182"* — the voucher edit log ships at schema v52 with a type, three routes, callers and `CREATE TABLE voucher_edit_log`. **16.4** claimed *"None of the three takes an actor or a **timestamp** parameter"* — `VoucherEditLogEntry` carries `RecordedAt` **and** `BeforeSnapshot`. | **All three re-graded `ABSENT` → `PARTIAL` on 2026-09-04, each grep re-run independently by the integrator before the state was touched.** 🔴 **The shape of the error is worth more than the three rows: nothing was BUILT that day — the passes were read-only — so this is a census that had fallen behind its own product, and the corrected count LOOKS like progress and is not.** **Planning consequence: 12.8 is named in §5 as *"what blocks T0-9"* and a whole wave was sequenced behind it. The QR / barcode / mono-logo half of that gate is OPEN** — see `plan.md`. **A fourth row, 6.20, keeps its correct `ABSENT` grade but had a false evidence sentence** (`Drc03` is not zero in `src/Apex.Desktop` — there are two hits, both a read-only alteration guard). **That one is the row-3.13 pattern again: a falsifiable evidence cell under a correct grade invites a reader to distrust the grade.** |
| 🔴 **NEW 2026-09-04. Census row 12.4's own TARGET LIST was wrong on two of its five items, and census row 16.3's TITLE merged two different features behind a slash.** 12.4 named the print formats as *"Neat / Quick / **Condensed** / Dot-Matrix / **Pre-Printed**"*; the source's `F8` Print Format list has **three** values (*Dot Matrix Format* / *Neat Mode* / *Quick/Draft Format*) — **"Condensed" is a Tally.ERP 9-era term with no place in it, and "Pre-Printed" is the `F9` PAPER toggle, a different axis.** 16.3 was titled *"Tally Audit / Edit Log"*; the source describes **Edit Log** (TallyPrime Release 2.1; the only edition meeting audit-trail compliance) and **Tally Audit** (an older auditor's review listing) as **two features**. | **Both corrected in place at their rows; neither grade moved and the denominator did not move.** These are *"what to build"* defects rather than *"what exists"* defects, and they bite at build time: **a builder handed 12.4 would have put a paper setting in a format dropdown**, and — ⚠️ **urgently, because ruling 11 builds it next** — **a builder handed 16.3 would ship the wrong feature under the right name.** |
| 🔴 **NEW 2026-09-04. Census §3 note 1 is half right about Area 15, and the missing half is the load-bearing one.** It says only that *"Real TallyPrime still ships these as downloadable 'Extension for Tax' modules"*. The vendor is explicit that the split is **reports versus everything else**, not module versus base product: *"To view **reports** of VAT, Service Tax, and Excise, you need to download the **Extension for Tax** installer. … However … **the masters and transactions will continue as is in the product.**"* | ⇒ **rows 15.1, 15.2, 15.3 and 15.4 are BASE-PRODUCT fidelity**, while 15.5 and the return-form halves of 15.6–15.8 are extension capabilities. **Anyone scoping Area 15 as "one optional module, defer it" would be scoping it against a source that says otherwise.** Recorded in §1.3 item 18 with the individual per-row attestations. |
| 🔴 **NEW 2026-09-04. TWO WEB CITATIONS BEHIND SHIPPED FIGURES HAVE ROTTED, AND ONE OF THEM IS THE SOLE SOURCE FOR A LIVE PAYROLL DEDUCTION.** `https://www.incometaxindia.gov.in/w/tax-rates` — cited at `src/Apex.Ledger/Services/SalaryIncomeTax.cs` for the **4% cess** — now **404s**, as does `/w/tax-rates-1`; the text is alive and **unchanged** at `/w/tax-rates-2` and was re-verified verbatim. Separately, **§1.3 item 5 cites `epfindia.gov.in`, which now 301s to `epfo.gov.in`**. | **The rates are fine; the citations are not.** 🔴 **The mechanism is the point: this project's citation test *"checks only that the path resolves and the line is inside the file, never that the target says what the citing sentence claims"* (item 9) — and it does not check WEB citations at all. T0-5 was closed on a URL that has since died and nothing in the repo would ever have noticed.** Re-point the cess citation to `/w/tax-rates-2` and item 5's host to `epfo.gov.in`. ⚠️ **And add to the seed file's existing plain-slug roll-forward warning: WHOLE PAGES GET RENUMBERED.** The other load-bearing incometaxindia citations in that file were re-checked on 2026-09-04 and are still live. |
| 🔴 **NEW 2026-09-04 (WAVE-3). FOUR MORE §1.2a EVIDENCE CELLS WERE FALSE AT HEAD, UNDER GRADES THAT WERE ALL CORRECT — THE 6.20 DEFECT CLASS, FOUR MORE TIMES, AND IT IS NOW THE MOST FREQUENTLY RECURRING DEFECT IN THIS DOCUMENT.** **3.4** said *"there is no bound input for the `StandardCost` value"* — `MainWindow.axaml:6612` binds `StandardCostText` two-way and `StockItemMasterViewModel.cs:441-461` validates it. **2.4** said *"exactly ONE … is settable anywhere in the UI"* — `grep -n "AllowZeroValuedTransactions" src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs` returns 994/997/998; it is two on that screen and six across two. **2.12**'s evidence grep targeted `Multi Ledger`/`MultiLedger`, a **Tally.ERP 9** name the reference product no longer uses. **2.5** measured us against `{Automatic, Manual, None}` when the vendor's set is `{Automatic, Automatic (Manual Override), Manual, Multi-User Auto}` — **the TARGET LIST was wrong, so the gap could not have been closed by building to it.** | **All four corrected in place in this pass, with the grep that proves each.** ⚠️ **The pattern to read, and it is not "someone was careless": every one of these cells was written from a search that was correct WHEN RUN and went stale as the code moved. A falsifiable evidence sentence under a correct grade still invites a reader to distrust the grade.** ⇒ **Owed: a citation test that resolves evidence claims BY CONTENT, not merely by path.** The existing one checks that a path resolves, which is why the `Key.K` line number has now drifted three times (653 → 757 → 835) without failing anything. |
| 🔴 **NEW 2026-09-04 (WAVE-3). A CENSUS ROW MEASURED THIS PRODUCT AGAINST A FEATURE THE REFERENCE PRODUCT DOES NOT HAVE.** Row **9.9** was titled *"Transfer Journal as a **named** voucher kind"*. **That is a Tally.ERP 9 artefact.** TallyPrime's own inventory-voucher page lists nine kinds and does not include it; the mechanism is a **Stock Journal Voucher Class**. | **Re-titled in place; the `ABSENT` state is unchanged and correct** (we ship neither). **The harm is planning harm: a row that names the wrong target cannot be closed by building the right thing**, and a slice written against the old title would have shipped a voucher kind the reference product has not had for two major versions. **This is the same species as 12.4's target list and 2.5's option-set — three occurrences now, all found the same way: by reading the vendor page BEFORE trusting the cell.** |
| 🔴 **NEW 2026-09-04 (WAVE-3). A WRONG STATUTORY SUB-CLAUSE CITATION IS IN SHIPPED SOURCE. NO MONEY MOVES.** `GstSetOffService.cs:12-16` attributes the CGST↔SGST cross-utilisation **wall** to *"§49(5)(c)/(d)"*. **Those clauses are the own-head-first rules; the wall is §49(5)(e) and (f).** | **The BEHAVIOUR is correct** — §1.3 item 6 stands — **and only the sub-clause letters are wrong.** Filed at TIER 3 rather than TIER 0 for exactly that reason. ⚠️ **But it is a citation in shipped code, which is the category this project has already had to strip commercial blogs out of (T0-6), so it is fixed rather than tolerated.** |

---

## 3. OBSOLETE BY LAW — ~~**USER DECISION REQUIRED, NOT DECIDED HERE**~~ **DECIDED 2026-08-19: BUILD THEM**

> **▶ 🔴 THE DECISION THIS SECTION ASKED FOR WAS TAKEN ON 2026-08-19, AND IT WENT AGAINST THE
> RECOMMENDATION BELOW.** User ruling 10 (R12 — `plan.md` §5, `FOUR FURTHER USER RULINGS (R12, 2026-08-19)`)
> brings all nine **into the denominator as build rows**. They are now **Area 15 of §1.2a**, each with a
> state measured against the code on 2026-08-19: **all nine ABSENT**, each on a named regex that returned
> zero. **This section is NOT deleted** — it is the record of what was recommended, decided against on
> 2026-08-10, and reversed on 2026-08-19, and the reasoning in it is still the reasoning a builder needs.
> **▶ WHAT CARRIED THE REVERSAL IS NOTE 1 BELOW** — real TallyPrime still ships these as downloadable
> tax-extension modules — **i.e. the counter-argument this section already recorded against itself.**
> **▶ THE RECOMMENDATION IS NOT REFUTED, ONLY OUTWEIGHED, AND IT SURVIVES AS A DESIGN CONSTRAINT:** these
> encode **repealed rate tables**, so they are built as **dated, historical** rate sets and never as live
> 2026 defaults. **Note 3's middle option — model them as *historical read-only* — is now the obvious shape
> for discharging that constraint, and choosing it is an open design question, not a settled one.**
> **▶ NOTE 2 IS UNTOUCHED AND STILL BINDING: TDS and TCS are NOT in this group.**

These 7.2 features exist only to serve pre-GST Indian tax law. Cloning them faithfully would build dead law into a 2026 product. **I recommend, but this section must not be actioned without the user's explicit call.**

| Feature | Status in law | Recommendation |
|---|---|---|
| State VAT — enable, dealer type, TIN, registration date | Subsumed by GST from 2017-07-01 | **Do not build.** |
| VAT/Tax Classifications (`Input VAT @ 4%`, `Output VAT @ 4%`) | Dead; and the UI itself was replaced by "Nature of Transaction" in ERP 9 Rel 5.0, so 7.2's screens differ even from the corpus | **Do not build.** |
| The 2005 four-slab VAT rate structure (1% / 4% / 12.5% / exempt, ~550 categories) | Dead | **Do not build** — this would hard-code 2005 tax law. |
| VAT Composition scheme | Dead | **Do not build.** |
| VAT Reports (Computation + state return forms) | Dead | **Do not build.** |
| Central Sales Tax — 2% interstate, C/F/H declaration forms | Dead | **Do not build.** |
| Service Tax + Form ST3 | Subsumed by GST 2017 | **Do not build.** |
| Excise (7.2's F12 invoice-format route; Excise for Dealers RG23D/Form 2; Excise for Manufacturers) | Central excise on most goods ended with GST | **Do not build.** |
| Fringe Benefit Tax | Abolished by Finance Act 2009; **not in 7.2 anyway** | **Do not build** — listed only so nobody adds it "for completeness". |

**Count: 9 capabilities.** ~~Held out of the denominator (§1.2).~~ **▶ IN the denominator since 2026-08-19 as §1.2a Area 15 (user ruling 10); the count of 9 is unchanged and is one of the two addends of `200 + 9 + 7 = 216`.** *(2026-08-18: this "9" is correct and is one of the two counts that showed the old top-down reconciliation never closed — see §1.2c. **One candidate ADDITION to this section is flagged and NOT actioned here:** Kerala Flood Cess, §1.2a row 6.26, measured ABSENT and believed lapsed. §3 says this section must not be actioned without the user's explicit call, so it stays in the denominator as an absent capability until they make one.)*

Three things the user should weigh before deciding:

1. **Real TallyPrime still ships these** as downloadable "Extension for Tax" modules (verification report A25, OFFICIAL tallysolutions.com). So "exactly cloned" arguably includes them. My recommendation is still no — they encode repealed rate tables.
2. **TDS and TCS are different and must not be swept in with the above.** The *mechanism* is current law. Only 7.2's *sections, rates, thresholds and return forms* are twenty years stale. **Clone the mechanism, never the numbers.**
3. **A partial option exists:** model VAT/CST/Service Tax as *historical read-only* — enough to display a migrated pre-2017 book, not enough to post new ones. Cheaper than a full clone, and honest about the law.

---

## 4. ~~EXCLUDED BY DECISION — NOT GAPS, MUST NOT BE COUNTED AS SUCH~~ **NO LONGER EXCLUDED AS OF 2026-08-19**

> **▶ 🔴 USER RULING 10 (R12, 2026-08-19 — `plan.md` §5) BROUGHT THIS SET INTO SCOPE.** The seven are now
> **Area 16 of §1.2a**, with states measured against the code that day: **six ABSENT and one PARTIAL.**
> **The PARTIAL is Repair / Rewrite / Verify**, and it is the reason the ruling required these states to be
> *measured* rather than assumed — the state recorded for it in this document on 2026-08-18 was wrong.
> **This section is retained as the record of what was excluded and why**, and it now points at Area 16.
> **▶ THE TWO ROWS BELOW THAT ARE STILL NOT COUNTED, and this has not changed: Phase 11** (hardening /
> packaging / release) is **process, not a capability**, and the **legacy indirect-tax stack** is counted in
> **§3 / Area 15**, not here — its basis line says *"excluded twice over"*. Counting either would break
> `200 + 9 + 7 = 216`. **▶ AND THE ARCHITECTURE-EXCLUDED LIST AT THE FOOT OF THIS SECTION IS NOT AFFECTED —
> ruling 10 does NOT bring it in**, and rule 4's withdrawn *"13"* stays withdrawn: **do not produce a fourth
> number for it.**

| Item | Basis |
|---|---|
| TallyVault (company-data encryption) | plan.md:432-440, standing user decision |
| Security Control: users, roles, security levels, password policy | plan.md Phase 10, user decision. R13 confirmed in code — 29 `password` hits are all explicit "no password is stored" notices or the NIC API credential record |
| Tally Audit / Edit Log / audit trail on masters and vouchers | plan.md Phase 10, user decision. **Note for the user, not as a gap:** their own Tally 7.2 has Tally Audit and this build has no equivalent |
| Alter / Delete / Cancel shipping with **no audit trail** | Explicit R12 decision recorded at plan.md Phase 10.11 |
| Split Company Data by financial year | plan.md Phase 10, user decision |
| Repair / Rewrite / Verify company data | plan.md Phase 10, user decision |
| Group Company consolidation | plan.md Phase 10, user decision |
| Hardening / packaging / v1.0.0 release | plan.md Phase 11, user decision |
| Legacy indirect tax stack | plan.md §1.3 out of scope — **and** obsolete-by-law per §3. Excluded twice over |

**Count: 7 capabilities** ~~held out of the denominator~~ **— IN the denominator since 2026-08-19 as §1.2a Area 16 (user ruling 10); the count of 7 is unchanged and is the second addend of `200 + 9 + 7 = 216`** (§1.2; Phase 11 is process, not a capability; the legacy stack is counted in §3, not here). *(2026-08-18: this "7" is correct as stated and is the second count that showed the old top-down reconciliation never closed — that check subtracted **5** against it, and no arrangement of these seven produces 5. See §1.2c. All seven were re-measured at HEAD and all are still ABSENT — see §1.2a's area 13 and area 14 held-out notes.)*

**▶ 🔴 THE CANONICAL ARCHITECTURE-EXCLUDED LIST (§1.1 rule 4 points HERE, as of 2026-08-18).** Separately excluded from the denominator as out-of-scope-by-architecture, not by user decision — surfaced here so the user can overrule: Tally.NET, Remote Access, Control Centre, Support Centre, TRiB, SMS, Auditors' Edition, Tally.Server 9, multi-site/rental licensing, TDL, multilingual, international statutory packs — **12 names**, plus the **3** that rule 4 enumerates and this list omitted (**Data Synchronisation, the 7.2 data-format migration tool, the 7.2 character-grid UI**), for a **union of 15**. Rule 4's *"13 rows"* was derivable from neither list and is withdrawn; see §1.2c. **One deserves a second look: Data Synchronisation's IP mode is self-hosted and needs no Tally.NET server** — if branch-to-HO sync ever matters, that one is buildable.

---

## 5. SEQUENCING PROPOSAL

### The three named blockers, confirmed

1. **No voucher alteration or deletion makes every other defect permanent** (T1-1). Confirmed. **This is the true root of the tree** — every Tier 0 wrong figure is unrecoverable until it lands.
2. **No Order No / Tracking No blocks correct order fulfilment** (T1-8). Confirmed — zero `TrackingNumber` hits.
3. **No master-screen F12 blocks a whole configuration layer** (T1-16). Confirmed, and it is entangled with the *missing F11 Accounting group* (T1-15) — they are one configuration layer, not two.

### Prerequisite graph

```
S0  PopulatedCompanyFixture extension (posts 15 more base types)
      -> prerequisite for HONESTLY REGRESSION-TESTING everything below.
         ⚠️ CORRECTED 2026-08-17: "currently 8 of 23 base types" was true when written and has been FALSE
         since 1de940e (2026-08-10) — the fixture posts 23 of 23 seeded base kinds. S0 IS THEREFORE ALREADY
         DISCHARGED and no longer gates S1. The second half STILL HOLDS: no print/export test uses it at all,
         which is the part of this row that is still open.

S1  Voucher lifecycle (Phase 10.11: alter / delete / cancel / duplicate / insert)
      -> unblocks: recovery from every Tier 0 defect
      -> depends on: S0

S2  Company creation + Alter Company (11 profile fields that ALREADY exist
    on the domain, in the schema, and in the printer)
      -> unblocks: T0-8 blank seller address, prior-FY books, Restore reachability

S3  Voucher Type master + Show Inactive + Voucher Class
      -> unblocks: T1-4 Payroll posting, T1-5 Manual/None numbering,
                   per-type print settings, custom types

S4  Shared report base carrying drill + print + export by construction
      -> unblocks: T1-9 (71 surfaces), T1-10 (32 surfaces), and MUST precede
                   T2-1 so the ~14 new reports are born drillable

S5  PdfWriter image/XObject + font embedding (or a no-NuGet PDF rewrite)
      -> unblocks: T0-9 IRN/QR, company logo, JPEG export, multilingual print,
                   cheque printing layout
      -> HARD DEPENDENCY, 3-6 weeks before any dependent feature starts

S6  F11 Accounting + Inventory Features groups, then global F12 tree
      -> Integrate-Accounts-with-Inventory needs its OWN slice with an oracle
         harness — it changes how the Balance Sheet sources closing stock
```

### Recommended order

**Wave 0 — do these first, they are cheap and they stop active harm.** All are UI over finished plumbing.

| Item | Size | Why first |
|---|---|---|
| T0-7 Bill of Supply routing + `DocumentTitle` field | ~1 day | The screen already computes the answer. Stops issuing illegal documents today. |
| S2 Company Create/Alter screen (11 existing fields) | days | Fixes T0-8 on every future invoice; unblocks prior-FY books. |
| T1-7 Restore reachable from Company Select | ~½ day | Difference between a backup feature and a disaster-recovery feature. |
| T1-11 Wire the 5 orphaned `GstReturnJson` writers to their screens | ~2-3 days | GSTN key schema still needs A14/R7 confirmation first. |
| Negative-stock warn toggle + e-Way config editor | days each | Shipped behaviour with no control surface. |
| Tier 3 doc corrections (23-vs-24, Phase 1/2/5/9/10.9 claims, IV-19's number) | ~1 day | Nothing downstream can be planned honestly until the registers stop lying. |
| **S0 fixture extension** | S | **Highest-leverage single item in the report.** Nothing below is honestly testable without it. |

**Wave 1 — the correctness wave.** T0-1 §194Q excess carve, T0-2/T0-3 valuation (with an oracle harness — see the negative-stock note: three attempts, three unbounded Balance-Sheet errors that each passed the full suite), T0-4 GST hierarchy, T0-10 CN/DN stock parity. Then **S1 voucher lifecycle**, so these fixes are recoverable in existing books.

**Wave 2 — the structural wave.** S3 Voucher Type master. S4 shared report base (drill + print + export in one refactor — they are the same refactor; do not do them separately). S6 F11/F12 configuration layer, with Integrate-Accounts-with-Inventory carved out under its own oracle.

**Wave 3 — breadth.** T2-1 report families (each is a projection over data we already post; the ReportKind+menu+grid pattern is proven 45 times). T1-12/T1-13 GST return completeness. T1-8 tracking numbers and order fulfilment.

**Wave 4 — S5 print engine**, then everything gated behind it: T0-9, cheque printing, multi-account printing, the five missing print documents.

**Wave 5 — T2-2 statutory long tail.** Nothing here is architecturally hard. It is tonnage, and it is most of the remaining tonnage.

---

## 6. WHAT IS STILL UNMEASURED AFTER THIS CENSUS

**The floor is still a floor.** State this plainly to whoever reads next.

1. **Behaviour is unmeasured for all but a handful of the capabilities in §1.2a — §1.3 holds the count, and holds it in one place.** This census measured *existence and reachability*. Only the capabilities enumerated in §1.3 have any sourced behavioural comparison to Tally; read the four figures there rather than from a copy here, because a copy here is exactly what went stale when §1.3 was last corrected. A `COMPLETE` row means the code is there and a user can reach it — nothing more. Every one of the complete rows could still compute the wrong number, and two of them demonstrably do. *(Amended 2026-08-18: this item used to say "all but a handful of the 115" and "the 42 present rows". The denominator and the split both moved — §1.2b says why — and the digits are deliberately not restated here, because restating them here is exactly what this item warns against.)*

2. **Report content and column sets are unmeasured across all 77 surfaces.** Nobody has compared a single Apex report's columns, groupings, totals or ordering against the same report in Tally. The 45 `ReportKind` values and 32 dedicated Screens were counted, not read.

3. **Print layout fidelity is unmeasured.** The renderers were inventoried; not one printed document has been laid against its Tally counterpart. What *is* now known is worse than "unmeasured": the engine is structurally capped (T2-4).

4. **GST return content correctness is unmeasured.** Missing *tables* were counted. Whether the rows that exist carry the right values under the right conditions has never been checked against a filed return.

5. **The 7.2 baseline itself is ~20/90 SECONDARY-sourced.** Course syllabi and blogs, because no official 7.2 documentation is reachable and the install is off limits. Several rows are honestly UNVERIFIED: `INV-VALUATION`, `INV-ACTUAL-BILLED`, `INV-ADDL-COST`, `MSTR-VOUCHER-TYPE`, `MSTR-VOUCHER-CLASS`, `VB-VOUCHER-NUMBERING`, `PRN-CHEQUE`, `PRN-STATIONERY` (marked GUESS at source), `DATA-REWRITE` (GUESS), `DATA-SPLIT`, `RPT-RATIO`, `RPT-CASHFLOW`, `RPT-STOCK-SUMMARY`, `RPT-EXCEPTIONS`, `RPT-COLUMNAR`, Job Costing, Item Cost Tracking.

6. **Several Tally field names in the absent list are UNVERIFIED against this corpus** even though their absence from our code is CONFIRMED: the Group behavioural flags (sub-ledger / nett debit-credit / used-for-calculation / allocation method), Godown "Allow storage of materials", credit limits. R7 grounding must precede any design work on these. *(2026-08-18: **the absent list now exists** — §1.2a, 58 rows — and each of these three carries the ⚠️ UNVERIFIED marker in its own row. When this item was written the list it referred to did not exist; that was census defect 3 and it is closed.)*

7. ~~**The 8 CANNOT-TELL rows in the table** were never greppted by any agent~~ — ✅ **CLOSED 2026-08-18. All eight are resolved and the undetermined column is 0.** The eight were: Actual-vs-Billed → **COMPLETE** (§1.2a 3.14); Additional Cost of Purchase → **COMPLETE** (3.15); Transfer Journal → **ABSENT as a named kind, the function partly covered by the Stock Journal** (9.9); Kerala Flood Cess → **ABSENT** (6.26, resolved here rather than by a survey — and recommended for a §3 user call); payroll job-rates / cost-centre allocation → **PARTIAL**, job rates exist and cost-centre allocation does not (7.17, also resolved here); unified Banking menu → **PARTIAL**, it exists and carries two rows (8.9); Job Costing → **ABSENT** (9.6); Item Cost Tracking → **ABSENT** (9.7). **This item was right that nobody had greppted them, and six of the eight then cost one grep each** — which is the part worth remembering.

8. **Bank statement import arithmetic was not re-verified** — the file exists and the screen is wired (MEDIUM confidence only).

9. **No print or export test uses `PopulatedCompanyFixture`.** Every renderer is locked against thin bespoke fixtures. That is precisely the condition that made the previous sweep undecidable, and it is unchanged.

10. **`docs/invented-vs-cloned.md` §7's unmeasured list is now only partly closed.** Closed by this census: printing and print layouts (structurally, not fidelity-wise), report layouts (existence only), company creation and F11/F12, backup/restore, import/export, POS, banking, security. **Still genuinely unmeasured: GST return *content*, payroll *entry-surface* fidelity, budgets, scenarios, forex, manufacturing, job work, multi-currency.**

---

## 6a. 🔴 UNREACHED — A SOURCE EXISTS AND COULD NOT BE RETRIEVED. ADDED 2026-09-04.

> **▶ 🔴 AMENDED THE SAME DAY BY USER RULING 14 (R12): ROW U-A BELOW IS NO LONGER AN *UNREACHED* ROW, AND THAT
> IS A THIRD VERDICT THIS SECTION DID NOT HAVE.** This section's own distinction is *corpus silent* (permanent)
> versus *unreached* (re-opened when the source becomes retrievable). **The corpus is neither: it is GONE.**
> `tally/` is empty and **was never git-tracked**, so no tooling improvement, no branch and no A12 action will
> ever retrieve it — the row can never be re-opened on its own terms, which is exactly what *unreached* promises
> and cannot here deliver. **Read U-A as: this evidence base has been REPLACED, not deferred.** The questions it
> lists are now put to the **vendor documentation** (`help.tallysolutions.com`), which is the project's primary
> source under ruling 14; where that is silent too, the capability is a documented divergence labelled as ours.
> **Every other row in this section is unaffected and still means what it says.**

**Why this section exists, and it is the most important paragraph in it.** *Corpus silent* and *unreached* are
**different verdicts with opposite futures**. Under ruling 9 a **corpus-silent** capability ships as a
documented divergence labelled as ours and **can never join the compared set**; an **unreached** one is re-opened
the moment its source becomes retrievable. **Recording a temporary tooling failure as silence would manufacture a
permanent, unfalsifiable verdict**, and this project has already paid for the opposite mistake: **§194A's rate
and the whole TCS with-PAN set were carried in shipped code on commercial charts precisely because repeated
failures to reach the primary instrument quietly hardened into assertions.** Both of those were closed on
2026-09-04 by simply reaching the document. **So: nothing below is an assertion, and nothing below is silence.
Each row says what would settle it.**

| # | What is unreached | Why it could not be retrieved | What would settle it |
|---|---|---|---|
| **U-A. THE CORPUS ITSELF — and it dominates every other row here** | `tally/` holds **zero PDFs**, so all nine documents this project's evidence cells cite by name are absent. **Every Tally-BEHAVIOURAL question in areas 6, 7 and 8, and every row in areas 9–16 needing page-cited verbatim screen text, is unreached rather than silent.** Named specifically: screen layouts, field labels, menu placement and report column sets for the GST return screens (6.8–6.19), the payroll masters (7.2–7.7), the payroll reports (7.15) and **the whole Banking column (8.1–8.10)**. | The folder is git-ignored by design (third-party IP), so **git cannot restore it, from any branch or worktree**. Measured three ways by three agents on 2026-09-04, `-Force` included so a hidden file would have counted; all seven live worktrees are empty too; a Desktop-wide `*.pdf` sweep found 29 PDFs and **not one is a corpus document**. `pdftotext` itself is present and healthy. | **Restoring the nine PDFs to `tally/`. This is a USER action — see U-0.** A14 is by mandate the only agent that opens the corpus, so **the entire corpus-first half of R7 is inoperative for every agent on every future pass until it is done**, and A14 must not be re-tasked with corpus work until it is. |
| **U-B. ESI (Central) Rules 1950, Rules 50 and 51** | The **contribution BASE** once a covered member's wages rise mid-period (our rule charges on uncapped actual wages), and the **rounding** rule our code cites in-file as *"Rule 51"*. | Two routes tried, both failed: WebFetch → *"unable to verify the first certificate"* (the site's chain fails validation for the fetcher); the browser pane → the server *"responded with a file download instead of a page"* and the pane refused to navigate. A same-origin fetch returns bytes but there is no PDF parser in the page. | ⚠️ **URGENT, because a secondary summary points AGAINST our shipped rule** — it stated that where wages exceed the limit after a period starts, contribution is paid *on the wage limit prescribed*. **That is NOT recorded as a source** (an aggregated summary, inadmissible under R7). **Settle it with the literal text of Rule 50 (base) and Rule 51 (rounding):** download the PDF out-of-band and read it with `pdftotext -raw` — it is a rules table, so expect `-layout` to scramble. **Until then the in-code "Rule 51" citation is an ASSERTION, and the base rule must not be treated as verified in either direction.** |
| **U-C. The Payment of Bonus Act 1965 and the Payment of Gratuity Act 1972** | **Every** shipped bonus figure (₹21,000 eligibility, 30 days, the ₹7,000 calculation ceiling, the 8.33%–20% band, the ₹100 floor) and **every** shipped gratuity figure (15/26, the ≥6-month round-up, 5-year vesting, the ₹20,00,000 cap). | `labour.gov.in` returned **403** to WebFetch; **`indiacode.nic.in` was DOWN throughout the session** — 403 to WebFetch and a 500/404 page in the browser pane — so the Government of India's own code repository was unavailable. | Either host recovering, or the Acts as published in the Gazette. **This blocks both families entirely, and until then every one of those figures is OURS.** |
| **U-D. The Finance Act's own section 2, for FY 2025-26 — the CESS LEVY** | The 4% Health & Education Cess is levied by the **Finance Act's section 2**, not by the First Schedule; grepping the full FA 2025 First Schedule for *"Health and Education Cess"* returns **zero hits**. | The Department's Finance-Acts browser serves **only "As amended by Finance Act 2026"**, exposes 167 sections and **no Schedules**, and offers no FA 2025 section text; `/w/section-N` slugs are the *Income-tax* Act, not the Finance Act. | The FA 2025 section 2 text. **So the cess remains sourced to a Department summary page, not to the Act** — exactly the caveat the code already records, confirmed still accurate. ✅ **Note the asymmetry worth celebrating: the First Schedule itself IS now reachable** (that is what closed §194A and the surcharge cap), so **only the cess, which lives outside it, is still short of the Act.** |
| **U-E. FY 2026-27 / AY 2027-28 salary rates** | The forward-year slabs, deductions, rebate and surcharge that **T1-26**'s date-blind engine would need. | The Department's rate page's newest column is **AY 2026-27** — there is no AY 2027-28 table — and the FA 2026 First Schedule is drafted against the **Income-tax Act 2025**, a **different statute** from the 1961 Act our engine encodes. | The FA 2026 First Schedule Part III read as the 2025-Act charging provision, **plus a decision on how this product spans the 1961 → 2025 Act cutover**. **T1-26's exposure cannot be sized until this lands.** |
| **U-F. A Year-2025 slug for §194A** | The **vintage of the ₹10,000 threshold** — whether it was in force for FY 2025-26. | Probed; **none exists.** The live slug serves **Year 2026**, and the page renders footnote **markers** but not **definitions**, so the substituting Act and w.e.f. date behind the figure are unreadable. | A Year-2025 §194A slug, or the amending Finance Act's text. ✅ **Much less serious than it was:** the **rate** no longer depends on this (the First Schedule closed it); only the **threshold's** vintage does. |
| **U-G. `Alt+X`'s attested scope — two sources in conflict** | Whether `Alt+X` is report-only (the official page, and what we ship) or *"Vouchers & Reports"* (the corpus cell §1.3 item 10 rests on). **Our scope may well be fidelity, and item 10 currently disclaims fidelity we may be entitled to.** | BOOK p.437 could not be re-opened — see U-A. | **Re-open BOOK p.437 with `-raw` once `tally/` is restored and count keys against functions per item 13's own test.** Item 13 is a standing warning that **pp.435-437 are exactly the pages `-layout` scrambles**, so the two-form cell may itself be an extraction artefact. |
| **U-H. Two Voucher-Type field names, and the memorandum/reversing-journal semantics** | Row 5.11's *"Use Common Narration"* and *"Show Inactive"* — **neither appears on the official Voucher Type master page**, and the nearest attested field (*"Provide narration for each ledger in voucher"*) is arguably the **inverse** feature. Rows **4.17** (Memorandum conversion) and **4.18** (Reversing Journal Applicable-Upto) are unreached **on behaviour**; their chords verify, their semantics do not. | The field names are not enumerated on the page retrieved; the memorandum/reversing-journal page **404'd** and no replacement URL was found. | A vendor page enumerating those two fields, or the corpus. **A row will not be graded on a name that could not be sourced** — that is how the two names came to be in the census in the first place. |
| **U-I. Row 12.3's copy labels**, ~~*and row 13.10's file chooser*~~ 🔴 **THE 13.10 HALF IS CLOSED 2026-09-04 (wave-3, §1.3 item 22) — it was reached, compared and moved into the compared set. This is the FIRST time this section has delivered what it promises: an unreached row RE-OPENED the moment its source became retrievable, rather than hardening into an assertion. The 12.3 half stands.** | Whether our copy-label enum matches the source's **product** vocabulary — *Original / Duplicate / Triplicate / Quadruplicate / Extra Copy* — rather than the statutory Rule 46 list the census records. And whether the reference product's *"Location of Import/Export Files"* control is a **typed path** or a **browse dialog**. | The copy-label enum was not opened in the pass. The source phrase *"Set the folder path to save the exported or imported file"* is **compatible with both readings**. | For 12.3: open our enum — **the cheapest follow-up in Area 12.** For 13.10: a screenshot or step-list of the `F12` export-configuration screen showing whether that field carries a browse control. 🔴 **This is worth settling BEFORE anyone builds T1-20, because the two readings imply very different work — if the reference control is a typed path, our typed-string paths AGREE and row 13.10's framing is a USABILITY complaint, not a fidelity defect.** |
| **U-J. Area 15's rates, and two of its rows** | Row **15.3**'s *"1% / 4% / 12.5% / exempt, ~550 commodity categories"* — **no rate, slab or threshold in Area 15 was verified.** Row **15.4** (VAT Composition) is not separately attested in anything retrieved. Row **15.9** (Fringe Benefit Tax) is unreached **and expected to stay so** — nothing in current vendor documentation mentions it. | Not attempted for the rates; **repealed State VAT schedules are exactly the kind of number that no longer has a live official home.** | For 15.3: an official source, **before any figure is written into code** — flagging this at target-definition time is far cheaper than flagging it at seed time, and this project has already had to strip inadmissible citations out of shipped rate seeds twice. For 15.9: **a user ruling (U-5), not more research** — it is the one Area-15 row with no reference-product behaviour to clone, and **a build agent handed it will invent a feature.** |
| **U-K. Row 11.6's other four registers**, ~~*and row 11.7*~~ 🔴 **(the 11.7 half is CLOSED 2026-09-04 — wave-3 §1.3 item 21 reached the vendor's ledgers-and-groups page, compared Group Summary and Group Vouchers, confirmed the `ABSENT` grade and specified the target; that pass's own note that its `Ctrl+B`/`Ctrl+H` option lists rest only on a search-engine summary is kept and NOT counted as compared)** **, and rows 16.5 / 16.6 / 16.7** | Row 11.6 covers **five** registers and exactly **one** — Sales — was compared; the other four are named by the source as existing but their column sets, and whether they share the month-wise-then-drill shape, are unknown. Row **11.7** (Group Summary / Group Vouchers): the only text retrieved was a one-line fragment, arguably describing a *columnar option* rather than the report, and naming nothing about Group Vouchers. Rows **16.5**, **16.6**, **16.7**: not compared. | The individual register pages and the Group Summary page were not located in the pass. | Their vendor pages. 🔴 **Row 11.6 must NOT be marked verified on the strength of Sales alone, and no target may be built out of row 11.7's fragment** — declining to build a target from thin text is the discipline this section exists to enforce. **16.6's `PARTIAL` state and its two named missing pieces are a CODE measurement that was not re-run and not sourced.** |
| **U-M. AREAS 2 AND 3 — thirteen master screens, and the vendor page for most of them EXISTS** (wave-3 §1.3 item 19) | Rows **1.8** and **1.9** (the F12 configuration tree, global and per-screen); **2.9** Budget master; **2.10** Scenario master; **2.11** Currency master and Rates of Exchange; **2.13** Show Inactive / hidden masters; **3.2** Stock Category master; **3.3** the Stock Item master's **full field set**; **3.8** Batch/Lot; **3.9** Bill of Materials; **3.10** Price Level; **3.11** Price List; **3.12** Reorder Levels. | **Not a retrieval failure — a boundary.** The pass spent its depth on the rows that had never been compared and on discharging two `UNVERIFIED` field-name caveats, *"because retiring those closes open questions permanently rather than adding another 'looks right'."* Two pages (`/manage-inventory-batch-wise-tally/`, `/reorder-stock-items-reorder-status-and-reorder-quantity/`) were **downloaded and not read**; `/tally-prime/accounting/multi-currency/` returned a **hub page only** and needs its leaf. | Named pages: `/budgets-tally/` (2.9) · `/scenarios-tally/` (2.10) · the multi-currency **leaf** (2.11) · `/charts-of-accounts-tally/` *Change View* (2.13) · `/manage-stock-item-tally/` category-creation screen (3.2) · `/manage-inventory-batch-wise-tally/` (3.8) · `/manage-inventory-in-manufacturing-tally/` (3.9) · a price-level page (3.10, 3.11) · `/reorder-stock-items-reorder-status-and-reorder-quantity/` (3.12). 🔴 **Three live threads to hand whoever picks this up:** (i) row 2.11's *"none of the four currency formatting options exists"* — the vendor's F12 base-currency section names **three** of them (`Suffix symbol to amount`, `Add space between amount and symbol`, `Show amount in millions`), so the row is measurable now; (ii) the vendor's `Provide Standard Buying and Selling Rates` is **TWO** rates and we carry **one** (`StandardCost`); (iii) the F12 chord's own description (*"the list of configurations applicable for the report/view"*) suggests F12 is **CONTEXTUAL rather than a global tree**, which would **reshape T1-16** — worth checking first because it may make 1.8 and 1.9 the same row. |
| **U-N. The BEHAVIOUR halves of rows 3.14 and 3.15** (wave-3 §1.3 item 19) | Actual-vs-Billed quantity, and Additional Cost of Purchase: the **placement** half of both is compared and sourced; **what the product DOES with them is not.** | Not attempted — the F11 pages establish placement, not apportionment. | The vendor's **actual-vs-billed** behaviour page, and its **additional-cost apportionment** rule (by value? by quantity?). ⚠️ **Do not infer the apportionment basis from our implementation and call it verified** — that is the shape of the §194A failure this section exists to prevent. |
| **U-O. AREA 6's GST REMAINDER, including both ANNUAL RETURNS** (wave-3 §1.3 item 20) | **6.12 GSTR-9 / 9C** and **6.13 GSTR-9A** — untouched. **6.11's GSTR-4 own table structure** (4A/4B/4C/4D, 5, 6) — only the CMP-08 *report surface* was compared. **6.1–6.7, 6.17–6.20, 6.22–6.26** — not examined. **Rule 88A primary text.** **CMP-08 / GSTR-4 statutory form structure.** | The annual returns are large forms and *"deserve their own pass rather than the tail of this one"* — a boundary, not a failure. **Rule 88A is a genuine non-retrieval:** it was inserted in 2019, **after** the only reachable CBIC consolidation (30-12-2017), where it returns **0 occurrences** — and the pass **declined to substitute a document that does not contain the rule.** `grep -c "CMP-08"` in that consolidation → **0**. | A **current** CGST Rules consolidation (post-2019) would settle Rule 88A, CMP-08 and GSTR-4 in one retrieval; the pass looked for one and did not locate it. The annual returns need the vendor's GSTR-9 / 9C pages plus the statutory forms. 🔴 **§1.3 item 6 (Rule-88A set-off) HOLDS ON ITS PRIOR GROUNDING and is explicitly NOT re-verified — it is counted in the anchor on the earlier comparison, and this row is the record that the re-verification was ATTEMPTED and honestly failed.** |
| **U-P. AREA 11's REMAINDER — and one row inside it is where a family grade would lie** (wave-3 §1.3 item 21) | **11.12's twelve-plus untouched members** (Stock Item Movement, Reorder Status, Batch-wise, Batch Age Analysis, Price List, the five inventory registers, Order Register, POS Register, the four Job Work books) · 11.9's Payables screen and the Bills Receivable/Payable print layouts · 11.10's **Group Break-up** and Cost Centre Class · 11.11's interest slabs / grace periods and the forex **settlement** path · 11.13's column layouts **and a TallyPrime (not ERP 9) enumeration of the exception family** · 11.14's Cash Flow Projection columns and the Funds Flow `Ctrl+B` Scale Factor. | **Three URLs returned navigation indexes rather than article bodies** (`…/MIS_Reports/Cost_Centre_Reports.htm`, `…/MIS_Reports/Cost_Category_Summary.htm`, `…/Display_Ledger_Vouchers.htm`) and **`help.tallysolutions.com/tally-prime/inventory-reports/stock-summary-tally/` 404s.** Where only a search-engine summary was available the pass **said so in place and did not count the claim as compared** — specifically the Group Vouchers `Ctrl+B`/`Ctrl+H` option lists and the Group Break-up report. | Working URLs for those four pages, or their current equivalents. 🔴 **AND THE WARNING THAT MATTERS MORE THAN THE LIST: row 11.12 is a §1.1-rule-2 FAMILY row hiding twelve-plus members, and only three of them have been compared. It is *the* place a family-row grade would lie**, and the pass named it as such rather than letting a `PARTIAL` on three members read as a measurement of the family. |
| **U-L. The largest TRACTABLE block left** | Census rows **6.27, 6.30–6.35, 6.38, 6.40–6.42** — Form 26Q, 16A, 27A, 24Q, 16, and the challan reconciliations. | Not attempted in the time available. **This is a boundary, not a failure to retrieve.** | The **NSDL/Protean FVU file specifications and the CBDT form notifications**, which are genuinely reachable. **Recommended as the next A14 slice — it is the single largest piece of verification that is neither corpus-blocked nor host-blocked.** |

---

## 7. 🔴 USER RULINGS NEEDED — ONE BLOCK, ADDED 2026-09-04, SO THEY CAN BE PUT AS A BATCH

**Nothing on this list may be decided by an agent, and no build agent may be dispatched into one of them.** Where
a ruling has a recommendation it is stated first, with the trade-off, per this project's standing preference.

> **▶ 🔴 UPDATED 2026-09-04 — TWO ROWS BELOW ARE NOW ANSWERED AND THE BATCH IS TEN, NOT ELEVEN. USER RULING 14
> (R12). The rows are struck in place rather than deleted, so the batch can be seen to shrink.**
> **U-0 is CLOSED — *proceed WITHOUT the corpus, on the official vendor documentation*.** `tally/` is empty and
> **was never git-tracked**, so restoration is not an option anyone has; the row's own recommendation
> (*"Restore them"*) is unactionable and must not be put to the user again. **U-3 is CLOSED — *YES, by
> necessity*:** an item verified against official vendor or primary-legal sources with **no** corpus page
> **belongs in the compared set**, and the seven items already resting on that precedent are **not downgraded**.
> **The other nine rows stand exactly as written, plus ruling 13's Q-A and Q-B, which belong with this batch.**
> The full ruling is in `plan.md` §5 (`A FOURTEENTH STANDING USER RULING (R12, 2026-09-04)`), the grounding
> order is in `CLAUDE.md` **R7**, and the fidelity consequence is in §1.3's **METHOD NOTE**.

| # | Decision | Why it cannot be taken by an agent | Recommendation |
|---|---|---|---|
| ~~**U-0**~~ ✅ **CLOSED 2026-09-04, RULING 14 — the second limb was chosen: PROCEED WITHOUT IT, on the vendor documentation. Not restorable (never git-tracked), so the "Restore them" recommendation is unactionable and this row must not be re-asked.** | 🔴 ~~**Restore the `tally/` corpus, or accept that everything from here ships as documented divergences labelled as ours.**~~ | The PDFs are third-party IP, git-ignored by design and **not recoverable from the repository by anyone**. Until they return, **the corpus-first half of R7 is inoperative for every agent**, and the stated goal — *"all 216 present AND corpus-verified"* — is unreachable by construction, not by effort. | **Restore them.** Nothing else on this list matters as much. **Note the honest consequence either way:** the three passes folded in on 2026-09-04 moved the compared figure 13 → 16 on **official vendor and primary-legal sources**, so verification is not blocked outright — but it is blocked for **product-behaviour** questions, which is most of areas 9–16. |
| **U-1** | **WHICH PRODUCT'S VOUCHER-TYPE SET does this clone target?** The official Statistics report lists **twenty-two** default voucher kinds; census Area 4 is titled for 7.2's classic **eighteen**, and we seed twenty-three. | These are two different products' defaults. A Statistics report built to the smaller list would be **wrong against the source while matching our own Area 4**. The question is **upstream of Area 4**, not of Area 11. | No recommendation — this is a scope decision. **Note it is cheap to ask now and expensive to discover after Area 11 ships.** |
| **U-2** | **THE COUNTING UNIT FOR THE FIDELITY FIGURE.** §1.3 has always counted **items**; the three wave-2 passes compared **80 capabilities across ~40 census rows** and were folded in as **+3**. | Counting the 80, or the 40, would **overstate the figure against every previous entry** in the section. Two of the three passes explicitly refused to propose a number and said the integrator must choose the unit. | **Keep the item as the unit** — that is what the block now records, made in the open. **If the user rules otherwise, the RULE changes in §1.3 and every item is re-counted under it; no digit is ever edited.** |
| ~~**U-3**~~ ✅ **CLOSED 2026-09-04, RULING 14 — ANSWERED *YES*, BY NECESSITY: with the corpus gone this is the only route into the compared set, and the seven dependent items are NOT downgraded.** | **DOES AN ITEM VERIFIED AGAINST OFFICIAL VENDOR / PRIMARY-LEGAL SOURCES, WITH NO CORPUS PAGE, BELONG IN THE COMPARED SET?** | This is the open R12 question §1.3 **item 14** states and declines to answer. **The precedent is strong and already load-bearing:** items 1, 3, 5 and 15 are all `COMPARED` on official pages with no corpus page at all, and item 15 says so in terms. Items 16, 17 and 18 are folded in on that precedent. | **Yes, and say so explicitly, because four items already depend on it.** ⚠️ **If the answer is NO, figure (1) does not merely stop growing — items 1, 3, 5, 15, 16, 17 and 18 all come OUT, and the anchor falls to single digits.** That is worth knowing before the answer is given casually. |
| **U-4** | **DEPTH vs BREADTH for wave 2.** 68 absent rows against 42 designed slices plus seventeen rulings, several XL. | The only lever on a 2–3-day horizon is the quality bar, and the corpus loss has already moved it. | **Close the cheap no-schema rows and stop**, rather than starting an XL track (VAT / Security / Excise) that will not land. |
| **U-5** | **Fringe Benefit Tax (row 15.9) — build it, or strike it?** | Ruling 10 said build Area 15. But FBT was abolished by the Finance Act 2009, **was never in 7.2**, and is the one row in the area with **no reference-product behaviour to clone**. Current vendor documentation does not mention it. | **Strike it.** **A build agent handed this row will invent a feature** — which is the failure mode this project has the most scar tissue about. |
| **U-6** | 🔴 **A SINGLE CHORD-MAP RULING, not three piecemeal answers.** Three attested chords are occupied: `Alt+I` (Insert Voucher) by the POS tender toggle; `Alt+K` (Company menu) by Saved Views; `Ctrl+I` (More Details) by the item-invoice toggle. `Alt+A`'s attested arm is third in arbitration behind two unattested ones. 🔴 **WIDENED 2026-09-04 BY WAVE 3, AND THE PATTERN IS NOW MUCH LARGER THAN "THREE COLLISIONS": all THREE of the vendor's top-level output menus are 0-for-3 in our shell** — `Alt+P` unbound, `Alt+E` **bound to `Ctrl+E`'s job**, `Alt+M` unbound (**T2-20**) — **and nine census rows sit behind them.** Add: `Alt+B` (the vendor's Settle Bills, and its Budget Variance overlay) is spent — ours is `Alt+A`, which is itself the Day Book's Add Voucher (**T2-24**, **T2-26**); `Ctrl+J` (Exception Reports) has **no binding at all** (**T2-27**); and **`F3` carries TWO different meanings inside our own app** while the vendor gives it one and gives Create Company no F-key at all (**T2-34**). | **Three separate collisions surfaced in one wave**, which is a pattern rather than three coincidences. 🔴 **Wave 3 makes it eight, spanning five areas, and turns this from a chord question into a NAVIGATION-SHELL question:** the three `Alt` menus are not chords to re-assign, they are **a menu shell that does not exist**, and nine rows currently written as independent builds collapse into it. **Ruling on chords alone would not unblock them.** A build agent must not pick a replacement chord alone, and each answer constrains the others. | **Take one ruling over the whole map.** Note the asymmetry: for `Ctrl+I` the mode toggle **already has its correct chord (`Ctrl+H`)**, so that one costs nothing to release. |
| **U-7** | **Physical printer output (row 12.5).** | Avalonia ships no printing API; delivering it means a platform dependency or a P/Invoke to the Win32 spooler. **W2-31's "number of copies" is meaningless until this is answered**, and 12.4 is blocked behind it. | State the decision either way and record it — **"print means PDF" is a defensible product choice, but it must be a recorded divergence rather than an accident.** |
| **U-8** | **JPEG report export (inside row 13.6).** | HTML, XML, JSON and ASCII are writers over the **existing** tabular projection — and **the XML and JSON writers already exist** on the whole-company surface. **JPEG alone needs a rasteriser**: a dependency, or a hand-rolled encoder. | **Carve JPEG out**, take the other four cheaply, and **record that 13.6 cannot then be marked closed.** |
| **U-9** | **TallyVault (row 16.1).** | Company-data encryption behind a passphrase means SQLCipher or an equivalent crypto dependency. 🔴 **And there is an architecture collision nobody had recorded: the source says a TallyVault password encrypts the company *"including the company name"*, while our companies live in a `.db` NAMED AFTER THE COMPANY** — the same constraint item 9 records as the reason rename is out of scope. **It cannot be bolted on: the filename leaks the plaintext the feature exists to hide.** | Decide the dependency **and** the storage-layout change together, or defer both. R13 (secrets) applies. |
| **U-10** | **Multiple GSTIN registrations (6.23) · Multi-company shell (14.2 Switch To, 16.7 Group Company) · WhatsApp sharing (14.10) · e-Payments file format (8.10) · GST Classification master (6.25) · Kerala Flood Cess (6.26) · `IntegrateAccountsWithInventory` · charting approach (14.3) · Show-Inactive scope (2.13).** | Each is an architecture or scope decision carried over from the wave-2 breadth design's rulings **R2 – R17**; they are listed together here only so the batch is complete. **The full text of each is in `plan.md`'s wave-2 block.** | Take them **with** the wave-2 dispatch decision (U-4), not before it — several become moot if U-4 says "cheap rows only". ⚠️ **Two carry a specific warning: `IntegrateAccountsWithInventory` is behaviour-bearing and needs an oracle harness first (three negative-stock attempts produced three DIFFERENT unbounded Balance-Sheet errors that each passed the full suite), and 14.3 already CONTRADICTS `plan.md`'s claim of a delivered graphical dashboard.** |
| **U-11** | 🔴 **NEW 2026-09-04 (wave-3, §1.3 item 22). DOES THE DENOMINATOR GAIN TWO ROWS? The vendor's `Alt+Y` Data estate is *Backup & Restore · Import · Migrate · Synchronise · Repair · Export · Split · Extract/Share*. The census has rows for Repair (16.6), Split (16.5) and Group Company (16.7), and excludes Synchronisation by architecture — but there is NO row for MIGRATE-AS-AN-OPERATOR-ACTION and none for EXTRACT/SHARE (ODBC / FTP / Pivot).** Row **13.9** is the closest thing to a Migrate row **and it describes a different mechanism entirely** (an automatic on-open side effect, versus a pausable, resumable, pre-checked, summarised menu action — T2-31). | **Adding a row MOVES THE 216 DENOMINATOR**, and §1.2b records that the denominator has already moved twice for scope reasons under ruling 10. **No agent may move it.** ⚠️ **And the choice is not cosmetic: adding a Migrate row would make 13.9 `COMPLETE` while a genuinely absent capability sat beside it, which is a truer picture than one `COMPLETE` row carrying a scope caveat — but it also raises the "missing" count by two on the day before a deadline.** | **RECOMMEND: add MIGRATE (one row) and NOT Extract/Share.** Migrate is a documented operator-facing capability we do not have and a business would miss; **Extract/Share was already ruled out of scope** at census line 754 and re-opening it is a bigger question than this one. **Trade-off: `216 → 217`, absent `68 → 69`, and every anchor figure's denominator changes with it.** ⚠️ **If the answer is NO, T2-31's scope sentence on row 13.9 is the whole record and must not be quietly deleted.** |

---

**Bottom line for the user** *(every figure below is as of **2026-09-05** and copied from its derivation — the capability split from §1.2, which is itself summed from §1.2a; the fidelity numbers from §1.3; the TIER 0 count from §2 TIER 0. §1.3's anchor block pins the last two. If a figure here disagrees with its derivation, the derivation is right and this paragraph is stale)*. A perfect clone needs **216 named capabilities — and this is the first version of this document in which you can read which ones**. We have **52** whole, **105** partial, **59** missing. *(🔴 Re-derived 2026-09-05 **a second time, in the b1/b3/b4/b5 landing**, by re-running §1.2a's own counting command; its LITERAL output is `TOTAL rows=216 C=52 P=105 A=59 U=0 sum=216`. Earlier the same day it read ~~*"**46** whole, **102** partial, **68** missing"*~~ and before that ~~*"**47** whole, **96** partial, **73** missing"*~~ — every one of those was stale in turn, and **no digit here was typed to a target.** The move is `+6 complete, +3 partial, −9 absent`; the nine rows are named in §1.2's banner. **It would have been eleven** — rows 6.10 and 6.13 are written, reviewed and gate-green — **but the branch carrying them was held on a red CI leg and is not on `main`, so on `main` those capabilities are still unreachable and the rows did not move.** That is the standard this pass held: a row moves when a USER can reach the capability, not when the code exists.)* *(The **115 · 42 / 44 / 21** you may have seen quoted elsewhere is the superseded 2026-08-10 snapshot, and **200 · 47 / 95 / 58** is the 2026-08-18 one; §1.2b explains the first move — mostly granularity, and an absent column that was provably too small — and §1.2's banner explains the second, which is a **scope decision**: user ruling 10 brought §3's nine and §4's seven into the denominator, `200 + 9 + 7 = 216`.)* Only **21** capabilities have ever been checked against a source for correctness as shipped, so the fidelity denominator is **195** wide open; one of those — voucher alteration — has its grounding banked ahead of the slice that builds it, which leaves **194** with no sourced verification of any kind. *(🔴 Re-derived 2026-09-05 in the same merge by re-running §1.3's grade command; its LITERAL output is `21 [GRADE: COMPARED]` / `1 [GRADE: GROUNDED-AHEAD]` / `1 [GRADE: METHOD-NOTE]`, which is the anchor block `21 · 22 · 195 · 194`. It read ~~*"**11** … **205** … **204**"*~~ here.)* **▶ And as of 2026-08-19 that gap is the goal itself: user ruling 9 makes "done" mean FULL PARITY *and* CORPUS VERIFICATION — with the honest limit that where the corpus is silent a capability ships as a documented divergence labelled as ours, and can never join the 21.** 🔴 **AMENDED BY RULING 14 (2026-09-04): read *"CORPUS VERIFICATION"* above as **VENDOR-DOCUMENTATION VERIFICATION** and *"where the corpus is silent"* as *"where no source in R7's order speaks"*. Only the source's name changed; the bar did not.** The most urgent items are not the missing ones: they are the **open TIER 0 defects** 🔴 **(COUNT DELIBERATELY NOT RESTATED, 2026-09-05. It read ~~*"eleven"*~~ and was stale, but unlike the capability split and the §1.3 grades the TIER 0 register carries **no single machine-readable state token** - a row's closure is prose (`✅ CLOSED`, `**CLOSED 2026-08-17**`, `~~struck~~`), so there is no command to re-run and any digit here would be typed to a target. The register held **27 rows** at this merge, counted by `awk` over the §2 TIER 0 table. **Re-walk it and classify the rows yourself; adding a bare state token to that table is filed as an open item.**)** — of which **nine are confirmed wrong-money-or-invalid-document defects a business would suffer today**, and **two (T0-5's 4% cess, T0-6's blog-cited TDS rates) are statutory figures the product applies to money on sourcing nobody can stand behind — confirmed unsourced, not confirmed wrong**. Two of the nine are new today and both are payroll: **recording an attendance period twice silently doubles the pay**, and **a leaver accrues gratuity and bonus for ever**. All of it sits on top of a book that still cannot be fully corrected: **no voucher can be ALTERED at all**, and **eight of the classic eighteen voucher kinds can be neither cancelled nor deleted nor even listed in the Day Book** (T1-17) — cancellation and deletion shipped for the other ten.

> **▶ 🔴 THE PARAGRAPH ABOVE IS THE 2026-08-19 RECORD AND EVERY FIGURE IN IT IS NOW STALE. IT IS MARKED RATHER
> THAN REWRITTEN, because it is the sentence a reader quotes and the point of the as-of date is that the drift is
> VISIBLE. Restated as of 2026-09-04, each figure pointing at its derivation rather than re-asserting it:**
> - **The capability split is now `46 complete · 102 partial · 68 absent · 0 undetermined` against the same 216**
>   — §1.2, re-summed from §1.2a on 2026-09-04 **after the wave-3 fold-in**. It read ~~*"47 complete · 101 partial ·
>   68 absent"*~~ earlier the same day; **row 9.3 then moved `COMPLETE` → `PARTIAL`** on the first comparison Area 9
>   has ever had. 🔴 **THE FIRST DOWNWARD STATE MOVE THIS YEAR, AND IT IS THE HEALTHIEST NUMBER IN THIS BLOCK:
>   measuring a row against a source made the product's score WORSE, which is what an honest measurement looks
>   like.** The paragraph above says ~~*"47 whole, 96 partial, 73 missing"*~~,
>   which was the 2026-08-19 snapshot. 🔴 **And read the shape of that move before reading it as progress: the
>   three rows that moved on 2026-09-04 (12.8, 16.3, 16.4) were ALREADY PARTIAL in the product — nothing was
>   built that day. The census had fallen behind itself.**
> - **The fidelity figures are now `21 · 22 · 195 · 194`** — §1.3's anchor block, which is the single derivation.
>   It read ~~*"`16 · 17 · 200 · 199`"*~~ earlier on 2026-09-04, and the paragraph above says ~~*"Only **11**
>   capabilities have ever been checked … the fidelity denominator is **205** wide open … leaves **204** with no
>   sourced verification of any kind"*~~. **Eight items were added on 2026-09-04 across two fold-ins** — three from
>   wave 2 (80 comparisons over ~40 rows) and **five from wave 3 (78 comparisons over ~98 rows)** — **counted as
>   +3 and +5 because the ITEM is the unit (U-2).** 🔴 **State the honest reading, because `21 of 216` is still
>   under ten percent and the move looks larger than it is: of wave 3's 78 comparisons, 63 DIVERGE. The figure
>   counts capabilities MEASURED against a source, never capabilities that PASSED** — which is exactly why it is
>   worth having, and exactly why it must never be inflated.
> - 🔴 **AND THE ONE THING THE PARAGRAPH ABOVE COULD NOT HAVE SAID, WHICH NOW GOVERNS EVERYTHING: `tally/` IS
>   EMPTY.** Its closing sentence — *"user ruling 9 makes 'done' mean FULL PARITY **and** CORPUS VERIFICATION"* —
>   **is exactly the goal that is currently unreachable by construction.** Not one of the three new items is
>   corpus-verified; all three rest on official vendor documentation and primary legal instruments. **See U-0.**
> - **TIER 0 now holds 27 rows** *(re-counted 2026-09-05 by `awk` over the §2 TIER 0 table; it read ~~*"fourteen"*~~)*, T0-25 having been added 2026-09-04 (professional-tax slabs shipped as
>   live money under an "A14-verified" label with no citation). **Re-walk the §2 TIER 0 table and count its row
>   markers; never carry that digit forward from this sentence either.**
> - **Two of the TIER 0 rows the paragraph calls *"confirmed UNSOURCED, not confirmed WRONG"* have changed
>   status in opposite directions, and this is the best news on the page:** **T0-6's** TDS/TCS sourcing is
>   substantially CLOSED — §194A's rate is now cited to the Finance Act 2025 First Schedule and every with-PAN TCS
>   rate to the bare §206C at its Year-2025 slug, so the commercial charts are no longer load-bearing — while
>   **T0-5's** 4% cess is re-verified as *correct* but its **sole citation has 404'd**, and the real defect behind
>   it turns out to be **T1-26**: the salary engine has no year dimension at all.