# Phase 9 slice S8 — GST-advanced reports + annual returns (GSTR-9 / 9A / 9C) + QRMP/IFF + GSTR-1/3B amendment tables — Implementation Brief

> **Slice:** P9-S8. **NO schema change** (`docs/phase9-gst-advanced-requirements.md:899` — "S8 adds no schema — pure
> projections"; PR-8 gate `:1051–1053` "no schema change (ER-13 trivially holds)"). S8 is a **REPORTS** slice: every
> deliverable is a **pure, read-only projection** over data already posted/imported by S1–S7, in the exact `Gstr3b` /
> `Gstr1` record-then-`Build` shape (`src/Apex.Ledger/Reports/Gstr3b.cs:22–172`, `Gstr1.cs:112–270`). It **posts
> nothing** and **stores nothing** — so it carries **none** of S7's ledger-mutation risk. The single risk that
> replaces it: **aggregation / reconciliation correctness** — a full-year annual return that does **not** foot to the
> sum of the year's monthly returns, or a GSTR-9C that does **not** reconcile GSTR-9 to the audited books, is a
> *silent misstatement* (the top risk, §5).
>
> Design-only; grounded in on-disk code. Schema head is `Schema.CurrentVersion = 43` and **stays 43** (S7's v44 lands
> before S8; S8 touches no schema regardless). Governed by CLAUDE.md R1–R14; built via `/software`. The tax law
> (GSTR-9 Part I–VI table structure, GSTR-9A, GSTR-9C reconciliation-statement mechanics, QRMP 35%-challan + IFF,
> and the GSTR-1 amendment-table layout 9A/9B/9C/10/11) is **flagged for A14 confirmation throughout and consolidated
> in §8** — A14 is running in parallel on exactly these points and the results must be reconciled into this brief
> before build. A10 adversarial pass (reconciliation-footing the top lens) before the PR-8 gate; A12 owns git (R4).
>
> **Key RQs:** **RQ-17** (annual returns GSTR-9/9A/9C + QRMP/IFF election — `:787–788`), **RQ-29** (GSTR-1/3B
> amendment tables 9A/9C/10/11 + prior-period 3B correction — `:789–793`); supporting report facets of RQ-7…RQ-28
> (RCM statement, e-Invoice dashboard, e-Way exception, 2B-recon, set-off statement — `:1044–1050`). **DPs:** **DP-18**
> (light GSTR-9/9A/9C projections, on-screen + offline JSON — `:1260–1262`), **DP-19** (QRMP + IFF + quarterly-2B,
> offline — `:1263–1264`), **DP-33** (amendment tables as projections, one-amendment-per-doc; reflect GSTR-1A +
> Table-4 auto-lock — `:1310–1313`). **ERs:** **ER-9** (reports are pure projections over posted lines — never
> recompute), **ER-10** (decimal-rupees in-engine / integer-paisa at the Io boundary), **ER-13** (advanced-GST-off ⇒
> byte-identical; **trivial for S8** — no schema, no new posting, and every new projection early-returns
> empty/not-applicable for a non-GST or non-composition company), **ER-2** (cess ring-fence carried through every
> aggregate). Requirements anchors: S8 slice plan **`:1043–1055`**; §2.16 amendments **`:554–566`**; §2.11 Rule-42
> annual true-up (feeds GSTR-9 Table 7 / true-up) **`:486–501`**; §2.4 composition returns **`:314–337`**; §2.15
> zero-rated refund = **deferred DP-29** (out of S8) **`:541–552`**.

---

## 0. Grounding — what already exists (verified on disk)

S8 is the **breadth completion of GST reporting**. Almost everything it aggregates already has a proven per-period
projection; S8 mostly **rolls those up over a full FY** and adds three reconciliation layers (9 → monthly, 9C → books,
amendment → prior period). **Ten seams are reused verbatim:**

| Seam | File(s):line | What S8 reuses |
|---|---|---|
| **The per-period outward return** | `Gstr1.Build` — `src/Apex.Ledger/Reports/Gstr1.cs:158–270` (B2B `:167`, B2C `:240`, HSN `:251`, Table 9B `:316`, 11A/11B `:359`) | GSTR-9 Part II (Tables 4/5 outward) + Table 17 HSN = **Σ of 12 monthly `Gstr1`** (or 4 quarterly for QRMP). The B2B/B2C/export/HSN/CDN/advance rows are the atoms. |
| **The per-period summary return** | `Gstr3b.Build` — `Gstr3b.cs:130–172`; `ReadSide` `:323`; `ReadRcm` `:241`; `ReadReversals` `:180`; `Table4bBucketOf` `:225` | GSTR-9 Table 4 (outward tax), Table 6 (ITC availed = Σ 4(A)), Table 7 (ITC reversed = Σ 4(B)(1)+4(B)(2), S7b), Table 9 (tax paid) all foot to **Σ of the year's `Gstr3b`**. The reversal buckets (Rule 37/37A/42/43/§17(5)) are already tagged and summable. |
| **Composition per-quarter + annual roll-up** | `Cmp08.Build` — `Cmp08.cs:57–65` (via `CompositionTaxService.ComputeForPeriod`); `Gstr4.Build` — `Gstr4.cs:55–72` | GSTR-9A = the composition annual return; **`Gstr9a` ALREADY EXISTS** as a light projection (`Gstr4.cs:143–167`). GSTR-4's "**Table 6 == Σ Table 5 by construction**" pattern (`Gstr4.cs:42–52` — sum the already-rounded quarters, never whole-FY re-round) is the **reconciliation-by-construction template** for GSTR-9. |
| **Composition tax engine** | `CompositionTaxService.ComputeForPeriod` (`CompositionTax { TotalTurnover, TaxableTurnover, Cgst, Sgst, RcmInwardTax, SubType, RateBasisPoints }`) | The 9A turnover/tax source; consumed by `Cmp08`/`Gstr4`/`Gstr9a` today. |
| **Electronic ledgers (S7)** | `ElectronicLedgersView.Build` — `ElectronicLedgersView.cs:50–133` (credit/cash/liability projections) | GSTR-9 Table 9 "tax paid — in cash vs through ITC" reads the **cash discharge (PMT-06/`gst_challans`)** vs **credit utilisation (set-off Table 6.1)** exactly as this view decomposes them. The set-off statement in the S8 report tree **is** this view. |
| **2B reconciliation (S6a) + ITC gate (S6b)** | `Gstr2bReconciliationReport` (`ReversalCandidates :33`); `ItcGateView` | GSTR-9 **Table 8A** (ITC as per GSTR-2B) = Σ the year's imported 2B snapshots; Table 8 difference reconciles 8A vs Table 6 availed. The 2B-recon report is a direct report-tree node. |
| **The books (GSTR-9C reconciles TO these)** | `ProfitAndLoss.Build` (`ProfitAndLoss { Income, TotalIncome, Expenses, TotalExpenses, OpeningStock, ClosingStock, GrossProfit, NetProfit }` — `ProfitAndLoss.cs:35–43`); `BalanceSheet.Build` (`BalanceSheet { Liabilities, TotalLiabilities, Assets, TotalAssets, NetProfitInCapital }`, `Balanced :38` — `BalanceSheet.cs:31–38`) | GSTR-9C Part A Table 5 books **turnover** = `ProfitAndLoss.TotalIncome` (the audited P&L revenue); the ITC-per-books (Table 12) reads the Input tax-ledger closings. These are the "audited financials" side of the reconciliation. |
| **Offline-JSON writer (S3 precedent)** | `GstReturnJson` — `src/Apex.Ledger.Io/GstReturnJson.cs:25–167` (`Cmp08 :38`, `Gstr4 :68`; deterministic `System.Text.Json`, integer-paisa boundary `MoneyCodec.ToPaisa`, `FinancialPeriod` MMYYYY `:104`, `SchemaStatus` flag `:27`) | The exact template for GSTR-9/9A/9C/GSTR-1-amendment offline JSON: pure DTO, fixed property order, no clock/RNG, per-return `schemaStatus` flag. **The GSTN offline-utility JSON keys are the standing A14 gap** (`GstReturnJson.cs:18–23`). |
| **Report → PDF/tabular** | `ReportPdf.Render(PrintReport, PageConfig)` — `ReportPdf.cs:17`; `TabularExport`; `Desktop/Services/ReportTabularProjector.cs` | The Phase-5 report→PDF/CSV path any S8 return prints through. De-branded (ER-11). |
| **How GST returns surface** | `Desktop/ViewModels/Cmp08ReportViewModel.cs`, `Gstr4ReportViewModel.cs`, `ReportsViewModel.cs`, `MainWindowViewModel.cs` | The Reports → Statutory → GST Reports cascading Miller-column tree (memory: cascading-column-navigation). S8 adds annual-return + amendment + QRMP nodes here (UI is a thin later pass; the engine records are this brief's scope). |

**Five facts shape the whole S8 design:**

1. **Aggregation is Σ-of-already-rounded-periods, NEVER whole-FY re-computation.** `Gstr4` proves it: `Table 6 ==
   Σ Table 5 by construction` (`Gstr4.cs:42–52`) — it sums the four **already-rounded** quarterly CMP-08 figures so
   the annual total **cannot diverge** from the periods. GSTR-9 adopts the identical rule: **GSTR-9 = Σ (the year's
   monthly `Gstr3b`/`Gstr1`), each period rounded once, then added.** This is the structural guarantee that "the
   annual return foots to the monthly returns" (the top risk) holds *by construction*, not by a fragile re-derivation.
   A whole-FY re-aggregation from raw vouchers would round differently and silently break the foot — **explicitly
   rejected**.

2. **Two independent numbers must agree, and S8's job is to *show the difference*, not hide it.** GSTR-9C reconciles
   **books turnover** (`ProfitAndLoss.TotalIncome`) to **return turnover** (GSTR-9 Table 5N/Table 4). These are
   computed from *different* sources (accrual accounts vs posted GST tax lines) and *will* differ (schedule III
   supplies, non-GST income, timing). The reconciliation statement's whole purpose is a **transparent unreconciled
   difference line** — S8 computes both sides deterministically and reports the delta; it never forces them equal.

3. **QRMP is already no-schema.** The election is persisted **today** as `GstConfig.Periodicity`
   (`Monthly`/`Quarterly`, `GstConfig.cs:42` → `companies.gst_periodicity`, `Schema.cs:144`;
   `GstReturnPeriodicity` enum `GstEnums.cs:64–72`). So QRMP cadence, IFF windows, and M1/M2 PMT-06 are **pure
   projections that read that existing flag** — no new column, no new table. IFF is literally the **B2B subset of the
   existing `Gstr1` projection over the M1/M2 sub-windows**; the M1/M2 challans are the **existing S7 PMT-06 /
   `GstDepositService`** viewed at monthly cadence inside a quarterly return.

4. **GSTR-9A already exists — S8 completes it, it does not create it.** `Gstr9a` (`Gstr4.cs:143–167`) is a light
   composition annual roll-up over `CompositionTaxService`. S8 either (a) leaves it as-is and wires it into the
   annual-return tree + offline JSON, or (b) fills its remaining light fields (late fee §47, CMP-02/CMP-04
   transitions) — those are marked carry-forward today. **Recommend (a)+JSON**; keep the transition fields marked.

5. **Amendments need an "original-document" linkage, and S8 must find it WITHOUT new schema (§1.5, the crux).** The
   only existing original-invoice link is the **§34 CDN link** (`GstCreditDebitNoteLink { OriginalInvoiceVoucherId,
   OriginalInvoiceNumber, OriginalInvoiceDate }`, S2b) — which covers **9C CDNRA** cleanly. Ordinary-invoice
   amendments (9A B2BA/B2CLA/EXPA, 10 B2CS-A) have **no** persisted amend-link, and adding one is a schema change.
   §1.5 resolves this without schema.

---

## 1. GSTR-9 — annual return, regular taxpayer (RQ-17; DP-18) — PURE aggregation

### 1.1 Shape

`src/Apex.Ledger/Reports/Gstr9.cs` — a pure record (like `Gstr3b`) built over an **FY window** `[fyFrom, fyTo]`:

```csharp
public static Gstr9 Build(Company company, DateOnly fyFrom, DateOnly fyTo);
```

Like `Gstr1`/`Gstr3b`, it **early-returns not-applicable** for a Composition dealer (they file 9A, §2) and for a
GST-off company (all-zero) — so ER-13 is automatic. Internally it enumerates the **period sub-windows** of the FY
(12 monthly, or 4 quarterly when `GstConfig.Periodicity == Quarterly`), builds `Gstr3b.Build` + `Gstr1.Build` for each,
and **sums the already-rounded period figures** (fact 1). Paisa-exact, deterministic, no clock/RNG.

### 1.2 The Part I–VI table structure and its feeders (**A14 to confirm the full layout — §8.1**)

The GST portal's GSTR-9 has six parts. DP-18 scopes a **light** projection: **auto-fill every table that a monthly
`Gstr1`/`Gstr3b`/2B already yields; mark the rest carry-forward** (demands, refunds, some Part-VI info the app has no
source for). Recommended mapping:

| GSTR-9 table | Content | S8 feeder (Σ over the FY's periods unless noted) |
|---|---|---|
| **Part I** — basic details | GSTIN, FY, legal/trade name | `company.Gst.Gstin` + `company.Name` + the FY window. No computation. |
| **Part II — Table 4** (A–N) | Outward + inward-RCM supplies **on which tax IS payable** (B2C 4A, B2B 4B, exports 4C, SEZ 4D, deemed-export 4E, inward-RCM 4G, sub-total 4H; **credit notes 4I, debit notes 4J, amendments 4K/4L**, net 4N) | Σ monthly `Gstr1` (B2B/B2C/export rows) + `Gstr3b.OutwardCgst/Sgst/Igst` + `Gstr3b.TotalRcmOutward` (3.1(d), 4G). Credit/debit notes 4I/4J = Σ `Gstr1.Table9B` (signed). **A14: confirm whether 9's 4I/4J are gross or net-of-amendment.** |
| **Part II — Table 5** (A–N) | Outward supplies **on which tax is NOT payable** (exempt 5D, nil 5E, non-GST 5F, zero-rated-no-pay 5A, SEZ-no-pay 5B, no-tax CDN 5H, turnover 5N) | Σ `Gstr3b.ExemptNilNonGstOutward` + `Gstr1.ExemptNilNonGstValue`. **Turnover 5N is the anchor GSTR-9C reconciles to (§3).** |
| **Part III — Table 6** (A–O) | **ITC availed** during the FY (6A auto-from-3B, 6B inputs/capital-goods/input-services, 6C/6D inward-RCM, 6E imports, 6H reclaim, …) | 6A = Σ `Gstr3b.TotalItc` + `Gstr3b.TotalRcmItc`; 6H reclaim = Σ `Gstr3b.TotalItcReclaimed` (4(D)(1)). The input/capital-goods/service **split (6B)** has no posted source ⇒ **mark carry-forward or lump** (A14: is the split mandatory for a light 9?). |
| **Part III — Table 7** (A–H) | **ITC reversed + ineligible** (7A Rule 37, 7C Rule 42, 7D Rule 43, 7E §17(5), 7H other) | Σ `Gstr3b.ItcReversed4B1*` + `ItcReversed4B2*`, **split by rule** via the `GstAdjustmentKind` tag already on each posted reversal line (`Gstr3b.Table4bBucketOf`, `:225`) — S7b already routes Rule 42/43/§17(5) → 4(B)(1), Rule 37/37A → 4(B)(2). S8 sub-buckets 7A/7C/7D/7E from the same tag. **Includes the Rule-42/43 annual true-up** (§2.11 `:486–488`; the September-following true-up delta is a posted reversal row that falls in the *next* FY's Apr–Sep — Part V, below). |
| **Part III — Table 8** (A–D) | ITC **reconciliation** (8A ITC-as-per-2B, 8B ITC availed 6B+6H, 8D difference) | 8A = Σ the year's imported **GSTR-2B** ITC-available (S6a snapshots); 8B from Table 6; **8D = 8A − 8B (the difference line — report, never force to 0)**. |
| **Part IV — Table 9** | **Tax paid** as declared in the FY's returns (payable vs paid-in-cash vs paid-through-ITC, per head + cess + interest/late-fee) | Payable = Σ `Gstr3b` outward+RCM; **paid-through-ITC = Σ set-off Table 6.1 (S7 `gst_setoff_lines`); paid-in-cash = Σ PMT-06 (`gst_challans`)** — exactly the `ElectronicLedgersView` credit-utilisation vs cash-discharge decomposition (`:79–133`). Interest/late-fee = flag-only (DP-34). |
| **Part V — Tables 10–13** | **Prior-FY transactions declared in Apr–Sep of the current FY** (10 additions/amendments ↑, 11 reductions ↓, 12 ITC reversal, 13 ITC availed) | The **amendment tables (§4)** filtered to *original-period = prior FY, declared-period = Apr–Sep this FY*. This is where a late correction of last year's invoice lands. **A14: confirm the Apr–Sep (now Apr–Nov?) cut-off window for FY2025-26.** |
| **Part VI — Tables 14–19** | Differential tax (14), demands/refunds (15), composition/§143/approval-goods (16), **HSN outward (17)**, **HSN inward (18)**, late fee (19) | **Table 17 HSN = Σ `Gstr1.HsnSummary`** over the FY (reuse Phase-4 HSN, `:251`). Table 18 inward-HSN = Σ purchase-side HSN (light; may be carry-forward if no inward-HSN projection exists). 14/15/16/19 = **carry-forward/marked** (no posted source for demands/refunds). |

### 1.3 The reconciliation GSTR-9 must foot to (the deliverable's whole point)

Three foot-checks, each a **test assertion** (§6) and ideally a computed property on the record:

- **9 ↔ 3B:** `Gstr9.Table4TotalTax == Σ_months Gstr3b.TotalOutwardTax` and `Gstr9.Table6ItcAvailed ==
  Σ_months Gstr3b.TotalItc(+RCM)` and `Gstr9.Table7ItcReversed == Σ_months Gstr3b.TotalItcReversed`. Guaranteed by
  construction (fact 1) — the test *proves* the Σ was not re-rounded.
- **9 ↔ 1:** `Gstr9.Table4Outward(B2B+B2C+export taxable) == Σ_months Gstr1` per section.
- **Internal 9 identity:** Table 4N (net taxable) − Table 4I/4J (CDN) reconciles; Table 8D = 8A − 8B is *reported*,
  not forced.

**Paisa-exact, deterministic.** No period is re-rounded; a mid-year rate change (GST 2.0, S1) is invisible to the
aggregation because each month already posted the correct rate.

---

## 2. GSTR-9A — composition annual return (RQ-17; DP-18) — EXTEND the existing projection

`Gstr9a` **already exists** (`Gstr4.cs:143–167`): a light FY roll-up over `CompositionTaxService.ComputeForPeriod`
(TotalTurnover, TaxableTurnover, tax paid CGST/SGST, inward-RCM tax, late fee = Zero). S8's job:

1. **Reconcile 9A to the year's CMP-08 by construction** — mirror `Gstr4.AnnualCompositionTax` (`:42–52`): make the 9A
   tax-paid = **Σ the four quarterly `Cmp08.OutwardTurnoverTax` + `Cmp08.InwardRcmTax`** (the already-rounded
   quarters), NOT a fresh whole-FY `ComputeForPeriod` (which round-trips odd-paisa turnover differently). *(Note: the
   current `Gstr9a.Build` calls `ComputeForPeriod(fyFrom, fyTo)` directly — S8 should switch it to the quarter-sum to
   guarantee 9A == Σ CMP-08, matching the `Gstr4` template. Flag as a small correction.)*
2. **Wire it into the annual-return tree + offline JSON** (§4/§7).
3. Keep §47 late fee + CMP-02/CMP-04 (ITC-03/ITC-01) transitions **marked carry-forward** (as today) — DP-18 "light".

Deterministic, paisa-exact; not-applicable for a Regular company (`Gstr4.cs:161`).

---

## 3. GSTR-9C — reconciliation statement (RQ-17; DP-18) — reconciles GSTR-9 to the AUDITED BOOKS

`src/Apex.Ledger/Reports/Gstr9c.cs` — a pure record reconciling the **two independently-computed numbers** (fact 2).
**Self-certified, AATO > ₹5 cr** (no CA/CMA attestation since FY2020-21 — **A14 confirm the FY2025-26 threshold/
self-cert status, §8.3**). Structure (**A14 to confirm the full Part-A/B table layout — §8.3**):

| GSTR-9C table | Content | S8 computation |
|---|---|---|
| **Part A Table 5** | Reconciliation of **gross turnover** (5A turnover per audited financials → adjustments 5B–5P → 5Q turnover per GSTR-9 → **5R unreconciled difference**) | **5A = `ProfitAndLoss.Build(company, fyTo).TotalIncome`** (the audited P&L revenue, `ProfitAndLoss.cs:37`); **5Q = `Gstr9.Table5N` turnover**; **5R = 5A − 5Q** (the transparent delta — never forced to 0). Adjustment rows 5B–5P (unbilled revenue, deemed supply, trade discounts, foreign-exchange, Schedule-III) = **carry-forward/user-entered** (the app has no automatic source). |
| **Part A Table 6** | Reasons for the unreconciled turnover difference | Free-text/carry-forward (advisory). |
| **Part A Table 7** | Reconciliation of **taxable turnover** (7A–7F) | 7E taxable per GSTR-9 = `Gstr9` Table 4N taxable; book taxable = P&L revenue less exempt. |
| **Part A Tables 9–11** | Rate-wise **tax liability** reconciliation + **additional tax payable** | Tax per GSTR-9 (Table 9 payable) vs tax per books (the Output tax-ledger postings); 11 = additional payable (report). |
| **Part B Table 12** | Reconciliation of **net ITC** (12A ITC per audited books → 12E ITC per GSTR-9 → **12F unreconciled**) | **12A = the Input tax-ledger closings per books** (the same ledgers `ElectronicLedgersView` reads, `:56–60`); **12E = `Gstr9` Table 7 net ITC (6 − 7)**; 12F = delta (reported). |
| **Part B Tables 14–16** | ITC by expense head + **tax payable on unreconciled ITC** | 14 expense-head split = carry-forward (no posted expense-head→ITC map); 16 = report. |

**The reconciliation is the deliverable** — S8 computes **books-side** (P&L/BS + tax ledgers) and **return-side**
(GSTR-9) independently and reports **every unreconciled difference line**; it never mutates either side to match. The
Bright fixture (trading, has turnover + ITC) is the primary 9C fixture; Robert (accounts-only, GST-off) yields
not-applicable. **A14 flag: the precise mapping of "audited financials turnover" — gross vs net of GST, treatment of
Stock-in-Hand, whether `TotalIncome` is the right anchor vs a dedicated turnover ledger group (§8.3).**

---

## 4. QRMP + IFF (RQ-17; DP-19) — pure cadence projections over the EXISTING periodicity flag

**No schema** (fact 3 — `GstConfig.Periodicity` already persists the election). QRMP is the **Quarterly-Return-Monthly-
Payment** scheme for **AATO ≤ ₹5 cr** (`:106–112`). S8 models three things, all projections:

### 4.1 Period cadence
A QRMP filer (`Periodicity == Quarterly`) files **GSTR-1 + GSTR-3B quarterly** but **pays monthly**. S8 exposes a
**cadence helper** (`GstReturnCadence` or a static in `PeriodRange`) that, given the FY + periodicity, yields the
return windows (4 quarters) and the payment sub-windows (M1/M2/M3 within each). GSTR-9's period enumeration (§1.1)
reads this so a QRMP filer's annual return sums **4 quarters** not 12 months (still Σ-of-rounded-periods).

### 4.2 M1/M2 PMT-06 challan (35% / self-assessment)
The monthly tax deposit in M1 and M2 of a quarter is a **PMT-06 challan** computed one of two ways (**A14 confirm the
35% base + due-date §8.4**):
- **Fixed-sum (35%) method:** 35% of the tax paid **in cash** in the immediately preceding **quarter** (if the filer
  filed quarterly), or 100% of the last month's cash tax (if monthly last period).
- **Self-assessment method:** actual tax on M1/M2 supplies less available ITC.
This is a **projection over the existing S7 PMT-06 / `GstDepositService` + `gst_challans`** — S8 computes the *suggested*
M1/M2 deposit; the actual challan is posted by S7's poster (S8 posts nothing). **Reconcile with S7 PMT-06:** the M3
quarterly GSTR-3B nets the M1/M2 deposits already made (the cash ledger's minor-head balance, `ElectronicLedgersView`
cash cells, `:106–114`).

### 4.3 IFF — Invoice Furnishing Facility
IFF lets a QRMP filer upload **B2B invoices** for M1 and M2 (optional, ≤ ₹50 L/month, filed 1st–13th of the succeeding
month) so the recipient sees ITC early; B2C and the residual B2B go in the quarterly GSTR-1. S8's IFF projection is
**the B2B subset of `Gstr1.Build` over the M1 (and M2) sub-window** — i.e. `Gstr1.B2B` + `Table9B` for that month,
excluding B2C. The quarterly GSTR-1 then shows M3 B2B + all-quarter B2C + the M1/M2 B2B already furnished via IFF (no
double-count — a projection flag, not a posting). **A14 confirm the ≤ ₹50 L IFF cap + the B2B-only scope (§8.4).**

Deterministic; a Monthly filer's IFF/QRMP projections are **empty/not-applicable** (ER-13 trivially).

---

## 5. GSTR-1 amendment tables + GSTR-3B correction (RQ-29; DP-33; §2.16 `:554–566`)

### 5.1 The linkage problem and the no-schema resolution (the crux — fact 5)

An amendment reports, in the **current** period's amendment table, the **original document reference** + the
**revised** values. GSTR-1 amendment tables: **9A** = amended B2B (**B2BA**) / B2C-Large (**B2CLA**) / exports
(**EXPA**); **9C** = amended CDN (**CDNRA**); **10** = amended B2C-others (B2CS-A); **11(II)** = amended advances.
"One amendment per document" (`:558`).

**No new schema may store an amend-link.** Recommended resolution, in priority order:

1. **9C CDNRA reuses the existing §34 CDN link.** `GstCreditDebitNoteLink` already carries
   `OriginalInvoiceVoucherId/Number/Date` (S2b) — an amended credit/debit note is a CDN whose original ref points at a
   **prior-period** document; the 9C projection = `Gstr1.Table9B` rows whose `OriginalInvoiceDate` is in a prior filed
   period. **Fully no-schema.**
2. **9A/10/11 (ordinary-invoice amendments) modeled as an ADVISORY prior-period-delta projection.** Because there is no
   persisted amend-link and none may be added, the honest no-schema model is: an amendment is detected when a voucher
   dated/edited in period P carries a **document number + party that also appears in an earlier period** with different
   posted values. S8 projects the pair (original ref, revised values) as **advisory** (`:793` "advisory where the
   portal hard-locks"). **Recommend:** implement this as a projection that requires a caller-supplied
   "already-reported-through" period boundary (the last filed period), consistent with GSTR-1A (§5.2). This is
   **advisory and best-effort** — flagged as such, not a silent auto-detection that could mis-state.
3. **If A14/the gate demands persisted, exact amendment linkage** (an `amends_voucher_id` on the voucher), that is a
   **schema change** and must be **deferred to a follow-on slice** (record the deviation in `memory.md`, R6). S8's
   remit (DP-33) is the *projection*; the schema-backed exact link is out of the no-schema envelope.

> **Design principle:** S8 amendment tables are **projections/advisory** (DP-33 `:1310–1313`, `:793`). The one-
> amendment-per-document guard is enforced *within a projection* (dedup by original-doc ref), not by a stored
> constraint. This keeps S8 strictly no-schema while giving RQ-18's "corrections route to GSTR-1 amendment
> (no amend-on-IRP)" a real, testable target.

### 5.2 GSTR-1A + Table-4/3.1 auto-lock (reflect current-law, §2.16 `:559–564`)
- **GSTR-1A** (live Aug-2024): a **same-period** GSTR-1 amendment made **before** that period's GSTR-3B is filed. S8
  reflects this as the "amend within the open period" path (distinct from the cross-period 9A tables). **Advisory.**
- **GSTR-3B Table-4/3.1 auto-lock:** the portal increasingly auto-populates + hard-locks 3B from GSTR-1/2B. S8 models
  3B corrections as **advisory where the portal hard-locks** (`:793`) — see §5.3. **A14 confirm the FY2025-26
  in-force/mandatory status of GSTR-1A + the hard-lock (§8.5, §2.16 `:564`).**

### 5.3 GSTR-3B amendment mechanics — there is NO direct 3B amendment
A filed GSTR-3B **cannot be amended**; corrections flow through the **subsequent period's** 3B (add/reduce liability or
ITC in a later month). S8 models this as: the correction is simply a **later-period posting** that the later month's
`Gstr3b.Build` already picks up (no special construct). The **amendment/adjustment view** for 3B is therefore a
**difference report** — "period P's 3B figures vs the books/GSTR-1 for P" — surfacing what should be corrected in P+1,
**advisory**. No new record; it reads existing postings across two windows.

---

## 6. Advanced-GST report tree (RQ-7…RQ-28 report facets; `:1044–1050`)

The slice also consolidates the **Reports → Statutory → GST Reports** cascading tree. Most nodes **already exist** as
projections and only need tree-wiring (the UI is a thin later pass; the engine records exist):

| Report-tree node | Source (exists / new) |
|---|---|
| RCM Liability/ITC statement (3.1(d) vs 4A(2)+4A(3)) | `Gstr3b` RCM fields (`RcmOutward*`, `RcmItc*`, `:37–56`) — **exists**; thin new grouping record. |
| e-Invoice status dashboard | `Gstr1.EInvoiceReconciliation` (`:279–307`) — **exists**. |
| e-Way pending/exception report | S5 e-Way projections — **exists**; thin exception grouping. |
| GSTR-2B recon report | `Gstr2bReconciliationReport` (S6a) — **exists**. |
| Set-off statement / electronic ledgers | `ElectronicLedgersView` (S7) — **exists**. |
| HSN summary | `Gstr1.HsnSummary` (`:251`) — **exists** (reuse Phase-4). |

S8 adds **new records only** for the annual returns (§1–§3), amendment tables (§5), and QRMP/IFF (§4); everything else
is aggregation/wiring.

---

## 7. NO-schema note + Io/output (RQ-23 posture; ER-13)

### 7.1 No schema, no persistence — ER-13 is trivial
S8 adds **no table, no column, no migration** (`:899`). Every deliverable is a `Build(...)`-style pure record computed
on demand and thrown away (like `Gstr3b`/`Gstr1`). Therefore:
- `Schema.CurrentVersion` unchanged; no `MigrateV*`, no `SchemaMigrationEquivalenceTests` delta, no `DowngradeToV9/V11`
  edit, **no `SqliteCompanyStore` read/save change.**
- **ER-13 is automatic and provable**: for an advanced-GST-off (or non-GST) company every new projection early-returns
  empty/not-applicable (mirroring `Gstr1.cs:164`, `Gstr3b.cs:135`, `Gstr4.cs:57/161`), and — because nothing is
  persisted — an off company serialises **byte-identical** to a v43 company with zero effort. **PR-8 gate: "no schema
  change (ER-13 trivially holds)"** (`:1053`).

### 7.2 Io — offline JSON only, NO canonical-model fold-in
S8 returns are **compute+export**, not persisted state, so — unlike every prior slice — there is **nothing to fold
into `CanonicalModel`/`CanonicalMapper`/`CanonicalXml`/`ImportPlan`.** The **Io-bypass A10 defect class does not apply**
(there is no new stored entity to round-trip). Confirm this explicitly at the gate so A10 does not flag a "missing
canonical fold-in" false-positive.

**Offline JSON** (DP-18 "on-screen + offline JSON"; DP-19 "offline"): extend `GstReturnJson` (`GstReturnJson.cs:25`)
with `Gstr9(...)`, `Gstr9a(...)`, `Gstr9c(...)`, and the GSTR-1 amendment tables — mirroring the existing
`Cmp08`/`Gstr4` writers exactly (deterministic `System.Text.Json`, fixed property order, **integer-paisa boundary**
`MoneyCodec.ToPaisa` per ER-10, `FinancialPeriod` MMYYYY `:104`, per-return **`schemaStatus` flag** `:27`). **The GSTN
offline-utility JSON keys/nesting are the standing A14 gap** (`GstReturnJson.cs:18–23`) — same `schemaStatus:
"faithful-structured; GSTN offline-tool JSON keys pending A14 confirmation"` posture; the projection *records* are
correct regardless, only the envelope is schema-sensitive.

**PDF/tabular:** each on-screen return prints through `ReportPdf.Render(PrintReport, PageConfig)` (`:17`) + the
`ReportTabularProjector`/`TabularExport` CSV path, de-branded (ER-11, never "Tally"). Deterministic (PR-8 gate:
"PDFs/exports deterministic + de-branded" `:1052`).

### 7.3 ER-13 confirmation (explicit)
An advanced-GST-**off** company's existing returns (Phase-4 GSTR-1/3B, and the Robert/Bright fixtures) are **unchanged
byte-for-byte** because S8 adds no code path that any existing `Build` calls — the new records are *additional*
entry-points, not edits to `Gstr1.Build`/`Gstr3b.Build`. The **one exception to audit**: if §2's small correction
(switch `Gstr9a.Build` to quarter-sum) touches shared code, confirm it leaves `Gstr4`/`Cmp08` byte-identical.

---

## 8. Law points — **FLAGGED FOR A14 CONFIRMATION** (running in parallel; reconcile before build)

A14 is verifying exactly these in parallel (R7). Every item below is **provisional** until A14 returns; the numbers
above are the *design target* to be reconciled. **All must be reconciled to A14's verdict and the results folded in.**

### 8.1 GSTR-9 structure & feeders
- The **full Part I–VI / Table 4–19 layout** for FY2025-26 (post-GST-2.0) — confirm no notification re-numbered tables.
- **Table 6B** input/capital-goods/input-service split — is it mandatory for a *light* 9, or may S8 lump/mark it?
- **Table 4I/4J** (credit/debit notes) gross vs net-of-amendment.
- **Table 8** (8A from 2B) — confirm 8A source is GSTR-2B (not 2A) for the annual figure.
- The **Rule-42/43 annual true-up** (§2.11 `:486–488`) — confirm it is a September-following-return posting that lands
  in **Part V** (prior-FY-in-current-year), not double-counted in Table 7.

### 8.2 GSTR-9 optionality / thresholds (FY2025-26)
- GSTR-9 **filing threshold** (AATO > ₹2 cr mandatory; optional below) and which tables are **relaxation-optional**
  for the target FY — so S8 marks the right tables "optional/carry-forward."

### 8.3 GSTR-9C
- The **full Part-A/Part-B table layout** (5–16) and the **self-certification** status + **AATO > ₹5 cr** applicability
  for FY2025-26.
- The exact definition of **"turnover as per audited financial statements"** (5A) and whether
  `ProfitAndLoss.TotalIncome` is the faithful anchor (gross vs net of GST; Stock-in-Hand; a dedicated turnover group).
- The **ITC-per-books** (12A) source — confirm the Input tax-ledger closings are the right books-side figure.

### 8.4 QRMP + IFF
- QRMP **eligibility** (AATO ≤ ₹5 cr) and the **35% fixed-sum** PMT-06 base (preceding-quarter cash vs last-month) +
  **due dates** (M1/M2 by 25th of M+1; quarterly return dates).
- **IFF** monthly **≤ ₹50 L cap**, **B2B-only** scope, and the 1st–13th furnishing window.
- **Quarterly-2B** behaviour for a QRMP filer (§2.7 `:400` — QRMP filers get 2B quarterly).

### 8.5 Amendments
- The **GSTR-1 amendment-table layout** 9A (B2BA/B2CLA/EXPA) / 9C (CDNRA/CDNURA) / 10 (B2CS-A) / 11(II), and the
  **one-amendment-per-document** rule wording.
- **GSTR-1A** (same-period pre-3B amend) and the **GSTR-3B Table-4/3.1 auto-lock** — **in-force/mandatory for
  FY2025-26?** (`:564`) — this decides how "advisory" the amendment/3B-correction views must be.
- The **Part V (GSTR-9 Tables 10–13) cut-off window** — Apr–Sep vs Apr–Nov of the following FY for declaring prior-FY
  transactions.

### 8.6 Offline-JSON schema (standing gap)
- The **GSTN offline-utility JSON keys/nesting** for GSTR-9/9A/9C + the GSTR-1 amendment sections, and whether money is
  **rupee-decimal** on the wire vs the **integer-paisa** used internally (ER-10) — the same open item as CMP-08/GSTR-4
  (`GstReturnJson.cs:18–23`). Until confirmed, emit `schemaStatus`-flagged faithful-structured JSON.

---

## 9. TDD test list (failing-first; ground in the test projects)

Projects: `Apex.Ledger.Tests` (report projections — the bulk), `Apex.Ledger.Io.Tests` (offline-JSON determinism +
PDF), `Apex.Desktop.Tests` (report-tree wiring — a later UI pass). Each test **red first**, proven to fail pre-build.

**GSTR-9 (RQ-17) — the reconciliation is the point**
1. **9 foots to the year's 3B (tax):** for a multi-month GST company, `Gstr9.Table4TotalTax == Σ_months
   Gstr3b.TotalOutwardTax`, `Table6 == Σ Gstr3b.TotalItc(+RCM)`, `Table7 == Σ Gstr3b.TotalItcReversed` — **paisa-exact,
   proving no whole-FY re-round** (fact 1).
2. **9 foots to the year's 1 (outward):** `Gstr9` Table 4 B2B/B2C/export == Σ_months `Gstr1` per section; Table 17 HSN
   == Σ_months `Gstr1.HsnSummary`.
3. **9 tax-paid split (Table 9):** paid-through-ITC == Σ set-off Table 6.1 (S7 `gst_setoff_lines`); paid-in-cash == Σ
   PMT-06 (`gst_challans`) — matches `ElectronicLedgersView` decomposition.
4. **Reversal rule-split (Table 7):** Rule 42/43/§17(5) land in 7C/7D/7E, Rule 37/37A in 7A — driven by the
   `GstAdjustmentKind` tag; a company with no reversal shows Table 7 all-zero.
5. **Composition dealer & GST-off ⇒ not-applicable 9** (empty), a targeted no-op assertion (mirrors `Gstr1.cs:164`).
6. **Determinism:** the same company yields byte-identical `Gstr9` across runs.

**GSTR-9A (RQ-17)**
7. **9A foots to the year's CMP-08:** `Gstr9a` composition-tax-paid == Σ 4 quarters' `Cmp08.OutwardTurnoverTax` +
   `InwardRcmTax` (the `Gstr4` template) — and a Regular company yields not-applicable.

**GSTR-9C (RQ-17) — reconciles to the books**
8. **9C reconciles to Bright's books:** 5A == `ProfitAndLoss(Bright).TotalIncome`; 5Q == `Gstr9(Bright)` turnover; 5R
   == 5A − 5Q (the unreconciled difference is *reported*, not forced to zero). 12A (ITC per books, Input-ledger
   closings) vs 12E (GSTR-9 net ITC) → 12F reported.
9. **9C on Robert (accounts-only/GST-off) ⇒ not-applicable.**
10. **The difference line is transparent:** a deliberately-inserted book-only income (Schedule-III supply) shows up as
    a **non-zero 5R**, never silently absorbed.

**QRMP + IFF (RQ-17; DP-19)**
11. **Cadence:** a `Periodicity == Quarterly` company's `Gstr9` sums **4 quarters** (not 12 months) and still foots to
    the quarterly `Gstr3b`/`Gstr1`.
12. **IFF = B2B subset of the M1/M2 window:** the IFF projection == `Gstr1.B2B` for that month (excludes B2C); the
    quarterly GSTR-1 does not double-count the IFF-furnished B2B.
13. **M1/M2 PMT-06 suggestion** reconciles with the S7 `gst_challans` deposits (the M3 quarterly 3B nets M1/M2 cash).
14. **Monthly filer ⇒ QRMP/IFF empty** (ER-13 trivially).

**Amendments (RQ-29; DP-33)**
15. **9C CDNRA via the §34 link:** an amended credit note (CDN whose `OriginalInvoiceDate` is in a prior period)
    appears in the 9C amendment projection with the original ref + revised values.
16. **9A B2BA advisory delta:** an ordinary invoice re-stated in a later period surfaces (original ref, revised values)
    in the advisory amendment projection; **one-amendment-per-document** dedup holds (a second re-statement replaces,
    not stacks).
17. **GSTR-3B correction flows to the next period:** a later-period posting is picked up by the later month's
    `Gstr3b.Build`; the 3B-difference view flags period-P under-reporting as a P+1 correction (advisory).

**Io / output**
18. **Offline JSON determinism:** `GstReturnJson.Gstr9/Gstr9a/Gstr9c(...)` emit byte-stable, integer-paisa,
    fixed-property-order JSON carrying the `schemaStatus` flag; re-serialising is identical.
19. **PDF/tabular deterministic + de-branded:** each return renders through `ReportPdf`/`TabularExport` with no "Tally"
    string (ER-11) and stable bytes.

**ER-13 off + regression fixtures**
20. **ER-13:** an advanced-GST-off company's existing Phase-4 GSTR-1/3B + the S1–S7 fixtures are **byte-identical**
    before/after S8 (S8 adds only new entry-points; it edits no existing `Build`).
21. **Robert + Bright + Phase-4 base-GST GSTR-1/3B fixtures stay green / byte-identical;** the §2 `Gstr9a` correction
    (quarter-sum) leaves `Gstr4`/`Cmp08` byte-identical.

---

## 10. Risks / subtleties (ranked)

1. **An annual return that does NOT reconcile to the monthly returns or the books — a *silent misstatement* (HIGHEST).**
   The entire slice's value is footing 9 → Σ(3B/1) and 9C → books. Mitigated **by construction**: Σ-of-already-rounded-
   periods (fact 1, the `Gstr4` template `:42–52`) makes the 9↔monthly foot exact and un-re-roundable; the 9C
   difference line is **computed and displayed, never forced** (fact 2). Tests 1–3, 8, 10 assert the foots hold and the
   difference is transparent. A quiet re-round or a hidden reconciliation would be the failure mode that must be
   impossible.
2. **GSTR-9C mapping the wrong "books turnover" / wrong ITC-per-books.** `ProfitAndLoss.TotalIncome` vs a dedicated
   turnover group, gross vs net of GST, Stock-in-Hand treatment — a wrong anchor makes 5R meaningless. **A14 must
   confirm the anchor (§8.3);** test 8 pins it to Bright.
3. **Amendment linkage with no schema (§5.1).** 9C CDNRA is clean (reuses the §34 link); 9A/10/11 ordinary-invoice
   amendments have no persisted link and must stay **advisory** (DP-33 `:793`). Risk: over-claiming automatic detection.
   Mitigated by scoping 9A/10/11 as advisory prior-period-delta projections and deferring any exact `amends_voucher_id`
   link to a schema-bearing follow-on (R6 deviation if pursued). **A14 confirm the amendment-table law (§8.5).**
4. **Table-4/3.1 auto-lock + GSTR-1A currency (§5.2).** If the portal hard-locks 3B for FY2025-26, the 3B-correction
   view must be advisory only; asserting an editable 3B would mis-model the law. **A14 confirm in-force status (§8.5).**
5. **QRMP cadence leaking into a monthly filer (or vice-versa).** The FY period-enumeration must read
   `GstConfig.Periodicity` and pick 4 vs 12 sub-windows consistently across 9/9C/IFF/PMT-06; a mismatch double- or
   half-counts. Test 11/14.
6. **Rule-42/43 annual true-up double-count (§2.11).** The September-following true-up delta is a *next-FY* Apr–Sep
   posting (GSTR-9 Part V), not a second Table-7 reversal in the same FY. Test 4 + §8.1. A conflation over-states Table 7.
7. **Cess ring-fence through every aggregate (ER-2).** Every Σ (Table 4/6/7/9, 9C ITC) must keep cess in its own column
   — the per-period projections already ring-fence it (`Gstr3b` `*Cess` fields, `Cmp08.PayableCess`); S8 must not merge
   it into the GST-head totals when summing.
8. **Offline-JSON schema drift (§8.6).** The GSTN keys are unconfirmed (`GstReturnJson.cs:18–23`); ship `schemaStatus`-
   flagged faithful-structured JSON, projection records correct regardless. A rename pass follows A14.
9. **False "missing Io fold-in" flag (§7.2).** S8 persists nothing, so there is deliberately no canonical-model change;
   confirm at the gate so A10 does not treat the absence as the recurring Io-bypass defect.
10. **`Gstr9a` quarter-sum correction touching shared code.** Switching `Gstr9a.Build` to Σ-quarters must leave
    `Gstr4`/`Cmp08` byte-identical (test 21).

---

## 11. Build recommendation — SPLIT into S8a + S8b under ONE PR-8 gate (mirrors S6a/S6b, S7a/S7b)

The slice is broad (annual returns 9/9A/9C + QRMP/IFF + amendment tables + report-tree wiring) but shallow (all pure
projections, no schema, no posting). Recommend **two coherent conventional commits under the single PR-8 gate**:

- **S8a — annual returns (the reconciliation core).** `Gstr9` (Part I–VI aggregation + 9↔3B/1 foots, §1), the `Gstr9a`
  completion (quarter-sum reconciliation + tree/JSON, §2), and `Gstr9c` (books reconciliation over P&L/BS + Input
  ledgers, §3); the `GstReturnJson` offline-JSON writers for all three (§7.2); tests 1–10, 18–21. This is the
  math-heavy, top-risk-bearing core — reviewable in isolation.
- **S8b — QRMP/IFF + amendments + report-tree wiring.** The `GstReturnCadence` helper + IFF/M1-M2-PMT-06 projections
  (§4), the GSTR-1 amendment tables + 3B-correction advisory view (§5), and the advanced-GST report-tree consolidation
  (§6, mostly wiring existing projections); tests 11–17. Logic-only, no schema, lower-risk.

Both land behind **PR-8** with the full A10 3-lens pass (**reconciliation-footing the top lens** — 9↔monthly, 9C↔books,
Σ-of-rounded-periods, cess ring-fence, ER-13 byte-identity), A12 commits, CI green across 3 OS. Advantage: the
annual-return reconciliation (the slice's whole value) is reviewed before the breadth of QRMP/amendments/tree wiring
piles on; the entire slice being no-schema means ER-13 is a one-line gate confirmation.

---

## Appendix — file manifest (create ● / edit ○)

- ● `src/Apex.Ledger/Reports/{Gstr9,Gstr9c}.cs` — pure records + `Build(company, fyFrom, fyTo)` (mirror
  `Gstr3b`/`Gstr4` shape); Σ-of-rounded-periods aggregation (§1, §3).
- ○ `src/Apex.Ledger/Reports/Gstr4.cs` — `Gstr9a` completion: switch `Build` to the quarter-sum reconciliation
  (`AnnualCompositionTax` template `:42–52`), keep late-fee/transition fields marked (§2).
- ● `src/Apex.Ledger/Reports/{GstReturnCadence, Gstr1AmendmentTables}.cs` (or fold into `PeriodRange`/`Gstr1`) — QRMP
  cadence helper + IFF window projection (§4); GSTR-1 amendment-table + 3B-correction advisory projections (§5).
- ○ `src/Apex.Ledger/Reports/Gstr1.cs` / `Gstr3b.cs` — *read-only reuse* (no edit expected beyond exposing an internal
  sum if convenient); the amendment projection reads `Table9B` / the §34 CDN link.
- ○ `src/Apex.Ledger.Io/GstReturnJson.cs` — add `Gstr9`/`Gstr9a`/`Gstr9c` + amendment-table offline-JSON writers
  (mirror `Cmp08`/`Gstr4` `:38–100`; integer-paisa boundary, `schemaStatus` flag) (§7.2).
- ○ `src/Apex.Desktop/ViewModels/*` + the Reports → Statutory → GST tree — new annual-return / amendment / QRMP nodes
  (thin UI pass; mirror `Cmp08ReportViewModel`/`Gstr4ReportViewModel`) (§6).
- ○ Tests: `Apex.Ledger.Tests` (the 9/9A/9C/QRMP/IFF/amendment projections — bulk), `Apex.Ledger.Io.Tests`
  (offline-JSON determinism + PDF/tabular), `Apex.Desktop.Tests` (tree wiring — later UI pass).
- **NO edits to** `Schema.cs`, `SqliteCompanyStore.cs`, `CanonicalModel/Mapper/Xml.cs`, `ImportPlan.cs`,
  `DowngradeToV9/V11` — S8 is no-schema, no-persistence (§7.1).
