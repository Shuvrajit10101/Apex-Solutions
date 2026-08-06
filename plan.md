# plan.md — Apex Solutions: Tally Prime Clone (Master Plan)

> **The single source of truth for building this project (CLAUDE.md R6).** Built via the `/software`
> skill from the study corpus. We execute **this** plan, in order, phase-gated. Minor deviations are
> allowed but must be logged in `memory.md` with a reason (R6). Requirements are grounded in
> `docs/tally-feature-catalog.md` (+ `…-verification-report.md`) and the `tally/` PDFs (R7).
>
> **Status:** APPROVED — build authorised by the user 2026-07-02. **Confirmed stack (§3): C# / .NET +
> Avalonia (cross-platform: Windows + Linux + macOS) + SQLite**, pixel-level UI fidelity, config-driven GST
> slabs. The domain model, phases, tests, and gates are stack-agnostic and unchanged.
> **Phases 0–9 and Phase 10.5 (CA-audit remediation, slices S1–S9) are COMPLETE and merged** (PRs #19–#25);
> the UI-defect campaign that followed is also merged (PRs #26–#33). `origin/main` = `c655dc2`, schema **v46**
> there. **Everything since lives UNPUSHED on `claude/confident-ellis-dedef5` — no PR, no upstream:**
> Phase 10.7 (voucher numbering S1–S5) and the service-invoicing work carried the schema **v46 → v49**;
> **Phase 10.8 is STOPPED AND BANKED** (2026-07-29, R12); and **Phase 10.9 — Tally-gap remediation is BUILT,
> reviewed and MERGED onto that branch** (2026-08-03 — five parallel streams, cross-stream interaction tests
> and the Tally version/voucher-entry audit). Suite **3651 tests green — Ledger 1281 · Io 361 · Sqlite 210 ·
> Desktop 1799**; build **0W/0E**; schema **v49** (Phase 10.9 is schema-clean). **Quote the FOUR numbers, never
> the total alone — a green total with the wrong per-project counts is a CONTAMINATED RUN, not a pass** (§6.2).
> **Phase 11 and the REST of Phase 10 — TallyVault, Security Control / roles, Edit Log / Tally Audit,
> split-by-FY, group company, repair/rewrite — remain EXCLUDED by standing user decision.**
> **`backup/restore` was CARVED OUT of Phase 10 and is BUILT** (user decision 2026-08-02) — this closes the
> contradiction where the plan named it as the mitigation for its own top-ranked data-loss risk **R-7** (§9.1)
> while placing it inside an excluded phase.
> **Current work: the Phase 10.9 R9 real-app run** (§5) — nine merged features have never been seen working
> outside a test harness.
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
- **▶ STATUS 2026-08-02 — EXCLUDED, WITH ONE CARVE-OUT (R12 USER DECISION). This bullet governs the reading
  of every bullet below.** The phase stays **EXCLUDED and unscheduled** — **TallyVault encryption, Security
  Control + user roles + password policy, Edit Log / Tally Audit, split-by-FY, group-company consolidation and
  repair/rewrite are NOT being built.** **`backup/restore` is the sole exception:** the user carved it out as a
  standalone slice and it is **BUILT, reviewed and merged** on `claude/confident-ellis-dedef5` (`e90a169`),
  specified under **Phase 10.9 / GAP-3**. *Why it was carved out:* **§9.1 R-7 — the plan's top-ranked
  data-loss risk — named backup/restore as its own mitigation while parking it inside an excluded phase**, so
  the mitigation for the highest-ranked risk did not exist. **That contradiction is now resolved** (R-7 amended
  in the same pass). Deciding record: `docs/tally-gap-decisions.md` **D12 = A** (and **D13 = leave excluded**).
- **Goals:** administration & data safety.
- **Modules (catalog §18, §19):** TallyVault encryption, Security Control + user roles + password policy,
  **Edit Log/Tally Audit**, backup/restore *(**DONE** — see the status bullet and Phase 10.9 / GAP-3)*,
  **split-by-FY**, **group company** consolidation, repair/rewrite.
- **Agents:** per-feature pipeline (+ security review; superpowers:security-review where relevant).
- **Deliverables:** encrypted company; role-gated access; lossless backup/restore *(**DELIVERED** by GAP-3,
  version-stamped against schema v49 with a restore round-trip test)*; a split & a consolidated
  group-company statement.
- **Exit gate:** R9; no secrets in repo (R13); audit trail verified. **Not applicable while the phase is
  excluded** — GAP-3 was gated under **Phase 10.9's** exit gate instead, not this one.

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

> **Phase 10.5 — WI-2 scope correction (recorded 2026-07-20, history above left intact).** WI-2 is recorded
> SHIPPED in slice **S5** (commit `43c8ea7`) and it is: the wrong-ledger `ToString` bug is fixed and the
> dropdowns are keyboard-navigable. But what S5 delivered for "type-ahead" is **type-to-JUMP** — the typed
> prefix moves the highlight. `GatewayColumn.TypeAhead` accumulates a prefix and then only calls
> `SetSelected`; it never touches `Items`. **No filtering infrastructure exists anywhere in `src/`.**
> Type-to-**FILTER** is therefore **new work**, scheduled as **KB-3** in Phase 10.6 below — not a re-run of
> WI-2, and WI-2's shipped status is not withdrawn.

### Phase 10.6 — Keyboard & input parity
- **Goals:** make the app keyboard-complete against NFR-2 (§1.4) — every screen driveable by arrows/Tab/Space
  including inside dropdowns, focus never lost below the fold, and pickers that **filter** as you type across
  **both** widget families. Closes the gap between what §1.2/NFR-2 promise and what S5 actually shipped.
- **Modules (catalog §21 keyboard surface; NFR-2):** the tunnel key handler, the ~199 `ComboBox` pickers, the
  cascade Miller-column navigator, and the focus/scroll behaviour of every form pane.
- **Settled contract (user-confirmed 2026-07-20 — R12 satisfied; do not re-litigate):** **R-KB1** arrows/Tab/
  Space work on every screen, dropdowns included, and Tab to a control below the fold brings it into view.
  **R-KB2** dropdowns filter by **PREFIX** as you type, the typed text is shown letter by letter, Backspace
  widens the match, matching runs against **Alias as well as Name**, case-insensitive, in **both** widget
  families. Also settled: **Space** is a literal character inside a filtering picker and activates elsewhere;
  **authored menu columns keep their bare-letter red hotkeys** (WI-9) and only **DataDriven** columns filter;
  repeated-letter **cycling is DROPPED**; **Escape is two presses** — the first clears the filter and leaves
  the list open, the second closes the dropdown, and neither ever also pops the Miller column; **F4 is
  Contra** and must never open a dropdown. The **15 `SelectedIndex`-bound `ListBox`es are DEFERRED**.
- **Work items (id — one-line):**
  - **KB-1** **Full keyboard navigation** — arrows / Tab / Space reach and operate every control on every
    screen, including inside an open dropdown; no control is mouse-only and no screen traps focus.
  - **KB-2** **Focus auto-scroll** — Tab to a control below the fold scrolls it into view. **MEASURED
    FINDING (this session):** the **Avalonia framework default already does this** in the real `MainWindow` —
    **40/40 Tab stops landed inside every ancestor clip** at both **1920×1080** and **1280×720**, with
    `Disabled` scrollers rewound before scoring. **No new auto-scroll component is required for ordinary form
    panes.** What remains **unsettled** is the **loaded case** — tax panels open, voucher lines added — which
    is not yet measured; KB-2 is therefore a measurement item, not a build item, until the loaded case says
    otherwise.
  - **KB-3** **Prefix type-to-filter pickers, both widget families** — implement the R-KB2 contract in the
    **~199 `ComboBox`** pickers **and** in the **cascade Miller columns**. The user has ruled **FULL PARITY**
    for the cascade family: it gets a **real character feed** of its own rather than a reduced behaviour.
    This is **NEW work** — see the WI-2 scope correction above; S5 shipped type-to-JUMP and there is no
    filtering code in `src/` to extend.
  - **KB-4** **Keystroke-arbitration defects** — the conflicts that ship today between the tunnel handler's
    first-match-wins ordering and the per-widget key handling (Space, Escape, F4, bare-letter vs typed
    character). Fixed now, under the arbitration rules settled above.
- **Build order (user decision 2026-07-20):** the **navigation slice (KB-1, KB-2, KB-4) ships now**; the
  **filter mechanism (KB-3) is preceded by a real-windowed measurement spike** before it is built, so the
  character-feed design for both families rests on measured behaviour rather than assumption.
- **Agents:** per-feature pipeline (§2.2) — Requirements/Design, **A14** (keyboard fidelity against the
  catalogue and the `tally/` PDFs, R7), Test author, Implementer, **A10** review, **A12** GitHub Expert,
  run-app verifier.
- **Deliverables:** a keyboard-only pass through master creation, voucher entry and a report drill-down with
  no mouse; the measurement spike's report for the loaded-pane auto-scroll case and for the character feed;
  prefix filtering demonstrable in both a `ComboBox` picker and a cascade column; regression tests locking the
  settled arbitration rules (Space, two-press Escape, F4-is-Contra, no cycling).
- **Exit gate:** R9 — tests green and **shown** (incl. Robert & Bright); A10 review pass; A12 commits and
  pushes small reviewed units (R4/R10); the real app run with keyboard-only evidence; `memory.md` updated;
  then **user go/no-go** per R12.

### Phase 10.7 — Voucher numbering
- **Goals:** ship per-voucher-type customizable **Voucher No.** for the existing **24 seeded voucher types** —
  **Prefix + Suffix** as **date-effective rows** (each value applies from its `ApplicableFrom` until the next
  row; separators live inside the affix text — there is **no implicit separator**), a numeric **Width**,
  **Prefill-with-zero**, and **Prevent-duplicate** — exposed through an **F12 numbering CONFIG screen** (this
  finally wires the Phase-1 **F12 Configure** stub). The rendered number `Prefix ++ leftPad(int, width) ++
  Suffix` becomes **THE document number at every site** (print, e-invoice / e-Way / B2C-QR, GSTR-1, registers,
  Day Book, POS, entry preview + accept toasts), while `Voucher.Number` stays the persisted **`int`** identity
  and sequence seed (the human number is an unpersisted projection over `(int, Date, the type's affix/width
  rows)`). Also add a captured **counterparty reference field** — **Reference No.** on sales /
  **Supplier Invoice No.** on purchase — a proper editable, persisted, printable field that **never** receives
  auto prefix/suffix/reset (it is the other party's number, distinct from the CDN `original_invoice_number`).
- **Explicitly DEFERRED (user decision 2026-07-21 — do NOT build here):** the financial-year **RESTART / reset**
  (no restart rows, no `PeriodOf`, no period-scoped `NextNumber`). *Rationale:* the bare `Voucher.Number` **IS**
  the statutory document number — the IRP `DocDtls.No` / e-Way `DocNo` and the e-invoice uniqueness key
  (`HasEInvoiceDocumentNumber`) — so a naive FY reset makes the `int` collide across years and **hard-blocks**
  the new-FY `#1` e-invoice. Deferring restart keeps the shipped `int` **strictly unique per type**
  (`NextNumber = max+1`), which is exactly what makes render-everywhere safe. When later built, FY-reset =
  **Tally-style MANUAL dated prefix rows** (user preference) paired with the document-number repoint this
  feature already ships — a separate, gated slice. Also deferred: the **5-method** `NumberingMethod` extension
  (stays **Automatic / Manual / None**) and the **Renumber / Retain** delete-behaviour toggle (today's only
  behaviour — Cancel keeps the number, Delete leaves a gap — is retained).
- **Modules:** the pure `VoucherNumberFormatter` (`Apex.Ledger` domain + `Services/`); the ~40 sites that today
  emit the bare `int`, repointed through one `Company.FormatVoucherNumber` policy incl.
  `DocumentNumberOf(company, voucher)`; the **second (inventory) posting engine** (`InventoryPostingService` —
  render + config + duplicate guard extended, allocation math unchanged at `max+1`); the schema / persistence /
  Io fold-in; the **F12 Miller-cascade config UI**; and the counterparty header field on `vouchers`.
- **R7 fidelity — web-verified (A14):** the spec was **web-verified against the official TallyPrime 7.1 docs
  (help.tallysolutions.com)** — Prefix/Suffix as tables of `{ApplicableFrom, Particulars}` with multiple
  date-ranged rows, separators inside `Particulars`, Width + Prefill-zero → `001`, Prevent-duplicate, scope
  per voucher type. The `tally/` PDF corpus was **screenshot-only on this feature**, so the official web docs
  are the authoritative R7 source of record here (record the citation in `memory.md`).
- **Work items (id — one-line):**
  - **VN-1** **Numbering engine** — the immutable `VoucherNumberAffix {Id, ApplicableFrom, Particulars}` value
    object, the new get-only ctor-injected `VoucherType` fields (`Prefixes` / `Suffixes` / `NumberWidth` /
    `PrefillWithZero` / `PreventDuplicate`, all defaulting empty/0/false), and the pure
    `VoucherNumberFormatter.Render` — date-selected affix (greatest `ApplicableFrom <= date`), `(ApplicableFrom,
    Id)` tie-break, **non-truncating** left-pad; empty-config + Width 0 returns exactly `int.ToString()`.
  - **VN-2** **Render-everywhere ONE policy** — repoint every bare-`int` render site to the formatter, incl.
    `DocumentNumberOf(company, voucher)` (e-invoice IRP / e-Way / B2C-QR / the GSTR-1 equality re-derivation),
    the print projector's **both** number fields, registers + Day Book via a row-carried `FormattedNumber`,
    Ledger Vouchers, POS, and the entry preview + accept toasts; cover **both** posting engines. Keep the raw
    `int` only for identity, `ORDER BY`, `max+1` allocation, and the separate RCM `SeriesNumber`.
  - **VN-3** **Prevent-duplicate guard** — compare the **fully-rendered** number (ordinal, case-sensitive)
    against non-deleted vouchers of the same type in `VoucherValidator` (so the Io import path inherits it),
    mirrored in `InventoryPostingService.Post`; the counterparty field is never run through it.
  - **VN-4** **Schema v47 + persistence + Io** — 3 scalar columns + `voucher_type_prefix` / `voucher_type_suffix`
    date-keyed child tables + indexes, `MigrateV46ToV47` (equivalence-parity) + a `DowngradeV47ToV46`,
    single-pass **ctor-injected** read of the child rows, the child **delete-clear** on Save, and a
    **conditional (omit-at-default)** Io round-trip + import mirror.
  - **VN-5** **F12 config UI + historical-stability guard** — `Screen.VoucherNumberingConfig` **pushed** by F12
    into the Miller cascade (prior panes persist; F12/Esc pops), the three cascade columns, the date-keyed
    affix editor (duplicate-`ApplicableFrom` **rejected** on commit), dropdown/keystroke-arbitration compliance
    (no regression of the `b8c617e` rules), inventory types configurable, and the guard that **blocks /
    warn-confirms** edits that re-project already-issued or filed documents.
  - **VN-6** **Counterparty reference field** — `Voucher.ReferenceNo` / `ReferenceDate` (a distinct header
    field, NOT a reuse of the CDN `original_invoice_number`), its schema columns, persistence, Io round-trip,
    entry capture (labelled per base type), and print surfacing.
- **Slices (build order — dependency order; full rationale in `memory.md`):**
  1. **S1 — Pure formatter** (VN-1) — **domain + `VoucherNumberFormatter`, no schema, no UI.** Files:
     `Domain/VoucherType.cs`, new `Domain/VoucherNumberAffix.cs`, new `Services/VoucherNumberFormatter.cs`.
     Low risk — nothing reaches posted vouchers yet.
  2. **S2 — Render-everywhere ONE policy + duplicate guard** (VN-2, VN-3) — **highest correctness;** repoint all
     ~40 bare-`int` sites incl. e-invoice / e-Way / B2C-QR / GSTR-1 / registers, add the rendered-duplicate
     guard to both post paths, and sync the entry preview to the posted number. Schema-clean (rules set
     in-memory by tests). Robert & Bright must not move. **Surfaces USER GATE (a) — digit-adjacent affix
     collision handling.**
  3. **S3 — Schema v47 + persistence + Io round-trip** (VN-4) — **highest discipline;** owns **v47** (see
     Schema below). ER-13 byte-identical for a never-configured type. Data-carrying only.
  4. **S4 — F12 config UI (Miller cascade) + historical-stability guard** (VN-5) — must not regress the
     `b8c617e` keystroke arbitration. **Surfaces USER GATE (b) — editing an affix that covers a GSTR-1-filed
     but not-e-invoiced voucher.**
  5. **S5 — Counterparty reference field** (VN-6) — independent; carries its own additive migration on
     `vouchers` (ordered after v47).
- **Schema:** numbering config is **v46 → v47** (S3: `prevent_duplicate` / `number_width` / `prefill_zero` on
  `voucher_types` + the `voucher_type_prefix` / `voucher_type_suffix` child tables + indexes, `MigrateV46ToV47`
  with `SchemaMigrationEquivalenceTests` parity and a `DowngradeV47ToV46`). The counterparty field (S5) rides
  its own additive migration on `vouchers` (`reference_no` / `reference_date`), ordered after v47 (design:
  **v47 → v48**). **Version coordination — numbering owns v47 and v48 (corrected 2026-07-27):** S5 shipped as
  **v48** (`636a104`) and the accounting-invoice flag then took **v49** (`5819fbf`), so the separately-planned
  **negative-stock** change — which this note previously told to "rebase to v48" — targets **v50**
  (**Phase 10.8**). S1/S2 are schema-clean; ER-13 stays byte-identical for a never-configured type via
  **conditional (omit-at-default) emit** — no golden regeneration.
- **User gates (recommend-first; surface at the named slice — R12):**
  - **(S2) Digit-adjacent affix collision handling** — when an affix's own digits abut the padded numeric core
    (e.g. suffix `2001` after core `00001` → `200100001`, or a prefix ending in a digit) the boundary reads
    ambiguously. *Recommend:* render **verbatim** (faithful to Tally — separators are part of `Particulars`,
    the operator's responsibility) plus a **config-time advisory** on a digit-adjacent affix; do not silently
    insert a separator.
  - **(S4) Editing an affix covering a GSTR-1-filed but not-e-invoiced voucher** — the stability guard already
    **blocks** edits that re-project a voucher carrying a generated e-invoice / e-Way (filed statutory doc) and
    **warn-confirms** for merely-posted-unfiled. The middle case — a voucher already inside a **filed GSTR-1**
    return but with **no e-invoice** — needs its own ruling. *Recommend:* **BLOCK** (treat GSTR-1 inclusion as a
    filed number, preserving the `Gstr1.cs:360` live-vs-frozen equality).
- **Agents:** per-feature pipeline (§2.2) — Requirements/Design (design of record in `memory.md`), **A14**
  (Tally fidelity, R7), Test author, Implementer, **A10** review, **A12** GitHub Expert, run-app verifier.
- **Deliverables:** a per-voucher-type numbering config reachable by **F12** on the 24 types; the printed /
  e-invoice / e-Way / QR / GSTR-1 / register / Day Book / POS number all equal to one rendered string; the
  Prevent-duplicate guard enforced on both create and import; the counterparty Reference / Supplier-Invoice
  number captured, persisted, round-tripped and printed; and regression tests locking empty-config == today
  (Robert & Bright unmoved), the two user-gate rulings, and the v47 migration parity + downgrade.
- **Exit gate:** R9 — tests green and **shown** (incl. Robert & Bright — they must not move under empty config);
  **A10** three-lens review per slice; **A12** (GitHub Expert) commits & pushes small reviewed units (R4/R10);
  the real app run with evidence (**F12 opens the numbering config**); `memory.md` updated; then **user
  go/no-go** per R12.

### Phase 10.8 — Allow negative stock
- **▶ STATUS 2026-07-29 — STOPPED AND BANKED (R12 USER DECISION). `S-A` is NOT DONE; `S-B` and `S-C` stay
  BLOCKED.** This status **governs the reading of every bullet below** — the goals, modules and deliverables
  remain the intent, but **none of the valuation work has landed**. The engine is **reverted to HEAD
  byte-for-byte** and the suite is back at the pre-session baseline **3491 green — Ledger 1261 · Io 359 ·
  Sqlite 184 · Desktop 1687** (build 0W/0E, schema
  **v49** — negative stock never reached its **v50**). **Eight attempts** (three in earlier sessions, five on
  `claude/confident-ellis-dedef5`) **each passed the FULL TEST SUITE; four also passed the ORACLE** and were
  then **convicted by adversarial review**. **What is BANKED:** the committed **HEAD-oracle harness**
  (`275c395`, extended since — **198 hand-derived goldens**, audit chain closed at **TRUSTWORTHY**, now
  correctly reporting **REJECTED against HEAD**), and a **precise specification of the unsolved prerequisite
  (`NS-8`)**. Full narrative, the **eight measured failure modes** and the reproducing books:
  **`tools/HeadOracle/README.md` (the HANDOVER DOCUMENT)** and the 2026-07-29 entry in `memory.md`.
- **▶ THE STRUCTURAL FINDING — why this stopped, and why a ninth scoping attempt must NOT be made: ANY
  PREDICATE-GATED SCOPE CREATES A VALUATION CLIFF AT ITS BOUNDARY.** The last candidate scoped the debt rule
  to **single-key items**. That scoping is **correct where it applies** and **provably inert where it is not**
  — a **20,736-row engine-vs-engine sweep**, key count computed **OUTSIDE the harness**, moves **0 of 15,552
  two-key rows**, and is **discriminating, not vacuous** (forcing the predicate true moves **3,712**). **But
  the predicate is a property of the BOOK, and an ordinary posting changes it:** **one ordinary internal
  godown transfer** — moving nothing in or out of the company — **flips an item's whole history between the
  two models**, **₹40,000,001.20 with the transfer vs ₹25,000,000.75 without**, **unbounded in the lot rate**,
  **surviving a same-day round trip** (Store B ends empty and the item stays on the phantom model for ever),
  and putting **₹15,000,000.45 between closing stock on 20 Apr and 25 Apr with NO ECONOMIC EVENT**. **HEAD is
  CONTINUOUS here (`jump=0.00`) — the discontinuity is CREATED by the change.** ⇒ **Scoping by any predicate
  (key count, method, item flag, date) is not the fix; it IS the failure mode.**
- **Goals:** allow negative stock **everywhere, by DEFAULT, globally** — a sale, a consumption, a
  **manufacturing journal** or a **stock journal** that over-draws an item **posts** instead of being
  rejected. Add one company-level **`WarnOnNegativeStock`** toggle (default **ON**, the domain default synced
  to `true`) that **only warns and never blocks** a posting, and make a negative — and a negative later
  recovered by an inward — **value at a reference rate, never silently at ₹0**. Today's engine both blocks
  the entry outright and, on recovery, values the item at a rate that never existed.
- **DEFERRAL REVERSED — `AverageCost` IS fixed in this phase (USER DECISION 2026-07-27):** an orchestrator
  decision earlier the same day **explicitly DEFERRED** the **`AverageCost`** valuation path under negative
  stock, holding it **byte-for-byte at HEAD**, on the stated rationale that *"HEAD's moving average is in band
  on every conservation trace measured"*. **That premise has been REFUTED by measurement — and the refutation
  is that it was a TAUTOLOGY:** the harness that "verified" it compared `AverageCost` against a reference
  implementation deliberately written to **ECHO HEAD's own averaging rule**, so `AverageCost` could never be
  shown wrong by it. An **adversarial audit** caught this. Two **independently written** implementations now
  agree — a C# reference inside the harness, and an out-of-band Python model validated **byte-for-byte against
  real HEAD output on 95/95 subjects**:
  - On `In 5 @ ₹1000.07 → Out 25 → In 20 @ ₹1000.07 → In 30 @ ₹0.37`, HEAD's `AverageCost` closes at
    **₹12,007.50** where debt-aware closes at **₹11.10** — **₹11,996.40 of phantom asset**, roughly **1000×
    the true closing value**, on an item with **₹25,012.85** ever spent.
  - **No check catches it:** the implied **₹400.25** sits inside the rate band `[₹0.37, ₹1000.07]`, and
    **₹12,007.50** is under total spend. **6 of 95** negative-family subjects disagree with the debt-aware
    oracle.
  - *Honest qualifier, recorded rather than buried:* per family, `AverageCost`'s **COGS-conservation
    violations are never worse than FIFO's**. That is the grain of truth in the original claim. But *"no worse
    than the method we already know is broken"* is **not** "in band", and is **not** a basis for shipping.
  - `AverageCost` is the **DEFAULT valuation method for a new stock item**, and today the posting guard makes
    negative stock unreachable — **S-B removes that guard, so this exposure goes live.**
  - ⚠️ **The historical warning STANDS:** the one previous attempt that changed FIFO/LIFO **and** the average
    together was **unboundedly wrong** — sell 1,000 with nothing on hand, then buy 1,001 @ ₹100 ⇒ the
    surviving 1 unit valued at **₹100,100** where HEAD and FIFO both give ₹100; the mirror case valued 20
    units of genuine ₹240 stock at **₹0.00**. **Why this is now different:** those attempts ran **blind**.
    The harness now carries a **calibrated point oracle with a proven-reachable ACCEPT state** that would
    **convict a wrong average** — precisely what the echo-reference could not do. The average is therefore
    built **oracle-gated**, not by inspection.
- **Modules:** `StockValuationService` — **all three valuation paths, not just the layer ones**: the
  lot machinery (`Consume` / `LayerValue` / `BuildLayers`) **and** the moving-average path (`RunAverage` /
  `AverageValue`), since the debt quantity now binds FIFO, LIFO **and** `AverageCost` alike;
  `InventoryPostingService` (the guard — `EnsureNoNegativeStockAnywhere` and its public wrapper); `Company`
  (domain) + `Apex.Persistence.Sqlite`'s `Schema` + `Apex.Ledger.Io` (the new flag); `JobWorkService` and the
  manufacturing-journal consumption path; plus a new **committed HEAD-oracle harness** under
  `tools/HeadOracle/`.
- **R7 fidelity — web-verified (A14):** negatives are **allowed by default** in Tally, with an optional
  **non-blocking F12 warning** and **no per-item allow flag**. This verification **FALSIFIED an earlier
  recommendation** of a per-item allow flag on the stock item: the control is **company-level and advisory
  only**, which is exactly why this phase ships allow-by-default plus one warn toggle. **⚠️ The finding is
  carried forward from a previous session and NO CITATION IS ON RECORD.** A14 must **re-verify and produce
  the actual source** at S-B, before the toggle and its default ship — this project has been bitten
  repeatedly by documentation that turned out to be a claim rather than a fact, and an uncited default is
  exactly that.
- **Work items (id — one-line):**
  - **NS-1** **Over-draw as a debt quantity — across FIFO, LIFO *and* the moving average** — `Consume`
    silently **discards** whatever it cannot draw and `LayerValue` **ignores real on-hand** (`_ = closingQty`),
    so a recovered negative is valued at a rate that never existed: buy 10 @ ₹10 → oversell 15 → buy 20 @ ₹12
    values 15 units at **₹240** (implied ₹16/u) instead of **₹180** — **+₹60 straight onto Balance-Sheet
    Stock-in-Hand and P&L**. Carry the over-draw as a **debt quantity**; a later inward **repays it at the
    incoming lot rate**. **Scope now includes `AverageCost`** (per the decision reversal above): the moving
    average carries the same debt and repays it on the same rule, so a recovered negative no longer closes at
    **₹12,007.50** against a true **₹11.10**. Two invariants bind **all three** methods: **(i) an existing debt
    is NEVER re-rated** — re-rating the balance is what produced a measured **18× overstatement**
    (Stock-in-Hand ₹24,050 → ₹476,000 on an item with ₹26,100 ever spent); and **(ii) NO FLOORS AND NO CLAMPS**
    — a wrong value is never papered over by pinning it at ₹0, at cost, or inside a band. Repayment semantics
    differ per method and are proven **one method and one scenario at a time** against the oracle (NS-7).
    **⚠️ SUPERSEDED 2026-07-29 — NS-1 IS NOT BUILDABLE AS WRITTEN; it is now GATED BEHIND `NS-8`.** Five
    attempts to land exactly this item were reverted. The two invariants above (**never re-rate a debt**, **no
    floors and no clamps**) **still hold and are still right** — what NS-1 got wrong is its **implicit
    assumption that a debt can be carried at ITEM level**. It cannot: valuation is item-level while quantity
    is **per (item, godown, batch) key**, so an item-level debt is repaid by an inward into **a godown that
    never owed anything**. Repointing NS-1 at the per-key model **without** cost-flowing transfers **broke
    ordinary transfers** (₹5,000,002.37 of Stock-in-Hand on ₹1,000,003.73 ever spent, where HEAD was exactly
    right). **NS-1 is therefore re-sequenced AFTER `NS-8`, not deleted** — the debt rule is still the fix;
    it simply has nowhere correct to live until the key model is fixed underneath it.
  - **NS-2** **HEAD-oracle harness (`tools/HeadOracle/`) — built FIRST, before any production change** — two
    processes, two private engine copies, one corpus; diffs `ClosingValue` / `TotalClosingStockValue` /
    `IssueValue`, plus an engine-independent **rate-band**, **total-spend-containment** and
    **COGS-conservation** check. Runs on **every** change. It is **not a member of `Apex.slnx`**, so it stays
    invisible to the gate and to CI.
  - **NS-3** **Guard becomes a non-throwing detector** — `EnsureNoNegativeStockAnywhere` stops throwing,
    postings **always persist**, and one **flag-gated `WarningsFor`** surface reports the negatives. Note that
    the **three internal call sites call the private method directly**, so a gate placed only in the public
    `EnsureNoNegativeStock` wrapper would be **bypassed**.
  - **NS-4** **`WarnOnNegativeStock` + schema v50 + Io** — the `Company` column, `CreateV1` +
    `MigrateV49ToV50` **parity**, a downgrade helper, **JSON + XML lossless** round-trip, and **ER-13
    byte-identical** while the flag sits at its default.
  - **NS-5** **Test-flip inventory + import-test rework** — every test that depends on the guard **THROWING**
    is re-pointed. `CompanyImportRoundTripTests.Failed_apply_…` needs **real rework**: its rollback trigger
    **IS** the guard being removed — use a **stock-journal imbalance** instead.
  - **NS-6** **Manufacturing + job-work shortfall costing** — `JobWorkService` shortfall costing currently
    **loses money on a location transfer**; the finished good must absorb **exactly** what the component's
    stock loses.
  - **NS-7** **Harness check INVERSION — `AverageCost` becomes a point-oracle subject** — the direct
    consequence of the decision reversal. The harness check that asserts **`AverageCost` byte-identity to
    HEAD** now **FORBIDS the very fix this phase must ship**, so it is **inverted**: `AverageCost` stops being
    a frozen-to-HEAD control and becomes a **first-class point-oracle subject**, compared against the
    **independent debt-aware reference** exactly as FIFO/LIFO are. Additionally — and binding on every future
    harness — **any part of a reference that merely ECHOES HEAD must be LABELLED as carrying no correctness
    evidence**: an echo can only prove *"unchanged"*, never *"right"*, and presenting it as verification is
    what produced the refuted deferral above. Agreement between a reference and HEAD counts as evidence only
    where the reference was derived **independently** of HEAD's implementation.
  - **NS-8** **THE VALUATION PREREQUISITE — per-key valuation + cost-flowing stock-journal transfers (added
    2026-07-29; BLOCKS NS-1, and therefore blocks S-A, S-B and S-C).** The output of eight measured attempts.
    **Two requirements, BOTH load-bearing, built TOGETHER:** **(i) value stock on the SAME (item, godown,
    batch) key as QUANTITY** — today valuation is **item-level** while quantity is **per-key**, and that
    desync is itself a measured defect (it made the FIFO recovery case **13.5× worse**, ₹240.00 → ₹3,243.90 on
    10 units); and **(ii) make a Stock-Journal TRANSFER CARRY ITS COST LAYERS between keys** instead of
    **re-deriving them at the destination**. **⚠️ (i) WITHOUT (ii) IS A REGRESSION, NOT A PARTIAL FIX** — it
    was tried and it **broke ordinary transfers**: **₹5,000,002.37** of Stock-in-Hand on **₹1,000,003.73** ever
    spent, **where HEAD was EXACTLY RIGHT**. **Do not attempt them separately, and do not scope either by a
    predicate** (see THE STRUCTURAL FINDING above). Two **pre-existing HEAD defects** surface here and are to
    be settled as part of NS-8, not before it: **`StockValuationService.MovementEvents` SKIPS
    `Allocations`/`DestinationAllocations` on a Physical-Stock-typed voucher** (the `continue;` at
    **`StockValuationService.cs:180`**, block **:175-181**) **while `InventoryLedger.ApplyToKey`
    (`InventoryLedger.cs:193-207`) applies them for EVERY voucher type**, so quantity and value read the same
    voucher differently; and the **item-level/per-key desync** itself. **Also binding on NS-8:** the debt gate
    must use the product's **per-DATE** definition of negative stock, not a **per-EVENT** one — the per-event
    gate let **one same-day dip in an UNRELATED THIRD GODOWN** take a valuation from **₹237.30 to ₹79.10**;
    and the cost chain must **never look FORWARD** — a physical count priced by a *later* purchase produced
    **₹9,000,000.27** on **9 units** against **₹1,001,001.33** ever spent. **All eight failure modes, with
    reproducing books, are in `tools/HeadOracle/README.md` — a ninth attempt that has not read them will
    rediscover them one at a time.**
- **Slices (build order — dependency order; full rationale in `memory.md`):**
  1. **S-A — HEAD oracle, its inversion, then recovery on ALL THREE methods** (NS-2 → NS-7 → NS-1) —
     **harness first**, then **invert the `AverageCost` byte-identity check** so the fix is provable rather
     than forbidden, then the debt quantity across **FIFO, LIFO and the moving average**, **one method and one
     scenario at a time**. Schema-clean. **USER GATE (a) is RESOLVED** — see below; no gate remains on this
     slice.
     **▶ STATUS 2026-07-29 — S-A is NOT DONE, and is RE-SEQUENCED to `NS-2` → `NS-7` → `NS-8` (NEW) → `NS-1`.**
     **DONE:** **NS-2** (the harness — committed `275c395`, extended since, audit chain closed at
     **TRUSTWORTHY**) and **NS-7** (the inversion — `AverageCost` is now a point-oracle subject and HEAD is
     convicted). **NOT DONE:** **NS-1**, five reverted attempts, now blocked on the newly-specified **NS-8**.
     The engine is **byte-identical to HEAD**. **The harness-trustworthiness precondition in the Exit gate is
     SATISFIED; the valuation prerequisite is NOT.**
  2. **S-B — Guard, flag, schema and the test flip** (NS-3, NS-4, NS-5) — the slice that actually makes a
     negative postable; owns **v50** (see Schema below). **Surfaces USER GATE (b) — the `WarnOnNegativeStock`
     default.**
     **▶ STATUS 2026-07-29 — BLOCKED on `NS-8`/`NS-1`, and the ordering is a SAFETY PROPERTY, not a
     convenience.** Today's posting guard is the only thing making negative stock **unreachable**; S-B
     **removes that guard**, so shipping S-B ahead of a correct valuation would take the measured
     Balance-Sheet errors from **theoretical to LIVE** on the `AverageCost` default. **Do not reorder.**
  3. **S-C — Manufacturing + job-work shortfall costing** (NS-6) — independent of the flag; rides on S-A's
     valuation.
     **▶ STATUS 2026-07-29 — BLOCKED.** It rides on S-A's valuation, and S-A has none.
- **Schema:** negative stock is **v49 → v50**, owned by **S-B** (the `Company` warn column, `CreateV1` +
  `MigrateV49ToV50` with `SchemaMigrationEquivalenceTests` parity and a `DowngradeV50ToV49`). **This corrects
  the stale coordination note in Phase 10.7**, which told negative stock to "rebase to **v48**": **v48** went
  to numbering **S5** (`636a104`) and **v49** to the accounting-invoice flag (`5819fbf`), and `Schema.cs:124`
  now reads `CurrentVersion = 49` — so negative stock targets **v50**. S-A is schema-clean; ER-13 must stay
  byte-identical while the flag is at its default (the emit mechanism is S-B's to choose and prove, not
  prescribed here). **⚠️ DEFAULT-TRUE ASYMMETRY — the trap in this slice:** every existing company flag
  (`UseSeparateActualBilledQuantity`, `PayrollEnabled`, `EnableJobOrderProcessing`) defaults to **false**, so
  a missing column / absent JSON attribute / absent XML attribute all coincide with the default.
  `WarnOnNegativeStock` defaults to **true**, so absence and default **no longer coincide**: the SQLite
  column needs `DEFAULT 1`, and an importer that reads a missing attribute as `false` would **silently flip
  an upgraded book's warnings off**. Every read path (`Bool(...)` in `CanonicalXml`, the JSON DTO's default,
  the migration's backfill, the downgrade round-trip) must be proven to yield **true** for data written
  before v50 — with a test that fails if it does not.
- **User gates (recommend-first; surface at the named slice — R12):**
  - **(S-A) ✅ RESOLVED 2026-07-27 — ship recovery on FIFO, LIFO *and* `AverageCost`.** The gate asked whether
    to ship **FIFO/LIFO-only** recovery, leaving `AverageCost` — the **DEFAULT method for a new stock item** —
    at HEAD behaviour; the standing recommendation was **YES**, resting on the claim that HEAD's moving average
    is **in band**. **Evidence overturned the recommendation:** that claim came from a harness whose reference
    **echoed HEAD's own averaging rule** (a tautology, caught by an adversarial audit); two independently
    written implementations — one validated **byte-for-byte against HEAD on 95/95 subjects** — put HEAD's
    close at **₹12,007.50** against a debt-aware **₹11.10** (**₹11,996.40 phantom, ~1000×**), inside every
    existing band and spend check, with **6 of 95** negative-family subjects disagreeing. **USER DECISION:
    fix `AverageCost` too, oracle-gated** (NS-1 scope widened, NS-7 inverts the blocking check). **Kept on
    record, not deleted**, because the reversal — and *why* the original evidence was worthless — is the
    reusable lesson.
  - **(S-B) The `WarnOnNegativeStock` default.** *Recommend:* **ON** — Tally-faithful (the warning exists but
    **never blocks**) and the safer default for an existing book that has never been able to go negative
    before. **STILL OPEN** — S-B has not started.
  - **▶ FURTHER USER RULINGS RECORDED 2026-07-27 / 2026-07-29 (R12) — recorded here WITH DATES because the
    harness depends on them and nothing else in the repo evidences them:**
    - **✅ (2026-07-27) Keep auditing the harness until an INDEPENDENT adversary returns TRUSTWORTHY** — both
      that a reference-conformant engine is **ACCEPTED** and that **distinct wrong engines are REJECTED**.
      **NOW SATISFIED:** the chain closed **NOT-READY → NOT-READY → TRUSTWORTHY-WITH-GAPS → NOT-READY →
      TRUSTWORTHY-WITH-GAPS → TRUSTWORTHY**, over **six build rounds**, with **198 hand-derived golden
      constants (133 closing + 65 issue) independently re-derived by TWO reviewers**.
    - **✅ (2026-07-27) Shortfall valuation uses the engine's EXISTING best-available-cost chain**
      (`CostContext.NoRateInwardCost`) — deliberately **no new policy**; it is the rule HEAD already applies
      to any unrated inward. **Several of the harness goldens are valid ONLY under this ruling.**
    - **⚠️ (2026-07-27, REVERSED ON EVIDENCE 2026-07-29) Fix the desync PER-KEY and report what moved.**
      Per-key **broke ordinary transfers** (₹5,000,002.37 on ₹1,000,003.73 ever spent, HEAD exactly right), so
      the ruling was withdrawn. **BOTH the ruling and its reversal were right when made** — per-key *is*
      required (it is `NS-8` requirement (i)); what the original ruling could not know is that it is **inert,
      and actively harmful, without cost-flowing transfers** (requirement (ii)). **Kept on record, not
      deleted:** the reversal *is* the reusable lesson.
    - **🔴 (2026-07-29) STOP AND BANK rather than attempt a NINTH scoping.** The decision that halts this
      phase's current line of work: **revert the engine, keep the harness and the findings.** **Resuming
      requires a fresh R12 go-ahead** — and the thing to resume with is **`NS-8`**, not another scoping.
- **Agents:** per-feature pipeline (§2.2) — Requirements/Design (design of record in `memory.md`), **A14**
  (Tally fidelity, R7), Test author, Implementer, **A10** review, **A12** GitHub Expert, run-app verifier.
- **Deliverables:** an over-drawing sale, consumption, manufacturing journal and stock journal that all
  **post**; a non-blocking negative-stock warning surface gated by `WarnOnNegativeStock`; the committed
  `tools/HeadOracle/` harness with its rate-band / spend-containment / COGS-conservation checks **and its
  NS-7 inversion** (`AverageCost` compared against the **independent debt-aware reference** rather than
  byte-locked to HEAD, with any echo-of-HEAD reference labelled as carrying no correctness evidence); the v50
  migration parity + downgrade; and regression tests locking the FIFO **₹180** recovery case, the
  **`AverageCost`** recovery case pinning **₹11.10** against HEAD's **₹12,007.50** on
  `In 5 @ ₹1000.07 → Out 25 → In 20 @ ₹1000.07 → In 30 @ ₹0.37`, the
  **never-re-rate-an-existing-debt** rule (**binding on all three methods**), the flipped guard tests, the
  reworked import-rollback test and the job-work shortfall conservation (Robert & Bright unmoved).
- **Exit gate:** R9 — **HARNESS TRUSTWORTHINESS FIRST (user decision 2026-07-27): an INDEPENDENT ADVERSARY
  must return TRUSTWORTHY on the harness BEFORE any production code is written**, verifying **both** that a
  reference-conformant engine is **ACCEPTED** (the ACCEPT state is reachable, not vacuous) **and** that
  **several distinct wrong engines are REJECTED** — a harness that cannot convict a wrong average is what
  produced the refuted deferral, and no valuation change ships behind an unaudited oracle. Then: tests green
  and **shown** (incl. Robert & Bright — they must not move); **A10** three-lens review per slice; **A12**
  (GitHub Expert) commits & pushes small reviewed units (R4/R10); the real app run with evidence (**a sale
  that over-draws stock now posts, warns, and values correctly on recovery — on the `AverageCost` default as
  well as FIFO/LIFO**); `memory.md` updated; then **user go/no-go** per R12.
  - **▶ EXIT-GATE STATUS 2026-07-29.** The **harness-trustworthiness precondition is SATISFIED** (independent
    adversary returned **TRUSTWORTHY**; a reference-conformant engine is **ACCEPTED** and **several distinct
    wrong engines are REJECTED**) — **that precondition is now spent, and it did its job: it convicted every
    one of the eight attempts before any of them shipped.** **Nothing else in the gate is met.** **Two
    additions, both learned by measurement, bind any future attempt:**
    - **CONTINUITY IS A GATE CONDITION, NOT A REVIEW OPINION.** No valuation change ships until it is shown
      that **an ordinary internal godown transfer does not move an item's history** — with **HEAD's `jump=0.00`
      as the reference**, and with the key count computed **OUTSIDE** the harness so the check cannot be graded
      by its own predicate.
    - **THE FULL SUITE IS NOT EVIDENCE HERE.** **Eight of eight** attempts passed it; **four of eight** also
      passed the oracle. **"Green" is a floor for this phase, never a verdict** — the verdict comes from the
      oracle **plus** an adversarial design review that is **shown the eight failure modes first**.

### Phase 10.9 — Tally-gap remediation: voucher entry, voucher-type reachability, cost sets, batches & backup
- **▶ STATUS 2026-08-03 — BUILT, REVIEWED AND MERGED on `claude/confident-ellis-dedef5`; the R9 real-app run
  is the ONLY outstanding gate item.** Twelve commits above `bc95728`: five feature streams (`aed9a50`,
  `7bfc2c6`, `e90a169`, `828fc9f`, `374b221`), their five merges, the cross-stream interaction tests
  (`688ccd2`) and the Tally audit (`6124a25`). Suite **3651 green — Ledger 1281 · Io 361 · Sqlite 210 ·
  Desktop 1799** (confirmed **identical across three separate runs** — one `--blame-crash`, two
  solution-level); build **0W/0E**; schema **v49 UNCHANGED** (this phase is schema-clean end to end).
  **NOT pushed, NO PR, no upstream** — A12 has not run (R4).
- **Goals:** close the defects the 2026-08-01 Tally version/voucher-entry audit found — the ones that **cost
  money or hide it**. In one sentence each: cost centres could not be allocated along more than one
  **category**; two of the 24 voucher types had **no menu row at all** and a third advertised a **dead key**;
  a normally-invoicing company had an **empty Receivables report** because neither invoice Accept path built
  bill allocations; batch allocation was a **free-text string** on the two screens where batched goods are
  actually bought and sold; and there was **no backup or restore** while §9.1 named it as the mitigation for
  the top-ranked data-loss risk. Grounding documents, all committed in `6124a25`:
  **`docs/tally-version-and-voucher-gap-audit.md`** (what the audit found, cited),
  **`docs/voucher-entry-specification.md`** (TallyPrime's four-layer field gating: F11 capability →
  F12-on-master visibility → the master's own value → F12-on-voucher visibility, a field appearing only when
  **all four** permit it) and **`docs/tally-gap-decisions.md`** (the 24-question decision set, **D1…D24**).
- **Modules:** `VoucherValidator` + the cost-allocation rehydration path and the cost reports;
  `MainWindowViewModel` voucher-type menu rows / shortcut table / type resolution and the six call sites that
  posted by base type; a new backup–restore service over the SQLite store (version-stamped, with the file-swap
  and destination guards); `VoucherEntryViewModel` — the item- and accounting-invoice Accept paths, Single
  Entry on Contra/Payment/Receipt, Purchase Accounting Invoice and its TDS/RCM detection over the Particulars
  lines; and the existing batch sub-screen, re-wired to item-invoice line entry.
- **R7 fidelity — corpus- and web-verified (A14):** cost allocation follows the **Study Guide's own worked
  example** (₹5,000 travel booked to **Branch → Kolkata AND Department → Marketing** simultaneously,
  `[CORPUS-SG pp.101-102]`) — TallyPrime allocates the same amount along **each category independently**, so
  a cost allocation is a **set of parallel per-category allocations, not a partition of the line**. Physical
  Stock's real shortcut is **Ctrl+F7**; **official TallyPrime help assigns F10 to "view list of all
  vouchers"**, which is what the app was advertising. Credit/Debit Note entry modes and the four-layer field
  gating are corpus-sourced in `docs/voucher-entry-specification.md`.
- **Work items (id — one-line; the branch names each was built on are recorded for traceability):**
  - **GAP-1** **Voucher-entry core** (`claude/gap-1` → `828fc9f`) — **bill-wise allocations on BOTH invoice
    Accept paths** (item- and accounting-invoice), **Single Entry mode** on Contra / Payment / Receipt
    (Receipt/Contra **Account = Dr**, Payment **Account = Cr**), and **Purchase Accounting Invoice** enabled
    with TDS/RCM detection reading the **Particulars** lines it was previously blind to. Decisions **D4**,
    **D5**, **D8**.
  - **GAP-2** **Cost centres as parallel sets** (`claude/gap-2` → `aed9a50`) — enforce the allocation total
    **per category** instead of summing across all categories, plus **legacy-book rehydration** so vouchers
    saved under the old rule still load, and corrected cost-report labelling.
  - **GAP-3** **Backup and restore** (`claude/gap-3` → `e90a169`) — **the Phase-10 carve-out** (D12 = A).
    Version-stamped against schema **v49** and **refuses a restore the running build cannot handle**;
    destination guard so a backup cannot silently overwrite a live company database; connection disposal on a
    failed store construction; **restore round-trip test**.
  - **GAP-4** **Voucher-type reachability** (`claude/gap-4` → `7bfc2c6`) — **menu rows for Credit Note and
    Debit Note** (D9), **Physical Stock repointed from the dead F10 to Ctrl+F7** (D7), and voucher-type
    selection **resolved by identity** instead of `?? FirstOrDefault(BaseType == x)` — which silently opened
    **deactivated** types and made a second Sales series unreachable — with a clear message when no active
    type exists; the sixth unconverted call site (bill settlement) converted.
  - **GAP-5** **Real batch allocation on the item-invoice screens** (`claude/gap-5` → `374b221`) — the
    existing batch sub-screen re-wired to item-invoice line entry (picker, available balance, expiry capture,
    split across batches, reconciliation), **gated so an item that does not maintain batches behaves exactly
    as before**; **stock valuation deliberately untouched and byte-identical**.
  - **GAP-6** **Cross-stream interaction tests** (`688ccd2`) — 12 tests, odd paisa throughout, covering what
    **no single stream could test because until the merge those combinations did not exist**: a batch-split
    line and a two-reference bill split reconciling to the same total (and the refusal at one paisa of
    difference); short-billing moving the bill-wise target onto the **billed** basis; bill-wise on a
    TDS-carved purchase accounting invoice; an invoice under a **second, non-predefined Sales type** carrying
    both behaviours, and a **deactivated** type being neither listed nor openable; a voucher carrying both a
    parallel cost set and a bill split; and a **backup round-trip carrying all of it**.
- **Slices (AS BUILT — this is a record, not a forward plan):** each stream was built in an **isolated
  worktree cut from `bc95728`**, individually gated, **adversarially reviewed, fixed**, then merged **one at a
  time with a full gate after each merge**. **Predicted-then-observed, four exact PER-PROJECT predictions in a
  row** — recorded as all four counts, per §6.2, because the total alone cannot detect a truncated run:

  | after | commit | Ledger | Io | Sqlite | Desktop | total |
  |---|---|---|---|---|---|---|
  | base | `bc95728` | 1261 | 359 | 184 | 1687 | **3491** |
  | GAP-2 + GAP-4 | `014f3ca` | 1281 | 361 | 188 | 1718 | **3548** |
  | GAP-3 + GAP-1 | `6580da9` | 1281 | 361 | 210 | 1767 | **3619** |
  | GAP-5 | `9688235` | 1281 | 361 | 210 | 1787 | **3639** |
  | interaction tests | `688ccd2` | 1281 | 361 | 210 | 1799 | **3651** |

  The final row is **confirmed identical across three separate runs** — one `--blame-crash`, two
  solution-level. **One conflict, hand-resolved and gated before commit** — `VoucherEntryViewModel.cs`,
  where GAP-1's 227-line bill-wise block and GAP-5's 75-line batch block both inserted immediately after the
  `InventoryLines` declaration (add/add at one point). **A plain concatenation would have been malformed:**
  each side ended mid-method and the single brace below the conflict could only close whichever side landed
  last, leaving `InvoiceBillAllocationsOk` unterminated with every batch member parsed as a local inside it.
  Resolution kept both plus one brace at the seam; whole-file balance verified **584/584**.
- **▶ WHAT THE ADVERSARIAL REVIEWS CAUGHT — pre-commit, and the justification for the per-stream review gate
  (R10):**
  - **GAP-1 — CRITICAL.** A **reverse-charge purchase credited the supplier ₹15,777.78 of tax it never
    charged and claimed input GST TWICE**, on a voucher that **balanced perfectly**, so nothing caught it.
  - **GAP-3.** A restore failing at the swap had **already deleted the target's `-wal` / `-shm` / `-journal`**,
    leaving the live company **unrecoverable**. The original is now never degraded until the replacement is
    verified in place.
  - **GAP-5 — two money defects.** Splitting a line across batches **changed the posted invoice total** and
    the party-ledger amount; and short-billing computed the live total **and the GST/TCS base** on the
    **Actual** rather than the **Billed** quantity.
- **Schema:** **NONE — v49 throughout.** Every stream is schema-clean; backup/restore **reads** the version
  stamp rather than adding one. (Negative stock still owns the unclaimed **v50** — Phase 10.8.)
- **User decisions (R12) — recorded with dates, because nothing else in the repo evidences them:**
  1. **(2026-08-01) TallyPrime is the fidelity target; Tally 7.2 is a CHECKLIST ONLY.** Settles
     `docs/tally-gap-decisions.md` **D1**. The user evaluates against a 2005 product roughly five product
     generations behind the corpus, and **all ten `tally/` PDFs are TallyPrime documents — there is no 7.2,
     Tally 9 or ERP 9 primary material at all**. 7.2 feedback is triaged through the audit's known
     divergences before anything is logged as a defect; things the user "knows" Tally does (Ctrl+V / Alt+I as
     separate mode keys, Credit Note on Ctrl+F8, the 1990s menu tree) are **7.2 behaviours TallyPrime
     deliberately removed**, and Apex is correct not to have them. **The installed 7.2 copy is out of bounds
     and was not opened, listed or launched** for the audit or the decision set.
  2. **(2026-08-01) OPTIMISE FOR COMPLETENESS OF VOUCHER ENTRY.** The prioritisation rule for this phase and
     the next: voucher entry is where the audit found the defects that cost money, so breadth-of-entry beats
     polish elsewhere. This is why **GAP-1 and GAP-5** were scheduled ahead of report and master work.
  3. **(2026-08-02) Goods-return STOCK PARITY on Credit / Debit Note is APPROVED — but BEHIND AN ORACLE, and
     it is NOT YET BUILT.** Settles **D3**. **`ItemInvoiceStock.Counts()` still ends with
     `type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales`**, so a Credit Note **cannot carry
     inventory lines at all** — a sales return credits the customer but leaves the goods off the books, a
     purchase return debits the supplier but leaves phantom goods on hand, and **the drift compounds with
     every return**. GAP-1 **deliberately excluded** it for exactly this reason. See **NEXT-1** below.
  4. **(2026-08-02) Backup/restore is CARVED OUT of Phase 10 and built now; the rest of Phase 10 stays
     EXCLUDED.** Settles **D12 = A** and **D13 = leave excluded**. **DONE** — GAP-3.
- **Agents:** per-feature pipeline (§2.2) — **A1** (the audit + the decision set), **A14** (Tally fidelity,
  R7 — corpus and official help), Requirements/Design, Test author, Implementer, **A10** adversarial review
  **per stream, pre-merge**, **A12** GitHub Expert, run-app verifier.
- **Deliverables:** a voucher carrying **parallel per-category cost allocations** that posts and reports;
  **every one of the 24 voucher types reachable by menu AND by its real shortcut**, with deactivated types
  neither listed nor openable; a **populated Receivables report and ageing** from an ordinary invoice, with
  bills to settle; **Single Entry** on Contra/Payment/Receipt and a working **Purchase Accounting Invoice**
  incl. TDS/RCM; **real batch selection** with available balance, expiry and split-across-batches on the
  item-invoice screens, with the invoice total unmoved; a **version-stamped backup and a verified restore**;
  and the 12 cross-stream interaction tests.
- **Exit gate:** R9 — tests green and **shown** (**3651 — Ledger 1281 · Io 361 · Sqlite 210 · Desktop 1799**,
  all four counts per §6.2; incl. Robert & Bright unmoved); **A10** review per
  stream (**done, pre-merge, and it caught the four defects above**); **A12** (GitHub Expert) commits &
  pushes small reviewed units (R4/R10) — **commits DONE, push and PR OUTSTANDING**; **the real app run with
  evidence — OUTSTANDING, and it is the wide one:** it now has to cover **nine newly-merged features plus the
  previous session's numbering and service invoicing, NONE of which has ever been seen working outside a test
  harness**; `memory.md` updated (done); then **user go/no-go** per R12.
- **▶ CARRY-FORWARDS — open after this phase:**
  - **NEXT-1 — Credit/Debit Note stock parity (D3, approved 2026-08-02, NOT BUILT).** Touches the posting
    validator, `ItemInvoiceStock.Counts()`, inventory replay ordering, stock valuation and the existing GST
    CN/DN linkage; Robert & Bright must stay byte-identical. **Approved behind an oracle** — treat the
    Phase-10.8 harness discipline as the precedent, not the exception.
  - **NEXT-2 — the unresolved test-host crash.** The first full-suite run after `CrossStreamInteractionTests.cs`
    **crashed the Desktop test host** (`Xunit.Sdk.TestPipelineException`, exit **-1**, after **340 of 1799**
    tests) and **has not reproduced in three subsequent clean runs**. **Recorded, not resolved — expect
    recurrence in CI.** The two obvious explanations were checked and **do not hold**: assembly
    parallelisation is **already disabled** in `AssemblyInfo.cs`, and each test project runs in its **own
    process**, so a process-global `ClearAllPools()` cannot cross assemblies. The likelier mechanism is that
    the test deliberately overwrites a live SQLite database with 40 KB of `X` and asserts a read throws — but
    a **corrupted-page read may abort the process rather than raise a managed exception**, which depends on
    what SQLite touches first and would explain the flakiness.
  - **NEXT-3 — Phase 10.8's `NS-8` valuation prerequisite is STILL UNSOLVED** and remains the blocker for
    allow-negative-stock. Nothing in this phase touched stock valuation (GAP-5 is byte-identical there by
    design).
  - **NEXT-4 — the remaining decisions in `docs/tally-gap-decisions.md` are UNANSWERED** (D2, D6, D8, D10,
    D11, D14–D24). They are the backlog this phase drew from; **nothing is built from them without an R6
    plan amendment first.**

### Phase 10.10 — Wrong figures (engine-side)
- **▶ NUMBERING (R6).** Continues the **10.x insertion band** in use since 10.5 — the slots after Phase 10 and
  before Phase 11 (release). These are **preconditions to release**, not Phase-10 scope; Phase 10 and Phase 11
  stay excluded and unchanged. The design draft proposed `11.A`/`11.B`; **rejected** — 10.x is the band in use.
- **▶ REGISTER CURRENCY — read before using `docs/invented-vs-cloned.md` (re-verified in-tree this session).**
  **IV-9 / D7 is STALE** — the negative-stock hard block **no longer throws** (`Company.cs:268` carries
  `WarnOnNegativeStock = true`), so **v50 is SPENT and the next free schema version is v51**, and Phase 10.8's
  status block is stale with it (NS-3/NS-4 shipped; only **NS-8 → NS-1** valuation remains blocked). **D1 and
  D4 are already fixed.** **IV-1's "the corpus is silent" sub-claim is FALSIFIED** — the GST notes PDF
  enumerates all five levels verbatim and shows the Stock-Group GST field shape; it is silent **only on the
  ORDERING**. **Cited `file:line`s have DRIFTED by tens of lines — re-derive a row before trusting it.**
- **Goals:** fix the **six Class-A "wrong figures" rows** that reach an invoice, a return, a Balance Sheet or a
  purchase order — **IV-1, IV-2, IV-6, IV-7, IV-8, IV-10** — **in the engine only**. Each computes an
  arithmetically wrong number today that nobody notices until an auditor, a supplier or a tax officer does.
- **Modules:** `GstService` + `Reports/Gstr1.cs`; `TdsService` + `TcsService`; `StockValuationService` +
  `StockValuationMethod` + `StockItem` + `InventoryService`; `Reports/InterestCalculation.cs`;
  `Reports/ReorderStatus.cs`; new `MasterGstDetails` / `MarketValuationMethod` / `RetiredCostingMethod` /
  `CostingMethodMigrationNotice`; `Schema` / `SchemaDowngrade` / `SqliteCompanyStore`; Io fold-in for
  **WF-1/2/3 only** (WF-4…WF-7 are Io-clean, and that is itself the check).
- **R7 fidelity — sources of record (A14 confirms each before its slice ships):** TallyPrime's **HSN/SAC and
  GST Rate Hierarchy** page (both hierarchy strings verbatim; **Source of HSN/SAC Details** and **Source of GST
  Rate** are **two** options) + the corpus GST notes; TallyHelp's §194Q *"Calculate tax on value exceeding the
  threshold"*; TallyHelp's stock-valuation-methods page (**costing vs market**); `[CORPUS-BOOK pp.116-118]`;
  TallyHelp's Reorder Status page (**Nett Available**, Shortfall, **F8 Reorder Only**) + `[BOOK pp.163-164]`.
- **Work items (id — one-line; full design per row in the briefs, not here):**
  - **WF-1 (IV-1)** GST rate + HSN/SAC resolved over TallyPrime's **five-level hierarchy in a selectable
    order** — a deliberately narrow `MasterGstDetails` on `StockGroup`/`Group`/`GstConfig` (**do NOT reuse
    `StockItemGstDetails`** — its RSP/cess/RCM/§17(5) fields are read item-first only); ancestor walks need a
    **visited-set + depth cap** (no domain cycle guard exists). Also fixes the three latent bugs the new
    default makes routine (item-first dated `RateHistory` HSN; the two-level `ResolveCess` walk; `Gstr1.cs`'s
    **0%-bucket** + **"(none)"** HSN) and **deletes the invented "most-granular-wins (DP-6)" class doc**.
  - **WF-2 (IV-2)** §194Q charged on the value **exceeding** the threshold, TDS/TCS twins reconciled —
    `CalculateOnValueExceedingThreshold` on both `NatureOfPayment` and `NatureOfGoods` (trailing optional ctor
    param, ~20 sites untouched) + a **LIMB-AWARE** `ChargeableBase`. **Do NOT copy `TcsService` verbatim — it
    is single-limb and §194C is not** (carving against the cumulative limb returns **0 on a liable bill**).
    `AssessableValue` stays the FULL value; each service cross-references the other naming IV-2.
  - **WF-3 (IV-6)** Costing/market valuation split and every `LastSaleCost` item migrated — retire
    `LastSaleCost` (**ordinal 5 reserved forever**), add `MarketValuationMethod` + `RetiredCostingMethod` (a
    permanent audit fact **never cleared**) and a pure `CostingMethodMigrationNotice`. The notice is
    deliberately **QUALITATIVE** — quantifying the restatement would mean keeping the deleted defective code.
  - **WF-4 (IV-7)** Interest "Always" accrues on the **running balance, segment by segment**. **A movement
    dated `d` creates a boundary AT `d`, not `d+1`** — this corrects the register's own Fix line and is what
    makes the change zero-regression. Also: `OnBalance` evaluated **per segment**, and a sign-flipping window
    **split into one `InterestLine` per contiguous same-side run** (Dr and Cr interest must not net).
  - **WF-5 (IV-8a)** The basis is resolved from the **segment's own start date** — `BasisFor(per,
    segmentStart)`; kills the uncited anchoring and makes Simple agree with Compound. **Round ONCE at the
    end**, never per segment. **T8-INDEPENDENT.**
  - **WF-6 (IV-8b)** The `BasisFor` **divisor table** — **BLOCKED on T8**; the branch is one six-line switch
    body plus two test methods. Also rewrite `InterestPer.cs:25` / `InterestParameters.cs:20`, which describe
    the per-period answer while the code implements ×12 — that disagreement is what exposed the defect.
  - **WF-7 (IV-10)** Reorder Status computes **Nett Available** (`closing + pendingPO − soDue`) and **stops
    filtering**; `orderToBePlaced = shortfall <= 0 ? 0 : max(shortfall, MOQ)`. Delete the `closing >
    reorderLevel` block (pendingPO would be double-counted) and the invented listing filter **with no
    replacement predicate** — that is what **F8 Reorder Only** is. **Do NOT substitute `if (shortfall <= 0)
    continue;`** — it deletes the PO-covered row `[CORPUS-BOOK p.164]` shows on screen.
- **Slices (schema-clean first, then the version chain in strict order; rationale in `memory.md`):**
  1. **S1 — Interest running-balance accrual + per-segment basis** (WF-4, WF-5) — **L / med / schema-clean** —
     **FIRST:** the only money fix both schema-clean and measurement-independent. Ships a compound test proving
     a no-movement window reproduces today's figure **to the paisa** before the movement case is added.
  2. **S2 — Reorder Nett Available + delete the invented filter** (WF-7) — **M / low / schema-clean** —
     **append** `NettAvailable` to the positional row rather than inserting it at Tally's column position.
  3. **S3 — Interest divisor table** (WF-6) — **S / low** — **DO NOT MERGE THE CONSTANTS UNTIL T8 LANDS.**
  4. **S4 — GST five-level hierarchy** (WF-1) — **XL / HIGH / owns v51** — the worst row in the register.
  5. **S5 — §194Q excess carve + TDS/TCS reconciliation** (WF-2) — **M / med / owns v52**.
  6. **S6 — Costing/market split + `LastSaleCost` migration** (WF-3) — **L / med / owns v53** — last: the only
     migration that **rewrites customer data**, and it wants the two preceding parity gates green first.
  - **▶ Why the worst row is fourth:** WF-1 was the only slice whose back-fill moves an existing customer's
    future invoices, so it could not start before that R12 ruling. The ruling has landed, so **WF-1 may now be
    pulled ahead of WF-6** without disturbing the version chain.
- **Schema (v50 → v53) — binding allocation, replacing three colliding "v50 → v51" claims: WF-1 = v51,
  WF-2 = v52, WF-3 = v53.** Each needs its columns in **BOTH** `CreateV1` **and** its migration byte-identically
  (`SchemaMigrationEquivalenceTests`), a true-inverse `DowngradeTo`, and Io parity. **Watch the
  default-asymmetry trap in both directions:** a `DEFAULT` back-filling an upgraded book to the *new* behaviour
  silently changes shipped figures (v51); a `DEFAULT 0` back-filling to the *old* one silently re-ships the bug
  (v52). v53 is the first **data rewrite** in the chain.
- **USER DECISIONS (R12 — settled; do not re-litigate):**
  1. **(WF-1) `MigrateV50ToV51` back-fills `StockItemFirst`** for books that already exist — provably changes
     **zero** currently-resolvable figures. **Fresh companies get TallyPrime's shipped `LedgerFirst`.**
  2. **(WF-3) Items on `LastSaleCost` migrate to `LastPurchaseCost`**, with a one-time notice **naming the
     affected items**, because prior-year Balance Sheets are affected.
  3. **(WF-7) HARD GATE PR-8 — the "MOQ floor at zero shortfall" rule — is RETIRED.** Requires amending
     `docs/phase6-advanced-inventory-requirements.md:598-601`, **inverting** the regression test at
     `tests/Apex.Ledger.Tests/InventoryReportsTests.cs:799`, and recording the reversal with its citation
     (**Tally-Prime-Book p.164**). The report also **stops filtering on closing stock** — TallyPrime's default.
- **ORCHESTRATOR RULINGS (with their reason):**
  1. **WF-1 covers all FOUR item-first lookups**, not just `ResolveBase`: also `GstService.cs:380` (HSN for the
     dated rate-history override), `GstService.cs:432` (`ResolveCess`) and `RcmService.cs:189` (`supplyGst`).
     **Reason:** fixing only `ResolveBase` would let a line take its **rate from the ledger** and its **cess
     from the item** — a new wrong-figure defect. Larger blast radius, **accepted deliberately**.
  2. **WF-1 ships TWO source-order fields** (HSN/SAC, GST Rate), matching TallyPrime's two separate options —
     **cheaper now than a later schema bump**.
  3. **WF-1 EXCLUDES corpus level 5, "Creating GST Classification"** — TallyPrime's own published hierarchy
     string omits it and there is **no GST-Classification master in `src/`**.
  4. **WF-2 seeds `CalculateOnValueExceedingThreshold = true` for §194Q** — a **deliberate, cited divergence**:
     TallyHelp documents the option but not its shipped default, and the statute mandates the excess base, so
     shipping `false` would ship a knowingly wrong figure out of the box.
  5. **WF-2's back-fill is guarded to predefined rows only; hand-authored §194Q masters are surfaced in the
     one-time notice rather than silently rewritten.**
  6. **WF-3's `MarketValuationMethod` ships PERSISTED-BUT-INERT** — no market-value column, no selling-price
     auto-fill. **Reason:** TallyHelp names "Average Price" but never states the averaging period, so computing
     it now would invent an unsourced figure — the exact sin IV-6 exists to punish. Matches the house precedent
     (`StockValuationMethod` itself shipped persist-only).
  7. **WF-3 DEFERS `AtZeroCost` and the Fifo/Lifo → Perpetual rename.** The rename would change the exported
     canonical **NAME** of every existing item and rests on an inference; `AtZeroCost` changes `IssueValue` and
     therefore Manufacturing Journal absorption.
  8. **WF-5 (IV-8) is built BEFORE WF-4 (IV-7)** — `BasisFor` is applied inside the accrual WF-4 rewrites, so
     the order is **not free**.
  9. **The interest report's Principal column becomes the TIME-WEIGHTED AVERAGE**, so `Principal × Rate × Days
     / Basis == Interest` stays true and an auditor can re-derive the row. `InterestReportViewModel.cs:73` is
     relabelled — **that relabel sits in the deferred UI tail.**
- **BLOCKED ON MEASUREMENT (the user is running these in a real TallyPrime):** **T8** gates **only** WF-6's two
  `BasisFor` arms (ThirtyDayMonth, CalendarMonth) — **the Calendar-Month ×12 defect is wrong under either
  answer and ships first, unblocked**. **T10** scopes WF-2's catch-up question (register §6 U-4) but **does not
  block** the excess carve. **WF-3's Fifo/Lifo rename and IV-11 are not in scope**, still blocked on T3/T9.
- **DEFERRED UI TAIL — its own follow-up slice, named so it is not smuggled in.** Keeping this phase out of
  `src/Apex.Desktop/**` entirely is **what makes the Desktop test count a single-stream variable.** Deferred:
  **(WF-1)** master GST fields + the F11 source-order switch; **(WF-2)** the `NatureOfPaymentMasterViewModel`
  field; **(WF-3)** the valuation picker and where the one-time notice surfaces; **(WF-7)** the Nett Available
  column (`ReportsViewModel`, `ReportTabularProjector.cs:146` header array); **(WF-4/5/6)**
  `InterestReportViewModel`.
- **Agents:** per-feature pipeline (§2.2) — Requirements/Design (design of record in `memory.md`), **A14** (R7,
  per slice), Test author, Implementer, **A10** three-lens review **per slice, pre-merge**, **A12**, run-app.
- **Deliverables:** a ledger-rated / stock-group-rated / company-defaulted line that **charges the right GST**
  and files under the right HSN in GSTR-1; a §194Q deduction on the **excess** with the TCS twin reconciled; a
  Balance Sheet no longer valuing stock at selling price, with affected items named; interest accrued on the
  **running balance** with a re-derivable Principal; a Reorder Status showing **Nett Available**, every item
  listed.
- **Exit gate:** R9 — tests green and **shown as all four per-project counts, never the total** (§6.2; baseline
  today **Ledger 1294 · Io 368 · Sqlite 214 · Desktop 1836**), **predicted before each merge, an exact match
  treated as evidence the merge is semantically clean**; Robert & Bright unmoved; **the schema gate is three
  migrations deep** (v51/v52/v53 each with equivalence + true-inverse downgrade parity green); **A10** review
  per slice pre-merge; **A12** commits & pushes (R4/R10); the real app run with evidence; `memory.md` updated;
  **user go/no-go** per R12.
- **▶ CARRY-FORWARDS:** the deferred UI tail · **T8c** (`PostDue`: due date or the day after) — measured here,
  fixed elsewhere · the **Fifo/Lifo → Perpetual** rename and **`AtZeroCost`** · the register's remaining
  Class-A rows **IV-11, IV-14, IV-17, IV-22, IV-33** · marking the six fixed rows in
  `docs/invented-vs-cloned.md` (see the documentation slice at the end of 10.11).

### Phase 10.11 — Voucher lifecycle: alter, delete, cancel
- **▶ SCOPE FENCE — THIS IS NOT PHASE 10 (R12, 2026-08-02).** Phase 10 and Phase 11 were **excluded by the
  user** and this phase re-opens neither. It builds three verbs TallyPrime has and we do not — **alter, delete,
  cancel** — over engines that **already exist** (`LedgerService.Cancel`, `.Delete`), and it builds **NO audit
  trail, NO Edit Log, NO security roles, NO user attribution.**
- **▶ STATED CARRY-FORWARD GAP — alter and delete ship with NO AUDIT TRAIL, BY EXPLICIT USER DECISION (item 3
  below). A recorded ruling, not an oversight, and it must be read as one at every review.**
  `MasterAlterationRules.cs:51-54` already defers the **master**-alteration audit trail to the excluded Phase
  10 in its own words — *"writing half of it here would leave an audit log no one can query or protect."*
  Voucher alteration and deletion are held to the identical ruling. **Consequence, stated so it is never
  discovered instead:** after this phase an operator can alter and delete a posted voucher and **the books
  carry no record that either happened.**
- **▶ WHY IV-5 IS HERE AND NOT IN 10.10 (R12 user decision).** IV-5 is filed as Class-C but by harm belongs
  with the wrong figures — it **posts real, irreversible receipt/payment vouchers**. **Moved to this stream by
  user decision:** its fix is **navigation, not arithmetic**, and it **collided on the shell files**
  (`MainWindow.axaml.cs`, `MainWindowViewModel.cs`) with VL-1…VL-3.
- **▶ BUILD SEQUENTIALLY IN ONE STREAM — do NOT use Phase 10.9's five-worktree pattern.** All four work items
  converge on the same ~700-line first-match-wins key dispatcher and the same prompt/button-bar machinery;
  10.9's single hand-resolved add/add conflict was **still nearly malformed**.
- **Goals:** give the operator the three lifecycle verbs a Tally user reaches for — **alter a posted voucher
  (IV-3)**, **delete a voucher or master (IV-4)**, **cancel a voucher keeping its number in sequence
  (IV-16)** — and **stop the one gesture that posts vouchers the operator never confirmed and cannot remove
  (IV-5)**. Two of the four are **wiring gaps, not design gaps**: the engine exists and no UI calls it.
- **Modules:** the tunnel key dispatcher in `MainWindow.axaml.cs` and the prompt / button-bar / routing
  machinery in `MainWindowViewModel.cs`; `LedgerService` (a new `Replace`; `Cancel`/`Delete` reached at last);
  `Company.ReplaceVoucherInternal`; a new pure `Services/MasterDeletionRules.cs` on the `MasterAlterationRules`
  shape; `VoucherEntryViewModel` (a `ForAlter` factory + the rehydration inverse of its four `To…()` writers);
  `BillSettlementService`; `ReportsViewModel`/`ReportRow`; the two Io print DTOs.
- **R7 fidelity (A14 per slice):** `[CORPUS-BOOK p.28]` — plain **Enter** on a register row goes **straight to
  Show/Edit**; TallyPrime has **no read-only voucher screen at all**, scopes **Alt+X** to *cancelling from a
  report* and reserves **Ctrl+Enter** for *display-only drill-down*. `[CORPUS-SG p.67]` — a ledger with
  transactions **cannot be deleted**; Tally **just refuses**. **Alt+A "Add voucher in report"** is TallyPrime's
  own bottom-bar entry `[CORPUS-BOOK p.431]`. **⚠️ Two Tally-side facts could NOT be settled and must NOT be
  fabricated (R7):** the exact cancellation prompt **wording**, and whether **un-cancel** exists.
- **Work items (id — one-line; full design per row in the briefs, not here):**
  - **VL-1 (IV-3)** Voucher alteration — the drill opens the entry screen pre-filled and Accept **REPLACES**
    via `LedgerService.Replace(Guid, Voucher)`, **ordered so a rejected replacement leaves the original
    untouched** (`Post` mutates before it can fail, so remove-then-post loses the original). **Three identities
    preserved, each for its own reason:** the **Guid** (25+ tables `REFERENCES vouchers(id)`), the **Number**
    (`Post` assigns when `Number <= 0`, so passing 0 renumbers a mid-sequence voucher to max+1) and the **list
    position** (Remove+Add reorders the Day Book for same-dated vouchers). Three hard parts, named so they are
    not discovered during implementation: the posted `Voucher.Lines` is **not** what the operator keyed; the
    hidden-sub-form rule is **inverted** for vouchers; and the old voucher's derived records must be **unwound
    before** the replacement re-derives.
  - **VL-2 (IV-4)** Delete on **Alt+D** behind a Y/N confirmation and referential guards — one confirmation
    channel with an action slot (keeping `ConfirmMasterAccept`/`DismissMasterAccept` by name, called by tests
    and the dispatcher), a new **pure** `MasterDeletionRules`, and routing from Day Book / register drill /
    voucher detail / Chart of Accounts / Stock Item list. **Also closes the modifier hole** — `CanQuickJump`
    never tests `e.KeyModifiers`, so **Alt+D already opens the Day Book today**.
  - **VL-3 (IV-16)** **Alt+X = CANCEL a posted voucher**, and cancelled documents **look** cancelled — delete
    the app-wide Alt+X arm outright (Escape already reaches `Back()`; it has no `!IsPickerOpen` guard and blows
    through the WI-11 Accept prompt) and **delete `CancelVoucher()` rather than repurpose it, so the compile
    breaks and every stale caller surfaces**; then a narrowly-gated arm calling `LedgerService.Cancel`, plus
    `IsCancelled` on `ReportRow`, a `CancelledRowToBrushConverter`, and the **greyed Day Book row**
    `plan.md:267` already specifies. **Two picker leaks go live the moment Cancel is reachable and must close
    in the same slice** — `BuildSection34Pickers()`/`BuildAdvancePickers()` filter on base type only, so a
    cancelled invoice is offered as the original supply a §34 Credit Note adjusts.
  - **VL-4 (IV-5)** Settlement comes **off Ctrl+B** and off the report — delete the arm, handler, button-bar
    row (**leaving it would paint a red badge that fires nothing — the IV-31 defect**) and `SettleBills()`;
    **keep `BuildSettlementAllocations`** (the only code that validates an AgstRef against a genuinely open
    bill and caps each knock at the pending amount), **delete `SettleAndPost`**. Replaced by **Alt+A** on the
    Outstandings screen, opening a **Single Entry** Receipt/Payment **pre-loaded** with the selected bills.
- **Slices (one sequential stream, all schema-clean; rationale in `memory.md`):**
  1. **S1 — the Alt+D modifier hole** (VL-2 step 1) — **S / low** — **its own commit, ahead of everything, and
     it must precede S4:** binding Alt+D to DELETE on top of a hole that already fires it as a bare-letter
     quick-jump would make a stray Alt+D destructive.
  2. **S2 — settlement off Ctrl+B** (VL-4) — **M / low** — **second, because it is the only row in the phase
     that CREATES bad data**: today Ctrl+B posts an irreversible voucher the operator never confirmed and —
     until S3/S4 — can neither cancel nor delete.
  3. **S3 — Cancel on Alt+X** (VL-3) — **M / med** — before delete, so the first new verb is the
     **non-destructive** one and the dispatcher is clean where S4 inserts.
  4. **S4 — Delete on Alt+D** (VL-2 steps 2-11) — **L / med**.
  5. **S5 — Voucher alteration** (VL-1) — **XL / HIGH** — last and largest; the only slice that rebuilds a
     posted aggregate rather than routing to an existing engine method.
- **Schema: NONE — schema-clean end to end, and that is designed, not coincidental.** `SqliteCompanyStore.Save`
  re-inserts the whole aggregate in one transaction, so persistence is a pure function of the in-memory
  `Company` graph. **Io: none for the canonical model** — asserted, not assumed (a never-altered company must
  still export byte-identically, ER-13).
- **USER DECISIONS (R12 — settled; do not re-litigate):**
  1. **(VL-1) Ctrl+Enter opens alteration; plain Enter keeps the read-only VoucherDetail column.** This is
     **BACKWARDS from TallyPrime on both keys** — Tally's Enter goes straight to Show/Edit and its Ctrl+Enter
     is display-only. A **deliberate, accepted divergence** to preserve the Miller-column cascade, **with a
     follow-up to reconsider.**
  2. **(VL-2) COMPANY deletion is SPLIT OUT into its own later slice;** this slice ships Alt+D for **voucher,
     ledger and group only**. **Reason:** `CompanyStorage.Delete` **swallows `IOException`** — a locked `.db`
     leaves the operator believing the company was deleted while the file survives — and `Company = null`
     appears nowhere, so deleting the **open** company leaves a live aggregate bound to a missing file. The
     split-out slice needs **type-the-company-name confirmation**, a **mandatory backup-first offer**, a real
     **teardown**, and the **IOException fixed**.
  3. **Alter and delete ship with NO audit trail**, by the earlier decision that excluded Phase 10 — a stated
     carry-forward gap, not an oversight (see the block above).
- **ORCHESTRATOR RULINGS (with their reason):**
  1. **VL-1 REFUSES alteration, with a named message, for POS, Manufacturing Journal, payroll and the three
     `InventoryVoucher` entry screens** — a silent no-op is the failure mode being avoided.
  2. **Altering a voucher's DATE is warn-and-proceed, not blocked** — blocking would repeat the IV-9 mistake
     where a default quietly became the only behaviour. **Refuse alteration only when
     `EInvoiceStatus.Generated`, not `Pending`.**
  3. **VL-3 ships NO un-cancel** in this slice — VL-1 ships in the same stream and **alteration is the route**.
     The confirmation string is **ours, recorded as UNVERIFIED-BY-DESIGN** rather than fabricating a Tally
     quote (R7). The e-invoice interlock is **warn-and-proceed**. The **pure-inventory Cancel analogue is
     deferred** until the registers carry a cancelled-inclusive view.
  4. **VL-4's replacement gesture is Alt+A** — TallyPrime's own documented *"add voucher in report"*. **Ctrl+B
     is freed and RESERVED**, and **Basis of Values is explicitly NOT built here** — recorded as named debt,
     and it is what Ctrl+B is reserved for.
- **Measurements this phase is blocked on: NONE** — every blocker was an R12 decision and all are settled.
- **Agents:** per-feature pipeline (§2.2) — Requirements/Design, **A14** (R7, incl. the explicit finding that
  two Tally strings are unpublished), Test author, Implementer, **A10** review **per slice, pre-merge**,
  **A12**, run-app verifier.
- **Deliverables (and the four driving tests, odd paise throughout, each proven to fail before its fix):** a
  posted voucher opened pre-filled, edited and re-accepted **keeping its Guid, number and Day-Book position**,
  derived records unwound and re-derived exactly once — driven by **(1)** a rejected `Replace` leaving the
  original **byte-identical and still at its list index**, **(2)** a TDS-carved purchase re-deriving from the
  **restored gross** at a carve rounding to odd paise, **(3)** a §34 Credit Note altered with
  `ShowSection34Details` false **keeping** its `GstCreditDebitNoteLink` and GSTR-1 9B row; **Alt+D deleting a
  voucher, ledger or group** behind a Y/N confirmation and a guard that refuses with the count of blockers;
  **Alt+X cancelling from the Day Book**, number staying in sequence — **(4)** a **₹1,84,733.45** invoice
  greyed **and** overprinted "CANCELLED"; **Ctrl+B unbound and reserved**, settlement on the **Alt+A pre-loaded
  Single-Entry Receipt/Payment** the operator confirms.
- **Exit gate:** R9 — tests green and **shown as all four per-project counts, never the total** (§6.2; baseline
  today **Ledger 1294 · Io 368 · Sqlite 214 · Desktop 1836**), **predicted before each merge, an exact match
  treated as evidence the merge is semantically clean**; Robert & Bright unmoved; **A10** review per slice
  pre-merge; **A12** commits & pushes (R4/R10); the **real app run with evidence** (alter a posted invoice and
  see the same number at the same Day-Book position; be refused deleting a ledger that has transactions; cancel
  an invoice and see it greyed and printed CANCELLED; settle two bills through the pre-loaded Receipt);
  `memory.md` updated; **user go/no-go** per R12. **One addition specific to this phase: the NO-AUDIT-TRAIL
  consequence is re-stated at the gate and acknowledged, not assumed** — with alter and delete working in front
  of them, the user confirms that shipping them without any record of who changed what is still the decision.
- **▶ CARRY-FORWARDS:** the **audit trail** itself (deferred to the excluded Phase 10; the gap widens with
  every altered or deleted voucher) · **company deletion**, specified above but not fixed · **alteration for
  the five deferred voucher families** · **cancellation for pure-inventory vouchers** · **Basis of Values**,
  which reclaims Ctrl+B from the reserved list this phase creates · the **key-map table** (IV-28) — build the
  Ctrl+B reserved-unbound row here so IV-28 inherits it.

> **▶ ONE POST-MERGE DOCUMENTATION SLICE, OWNED BY ONE AGENT — applies to BOTH 10.10 and 10.11 (R5/R6).** All
> documentation edits arising from these two phases — `docs/invented-vs-cloned.md` (row status + the register
> corrections recorded in 10.10), `docs/phase6-advanced-inventory-requirements.md` (retiring PR-8),
> `docs/voucher-entry-specification.md:101` and `memory.md` — are **deferred to a single slice after both
> merges, performed by one agent**. Reason: two worktrees × nine slices appending to the same files
> concurrently would conflict on every one of them.

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
- **▶ A GATE IS THE FOUR PER-PROJECT COUNTS, NEVER THE TOTAL ALONE (standing rule — recorded 2026-08-03).**
  Every gate is reported as **Ledger · Io · Sqlite · Desktop = total**, and **a green total carrying the wrong
  per-project counts is a CONTAMINATED RUN, not a pass.** *Why the rule exists:* a **truncated Desktop run once
  reported "Passed! 610" against a real 1635** and **looked identical to success** — the total is the one field
  that cannot detect it. **The four numbers ARE the check**, so a future session cannot apply the rule against a
  bare total; wherever a suite size is written down in `plan.md` or `memory.md`, write all four.

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
| R-7 | **Data-loss / migration risk** (SQLite schema changes) | Versioned migrations from Phase 0; **backup/restore BUILT 2026-08-03 (Phase 10.9 / GAP-3, `e90a169`)** — version-stamped against schema v49, refusing a restore the running build cannot handle, with a **restore round-trip test**; no destructive op without confirmation (NFR-8). **Amendment (R6, 2026-08-02):** this row previously read "backup/restore (Phase 10) round-trip-tested" while **Phase 10 was excluded** — the plan's top-ranked data-loss risk was mitigated by a feature the plan had also cancelled. The user carved the item out of Phase 10 (D12 = A) and it now exists; the rest of Phase 10 stays excluded. |
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
refinements §10 (C-1…C-9) folded in the same day from the plan critique. Amended 2026-07-20 (user-authorised,
R6): stale status header corrected to the real state (Phases 0–9 + 10.5 merged, schema v46, 3321 tests green —
Ledger 1239 · Io 349 · Sqlite 173 · Desktop 1560,
Phases 10/11 excluded); **Phase 10.6 — Keyboard & input parity** added (KB-1…KB-4) with the WI-2 scope
correction recorded above it. Amended 2026-07-27 (R6): **Phase 10.8 — Allow negative stock** added (NS-1…NS-6
over slices S-A…S-C, allow-by-default globally + a warn-only `WarnOnNegativeStock` toggle, schema **v49 →
v50**, `AverageCost` under negatives explicitly deferred), and the now-stale "rebase to v48" version-
coordination note in Phase 10.7 corrected to **v50** (v48 went to numbering S5, v49 to the accounting-invoice
flag). Amended again 2026-07-27 (R6, **user decision**): the `AverageCost` **deferral is REVERSED** — its
"in band" premise was refuted as a **tautology** (the verifying harness's reference **echoed HEAD's own
averaging rule**; an adversarial audit caught it, and two independent implementations — one validated
byte-for-byte against HEAD on **95/95** subjects — measured HEAD closing at **₹12,007.50** against a
debt-aware **₹11.10**, undetected by every band and spend check). **NS-1** widened to debt-aware repayment
across **FIFO, LIFO and the moving average** (never-re-rate-a-debt and no-floors/no-clamps retained), new
**NS-7 — harness check inversion** added (the `AverageCost` byte-identity assertion now forbids the fix and
becomes a point-oracle subject; echo-of-HEAD references must be labelled as carrying **no correctness
evidence**), **S-A** rescoped to all three methods, **user gate (a) RESOLVED** (kept on record with its
evidence, not deleted; gate (b) still open), and the Exit gate now requires an **independent adversary to
return TRUSTWORTHY on the harness before production code is written**. **Modules** and **Deliverables** were
realigned to the same three-method scope in the same amendment (`StockValuationService` now named as the
lot machinery **and** the moving-average path; the `AverageCost` ₹11.10-vs-₹12,007.50 regression case and the
NS-7 harness inversion added to the test list) so no bullet still describes the phase in FIFO/LIFO terms. Amended
2026-07-29 (R6, **user decision — STOP AND BANK**): **Phase 10.8 is STOPPED, the engine REVERTED to HEAD
byte-for-byte and the suite back at the pre-session baseline 3491 — Ledger 1261 · Io 359 · Sqlite 184 · Desktop
1687.** **S-A is NOT DONE** (its **NS-2** harness and
**NS-7** inversion ARE done — the harness is committed and its audit chain closed at **TRUSTWORTHY**, satisfying
the Exit gate's harness precondition; **NS-1** is not, after **eight** attempts that **each passed the full suite**
and **four of which also passed the oracle** before adversarial review convicted them). **S-B and S-C are marked
BLOCKED**, S-B with the note that its ordering is a **safety property** — it removes the guard that currently makes
negative stock unreachable. A **STATUS** banner and **THE STRUCTURAL FINDING** were added at the head of the phase
(**any predicate-gated scope creates a valuation cliff at its boundary**: an ordinary internal godown transfer flips
an item's whole history, **₹40,000,001.20 vs ₹25,000,000.75**, unbounded, surviving a same-day round trip, where
**HEAD is continuous at `jump=0.00`**). **NS-1 is marked SUPERSEDED-AND-GATED** — its invariants stand, its
item-level debt assumption does not — behind a new **NS-8**, the precisely-specified valuation prerequisite:
**per-(item, godown, batch)-key valuation AND cost-flowing stock-journal transfers, built TOGETHER** (re-keying
alone **broke ordinary transfers**, ₹5,000,002.37 on ₹1,000,003.73 ever spent, where HEAD was exactly right), plus
the per-**date** debt gate, the no-forward-look cost chain, and two **pre-existing HEAD defects** to settle inside
it (`StockValuationService.cs:180` skipping allocations on a Physical-Stock voucher against
`InventoryLedger.cs:193-207`; and the item-level/per-key desync). **Four further user rulings** were recorded with
dates under User gates — including one (**fix the desync per-key**) **REVERSED ON EVIDENCE and KEPT ON RECORD**,
because the reversal is the reusable lesson — and the **Exit gate** gained two measured conditions: **continuity
across the boundary is a gate condition**, and **a green suite is a floor, never a verdict, in this phase** (eight
of eight passed it). The eight measured failure modes and their reproducing books live in
**`tools/HeadOracle/README.md`, the handover document**; the full narrative is the 2026-07-29 entry in `memory.md`.
Amended 2026-08-03 (R6 — **the plan was contradicting the repository, which is what R6 exists to prevent**):
**Phase 10.9 — Tally-gap remediation** added as the record of twelve commits above `bc95728` that were **built,
reviewed and merged with no plan entry at all** (five parallel streams **GAP-1…GAP-5** — voucher-entry core,
parallel cost sets, backup/restore, voucher-type reachability, real batch allocation — plus **GAP-6** twelve
cross-stream interaction tests and the committed Tally audit `6124a25`; the gate recorded as a **per-project
table**, **3491 → 3548 → 3619 → 3639 → 3651** closing at **Ledger 1281 · Io 361 · Sqlite 210 · Desktop 1799**,
build 0W/0E, **schema v49 unchanged**, and the hand-resolved `VoucherEntryViewModel.cs` add/add conflict
recorded). **§6.2 gained a standing rule** in the same pass: **a gate is the FOUR per-project counts, never the
total alone** — a green total with the wrong per-project counts is a **contaminated run, not a pass**, the
failure it exists to catch being a **truncated Desktop run that reported "Passed! 610" against a real 1635 and
looked identical to success**; every suite size written into `plan.md` or `memory.md` now carries all four. **The backup/restore CONTRADICTION is corrected explicitly:** the plan simultaneously listed
backup/restore as an **excluded** Phase-10 module **and** as the mitigation for its own **top-ranked data-loss
risk R-7**, while the feature was in fact **built and committed** — **Phase 10** now carries a **carve-out status
banner** (backup/restore built as Phase 10.9 / GAP-3; **TallyVault, Security Control / roles, Edit Log / Tally
Audit, split-by-FY, group company and repair/rewrite remain EXCLUDED by standing user decision**), and **R-7's
mitigation cell** was rewritten to name the shipped feature and to state the contradiction it replaces. **Four
user decisions recorded with dates** under Phase 10.9: **(2026-08-01)** TallyPrime is the fidelity target and
**Tally 7.2 is a checklist only** (D1 — all ten corpus PDFs are TallyPrime; the installed 7.2 was never opened),
and **optimise for completeness of voucher entry**; **(2026-08-02)** goods-return **stock parity on Credit/Debit
Note is APPROVED BEHIND AN ORACLE and is NOT YET BUILT** (D3 — `ItemInvoiceStock.Counts()` still admits only
Purchase/Sales carriers, so returns silently drift the books), and **backup/restore carved out of Phase 10**
(D12 = A, D13 = leave excluded). **Four carry-forwards** opened — **NEXT-1** CN/DN stock parity, **NEXT-2** the
unreproduced Desktop test-host crash (recorded, not resolved — expect recurrence in CI), **NEXT-3** the still
unsolved `NS-8` valuation prerequisite, **NEXT-4** the unanswered decisions D2/D6/D8/D10/D11/D14–D24 — and the
**status header** was corrected to the real state (unpushed branch `claude/confident-ellis-dedef5`, **no PR**,
**3651 green — Ledger 1281 · Io 361 · Sqlite 210 · Desktop 1799**, schema v49, current work = the **outstanding
R9 real-app run** across nine merged features never yet seen outside a test harness).
Any deviation during execution is
recorded in `memory.md` with its reason (R6).*
