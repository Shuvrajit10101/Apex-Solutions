# plan.md — Apex Solutions: Tally Prime Clone (Master Plan)

> **The single source of truth for building this project (CLAUDE.md R6).** Built via the `/software`
> skill from the study corpus. We execute **this** plan, in order, phase-gated. Minor deviations are
> allowed but must be logged in `memory.md` with a reason (R6). Requirements are grounded in
> `docs/tally-feature-catalog.md` (+ `…-verification-report.md`) and the `tally/` PDFs (R7).
>
> **Status:** APPROVED — build authorised by the user 2026-07-02. **Confirmed stack (§3): C# / .NET +
> Avalonia (cross-platform: Windows + Linux + macOS) + SQLite**, pixel-level UI fidelity, config-driven GST
> slabs. The domain model, phases, tests, and gates are stack-agnostic and unchanged. **Phases 0–6
> COMPLETE** (schema **v24**; on `origin/main` via PR #18). **Current phase: Phase 7 — TDS/TCS** (open
> decisions D1–D7 RESOLVED to recommended defaults, user-approved 2026-07-10 — see
> `docs/phase7-tds-tcs-requirements.md`).
>
> **Reading order for any session:** `memory.md` → this file (current phase) → `CLAUDE.md` → `agents.md`.

---

## 1. Vision & Scope

### 1.1 What "clone Tally Prime" means
Build a **faithful, offline, keyboard-first, single-window desktop double-entry accounting + inventory +
Indian statutory (GST/TDS/TCS/Payroll) system** that reproduces Tally Prime's *behaviour, navigation, and
keyboard shortcuts* as catalogued in `docs/tally-feature-catalog.md`. "Faithful" means: same core UX verbs
(**Create / Alter**), same **Gateway of Tally** hub, same **F11 (Features) / F12 (Configuration)** gating,
**drill-down everywhere** (any report figure `Enter`s to its voucher), the **To/By (Cr/Dr)** entry model,
the **28 predefined groups + 2 default ledgers + 24 predefined voucher types** seed, and matching reports
(Balance Sheet, P&L, Trial Balance, Day Book, Stock Summary, Outstandings, GST returns, …).

The heart is a **framework-agnostic double-entry ledger engine** with **local persistence**. Everything
else (inventory, GST, payroll, reports) is a projection or extension over that engine (catalog §1
clone-note). The two deterministic fixtures **"Robert"** and **"Bright"** are the engine's regression
baseline (R8).

### 1.2 In scope (the clone surface — catalog §23 scope map)
- **Accounting core:** Company/tenant boundary; 28-group Chart of Accounts + ledgers (seeded); accounting
  vouchers (Contra F4, Payment F5, Receipt F6, Journal F7, Sales F8, Purchase F9, Credit Note Alt+F6,
  Debit Note Alt+F5) with Item / Accounting / As-Voucher modes; opening balances; **Balance Sheet, P&L,
  Trial Balance, Day Book, Ledger/Cash/Bank books**.
- **Bill-wise** (New/Agst/Advance/On-Account refs; split refs; ageing; Outstandings/Receivables/Payables).
- **Banking** (BRS incl. statement auto-import & auto-recon; cheque printing; bank allocation; post-dated).
- **Cost Categories & Cost Centres**; Budgets; Scenarios; Reversing Journals; Memoranda; Interest.
- **Inventory:** Stock Group/Category, Units (simple+compound), Godown, Stock Item; stock & order vouchers
  (PO/SO, GRN/Delivery, Rejection In/Out, Stock Journal, Physical Stock); order processing chain.
- **Advanced inventory:** Batches/expiry, BOM & Manufacturing Journal, additional cost of purchase,
  zero-valued/actual-vs-billed, Price Levels/Lists, Reorder, POS (multi-mode payment), Job Work.
- **GST** (the statutory centrepiece): regular intrastate (CGST+SGST) / interstate (IGST) routing, rate
  resolution, tax & party masters, B2B/B2C, RCM, imports/exports/SEZ, advance-receipt GST, ITC set-off
  (Rule 88A), stat payment; **GSTR-1, GSTR-3B**, HSN summary, GSTR-2A/2B reconciliation; QRMP+IFF;
  e-Invoice (IRN/QR) & e-Way Bill (online + offline JSON); composition (GSTR-4/CMP-08); annual returns.
- **TDS/TCS:** Nature of Payment/Goods masters, applicability flags, auto-computation, challan recon,
  Form 26Q/27EQ (FVU export), 194Q, 206AB/206CCA.
- **Payroll:** employee/group/category masters, pay heads, salary structures, attendance→payroll→payment
  processing, payslips, PF/ESI/PT/IT statutory (computed EPS/EPF split, not hardcoded 3.67%).
- **Reports depth**, printing, export (PDF/Excel/XML/JSON/HTML), import (XML), email.
- **Security & administration:** TallyVault, Security Control, user roles, password policy, **Edit Log /
  Tally Audit** (audit trail).
- **Data management:** backup/restore, split-by-FY, group company (consolidation), repair/rewrite.
- **Configuration model:** first-class **F11/F12** feature-flag + per-screen config layer that gates which
  fields/vouchers/reports appear (catalog §20 clone-note).
- **Modern baseline enrichments** (release-wise, catalog verification §(B)): graphical dashboard, Go To
  multi-tasking, Save View, More Details side-panel. (Connected-GST / IMS / WhatsApp: see out-of-scope.)

### 1.3 Out of scope (explicit)
- **Legacy VAT / CST / Service Tax / Excise** `[legacy]` (catalog §15). *Note (verification §A25): real Tally
  Prime still ships these as optional F11 modules, but they are superseded by GST and out of scope for this
  clone unless the user later requests historical fidelity.*
- **TDL** (Tally Definition Language add-on ecosystem) — catalog §23.
- **Tally.NET / remote access / ODBC / synchronisation / browser (Tally) / mobile Tally** — catalog §23.
- **Cloud/online-only statutory automations** that require live third-party portals or accounts:
  **Connected GST portal filing, direct GSTIN auto-fetch, IMS live download, WhatsApp sharing, online IRN
  from the IRP.** We implement the **offline JSON** paths (e-Invoice/e-Way Bill export, GSTR JSON) which
  keep the app fully offline; live-portal round-trips are deferred (see Open Questions §9).
- **Multi-user server / concurrent networked data access** — the clone is single-user, local-data, matching
  Tally's default desktop feel. (Group-company consolidation is in scope; multi-user auth is not.)

### 1.4 Non-functional requirements (the constraints that shape the build — requirements.md §NFR)
Written testably (requirements.md "good requirement" checklist):
- **NFR-1 Offline-first:** all core functions operate with **no network**; data lives in **local files**.
- **NFR-2 Keyboard-first:** every catalogued action reachable by its documented shortcut without a mouse;
  single-window navigation (GOT hub, Alt+G Go To, Ctrl+G Switch To).
- **NFR-3 Correctness/fidelity:** ledger math is exact (Dr=Cr per voucher; statements reconcile); the
  Robert & Bright fixtures reproduce known totals to the paisa.
- **NFR-4 Performance:** typical report (Trial Balance / Day Book on a year of vouchers) renders < 1 s on
  commodity hardware; voucher save is perceptibly instant.
- **NFR-5 Portability:** runs on Windows (primary target); domain core portable to Linux/macOS.
- **NFR-6 Maintainability:** accounting core is a standalone, UI-independent library with ≥ (threshold set
  in Phase 0) test coverage on posting/valuation logic.
- **NFR-7 Security:** company data can be password-encrypted (TallyVault); no secrets in the repo (R13);
  audit trail (Edit Log) records master/voucher changes.
- **NFR-8 Data safety:** backup/restore round-trips losslessly; no destructive op without confirmation.

---

## 2. Process Model

### 2.1 Model: iterative, incremental, phase-gated (CLAUDE.md R9)
We use an **Agile-iterative** lifecycle with **hard phase gates** (a hybrid: agile inside a phase, gated
between phases). Each phase is a thin vertical slice that delivers working, tested, catalogued features and
ends at a gate the **user** must clear before the next phase starts (R9, R12). Within a phase we run
**TDD** (superpowers:test-driven-development; testing.md): Red → Green → Refactor, tests before code (R8).

Rationale (project-management.md, testing.md): the domain is large and precise; a phase-gated arc keeps the
ledger engine correct before features pile on it, gives the user go/no-go control (R12), and lets each phase
be independently demoed and regression-locked.

### 2.2 How work flows through the agents (CLAUDE.md R2/R3 — agents in `agents.md`, run inside Workflows)
The **main loop only decides, sequences, synthesizes, and talks to the user** (R2/R14). All substantive
work is delegated to **named agents in `agents.md`**, orchestrated with the **Workflow** tool. The standard
per-feature pipeline:

1. **Requirements/Design agent** — turns a catalog section into atomic, testable requirements (SRS slice)
   + UML where useful (use-case / sequence / class, as Mermaid, kept in-repo — design-ux-uml.md), grounded
   by the **Tally Domain/Corpus Expert** (A14) against the catalog + `tally/` PDFs (R7).
2. **Tally Domain/Corpus Expert (A14)** — resolves any fidelity doubt against the corpus; law/edition facts
   are **web-verified** against official sources, never asserted from memory (R7).
3. **Test author** — writes failing unit/integration tests from the requirements (TDD; R8).
4. **Implementer** — writes the code to green, following implementation.md coding-craft + defensive
   programming; keeps the accounting core UI-independent.
5. **Code Reviewer** — reviews for the six qualities (readability/maintainability/performance/traceability/
   correctness/completeness) before merge (R10; implementation.md §6).
6. **GitHub Expert** — the **exclusive** owner of all git/GitHub: branch, small conventional commits, PR,
   review-gated merge, tags, releases, CI/CD (R4/R10). No other agent or the main loop touches git.
7. **Run-the-app verification** — the app is actually launched and the feature exercised; evidence recorded
   (R9/R11; superpowers:verification-before-completion).
8. **memory.md updated** every step (R5); **plan.md** updated if scope shifts (R6).

> If a needed capability has no agent, **add it to `agents.md` first**, then use it (R3). `agents.md` is
> authored/finalised as a Phase-0 deliverable (see memory.md — drafting in progress).

### 2.3 Cadence & artifacts (project-management.md)
- **Backlog** = this plan's phases → modules → catalog-item work items, tracked as **GitHub Issues/Projects**
  by the GitHub Expert (bug tracking = GitHub Issues; testing.md/tools-and-databases.md).
- **Living roadmap** replaces a static Gantt (project-management.md "modernized"): phase order below +
  per-phase exit gate. Kept alive, not filed-and-forgotten.
- **Documentation-as-code:** ADRs, SRS slices, UML, README, CHANGELOG, user notes live in the repo next to
  the code (deployment-docs-maintenance.md §2.2/§2.3).

---

## 3. Architecture & Tech Stack — **CONFIRMED (user-approved 2026-07-02)**

### 3.0 Locked decisions (user, 2026-07-02) — these supersede the proposal below

| Decision | Choice |
|---|---|
| **Language / runtime** | **C# / .NET** (latest LTS) |
| **Desktop UI** | **Avalonia** (cross-platform XAML) — chosen over WPF specifically for cross-platform |
| **Persistence** | **SQLite** (one `.db` file per company = tenant boundary) via EF Core or Microsoft.Data.Sqlite + versioned migrations |
| **Accounting core** | **`Apex.Ledger`** — a framework-agnostic C# class library (no UI/DB deps; persistence via repository interfaces) |
| **Testing** | **xUnit** (unit/integration) + an Avalonia UI test harness; coverage via coverlet |
| **CI/CD & packaging** | **GitHub Actions**; cross-platform installers (Windows exe/MSI, Linux AppImage/deb, macOS dmg) |
| **OS target (v1.0)** | **Windows + Linux + macOS** (Avalonia is cross-platform) |
| **UI fidelity** | **Pixel-level mimicry** of Tally Prime's actual screens — exact layouts, colours, column arrangements, the blue-panel look; highest-fidelity UX bar |
| **GST slabs** | **Config-driven**; seed the classic **0/5/12/18/28 + Cess** set now; add GST 2.0 (5/18/40) only after official CBIC confirmation at Phase 4 (see §10 C-9) |

> The architecture principles below (3-tier separation, framework-agnostic domain core, repository/port
> persistence, diagrams-as-code) **still apply verbatim** — only the concrete tool names change from the
> original TypeScript/Tauri proposal to the C#/.NET/Avalonia stack above. The §3.2 baseline table and §3.4
> alternatives are retained as historical rationale; where they name TS/Tauri/Vitest, read
> C#/Avalonia/xUnit. A Phase-0 ADR will record this decision formally.

### 3.1 Constraints driving the choice (architecture-and-platforms.md "choosing a platform")
Tally is a **keyboard-first, single-window, OFFLINE desktop app with local data** — the clone must match
that feel (NFR-1/2/5). We also optimise for a stack **AI agents can build & test quickly** with strong
automated testing, and one that keeps the **accounting core as a clean, framework-agnostic library with
local persistence** (R2 agentic build; implementation.md API-driven design; architecture-and-platforms.md
3-tier layering).

### 3.2 Recommended baseline stack
**A cross-platform desktop shell over a TypeScript accounting-core library, with an embedded local SQL
database — Windows as the primary target.**

| Layer | Proposed choice | Why |
|---|---|---|
| **Language** | **TypeScript** (strict) | One language across core + UI; fastest AI agent iteration; huge test ecosystem; static types catch ledger-math errors early (implementation.md §10 fail-fast). |
| **Accounting core** | **Framework-agnostic TS library** (`@apex/ledger-core`) — no UI, no DB imports; pure domain: entities, posting engine, valuation, report projections; persistence via a repository interface | Satisfies the R2/§1.1 mandate: the core is stack-agnostic and unit-testable in isolation (implementation.md API design; architecture-and-platforms.md 3-tier "business tier"). This is the load-bearing decision and it is **stack-independent**. |
| **Persistence** | **SQLite** (single-file, local) via **better-sqlite3**; schema versioned by **migrations** (Drizzle/Prisma or hand-rolled SQL migrations) | tools-and-databases.md: SQLite = "serverless, single-file, zero-config; ideal for embedded/local." Matches Tally's per-company local data file & NFR-1/8. One `.db` file per company = the tenant boundary. |
| **Desktop shell** | **Tauri** (Rust host + web UI) — *baseline*; **Electron** = fallback | Tauri gives a tiny, fast, secure offline desktop app; both let agents build UI with web tech and drive it in CI (mcp Preview/Playwright). Tauri preferred for footprint/perf (architecture-and-platforms.md "modernized desktop"). |
| **UI** | **React + TypeScript**, keyboard-first (global shortcut/focus manager reproducing F-keys, Alt+G, drill-down, To/By grid) | Component model + design tokens enforce Tally "look & feel" (design-ux-uml.md); web UI is the most agent-testable surface. |
| **Testing** | **Vitest** (unit/integration), **Playwright** (system/keyboard-flow), coverage via Vitest/c8 | testing.md modernized table (JS/TS → Vitest/Jest + Playwright); AAA structure; run in CI on every push. |
| **Tooling/CI** | ESLint + Prettier + `.editorconfig`; **GitHub Actions** CI; packaging via Tauri bundler / electron-builder | implementation.md §2 (style automated in CI); deployment-docs-maintenance.md §4 (CI/CD, desktop installers). |
| **UML/diagrams & docs** | **Mermaid** in-repo | diagram-as-code, reviews in PRs, stays in sync (design-ux-uml.md, tools-and-databases.md §21). |

**Architecture pattern:** a **3-tier separation inside one desktop process** (architecture-and-platforms.md
Part 2): Presentation (React keyboard UI) → Business (`@apex/ledger-core` domain library, the reusable API)
→ Data (SQLite via a repository/port). The core never imports UI or DB directly — it depends on interfaces,
so the DB or shell can be swapped without touching accounting logic (implementation.md API-driven design).

### 3.3 Why this baseline (rationale)
- **Matches Tally's feel:** genuine offline desktop app, local single-file data, single-window keyboard UI.
- **Agent-velocity:** one language (TS) end-to-end; the richest fast unit-test + browser-drive tooling, which
  the agentic build (R2) and CI gates (R9) depend on.
- **Protects the core:** the framework-agnostic TS domain library is the one thing we must get right; it is
  isolated, exhaustively unit-tested (Robert/Bright), and portable if the shell/DB ever change.
- **Cheap, reproducible, no cloud:** SQLite + a bundled shell = zero server, zero external dependency — fits
  NFR-1 and R13.

### 3.4 Alternatives (2–3), each keeping the domain layer intact
1. **.NET (C#) + Avalonia/WPF + SQLite (EF Core), core as a class library.** Pros: strongest native Windows
   desktop fit, mature (WPF/XAML is Tally-like), excellent tooling (implementation.md §11, xUnit). Cons:
   slower AI-agent UI iteration/browser-driving than web; WPF is Windows-only (Avalonia restores
   cross-platform). Domain core (`Apex.Ledger`) is identical in shape — only language/tooling differ.
2. **Python + Qt (PySide6) + SQLite, core as a pure-Python package.** Pros: very fast to prototype, great for
   the statutory/number-crunching logic, pytest is excellent. Cons: desktop packaging & keyboard-UI polish
   are heavier; weaker typed-refactor safety than TS/C#.
3. **Web SPA (React/TS) + local persistence via SQLite-WASM/IndexedDB, wrapped as a PWA/desktop later.**
   Pros: maximal agent-testability, zero install. Cons: "offline desktop with local file data" is more
   awkward in a browser sandbox; weaker OS integration (printing, file dialogs, TallyVault-style encryption).

**Decision rule:** pick by the user's platform priority and the team's comfort. **The plan does not change**
under any of these — the domain model, phases, tests, and gates are all stack-agnostic; §3 is the only
section that would be re-specified.

---

## 4. Domain / Data Model

> The core entities and relationships, distilled from catalog §1–§14 + §22 (seed) and the verification
> report's group corrections. Modelled OO in the framework-agnostic core (implementation.md §8), persisted
> relationally in SQLite (tools-and-databases.md §25 — PK/FK, migrations). **Rename semantics** (verification
> §A11): masters have a **stable ID**; the *name is not the key*; Alter renames in place and applies
> retroactively to all historical vouchers.

### 4.1 Core entities

- **Company** — the tenant/dataset boundary; owns all masters & vouchers (catalog §2). Fields: Name, Mailing
  Name, Address, Country/State/Pin, contacts, **Financial-year-from** (default 1-Apr) vs **Books-from**
  (mid-year start), Base Currency (₹/INR, 2 decimals, "Paisa"), Security (TallyVault), **F11 feature flags**.
  *Seed on create: 28 groups + 2 ledgers + 24 voucher types + Primary Cost Category + Main Location.*
- **Group** — classification node with **nature** (Asset/Liability/Income/Expense) + parent. **28 predefined**
  = **15 Primary** (9 BS + 6 P&L) + **13 Sub-groups** (1/3/6/3 split), per the corrected list
  (verification §A6/A7): *Primary* = Capital Account, Loans (Liability), Current Assets, Current Liabilities,
  Fixed Assets, Investments, Misc. Expenses (Asset), Suspense A/c, Branch/Divisions, Sales Accounts,
  Purchase Accounts, Direct Incomes, Indirect Incomes, Direct Expenses, Indirect Expenses; *Sub* = Reserves
  & Surplus *(Capital)*; **Bank OD A/c (alias Bank OCC A/c)**, Secured Loans, Unsecured Loans *(Loans Liab.)*;
  Bank Accounts, Cash-in-Hand, Deposits (Asset), Loans & Advances (Asset), Stock-in-Hand, Sundry Debtors
  *(Current Assets)*; Duties & Taxes, Provisions, Sundry Creditors *(Current Liab.)*. Custom groups nest
  under any. Predefined groups cannot be deleted. **P&L A/c is a reserved head, NOT a 29th group**
  (verification §A8).
- **Ledger** — transactional account, `Under` a Group; Opening Balance (Dr/Cr). **2 defaults: Cash**
  (Cash-in-Hand) and **Profit & Loss A/c** (verification §A8: P&L is a ledger/reserved head). Feature-gated
  blocks: bill-by-bill + credit period + **Credit Limit**, interest params, bank details, **"Inventory
  values are affected?"**, "Cost centres applicable?", GST/TDS/TCS statutory sub-screens, PAN/MSME.
  *Stock-in-Hand ledger closing balance is **derived** from inventory when Accounts+Inventory integrated
  (verification §A10).*
- **VoucherType** — **24 predefined** (base type + shortcut + numbering), plus custom. Fields: Name, base
  type, Abbreviation, Active?, Numbering (Automatic/Manual/None), Use Common Narration, Print after save,
  **Use for POS**, **Use as Manufacturing Journal**, **Use for Job Work**, **Track Additional Costs**, Allow
  zero-valued, **Name of Class** (voucher classes with default accounting allocations — verification §B). The
  8 non-core additional types (Memorandum, Reversing Journal, Job Work In/Out Order, Material In/Out,
  Attendance, Payroll) — Payroll & Job-Work types appear only when their F11 feature is on (verification §A15).
- **Voucher** — header (type, number, date, party, narration, optional/post-dated/cancelled flags) + **≥2
  balanced EntryLines**. Invariant: **Σ Dr = Σ Cr** (catalog §1/§4). **Cancel (Alt+X)** keeps the number in
  sequence (greyed in Day Book); **Delete (Alt+D)** removes it and can gap numbering (verification §A14).
  Modes: Item / Accounting / As-Voucher; single-vs-double entry is an F12 mode (verification §A13).
- **EntryLine** — ledger, Dr/Cr amount; optional sub-allocations: **inventory allocation** (item, qty, rate,
  godown, batch), **bill references**, **cost-centre allocation**, **GST/TDS/TCS breakup**, **bank allocation**.

### 4.2 Sub-ledgers & extensions
- **Bill (reference)** — party ledger, ref name, type (**New / Agst / Advance / On-Account**), due date,
  **GST-inclusive amount** (catalog §5); a voucher amount may **split** across several bills. Drives ageing.
- **CostCategory** (Allocate Revenue / Non-Revenue; default *Primary Cost Category*) → **CostCentre**
  (hierarchical, under Primary or a parent) — catalog §6.
- **Inventory masters** (catalog §9): **StockGroup** (add-quantities?, group GST), **StockCategory**
  (independent axis), **Unit** (Simple: symbol/UQC/decimals; **Compound**: first × factor + tail),
  **Godown/Location** (default *Main Location*; third-party flag), **StockItem** (Under, Category, Units,
  opening balance w/ godown+batch, HSN/SAC + GST, batch tracking, **BOM**, reorder levels, TCS).
  **Batch** (lot no., mfg/expiry dates). Valuation method (FIFO/Avg/…) drives stock value.
- **GST config** (catalog §12): Company GST (State, Registration Type, GSTIN, GSTR-1 periodicity incl. QRMP,
  e-Way/e-Invoice thresholds); tax ledgers under **Duties & Taxes** (Central/State/Integrated/Cess);
  party GST (reg type, GSTIN, state, SEZ/e-com flags); item/ledger GST (HSN/SAC, taxability, calc type,
  rates, supply type, RCM, ineligible-ITC, nature-of-transaction). **Rate resolution** 5 levels (Company →
  Stock Group → Stock Item → Ledger → GST Classification, most-granular wins).
- **TDS/TCS config** (catalog §13): **NatureOfPayment** (section, rate w/ & w/o PAN, threshold; 194Q,
  206AB/206CCA) / **NatureOfGoods** (206C); duty ledgers; applicability flags on expense/party/item ledgers.
- **Payroll masters** (catalog §14): EmployeeCategory, EmployeeGroup, **Employee** (PAN/Aadhaar/UAN/PF/ESI/
  PRAN, bank), PayrollUnits, Attendance/Production Types, **PayHead** (Earnings/Deductions/Employer-Contrib/
  Employer-Other/Payable, with Calculation Type + formulas), SalaryStructure. **Computed EPS/EPF split**
  (verification §A26): EPS = 8.33%×min(wage,15000) cap ₹1,250; employer-EPF = 12%×PF-wage − EPS.
- **Config layer** — **F11 CompanyFeatures** (module switches) + **F12 Configuration** (per-screen options);
  a first-class settings entity that gates which fields/vouchers/reports are visible (catalog §20).
- **Security/Audit** — User, Role (permissions, back-dated limits), PasswordPolicy, **EditLog/AuditEntry**
  (before/after, user, timestamp) — catalog §18.

### 4.3 Key relationships (ER shape)
Company **1—∗** Group; Group **1—∗** Ledger (self-nesting for sub-groups); Ledger **1—∗** EntryLine;
Voucher **1—∗** EntryLine (Σ Dr = Σ Cr); VoucherType **1—∗** Voucher; EntryLine **1—∗** {BillRef,
InventoryAllocation, CostAllocation, TaxBreakup}; StockGroup/Category **1—∗** StockItem; StockItem **∗—∗**
Godown/Batch (via allocations); CostCategory **1—∗** CostCentre; Employee **∗—1** EmployeeGroup/Category.
All masters carry a **stable surrogate PK** (`<Entity>Id`, tools-and-databases.md convention), FK-linked.

### 4.4 Seed data (catalog §22) — applied on every `Company.create`
28 groups (nature+parent) · Cash + P&L A/c ledgers · Primary Cost Category · Main Location · 24 voucher types
(base type + shortcut + numbering) · base currency ₹/INR 2-dp "Paisa" · FY 1-Apr→31-Mar. **This seed is
itself a fixture-backed unit test** (a fresh company must contain exactly these).

---

## 5. Phased Roadmap

> Ordered, phase-gated. Each phase: **Goals → Catalog modules delivered → Agents involved → Deliverables →
> Exit gate**. Every phase's exit gate is the CLAUDE.md **R9** sequence (tests green shown → review pass →
> GitHub Expert commits/pushes → run the real app → memory.md updated → **user go-ahead**) and satisfies the
> **R11** Definition of Done for its features. Agents are those in `agents.md` (finalised in Phase 0).

### Phase 0 — Setup, scaffold, governance
- **Goals:** stand up the repo, toolchain, CI, and the framework-agnostic project skeleton; finalise
  `agents.md`; write the SRS skeleton + architecture ADRs; **lock the stack (user confirms §3).**
- **Modules:** none functional — foundations only.
- **Agents:** GitHub Expert (repo init, .gitignore incl. `tally/`, branch model, CI/CD skeleton — R4);
  Requirements/Design agent (SRS skeleton, ADRs, top-level UML); Tally Corpus Expert (A14, review).
- **Deliverables:** repo scaffolded (`ledger-core` lib + shell + test harness stubs); ESLint/Prettier/
  editorconfig; GitHub Actions running an empty green test suite; `agents.md` complete; SRS/ADR/README seeds;
  **Robert & Bright fixtures captured as data** (expected totals) ready to drive Phase 1.
- **Exit gate:** CI green on an empty suite; stack approved by user; `agents.md` merged; memory.md updated.

### Phase 1 — Accounting core (the ledger engine)
- **Goals:** the double-entry engine + minimal keyboard UI; **Robert & Bright pass** end-to-end.
- **Modules (catalog §1–§4, §16):** Company (+seed), Chart of Accounts (28 groups + ledgers, single/multi/
  inline create, delete guards), core vouchers (Contra/Payment/Receipt/Journal/Sales/Purchase + Credit/Debit
  Note; To/By model; modes; Ctrl+A save, Alt+D/Alt+X), **Trial Balance, Day Book, Balance Sheet, P&L, Ledger/
  Cash/Bank books**, drill-down.
- **Agents:** full per-feature pipeline (§2.2) — Requirements/Design, A14, Test author, Implementer, Reviewer,
  GitHub Expert, run-app verifier.
- **Deliverables:** `ledger-core` posting engine + report projections with exhaustive unit tests; the two
  fixtures as **regression baselines** (R8); a runnable single-window keyboard app that enters the 13 Robert
  vouchers and shows correct statements.
- **Exit gate:** Robert & Bright reproduce known totals to the paisa (shown); R9 sequence complete.

### Phase 2 — Bill-wise + Banking + Cost Centres
- **Goals:** receivables/payables, bank workflows, cost analysis over the Phase-1 engine.
- **Modules (catalog §5, §6, §8):** bill-wise (4 ref types + split; Outstandings/Receivables/Payables;
  ageing), BRS (+ statement auto-import & auto-recon, bank allocation, cheque printing, post-dated Ctrl+T),
  Cost Categories/Centres (+ allocation window + cost reports).
- **Agents:** per-feature pipeline.
- **Deliverables:** ageing & outstandings reports; BRS matching an imported statement; cost-centre break-ups.
- **Exit gate:** R9; new features regression-locked; fixtures still green.

### Phase 3 — Inventory (masters + stock vouchers + order processing)
- **Goals:** stock keeping integrated with accounts.
- **Modules (catalog §9, §10):** inventory masters (Stock Group/Category, Units simple+compound, Godown,
  Stock Item), stock & order vouchers (PO/SO, GRN/Delivery, Rejection In/Out, Stock Journal, Physical Stock
  via F10), order-processing chain & effect rules, Stock Summary + inventory registers, Accounts↔Inventory
  integration & valuation.
- **Agents:** per-feature pipeline (+ A14 on valuation fidelity).
- **Deliverables:** item-invoice sales/purchase affecting stock+accounts; order books; Stock Summary with a
  valuation method; **Bright** re-verified with closing stock.
- **Exit gate:** R9; valuation reconciles into the Balance Sheet.

### Phase 4 — GST (regular intrastate/interstate; GSTR-1/3B)
- **Goals:** the statutory core — correct CGST+SGST / IGST routing and the two headline returns.
- **Modules (catalog §12, MVP subset):** F11 Enable GST + company/party/item/tax masters, 5-level rate
  resolution, intrastate (CGST+SGST) vs interstate (IGST) routing on assessable value, B2B/B2C, tax analysis
  (Alt+A), stat adjustment (Alt+J) + stat payment (Ctrl+F), **GSTR-1 & GSTR-3B** + HSN summary.
- **Agents:** per-feature pipeline with **A14 leading** (fidelity + law verification, incl. slab decision).
- **Deliverables:** GST invoices computing tax correctly across mixed rates; GSTR-1/3B matching worked
  examples; ITC set-off per **Rule 88A**.
- **Exit gate:** R9; a golden-set of GST invoices produces exact GSTR-1/3B figures.

### Phase 5 — Reports depth + printing/export/import/email
- **Goals:** complete the report surface and I/O.
- **Modules (catalog §16, §17):** report families (Account/Inventory Books, Statements of Accounts/Inventory,
  Exception reports, Ratio Analysis, Cash/Funds Flow, comparative/columnar), cross-cutting report actions
  (Alt+F1/F2/C/N/F12, Enter, F12 config, **Save View**), **print** (render-to-PDF + on-screen preview;
  OS-native spooler deferred), **export** (**PDF / XLSX / CSV / JSON / XML**; *HTML export deferred —
  tracked*), **import** (JSON / CSV / XML), **email** (compose + `.eml`/mail-client hand-off; SMTP profile
  captured, no secret; live SMTP send deferred — tracked), graphical **dashboard**, Go To multi-tasking,
  More Details.
- **Agents:** per-feature pipeline (+ **A15 Reporting & I/O Engineer** owns the print/export/import/email IO layer).
- **Deliverables:** printable invoices & statements; **lossless round-trip in BOTH JSON and XML** export/import;
  saved report views.
- **Exit gate:** R9; export/import round-trips losslessly.

> **Phase 5 — deferred (tracked):** (1) **HTML export** — deferred this phase; the other five export formats
> (PDF/XLSX/CSV/JSON/XML) ship now. (2) **Wire live SMTP email send** — this phase composes + hands off
> `.eml`/mail-client and captures the SMTP profile (no secret in repo, R13); the live SMTP send is deferred to
> revisit in a later phase.

### Phase 6 — Advanced inventory
- **Goals:** the deep inventory features.
- **Modules (catalog §11):** batches/expiry, **BOM & Manufacturing Journal**, additional cost of purchase,
  zero-valued & actual-vs-billed qty, **Price Levels/Lists**, **Reorder** (+status report), **POS**
  (multi-mode payment, Alt+I), **Job Work** (in/out orders + material in/out, third-party godowns).
- **Agents:** per-feature pipeline (+ A14).
- **Deliverables:** manufacture-a-finished-good flow; POS multi-tender receipt; reorder suggestions.
- **Exit gate:** R9.

### Phase 7 — TDS / TCS
- **Goals:** income-tax withholding statutory.
- **Modules (catalog §13):** TDS (Nature of Payment incl. 194J/194C/194H/194I/194A/**194Q**, deductor/party
  flags, deduct→pay→deposit, **Challan Recon**, **Form 26Q** + FVU export, 206AB/206CCA), TCS (Nature of
  Goods 206C, auto-compute, **Form 27EQ**/27D). *206C(1H) → legacy year-gated; 206AB/206CCA omitted (FA
  2025). RESOLVED (D1–D7, see `docs/phase7-tds-tcs-requirements.md`).*
- **Agents:** per-feature pipeline with **A14 leading**.
- **Deliverables:** TDS deduction + Form 26Q (FVU-valid) worked example; TCS on a sale.
- **Exit gate:** R9.

### Phase 8 — Payroll
- **Goals:** full payroll cycle + statutory.
- **Modules (catalog §14):** masters, pay heads (all calc types), salary structures, **attendance → payroll
  → payment** processing (Ctrl+F4, autofills), payslips/registers, PF/ESI/PT/IT statutory with **computed
  EPS/EPF** (not 3.67%), gratuity.
- **Agents:** per-feature pipeline (+ A14 for constants/thresholds; web-verify current rates).
- **Deliverables:** a run producing correct payslips + PF/ESI challans for a sample workforce.
- **Exit gate:** R9; statutory figures match verified constants.

### Phase 9 — GST advanced + returns + e-invoice/e-way (offline)
- **Goals:** the rest of GST breadth.
- **Modules (catalog §12 advanced):** RCM, imports (BoE)/exports (LUT/shipping bill)/SEZ/deemed exports,
  advance-receipt & advance-payment RCM, composition (**GSTR-4 annual / CMP-08 quarterly**), **QRMP + IFF**,
  **GSTR-2A/2B reconciliation**, annual returns (GSTR-9/9A/9C), **e-Invoice IRN/QR** & **e-Way Bill** via
  **offline JSON** export, per-tax-ledger rounding, multi-GSTIN.
- **Agents:** per-feature pipeline with **A14 leading** (heavy law verification).
- **Deliverables:** offline e-Invoice/e-Way JSON; GSTR-2B reconciliation; composition returns.
- **Exit gate:** R9.

### Phase 10 — Security/roles/audit + data management
- **Goals:** administration & data safety.
- **Modules (catalog §18, §19):** TallyVault encryption, Security Control + user roles + password policy,
  **Edit Log/Tally Audit**, backup/restore, **split-by-FY**, **group company** consolidation, repair/rewrite.
- **Agents:** per-feature pipeline (+ security review; superpowers:security-review where relevant).
- **Deliverables:** encrypted company; role-gated access; lossless backup/restore; a split & a consolidated
  group-company statement.
- **Exit gate:** R9; no secrets in repo (R13); audit trail verified.

### Phase 10.5 — CA-audit remediation
- **Goals:** implement the Chartered-Accountant audit backlog — **WI-1 … WI-14** per
  `docs/ca-audit-backlog.md` (the CA's 15 raw points decoded to 14 work items) — honouring the recorded
  **user rulings**: (a) point 9 → **KEEP** the existing Payroll §192 module + small discoverability/gate
  fixes; do **NOT** build a parallel per-employee-ledger salary path (an active correctness regression) —
  WI-8; (b) WI-3 → ship editable masters now but **DEFER the alteration audit-trail hook to Phase 10**
  (needs Phase-10 audit infra), so WI-3 stays v44/schema-free; (c) WI-13 renumbering → A14 **web-verifies
  each rename** (R7), ship the law-confirmed form renames + **flag** the still-unconfirmed TY 2026-27
  slab/SD/87A/surcharge rate question (retry the 403'd official fetch); (d) point 6 / WI-6 → the §192
  salary-TDS engine is complete, the sole break is a missing pay-head picker option (an **S** fix). Ground
  every fidelity/law doubt in A14 (R7); do **NOT** re-derive the backlog — `docs/ca-audit-backlog.md` is the
  ground truth (per-WI decoded requirement, fidelity target + citation, file:line evidence, gap, proposal,
  effort, risk, open questions).
- **Live correctness bugs to fix inside their WIs (found during the decode, NOT CA-reported):** Alt+C
  mid-voucher silently destroys the in-progress voucher — data loss (**WI-1**); type-ahead selects the
  **wrong** ledger on 56 domain-bound pickers, every item's search-text being `Apex.Ledger.Domain.Ledger`
  (no `ToString` override) (**WI-2**); `ImportPlan.cs:172` accepts a group Nature contradicting its parent →
  Balance-Sheet corruption that still "balances" (**WI-7**); `Unit.cs:146` inverts the conversion direction
  (**WI-10**); `dd/MM`→`MM/dd` InvariantCulture silent date misread + silent-discard of unparseable dates
  across ~21 parse sites incl. the main `VoucherEntryViewModel` (**WI-5**).
- **Work items (id — one-line):**
  - **WI-1** Context-aware **Alt+C create-on-the-fly** — dispatch on the focused field's master kind
    (ledger/item/stock-group/category/unit/godown/acct-group), open non-destructively **beside** the
    voucher, return-to-caller with the new master selected, plus an in-dropdown "Create" entry.
  - **WI-2** **Dropdown keyboard-nav + type-ahead** — every dropdown navigable (Up/Down/Enter/Esc) and
    filtering-as-you-type on Name+Alias; fixes the 56-picker wrong-ledger `ToString` bug.
  - **WI-3** **Master alteration (the "Alter" verb)** — pick a master, open the same form pre-filled, edit
    any field, accept, save against **stable Guid identity** so a rename propagates retroactively; scoped
    to ledger + group + item (the other ~15 masters already have tested engine mutation).
  - **WI-4** **Party ledger Mailing Details** — Mailing Name / Address / Country / State / PIN captured,
    persisted, Io round-tripped and **printed on invoices** (nullable value-object off `Ledger`; **v45**).
  - **WI-5** **Date handling** — one canonical format app-wide, lenient day-first input re-rendered
    canonically on commit, and **F2 sets the working/voucher date on all entry screens** (not just reports).
  - **WI-6** **Reachable salary-TDS pay-head option** — add the missing `TaxDeductedAtSource` entry to the
    `PayHeadMaster` income-tax picker so a UI-created head can be the TDS head (today salary TDS ≡ ₹0).
  - **WI-7** **Accounting-Group master (Create + Alter)** — a real Group creation screen (fix the
    Create→Group mis-wire that opens Ledger Creation); **Nature derived from the parent, never accepted**;
    validator **shared** with `ImportPlan.cs:172`. Prerequisite for WI-1 / WI-3 / WI-11.
  - **WI-8** **TDS on a non-party (employee) ledger** — per the user ruling, do **discoverability**, NOT the
    dangerous parallel §194x salary path (which would route salary into Form 26Q at the wrong rate mechanic).
  - **WI-9** **Bare-letter menu hotkeys, letter shown red** — single bare-letter activation, first-letter by
    default with a per-column-unique fallback; the letter must NOT be encoded in the Label string. Fidelity
    target **UNVERIFIED** (bare-letter? red? collision rule?) → A14/live-Tally grounding first.
  - **WI-10** **Multiple units per item + conversion** — pass a **line unit** at entry (fix the always-null
    `UnitId` and the backwards `Unit.cs:146`); per-item **Alternate Units** and invoice-line rate semantics
    (Slices A+B no-schema; C+D **v45**).
  - **WI-11** **Y/N Accept confirmation** — add the terminal "Accept? Y/N" on master save. Ctrl+A
    accept-as-is is already comprehensively implemented and must **BYPASS** the prompt (do not rebuild it).
  - **WI-12** **Alt+A add-a-voucher from the Day Book** — a voucher-type picker (any active type) without
    destroying the report, refreshing it on save; also bind the specified-but-missing **Alt+F5 Debit Note /
    Alt+F6 Credit Note** keys (engine + screen + tests already exist).
  - **WI-13** **Income-tax Act 2025 renumbering** — rename §192→**§392** and Forms 24Q→**138** / 16→**130** /
    12BB→**124** / 16A→**131** ("tax year") on the user-visible surface + citations (A14-verified **per
    rename**, R7); move hardcoded FY2025-26 rate consts to **effective-dated seeded config**.
  - **WI-14** **Salary-TDS deposit / challan path** — let accrued salary TDS be deposited/challaned (the
    Phase-7 deposit machinery keys on `TdsLineTax` §194x, salary carries `PayrollLineDetail`); needs an
    architecture decision to avoid polluting Form 26Q. **Deferred** (A14-discovered); revisit after WI-13.
- **Slices (build order — high-value / low-risk first; full per-slice rationale kept in `memory.md`):**
  1. **S1 — Salary-TDS reachability + TDS discoverability** (WI-6, WI-8) — **S / low / v44** — **FIRST:** an
     S one-line picker fix that un-breaks the whole already-built §192 engine (today unconditionally ₹0);
     WI-8 resolves to discoverability, not new build.
  2. **S2 — Accounting-Group master + shared Nature validator** (WI-7) — **M / med / v44** — prerequisite
     for S6/S7; the validator simultaneously closes the live `ImportPlan.cs:172` Balance-Sheet-corruption
     path.
  3. **S3 — Day-Book Alt+A add-voucher + Alt+F5/F6 CN-DN keys** (WI-12) — **M / low / v44** — independent
     momentum win; the CN/DN engine, screen and 10 tests already exist (wiring only).
  4. **S4 — Date handling** (WI-5) — **L / med / v44** — fixes two live date bugs across ~21 parse sites;
     order its F2-key arm just before the keyboard cluster to share tunnel-handler context.
  5. **S5 — Keyboard cluster** (WI-2, WI-9, WI-11) — **XL / HIGH / v44** — the three edit the same
     ~450-line first-match-wins tunnel handler and MUST be designed together (WI-2 type-ahead FILTER vs WI-9
     bare-letter ACTIVATE collide on data-driven picker columns; WI-11's Y/N block must precede the existing
     Y/Alt+N handlers). Fixes the live wrong-ledger bug. **A14-gated** (WI-9 fidelity unverified).
  6. **S6 — Context-aware Alt+C create-on-the-fly** (WI-1) — **L / med / v44** — depends on S2 (group
     target) + S5 (WI-2 return-to-caller contract); fixes the live Alt+C data-loss and the latent Alt+B
     return bug.
  7. **S7 — Master-alteration spine + party mailing details** (WI-3, WI-4) — **XL / med / v45 (WI-4)** —
     depends on S2; WI-3 makes WI-4's new mailing fields editable (create-only WI-4 would be worse than no
     field) and unblocks re-tagging the WI-6 pay head.
  8. **S8 — Multiple units per item + conversion** (WI-10) — **XL / med-high / v45 (C/D)** — independent;
     Slices A+B (line-unit + the `Unit.cs:146` fix) are schema-free, C/D need v45 and **A14** rate semantics.
  9. **S9 — Income-tax Act 2025 renumbering + rate effective-dating + salary-TDS deposit** (WI-13, WI-14) —
     **L / med-high / v44** — **A14-gated per rename** (R7); reconciles the WI-6 picker label naming once
     (§392); WI-14 carries the deferred deposit-path architecture decision.
- **Schema (v44 → v45):** only **WI-4** (S7) and **WI-10 C/D** (S8) touch the store — each needs CreateV1 +
  a `MigrateV44ToV45` with parity, a `DowngradeTo`, and an Io fold-in (mailing address/PIN; alternate
  units). Everything else is **v44-clean**.
- **A14 (R7) web-verification required before build:** **WI-13** (each Act-2025 rename + the TY 2026-27
  slab/SD/87A/surcharge rates — hard R7 law gate), **WI-9** (bare-letter/red/collision fidelity — NOT FOUND
  in the catalogue or the 10 PDFs), **WI-10** (Dozen/Nos rate semantics); lighter fidelity checks on WI-2
  (prefix-vs-substring), WI-5 (2-digit-year pivot), WI-11 (bare-Y/N accelerator), WI-12 (Add-vs-Insert).
- **Deliverables:** every CA point demonstrably addressed (or deferred-with-reason) — Alt+C
  create-on-the-fly, keyboard-navigable type-ahead pickers, the Alter verb, party mailing/PIN on printed
  invoices, canonical dates, reachable salary-TDS, a real accounting-Group master, multi-unit items, the
  Y/N accept prompt, and Day-Book Alt+A.
- **Exit gate:** R9 — every WI **done or explicitly deferred-with-reason** (recorded in `memory.md`, R6);
  tests green and **shown** (incl. Robert & Bright); **A10** three-lens adversarial review pass per slice;
  **A12** (GitHub Expert) commits & pushes small reviewed units (R4/R10); the real app run with evidence;
  `memory.md` updated; then **user go/no-go** per R12.

### Phase 11 — Hardening, packaging & release
- **Goals:** ship a v1.0.
- **Modules:** performance passes (NFR-4), end-to-end system/acceptance tests, docs completion (user manual,
  FAQ, troubleshooting, admin runbook, maintenance guide — deployment-docs-maintenance.md §2.5), installer/
  packaging, CHANGELOG, **v1.0.0 release**.
- **Agents:** Reviewer, run-app verifier, docs agent, **GitHub Expert** (release, tag, installer via CI/CD).
- **Deliverables:** signed installer(s), release notes, complete user/system docs, tagged **v1.0.0**.
- **Exit gate:** acceptance tests pass; user accepts (SAT-style, testing.md); R9 complete.

> **Justification of the arc:** it front-loads the **ledger engine** (everything projects over it), then
> layers **bill-wise/banking/cost** (still accounts-only) before **inventory**, so each new capability rests
> on a proven, regression-locked base. **GST MVP (Phase 4)** lands as early as the domain allows because it
> is the statutory centrepiece and the highest-risk fidelity work; GST breadth (Phase 9) waits until reports
> and advanced inventory exist to support it. Security/data-management (Phase 10) precedes release so the
> shipped product is safe by default. This mirrors the catalog §23 MVP-core vs Phase-2+ split.

---

## 6. Testing Strategy

Grounded in `testing.md` (levels, 7 principles, TDD, coverage limits) — R8.

### 6.1 Levels (run in order; each builds on the last)
1. **Unit** — every posting rule, valuation method, tax computation, statutory formula tested in isolation in
   `ledger-core` (AAA structure, float tolerances for money). Written **before** the code (TDD; R8).
2. **Regression** — the full unit suite re-runs on every change; **Robert & Bright are the standing
   ledger-engine regression baselines** (§6.3). Pesticide-paradox guard: add cases as bugs are found.
3. **Integration** — components wired together (voucher → ledger → report; inventory → accounts;
   GST masters → invoice → GSTR).
4. **System / validation** — black-box, keyboard-driven end-to-end flows through the real UI (Playwright):
   "create company → enter vouchers → view Balance Sheet", "raise GST invoice → check GSTR-1". One test per
   SRS requirement where feasible (testing.md "one test per requirement").
5. **Acceptance** — the user validates each phase at its gate (R9/R12); Phase 11 = SAT-style sign-off.

### 6.2 Method & discipline
- **TDD** (Red→Green→Refactor) is the default (superpowers:test-driven-development).
- **Black-box** for system/GST-return correctness; **white-box** for engine branch coverage.
- **Coverage** is a **floor and guide, not a target** (testing.md limits): chase meaningful paths/edge cases,
  set a threshold in Phase 0, gate on it in CI — but never treat % as proof of correctness (Principle 1).
- **Defensive tests** for the fail-fast boundaries (unbalanced voucher rejected, invalid GSTIN rejected).

### 6.3 The two deterministic fixtures (R8) — ledger-engine regression baselines
- **"Robert"** — transport business, **accounts-only, 13 deterministic vouchers**; exact expected Trial
  Balance / P&L / Balance Sheet totals. The primary engine smoke + regression test from Phase 1 onward.
- **"Bright"** — trading business: opening balances + depreciation + **closing stock**; exercises inventory-
  integrated valuation (re-verified in Phase 3). Both are committed as data + expected outputs and **must
  stay green in every subsequent phase** (any red = stop, per the R9/verification-before-completion lesson).

### 6.4 CI (testing.md "modernized" — required checks)
GitHub Actions runs lint + format + full test suite on **every push/PR**; **branch protection requires green
before merge** (the modern gated check-in). Bugs tracked as **GitHub Issues** with repro/expected/actual
(tools-and-databases.md §23). Managed exclusively by the **GitHub Expert** (R4).

---

## 7. Deployment & Release

All git/GitHub/CI/CD is the **GitHub Expert's exclusive domain** (R4), on
`https://github.com/Shuvrajit10101/Apex-Solutions`.

- **Environments** (deployment-docs-maintenance.md §1.4): **Development** (agent/dev machines) → **Test/CI**
  (clean GitHub Actions runners — the "clean environment") → **Release** (built installer artifacts). No
  separate production server (offline desktop app; the user's machine is production).
- **Versioning:** **Semantic Versioning** `MAJOR.MINOR.PATCH`; **schema version** tracked separately for
  SQLite migrations. Pre-release ladder where useful: Alpha → Beta → RC → **v1.0.0** (testing.md/
  deployment-docs-maintenance.md §1.2).
- **CI/CD:** GitHub Actions pipeline builds → tests → lints → (on tag) **packages the desktop installer**
  (Tauri bundler / electron-builder) and attaches it to a **GitHub Release** with **release notes/CHANGELOG**.
  Same artifact promoted; immutable builds; tag in Git (deployment-docs-maintenance.md §4).
- **Packaging:** signed Windows installer (primary); ship an **auto-updater**; keep the app self-contained
  (SQLite bundled, no external services) — matches NFR-1.
- **Branch/commit model:** small, conventional commits tied to plan items; feature branches → review-gated
  PR → merge to the default branch; tags for releases (R10; tools-and-databases.md §22 core Git flow).

---

## 8. Milestones & Gates

Each phase's gate = the **CLAUDE.md R9** sequence and its features meet the **R11** Definition of Done.

**"Done" per phase (R9 gate):** (1) tests green — **shown**, including Robert & Bright; (2) Code Reviewer
pass; (3) GitHub Expert has committed & pushed small reviewed commits; (4) the **real app run** and the
phase's flows exercised with evidence; (5) `memory.md` updated; (6) **user go-ahead** to proceed.

**"Done" per feature (R11):** behaviour + navigation + **keyboard shortcuts** match the catalog; unit +
integration tests written and green; reviewed; docs/user-notes updated; committed & pushed by the GitHub
Expert; `memory.md` updated. **No feature is "done" without running it and showing evidence** (R8;
superpowers:verification-before-completion).

**Milestones (headline):** M0 scaffold+stack-locked · **M1 ledger engine (Robert+Bright green)** · M2 bill-
wise/banking/cost · M3 inventory · **M4 GST MVP (GSTR-1/3B)** · M5 reports/print/export · M6 advanced
inventory · M7 TDS/TCS · M8 payroll · M9 GST-advanced · M10 security/data-mgmt · **M11 v1.0.0 release**.

---

## 9. Risks & Mitigations; Open Questions

### 9.1 Risks & mitigations
| # | Risk | Mitigation |
|---|---|---|
| R-1 | **Statutory law drift** (GST 2.0 slabs 5/18/40 unconfirmed; 206C(1H) status; PF/ESI ceilings) | A14 **web-verifies against official sources** before coding each statutory feature (R7); slab set is a config, not hardcoded; verification report §C flags tracked as Open Questions. |
| R-2 | **Fidelity gaps** vs real Tally (shortcuts, edge behaviours the PDFs garble) | Ground every feature in the catalog + `tally/` PDFs, cited (R7); A14 resolves doubt; catalog corrections (verification report) already folded in. |
| R-3 | **Ledger-engine correctness** (a green gate hiding real bugs — the recorded lesson) | Robert & Bright to the paisa + exhaustive unit tests + **adversarial verification** at every gate (superpowers:verification-before-completion); never trust a green gate blindly. |
| R-4 | **Scope creep** across 24 catalog sections | Phase gates + user go/no-go (R9/R12); backlog tied to plan items; no work outside plan.md without updating it (R6). |
| R-5 | **Keyboard-first single-window UX is hard to reproduce faithfully** | Build a central shortcut/focus manager early (Phase 1); system tests drive by keyboard (Playwright); design tokens enforce look-and-feel. |
| R-6 | **Offline constraint vs online-only Tally features** (Connected GST, IMS, live IRN) | Explicitly out of scope (§1.3); implement **offline JSON** paths only; revisit with user if online round-trips are wanted. |
| R-7 | **Data-loss / migration risk** (SQLite schema changes) | Versioned migrations from Phase 0; backup/restore (Phase 10) round-trip-tested; no destructive op without confirmation (NFR-8). |
| R-8 | **Third-party IP** (`tally/` PDFs) | Never committed — git-ignored (R4); referenced, never reproduced verbatim (recorded IP-leak lesson). |
| R-9 | **Agent/orchestration overhead** (main-loop bloat) | Token-lean main loop; delegate to agents; detail in memory.md/plan.md (R2/R14). |

### 9.2 Open questions for the user (surface at the Phase-0 gate — R12)

> **Resolved 2026-07-02 (user):** Q1 stack → **C#/.NET + Avalonia + SQLite**; Q5 OS → **Windows + Linux +
> macOS at v1.0**; Q7 fidelity → **pixel-level mimicry**; Q2 GST slabs → **config-driven, seed classic
> 0/5/12/18/28 now, add 5/18/40 after CBIC confirmation at Phase 4**. **Still open:** Q3 (206C(1H) status,
> decide at Phase 7), Q4 (online statutory round-trips — offline JSON only unless changed), Q6 (legacy
> VAT/CST/Excise scope — currently out).
1. **Stack confirmation (§3):** approve the proposed **TypeScript + Tauri(/Electron) + React + SQLite**
   baseline, or pick an alternative (.NET/Avalonia, Python/Qt, web/PWA)? *(Blocks Phase 0 completion.)*
2. **GST slab target:** build against the **legacy 0/5/12/18/28** set or the reported **GST 2.0 5/18/40**?
   (verification §C2 — needs an official CBIC confirmation regardless; slabs will be config-driven.)
3. **206C(1H) TCS on sale of goods:** model as **current** or **legacy/superseded by 194Q** for FY 2025-26?
   (verification §C4.)
4. **Online statutory round-trips:** confirm Connected-GST portal filing, live IRN/IRP, IMS live download,
   and WhatsApp sharing are **out of scope** (offline JSON only) — or should any be added later?
5. **Primary OS target:** Windows-only for v1.0, or must Linux/macOS ship at v1.0 too? (Affects §3 shell
   choice and packaging.)
6. **Legacy VAT/CST/Excise/Service Tax:** confirm **out of scope** (they remain in real Tally but are
   superseded) — any historical-fidelity need?
7. **"Faithful" bar:** pixel-level UI mimicry of Tally's screens, or behaviour/navigation/shortcut fidelity
   with a clean modern skin? (Affects UX effort in every phase.)

---

## 10. Coverage refinements (folded in from the plan critique — authoritative)

A completeness critic audited §§4–9 against the catalog. These refinements close the gaps it found; they
**refine, not replace**, the sections above and are binding.

- **C-1 Multi-currency (was omitted).** Add a **Currency** master (symbol, formal name, decimals,
  amount-in-words) + **Rates of Exchange** (std/selling/buying, dated) to the domain model; party/ledger
  "Currency of ledger"; voucher forex fields (Rate in Forex / Rate of Exchange / Rate in ₹); a **Forex
  Gain/Loss** ledger + period-end unrealized-forex adjustment. **Scheduled in Phase 2.**
- **C-2 Advanced-accounting features are Phase 2.** Explicitly schedule **Budgets, Scenarios, Reversing
  Journals, Memoranda, and Interest calculation** (in §1.2 scope but previously unassigned) into **Phase 2**
  alongside bill-wise / banking / cost / multi-currency.
- **C-3 Bill Settlement (Ctrl+B).** Phase 2 delivers not just ageing/Outstandings display but the **Settle
  Bill (Ctrl+B)** action (spacebar multi-select) from the Outstandings report — a testable requirement.
- **C-4 Party multi-address.** Add **Additional Contact/Address Details** (multiple billing/shipping
  addresses, selectable at Sales/Purchase entry) to the domain model §4.2; built in **Phase 1–2**, upstream
  of Phase-5 invoice printing.
- **C-5 TDS/TCS ancillary forms & exception reports (Phase 7).** Add **Form 27A** (control chart), **Form
  16A** (TDS certificate), **Form 27D** (TCS), and exception/outstanding reports (TDS Outstanding, Not
  Deducted, Late Deduction/Payment; TCS equivalents) to Phase 7 — not just 26Q/27EQ.
- **C-6 GST advanced (Phase 9) additions.** Explicitly include the **GST Rate Setup** bulk screen (mass
  HSN/rate update), **GSTR-9C** reconciliation-statement mechanics (separate from GSTR-9/9A), and clarify
  that the **IMS local accept/reject/pending** workflow over already-fetched GSTR-2A/2B data is **in scope
  (offline)** even though live IMS download is out.
- **C-7 Edit Log vs Tally Audit (Phase 10) are two deliverables.** **Edit Log** = field-level before/after
  on every master/voucher; **Tally Audit** = the reviewer's audit-summary report. Build both; don't conflate.
- **C-8 Composition interim limitation (Phase 4 gate note).** A company created as **Composition** is seeded
  in Phase 4 but has **no working composition tax path until Phase 9** — note this at the Phase-4 gate so it
  isn't mistaken for a defect.
- **C-9 Re-verify phase-critical law at each kickoff (amends §9.2 / R-1).** The **GST slab set** (Open Q2)
  is re-confirmed at **Phase 4** kickoff and the **206C(1H)** status (Open Q3) at **Phase 7** kickoff — not
  only at the Phase-0 gate — because law can drift across a multi-phase build (R7). Slabs stay config-driven.

---

*Change log: initial master plan drafted 2026-07-02 via `/software` from the study corpus; coverage
refinements §10 (C-1…C-9) folded in the same day from the plan critique. Any deviation during execution is
recorded in `memory.md` with its reason (R6).*
