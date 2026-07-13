# Phase 8 — Payroll Requirements & Implementation Plan (Apex Solutions, Tally Prime clone)

> **Status:** PLAN-ONLY (requirements + decision-ready plan for user approval per R9/R12). **Nothing here is
> executed.** No application code is written by this document. Schema head is **v29** (Phase 7 TDS/TCS
> complete @ `be17d0c`). Payroll adds **v30 → v3x** incrementally (additive migrations, each folded into the
> `Apex.Ledger.Io` canonical model in its introducing slice). Governed by CLAUDE.md R1–R14; built via
> `/software`, sequenced by the main loop, executed by named agents (A14 leads law/fidelity; A10 adversarial
> review; A12 GitHub Expert owns all git per R4).
>
> **A14 MUST re-verify every rate / ceiling / slab / statutory constant against official sources at Phase-8
> kickoff and per slice (R7).** All law in §2 is web-verified as-of the dates shown; treat every "⚠ RE-VERIFY"
> flag as a build-time gate, not a settled fact. Payroll statutory law drifts frequently (wage-ceiling hikes,
> new Labour Codes, PT slab revisions), so re-verification is mandatory even where a source is cited.
>
> **Reuse posture:** Payroll is the **last statutory pillar** and deliberately reuses machinery already
> shipped — the Phase-4 GST tax-ledger/`Duties & Taxes` pattern, the Phase-5 `Apex.Ledger.Io`
> (PDF / flat-file / JSON+XML) engine, and — critically — the **Phase-7 TDS engine (`TdsService`,
> TDS-Payable ledgers, challans, `FvuWriter`)**, which **§192 salary-TDS + Form 24Q extend rather than
> fork**. This is the withholding foundation the plan repeatedly leans on.

---

## 1. Scope & Fidelity

### 1.1 What Tally Prime Payroll does (grounded in the catalog + corpus)

Tally Prime Payroll is a **complete payroll-management module integrated with the accounting engine**: it
computes employee remuneration from attendance + a user-defined salary structure, posts the results to
accounting ledgers, and produces payslips, registers, and the PF/ESI/PT/Income-Tax statutory outputs.
Grounded in `docs/tally-feature-catalog.md` §14 and the corpus **Study-Guide PDF, Chapter 11 "Payroll
Management" (pp. 187–222)** and **Tally-Prime-Book PDF §11**. Defining behaviours a faithful clone must
preserve:

- **Enable via F11** — `F11 → Maintain Payroll = Yes` + `Enable Payroll Statutory = Yes`, which opens the
  **Company Payroll Statutory Details** screen for PF codes (Company/Account-group/Security code), ESI codes
  (Company code, branch office, **standard working days per month = 26** default), NPS, and Income-Tax (TAN,
  TAN registration no., circle/ward, deductor type/branch, person responsible + PAN). *(Catalog §14; Study
  Guide pp. 192.)* **Payroll and the Attendance/Payroll voucher types appear only once F11 Payroll is on**
  (verification-report #15).
- **Employee masters** — **Employee Category** (revenue / non-revenue cost allocation; default *Primary Cost
  Category*), **Employee Group** (optional department/division/function classification; "Define salary
  details?"), **Employee** (joining date, Employee No., designation, function, location, gender, DOB,
  **PAN / Aadhaar / UAN / PF a/c / EPS a/c / PRAN / ESI no. / ESI dispensary**, bank details, contract,
  passport/visa, **Applicable Tax Regime**). *(Study Guide pp. 193–195.)*
- **Payroll masters** — **Payroll Units** (Simple: Days/Hrs/Month; Compound: "Hrs of 60 Min", "Month of 26
  Days"), **Attendance/Production Types** (Attendance/Leave-with-Pay, Leave-without-Pay, Production,
  User-defined Calendar; Period type Days/Hrs). *(Study Guide pp. 196–197.)*
- **Pay Heads** — the heart of the structure. Each pay head has a **Pay Head Type** (Earnings; Employees'
  Statutory Deductions; Employer's Statutory Contributions; Employer's Other Charges; Bonus; Gratuity; Loans
  & Advances; Reimbursements to Employees; Not Applicable/Payable) posted **Under** an accounting group
  (Indirect Expenses / Current Liabilities / Duties & Taxes), plus a **Calculation Type**:
  **On Attendance / Flat Rate / As Computed Value / On Production / As User-Defined Value**, with slab
  formulas ("Add Pay Head …", Slab Type Percentage/Value, "Amount Up To / Greater Than", Rounding Method),
  an **Income-Tax component** tag (Basic Salary / HRA / Transport Allowance / Bonus / Fully-Exempt / …), and
  a **"Use for calculation of gratuity?"** flag. *(Catalog §14; Study Guide pp. 198–210 — worked pay-head
  set: Basic, DA = 40% of Basic, HRA = 20% of (Basic+DA), TA = 10% of Basic, Uniform Washing (flat), Bonus,
  Over Time (on production), Professional Tax, Employee PF 12%, Employer EPS 8.33%, Employer PF 3.67%,
  Employee ESI 0.75%, Employer ESI 3.25%.)*
- **Salary structure** — defined at **Employee Group** and/or **Employee** level, with **effective-from**
  dates and **Start Type = Copy From Parent / Copy From Employee / Start Afresh**. *(Study Guide pp. 211–212.)*
- **Process cycle** — **Attendance voucher** (`F10 → Attendance`, or **Ctrl+F Autofill**) → **Payroll
  voucher** (`Ctrl+F4`, **Payroll Autofill** for Salary / PF Contribution / ESI Contribution / NPS /
  User-Defined) → **Payment voucher** (`F5`, **Payroll Autofill** for Salary Payment / PF Challan / ESI
  Challan / PT Payment) with per-employee **Bank Allocation** (instrument no. per employee). *(Catalog §14;
  Study Guide pp. 213–221.)*
- **Reports** — Payslip, Pay Sheet, Attendance Sheet, Payment Advice, Payroll Register/Statement, Employee
  Profile / Head Count, Expat (passport/visa/contract expiry). **Statutory:** PF (Form 5/10/12A/3A/6A/**ECR**),
  ESI (Form 3/5/6/monthly), Professional Tax, NPS, **Gratuity**, Income-Tax computation + **Form 24Q / Form
  16**. *(Catalog §14/§16.)*

### 1.2 In scope (Phase 8)

- **Masters & config:** F11 Enable Payroll + statutory; Employee Category/Group/Employee; Payroll Units;
  Attendance/Production Types; Pay Heads (all calculation types); Salary Structures (group + employee, dated).
- **Engine:** Attendance voucher; **Payroll voucher** salary-computation engine (resolve pay heads per
  employee per period against attendance/production and the dated salary structure); **integrated accounting
  posting** (earnings → expense; deductions/contributions → payable/statutory ledgers; net → Salary Payable);
  Payment vouchers with employee-wise bank allocation.
- **Statutory computation:** **PF** (employee 12%; **computed EPS/EPF split**, EDLI, admin charges) with
  ECR; **ESI** (0.75% / 3.25%, ₹21,000 ceiling, contribution/benefit periods) with monthly return; **PT**
  (state-configurable slabs, seed a representative set) with annual ₹2,500 cap; **§192 salary TDS** with
  Form 24Q (Annexure I all quarters + Annexure II in Q4) + FVU export, reusing/extending the Phase-7 TDS
  engine; **Gratuity** provision computation + report; **statutory Bonus** (light).
- **Reports/returns:** Payslip, Pay Sheet, Payroll Statement/Register, Attendance Sheet, Payment Advice,
  Employee Profile/Head Count, Expat; PF/ESI/PT/NPS/Gratuity/Income-Tax statutory reports; Form 16 Part B.
- **Io losslessness:** every new master + voucher extension folded into `Apex.Ledger.Io`
  CanonicalModel/CanonicalMapper (JSON + XML round-trip) **in the introducing slice** — never deferred (the
  Phase-6 carry-forward defect class).

### 1.3 Explicitly OUT of scope (deferred / decide at gate)

- **Actuarial gratuity valuation** (AS-15 / Ind-AS-19 projected-unit-credit, discount-rate assumptions) —
  out; we compute the **statutory gratuity provision** (15/26 formula, ₹20L cap) only.
- **Income-Tax investment-declaration/proof-management workflow depth** (Chapter VI-A optimizer, HRA-exemption
  auto-computation from rent receipts, perquisite valuation) — build the **regime engine + standard
  deduction + 87A + surcharge/cess** and a **declared-deductions capture** (old regime), but not a full
  proof-management/perquisite-valuation subsystem. Flag at gate (DP-4/DP-11).
- **New Labour Codes** (Code on Wages 2019 / Social Security Code 2020 "wages ≥ 50% of CTC" redefinition) —
  **NOT notified/enforced as of the verification date**; build to the currently-operative EPF/ESI/PT/IT
  law and flag as a watch-item (see §2 and DP-7).
- **Online statutory round-trips** — no live EPFO/ESIC/TRACES portal upload; **offline file generation only**
  (ECR text, ESI return, FVU flat-file), consistent with the project-wide offline posture (plan.md §1.3, Q4).
- **§206C(1G) / GST-side payroll interactions / multi-country payroll** — out.

### 1.4 Fidelity anchors (fixtures)

Phase 8 adds a **payroll fixture** (a small deterministic workforce mirroring the corpus "Rajkumar Sharma /
Marketing" example — Study Guide pp. 193–221) with exact expected payslip, PF/ESI/PT/TDS figures, and a
Form-24Q line, kept green in every subsequent phase (R8). The existing **Robert** and **Bright** ledger
fixtures **must stay green** (payroll off ⇒ ER-13 byte-identical).

---

## 2. Web-Verified Statutory Law (FY 2025-26 basis) + re-verify flags

> **Target FY assumption:** **FY 2025-26 (AY 2026-27)** — consistent with Phase 7 (DP-1). All figures below
> are web-verified as-of the dates shown. **Every value is config-driven / seedable, never hardcoded** —
> A14 re-verifies at kickoff. Where two authoritative sources disagree (notably PF admin / EDLI-admin exact
> figures), the discrepancy is flagged for build-time resolution.

### 2.1 EPF / EPS / EDLI / administration (Employees' Provident Fund) — as-of 2026-07-11

| Account | Head | Employee | Employer | Base / ceiling | Notes |
|---|---|---|---|---|---|
| **A/c 1** | EPF (Provident Fund) | **12%** | **balance** (≈3.67% at ceiling) | Basic + DA (+ retaining allowance) | Employer PF = (12% × PF-wage) − EPS. Equals 3.67% **only** when wage = ₹15,000. |
| **A/c 10** | EPS (Pension) | — | **8.33%** | **min(PF-wage, ₹15,000)** | **Max EPS = ₹1,250/month** (8.33% × 15,000 = 1,249.50 → ₹1,250). |
| **A/c 21** | EDLI (insurance) | — | **0.50%** | min(PF-wage, ₹15,000) | **Capped ≈ ₹75/employee/month.** |
| **A/c 2** | EPF admin charges | — | **0.50%** | PF-wage | **Minimum ₹500/month per establishment** (₹75 if no contributing member that month). *(w.e.f. 01-Jun-2018.)* |
| **A/c 22** | EDLI admin charges | — | **NIL** (waived) | — | Waived w.e.f. 01-Apr-2017. |

- **Employee 12%** goes wholly to A/c 1. When Basic+DA > ₹15,000, **employee PF is commonly capped at ₹1,800**
  (12% × 15,000) unless the employer opts to contribute on actual wages (higher-wage option, DP-7).
- **Wage ceiling ₹15,000**; **EPF interest FY 2025-26 = 8.25% p.a.** (declared).
- **⚠ RE-VERIFY (source drift):** one secondary source (taxguru) still shows A/c 2 = **0.65%** and A/c 22 =
  **0.01% min ₹200** (pre-2018 figures). The **current** rule is A/c 2 = **0.50% min ₹500** and A/c 22 =
  **NIL**. **A14 must confirm the exact admin/EDLI-admin figures against the official EPFO circular before
  coding** (the official `ContributionRate.pdf` did not render via fetch — verify directly).
- **⚠ RE-VERIFY (watch-item):** a proposed **PF/EPS ceiling hike to ₹21,000/₹25,000** has been discussed but
  is **not notified** (verification-report §C3). Keep **₹15,000**; re-check at kickoff.
- Sources (as-of 2026-07-11): EPFO `ContributionRate.pdf`
  <https://www.epfindia.gov.in/site_docs/PDFs/MiscPDFs/ContributionRate.pdf>; ClearTax EPFO
  <https://cleartax.in/s/epfo>; ClearTax EDLI <https://cleartax.in/s/edli>; TaxGuru rates-of-contribution
  <https://taxguru.in/corporate-law/rates-contribution-epf-eps-edli-admin-charges-india.html>.

### 2.2 ESI (Employees' State Insurance) — as-of 2026-07-11

| Party | Rate | Base | Notes |
|---|---|---|---|
| **Employee** | **0.75%** | Gross wages | Rates unchanged since 01-Jul-2019 (down from 1.75%/4.75%). |
| **Employer** | **3.25%** | Gross wages | Combined **4.00%**. |

- **Wage ceiling ₹21,000/month** (**₹25,000** for employees with disability). Coverage: establishments with
  **≥10 employees** (20 in some states) with gross ≤ ceiling.
- **Contribution periods** (fixed): **Apr–Sep** and **Oct–Mar**; **benefit periods** **Jan–Jun** and
  **Jul–Dec**. **If an employee's wage crosses ₹21,000 mid-period, ESI continues to be deducted until the
  end of that contribution period** — a fidelity rule the engine must implement.
- **⚠ RE-VERIFY (watch-item):** a proposed **ceiling hike to ₹25,000/₹30,000** is circulating but **not
  notified**; keep ₹21,000; re-check at kickoff. Round contributions to the next rupee (ESIC convention).
- Sources: ESIC contribution <https://esic.gov.in/contribution>; ClearTax ESI rate
  <https://cleartax.in/s/esi-rate>.

### 2.3 Professional Tax (PT) — state-configurable, as-of 2026-07-11

PT is a **state subject** (Article 276) — **slabs vary by state** and the module must treat PT as a
**per-state, config-driven slab master** with a **constitutional annual cap of ₹2,500**. Seed a representative
set (DP-3):

| State | Representative slab (monthly) | Annual |
|---|---|---|
| **Maharashtra** (male) | ≤₹7,500 Nil · ₹7,501–₹10,000 ₹175 · >₹10,000 ₹200/mo (**₹300 in February**) | ₹2,500 |
| **Maharashtra** (female) | ≤₹25,000 Nil; then as above | ≤₹2,500 |
| **Karnataka** | ≤₹25,000 Nil · >₹25,000 **₹200/mo** | ₹2,400 |
| **West Bengal** | slabbed from low income; e.g. ₹10,001–₹15,000 ₹110 · ₹15,001–₹25,000 ₹130 · ₹25,001–₹40,000 ₹150 · >₹40,000 ₹200 | ≤₹2,500 |

- **⚠ RE-VERIFY:** exact WB/Karnataka band boundaries and the Maharashtra Feb-₹300 quirk against each state's
  current PT schedule; Karnataka raised its exemption threshold to ₹25,000. Slabs are **config-driven**;
  seed values are a starting point, not law.
- Sources: Saral PT slabs <https://saral.pro/blogs/professional-tax-slab-rates-in-different-states/>; TaxGuru
  state-wise PT <https://taxguru.in/corporate-law/state-wise-professional-tax-slab-rates-2024-2025.html>.

### 2.4 §192 Salary TDS — income-tax slabs FY 2025-26 (AY 2026-27) — as-of 2026-07-11

**New regime (default u/s 115BAC):**

| Slab (₹) | Rate |
|---|---|
| 0 – 4,00,000 | Nil |
| 4,00,001 – 8,00,000 | 5% |
| 8,00,001 – 12,00,000 | 10% |
| 12,00,001 – 16,00,000 | 15% |
| 16,00,001 – 20,00,000 | 20% |
| 20,00,001 – 24,00,000 | 25% |
| above 24,00,000 | 30% |

- **Standard deduction: ₹75,000** (new regime) / **₹50,000** (old regime).
- **§87A rebate (new regime): up to ₹60,000**, so **taxable income ≤ ₹12,00,000 ⇒ zero tax** (salaried
  break-even ≈ ₹12,75,000 gross after standard deduction). **Old regime §87A: ₹12,500**, taxable ≤ ₹5,00,000.
- **Marginal relief** applies just above ₹12,00,000 (new regime): additional tax capped at income over ₹12L.
- **Surcharge** (on tax): 10% (>₹50L), 15% (>₹1cr), 25% (>₹2cr) — **new-regime surcharge capped at 25%**
  (no 37% band); old-regime max 37%. **Health & Education Cess = 4%** on (tax + surcharge), both regimes.
- **Old regime** slabs unchanged (0–2.5L Nil / 2.5–5L 5% / 5–10L 20% / >10L 30%; senior-citizen variants),
  with Chapter VI-A deductions (80C, 80D, HRA exemption, etc.).
- **§192 mechanics:** employer estimates **total annual salary income**, applies the employee's elected
  regime, computes **estimated annual tax**, and deducts **1/12 (average rate)** each month, **truing-up**
  across the year as declarations/actuals change; **"On Projected Value"** vs "On Actual Value" basis
  (corpus pay-head field). Rounding to the **nearest rupee** (reuse the Phase-7 income-tax rounding). No
  threshold — TDS applies from the first rupee of taxable salary. **§206AA no-PAN:** higher of average rate
  or 20%.
- Sources: ClearTax income-tax-slabs <https://cleartax.in/s/income-tax-slabs>; ClearTax marginal-relief &
  surcharge <https://cleartax.in/s/marginal-relief-surcharge>.

### 2.5 Form 24Q (quarterly salary-TDS return) — as-of 2026-07-11

- **Form 24Q** reports TDS on salary u/s 192, filed **quarterly**. **Annexure I** (deductee-wise +
  challan-wise) is filed **all four quarters**; **Annexure II** (per-employee salary breakup + full tax
  computation) is filed **only in Q4** and drives **Form 16 Part B**.
- **Due dates:** Q1 31-Jul · Q2 31-Oct · Q3 31-Jan · **Q4 31-May**. **Deposit** by the **7th of the following
  month** (March TDS by 30-Apr). Certificates (Form 16) by **15-Jun**.
- Emit the **FVU-compatible NSDL flat-file** (fixed-width/caret-delimited `.txt`) + **emulate control-total
  checks**, offline, reusing the Phase-7 `FvuWriter` (DP-8). **⚠ RE-VERIFY** the FVU/RPU format version at
  build (Protean/NSDL versions the layout).
- Sources: ClearTax Form 24Q <https://cleartax.in/s/tds-return-salary-payment>; Form 24Q (see s.192, rule
  31A) <https://taxindiaonline.com/RC2/pdfdocs/forms/itforms/62Form24Q.pdf>.

### 2.6 Gratuity & Bonus — as-of 2026-07-11

- **Payment of Gratuity Act, 1972:** **Gratuity = (Last-drawn Basic+DA × 15 × completed years) / 26**;
  **tax-free/statutory ceiling ₹20,00,000**; eligibility ≥ 5 years continuous service. Only **Basic + DA**
  enter the base. Payroll computes a **gratuity provision** per eligible employee (DP-4).
  Source: ClearTax gratuity <https://cleartax.in/s/gratuity-calculator>.
- **Payment of Bonus Act, 1965:** **min 8.33% / max 20%** of (Basic+DA); **eligibility wage ≤ ₹21,000/month**;
  **calculation ceiling ₹7,000/month or the state minimum wage, whichever higher**. Statutory Bonus is a
  **light** deliverable (a Bonus pay head + register), not a full allocable-surplus computation (DP-4).
  Source: Payment of Bonus Act (Chief Labour Commissioner) <https://clc.gov.in/clc/acts-rules/payment-bonus-act>.

### 2.7 Build-time re-verify checklist (A14, at kickoff)

1. Target FY confirmed (DP-1) → pick slab table + statutory constants.
2. PF admin (A/c 2) and EDLI-admin (A/c 22) **exact** figures + minimums (source drift — §2.1).
3. PF/EPS wage ceiling still ₹15,000 (hike not notified?); EPS max ₹1,250; EDLI cap ₹75.
4. ESI ceiling still ₹21,000/₹25,000-disabled; contribution/benefit-period dates; rounding convention.
5. PT slab schedules for each seeded state (band boundaries, female exemption, Feb quirk).
6. §192 new + old slabs, standard deduction (75k/50k), 87A (60k/12.5k), surcharge caps, cess 4%, marginal relief.
7. §192 average-rate / projected-value mechanics + nearest-rupee rounding (reuse Phase-7 rule).
8. Form 24Q Annexure I/II layout + FVU/RPU version to pin the writer.
9. Gratuity ceiling (₹20L) + Bonus wage/calc ceilings (₹21,000 / ₹7,000).
10. New Labour Codes still not enforced (wages-≥-50%-of-CTC redefinition would change every PF/ESI/gratuity base).

---

## 3. Engine Mapping + New Masters/Entities (ER rules)

Payroll **mirrors the shipped statutory patterns**: config-driven, **framework/DB/clock/RNG-free** pure
services over the `Company` aggregate, producing **additive `EntryLine`s** carrying a self-describing detail
(mirror of `GstLineTax` / `TdsLineTax`) that reports read back (never recompute). Salary computation is a
**pure projection** over masters + posted attendance; statutory figures are **compute-once-then-derive** to
conserve to the paisa/rupee.

### 3.1 Services

- **`PayrollComputationService`** (new) — mirrors `GstService`/`TdsService` shape. Given an employee, a
  period, the **dated salary structure**, and posted **attendance/production**, it resolves each pay head in
  dependency order (a pay head's formula may reference other heads, e.g. `PF = 12% of (Basic + DA)`),
  applies calculation type + slabs + rounding, and returns the ordered **earning / deduction / employer-
  contribution / payable** `EntryLine`s for the **Payroll voucher**. **Balance invariant:** Σ earnings (Dr
  expense) = Σ deductions (Cr statutory/payable) + net (Cr Salary Payable), employer contributions are a
  separate balanced pair (Dr expense / Cr employer-payable). Dependency resolution must be **deterministic
  and cycle-guarded** (fail-fast on a formula cycle).
- **`PayrollStatutoryService`** (new, or split modules) — PF (EPS/EPF split, EDLI, admin), ESI
  (ceiling + period), PT (state slab), each a pure function of the computed salary + config. **Computed
  EPS/EPF, never hardcoded 3.67%** (verification-report #26): `EPS = 8.33% × min(PF-wage, 15000)`
  (cap ₹1,250); `EmployerEPF = 12% × PF-wage − EPS`.
- **§192 via the Phase-7 `TdsService` (extended)** — a new **Nature of Payment "192 – Salary"** with an
  **average-rate** computation mode: estimate annual tax on projected salary (regime engine), divide by
  remaining months, deduct as an **Income-Tax deduction pay head** carving out of net pay into **TDS Payable
  (192)** — the **same TDS-Payable ledger + challan + `FvuWriter`** machinery from Phase 7. **§192 does not
  fork the TDS engine; it adds a computation mode + Form 24Q projection.**

### 3.2 New masters / entities (mirror existing types)

| New master | Mirrors | Key fields |
|---|---|---|
| **Employee Category** | `CostCategory` | name, allocate revenue/non-revenue; default *Primary Cost Category*. |
| **Employee Group** | `Group`/`CostCentre` | name, parent, "define salary?"; hierarchical. |
| **Employee** | `Ledger` + `PartyGstDetails` | code, name, group, join date, designation, function, location, gender, DOB, **PAN (validated, reuse `Pan.Validate`) / Aadhaar / UAN / PF a/c / EPS a/c / PRAN / ESI no.**, bank details, **applicable tax regime (new/old)**, contract/passport/visa; a distinct master (not a normal ledger) but cost-allocable. |
| **Payroll Unit** | `Unit` (simple+compound) | symbol, formal name, type, decimals; compound = first × factor + tail (e.g. "Hrs of 60 Min"). |
| **Attendance/Production Type** | `Unit`/`StockCategory` | name, parent, type (Attendance/Leave-with-Pay · Leave-without-Pay · Production · User-defined), period type (Days/Hrs), production unit. |
| **Pay Head** | `Ledger` + `GstRateSlab` | name, **pay head type**, **under (group)**, statutory pay type (PF A/c1 / EPS A/c10 / ESI / PT / IT …), affect-net-salary, display name, **calculation type**, **calc period**, rounding, **computation formula** (basis heads + slabs: percentage/value, amount up-to/greater-than), **income-tax component**, **use-for-gratuity**. |
| **Salary Structure** | `PriceLevel`/`PriceList` (dated tiers) | scope (group or employee id), **effective-from**, start-type (copy-parent/copy-employee/afresh), ordered **structure lines** (pay head id + override rate/formula). |
| **Payroll statutory config (Company)** | `GstConfig`/`TdsConfig` | payroll_enabled, payroll_statutory_enabled, PF codes, ESI codes + branch + **standard working days (26)**, NPS, IT (TAN/circle/deductor/responsible person — **reuse Phase-7 deductor config**). `EnsureValid()` fail-fast. |
| **PF/ESI/PT/Gratuity constants** | `GstRateSlab`/`SeedGstRates` | seeded, dated, config-driven statutory rate/ceiling tables (§2), year-aware. |
| **Payroll ledgers** | `GstService.EnsureTaxLedger` | auto-create **Salary Payable, PF Payable, ESI Payable, PT Payable, NPS Payable** (Current Liabilities / Duties & Taxes) + statutory expense heads on enable, classification-tagged so reports map ledger→head without name parsing. |

### 3.3 New voucher types

- **Attendance** and **Payroll** are **two genuinely new voucher base types** (they are among Tally's
  additional voucher types, active only when F11 Payroll is on — catalog §4.1 / verification-report #15).
  Unlike Phase-7 Stat-Payment (which reused the Payment base type via a flag), these **cannot** be modelled
  as flags — they carry employee/attendance allocations rather than balanced Dr/Cr accounting lines (the
  Attendance voucher is non-accounting). **DP-10 / regression-risk:** adding base types ripples through every
  exhaustive `VoucherBaseType` switch (e.g. `GstReportSupport.DirectionOf`, validators, day-book
  projections) — audit and handle/exclude payroll vouchers in each.
- **Payment** (F5) is reused for Salary/PF/ESI/PT disbursement (Payroll-Autofill mode) — no new base type.

### 3.4 Data-model additions (SQLite, additive from v29)

New tables (company_id FK + `ix_*_company`, all additive, empty when payroll off → ER-13): `employee_categories`,
`employee_groups`, `employees`, `payroll_units`, `attendance_types`, `pay_heads`, `pay_head_computation`
(slab rows), `salary_structures`, `salary_structure_lines`, `payroll_lines` (child of `entry_lines`:
employee_id, pay_head_id, computed_amount_micro, basis detail), `attendance_entries` (employee_id,
attendance_type_id, value, period), plus statutory constant tables (`pf_config`, `esi_config`,
`pt_slabs`, `gratuity_config`) or a generic dated `payroll_statutory_rates`. ALTER `companies` ADD payroll +
statutory-code columns (NULL/DEFAULT 0). ALTER `voucher_types` — activate Attendance/Payroll types. `§192`
reuses the Phase-7 `tds_lines`/`tds_challans` tables (add section `192`).

---

## 4. Numbered Requirements

### Masters & configuration
- **RQ-1** — F11 `Maintain Payroll` + `Enable Payroll Statutory` toggles; enabling opens the Company Payroll
  Statutory Details screen (PF/ESI/NPS/IT codes, standard working days = 26). Idempotent (mirror
  `GstService.EnableGst`). Disabled by default ⇒ ER-13 byte-identical.
- **RQ-2** — Create/Alter **Employee Category** (revenue/non-revenue), **Employee Group** (hierarchical,
  "define salary?"), **Employee** (all identity/statutory/bank fields; PAN + UAN + ESI validated; applicable
  tax regime). Rename-in-place semantics (stable id).
- **RQ-3** — Create/Alter **Payroll Units** (simple + compound) and **Attendance/Production Types** (4 types,
  period Days/Hrs).
- **RQ-4** — Create/Alter **Pay Heads** across all pay-head types with all **five calculation types**
  (On Attendance / Flat Rate / As Computed Value / On Production / As User-Defined Value), computation
  formulas (basis heads + percentage/value slabs + amount up-to/greater-than), rounding methods, income-tax
  component tag, and use-for-gratuity flag.
- **RQ-5** — Define **Salary Structure** at Employee Group and Employee level, dated (effective-from), with
  Copy-From-Parent / Copy-From-Employee / Start-Afresh.

### Process & computation
- **RQ-6** — **Attendance voucher** (F10 Attendance) records per-employee attendance/production values;
  **Ctrl+F Attendance Autofill** pre-fills a period/group.
- **RQ-7** — **Payroll voucher** (Ctrl+F4) with **Payroll Autofill** computes each employee's salary from the
  dated structure + attendance and **posts integrated accounting entries** (earnings→expense;
  deductions→statutory/payable; net→Salary Payable). Separate autofill runs for **PF Contribution**, **ESI
  Contribution**, **NPS**, and **User-Defined** (Bonus). Balance invariant holds by construction.
- **RQ-8** — **Payment voucher** (F5) with Payroll Autofill for **Salary Payment / PF Challan / ESI Challan /
  PT Payment**, with employee-wise **Bank Allocation** (per-employee instrument no.); reduces the payable
  ledger to zero.

### Statutory
- **RQ-9 (PF)** — Employee PF 12% (cap ₹1,800 at ceiling / higher-wage option); **computed** Employer EPS
  (8.33% × min(wage,15000), cap ₹1,250) + Employer EPF (12% × wage − EPS); EDLI 0.5% (cap ₹75); admin 0.5%
  (min ₹500/estab). **ECR** text export. **Never hardcode 3.67%.**
- **RQ-10 (ESI)** — Employee 0.75% + Employer 3.25% on gross ≤ ₹21,000 (₹25,000 disabled); **contribution-
  period continuation** rule (crossing ceiling mid-period keeps deducting to period end); monthly ESI return.
- **RQ-11 (PT)** — State-configurable slab master (seed Maharashtra/Karnataka/WB + "None"); monthly deduction
  by slab with the annual **₹2,500 cap**; PT payment + return.
- **RQ-12 (§192)** — Estimate annual tax on projected salary under the employee's **elected regime**
  (new default), apply standard deduction / 87A / surcharge / cess / marginal relief; deduct **average-rate
  monthly TDS** via an Income-Tax pay head into **TDS Payable (192)**; deposit via Stat Payment (reuse Phase
  7); **true-up** across the year.
- **RQ-13 (Form 24Q)** — **Annexure I** (all quarters, deductee + challan) + **Annexure II** (Q4 salary
  detail + tax computation) as pure projections; **FVU** flat-file export + control-total emulation;
  **Form 16** (Part A challan summary + **Part B** salary/tax detail from Annexure II).
- **RQ-14 (Gratuity)** — Gratuity provision per eligible employee = (Basic+DA × 15 × years)/26, cap ₹20L;
  gratuity report. **RQ-15 (Bonus)** — statutory Bonus pay head (8.33%–20%, ceiling ₹7,000) + register.

### Reports & I/O
- **RQ-16** — **Payslip** (per employee, PDF, amount-in-words), **Pay Sheet**, **Payroll Statement/Register**,
  **Attendance Sheet**, **Payment Advice**, **Employee Profile / Head Count**, **Expat** reports; standard
  report actions (Alt+F2 period, drill-down, export, print, F12 config).
- **RQ-17** — **Statutory reports:** PF (Form 5/10/12A/3A/6A/ECR), ESI (Form 3/5/6/monthly), PT, NPS,
  Gratuity, Income-Tax computation — reconciling to the payable-ledger postings.
- **RQ-18 (Io)** — every new master + voucher extension round-trips **lossless in JSON + XML** (paisa +
  count exact), folded into CanonicalModel/CanonicalMapper **in the introducing slice**.

### Engine/entity invariants (ER)
- **ER-1** Payroll voucher balances (earnings Dr = deductions Cr + net Cr; employer contributions a separate
  balanced pair). **ER-2** Statutory figures **compute-once-then-derive** (rupee/paisa conserving). **ER-3**
  Pay-head formula resolution is deterministic + cycle-guarded. **ER-4** Salary structure is **dated**;
  computation uses the structure effective on the voucher date. **ER-5** All statutory constants are
  config-driven + **year-aware** (a date-effective table). **ER-6** Employee master carries a stable
  surrogate id (rename-in-place). **ER-7** ESI period-continuation is computed from posted history (pure
  projection), not a stateful flag. **ER-8** §192 reuses Phase-7 TDS-Payable/challan/FvuWriter — no parallel
  withholding path. **ER-9** New columns DEFAULT 0/NULL, new tables empty ⇒ **ER-13 byte-identical when
  payroll off**. **ER-10** No `Tally`/brand strings in payroll UI, code, or exports (de-brand scan).

---

## 5. Ordered Slice Plan (schema v29 → v3x)

Sequential build (Phase 6/7 constraint): every schema slice edits `Schema.cs` + `SqliteCompanyStore.cs` +
`Company.cs` on **one migration chain** → slices **cannot** run in parallel. Each migration is one additive
`MigrateV(N-1)ToVN` in a txn that bumps `schema_version`; a fresh DB stamps straight to head via `CreateV1`;
each paired with a legacy-DDL upgrade test (mirror `JobWorkSchemaTests`/`TdsSchemaTests`) proving the upgrade
keeps existing rows untouched + ER-13 byte-identical when payroll off. A12 commits each slice in small
conventional units; A10 adversarial pass before each PR gate.

> **Schema progression: v29 → v30 → v31 → v32 → v33 → v34 → v35.** (S7/S8/S9/S10 add no schema — pure
> projections/reports; the numbering leaves the persistence slices contiguous.)

### P8-S1 — Payroll masters + F11 config + statutory config → **v29→v30**
- **Scope:** F11 Enable Payroll + Payroll Statutory + Company statutory codes (PF/ESI/NPS/IT, std working
  days = 26, reuse Phase-7 deductor config); Employee Category / Group / Employee masters (PAN/UAN/ESI
  validators); Payroll Units (simple+compound); Attendance/Production Types; auto-create payroll ledgers on
  enable. **No computation yet.** Fold masters into `Apex.Ledger.Io` now.
- **Schema v30:** ALTER `companies` (payroll flags + statutory codes); new tables `employee_categories`,
  `employee_groups`, `employees`, `payroll_units`, `attendance_types` + indices.
- **PR-1 gate:** Enable idempotent; masters CRUD + validators; legacy-DDL v29→v30; ER-13 byte-identical off;
  de-brand clean; Io round-trip of new masters.
- **Key RQs:** RQ-1, RQ-2, RQ-3. **Files to mirror:** `GstConfig.cs`, `CostCategory.cs`/`CostCentre.cs`,
  `Unit.cs`, `Pan.cs`, `GstService.cs` (Enable/EnsureTaxLedger), `Company.cs`, `Schema.cs`,
  `SqliteCompanyStore.cs`, `CanonicalModel.cs`/`CanonicalMapper.cs`, `TdsSchemaTests.cs`.

### P8-S2 — Pay Heads + Salary Structures → **v30→v31**
- **Scope:** Pay Head master (all pay-head types + all 5 calculation types + slab computation + income-tax
  component tag + gratuity flag); Salary Structure (group + employee, dated, copy/afresh). Fold into Io now.
- **Schema v31:** `pay_heads`, `pay_head_computation`, `salary_structures`, `salary_structure_lines`.
- **PR-2 gate:** pay-head formulas persist + resolve; dated structure select-by-date; legacy-DDL; ER-13; Io.
- **Key RQs:** RQ-4, RQ-5. **Files to mirror:** `GstRateSlab.cs`, `SeedGstRates.cs`, `PriceLevel.cs`,
  `Ledger.cs`, `Schema.cs`.

### P8-S3 — Attendance + Payroll voucher (salary computation engine) → **v31→v32**
- **Scope:** Attendance voucher (F10 + Ctrl+F autofill); **Payroll voucher** (Ctrl+F4) `PayrollComputation
  Service` — resolve pay heads per employee against attendance + dated structure; **integrated accounting
  post** (earnings/deductions/net); teach `VoucherValidator` the payroll balance shape; add the **Attendance
  + Payroll voucher base types** and audit every exhaustive base-type switch (DP-10). **This is the core
  engine slice.**
- **Schema v32:** `attendance_entries`, `payroll_lines` (child of `entry_lines`); activate Attendance/Payroll
  voucher types.
- **PR-3 gate:** worked payslip balances to the paisa; formula dependency order deterministic + cycle-guarded;
  **Robert + Bright regression green**; base-type switches handle payroll vouchers.
- **Key RQs:** RQ-6, RQ-7 (salary part). **Files to mirror:** `GstService.cs`, `TdsService.cs`,
  `VoucherValidator.cs`, `EntryLine.cs`, `GstReportSupport.cs` (DirectionOf switch).

### P8-S4 — PF (EPS/EPF/EDLI/admin) + PF autofill + ECR → **v32→v33**
- **Scope:** PF Contribution autofill; **computed EPS/EPF split** + EDLI + admin (§2.1); PF Payable ledgers;
  PF Challan payment (F5 autofill) + **ECR** text export (reuse `TabularExport`/`CsvWriter`/`FvuWriter`
  deterministic writers). Config-driven, year-aware constants seeded.
- **Schema v33:** `pf_config`/`payroll_statutory_rates` (dated) + optional `pf_challans` (or reuse challan
  machinery).
- **PR-4 gate:** EPS = 8.33%×min(wage,15000) cap ₹1,250; EmployerEPF = 12%×wage − EPS; **not 3.67%**; ECR
  byte-stable + de-branded; legacy-DDL; ER-13.
- **Key RQs:** RQ-9. **Files to mirror:** `TdsService.cs`, `GstReportSupport.cs`, `TabularExport.cs`.

### P8-S5 — ESI + ESI autofill + monthly return → **v33→v34**
- **Scope:** ESI Contribution autofill (0.75% / 3.25%, ₹21,000 ceiling); **contribution-period continuation**
  rule (pure projection over posted history); ESI Payable; ESI Challan payment + monthly return export.
- **Schema v34:** `esi_config` (dated) if not in the generic rate table.
- **PR-5 gate:** ESI ceiling on/below/crossing; mid-period continuation deterministic; return reconciles;
  legacy-DDL; ER-13.
- **Key RQs:** RQ-10. **Files to mirror:** S4 file set, `Gstr1.cs` (period accumulation pattern).

### P8-S6 — Professional Tax (state-configurable) + PT payment → **v34→v35**
- **Scope:** state-configurable PT slab master (seed Maharashtra/Karnataka/WB + None); monthly deduction by
  slab with annual ₹2,500 cap (Feb-quirk for Maharashtra); PT payment + return.
- **Schema v35:** `pt_slabs` (state, band, amount, month-override).
- **PR-6 gate:** each seeded state computes its slab; annual cap enforced; Maharashtra Feb-₹300; legacy-DDL; ER-13.
- **Key RQs:** RQ-11. **Files to mirror:** `GstRateSlab.cs`, `SeedGstRates.cs`.

### P8-S7 — §192 Salary TDS + Form 24Q (Annexure I + II) + FVU → **no schema** (reuse Phase-7 tables)
- **Scope:** Nature of Payment "192 – Salary" with **average-rate** computation mode on the **Phase-7
  `TdsService`**; regime engine (new default + old option) + standard deduction + 87A + surcharge + cess +
  marginal relief; Income-Tax deduction pay head → **TDS Payable (192)**; deposit via Stat Payment (reuse S3
  Phase-7); **Form 24Q** Annexure I (all Q) + Annexure II (Q4) projection + **FVU** flat-file (reuse
  `FvuWriter`) + control-total emulation; **Form 16 Part B**. Add section `192` to `tds_lines`; **fold any
  new §192 detail into Io.**
- **PR-7 gate:** average-rate monthly TDS true-up; new + old regime; no-PAN 20%; 24Q reconciles to TDS-Payable
  (192) postings; FVU byte-stable + de-branded; Form 16 figures match Annexure II; Io lossless.
- **Key RQs:** RQ-12, RQ-13. **Files to mirror:** `TdsService.cs`, `FvuWriter.cs`, `Gstr1.cs`,
  `GstReportSupport.cs`, `IndianAmountInWords.cs`, Phase-7 `tds_lines`/`tds_challans`.

### P8-S8 — Payslips + payroll registers/reports → **no schema**
- **Scope:** Payslip (PDF, amount-in-words), Pay Sheet, Payroll Statement/Register, Attendance Sheet, Payment
  Advice, Employee Profile/Head Count, Expat — all **pure projections** via `ReportPdf`/`InvoicePdf` +
  `TabularExport`.
- **PR-8 gate:** payslip figures reconcile to the payroll voucher; PDFs deterministic + de-branded; PDF golden test.
- **Key RQs:** RQ-16. **Files to mirror:** `ReportPdf.cs`, `InvoicePdf.cs`, `TabularExport.cs`,
  `IndianAmountInWords.cs`.

### P8-S9 — Statutory reports + Gratuity/Bonus → **no schema** (or tiny config)
- **Scope:** PF (Form 5/10/12A/3A/6A/ECR views), ESI (Form 3/5/6/monthly), PT return, NPS, **Gratuity
  provision** report ((Basic+DA×15×years)/26 cap ₹20L), **Income-Tax computation** report; **statutory Bonus**
  pay head + register (8.33%–20%, ceiling ₹7,000).
- **PR-9 gate:** statutory reports reconcile to payable postings; gratuity/bonus figures match §2.6; de-brand clean.
- **Key RQs:** RQ-14, RQ-15, RQ-17. **Files to mirror:** existing statutory-report projections,
  `GstReportSupport.cs`.

### P8-S10 — Exit gate → **no schema** (see §6)

---

## 6. Exit Gate (P8-S10) — R9 + PR gates + golden worked example

Mirror the Phase-6/7 exit gate. **Does not pass until all of:**

1. **All prior PR gates (PR-1…PR-9) re-green** with evidence.
2. **Full test suite green** (Io / Ledger / Sqlite / Desktop) with counts shown; **Robert + Bright** stay
   green; the new **payroll fixture** reconciled across all engines.
3. **Migration chain v29→v35 proven** — each `MigrateV(N-1)ToVN` + its legacy-DDL upgrade test; a fresh DB
   stamps to head via `CreateV1`.
4. **ER-13 byte-identical when payroll off** — a company that never enables Payroll serializes identically to
   a v29 company (every new column DEFAULT 0/NULL, every new table empty).
5. **Io losslessness re-verified** — JSON + XML round-trip **paisa + count exact** for **ALL** Phase-8 masters
   + voucher extensions (employees, pay heads, structures, attendance, payroll_lines, statutory config);
   explicit "no silent drop" check.
6. **De-brand scan** clean (no "Tally" in app UI / code / exports).
7. **A10 adversarial sign-off** — payslip paisa/rupee conservation, EPS/EPF split correctness, ESI
   period-continuation determinism, §192 average-rate true-up, base-type-switch exhaustiveness.
8. **REAL `Apex.Desktop` app launched** (kill any running exe first), de-branded, payroll screens exercised.
9. **A12 commits & pushes**; PR CI green-capable across all 3 OS (audit Windows-only-passing tests — the
   Phase-6 path-separator lesson).

### Golden worked example (the R9 deliverable, shown numerically)

**Employee "Rajkumar Sharma" (Marketing), FY 2025-26, monthly, new regime, with PAN, 10 yrs service:**

Earnings — Basic ₹80,000 · DA ₹32,000 (40% of Basic) · HRA ₹22,400 (20% of Basic+DA) · TA ₹8,000 (10% of
Basic) → **Gross ₹1,42,400**.

Deductions:
- **Employee PF 12% of min(Basic+DA=₹1,12,000, ₹15,000) = ₹1,800.**
- **Professional Tax (Maharashtra) = ₹200** (Basic+DA > ₹10,000).
- **§192 TDS:** projected annual gross ₹17,08,800 − standard deduction ₹75,000 = **taxable ₹16,33,800**;
  new-regime tax = 5%·4L + 10%·4L + 15%·4L + 20%·₹33,800 = 20,000 + 40,000 + 60,000 + 6,760 = **₹1,26,760**;
  + 4% cess ₹5,070 = **₹1,31,830** annual → **monthly TDS ≈ ₹10,986** (nearest rupee). No surcharge (< ₹50L),
  no §87A rebate (taxable > ₹12L).
- **ESI = N/A** (gross ₹1,42,400 > ₹21,000 ceiling).

**Net pay = 1,42,400 − 1,800 − 200 − 10,986 = ₹1,29,414.** *(Assert Σ deductions + net = gross to the paisa.)*

Employer cost (separate balanced pair, not in net pay):
- **EPS = 8.33% × ₹15,000 = ₹1,250** (cap) · **Employer EPF = 12%×15,000 − 1,250 = ₹550** · **EDLI = 0.5%×15,000
  = ₹75** · **PF admin = 0.5%×15,000 = ₹75** (₹500 estab-min applies at aggregate). *(Assert Employer EPF +
  EPS = ₹1,800 = employee-share — the computed-split invariant, NOT a hardcoded 3.67%.)*

**Payroll voucher (integrated):** `Dr Basic 80,000 / Dr DA 32,000 / Dr HRA 22,400 / Dr TA 8,000` ·
`Cr Employee PF 1,800 / Cr PT Payable 200 / Cr TDS Payable(192) 10,986 / Cr Salary Payable 1,29,414`;
employer run: `Dr PF Exp (EPS 1,250 + EPF 550 + EDLI 75 + admin 75) 1,950 / Cr PF Payable 1,950`.

**ESI sub-example** (separate low-wage employee, gross ₹18,000 ≤ ₹21,000): Employee ESI 0.75% = **₹135**,
Employer ESI 3.25% = **₹585**; period-continuation rule illustrated if the wage later crosses ₹21,000.

**Statutory outputs:** **Form 24Q Annexure I** deductee row (PAN, section 192, TDS ₹10,986/mo → quarter
₹32,958, challan-mapped) reconciles to TDS-Payable(192); **Annexure II (Q4)** = full salary breakup + tax
computation → **Form 16 Part B**; **FVU** file byte-stable; **ECR** lists PF ₹1,800/EPS ₹1,250/EPF ₹550.
**Gratuity provision** = (₹1,12,000 × 15 × 10)/26 = **₹6,46,154** (< ₹20L cap).

> **Realism note (fidelity):** ESI (ceiling ₹21,000/mo ≈ ₹2.52L/yr) and §192 TDS (nil below ~₹12.75L/yr new
> regime) **almost never coexist for the same employee** — hence the two sub-examples. The engine must handle
> both, but a golden test asserting "all four on one employee" would be unrealistic.

---

## 7. Dependencies + Regression Risks

### Dependencies
- **Ordered/sequential build:** one migration chain → no parallel slice builds. **Order:** S1 (masters) →
  S2 (pay heads/structure) → S3 (engine) → S4 (PF) → S5 (ESI) → S6 (PT) → S7 (§192/24Q) → S8 (payslips) →
  S9 (statutory/gratuity) → S10 (gate).
- **Reuse:** Phase-4 `Duties & Taxes` ledger machinery, Phase-5 `Apex.Ledger.Io`, **Phase-7 `TdsService` +
  TDS-Payable + challan + `FvuWriter`** (§192/24Q extend these). Confirm the Phase-7 branch is merged/available.
- **Cross-cutting:** Payroll adds cost-allocation over Employee Category (Phase-2 cost-centre machinery) —
  reuse, don't fork.
- **External:** **A14 leads** (Study-Guide Ch.11 is the fidelity source) and web-verifies all law (§2.7
  checklist) at kickoff and per slice. A10 adversarial pass per slice. A12 all git (R4).

### Regression risks (ranked)
1. **New voucher base types (HIGHEST).** Attendance + Payroll are genuinely new base types → they ripple
   through every exhaustive `VoucherBaseType` switch (`GstReportSupport.DirectionOf`, validators, day-book,
   TDS/TCS direction). A missed switch arm = a silent mis-projection. Audit all switches in S3; a green suite
   can hide it (the Phase-6/7 lesson).
2. **Payslip paisa/rupee conservation.** Multi-head computation with percentage slabs + rounding must
   conserve exactly (Σ deductions + net = gross). **Compute-once-then-derive**; watch the percent-carve-out
   leak class (Phase-6 bug).
3. **Computed EPS/EPF split.** Must be `EPS = 8.33%×min(wage,15000)` cap ₹1,250 and `EmployerEPF = 12%×wage −
   EPS` — **never hardcode 3.67%** (verification-report #26). Assert Employer-EPF + EPS = employee-share.
4. **§192 average-rate true-up.** Estimated-annual-tax spread monthly with mid-year declaration changes is
   stateful across months; model as a **pure projection** over posted payroll + declarations, deterministic,
   nearest-rupee. Regime toggle (new/old) must not leak.
5. **ESI period-continuation determinism.** Crossing ₹21,000 mid-period keeps deducting to period end — a
   history-dependent rule; compute from posted vouchers, not a mutable flag.
6. **Io silent-drop.** Every new master + voucher extension folded into CanonicalModel/CanonicalMapper **in
   its slice** — never deferred (Phase-6 defect class).
7. **ER-13 byte-identical when off.** New columns DEFAULT 0/NULL, new tables empty; legacy-DDL test per slice.
8. **PT state slabs.** State-config with per-state quirks (Maharashtra Feb-₹300, female exemption, WB bands) —
   keep config-driven + seeded, not code-branched per state.
9. **Migration-chain fragility.** v29 is head; one additive `MigrateV(N-1)ToVN` per slice, txn-wrapped, fresh
   DB via `CreateV1`, each proven by a legacy-DDL upgrade test.
10. **Windows-only-passing tests** (Phase-6 lesson): audit path-separator/OS assertions before the PR CI gate.

---

## 8. Decision Points for the User (approve before execution)

- **DP-1 — Target FY.** *Recommended:* **FY 2025-26 (AY 2026-27)**, consistent with Phase 7 (rates in §2). If
  FY 2026-27 is wanted, re-verify slabs + any new-Labour-Code base redefinition. **Confirm target FY.**
- **DP-2 — §192 default regime.** *Recommended:* **New regime = default** (statutory default u/s 115BAC),
  with a **per-employee "Applicable Tax Regime" toggle** for the old regime (corpus field). **Approve
  new-default + old-optional, or make old the default.**
- **DP-3 — PT states to seed.** *Recommended:* seed **Maharashtra + Karnataka + West Bengal + "None/Not
  applicable"**, PT fully **state-configurable** (add any state via the slab master). **Approve set, or
  name specific states you operate in.**
- **DP-4 — Gratuity & Bonus scope.** *Recommended:* **Gratuity** = statutory provision computation + report
  (15/26, ₹20L cap) — **in**; **Bonus** = statutory pay head + register (8.33%–20%, ₹7,000 ceiling) — **in,
  light**; **actuarial gratuity valuation (AS-15/Ind-AS-19) — out**. **Approve, or pull actuarial/allocable-
  surplus in (significant scope).**
- **DP-5 — Payroll → accounting posting.** *Recommended:* **Integrated (auto-post to ledgers)** — earnings→
  expense, deductions→statutory/payable, net→Salary Payable — matching Tally and the GST/TDS additive-
  EntryLine pattern. **Approve, or a "compute-only, no auto-post" mode (not Tally-faithful).**
- **DP-6 — PF admin / EDLI-admin exact figures.** *Recommended:* use the **current EPFO** values — EPF admin
  A/c 2 = **0.50% (min ₹500/estab)**, EDLI A/c 21 = **0.50% (cap ₹75/emp)**, EDLI-admin A/c 22 = **NIL** —
  all **config-driven, year-aware**; A14 re-confirms against the official circular (a secondary source shows
  stale 0.65%/0.01% — §2.1). **Approve current values + config-driven.**
- **DP-7 — PF/ESI ceiling & higher-wage edge cases.** *Recommended:* cap PF at **₹15,000** (EPS max ₹1,250,
  EDLI cap ₹75) and ESI at **₹21,000/₹25,000-disabled** (both un-hiked as of verification), support an
  **optional "contribute on higher wages / VPF"** flag, **pro-rate for partial-month joiners/leavers**;
  **international-worker no-ceiling — out**. **Approve.**
- **DP-8 — Form 24Q depth.** *Recommended:* same posture as Phase 7 — emit the **FVU-compatible NSDL
  flat-file** (Annexure I all-Q + Annexure II Q4) + **control-total emulation**, offline, pinned to one FVU
  version; **no online TRACES upload, no bundling govt FVU/RPU JARs**; generate **Form 16 (Part A + B)**.
  **Confirm depth.**
- **DP-9 — Integration with the Phase-7 TDS engine.** *Recommended:* **§192 reuses/extends `TdsService` +
  TDS-Payable + challan + `FvuWriter`** (a new "192 – Salary" Nature-of-Payment with an average-rate mode) —
  **do not fork** a parallel salary-withholding path. **Approve reuse.**
- **DP-10 — New voucher base types.** *Recommended:* add **Attendance + Payroll** as two new
  `VoucherBaseType`s (unavoidable — they are not accounting-line flags like Stat-Payment) and **audit every
  exhaustive base-type switch** so payroll vouchers are handled/excluded correctly. **Approve (informational
  — highest regression risk).**
- **DP-11 — Income-Tax declaration/perquisite depth & NPS.** *Recommended:* build the **regime engine +
  standard deduction + 87A + surcharge/cess + declared-deductions capture (old regime)**; **NPS** = seed
  masters + employer contribution pay head (light); **full proof-management / perquisite-valuation / HRA-
  from-rent auto-exemption — out** (declared values only). **Approve the boundary, or pull deeper IT depth
  in.**

---

## 9. Appendix — key reference files to mirror (relative to repo root)

Services/domain: `src/Apex.Ledger/Services/{GstService,TdsService,TcsService,VoucherValidator}.cs`;
`src/Apex.Ledger/Domain/{GstConfig,TdsConfig,GstRateSlab,GstLineTax,TdsLineTax,PartyGstDetails,
StockItemGstDetails,EntryLine,Ledger,Company,Pan,Tan}.cs`; cost model
`src/Apex.Ledger/Domain/{CostCategory,CostCentre}.cs`, `src/Apex.Ledger/Domain/Unit.cs`,
`src/Apex.Ledger/Domain/PriceLevel.cs`.
Reports: `src/Apex.Ledger/Reports/{ClassificationRules,Gstr1,GstReportSupport}.cs`;
seed `src/Apex.Ledger/Seed/SeedGstRates.cs`.
Persistence: `src/Apex.Persistence.Sqlite/{Schema,SqliteCompanyStore}.cs`.
Io: `src/Apex.Ledger.Io/{CanonicalModel,CanonicalMapper,ReportPdf,InvoicePdf,TabularExport,CsvWriter,
FvuWriter,IndianAmountInWords}.cs`.
Tests: `tests/Apex.Persistence.Sqlite.Tests/{JobWorkSchemaTests,GstRoundTripTests}.cs` (+ the Phase-7
TDS schema/round-trip tests).
Corpus: `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf` Ch.11 (pp.187–222); `tally/664311548-Tally-Prime-Book.pdf`
§11; catalog `docs/tally-feature-catalog.md` §14 + verification-report #15, #26.

---

*Change log: Phase 8 (Payroll) requirements + slice plan drafted 2026-07-11 via `/software`, grounded in
`docs/tally-feature-catalog.md` §14 + Study-Guide Ch.11, with FY 2025-26 statutory law web-verified per R7
(sources + as-of dates in §2). PLAN-ONLY — no code executed. Awaiting user approval of DP-1…DP-11 before
build. Any deviation during execution is recorded in `memory.md` with its reason (R6).*
