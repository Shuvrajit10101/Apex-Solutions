# Phase 7 — TDS / TCS Implementation Plan (Apex Solutions, Tally Prime clone)

> ## Decision resolution header — approved by the user 2026-07-10
>
> The 7 open decisions from §8 below were **RESOLVED to their recommended defaults** and **approved by the
> user on 2026-07-10**. This document is now the authoritative Phase-7 requirements reference; the rest of
> the plan content below is retained **verbatim** for traceability.
>
> - **D1 — §206C(1H) treatment:** implement as **legacy, year-gated** — default **OFF / non-selectable for
>   transaction dates ≥ 01-Apr-2025** (27EQ/27D suppress it), **retained + selectable for FY 2024-25** so
>   historical/prior-year returns still compute; current goods sales route to buyer-side §194Q TDS.
> - **D2 — Target FY:** **FY 2025-26 (AY 2026-27)** is the primary target (rate tables in §2).
> - **D3 — Salary TDS (§192 / Form 24Q / Form 16):** **DEFERRED to Phase 8 (Payroll)** — not in Phase 7.
> - **D4 — FVU-format depth:** emit the **FVU-compatible NSDL offline flat-file + emulate control-total
>   checks** (pinned to one FVU version A14 verifies); **no online TRACES/portal upload** and no bundling of
>   the government FVU/RPU JARs.
> - **D5 — §206AB / §206CCA (non-filer higher rate):** **NOT implemented** (omitted by Finance Act 2025);
>   catalog verification-report #131 marked STALE. Retain only §206AA (TDS no-PAN 20%) and §206CC (TCS
>   no-PAN 2×/5%).
> - **D6 — §206C(1G) LRS / overseas-tour matrix:** **DEFERRED to Phase 9+** (volatile multi-tier; low value
>   for an SMB clone).
> - **D7 — Predefined master seed set:** Nature-of-Payment = **194A / 194C / 194H / 194I / 194J / 194Q**;
>   Nature-of-Goods (§206C) = **scrap / timber / liquor / tendu / minerals / 1F + 1H-legacy** (year-gated
>   per D1).
>
> **⚠ Every rate / threshold / section-code remains an A14 build-time re-verify item (R7)** — see the §2.5
> re-verify checklist. Resolving these decisions does **not** settle any statutory figure; each is re-verified
> against official sources at Phase-7 kickoff and per slice.

> **Status:** PLAN-ONLY. Nothing here is executed. This is the authoritative, decision-ready Phase 7
> plan for user approval per R9/R12. Schema head is **v24** (Phase 6 complete). Branch
> `claude/wonderful-hellman-59520a`. Governed by CLAUDE.md R1–R14; built via `/software`, sequenced by
> the main loop, executed by named agents (A14 leads law/fidelity; A10 adversarial review; A12 GitHub
> Expert owns all git per R4).
>
> **A14 MUST re-verify every rate/threshold/section-code and the 206C(1H) + FY decisions at Phase-7
> kickoff (R7).** All law below is grounded to the sources listed in §2; treat every "⚠ RE-VERIFY" flag
> as a build-time gate, not a settled fact.

---

## 1. Scope + Goals (from plan.md Phase 7)

**Goal:** Income-tax withholding statutory compliance — TDS (Tax Deducted at Source, deductor/payer side)
and TCS (Tax Collected at Source, seller side) — modelled faithfully on Tally Prime and grounded in
`docs/tally-feature-catalog.md` §13 + Study-Guide PDF pp.136–147.

**In scope (catalog §13 + verification-report C-5):**
- **TDS masters & config:** F11 Enable TDS, company Deductor details (TAN, deductor type, person
  responsible, surcharge/cess), **Nature of Payment** master (194J/194C/194H/194I/194A/**194Q** …),
  ledger/party applicability flags + PAN + deductee type.
- **TDS engine:** deduct→pay→deposit flow; withholding carve-out on Journal/Payment/Purchase/expense
  vouchers; No-PAN 20% (§206AA); single + cumulative-FY thresholds; **Challan Reconciliation (Alt+R)**.
- **TDS returns/forms:** **Form 26Q** (Alt+B save) + **FVU** flat-file export; **Form 16A** certificate;
  **Form 27A** control chart.
- **TCS masters & engine:** **Nature of Goods** (§206C) master; auto-compute TCS on the Sales voucher
  (collect-on-top); No-PAN §206CC higher rate; thresholds.
- **TCS returns/forms:** **Form 27EQ** + FVU; **Form 27D** certificate.
- **Exception/outstanding reports (C-5):** TDS Outstanding, Not Deducted, Late Deduction/Payment; TCS
  equivalents; Nature-of-Payment-wise summary.
- **Io losslessness:** all new masters folded into `Apex.Ledger.Io` CanonicalModel/Mapper (JSON+XML
  round-trip) **in the introducing slice** — not deferred (the Phase-6 carry-forward defect).

**Deliverables (plan.md):** a TDS deduction + Form 26Q (FVU-valid) worked example, and a TCS-on-a-sale
worked example. Exit gate = R9.

**Explicitly OUT of scope (deferred / decide at gate):**
- **Salary TDS (§192 / Form 24Q / Form 16)** — payroll-coupled; belongs with **Phase 8 (Payroll)**.
  Recommend deferring §192 to Phase 8 (see Open Decision D3).
- **GST TDS/TCS (CGST §51 GSTR-7 / §52 GSTR-8)** — a *separate* regime; do **not** conflate with
  income-tax TDS/TCS (verification-report #152). Stays in GST scope (Phase 9).
- **§206AB / §206CCA non-filer higher rates** — **omitted by Finance Act 2025** (see §2). Do NOT
  implement; mark catalog verification-report #131 STALE. (Optional date-gated legacy only if D-hist chosen.)
- **§206C(1G) LRS / overseas-tour multi-tier matrix** — low priority for an SMB clone; defer to Phase 9+.
- **Online TRACES / portal upload** — offline file generation only (project Q4: offline JSON/flat-file).

---

## 2. Current Verified Law Summary (FY 2025-26 basis) + re-verify flags

> **Target FY assumption:** this plan is written for **FY 2025-26 (AY 2026-27)** as the default target
> (Open Decision D2). All rates below are FY 2025-26. If the user picks FY 2026-27, the **new Income-tax
> Act, 2025 renumbering (ss. 392/393/394)** applies from 01-Apr-2026 — section codes change (rates carry
> over) → the Nature-of-Payment/Goods masters MUST be year-aware. **⚠ RE-VERIFY the full code mapping
> before any FY 2026-27 build.**

### 2.1 TDS sections (FY 2025-26)

| Section | Nature | Rate (with PAN) | Threshold | No-PAN | Notes / source |
|---|---|---|---|---|---|
| **192** | Salary | Avg. slab rate (115BAC new-regime default) | none | §206AA: higher of avg rate or 20% | Payroll-coupled → Phase 8. [indiapost, cleartax] |
| **194A** | Interest (non-securities) | 10% | Bank/co-op/PO **₹50,000** (sr. citizen **₹1,00,000**); other **₹10,000** — *raised by FA 2025* | 20% | ⚠ RE-VERIFY sr-citizen split against bare FA 2025. [indiapost, cleartax] |
| **194C** | Contractor/sub-contractor | **1%** Ind/HUF, **2%** others | **₹30,000** single OR **₹1,00,000** aggregate/FY | 20% | [indiapost, busy.in] |
| **194H** | Commission/brokerage | **2%** (from 01-Oct-2024) | **₹20,000** (raised from 15k, FA 2025) | 20% | [indiapost, cleartax] |
| **194I** | Rent | **2%** plant/mach/equip; **10%** land/bldg/furniture | **₹6,00,000/yr (₹50k/mo)** — *raised FA 2025* | 20% | [indiapost, cleartax] |
| **194J** | Professional/technical/royalty | **10%** prof/royalty/non-compete; **2%** technical/call-centre/certain royalty | **₹50,000** (raised from 30k, FA 2025) | 20% | [indiapost, busy.in] |
| **194Q** | Purchase of goods | **0.1%** on value **> ₹50 lakh/FY** to resident seller; buyer turnover **> ₹10 cr** prior FY | ₹50 lakh/FY | **5%** (§206AA special, NOT 20%) | Deduct at earlier of credit/payment. **194Q precedes 206C(1H).** ⚠ RE-VERIFY turnover + 5% no-PAN. [indiapost, verif #130] |

**Timing rule (all non-192):** deduct at **earlier of credit-to-party or payment**. **§206AA no-PAN =
20%** general (5% for 194Q). **Lower/nil deduction:** Form 13 / §197 certificate override → carry a reason
code + certificate no. into 26Q.

### 2.2 TCS sections — §206C (FY 2025-26)

| Code | Nature of Goods | Rate | Threshold | Source |
|---|---|---|---|---|
| 206C(1) | Alcoholic liquor (human consumption) | **1%** | — | [disytax, cleartax] |
| 206C(1) | Tendu leaves | **5%** | — | " |
| 206C(1) | Timber (forest lease) | **2%** (reduced from 2.5%, 01-Apr-2025) | — | " |
| 206C(1) | Timber/forest produce (other mode) | **2%** | — | " |
| 206C(1) | Other forest produce (non-tendu/non-timber) | **2%** | — | " |
| 206C(1) | **Scrap** | **1%** | — | ⚠ RE-VERIFY: hike to **2% reported for FY 2026-27** — pin to target FY. [disytax] |
| 206C(1) | Minerals (coal/lignite/iron ore) | **1%** | — | " |
| 206C(1F) | Motor vehicle / listed luxury goods | **1%** | value **> ₹10 lakh** (luxury extension w.e.f 01-Jan-2025) | [disytax] |
| 206C(1G) | LRS foreign remittance | **20%** over ₹10L; education/medical **5%**; education-by-loan **NIL**; tour pkg **5%** ≤₹10L / **20%** above | ₹10 lakh (raised from 7L, 01-Apr-2025) | Volatile multi-tier — defer/low-pri. [disytax, cleartax] |
| **206C(1H)** | **Sale of goods** | 0.1% over ₹50L receipts | **LEGACY — see 2.4** | **NOT operative FY 2025-26+** [taxguru] |

**No-PAN TCS (§206CC):** higher of **2× rate or 5%**. **Form 27C** buyer declaration exempts TCS where
goods are for manufacturing/processing/power (not trading). **Timing:** collected by the **seller** at
**earlier of debit-to-buyer or receipt** → computed on the **Sales voucher**, not a Journal.

### 2.3 §206AB / §206CCA (non-filer higher rate) — **OMITTED**

Both sections **removed by Finance Act 2025, effective 01-Apr-2025** (deductors couldn't verify filing
status). **DO NOT implement higher-rate-for-non-filer logic for FY 2025-26+.** Retain ONLY **§206AA**
(no-PAN → 20% TDS) and **§206CC** (no-PAN → 2×/5% TCS). Sources: [taxguru budget-2025-removal…],
[stoxntax], [saginfotech], [lexology]. **⚠ This makes catalog verification-report #131 STALE — mark it
legacy in plan.md so no one re-adds non-filer logic.** (Optional date-gated FY 2021-22…2024-25 fidelity
only if the user elects D-hist.)

### 2.4 §206C(1H) decision — **RESOLVED: LEGACY, year-gated**

206C(1H) TCS-on-sale-of-goods is **NO LONGER OPERATIVE from 01-Apr-2025** (Finance Act 2025 inserted a
proviso making it inapplicable; withdrawn to end the double-incidence overlap with buyer-side **§194Q**).
Nuance: rendered *inapplicable via proviso*, not struck from statute text; the new Income-tax Act, 2025
does not carry it forward for FY 2026-27. Sources: [taxguru tcs-sale-goods-removed…], [taxguru
budget-2025-tcs-sale-goods-omitted…], [indiafilings], SAP KBA 3576728/3574318.

**Implementation decision (recommended, pending user confirm — Open Decision D1):** implement 206C(1H) as
a **legacy, year-gated Nature-of-Goods entry** — **DEFAULT OFF and non-selectable for transaction dates ≥
01-Apr-2025** (so 27EQ/27D suppress it), but **retained + selectable for FY 2024-25 dates** so historical
data and prior-year returns still compute. For current/future goods sales, route tax logic to buyer-side
**§194Q TDS** instead. Matches verification-report #97 and #176.

### 2.5 Build-time re-verify checklist (A14, at kickoff)

1. Target FY confirmed (§2 header / D2) → pick rate table + section-code scheme.
2. If FY 2026-27: map old 194x/206C codes → new ss.392/393/394 table codes.
3. Scrap TCS 1% vs 2% for the target FY (CBDT notification).
4. 194A senior-citizen ₹1L / other ₹10k; 194I ₹6L; 194H ₹20k; 194J ₹50k against bare FA 2025.
5. 194Q buyer-turnover >₹10cr, >₹50L threshold, 5% no-PAN rate.
6. Income-tax **rounding rule** (nearest rupee — confirm exact rule/direction) before coding the engine.
7. FVU/RPU file-format version (Protean/NSDL) to pin the FvuWriter layout.
8. 206C(1H) legacy gate date and §206AB/§206CCA omission re-confirmed.

---

## 3. Engine Mapping + New Masters

TDS/TCS **mirror the existing GST pattern exactly**: config-driven, **framework/DB/clock/RNG-free** pure
services over the `Company` aggregate, producing **additive `EntryLine`s** that carry a self-describing
tax detail (mirror of `GstLineTax`) and post to **Duties & Taxes** ledgers that report projections read
back (never recompute). This reuses Phase-4 GST tax-ledger machinery and Phase-5 `Apex.Ledger.Io`.

### 3.1 Two services

**(A) `TdsService`** — mirror `GstService`, but **WITHHOLDING, not additive-on-top.** On a
Journal/Payment/Purchase/expense voucher where an expense ledger is *Is TDS Applicable* and the party is a
deductee, the deductor **carves the TDS OUT of the party leg**:
- `Dr Expense = GROSS`
- `Cr Party = NET` (gross − TDS)
- `Cr "TDS Payable"` (Duties & Taxes liability)

`ComputeWithholding(assessableValue, natureOfPayment, deductee)` → resolves rate (PAN present ⇒
`RateWithPan`; no PAN ⇒ 20% §206AA, or 5% for 194Q), applies section threshold (single-transaction +
cumulative-FY per party×nature — a **pure projection** over prior posted vouchers, like `Gstr1` YTD
accumulation), applies income-tax **nearest-rupee** rounding, returns TDS `EntryLine`(s) + a `TdsLineTax`
detail. **Balance invariant holds automatically** (gross Dr = net Cr + TDS Cr). Direction Deduct derived
from voucher base type like `GstReportSupport.DirectionOf`. Canonical Tally flow: deduct-via-Journal (F7)
→ pay party Agst Ref (F5) → deposit via Payment Ctrl+F Stat Payment.

**(B) `TcsService`** — mirror `GstService`, **ADDITIVE like GST.** On a Sales voucher where the
item/sales-ledger/party is *Is TCS Applicable* under a Nature of Goods (§206C), TCS is **collected on
top**:
- `Dr Party = value + GST + TCS`
- `Cr Sales`
- `Cr Output GST`
- `Cr "TCS Payable"` (Duties & Taxes)

Base = assessable value (config: with/without GST per section). Same rate resolution (PAN / §206CC no-PAN
2×/5%) + threshold + `TcsLineTax` detail.

Both reuse: `GstService.EnsureTaxLedger` auto-creation on enable (→ `Tds/TcsLedgerClassification`); the
`EntryLine` additive seam; `ClassificationRules.IsDutiesAndTaxesLedger` (must cover the new payable
ledgers). Reporting mirrors `Gstr1`/`Gstr3b`/`GstReportSupport` — **pure projections** reading posted
`TdsLineTax`/`TcsLineTax` so 26Q/27EQ reconcile to payable-ledger postings by construction. Deposit
mirrors GSTR-3B net-liability → a real Payment voucher against the payable ledger + a challan record.

### 3.2 New masters (all mirror existing GST types — reference files in §7)

| New master | Mirrors | Key fields |
|---|---|---|
| **Nature of Payment (TDS)** | `GstRateSlab` | section code, name, `RateWithPanBp`, `RateWithoutPanBp` (default 2000bp), single-txn threshold, cumulative-FY threshold, FVU section code, effective-from, `IsPredefined` (seeded). Hung off `Company` like `RateSlabs`. |
| **Nature of Goods (TCS §206C)** | `GstRateSlab` | collection code (6CA scrap / 6CI timber / 6CE liquor / 206C(1H)…), name, `RateWithPanBp`, `RateWithoutPanBp` (§206CC), threshold, base-includes-GST flag, FVU code, effective-from, legacy/year-gate. |
| **Deductor details (Company)** | `GstConfig` | `TdsConfig`/`TcsConfig`: TAN, deductor type (Company/Individual/Firm/Govt…), responsible-person name/PAN/designation/address, surcharge & cess flags, applicable-from, periodicity. `EnsureValid()` fail-fast + new `Tan.Validate`/`Pan.Validate` (mirror `Gstin.Validate`). |
| **Party TDS/TCS details (Ledger)** | `PartyGstDetails` | `PartyTdsDetails`: deductee type, PAN (validated), "deduct in same voucher" flag. `PartyTcsDetails`: collectee type, Form-27C flag. (No §206AB/§206CCA flags — omitted.) |
| **Expense/purchase ledger flags** | `StockItemGstDetails` | `LedgerTdsDetails`: *Is TDS Applicable* + default Nature-of-Payment id. |
| **Stock-item / sales-ledger TCS flags** | `StockItemGstDetails` | *Is TCS Applicable* + Nature-of-Goods id on `StockItem` and sales ledger. |
| **TDS/TCS duty ledgers** | `GstService.EnsureTaxLedger` + `RoundOffLedger` | auto-created under Duties & Taxes on enable; tagged `TdsLedgerClassification(natureId, direction)` / `TcsLedgerClassification` so reports map ledger→section without name parsing. |

---

## 4. Ordered Slice Plan (schema v24 → v30)

Sequential build (like Phase 6): every schema slice edits `Schema.cs` + `SqliteCompanyStore.cs` +
`Company.cs` on **one migration chain** → slices **cannot** run in parallel (they collide on the version
chain). Each migration is one additive `MigrateV(N-1)ToVN` constant in a txn that bumps `schema_version`;
a fresh DB stamps straight to vN via `CreateV1`; each paired with a legacy-DDL upgrade test (mirror
`JobWorkSchemaTests`/`ReorderSchemaTests`) proving the upgrade keeps existing rows untouched + **ER-13
byte-identical when TDS/TCS off**. A12 commits each slice in small conventional units; A10 adversarial
pass before each PR gate.

> **Schema progression: v24 → v25 → v26 → v27 → v28 → v29 → v30.** (S4/S7/S8 add no schema — pure
> projections; the numbering leaves the persistence slices contiguous.)

### P7-S1 — TDS/TCS masters + F11 config + duty-ledger auto-create  → **v24→v25**
- **Scope:** Nature of Payment & Nature of Goods masters (config-driven, seeded predefined set,
  A14-verified); Company deductor details (TAN/PAN validators); F11 Enable TDS + Enable TCS (idempotent,
  mirror `GstService.EnableGst`); ledger/party/stock-item TDS/TCS flags + PAN; auto-create "TDS
  Payable"/"TCS Payable" under Duties & Taxes with `Tds/TcsLedgerClassification`. **No compute yet.**
- **Schema v25:** ALTER `companies` ADD `tds_enabled,tcs_enabled,tan,deductor_type,
  responsible_person_name/pan/designation/address,surcharge_applicable,cess_applicable,tds_periodicity`
  (all NULL/DEFAULT 0 → ER-13). New tables `nature_of_payment`, `nature_of_goods` (company_id FK + code /
  name / rate_with_pan_bp / rate_without_pan_bp / threshold_micro / cumulative_threshold_micro / fvu_code
  / effective_from / is_predefined) + `ix_*_company`. ALTER `ledgers` ADD `tds_applicable, tds_nature_id,
  deductee_type, party_pan, deduct_in_same_voucher, tcs_applicable, tcs_nature_id, collectee_type`. ALTER
  `stock_items` ADD `tcs_nature_id`. *(No `higher_rate_206ab/206cca` columns — omitted per §2.3.)*
- **PR-1 gate:** EnableTds/EnableTcs idempotent; masters CRUD + validate; TAN/PAN validators;
  legacy-DDL v24→v25 test; ER-13 byte-identical when off; de-brand clean.
- **Key RQs:** F11 Enable + deductor; Nature of Payment; Nature of Goods (§206C); ledger/party/item flags + PAN + deductee/collectee type.
- **Files to mirror:** `GstConfig.cs`, `GstRateSlab.cs`, `Gstin.cs`, `PartyGstDetails.cs`,
  `StockItemGstDetails.cs`, `LedgerGstClassification.cs`, `GstEnums.cs`, `SeedGstRates.cs`,
  `GstService.cs` (EnableGst/EnsureTaxLedger), `Company.cs`, `Ledger.cs`, `Schema.cs`,
  `SqliteCompanyStore.cs`, `JobWorkSchemaTests.cs`.

### P7-S2 — TDS compute + auto-deduct engine  → **v25→v26**
- **Scope:** `TdsService` — rate resolution (PAN⇒RateWithPan, no-PAN⇒20% §206AA / 5% §194Q);
  single + cumulative-FY threshold as a **pure projection**; income-tax nearest-rupee rounding (verify);
  withholding carve-out on Payment/Journal/Purchase/expense (Dr expense gross, Cr party net, Cr TDS
  Payable); `TdsLineTax` seam on `EntryLine`; **teach `VoucherValidator` the carve-out** so item-invoice
  pairing still foots (gross = net + withheld).
- **Schema v26:** new table `tds_lines` (child of `entry_lines`): nature_id, section_code,
  assessable_value_micro, rate_bp, tds_amount_micro, deductee_ledger_id, pan_applied_flag. Additive, empty when off.
- **PR-2 gate:** worked deduction paisa-conserving (**gross = net + TDS exactly**); no-PAN 20%; threshold
  on/below/crossing; **Robert + Bright regression green**; item-invoice validation intact.
- **Key RQs:** deduct→net-pay flow; No-PAN 20%; single + annual cumulative threshold; deduct-in-same-voucher.
- **Files to mirror:** `GstService.cs`, `GstLineTax.cs`, `EntryLine.cs`, `VoucherValidator.cs`,
  `ClassificationRules.cs`, `Gstr1.cs` (YTD accumulation pattern), `GstReportSupport.cs`.

### P7-S3 — Challan / Stat Payment deposit + Challan Reconciliation  → **v26→v27**
- **Scope:** deposit TDS via Payment **Ctrl+F Stat Payment** against TDS Payable (mirror GSTR-3B
  net-liability → real payment); challan record (challan no / BSR / date / section / minor head — ITNS-281
  fields); **Challan Reconciliation (Alt+R)** matching deposits to deductions.
- **Schema v27:** new table `tds_challans` (company_id, challan_no, bsr_code, deposit_date, amount_micro,
  section, minor_head) + `challan_voucher_links` (challan_id ↔ stat-payment voucher_id); ALTER
  `voucher_types` ADD `is_stat_payment` flag (mirror `use_for_pos`/`use_for_job_work` — **reuse Payment
  base type via flag; do NOT invent a new VoucherBaseType**).
- **PR-3 gate:** stat-payment reduces payable to zero; challan recon matches; legacy-DDL v26→v27; ER-13.
- **Key RQs:** deposit via Payment Ctrl+F Stat Payment; Challan Reconciliation (Alt+R).
- **Files to mirror:** `GstService.cs`, `GstReportSupport.cs`, voucher-type flags (POS/job-work slices), `Schema.cs`, `SqliteCompanyStore.cs`.

### P7-S4 — Form 26Q + FVU export + **Io losslessness fold-in**  → **no schema**
- **Scope:** Form 26Q quarterly TDS return as a **pure projection** (mirror `Gstr1.Build` /
  `GstReportSupport.PostedGstVouchers` filtered to TDS payable direction) — deductor block + challan
  block + deductee-wise rows (PAN, section code, date, amount paid, TDS, rate, §197 reason code); Alt+B
  save-return; **new `FvuWriter`** flat-file (fixed-width/caret-delimited .txt, mirror deterministic
  `CsvWriter`/`TabularExport`, TabularDebrand-clean, no clock/RNG). **Fold ALL Phase-7 masters into
  `Apex.Ledger.Io` CanonicalModel + CanonicalMapper NOW** (new DTOs mirroring
  `GstConfigDto`/`GstRateSlabDto`/`PartyGstDto`/`StockItemGstDto`/`GstLineTaxDto`) so JSON+XML
  export→import is lossless — **this is the exact Phase-6 carry-forward defect; built in, not deferred.**
- **PR-4 gate:** 26Q reconciles to TDS Payable postings; FVU byte-stable + de-branded; Nature-of-Payment
  + deductor + tds_lines round-trip JSON+XML lossless (paisa + count exact).
- **Key RQs:** Form 26Q (Alt+B); FVU export; deductor + deductee + challan in the return.
- **Files to mirror:** `Gstr1.cs`, `GstReportSupport.cs`, `CanonicalModel.cs`, `CanonicalMapper.cs`,
  `CsvWriter.cs`, `TabularExport.cs`, `GstRoundTripTests.cs`.

### P7-S5 — TCS compute on sales  → **v28→v29** *(v27→v28 reserved; see note)*
- **Scope:** `TcsService` (additive like GST): base per §206C (incl-GST flag), rate resolution (PAN /
  §206CC), threshold; collect-on-top on Sales (Dr party value+GST+TCS, Cr Sales, Cr TCS Payable);
  `TcsLineTax` seam. **Apply the 206C(1H) legacy year-gate (§2.4 / D1).**
- **Schema v29:** new table `tcs_lines` (mirror `tds_lines`) for TCS breakup on sales entry_lines; ALTER
  as needed for base-incl-GST. *(Note: numbering leaves v27→v28 for a reserved persistence step if S5
  needs a split; keep the chain contiguous — final head is v30. The engine team may collapse S5 to a
  single v28 migration if no split is needed; confirm at build.)*
- **PR-5 gate:** TCS-on-a-sale worked example paisa-exact; coexists with GST additive on the same invoice
  (ordering: TCS base may include GST; no double-count in GSTR-1 vs 27EQ); ER-13; regression green.
- **Key RQs:** Sales auto-computes TCS; stock/sales/buyer TCS flags; §206C threshold; 206C(1H) legacy gate.
- **Files to mirror:** `GstService.cs`, `GstLineTax.cs`, `EntryLine.cs`, `VoucherValidator.cs`, Sales `VoucherEntryVM`.

### P7-S6 — Form 27EQ + TCS challan/stat payment + FVU + Io fold-in  → **v29→v30**
- **Scope:** TCS deposit via Stat Payment + `tcs_challans` + recon (mirror S3); Form 27EQ projection +
  FVU export (mirror S4); **fold TCS masters + tcs_lines + tcs_challans into CanonicalModel/Mapper NOW.**
- **Schema v30:** `tcs_challans` (mirror `tds_challans`) + `challan_voucher_links` for TCS.
- **PR-6 gate:** 27EQ reconciles to TCS Payable; FVU byte-stable; masters round-trip lossless; legacy-DDL v29→v30.
- **Key RQs:** Form 27EQ; TCS stat payment + challan recon.
- **Files to mirror:** S3 + S4 file sets.

### P7-S7 — Certificates & control chart — Form 16A, 27D, 27A  → **no schema**
- **Scope:** Form 16A (TDS cert, per deductee per quarter), Form 27D (TCS cert), Form 27A (return control
  chart / control totals) as **PDFs** via `ReportPdf`/`PdfWriter`+`PrintModel`+`PageConfig` +
  `IndianAmountInWords` (mirror GST tax-invoice `InvoicePdf`). Form 27A cross-checks control totals
  (record count + total tax + total amount paid) against the 26Q/27EQ before FVU validation. C-5.
- **PR-7 gate:** certs render deterministic + de-branded; figures match 26Q/27EQ; PDF golden test.
- **Key RQs:** Form 16A; Form 27D; Form 27A control chart.
- **Files to mirror:** `ReportPdf.cs`, `InvoicePdf.cs`, `IndianAmountInWords.cs`.

### P7-S8 — Exception / outstanding reports  → **no schema**
- **Scope:** TDS Outstanding (deducted-not-deposited), TDS Not Deducted (applicable-but-skipped /
  below-threshold-now-crossed), Late Deduction / Late Payment (interest u/s 201(1A): 1%/mo not-deducted,
  1.5%/mo deducted-not-paid), Nature-of-Payment-wise summary; TCS equivalents — **pure projections**
  (mirror NegativeStock/Outstandings exception reports). C-5.
- **PR-8 gate:** exception reports flag the seeded defect cases; deterministic ordering; de-brand clean.
- **Key RQs:** TDS Outstanding / Not Deducted / Late Deduction/Payment; TCS equivalents.
- **Files to mirror:** existing exception-report projections, `GstReportSupport.cs`.

### P7-S9 — Exit gate  → **no schema** (see §6)

---

## 5. Statutory Forms / Returns Deliverables & how they use `Apex.Ledger.Io`

All forms **reuse the existing NuGet-free, deterministic, de-branded `Apex.Ledger.Io` engine** (Phase 5).
Two categories:

**(1) Certificates & control charts → PDF** (S7). Form **16A** (TDS cert), **27D** (TCS cert), **27A**
(control chart/summary) render through `ReportPdf.cs` / `PdfWriter` + `PrintModel` + `PageConfig` — the
same pipeline that emits the GST tax-invoice (`InvoicePdf.cs`) and report PDFs — using
`IndianAmountInWords` for amounts. Form 27A auto-generates from the return and **cross-checks control
totals** (deductee/collectee record count + total tax deposited + total amount paid) tallying with the
return .txt before FVU validation.

**(2) Statutory return files → NSDL FVU flat-file** (S4/S6). Form **26Q** (quarterly TDS) and **27EQ**
(quarterly TCS) export as the **FVU-compatible NSDL text format** (fixed-width / caret-delimited .txt) via
a **new `FvuWriter`** mirroring the deterministic, byte-stable `CsvWriter`/`TabularExport` writers (no
clock/RNG, TabularDebrand-clean). The 26Q/27EQ line-item data is a **pure projection** (mirror
`Gstr1.Build` / `GstReportSupport.PostedGstVouchers` filtered to TDS/TCS payable directions). The clone
**emulates the FVU control-total checks** (record counts, challan tallies) and emits an error/warning
report — it produces the FVU-compatible upload file offline (no online TRACES/portal per project Q4). CSV
/ XLSX exports of every new master (Nature of Payment/Goods, challan register, exception reports) ride the
existing `TabularExport` path.

**Return metadata:** deductor TAN/PAN/type + responsible person; per-**challan** block (ITNS-281: BSR
code, challan serial, deposit date, tax/surcharge/cess/interest/fee); **deductee/collectee-wise** rows
(PAN, name, section/collection code, date, amount, tax, rate, §197/27C reason flag). Filing cadence for
user-facing reminders: **26Q** Q1 31-Jul / Q2 31-Oct / Q3 31-Jan / Q4 31-May; **27EQ** Q1 15-Jul / Q2
15-Oct / Q3 15-Jan / Q4 15-May; certs (16A/27D) within 15 days of the return due date; deposit by 7th of
following month (March TDS by 30-Apr).

> **⚠ RE-VERIFY:** FVU/RPU file format is versioned by Protean (NSDL) and changes periodically — pin the
> `FvuWriter` layout to a specific FVU version at build (A14). See Open Decision D4 for FVU-depth choice.

---

## 6. Exit Gate (P7-S9) — R9 + PR gates + golden worked example

Mirror the Phase-6 Slice-9 exit gate. **Does not pass until all of:**

1. **All prior PR gates (PR-1…PR-8) re-green** with evidence.
2. **Full test suite green** (Io / Ledger / Sqlite / Desktop) with counts shown; Robert + Bright extended
   regressions reconciled across all engines.
3. **Migration chain v24→v30 proven** — each `MigrateV(N-1)ToVN` + its legacy-DDL upgrade test; a fresh
   DB stamps to v30 via `CreateV1`.
4. **ER-13 byte-identical when TDS/TCS off** — a company that never enables TDS/TCS serializes identically
   to a v24 company (every new column DEFAULT 0/NULL, every new table empty).
5. **Io losslessness re-verified** — JSON+XML round-trip **paisa + count exact** for **ALL** Phase-7
   masters (Nature of Payment/Goods, deductor config, ledger/party/item flags, tds_lines/tcs_lines,
   challans); explicit "no silent drop" check (the Phase-6 defect class).
6. **De-brand scan** clean (no "Tally" in app UI/code/exports).
7. **A10 adversarial review** sign-off (paisa/rupee conservation, carve-out balance, threshold
   determinism — the highest-risk items in §7).
8. **REAL `Apex.Desktop` app launched** (kill any running exe first — a running exe locks the build),
   de-branded, TDS/TCS screens exercised.
9. **A12 commits & pushes**; PR to `main` CI green-capable across all 3 OS (audit for
   Windows-only-passing tests — the Phase-6 path-separator lesson).

### Golden worked example (the R9 deliverable, must be shown numerically)

**TDS (194J, deductor side):**
- Professional fee invoice ₹1,00,000 (crosses the ₹50,000 194J threshold), payee = Individual with PAN.
- Journal (F7): `Dr Professional Fees 1,00,000` / `Cr Vendor 90,000` / `Cr TDS Payable (194J) 10,000`
  (10%). **Assert gross 1,00,000 = net 90,000 + TDS 10,000 to the paisa.**
- No-PAN variant: TDS = 20% = ₹20,000 (net 80,000).
- Pay vendor (F5): `Dr Vendor 90,000 / Cr Bank 90,000` (Agst Ref).
- Deposit (F5 Ctrl+F Stat Payment): `Dr TDS Payable 10,000 / Cr Bank 10,000`; challan recorded; Alt+R
  reconciles → TDS Payable = 0.
- **Form 26Q** generated → deductee row + challan block reconcile to postings; **FVU** file byte-stable;
  **Form 27A** control totals tally; **Form 16A** cert figures match.

**TCS (206C scrap 1%, seller side):**
- Sale of scrap ₹1,00,000 + GST (say 18% = ₹18,000), buyer with PAN.
- Sales voucher: `Dr Buyer 1,19,000` / `Cr Sales 1,00,000` / `Cr Output GST 18,000` / `Cr TCS Payable
  1,000` (1% — confirm base incl/excl GST per §206C config). **Assert additive total foots; no
  double-count in GSTR-1 vs 27EQ.**
- No-PAN variant: TCS = 2×/5% per §206CC.
- Deposit via Stat Payment; **Form 27EQ** + FVU; **Form 27D** cert figures match.

---

## 7. Dependencies + Regression Risks

### Dependencies
- **Ordered/sequential build** (Phase-6 constraint): every schema slice edits `Schema.cs` +
  `SqliteCompanyStore.cs` + `Company.cs` on one migration chain → no parallel slice builds.
- **Slice order:** S1 (masters/config) is the foundation for all; S2 depends on S1; S3 on S2; S4 on
  S2+S3 (needs deductions + challans) and **must fold masters into Io**; S5 on S1 (reuses S2 engine
  shape); S6 on S5; S7 certs on S4/S6 projections; S8 exception reports on S2/S5; S9 on all.
- **Cross-cutting:** TDS/TCS on invoices touch Sales/Purchase `VoucherEntryVM` (like Phase-5/6 slices
  5&7) — coordinate with any concurrent GST/invoice edits; do not build in parallel with GST-touching work.
- **Reuse:** Phase-4 GST tax-ledger/Duties&Taxes machinery + Phase-5 `Apex.Ledger.Io`
  (PDF/flat-file/JSON-XML) — both shipped.
- **External:** **A14 leads** (GST-Notes PDF is the Phase-7 source) and web-verifies all law (§2.5
  checklist) at kickoff. A10 adversarial pass per slice. A12 all git (R4).

### Regression risks (ranked)
1. **Item-invoice validation on TDS carve-out (HIGHEST).** TDS reduces the party leg to NET while
   stock/expense stays GROSS → `EnsureItemInvoiceValid`'s stock-vs-party pairing no longer foots naively.
   Must teach the validator that `TDS Payable (D&T) credit + net party credit = gross`; verify
   `ClassificationRules.IsDutiesAndTaxesLedger` covers the new payable ledgers. *A green suite can hide a
   balance-sheet leak — exactly the Phase-6 job-work phantom-value bug class.*
2. **Paisa/rupee conservation.** GST is paisa-exact; TDS/TCS use income-tax **nearest-rupee** rounding — a
   different rounding domain. Carve-out must conserve exactly (gross = net + TDS to the paisa) or it leaks
   (the Phase-6 percent-carve-out-leak bug). **Compute-once-then-derive** like `GstService` total-then-split.
3. **Cumulative-FY threshold determinism.** Threshold depends on prior-YTD deductions per party×nature — a
   stateful accumulation. Must be a **pure projection** over posted vouchers (like `Gstr1`), never a
   clock/order-dependent side effect, or reports become non-deterministic.
4. **Io silent-drop (Phase-6 carry-forward).** New masters MUST be added to CanonicalModel/CanonicalMapper
   **in the introducing slice** (S4/S6), NOT deferred — else JSON/XML export→import drops them and breaks
   PR-4 losslessness.
5. **ER-13 byte-identical when off.** Every new column DEFAULT 0/NULL, every new table empty; legacy-DDL
   test per slice + off-company byte-identical assertion.
6. **VoucherType/base-type additions.** Stat Payment reuses the Payment base type via `is_stat_payment`
   flag — do **not** invent a new `VoucherBaseType` that could ripple through
   `GstReportSupport.DirectionOf` and other exhaustive switches.
7. **GST + TCS on the same sales invoice.** Both additive to the party leg — ensure ordering (TCS base may
   include GST) and no double-count in GSTR-1 vs 27EQ.
8. **Migration-chain fragility.** v24 is head; one additive `MigrateV(N-1)ToVN` per slice, txn-wrapped
   with the `schema_version` bump, fresh-DB via `CreateV1`, each proven by a legacy-DDL upgrade test.
9. **Windows-only-passing tests** (Phase-6 lesson): audit path-separator/OS-specific assertions before
   the PR CI gate so Linux/macOS runners pass.

---

## 8. Open Decisions for the User (approve before execution)

- **D1 — §206C(1H) treatment.** *Recommended:* implement as **legacy, year-gated** — default OFF /
  non-selectable for dates ≥ 01-Apr-2025 (27EQ/27D suppressed), retained + selectable for FY 2024-25 so
  historical/prior-year returns compute; current goods sales route to buyer-side §194Q TDS. **Approve, or
  choose "drop entirely" (no historical support) / "keep active" (non-compliant).**
- **D2 — Target FY.** *Recommended:* **FY 2025-26 (AY 2026-27)** as the primary target (rates in §2). If
  **FY 2026-27** is wanted, the new Income-tax Act, 2025 renumbering (ss.392/393/394) applies — masters
  must be year-aware and A14 must re-map every section code (adds scope to S1). **Confirm target FY.**
- **D3 — Salary TDS (§192 / Form 24Q / Form 16) scope.** *Recommended:* **DEFER to Phase 8 (Payroll)** —
  §192 needs slab computation on estimated annual salary + payroll structures. **Approve deferral, or pull
  a minimal §192 into Phase 7 (adds a slab-rate engine + 24Q form — significant scope).**
- **D4 — FVU-format depth.** *Recommended:* emit the **FVU-compatible NSDL flat-file + emulate control-
  total checks** (offline), pinned to one FVU version A14 verifies; do **not** attempt online
  TRACES/portal upload or bundling the actual government FVU/RPU JARs. **Confirm depth (flat-file +
  control-total emulation) vs. file-only (no validation) vs. deeper.**
- **D5 — §206AB/§206CCA + historical fidelity.** *Recommended:* **DO NOT implement** non-filer higher-rate
  logic (omitted FA 2025); mark verification-report #131 STALE. **Approve.** *(Optional D-hist: add
  date-gated FY 2021-22…2024-25 §206AB/§206CCA + active-206C(1H) only if you need pre-2025 return
  fidelity — extra scope; default = no.)*
- **D6 — §206C(1G) LRS / overseas-tour matrix.** *Recommended:* **defer to Phase 9+** (volatile multi-tier;
  low value for an SMB clone). **Approve deferral.**
- **D7 — Predefined master seed set.** Confirm the seeded Nature-of-Payment set (194A/194C/194H/194I/194J/
  194Q) and Nature-of-Goods set (scrap/timber/liquor/tendu/minerals/1F/1H-legacy) — or expand/trim. A14
  web-verifies rates for whatever set is chosen.

---

### Appendix — key reference files to mirror (absolute paths)

Services/domain: `src/Apex.Ledger/Services/GstService.cs`, `.../VoucherValidator.cs`;
`src/Apex.Ledger/Domain/{GstConfig,GstRateSlab,GstLineTax,LedgerGstClassification,GstEnums,PartyGstDetails,StockItemGstDetails,EntryLine,Ledger,Company,Gstin}.cs`;
`src/Apex.Ledger/Reports/{ClassificationRules,Gstr1,Gstr3b,GstReportSupport}.cs`;
`src/Apex.Ledger/Seed/SeedGstRates.cs`.
Persistence: `src/Apex.Persistence.Sqlite/{Schema,SqliteCompanyStore}.cs`.
Io: `src/Apex.Ledger.Io/{CanonicalModel,CanonicalMapper,ReportPdf,InvoicePdf,TabularExport,CsvWriter,IndianAmountInWords}.cs`.
Tests: `tests/Apex.Persistence.Sqlite.Tests/{JobWorkSchemaTests,GstRoundTripTests}.cs`.

*(All under `C:\Users\dkpho\OneDrive\Desktop\Apex Solutions(end)\.claude\worktrees\wonderful-hellman-59520a\`.)*
