# Software Requirements Specification (SRS) — Skeleton

> **Purpose of this file.** This is the *skeleton* SRS for the Apex Solutions Tally Prime clone, created in
> Phase 0 per the `/software` lifecycle (requirements → design → implementation → …). It defines the
> structure and the project-level requirements known today. Each subsequent phase **fills its own slice** of
> the functional-requirements section (§4) from `docs/tally-feature-catalog.md` (+ verification report) and
> the `tally/` PDFs, grounded by the Tally Corpus Expert (A14, R7). Requirements follow the "good
> requirement" checklist: uniquely identified, atomic, testable, unambiguous, traceable.
>
> Document status: **living skeleton** — non-functional requirements (§5) are project-wide and stable;
> functional requirements (§4) grow phase-by-phase.

---

## 1. Introduction

### 1.1 Purpose
Specify the requirements for **Apex Solutions**, a faithful, offline, keyboard-first desktop clone of
**Tally Prime** (double-entry accounting + inventory + Indian statutory: GST/TDS/TCS/Payroll). This SRS is
the contract between the plan (`plan.md`) and the implementation; it is the traceability anchor for tests.

### 1.2 Scope
Reproduce Tally Prime's **behaviour, navigation, and keyboard shortcuts** as catalogued in
`docs/tally-feature-catalog.md`: the Gateway of Tally hub, Create/Alter verbs, F11/F12 gating, drill-down
everywhere, the To/By (Dr/Cr) model, the 28 predefined groups + 2 default ledgers + 24 predefined voucher
types seed, and the matching reports (Balance Sheet, P&L, Trial Balance, Day Book, Stock Summary,
Outstandings, GST returns, …). Full in-scope / out-of-scope lists live in `plan.md` §1.2–§1.3.

### 1.3 Definitions, acronyms, abbreviations
- **GOT** — Gateway of Tally (the single-window hub).
- **Voucher** — a dated, balanced double-entry transaction (Dr = Cr).
- **Group / Ledger** — chart-of-accounts nodes (28 predefined groups; ledgers underneath).
- **To/By** — Tally's Cr/Dr entry vocabulary.
- **Robert / Bright** — the two deterministic study fixtures used as ledger-engine regression baselines.
- **NFR / FR** — Non-Functional / Functional Requirement.

### 1.4 References
- `plan.md` (master plan), `CLAUDE.md` (governance rules), `agents.md` (agent roster).
- `docs/tally-feature-catalog.md` and `docs/tally-feature-catalog-verification-report.md`.
- `docs/adr/0001-tech-stack.md` (technology decision).
- `tally/` source PDFs (git-ignored, third-party IP — R4).

### 1.5 Overview
§2 overall description; §3 external interfaces; §4 functional requirements (filled per phase); §5
non-functional requirements; §6 traceability.

---

## 2. Overall Description

### 2.1 Product perspective
A **single-user, offline, single-window desktop application**. The heart is a **framework-agnostic
double-entry ledger engine** (`Apex.Ledger`); inventory, GST, payroll, and reports are projections or
extensions over that engine. Persistence is local **SQLite**. UI is **Avalonia** (see ADR 0001).

### 2.2 Product functions (high level — detailed per phase in §4)
Company/tenant management; chart of accounts; accounting & inventory vouchers; bill-wise tracking; banking &
BRS; cost centres; GST/TDS/TCS; payroll; reports with drill-down; printing/export/import; security & audit;
backup/restore. Mapped to phases in `plan.md` §5.

### 2.3 User characteristics
Accountants and business users familiar with Tally Prime who expect identical keyboard workflows and screen
behaviour; mouse use is optional (NFR-2).

### 2.4 Constraints
Offline-only core (NFR-1); keyboard-first single window (NFR-2); exact decimal money (NFR-3); config-driven
statutory law (no hardcoded GST slabs); cross-platform core (NFR-5); no secrets in the repo (R13).

### 2.5 Assumptions and dependencies
.NET 10 SDK available (pinned at `~/.dotnet`); Avalonia toolkit; SQLite; GitHub Actions CI. Fidelity facts
are grounded in the catalog + `tally/` PDFs; law/edition facts are web-verified, never asserted from memory.

---

## 3. External Interface Requirements

- **3.1 User interfaces** — single-window Avalonia shell reproducing Tally chrome (GOT, right-hand button
  panel, bottom bars); pixel-level fidelity (R7). *Detailed per screen, per phase.*
- **3.2 Hardware interfaces** — commodity desktop; keyboard-centric; optional printer for vouchers/reports.
- **3.3 Software interfaces** — embedded SQLite file store; import/export (PDF/Excel/XML/JSON/HTML);
  offline statutory JSON (e-Invoice / e-Way Bill / GSTR) — no live portal round-trips.
- **3.4 Communications interfaces** — none required for core operation (offline-first, NFR-1).

---

## 4. Functional Requirements — filled per phase

> Each phase appends its atomic, testable FRs here (or in a linked `SRS-<phase>-*.md`), traced to catalog
> sections and to tests. **Phase 0 defines no functional behaviour** (foundations only).

### 4.0 Phase 0 — Setup, scaffold, governance
- **FR-0.1** The solution SHALL build green (`dotnet build`) with a framework-agnostic core library
  (`Apex.Ledger`), a test project (`Apex.Ledger.Tests`), and — when its toolchain is present — a desktop
  shell (`Apex.Desktop`). *Verifies: build succeeds.*
- **FR-0.2** The test suite SHALL run green (`dotnet test`) with at least one passing test, so CI is green on
  an empty/near-empty suite. *Verifies: `dotnet test` exit 0.*
- **FR-0.3** The Robert and Bright fixtures SHALL be captured as **data** (masters, deterministic vouchers,
  expected Trial Balance / P&L / Balance Sheet totals) and tracked by a **skipped** test that loads them,
  documenting intent without asserting engine behaviour (engine arrives in Phase 1). *Verifies: fixtures
  parse; skipped test present.*
- **FR-0.4** The `tally/` folder SHALL be excluded from version control (third-party IP, R4).

### 4.1 Phase 1 — Accounting core (ledger engine)  *(to be filled)*
> Company + seed (28 groups + 2 ledgers + 24 voucher types); Chart of Accounts; core vouchers
> (Contra/Payment/Receipt/Journal/Sales/Purchase + Credit/Debit Note; To/By; modes; Ctrl+A save,
> Alt+D/Alt+X); Trial Balance, Day Book, Balance Sheet, P&L, Ledger/Cash/Bank books; drill-down.
> **Acceptance:** Robert & Bright reproduce known totals to the paisa (NFR-3).

### 4.2 Phase 2 — Bill-wise + Banking + Cost Centres  *(to be filled)*
### 4.3 Phase 3 — Inventory  *(to be filled)*
### 4.4+ Later phases  *(GST, TDS/TCS, Payroll, Reports depth, Security/Audit, Data mgmt, Release — per `plan.md` §5)*

---

## 5. Non-Functional Requirements (project-wide — from `plan.md` §1.4)

- **NFR-1 Offline-first** — all core functions operate with no network; data lives in local files.
  *Testable:* run core flows with networking disabled.
- **NFR-2 Keyboard-first** — every catalogued action reachable by its documented shortcut without a mouse;
  single-window navigation (GOT, Alt+G Go To, Ctrl+G Switch To). *Testable:* keyboard-only walkthrough.
- **NFR-3 Correctness/fidelity** — ledger math is exact (Dr = Cr per voucher; statements reconcile); Robert &
  Bright reproduce known totals **to the paisa**. *Testable:* fixture assertions (Phase 1+).
- **NFR-4 Performance** — a Trial Balance / Day Book over a year of vouchers renders < 1 s on commodity
  hardware; voucher save perceptibly instant. *Testable:* timed benchmark.
- **NFR-5 Portability** — runs on Windows (primary); domain core portable to Linux/macOS. *Testable:* core
  test suite green on all three in CI.
- **NFR-6 Maintainability** — accounting core is a standalone, UI-independent library with ≥ (coverage
  threshold, set in Phase 0) coverage on posting/valuation logic. *Testable:* coverage report.
- **NFR-7 Security** — company data can be password-encrypted (TallyVault); no secrets in the repo (R13);
  audit trail (Edit Log) records master/voucher changes. *Testable:* encryption round-trip; audit entries.
- **NFR-8 Data safety** — backup/restore round-trips losslessly; no destructive op without confirmation.
  *Testable:* backup → restore equality; confirmation prompts present.

---

## 6. Traceability

Every FR traces **catalog section → SRS FR-id → test(s) → plan.md phase**. Maintained as the functional
sections fill. Phase 0 baseline: FR-0.1/0.2 → build+test green; FR-0.3 → fixtures + skipped loader test;
FR-0.4 → `.gitignore` excludes `tally/`.
