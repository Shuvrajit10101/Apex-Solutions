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
| 1 | Company creation & configuration (F11/F12) | 9 | 0 | 6 | 3 | 0 |
| 2 | Accounting masters | 13 | 0 | 8 | 5 | 0 |
| 3 | Inventory masters | 15 | 2 | 11 | 2 | 0 |
| 4 | Voucher types (7.2's classic eighteen) | 18 | 5 | 13 | 0 | 0 |
| 5 | Voucher behaviours & edit verbs | 15 | 5 | 6 | 4 | 0 |
| 6 | Statutory, current law (GST, TDS/TCS, salary IT) | 42 | 18 | 14 | 10 | 0 |
| 7 | Payroll | 21 | 6 | 10 | 5 | 0 |
| 8 | Banking | 10 | 1 | 4 | 5 | 0 |
| 9 | Inventory / manufacturing / job work (post-7.2) | 9 | 4 | 1 | 4 | 0 |
| 10 | Accounting features (post-7.2) | 2 | 0 | 0 | 2 | 0 |
| 11 | Reports | 17 | 2 | 12 | 3 | 0 |
| 12 | Printing | 9 | 1 | 3 | 5 | 0 |
| 13 | Data management (import/export/backup/e-mail) | 10 | 2 | 6 | 2 | 0 |
| 14 | TallyPrime-only capabilities | 10 | 1 | 2 | 7 | 0 |
| 15 | Statutory, obsolete by law (pre-GST) — **was §3** | 9 | 0 | 0 | 9 | 0 |
| 16 | Formerly excluded by decision (security, audit, data structure) — **was §4** | 7 | 0 | 1 | 6 | 0 |
| | **TOTAL** | **216** | **47** | **97** | **72** | **0** |

**A full clone requires 216 named capabilities. We have 47 complete, 97 partial, 72 absent, 0 undetermined.**
Every `COMPLETE` still means **present and reachable**, never *correct*; §1.3 holds the fidelity figures and is
the only place they are maintained.

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

**▶ 🔴 EXPECTED RUN FROM 2026-08-20 ONWARD: `TOTAL rows=216 C=47 P=97 A=72 U=0 sum=216`.** This supersedes the
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

#### Area 1 — Company creation & configuration (F11/F12) · 9 rows · 0 complete / 6 partial / 3 absent

| # | Capability | State | Evidence · gap · disagreement |
|---|---|---|---|
| 1.1 | Company Creation — the profile capture screen | PARTIAL | Twelve bound fields on `CompanyProfileViewModel`, with corpus-matched labels. **Gap:** the five contact fields, the three base-currency formatting toggles, "decimal places for amount in words" (no domain property), the whole Security Control heading, Directory, and Group Company / Alt+R. Post-save hands off to the Gateway, not to F11 — a departure recorded in §1.3 row 9. |
| 1.2 | Company Alteration (Gateway → Masters → Alter Company) | PARTIAL | Same view model; eleven editable fields; accept path verified. **Gap:** company Name is read-only (a storage constraint of ours). **No Alt+K company menu** — that chord is bound to Saved Views and only in report context (14.9). ⚠️ §1.3 row 9 cites that binding at `MainWindow.axaml.cs` line 653; **at HEAD it is line 757** — content drift, not a dangling citation. |
| 1.3 | Company Select / open an existing company | PARTIAL | Enumerates stored companies plus Create Company (F3) and Load Robert Demo. **Gap:** no named **Shut Company** — zero `Shut` hits in `src/Apex.Desktop`; closing is a side effect of Esc collapsing the cascade. |
| 1.4 | Company Rename and Company Delete | ABSENT | `CompanyStorage.Delete(CompanyEntry)` is declared and has **zero callers** in `src/` or `tests/` — dead code. No rename code at all, no Screen member, no menu row. Delete was split out by ruling VL-2; rename is absent outright. |
| 1.5 | F11 Company Features — the Statutory Configuration page | PARTIAL | `GstConfigViewModel`, 21 observable switches, titled "Statutory Configuration (F11)"; hosts GST, TDS, TCS, PF/ESI/PT, salary TDS, gratuity, bonus. **Gap:** one flat page, not Tally's F11 group structure. |
| 1.6 | F11 Company Features — Inventory & Payroll feature toggles | PARTIAL | Batch-wise details, BOM + component type, multiple Price Levels, Job Order Processing, Maintain Payroll + Payroll Statutory, each applied live. **Gap:** 🔴 `Company.WarnOnNegativeStock` is persisted and honoured by `InventoryPostingService` with **zero** hits in `src/Apex.Desktop` — shipped behaviour with no control anywhere (the W0-5 row, still unshipped). `UseSeparateActualBilledQuantity` is toggled from the **voucher-entry** screen, not from here (see 3.14). No Integrate Accounts with Inventory, no maintain mode. |
| 1.7 | F11 Company Features — the Accounting Features group | ABSENT | Zero `IntegrateAccountsWithInventory` and `MaintainMode` hits in `src/` or `tests/`. No per-company switch for bill-wise, interest, cost centres, multi-currency, budgets, credit limits, cheque printing or multi-address. **= T1-15.** |
| 1.8 | F12 Configure — the global configuration tree | ABSENT | `F12Configure()` has three real arms and then a fall-through that literally sets a stub message string. No configuration-tree type, no Screen member, no menu row. **= T1-16.** |
| 1.9 | F12 Configure — per-screen context panels | PARTIAL | Four real panels exist off the key tunnel: print-preview config, report config, Alt+F12 report sort/filter, and the Ledger-master / voucher-numbering arms. **Gap:** every other screen — all master screens except Ledger, every voucher screen except through the numbering column, every report surface with no `Reports` object — falls through to the stub. |

#### Area 2 — Accounting masters · 13 rows · 0 complete / 8 partial / 5 absent

| # | Capability | State | Evidence · gap · disagreement |
|---|---|---|---|
| 2.1 | Accounting Group master — create / alter / delete | PARTIAL | All three verbs verified: create from the Create column, `ForAlter` from a Chart-of-Accounts row, Alt+D delete guarded by `MasterDeletionRules.EnsureGroupDeletable`. **Gap:** no Display verb, no multi-group create, and the v51 group-level GST block has no capture field (3.13). |
| 2.2 | Group behavioural flags — sub-ledger, nett debit/credit, used for calculation, allocation method | ABSENT | All four identifiers return zero hits over `src/`. `Group` carries only Id, Name, Nature, ParentId, Alias, IsPredefined and Gst. ⚠️ Tally's field names for these are themselves **UNVERIFIED** against this corpus (§6 item 6) — the absence from our code is confirmed; what Tally calls them is not. In T2-3. |
| 2.3 | Ledger master — create / alter / delete | PARTIAL | ~30 bound fields; all three verbs, delete guarded by `EnsureLedgerDeletable`. **Gap:** Alias is deliberately not capturable (named in the view model's own "not written, on purpose" list); no credit limit (10.1); no multi-address (10.2); no multi-ledger create (2.12); no Display verb. |
| 2.4 | Voucher Type master — create / alter / display / delete | ABSENT | No `VoucherTypeMasterViewModel` among the ~110 view models; no Screen member; no Create-menu row; `MasterCreateKind` has no member for it. Exactly **one** of `VoucherType`'s ~20 configurable properties is settable anywhere in the UI (`TrackAdditionalCosts`, from the purchase-invoice screen). ⚠️ **Area assignment ambiguous** — two surveys named this capability, one under Accounting masters and one under Voucher behaviours. Counted **once, here**; area 5 carries an uncounted cross-reference. **= T1-3.** |
| 2.5 | Voucher numbering configuration (F12 per voucher type) | PARTIAL | Prevent-duplicate, number width, prefill-with-zero and the prefix/suffix affix rows, on their own Screen. **Gap:** `MethodDisplay` is a get-only expression-bodied string with no setter and no picker in the XAML, so **Manual and None are unreachable** and every seeded type stays Automatic (5.10, **T1-5**). |
| 2.6 | Voucher Class | ABSENT | Zero `VoucherClass` hits in `src/Apex.Ledger` and zero in `src/Apex.Desktop`. No domain type, no persistence table, no view model, no Screen, no menu row. Interest auto-posting via a Debit/Credit-Note class is unreachable in consequence. ⚠️ Same area ambiguity as 2.4; counted once here. |
| 2.7 | Cost Category master | PARTIAL | Create only (name + the two allocate flags). No `ForAlter` and no highlighted-row route, so the existing rows carry no route; no delete service exists in `src/Apex.Ledger/Services`; no Display verb. |
| 2.8 | Cost Centre master | PARTIAL | Create only (name, category, parent). `CostCentre.Alias` is never captured. No Alter, no delete service, no delete route, no Display verb. |
| 2.9 | Budget master | PARTIAL | Create only, with lines targeting a Group or a Ledger. **Gap:** no cost-centre target (the target option carries no cost-centre id); no nested budget (the `UnderId` has no picker); no Alter; no Delete service. |
| 2.10 | Scenario master | PARTIAL | Create only (name, include-actuals, a tick-list of voucher kinds). **Gap:** `Scenario.ExcludeType` has zero Desktop callers, so an exclusion can arrive only through import; no period; no Alter; no Delete. |
| 2.11 | Currency master and Rates of Exchange | PARTIAL | Create for a currency and for a dated rate, with Existing and Rates lists; per-ledger currency selection exists. **Gap:** no Alter and no Delete for either (`RemoveCurrency` / `RemoveExchangeRate` have no Desktop caller); none of the four Tally currency formatting options exists here or on the company base-currency block. |
| 2.12 | Multi-master create (Multi Ledger / Multi Group) | ABSENT | Zero `Multi Ledger` / `MultiLedger` hits over `src/`. The Create column contains only single-master rows and the label dispatch has no multi-create case. In T2-3. |
| 2.13 | Show Inactive / hidden masters | ABSENT | Zero `Show Inactive` / `ShowInactive` hits over `src/` except one comment in `VoucherTypeResolver.cs` recording that the gesture "meant nothing". Every master's Existing list is an unconditional enumeration. Overlaps the Show-Inactive element of 5.11; the master-level capability is counted here, the voucher-type flag there. |

#### Area 3 — Inventory masters · 15 rows · 2 complete / 11 partial / 2 absent

| # | Capability | State | Evidence · gap · disagreement |
|---|---|---|---|
| 3.1 | Stock Group master | PARTIAL | Create only (name, alias, under, add-quantities). No Alter; `InventoryService.DeleteStockGroup` has **zero** hits in `src/Apex.Desktop`. |
| 3.2 | Stock Category master | PARTIAL | Create only. No Alter; `DeleteStockCategory` has zero Desktop hits. |
| 3.3 | Stock Item master — create / alter / delete | PARTIAL | The **only** inventory master with all three verbs; delete guarded by `EnsureStockItemDeletable`. **Gap:** no Display verb, no multi-item create, plus 3.4 and 3.6. |
| 3.4 | Stock item valuation method — Standard Cost | PARTIAL | 🔴 The method **is** selectable on the master screen — the dropdown is populated with all six methods and **is rendered** — and the create path passes the selection through unguarded, but **there is no bound input for the `StandardCost` value**, so valuation silently falls back to last purchase rate. **This CORRECTS T0-3's "reachable only through JSON/XML import" caveat**; see the T0-3 row and §1.3's anchor block. |
| 3.5 | Unit of Measure master (simple and compound) | PARTIAL | Create for both shapes, persisted. **Gap:** no Alter and no Delete — the list row type carries no Guid, so no row can address a unit, and `DeleteUnit` has zero Desktop hits. |
| 3.6 | Alternate units per stock item | ABSENT | Zero `AlternateUnit` / "Alternate Unit" hits over `src/`. `StockItem` carries a single base unit and `VoucherInventoryLine` has no alternate-unit quantity. In T2-3. |
| 3.7 | Godown / Location master | PARTIAL | Create only (name, alias, under, third-party). **Gap:** no "Allow storage of materials" (zero hits — ⚠️ the Tally field name is UNVERIFIED per §6 item 6), no address block, no Alter, no Delete route. |
| 3.8 | Batch / Lot master | PARTIAL | Create with manufacturing/expiry dates or expiry period, opening quantity and rate; menu row gated on the F11 batch flag. No Alter, no Delete route. |
| 3.9 | Bill of Materials master | PARTIAL | Create with component lines, By-Product/Co-Product/Scrap typing and carve-out rate/percent; gated on the F11 BOM flag. No Alter, no Delete route. ⚠️ **Counted once here**; a second survey named the same capability under area 9, which carries an uncounted cross-reference. |
| 3.10 | Price Level master | PARTIAL | Create (name only) with an Existing list, gated on the F11 price-level flag. No Alter, no Delete route. |
| 3.11 | Price List — dated slab rates per level and item | PARTIAL | Slab rows (from/to quantity, rate, discount), an applicable-from date and a version history. **Gap:** revision is by saving a **new dated version**, not by altering one; no route deletes a list or a version. |
| 3.12 | Reorder Levels master | PARTIAL | Create with scope (item / group / category), simple or advanced quantities, consumption period and Higher/Lower criteria. **Gap:** alteration is an **upsert only** — creating for an existing scope+target replaces it; no Alter screen, no Delete route. |
| 3.13 | GST details capture on the Stock Group and accounting Group masters (the v51 hierarchy levels) | ABSENT | 🔴 The **storage** shipped — `MasterGstDetails` on `Group` and `StockGroup`, `DefaultGst` on `GstConfig`, the v51 columns in `Schema` — and **`MasterGstDetails` has exactly ONE hit in `src/Apex.Desktop`, a doc comment**: `Services/CompanyStorage.cs` line 95 names `MasterGstDetails.EnsureValid` while explaining why the validation floor sits in the storage choke point. **There is no view-model property and no XAML field**, and both master screens show only name/alias/under. The only writer is the importer. **This is the UI half of T0-4** and the reason that defect is still open. *(🔴 **Wording corrected 2026-08-18. The GRADE is unchanged and correct** — ABSENT is about no view-model property, no route and no caller, and a doc comment is none of those. This cell said **"zero hits"**, which is falsifiable by one grep and was false; a reader who ran it would have had grounds to distrust the row's evidence rather than its wording. The single hit is a comment, so nothing about the capability changes.)* |
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
| 4.7 | Credit Note (Alt+F6) — sales return | PARTIAL | §34 original-invoice capture present. **Moves no stock** (**T0-10**). 🔴 **RE-ATTRIBUTED 2026-08-20 — THE T0-11 HALF OF THIS ROW IS REFUTED.** It read ~~*"and **never prints in invoice format** (**T0-11**)"*~~, which blames the print gate. **A Credit Note cannot carry inventory lines AT ALL**, so the print gate is not what stops an item table appearing: `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` throws *"Item-invoice stock lines are only valid on a Purchase or Sales voucher"* on **every** post (reached from `src/Apex.Ledger/Services/VoucherValidator.cs:150-151`), and `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` makes the item-invoice chord inert on this family. **That wall is T0-10, not T0-11** — and flipping the print gate alone would route a note into the invoice projection and emit a **ZERO-ROW document**. ✅ **AND THE NOTE DOCUMENT DOES NOT NEED THE WALL REMOVED:** CGST **Rule 53** is value-level (nature of the document · corresponding invoice serial and date · value, rate and amount credited/debited — no HSN, no quantity, no per-item lines), so the legally complete note is **RQ-11b** and it ships with **no dependency on T0-10**. *(Verified first-hand at those exact lines on 2026-08-20 before being written down.)* |
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

#### Area 5 — Voucher behaviours & edit verbs · 15 rows · 5 complete / 6 partial / 4 absent

> **▶ 🔴 HEADING RE-DERIVED 2026-08-20.** It previously read ~~*"15 rows · 5 complete / 5 partial / 5
> absent"*~~. **Row 5.1 alone moved** (`ABSENT` → `PARTIAL`, Phase 10.11 S5a–S5e). The ABSENT set for this area
> is now **5.4, 5.5, 5.10, 5.11** — four, not five. Re-derived by re-running §1.2a's counting command, not by
> editing a digit; §1.2's area-5 row is the column sum of these fifteen rows and was re-summed with it.

> **Uncounted cross-references** (counted in the area named): **Voucher Type master → 2.4**; **Voucher Class →
> 2.6**; **the Day Book itself → 11.4**. The two-collection finding that governs eight of area 4's rows is
> carried in 11.4's gap column and filed as **T1-17**.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 5.1 | Voucher alteration — open a posted voucher, change it, re-save | PARTIAL | 🔴 **BUILT — S5a…S5e, and this row graded it ABSENT for a day after it shipped.** Engine `Replace` (S5a), `ForAlter` rehydration (S5b), the carve inversions and CARRY table (S5c), the `Ctrl+Enter` wiring on three surfaces (S5d, `a34d989`), and the narrowing that opened purchase item invoices and gave POS its own door (S5e, `b89213e`). **Fidelity record: §1.3 item 12** — that is where the comparison and its two R7 categories live, and this cell deliberately does not duplicate them. **Gap, named:** the **SALES ITEM INVOICE is still refused by name on every key**, against a corpus route that attests altering one from the Day Book and the Sale Register (§1.3 item 12); several families stay `DEFER-DEFERRED` (service GST advance receipt — a user ruling, not a slice; purchase accounting invoice); and a re-accept still **silently destroys** a `BankAllocation` and a bill-wise `BillAllocations` on the legs named in **T1-22 / T1-23**. **▶ 🔴 THE SUPERSEDED CELL, QUOTED SO THE CORRECTION IS CHECKABLE:** ~~*"ABSENT — Searched four ways, all zero: the detail view model exposes no alter or save member; `ForAlter` exists in exactly three master view models and no voucher one; the entry view model has zero `Alter`/`Duplicate`/`Insert` occurrences; Ctrl+Enter is bound to stock-item alteration and nothing else. No Screen member."*~~ **Three of those four limbs are FALSE at HEAD** (measured 2026-08-20): `ForAlter` is declared in **five** view models — `AccountGroupMasterViewModel`, `LedgerMasterViewModel`, `StockItemMasterViewModel`, **`VoucherEntryViewModel`** and **`PosBillingViewModel`** — so *"three master view models and no voucher one"* is wrong twice over; the entry view model has **70** `Alter`/`Duplicate`/`Insert` occurrences at `b89213e` (53 at `a34d989`), not zero; and `Ctrl+Enter` is bound to voucher alteration on three surfaces through `MainWindowViewModel.RequestAlterHighlightedVoucher`, not to stock-item alteration alone. Only the first limb (the detail view model exposes no alter or save member) is still true, and it is true **by design** — `VoucherDetailViewModel` is the read-only column. **T1-1's alteration half is CLOSED by this row; its duplication and insertion halves stand (5.4, 5.5).** |
| 5.2 | Voucher cancellation (Alt+X) on a posted voucher | PARTIAL | 🔴 **BUILT — S3.** Key arm, gate, confirmation, engine cancel, greyed Day Book row, CANCELLED over-print, live-IRN/e-Way refusal. **Gap:** armed on **one** surface (the live Day Book) where the corpus scopes it to "Vouchers & Reports"; resolves only through the accounting aggregate, so no stock/order voucher can be cancelled; no un-cancel and no Cancelled Voucher register. |
| 5.3 | Voucher deletion (Alt+D) on a posted voucher | PARTIAL | 🔴 **BUILT — S4.** Key arm, five surfaces, `MasterDeletionRules` guards, engine delete. **Gap:** cannot delete a stock/order voucher (same aggregate boundary); deleting the highest-numbered **unfiled** voucher reuses its number — a known and accepted residual, not a silent one; no numbering floor. |
| 5.4 | Voucher duplication (Alt+2) | ABSENT | Zero hits for a duplicate-voucher verb; the only matches are the numbering feature's prevent-duplicate. No key arm, no menu row, no button-bar item. Corpus-attested and not built. |
| 5.5 | Insert Voucher (Alt+I) | ABSENT | Alt+I is spent on the POS tender-mode toggle. No insert-at-position code of any kind, no Screen member, no menu row. Corpus-attested and not built. |
| 5.6 | Add Voucher from a report (Alt+A) | PARTIAL | 🔴 **BUILT — and §1.3 item 12's grouping of Alt+A with the unbuilt Insert verb is wrong.** The key arm opens its own picker column beside the live report so the report survives, and the picker preserves the exact series. **Gap:** scoped to the Day Book alone, and the picker lists only active types, so an inactive series cannot be added. |
| 5.7 | Optional voucher (Ctrl+L) | PARTIAL | Flag, toggle, checkbox, key arm and balance exclusion all present. **Gap:** dispatched only on the accounting entry screen — inventory/order, POS, manufacturing-journal and job-work entries cannot be Optional, and `InventoryVoucher` has no Optional member at all; **a posted Optional voucher can never be regularised** (zero post-construction writers, no alteration screen); no Optional Voucher register. Filed as **T1-18.** |
| 5.8 | Post-dated voucher (Ctrl+T) | PARTIAL | Flag on both aggregates, dispatched to both entry screens, honoured by the balance walk. **Gap:** **zero post-construction writers**, so the flag can never be cleared when the cheque clears; no post-dated register or PDC summary (8.8). Filed as **T1-18.** |
| 5.9 | Automatic voucher numbering — date-effective affixes, width, prefill, prevent-duplicate | COMPLETE | Config screen, formatter, and enforcement on both posting services. |
| 5.10 | Voucher numbering method **Manual** / **None** | ABSENT | The method display is a get-only string, self-described "DISPLAY-ONLY this slice"; there is no setter and no bound control. The Voucher No. on all four entry screens is a `<Run>` inside a `TextBlock`, not a TextBox. The seed hard-codes Automatic throughout. **= T1-5.** |
| 5.11 | Voucher-type user flags — Use Common Narration, Print after saving, Show Inactive → activate | ABSENT | `VoucherType` has no common-narration and no print-after-saving member. "Show Inactive" returns exactly one hit in `src/`, a comment recording that the gesture meant nothing. The two inactive families are flipped only by `JobWorkService`; the other write site is a rollback restore inside a catch, not an activation route. |
| 5.12 | Voucher entry modes — As Voucher / Item Invoice / Accounting Invoice, and Single vs Double Entry | COMPLETE | Ctrl+I and Ctrl+H arms verified; the change-mode gate was widened to Contra/Payment/Receipt so Single Entry is reachable on the three kinds that have it; the accounting-invoice mode is persisted structurally. |
| 5.13 | Bill-wise details on a voucher line (New Ref / Agst Ref / Advance / On Account) | COMPLETE | Per-line model with the sum-to-line-amount rule; the sub-panel renders on the plain grid, on Single Entry and on both invoice modes. |
| 5.14 | Cost-centre allocation on a voucher line | COMPLETE | Allocations drive the sub-panel, feed the posted entry line and are consumed by the cost reports. |
| 5.15 | Batch / lot allocation on a voucher line (FEFO/FIFO default, expiry warning) | COMPLETE | Its own cascade column, wired from both the accounting item-invoice path and the inventory entry path, with the engine's default issue selection. |

#### Area 6 — Statutory, current law (GST, TDS/TCS, salary IT) · 42 rows · 18 complete / 14 partial / 10 absent

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
| 6.4 | GST rate hierarchy above the Stock Item — company default / Accounting Group / Stock Group, and the source-of-HSN and source-of-rate order options | ABSENT | Persistence exists; **no UI writes it and no service, report or view model reads it**. Resolution is still item → ledger → unresolved. **= T0-4**, and 3.13 is its capture half. |
| 6.5 | GST computation on a voucher — CGST/SGST vs IGST routing, per-rate line tax, cess, round-off leg | COMPLETE | Engine entry points with both desktop callers (voucher entry and POS). |
| 6.6 | Reverse charge (RCM) — inward dual leg, import of services, outward RCM flag, 3B tables 3.1(d) / 4(A)(2) / 4(A)(3) | COMPLETE | Service, live panel, supply-kind picker and the 3B projection. |
| 6.7 | GST on advance receipts — tax on advance, adjustment against invoice, GSTR-1 tables 11A / 11B | COMPLETE | Service, entry wiring and both projections. |
| 6.8 | GSTR-1 outward return on screen (period-scoped, printable and exportable) | PARTIAL | B2B 4A, rate-wise B2C, HSN 12, a single exempt bucket, 4B outward RCM, 9B credit/debit notes, 11A/11B. **Gap:** seven form tables unmodelled — 5, 6A, 6B, 6C, 7, 8's four-way split, 13; the B2C row type carries **no Place-of-Supply member at all**, which is what blocks 5 and 7. **= T1-12.** |
| 6.9 | GSTR-3B summary return on screen | PARTIAL | 3.1 by head, 3.1(d) RCM, 4(A)(2)/(3), 4(B)(1)/(2), 4(D)(1). **Gap:** 3.1 is a single taxable-outward value, not the four-way split; zero hits for tables 3.1.1, 3.2, 5 and 5.1. **= T1-12.** |
| 6.10 | GSTR-1 / GSTR-3B portal JSON — the artefact that actually gets filed | ABSENT | The JSON writer class exposes exactly five writers (CMP-08, GSTR-4, 9, 9A, 9C) and **no GSTR-1 or 3B emitter anywhere**; the class itself has **zero production callers** — the only references in `src/` are two doc comments. **= T1-11.** |
| 6.11 | Composition returns CMP-08 (quarterly) and GSTR-4 (annual) | PARTIAL | Both engine projections and both screens, gated on the composition flag. **Gap:** **no output of any kind** — neither view model writes a file and neither is a report page, so no print and no export. The matching JSON writers exist and are never called. **In T1-10.** |
| 6.12 | Annual returns GSTR-9 and GSTR-9C | PARTIAL | Both projections and both screens, reachable for a regular dealer. **Gap:** identical to 6.11 — no print, no export, dead JSON writers. **In T1-10.** |
| 6.13 | GSTR-9A (composition annual return) | ABSENT | The only `Gstr9a` hits are engine-side: an uncalled JSON writer and two report files that mention it. No Screen member, no view model, no menu label case. |
| 6.14 | e-Invoice (IRN) — coverage decision, offline INV-01 JSON, recording the IRP response, cancellation | PARTIAL | Coverage, prepare, record-response, cancel and reporting-age all present with desktop callers. **Gap:** **no live IRP submission** — every online connector throws from every member and one has zero construction sites — and the **IRN and signed QR never reach the printed document**, structurally, because the PDF writer has no image primitive. **= T0-9.** |
| 6.15 | e-Way Bill — Part-A/Part-B, EWB-01 offline JSON, portal response, cancel, extend, close | PARTIAL | Eight engine entry points, all with desktop callers. **Gap:** no live NIC submission (same stub connectors); the **Consolidated e-Way Bill (EWB-02) is engine-only** — zero Desktop callers. |
| 6.16 | GSTR-2B import, reconciliation, and IMS (accept / reject / pending) | COMPLETE | Reconciler, IMS service, JSON parser, and three routes with callers. |
| 6.17 | ITC set-off (Rule 88A with the §49(5)(c)/(d) proviso) and cash discharge via a PMT-06 challan | COMPLETE | Both services, the GST-Actions route and the posting caller. §1.3 item 6 is its fidelity row. |
| 6.18 | ITC reversal posting (Rules 37/37A/38/42/43, §17(5)) and the reversal report | COMPLETE | Service, the 3B reversal tables, and both routes. |
| 6.19 | Advanced-GST read-only screens — Electronic Ledgers, ITC Set-Off view, ITC Gate, QRMP/IFF, GST Amendments, e-Invoice/e-Way Status | PARTIAL | All six exist and all six dispatch. **Gap:** all six are **output dead ends** — none writes a file and none is a report page, so none can be printed or exported. QRMP is a PMT-06 advisory only; its IFF rows are a window view, not an upload artefact. **In T1-10.** |
| 6.20 | DRC-03 voluntary payment / demand discharge | ABSENT | 🔴 The **engine verb exists and is complete** — a deposit-service posting method with its own record type — and `Drc03` returns **zero hits across all of `src/Apex.Desktop`**. No Screen member, no view model, no menu case. Reachable only by JSON/XML import. Filed as **T2-9.** |
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

#### Area 7 — Payroll · 21 rows · 6 complete / 10 partial / 5 absent

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
| 7.16 | Alter and Delete on the payroll masters — all eight kinds | ABSENT | 🔴 **Stated once, as a capability in its own right rather than eight coincidences.** `ForAlter` exists in exactly three master view models tree-wide and **none is a payroll master**; every one of the eight payroll master view models returns zero for `Alter` and `Delete`. The payroll service **advertises** create/alter/delete in its own doc comment and nothing reaches the last two. Sole exception on the alter side: the income-tax declaration reloads an existing declaration — and it too has no delete. |
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

#### Area 9 — Inventory / manufacturing / job work (post-7.2) · 9 rows · 4 complete / 1 partial / 4 absent

> **Uncounted cross-references:** *Bill of Materials master → 3.9*; *Additional Cost of Purchase → 3.15*;
> *Actual-vs-Billed → 3.14*. All three were named here by one survey and under Inventory masters by another.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 9.1 | Job Order Processing — the F11 toggle and its four voucher kinds | COMPLETE | The service flips the company flag **and** activates the four seeded-inactive kinds, stamping the job-work and consumption flags; the F11 handler drives it with a rollback on failure; four menu rows gated on the flag. |
| 9.2 | Job Work order entry and Material In/Out movement entry | PARTIAL | Three entry view models posting through the inventory posting service, with the movement valuation in the job-work service. **Gap:** entry only — with no voucher alteration anywhere (5.1), a mis-keyed order or movement can be neither corrected nor (per T1-17) cancelled or deleted. |
| 9.3 | Job Work registers — In Order Book, Out Order Book, Material In Register, Material Out Register | COMPLETE | One engine report file with per-component pending arithmetic; four menu rows under their own header, surfaced only while the F11 flag is on. Existence and reachability only — content never compared to Tally. |
| 9.4 | Manufacturing Journal (BoM-driven production voucher) | COMPLETE | Service, entry screen, menu row with the Alt+F7 hint gated on the BOM flag, and an opener that auto-creates the user type over the Stock Journal parent. |
| 9.5 | POS invoicing (multi-mode tender, POS register, POS receipt) | COMPLETE | Tender service, register projection, receipt PDF and data, billing screen, two menu rows, and an opener also reached when a POS-flagged Sales type is chosen. |
| 9.6 | Job Costing | ABSENT | 🔴 **Resolved — one of the eight cannot-tell rows.** Case-insensitive search of every `.cs` and `.axaml` in `src/` for the phrase and the identifier returns **zero**. No service, no view model, no Screen member, no report file, no menu row. |
| 9.7 | Item Cost Tracking | ABSENT | 🔴 **Resolved — one of the eight cannot-tell rows.** Case-insensitive search returns exactly one hit and it is unrelated (a comment about *additional*-cost tracking). No type, no screen, no report, no menu row. |
| 9.8 | Tracking Numbers linking Receipt Note ↔ Purchase and Delivery Note ↔ Sales | ABSENT | Zero `TrackingNumber` identifiers anywhere in `src/`; the two "Tracking No" strings are doc comments quoting the corpus. Order fulfilment is **inferred** by a FIFO walk over candidate movements, so there is no operator-entered tracking datum. **= T1-8.** |
| 9.9 | Transfer Journal as a **named** voucher kind | ABSENT | 🔴 **Resolved — one of the eight cannot-tell rows.** Zero hits for the phrase and the identifier. **Read this precisely:** the *function* is partly covered — an inventory line carries a godown with an in/out direction and the posting service handles the Stock Journal base kind with its own balance guard, so inter-godown movement **is** expressible. What is absent is the separately named kind. |

#### Area 10 — Accounting features (post-7.2) · 2 rows · 0 complete / 0 partial / 2 absent

> **This is the one area where the new list AGREES with the superseded table exactly** (`2 · 0 / 0 / 2 / 0`).
> The agreement is on a reconstruction, though — the old table named neither row.

| # | Capability | State | Evidence |
|---|---|---|---|
| 10.1 | Credit Limits on a ledger, with the over-limit block on voucher save | ABSENT | Case-insensitive search of every `.cs` and `.axaml` in `src/` for the identifier and the phrase returns **zero**: no domain property, no persistence column, no view-model field, no guard in the validator. ⚠️ Also named under Accounting masters by a second survey; counted **once, here**, per §1.1 rule 3 (earliest product that shipped it). In T2-3. |
| 10.2 | Multi Address (multiple mailing / shipping addresses per company and per ledger) | ABSENT | Zero hits for every spelling and for an address-book type. The party address is a single flat block of four columns and the company address a single block; no address-list type, no per-voucher address picker. In T2-3. |

#### Area 11 — Reports · 17 rows · 2 complete / 12 partial / 3 absent

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
| 11.5 | Account Books family — Cash Book / Bank Book / Ledger | PARTIAL | Column builder, three pickers, three show methods, an opener, the ledger-book projection and cash/bank classification. **Gap:** the family ships three books and **none of its registers** — see 11.6 and 11.7, which are counted separately rather than hidden inside this row. |
| 11.6 | Sales / Purchase / Journal / Credit Note / Debit Note Registers | ABSENT | Per-name greps over `.cs` and `.axaml` return **zero** for each. The single "Sales Register" hit is a prose comment; all `JournalRegister` hits are the **Reversing** Journal Register. No report kind, no Screen member, no Gateway row, no case in the ~180-case menu dispatch. **In T2-1.** |
| 11.7 | Group Summary / Group Vouchers | ABSENT | Zero hits for the identifier and the phrase. No report kind, no menu row, no dispatch case. **In T2-1.** |
| 11.8 | Statistics (voucher and master counts) | ABSENT | Zero hits over `.cs` and `.axaml`. No type, no report kind, no menu row. **In T2-1.** |
| 11.9 | Statements of Accounts — Outstandings (Receivables / Payables) with ageing buckets | PARTIAL | Bill record with overdue days, bucket type, default buckets and the build; column builder, two dispatch cases and an opener. **Gap:** it is a **dedicated page Screen**, so the report context is null and that single fact switches off **print, export, drill, F2/Alt+F2 period, F12 config, Alt+F12 sort/filter and Alt+K saved views at once**. Also no ledger-wise or group-wise view, no reminder letter, no confirmation of accounts (12.7). **In T1-9 and T1-10.** |
| 11.10 | Statements of Accounts — Cost Centre reports (Category Summary, Cost Centre Break-up) | PARTIAL | Engine reports, column builder, two dispatch cases, view model. **Gap:** the same dead end as 11.9 — dedicated Screen, no print, no export, no drill, no period or config panel. |
| 11.11 | Statements of Accounts — Interest Calculation, Forex Gain/Loss, Budget Variance | PARTIAL | Three engine reports, three routes, three view models. **Gap:** all three are dedicated Screens with the same six gestures off; none carries a bespoke export. |
| 11.12 | Inventory Books / Statements of Inventory (Stock Summary, Godown Summary, Stock Movement, Reorder Status, Batch-wise, Batch Age Analysis, Price List, five inventory registers, Order Register, POS Register, four Job Work books) | PARTIAL | Ten engine report files, one column builder with conditional sub-sections, and the report-kind builders. **Gap:** absent from the family, each on a zero-hit grep — **Stock Query, Movement Analysis, Stock Ageing** (the batch report is an **expiry** report, not an age-of-stock bucket report), **Stock Category summary** (every "Category Summary" hit is a **cost** category), **Sales/Purchase Order Summary, Bills Pending**. Only two of the inventory kinds drill; the other fourteen are dead ends. **In T2-1.** |
| 11.13 | Exception Reports (Negative Stock, Negative Cash/Bank, Memorandum Register, Reversing Journal Register) | PARTIAL | Four engine reports, a column builder, four dispatch cases and two builders. **Gap:** four of the reference product's ~nine. Absent on zero-hit greps: **Optional Voucher register, Post-Dated Voucher register, Cancelled Voucher register** (the flag now exists on the voucher and in the Day Book row, and nothing lists them), overdue receivables/payables exception views. **Dead field:** the memorandum row record carries a voucher id that the builder never assigns to the drill target, so Enter on a memo row is inert. **In T2-1.** |
| 11.14 | Cash Flow / Funds Flow / Ratio Analysis | PARTIAL | Three engine reports, a column builder, three dispatch cases and three builders. **Gap:** no drill (not in the drill switch), no comparative columns (the comparative map covers four kinds only); **Cash Flow Projection is absent** on a zero-hit grep. |
| 11.15 | Report drill-down (Enter / double-click on a row) | PARTIAL | The drill switch handles exactly **6 of the 45** report kinds; for the **32** dedicated report Screens the string "Drill" occurs in only four files under the view-model directory and **none of them is a dedicated report view model**, so **0 of 32** drill. **= T1-9, CONFIRMED unchanged at HEAD.** |
| 11.16 | Report parameters — F2 as-of, Alt+F2 period, Alt+F1 detailed/summary, F12 configure, Alt+F12 sort & filter, Alt+K saved views | PARTIAL | Four option types and three view models; every entry point gated on the report context. **Gap:** available on the 45 report kinds only. The report context requires a non-null `Reports`, which the sub-screen clear nulls for all 32 dedicated report screens — so GSTR-4/9/9C, ITC, both challan recons, BRS, Outstandings, Cost, Budget, Interest, Forex and the payroll and TDS certificate screens have no period control, no configuration and no saved views. |
| 11.17 | Multi-period / multi-column comparison (Alt+C New Column, Alt+N Auto Columns) | PARTIAL | Comparative type, two view models, two Screens, both gated on a supports-comparative predicate. **Gap:** the comparative map covers **4 of the 45** kinds and **0 of the 32** dedicated screens; Auto Columns offers a monthly axis and a scenario axis only. |

#### Area 12 — Printing · 9 rows · 1 complete / 3 partial / 5 absent

> **Uncounted cross-reference:** *cheque printing* → **8.4**. The five-document group one survey wrote as a
> single Printing row is split here: deposit slip → **8.6**, banking payment advice → **8.7**, and the
> remaining three are 12.7.

| # | Capability | State | Evidence · gap |
|---|---|---|---|
| 12.1 | Print Preview of a report and Save-to-PDF (P / Ctrl+P) | PARTIAL | Route, key binding, Screen, preview view model, report projector, print model, report PDF and the PDF writer. **Gap, four of them, and two are new findings:** (a) reachable only on the 45 report kinds plus a drilled voucher — the 32 dedicated screens are excluded (**T1-10**); (b) 🔴 **every wide report prints with BLANK column headings** — the print projector hard-labels column 1 and emits an **empty caption** for columns 2..n, while the real captions exist only in the **export** twin, so a printed Stock Summary or Order Register has no headings while its CSV of the same data does (filed as **T1-19**); (c) Save PDF has **no file dialog** — it writes to Documents under a title-derived name and silently overwrites (**T1-20**); (d) all text is ASCII-folded (every character above code point 126 becomes a hyphen) and cells are ellipsis-clipped rather than wrapped. ⚠️ One survey additionally **predicts** crore-scale figure truncation on an 8-column A4-portrait report from the writer's own width table; it says explicitly it did **not** render a PDF to confirm it, and it is recorded here as a prediction, not a measurement. |
| 12.2 | Print a voucher / tax invoice from a drilled voucher | PARTIAL | The detail view model selects an invoice or a plain voucher projection; invoice PDF, voucher PDF, print projector and print data all present. **Gap:** **Sales-only** — the tax-invoice predicate returns false unless the base kind is Sales, so Purchase item-invoices, Credit Notes and Debit Notes fall back to the plain Dr/Cr print (**T0-11**); and no IRN or signed QR on an e-invoiced supply (**T0-9**), structurally impossible while the writer has no image primitive (12.8). *(The Bill-of-Supply half of this path is counted at 6.22 and is COMPLETE.)* 🔴 **CORRECTED 2026-08-20 IN TWO PLACES, BOTH INSIDE THE STRUCK CLAUSE ABOVE.** **(i) THE CREDIT / DEBIT NOTE HALF IS REFUTED AND RE-ATTRIBUTED TO T0-10** — a note cannot carry inventory lines at all (`src/Apex.Ledger/Services/VoucherValidator.cs:257-259`, reached from `src/Apex.Ledger/Services/VoucherValidator.cs:150-151`; the chord is inert at `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68`), so the print gate is not the wall; see rows 4.7 and 4.8. **(ii) "THE TAX-INVOICE PREDICATE RETURNS FALSE UNLESS THE BASE KIND IS SALES" IS TRUE BUT IS NOT THE DEFECT** — it is the **correct** answer to *"may we ISSUE?"* (CGST §31(1)); the defect is the **call site** using it to answer *"should this RENDER with item detail?"* at `src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107`. **What remains under T0-11 here is the PURCHASE half alone.** ✅ **AND THAT HALF'S ITEM-INVOICE SHAPE IS CLOSED 2026-08-20 (Phase 10.13 slice S2)** — see row 4.6. The drilled Purchase item invoice now routes to the invoice projection through the classification seam and prints a `PURCHASE RECORD`. **Outstanding on this row:** the purchase **accounting-invoice** shape (S3), the Rule 53 note document (S4), and T0-9's IRN / signed QR. |
| 12.3 | Print configuration (F12 title override, narration on/off, copy marking) and page setup | PARTIAL | Config types with the Rule 46 copy labels, page size/orientation/margins/font sizes, route, Screen, key binding and view model. **Gap:** the config is **voucher/invoice only**, so a **report** print has no configuration beyond the page-size and orientation toggles; no margin control in the UI; company logo explicitly deferred. |
| 12.4 | Print format selector (Neat / Quick / Condensed / Dot-Matrix / Pre-Printed), number of copies, page range | ABSENT | Zero grep hits over `src/` for every one of those identifiers and phrases. ⚠️ One survey named this as a distinct absent capability in its prose while folding it into 12.3's gap column; it is given its own row here so the absent count is not understated. |
| 12.5 | Physical printer output (printer selection, print job, spooler) | ABSENT | Zero lines over `.cs` and `.axaml` for the dialog, settings, printing namespace, spooler, queue and ticket identifiers. There is no printer abstraction, no device enumeration and no spool call anywhere. "Print" means render a PDF into a byte array and write it to a file. **= T2-5.** |
| 12.6 | Multi-account printing / multi-voucher (range) printing | ABSENT | Zero grep hits for the identifiers and phrases. Nothing iterates a set of accounts or vouchers into one print job — the opener builds exactly one preview from exactly one report or one drilled voucher. **In T1-14.** |
| 12.7 | Delivery challan, reminder letter, confirmation of accounts | ABSENT | Zero hits for the reminder-letter and confirmation-of-accounts phrases; the nine "Delivery Challan" hits are all e-Way-bill document-kind prose, and no printable challan exists. **In T1-14.** *(Deposit slip and banking payment advice are 8.6 and 8.7.)* |
| 12.8 | Print engine capability floor — raster images, embedded fonts, colour | ABSENT | The PDF writer's entire public surface is begin-page, text, line, page-count and build. Zero hits in that file for any image, compression or font-embedding identifier; fonts are the standard-14 faces with no embedding. **Consequence: no logo, no QR, no barcode, no non-Latin script and no colour fill, ever, without replacing the writer.** **= T2-4**, and it is what blocks T0-9. |
| 12.9 | Payslip / POS receipt / TDS-TCS certificate document printing | COMPLETE | Five renderers, each with a verified caller: the payslip PDF from the preview construction, the POS receipt PDF, and the Form 16A / 27A / 27D PDFs from their screens. **Narrow scope:** COMPLETE for those five documents, not for document printing generally. |

#### Area 13 — Data management (import / export / backup / e-mail) · 10 rows · 2 complete / 6 partial / 2 absent

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
| 13.1 | Backup the open company to a versioned archive | COMPLETE | A real SQLite **Online Backup API** snapshot — not a file copy — verified with an integrity check, zipped with a schema-stamped manifest; screen flushes the aggregate first; route, menu row and an Alt+Y button-bar row. ⚠️ The version-gap audit's *"Backup / Restore — absent as such"* row is **STALE** and must not be cited. |
| 13.2 | Restore an archive over a company | PARTIAL | Staged beside the target with format, schema and checksum refusals before anything is touched; a two-step examine-then-apply screen with its own post-restore validity check and a pre-restore safety copy. **Gap, and it is WIDER than T1-7 states:** it can only ever restore **into the company already open**. Two independent locks — the opener returns early with no company and the Data menu bounces to Company Select, so on a machine with zero companies there is no route in at all; **and** the target-name property has **zero bindings in the XAML**, so even with a company open the target cannot be redirected. The engine signature would allow it. **T1-7 widened.** |
| 13.3 | Whole-company canonical export (JSON / XML) for interchange and re-import | PARTIAL | Both exporters, a screen with a format choice, an opener and a bare-key arm. **Gap: reachability only, and it is a trap.** There is **no menu row** — the Gateway's "Data" header carries exactly one child, Backup / Restore — and the Gateway header hint reads "Y: Data" while **bare Y opens Export Data and Alt+Y opens Backup/Restore**, so the one hint the screen gives points at the wrong screen. Filed as **T2-10.** |
| 13.4 | Import into the open company (canonical JSON / XML, flat CSV) | PARTIAL | Three parsers through a validate-before-apply, transactional import service with a duplicate policy. **Gap:** (a) **no Tally-XML reader** (zero `TALLYMESSAGE` hits) and no SDF reader, so no third-party Tally data can be ingested, and no Excel reader — the XLSX support is write-only; (b) imports only **into an already-open company**, so recovering a lost book means Create Company first; (c) no menu row — a bare-key arm on the Gateway root only. **In T2-6.** |
| 13.5 | Report and master-list export (E / Alt+E) to CSV, XLSX and PDF | PARTIAL | Route, gate, key binding, Screen, three writers and two projectors, with 26 master view models implementing the export source. **Gap:** (a) the **same report-context gate as print**, so the 32 dedicated report screens are excluded — exactly **10** of those 32 carry a bespoke per-screen export, leaving **22 with no egress in any form** (**T1-10**, figures re-verified at HEAD); (b) 🔴 **17 report kinds export with BLANK column headers** — the header map covers only 16 kinds and falls through to an empty array, so Batch-wise, Batch Age Analysis, Price List, the nine TDS/TCS kinds and the five payroll kinds emit a header row of empty strings (**T1-19**); (c) no folder browse dialog (13.10). |
| 13.6 | Report export in HTML, XML, JSON, ASCII or JPEG | ABSENT | The export-format enum has exactly three members and the extension switch covers only those three. No other writer, no other UI option. XML and JSON exist only on the **different** whole-company surface (13.3), which exports a company file and never a report. **= T2-6.** |
| 13.7 | E-mail a report or invoice | PARTIAL | Compose view model, EML composer and message, mailto builder, SMTP profile types and a settings screen; a button-bar row gated on printability. **Gap:** **nothing is sent and nothing can be** — zero `SmtpClient` / `System.Net.Mail` hits anywhere in `src/`, and the view model's own notice says so. Of the two documented offline hand-offs only one is reachable: the **mailto URI is computed and bound nowhere** in the XAML — a dead field of the same species as 8.4. The `.eml` goes to a fixed Documents path with no save dialog; the attachment is always PDF. **In T2-6.** |
| 13.8 | SMTP profile capture (outgoing-mail server settings) | PARTIAL | Profile type, repository interface and a persisted table; a settings screen and a button-bar row. **Gap:** capture-only dead field — nothing in `src/` reads the saved profile to open a socket, and the screen says so itself. No password is captured, by the R13 decision. |
| 13.9 | Automatic forward migration of an older company data format on open | COMPLETE | The schema check reads the stored version and walks the migrations upward, bumping the row each step, to the current version; it runs on every load, so any older database opens and upgrades in place. Downgrade scripts exist for round-trip tests only. |
| 13.10 | File / folder chooser for any data path (backup destination, restore source, import source, export destination, `.eml` path) | ABSENT | 🔴 **A cross-cutting fact the census has never stated.** Searched `src/Apex.Desktop` for the storage provider, both file dialogs, the folder dialog and the picker options type — **zero hits for all five**. Every path is a typed string or a silent default to Documents. **A user restoring from a backup must type the full archive path from memory.** Filed as **T1-20.** |

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

#### Area 16 — Formerly excluded by decision (security, audit, data structure) · 7 rows · 0 complete / 1 partial / 6 absent

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
| 16.3 | Tally Audit / Edit Log — a persisted record of who changed what, and when, on masters and vouchers | ABSENT | `AuditTrail`, `EditLog`, `ModifiedBy`, `CreatedBy`, `ActorId` → **all zero**; the single `ChangedBy` hit is the substring inside a test name. **No audit, log or history table among the 182.** The ~40 `audit` hits in `src/` are review-round comments, or *"audit"* meaning **evidentiary statutory basis** on a computed figure, or **two doc comments saying it is out of scope by ruling**. |
| 16.4 | Attribution on the three lifecycle verbs — who altered / deleted / cancelled a posted voucher, when, and from what | ABSENT | 🔴 **THE VERBS ARE COMPLETE AND THE ATTRIBUTION IS ABSENT — the row is the attribution, not the verbs.** `Cancel(Guid)`, `Delete(Guid)` and `Replace(Guid, Voucher)` all exist, all have routes and all persist; `vouchers.cancelled` and `inventory_vouchers.cancelled` round-trip. **None of the three takes an actor or a timestamp parameter**, and the `vouchers` table's 14 columns include no user, actor or change-timestamp. Of the whole schema, only `itc_reversals.created_at` and `gst_drc03.created_at` carry a timestamp at all — GST statutory rows, no user, unrelated to voucher change. Do **not** count `EInvoiceRecord.CancelledOn` / `EWayBillRecord.CancelledOn`: those record the **IRP/NIC portal's** cancellation of an e-document, not the local Alt+X. **Cross-reference: 16.3.** |
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
    - The **tax-head shape pin** and the **cess magnitude pin** on both accept paths — the corpus says nothing
      about what happens when a master moves between posting and amendment. ⚠️ **And the pin is known-blind on
      two measured axes** (**T0-14**, **T0-15**): both are OURS, both are open, and neither is a fidelity gap.
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
**12 of 216 capabilities have had their SHIPPED behaviour compared to a source — the ninth is PARTIAL, with its unsourced half enumerated rather than glossed; the tenth and eleventh became shipped-and-compared when S3 and S4 landed; and the twelfth became shipped-and-compared on 2026-08-20, when S5a–S5e's step-5a record was written into item 12 above. ~~NO ITEM HEADER WAS ADDED, so the GROUNDED count stays at 12 — what changed is that the last grounded-but-unbuilt header is now built and compared. That leaves 204 uncompared as shipped behaviour, and 204 with no sourced verification of any kind.~~** 🔴 **AMENDED LATER THE SAME DAY (2026-08-20), BY THE T0-11 SLICE-S0 PASS, AND THE STRUCK SENTENCE IS WHY THE AMENDMENT IS NOT A CONTRADICTION.** A header WAS added afterwards — **item 14**, graded `[GRADE: GROUNDED-AHEAD]` (its header reads *"GROUNDED; PARTLY BUILT"*) — so **the grounded count is 13, and figures (3) and (4) SEPARATE AGAIN at 204 and 203.** The struck sentence predicted exactly this: *"if a later slice grounds a capability ahead of building it, they separate again."* **Figure (1) did not move**; nothing new was compared. Every "COMPLETE" in §1.2 means *present and reachable*, not *correct*. A previous sweep on this project reported CANNOT TELL 256 and the 256 was the honest part; the equivalent honest number here is **204**.

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
> **As of 2026-08-20 (second pass of that day — T0-11 slice S0), against §1.2's 216 denominator:
> 12 shipped-and-compared · 13 grounded · 204 uncompared as shipped · 203 with no sourced verification of any
> kind.**
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
> **Its literal output, 2026-08-21 — fourteen tokens for fourteen numbered items:**
>
> ```
>      12 [GRADE: COMPARED]
>       1 [GRADE: GROUNDED-AHEAD]
>       1 [GRADE: METHOD-NOTE]
> ```
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
>    Re-count them: items **1–12**. → **12**. *(Item 12 joined on 2026-08-20;
>    the superseded count was* ~~*items 1–11 → 11*~~*.)*
> 2. **grounded** = that number, plus the items graded `[GRADE: GROUNDED-AHEAD]`. Today that is
>    item **14** alone. → **12 + 1 = 13**. *(Superseded:* ~~*item 12 alone → 11 + 1 = 12*~~*.)*
> 3. **uncompared as shipped** = §1.2's denominator minus (1). → **216 − 12 = 204**. *(Was `216 − 11 = 205`
>    until 2026-08-20, and `200 − 11 = 189` until 2026-08-19; the denominator moved, the derivation did not.)*
> 4. **no sourced verification of any kind** = (3) minus the grounded-ahead items, i.e. (2) − (1). →
>    **204 − 1 = 203**. *(Was `205 − 1 = 204`; and `189 − 1 = 188` until 2026-08-19.)*
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
>   `BillAllocations` **RE-KEYED on the party leg, SILENTLY DROPPED elsewhere (T1-23)** · `CostAllocations`
>   **CARRIED (fixed)** · `BankAllocation` **DROPPED (T1-22)** · `Forex` **CARRIED (fixed)** · `Gst`
>   **RE-DERIVED and shape-pinned (blind on T0-14 / T0-15)** · `Tds` **REFUSED AT THE DOOR** · `Tcs` **REFUSED
>   AT THE DOOR** · `Payroll` **REFUSED AT THE DOOR**. **T1-22 and T1-23 are the entire residue.**
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
| **T0-4** | **GST rate hierarchy inverted; the missing resolution levels now EXIST as masters but nothing READS them.** **STILL OPEN — only the master/plumbing half of WF-1 shipped (schema v51, 2026-08-15, committed and pushed as `e49b88e`).** | Register IV-1. **[V] 2026-08-15:** `MasterGstDetails` is carried by `Group`, `StockGroup` and `GstConfig.DefaultGst`, and `GstConfig` holds the two source-order options (`SourceOfHsnSacDetails`, `SourceOfGstRate`) — but those two have **no reader outside the persistence and Io layers**, and `GstService.cs` / `RcmService.cs` / `Reports/Gstr1.cs` are **unmodified**, so every rate still resolves item-first. See `plan.md` slice S4 (WF-1) for the R6 deviation this half shipped under, and — **added 2026-08-16** — for the **three-lens review that half owed, now PAID** (34 findings; the migration back-fill was being erased by the ordinary save path on non-GST books and is fixed; the missing **design** gate is not retroactively granted). | Wrong tax rate on invoices → wrong GSTR-1/3B → wrong liability. |
| **T0-5** | **4% Health & Education Cess applied to live payroll deductions on a rate the code itself says it could not verify.** | `src/Apex.Ledger/Services/SalaryIncomeTax.cs:50-54` — the comment states the rate must be verified before the FY 2026-27 tables are relied on. | Real money deducted from real salaries on an unsourced statutory figure. **Standing user decision, highest priority.** |
| **T0-6** | **Shipped TDS rates and thresholds cited to commercial blogs** (cleartax, disytax). | `src/Apex.Ledger/Seed/SeedTdsTcsRates.cs:7-8`. | R7 violation on figures the product applies to money. |
| **T0-7** | ~~**A composition dealer's every printed document is an illegal tax invoice.**~~ 🔴 **CLOSED 2026-08-18 (W0-1).** The invoice PDF now branches on the bill-of-supply flag, takes its title from the shared predicate with a **structural, case-insensitive refusal of a TAX INVOICE title**, suppresses every tax head and renders the §10 / Rule 5(f) declaration; the print projector supplies the flag and the title. **What is NOT retroactive:** nothing here re-prints a document already issued. | **[V] 2026-08-10 (the original finding):** `GstReportSupport.cs:110-123`, `VoucherDetailViewModel.cs:36-43`, `MainWindow.axaml:1990` — and **zero** `BillOfSupply` hits in `Apex.Ledger.Io` or `VoucherPrintProjector.cs`. 🔴 **THAT LAST CLAUSE IS THE ONE THAT WENT STALE, AND IT WAS THE WHOLE EVIDENCE FOR THE ROW.** Two of the five 2026-08-18 surveys measured it independently and both counted the opposite: **30 hits in `Apex.Ledger.Io`** and **34 in `VoucherPrintProjector.cs`**. The row survived only because **T0-8 was updated on 2026-08-17 and T0-7 beside it was not** — the same fix pass touched both halves of the printed document. See §1.2a row 6.22. | ~~Non-compliant document issued to customers.~~ **Closed.** The residual is historical documents already issued, which no code change reaches. |
| **T0-8** | **Every printed invoice carried a blank seller address block.** **CLOSED 2026-08-17 - both halves have shipped and the creation path's crash is fixed.** The PRINT half (W0-2a, 2026-08-15) made `SellerBlock` read `MailingName`, `Address`, `Country` and `Pin`, so a captured address prints in full and matches the recipient block. The **WRITE half (W0-2b)** is the company profile screen: the Rule 46(a) address is typeable on creation and on alteration. **What is NOT retroactive, and must not be read as closed:** books already on disk carry no address until someone opens Company Alteration and types one - the fix makes the field reachable, it does not populate history. | **[V] 2026-08-17:** `VoucherPrintProjector.cs:758-764` (`SellerBlock`), `:747-751` (`SupplierPostalAddressText` - the guard that keeps an uncaptured company byte-identical), `Company.cs:67-97`; the capture side is `CompanyProfileViewModel.cs` and `MainWindowViewModel.cs`. Pinned end-to-end by `A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block`. **The structural pin is `CompanyCaptureReachTests`, and its own claim was corrected 2026-08-17:** the reach test that merely counts assignment sites had THREE independent satisfiers (creation, alteration, and the alter screen's private rollback helper), so deleting either real capture left it green. It is now two tests - a floor that says the block is typeable at all, and `Both_company_capture_methods_still_assign_every_postal_member`, which names the two capture methods and fails if either stops assigning any of the four members. **The floor that made the write half safe - `CompanyStorage.cs:128`** is `company.EnsureValid()`, the desktop layer's single validation choke point; it now also holds the books-begin invariant, so a company Save accepts is a company Load can reopen. Its one carve-out - a file-level backup RESTORE, which cannot pass through it - is checked in `RestoreCompanyViewModel.Apply` and stated in the `Save` doc. **And the inheritance is a DISPLAY default, not a stamp - `GstConfigViewModel.cs:583`** seeds the GST home State from the postal one only when nothing is stored and no GSTIN was typed, because a code written onto a GST-off company is discarded by the very next load. *(Previously cited `VoucherPrintProjector.cs:745-750` at census baseline `468a96e`.)* | CGST Rule 46 requires the supplier address on a tax invoice. **Fixable from inside the UI at last** - and still absent on every historical book until it is typed. |
| **T0-9** | **IRN and signed QR are never printed on an e-invoiced supply** — and structurally cannot be. `PdfWriter` exposes only `Text` and `Line`; there is no image primitive. | `PdfWriter.cs:30-70`; zero `Irn`/`QrCode` hits in `InvoicePdf.cs`/`InvoicePrintData.cs`/`VoucherPrintProjector.cs`. | A printed e-invoiced supply is non-compliant. Blocked behind a print-engine rewrite. |
| **T0-10** | **Credit and Debit Notes move no stock.** `ItemInvoiceStock.Counts()` returns true only for Purchase and Sales. 🔴 **WIDENED 2026-08-20 — THIS ROW ALSO OWNS THE CN/DN *PRINT-SHAPE* WALL, WHICH T0-11 USED TO BE BLAMED FOR.** A note cannot carry inventory lines **at any point in its life**: `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` throws on every post and `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` makes the item-invoice chord inert, so there is nothing for a printer to draw. **The re-attribution does NOT enlarge the fix**, and it must not be read as doing so: the legally complete note (**RQ-11b**, CGST **Rule 53**) is **value-level** and ships without this row moving at all. What the re-attribution buys is honesty about the cause. | `src/Apex.Ledger/Services/ItemInvoiceStock.cs:53`. plan.md 10.9 NEXT-1, decision D3 approved behind an oracle. **The print-shape half:** `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` (the throw), `src/Apex.Ledger/Services/VoucherValidator.cs:150-151` (the call), `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` (the inert chord) — all three re-measured first-hand 2026-08-20 before the re-attribution was written down. | Every goods return leaves inventory permanently overstated. **And** an item table can never appear on a printed note while this row is open — which is a commercial-presentation gap, **not** a compliance one, because Rule 53 does not require one. |
| **T0-11** | **A Purchase item-invoice prints as a Dr/Cr voucher with ZERO item detail.** 🔴 **RE-SCOPED AND RE-CAUSED 2026-08-20 (T0-11 grounded design pass, slice S0). THE ORIGINAL ROW READ** ~~*"Purchase item-invoices, Credit Notes and Debit Notes never print in invoice format — they silently fall back to a Dr/Cr voucher print."*~~ **It named the right symptom, the wrong cause, and bundled two different defects under one id.** The **Credit / Debit Note half is REFUTED and moved to T0-10** (rows 4.7 / 4.8). What remains here is the **PURCHASE** half — and it is **worse** than the row said: the plain voucher projection never reads `voucher.InventoryLines` at all, the voucher print DTO has nowhere to put them, and the voucher PDF can only draw a Particulars / Debit / Credit table. **This is a MISSING PROJECTION at three layers, not a predicate flip.** | 🔴 **CORRECTED 2026-08-20. THE ORIGINAL EVIDENCE CELL READ** ~~*"`VoucherPrintProjector.IsTaxInvoice` requires `BaseType == Sales` (`:48`). Contradicts `docs/phase5-reports-io-requirements.md:217` RQ-11."*~~ **All three of its claims are wrong, and each was re-measured first-hand at HEAD before being replaced.** **(1) THE LOCATOR `:48` IS STALE.** `src/Apex.Desktop/Services/VoucherPrintProjector.cs:48` is **prose inside an XML doc comment** (a §206C TCS carry-forward note). The wrapper is a **pure forward** at `src/Apex.Desktop/Services/VoucherPrintProjector.cs:116-117`, and **the rule lives at `src/Apex.Ledger/Reports/GstReportSupport.cs:1346`** — `if (type?.BaseType != VoucherBaseType.Sales) return false;` — where it moved when the §31(3)(c) exempt limb began serving the e-Way engine as well as the printer. **(2) "CONTRADICTS RQ-11" IS BACKWARDS: RQ-11 WAS ITSELF WRONG AND THIS ROW INHERITED THE ERROR.** RQ-11 as shipped commanded a **tax-invoice** format for a *"sales / **purchase** item-invoice"* — a document CGST **§31(1)** puts on *"a registered person **supplying**"*, i.e. one we have no right to issue on the purchase half. **RQ-11 is amended in place to SALES-ONLY, and RQ-11a (recipient-side record) and RQ-11b (Rule 53 note) are added** — `docs/phase5-reports-io-requirements.md:217`. **(3) THE DEFECT IS THE CALL SITE, NOT THE PREDICATE.** Sales-only is the **correct** answer to the question `IsTaxInvoice` is named for; it is **used** at `src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107` to answer a different one — *"should this render with item detail?"* ⚠️ **AND WIDENING THE PREDICATE WOULD BE DANGEROUS, NOT MERELY WRONG — THIS IS THE HAZARD THE ROW NEVER SAW.** `src/Apex.Ledger/Reports/GstReportSupport.cs:1098` gates `IsBillOfSupply`'s limb 2 on `IsTaxInvoice`, and `IsBillOfSupplyForFiling` (`src/Apex.Ledger/Reports/GstReportSupport.cs:1148`) feeds the NIC e-Way `docType` at `src/Apex.Ledger/Services/EWayBillService.cs:482`. So the naive fix would **also** title a wholly-exempt purchase **"BILL OF SUPPLY"** — which CGST **Rule 49** likewise puts on the supplier — **and silently move a code we file with a government portal.** Three consumers move together; the method's NAME is the conflation. **The resolution is the three-axis split in `docs/adr/0002-printed-document-three-axis-split.md`; the slice chain is `plan.md` Phase 10.13.** | A supplier's document is unusable as a document — a purchase item-invoice prints no items, so it cannot be used to verify the input tax credit being claimed. ✅ **THE ITEM-INVOICE SHAPE IS CLOSED 2026-08-20 (Phase 10.13 slice S2)**: it prints a recipient-side `PURCHASE RECORD` carrying its item detail, headed by the SUPPLIER (CGST Rule 46(a)), stating the tax he charged under a caption naming him, with place of supply, our declaration and our signature suppressed, and our voucher number under its own caption *"Our Record Ref."* rather than *"Invoice No."*. **`IsTaxInvoice` and `IsBillOfSupply` were NOT edited** — the classifier consults them — so the NIC e-Way `docType` is unmoved, and the byte golden shows this slice moved **one** printed document and no other. 🔴 Every string on it is **OURS (ruling 9)**. **STILL OPEN under this row:** the purchase **accounting (service)** invoice, which takes the other projection pass (slice S3). |
| **T0-12** | 🔴 **NEW 2026-08-18. Recording the same attendance period twice silently DOUBLES the pay.** The attendance service's record method **always appends a new entry** with no dedupe on employee + type + period; its delete method has **zero callers in `src/Apex.Desktop`**; and the payroll computation **sums every matching entry**. | Survey-measured at HEAD `6fb5fe5`: `PayrollAttendanceService` (record appends, delete uncalled), `AttendanceVoucherEntryViewModel` (writes every non-blank row, zero `duplicat` hits), `PayrollComputationService` (the attendance sum). §1.2a row 7.8. | An On-Attendance or On-Production pay head pays twice, and **the operator has no in-app way to undo it** — the recorded entry can be neither altered nor removed. Real money, real salaries. |
| **T0-13** | 🔴 **NEW 2026-08-18. A leaver accrues gratuity provision and statutory bonus for ever.** `Employee.DateOfLeaving` has **zero hits across all of `src/Apex.Desktop`** — no field, no XAML — while **three engines read it**: the gratuity provision skips an employee who has left, the bonus register clips the eligibility year on it, and the ESI contribution emits it as the last working day. | Survey-measured at HEAD: `Employee`, `GratuityProvision`, `BonusRegister`, `EsiContribution`; settable only through JSON/XML import. §1.2a row 7.2. | Provisions and bonus keep accruing for staff who left, and the ESI file never carries a last working day. Both are wrong figures in a filed or auditable artefact. |
| **T0-14** | 🔴 **NEW 2026-08-20. The alteration screen's tax-head shape pin is BLIND to an intra-state GST rate master moved between an EVEN basis-point figure and the ODD one above it, so the ITC and the supplier's credit silently restate on an amendment that touched nothing.** The CGST and SGST legs are stamped with `integratedBp / 2`, an **integer** division, so **500 and 501 both stamp 250** and `TaxHeadSignature` — which compares `ledger｜side｜head｜rate` — sees no change. An INTER-state invoice is safe: the IGST leg carries the full basis points. | **[V] 2026-08-20**, reproduced through the REAL purchase item-invoice screen by the agent fixing the cess blocker: moved the item's rate 5.00% → 5.01% with the alteration screen open and `AcceptAlteration` returned TRUE with *"Purchase No. 1 altered."*; signature identical on both sides; **ITC moved 92.60 + 92.59 = 185.19 → 92.78 + 92.78 = 185.56 and the supplier's credit 3,888.90 → 3,889.27.** The halving is in `GstService.ComputeInvoiceTax`'s rate-group loop; the pin is `VoucherAlterationDerivedLegs.TaxHeadSignature`, whose doc comment now carries these literals under its *"WHAT THIS SIGNATURE IS BLIND TO"* enumeration. | Rs 0.37 on the measured fixture, **unbounded in principle** — the drift scales with the invoice. It is written into the book and into the filed return under the guard's own claim that *"a rate master moved since posting"* is exactly what it refuses. **OPEN.** §1.3 item 12 category (D). |
| **T0-15** | 🔴 **NEW 2026-08-20. The same pin is BLIND to a TAXABILITY FLIP that another line of the same rate group masks.** The signature deliberately excludes the stamped `GstLineTax.TaxableValue` — right for an ordinary amendment, and it also hides a moved master. Only a flip that empties the WHOLE rate group is caught, because only then does a leg disappear. | **[V] 2026-08-20**, reproduced through the real screens: two items both at 18% (one rate group), one posted invoice; flipping ONE item Taxable → Exempt with the screen open was ACCEPTED (*"Purchase No. 1 altered."*) with the signature identical, while **the stamped taxable base fell 7,654.15 → 3,950.44, the ITC fell 688.88 + 688.87 → 355.54 + 355.54 and the supplier's credit fell 9,031.90 → 8,365.23.** | **Rs 666.67 measured on one two-line invoice**, on an alteration that touched nothing. Same class as T0-14 and as the cess blocker the same review found. **OPEN.** §1.3 item 12 category (D). |
| **T0-16** | 🔴 **NEW 2026-08-20. A cess-bearing item sold over the counter collects ZERO Compensation Cess, while the identical item on a Sales item invoice collects it.** `PosBillingViewModel.ComputeGst` builds its taxable line with **no cess argument at all**, where the accounting item-invoice screen resolves the cess master and passes one. This is a **feature gap, not a regression** — it has been true since the POS screen was built — and it is why the cess drift-pin's POS arm is a refusal on an un-re-derivable bill rather than a master-drift comparison. | **[V] 2026-08-20**, found while wiring the POS half of the cess pin: `PosBillingViewModel.ComputeGst` vs `VoucherEntryViewModel.ComputeItemInvoiceGst` (which calls the cess resolver). The doc comment on `PosBillingViewModel.ReDerivedCessOnPostedRows` names this and states what becomes of the guard when `ComputeGst` gains cess. | Under-collected Compensation Cess on every counter sale of a cess-bearing good, and a GSTR-1 cess column that disagrees with the accounting screen for the same item. **OPEN — needs an R6 plan row, and its RATE side must be web-verified against CBIC at build time (A6/R7 mandate); no per-unit or ad-valorem cess figure may be asserted from memory.** |

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
| **T1-22** | 🔴 **NEW 2026-08-20. A `BankAllocation` on the PARTY leg of an item invoice is DESTROYED on re-accept — the instrument detail AND the reconciliation date — and the warning rides on the SUCCESS message.** `BuildItemInvoice` constructs the party line bare, so the cheque/DD number, its type, its instrument date and its bank date all vanish. `Replace`'s `CarryBankDatesForward` does not carry it; it raises a warning which is then **appended to the "… altered." message**, so the operator is told the amendment succeeded and the loss is on the same line. **The party picker really does offer a bank ledger** — the party list is *"(none)" + every ledger*. | **[V] 2026-08-20**, scratch xUnit fact posted through the REAL item-invoice screen, then a `Replace`-stamped `BankAllocation`, then the REAL `ForAlter`/`AcceptAlteration`. Verbatim before: `bank=True instr='CHQ-90210' type=ChequeOrDD instrDate=03-04-2026 bankDate=05-04-2026`; after: `bank=False instr='' type= instrDate= bankDate=`. 🔴 **AND THIS CORRECTS THE S5d/S5e VERIFIER, WHICH IS THE PART THIS PROJECT LOSES:** the verifier told the fixer to DROP this limb and asserted *"only the instrument detail, not the reconciliation date, would be at risk there"*. **The reconciliation is lost too.** The fixer probed instead of assuming, and the verifier was wrong. | A reconciled bank line silently becomes unreconciled, and the instrument reference the reconciliation was made against is gone. **NOT FIXED** — it was outside the fixer's item by explicit instruction. The mechanism to close it exists: `TryCarryDerivedLegChildren` / `CarriedLegChildren` in `VoucherEntryViewModel`. ⚠️ **Closing it needs a ruling on whether `Replace`'s `CarryBankDatesForward` warning stays.** |
| **T1-23** | 🔴 **NEW 2026-08-20. `BillAllocations` on a bill-wise VALUE leg are destroyed on re-accept with NO warning at all — not even the one T1-22 gets.** Bill-wise is properly re-keyed on the PARTY leg; on the value leg it is dropped. A Purchase Accounts ledger with `MaintainBillByBill` set is legal — the validator gates only on that flag and on the split footing the line, neither of which is party-specific. **Nobody had enumerated this: the finding, its verifier and the completeness critic all discuss bill-wise only on the party leg.** | **[V] 2026-08-20**, same scratch fact: value ledger with `billWise: true`, item invoice 2 @ 1234.57, value leg 2469.14 carrying one `BillAllocation(NewRef, 'VALUE-LEG-REF', 2469.14)`. Verbatim before: `bills=1 'VALUE-LEG-REF'`; `AcceptAlteration -> True : Purchase No. 1 altered.`; after: `bills=0`. | That ledger's bill-wise outstanding becomes unallocated, silently, under a plain success message. **NOT FIXED** — it carries a design question (carry, or refuse at the door?) that is a user/design call, not a fixer's. |
| **T1-24** | 🔴 **NEW 2026-08-20. The type F-keys destroy an in-progress POS bill AND an unsaved POS ALTERATION, with no prompt and no notice.** Same root as the accounting-screen work-loss defect fixed in the same review: the F4–F9 button-bar rows are enabled on *has a company* alone, and `OpenVoucher` → `OpenPageColumn` → `ClearSubScreens` nulls `PosBilling` and `Reports` unconditionally. **The fix that shipped is scoped to `Screen.VoucherEntry` per its brief and does NOT cover this.** | **[V] 2026-08-20**, throwaway `[AvaloniaFact]` driving the REAL MainWindow tunnel handler. Verbatim: one plain **F8** replaced a keyed bill of 3 × Rs 849.37 (cash tendered Rs 3,000) with a blank Sales entry — `notice='' message='' promptOpen=False`. The ALTERING half is worse because it also tears the report down: `isAltering=True rate='999.11' columns=3 reportsNull=False billTotal=1298.74` → `posNull=True notice='' promptOpen=False columns=2 reportsNull=True` — the amendment and the Day Book column both gone to one keystroke. | Unsaved keying and the operator's place in the report, lost to a key they pressed for another purpose. **FIX SHAPE, already named in the shipped guard's own doc comment:** a `HasUnsavedWork` on `PosBillingViewModel` plus a second arm in `MainWindowViewModel.OpenVoucherFromTypeKey`. **OPEN.** |

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

**Bottom line for the user** *(every figure below is as of **2026-08-19** and copied from its derivation — the capability split from §1.2, which is itself summed from §1.2a; the fidelity numbers from §1.3; the TIER 0 count from §2 TIER 0. §1.3's anchor block pins the last two. If a figure here disagrees with its derivation, the derivation is right and this paragraph is stale)*. A perfect clone needs **216 named capabilities — and this is the first version of this document in which you can read which ones**. We have **47** whole, **96** partial, **73** missing. *(The **115 · 42 / 44 / 21** you may have seen quoted elsewhere is the superseded 2026-08-10 snapshot, and **200 · 47 / 95 / 58** is the 2026-08-18 one; §1.2b explains the first move — mostly granularity, and an absent column that was provably too small — and §1.2's banner explains the second, which is a **scope decision**: user ruling 10 brought §3's nine and §4's seven into the denominator, `200 + 9 + 7 = 216`.)* Only **11** capabilities have ever been checked against a source for correctness as shipped, so the fidelity denominator is **205** wide open; one of those — voucher alteration — has its grounding banked ahead of the slice that builds it, which leaves **204** with no sourced verification of any kind. **▶ And as of 2026-08-19 that gap is the goal itself: user ruling 9 makes "done" mean FULL PARITY *and* CORPUS VERIFICATION — with the honest limit that where the corpus is silent a capability ships as a documented divergence labelled as ours, and can never join the 11.** The most urgent items are not the missing ones: they are the **eleven open TIER 0 defects** — of which **nine are confirmed wrong-money-or-invalid-document defects a business would suffer today**, and **two (T0-5's 4% cess, T0-6's blog-cited TDS rates) are statutory figures the product applies to money on sourcing nobody can stand behind — confirmed unsourced, not confirmed wrong**. Two of the nine are new today and both are payroll: **recording an attendance period twice silently doubles the pay**, and **a leaver accrues gratuity and bonus for ever**. All of it sits on top of a book that still cannot be fully corrected: **no voucher can be ALTERED at all**, and **eight of the classic eighteen voucher kinds can be neither cancelled nor deleted nor even listed in the Day Book** (T1-17) — cancellation and deletion shipped for the other ten.