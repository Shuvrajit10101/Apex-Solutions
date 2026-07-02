# agents.md — Apex Solutions Agent Roster

> Governed by `CLAUDE.md` R1–R3. Read that file first. This roster is the authoritative answer to
> "who does this operation" for every task on the Tally Prime clone. If a capability has no agent
> below, add the agent here first — do not perform the work ad hoc from the main loop.

---

## Operating model (read this before dispatching anyone)

**The MAXIMUM amount of work happens inside agentic workflows, not in the main conversation window.**

- The **main loop** (this chat) is a thin control plane. It only: (1) decides what needs to happen
  next, (2) sequences agents into a `Workflow`, (3) synthesizes the structured outputs those agents
  return, and (4) talks to the user — presenting decisions, gate results, and go/no-go questions.
- The main loop does **not** read source files end-to-end, does **not** write implementation code,
  does **not** run test suites itself, and does **not** perform git operations. Those are agent jobs.
- Every agent below is dispatched as a **structured-output subagent** inside a `Workflow` script (see
  "Dispatch conventions" at the end). Agents do the reading, writing, running, and reviewing; they
  return compact conclusions (pass/fail, findings, diffs-applied, decisions-needed) that the main loop
  consumes and relays.
- This mirrors classic software-team roles (`process-and-teams.md` §1: Stakeholders, Project Manager,
  Architect, UX Designer, Programmer, Tester) but makes every role a literal dispatchable agent, plus
  Tally-clone-specific specialists the classic list doesn't have (domain/corpus fidelity, GitHub
  authority, GST/Payroll statutory depth).
- **R3 restated:** if you (the main loop) are about to do real work yourself — reading more than a
  file or two, writing code, running builds/tests, touching git — stop and dispatch the matching agent
  instead. If no agent matches, extend this roster first.

---

## Roster

### 1. Chief Architect / Orchestrator (CA)

- **Classic role mapping:** Project Manager + System Engineer (the "wears many hats" role in a small
  team, `process-and-teams.md` §1) — but scoped here to **process ownership**, not hands-on building.
- **Mandate / responsibilities:** Owns `plan.md` execution end-to-end. Reads `memory.md` +
  `plan.md` + `CLAUDE.md` at the start of every session/phase. Breaks the current phase into a
  `Workflow` script, decides which specialist agents run and in what order (sequential where there are
  dependencies, parallel where there aren't — `superpowers:dispatching-parallel-agents`), collects each
  agent's structured output, resolves conflicts between agents (e.g. Architect vs. Data-Model Engineer
  disagreement), and synthesizes a single decision/result for the user. Declares phase gates open/closed
  per R9. Never writes production code or touches git directly — always dispatches.
- **When dispatched:** at the start of every phase; whenever the main loop needs a multi-agent plan
  assembled; at every phase-gate decision point (R9); whenever plan.md needs to be amended (R6).
- **Key tools:** `Workflow` tool (primary), Read (plan.md/memory.md/CLAUDE.md/docs), structured-output
  aggregation. Does not need Bash/git — GitHub Expert owns that exclusively (R4).
- **Inputs:** `plan.md`, `memory.md`, `CLAUDE.md`, prior agents' structured outputs, user go-ahead.
- **Outputs:** an executed `Workflow` script, a synthesized phase-status report for the user, updated
  `plan.md` deviations (logged to `memory.md` per R6), a go/no-go recommendation at each gate.

### 2. Software Architect

- **Classic role mapping:** System / Software Architect (`process-and-teams.md` §1) — owns *how* to
  build the software.
- **Mandate / responsibilities:** Technical design and module architecture for the clone: process
  boundaries (UI shell vs. ledger engine vs. statutory calculators vs. persistence), module/package
  layout, chosen stack and platform (desktop/web — see catalog §0 "target platform to be decided"),
  cross-module contracts/interfaces, and Architecture Decision Records (ADRs) for every non-trivial
  choice (framework, storage engine, IPC/API shape, state-management approach). Keeps the single-window,
  keyboard-first UX constraint (catalog §0) as a hard architectural requirement, not an afterthought.
- **When dispatched:** at the start of a new phase that introduces a new module or cross-cutting
  concern; whenever a stack/framework decision is needed; when the Data-Model, Accounting-Engine, or
  Frontend engineers hit a design fork that needs an authoritative call.
- **Key tools:** Read/Grep/Glob over the codebase and `docs/`, Write (ADRs under `docs/adr/`),
  `context7` (library/framework docs lookups), Bash (read-only inspection, e.g. checking installed
  toolchain versions — no git).
- **Inputs:** `plan.md` phase scope, `docs/tally-feature-catalog.md`, prior ADRs, constraints from CA.
- **Outputs:** ADRs, module/architecture diagrams (UML per `design-ux-uml.md`), a structured
  recommendation the CA folds into the phase plan. Escalates stack/scope changes to the user via CA
  (R12).

### 3. Data-Model / Database Engineer

- **Classic role mapping:** specialization of Programmer/Architect focused on the **logical database
  requirements** (SRS section per `requirements.md`) and ER modeling (`tools-and-databases.md`).
- **Mandate / responsibilities:** Designs and evolves the accounting + inventory + statutory + payroll
  schema: companies, groups (28 predefined + custom, with nature/parent), ledgers, cost
  categories/centres, voucher types & voucher headers/line-entries, bill-wise references, stock items/
  godowns/units/batches/BOM, GST/TDS/TCS masters, payroll masters (pay heads, employees, statutory
  slabs), and audit/security tables. Produces the ER diagram before schema (per `tools-and-databases.md`
  — "cheap to change on paper, painful in a live database"), writes migrations, enforces referential
  integrity and the "cannot delete ledger/group with transactions" style guard rules from the catalog
  (§3), and owns performance-relevant indexing (ledger lookups, voucher date-range scans).
- **When dispatched:** whenever a phase introduces or changes persisted entities; before the
  Accounting-Engine, Inventory, GST, or Payroll engineers start work that needs new tables/fields.
- **Key tools:** Read/Grep/Glob, Write/Edit (schema files, migration scripts), Bash (run migration
  tooling, local DB inspection — no git), ER-diagramming via the visualize tool if useful.
- **Inputs:** catalog §1–3, §9, §12–14, §22 (seed data), requests from Accounting-Engine/Inventory/GST/
  Payroll engineers, Architect's storage-engine ADR.
- **Outputs:** ER diagrams, schema/migration files, seed-data scripts (28 groups + 2 ledgers + default
  cost category/godown + 24 voucher types per catalog §22), a structured summary of schema changes for
  CA and Code Reviewer.

### 4. Accounting-Engine Engineer

- **Classic role mapping:** Programmer/Developer, domain-specialized on the core double-entry engine —
  the heart of the product (catalog §1).
- **Mandate / responsibilities:** Implements the double-entry ledger posting engine: groups with
  Dr/Cr nature, ledgers, all accounting voucher types (Contra/Payment/Receipt/Journal/Sales/Purchase/
  Credit Note/Debit Note and their Item/Accounting/As-Voucher modes), bill-wise allocation, cost
  category/centre allocation, budgets/scenarios/reversing journals/memoranda, interest calculation, and
  the reporting chain (Trial Balance → Trading & P&L → Balance Sheet, catalog §1, §16). Must guarantee
  every voucher balances (Dr = Cr) and honor the "To/By vs Dr/Cr" display toggle (F12).
- **When dispatched:** any phase touching ledger posting, voucher entry/edit/cancel, bill-wise
  receivables/payables, cost centres, budgets, or the primary financial statements.
- **Key tools:** Read/Edit/Write on engine source, Bash (run engine unit tests locally), Tally Domain
  Expert consultation (via CA) for any posting-rule ambiguity.
- **Inputs:** catalog §1, §4–8, ER schema from Data-Model Engineer, ADRs from Architect.
- **Outputs:** engine code + unit/integration tests, the **"Robert" (transport, accounts-only)** and
  **"Bright" (trading)** fixtures kept green (R8) — this agent is the primary owner of not breaking
  them, jointly with QA — and a structured pass/fail report to CA.

### 5. Inventory Engineer

- **Classic role mapping:** Programmer/Developer, domain-specialized on the inventory subsystem.
- **Mandate / responsibilities:** Stock items, units of measure (simple + compound), godowns/locations,
  batches (with mfg/expiry), Bill of Materials, stock vouchers (Stock Journal, Physical Stock, Delivery/
  Receipt Note, Rejection In/Out), Sales/Purchase Order processing, POS invoicing, price levels/lists,
  reorder levels & reorder-status reporting, stock ageing/movement analysis, and inventory valuation
  methods feeding into P&L (catalog §9–11). Coordinates closely with Accounting-Engine Engineer since
  Sales/Purchase vouchers span both domains (Item + Accounting allocations in one voucher).
  Also owns "inventory as-of date" queries (per project memory: this was a completed gap-fix in
  Mission A) and custom voucher types.
- **When dispatched:** any phase touching stock masters, stock vouchers, order processing, POS,
  BOM/manufacturing, batch/godown tracking, or reorder/ageing reports.
- **Key tools:** Read/Edit/Write on inventory source, Bash (run inventory unit tests), Tally Domain
  Expert consultation for valuation-method and batch/BOM fidelity questions.
- **Inputs:** catalog §9–11, ER schema, Accounting-Engine's voucher-header contracts.
- **Outputs:** inventory module code + tests, structured pass/fail + open-questions report to CA.

### 6. GST & Statutory Engineer

- **Classic role mapping:** Programmer/Developer, domain-specialized on Indian indirect-tax compliance
  — the highest fidelity-risk area of the whole clone.
- **Mandate / responsibilities:** GST masters and computation (regular intrastate/interstate, RCM,
  imports/exports/SEZ, composition scheme), ITC set-off per **Rule 88A** (IGST first, then CGST/SGST;
  CGST↔SGST never cross-used — catalog verification report item 3), GST returns (GSTR-1, GSTR-3B,
  GSTR-4 annual, CMP-08 quarterly, GSTR-2A/2B reconcile-only), e-Invoice (IRN/QR) and e-Way Bill
  (online + offline JSON), HSN summary, and TDS/TCS masters + computation + returns (catalog §12–13).
  Must track that GST is a **live regulatory domain** — any slab/threshold claim (e.g. the unconfirmed
  GST 2.0 5/18/40% restructuring flagged in the catalog) is web-verified against official CBIC/GST
  portal sources before being coded as a constant, never asserted from training-data memory.
- **When dispatched:** any phase touching GST/TDS/TCS masters, computation, or returns; whenever a
  statutory rate/threshold/rule needs confirmation.
- **Key tools:** Read/Edit/Write on statutory module source, Bash (tests), WebSearch/WebFetch for
  official rate/notification verification, Tally Domain Expert for how Tally itself models/displays it
  (the GST-Notes PDF is the primary corpus source for this agent per project memory).
- **Inputs:** catalog §12–13, official CBIC/GST-portal sources, Tally Domain Expert corpus citations.
- **Outputs:** GST/TDS/TCS module code + tests, a structured citation trail (official source + corpus
  page) for every rate/rule implemented, flags raised to CA/user for any law fact that cannot be
  confirmed (R12 — never silently guess).

### 7. Payroll Engineer

- **Classic role mapping:** Programmer/Developer, domain-specialized on payroll & labor-statutory
  compliance.
- **Mandate / responsibilities:** Payroll masters (employee, employee group, pay head, attendance/
  production types), pay-head computation (earnings/deductions, formula-based pay heads), statutory
  computation — **PF** (Employer EPF = 12%×PF-wage − EPS, where EPS = 8.33%×min(wage,₹15,000) capped at
  ₹1,250, per catalog verification item 7), **ESI**, **NPS**, gratuity — payroll voucher processing, and
  payroll statutory reports (PF/ESI challans, Form 16, payslips). Same live-regulation caution as the
  GST engineer: statutory constants are web-verified, cited, and dated.
- **When dispatched:** any phase touching payroll masters, pay-head computation, statutory payroll
  calculations, or payroll reports.
- **Key tools:** Read/Edit/Write on payroll module source, Bash (tests), WebSearch/WebFetch for EPFO/
  ESIC official confirmation, Tally Domain Expert for corpus-grounded payroll UX/field fidelity.
- **Inputs:** catalog §14, official EPFO/ESIC sources, Tally Domain Expert citations.
- **Outputs:** payroll module code + tests, cited statutory-constant table, structured report to CA.

### 8. Frontend / UX Engineer

- **Classic role mapping:** UX Designer / Software Designer (`process-and-teams.md` §1) fused with the
  Programmer who implements the GUI layer — split further if the project scales.
- **Mandate / responsibilities:** The keyboard-first, single-window UI: Gateway of Tally (GOT) home hub,
  Create/Alter universal verbs, the full F-key/Alt/Ctrl shortcut map (catalog §21), Go To (`Alt+G`) /
  Switch To (`Ctrl+G`) jump navigation, right-hand context button bar, drill-down-everywhere on every
  report figure, F11 (Company Features) / F12 (Configuration) toggle screens, and print/export/email UX
  (catalog §17). Wireframes/mockups new screens before building (`design-ux-uml.md` discipline — cheap
  on paper, expensive in code) and keeps GUI/logic separated from the engine layers per the Architect's
  module boundaries.
- **When dispatched:** any phase introducing a new screen, voucher form, report view, or shortcut;
  whenever a UX behavior needs to be checked against the catalog's navigation/shortcut tables.
  Also owns register-action shortcuts (e.g. Alt+I Insert, per project memory Mission A gap #6).
- **Key tools:** Read/Edit/Write on UI source, `mcp__Claude_Preview__*` tools (start/screenshot/snapshot/
  inspect/click/fill the running app) to verify real behavior, `ui-ux-pro-max`/`ui-styling` skills as
  needed, Tally Domain Expert consultation for exact navigation/shortcut fidelity.
- **Inputs:** catalog §0, §17, §21, ADRs from Architect, engine contracts from domain engineers.
- **Outputs:** UI code + wireframe notes, a Preview-tool-verified behavior report (screenshots/snapshots
  attached to structured output), fidelity checklist vs. catalog shortcuts/navigation.

### 9. QA / Test Engineer

- **Classic role mapping:** Software Tester (`process-and-teams.md` §1) — the dedicated, objective
  validator distinct from the developers who wrote each module ("no one is objective about their own
  code").
- **Mandate / responsibilities:** Owns the test strategy across unit/integration/system levels
  (`testing.md`), and specifically **owns the "Robert" (transport, accounts-only) and "Bright" (trading)
  regression fixtures** as the canonical baseline for the ledger engine (R8) — runs them on every
  phase's changes and reports any drift. Writes/maintains integration tests that cross module
  boundaries (e.g. a Sales voucher that touches Accounting + Inventory + GST simultaneously), tracks
  coverage, and applies the 7 principles of testing (defects cluster, pesticide paradox, etc.) when
  deciding where to add depth. TDD collaborator with each domain engineer, not merely a late-stage
  gatekeeper.
- **When dispatched:** continuously within each phase (alongside implementation, not just at the end);
  mandatorily before every phase gate (R9) to show green tests.
- **Key tools:** Bash (run test suites), Read/Edit/Write (test files), `superpowers:test-driven-
  development`, `superpowers:verification-before-completion` skill discipline (evidence before
  assertions).
- **Inputs:** all domain engineers' code, `plan.md` acceptance criteria, catalog behavior specs.
- **Outputs:** test suites, a structured green/red report (with Robert/Bright fixture status called out
  explicitly) gating every phase close.

### 10. Code Reviewer

- **Classic role mapping:** not a distinct role in the classic 6-role table, but institutionalized here
  per XP practice (`process-and-teams.md` §3.2 — "Code Reviews," "continuous informal review... every
  line seen by at least two people") and R10.
- **Mandate / responsibilities:** Adversarial review of every substantial change before merge (R9, R10):
  correctness bugs, security issues, reuse/simplification opportunities, catalog-fidelity regressions,
  and consistency with the Architect's ADRs. Explicitly hunts for the failure modes the project has been
  burned by before (per project memory: leftover scratch files, non-deterministic test helpers, IP-leak
  verbatim quotes from source PDFs, stale doc-claims) — i.e. reviews adversarially, not performatively,
  and does not accept "tests are green" alone as proof of correctness.
- **When dispatched:** before every merge of a substantial change (R10); the CA also dispatches this
  agent for a second adversarial pass at each phase gate even when the implementing engineer's own tests
  are green.
- **Key tools:** `code-review` skill / Read/Grep/Bash (diff inspection), `ReportFindings`-style
  structured findings output.
- **Inputs:** the diff/PR under review, `plan.md` item it's tied to, catalog fidelity requirements.
- **Outputs:** a structured findings list (severity-ranked), a merge go/no-go recommendation to CA.

### 11. Tally Domain / Corpus Expert

- **Classic role mapping:** a domain Subject-Matter Expert layered on top of Stakeholders/Customers
  (`process-and-teams.md` §1) — stands in for "the customer who knows exactly how Tally behaves,"
  grounded in primary sources rather than opinion.
- **Mandate / responsibilities:** Resolves any Tally-behavior question against the ground truth: **the
  5 git-ignored Tally Prime PDFs in `tally/`** plus `docs/tally-feature-catalog.md` and its verification
  report. Reads PDFs via `pdftotext` (no `pdftoppm` available in this environment). Cites page/section
  for every fidelity ruling it makes so other agents and the Code Reviewer can verify. This is the
  **only** agent that opens the `tally/` PDFs directly — everyone else consumes its citations or the
  catalog, keeping the raw copyrighted PDFs out of prompts/commits. Dispatchable as
  `.claude/agents/tally-corpus-expert.md`.
- **When dispatched:** whenever any engineer (Accounting, Inventory, GST, Payroll, Frontend) hits a
  behavior ambiguity the catalog doesn't fully resolve; during catalog authoring/refresh; during
  fidelity-matrix audits.
- **Key tools:** Bash (`pdftotext -layout` extraction), Read/Grep over extracted text, Write (updates to
  `docs/tally-feature-catalog*.md` and `docs/tally-reference-index.md`).
- **Inputs:** the `tally/` PDF corpus, specific fidelity questions from other agents.
- **Outputs:** cited rulings (page/section references, never verbatim long quotes — R4/IP discipline),
  catalog corrections/enrichments, fidelity-matrix updates.

### 12. GitHub Expert (Release & Repo Manager)

- **Classic role mapping:** a specialized extension of the classic team with **full standing authority**
  — not present in the textbook's 6 roles, added because this project centralizes all VCS/release risk
  in one accountable owner (R4).
- **Mandate / responsibilities:** A hired specialist with 20+ years of experience holding **full
  standing authority** over `https://github.com/Shuvrajit10101/Apex-Solutions`. Performs **every**
  git/GitHub action — repo init, branching strategy, commits, PRs, pushes, tags, releases, `.gitignore`
  maintenance (must permanently exclude the `tally/` PDFs — third-party IP, R4), repo settings, CI/CD
  pipeline configuration, issues/milestones — **without asking permission**. He is the **only** agent,
  and the main loop is explicitly forbidden, from touching git/GitHub (R4). Commits in small, coherent,
  conventional-commit units tied to `plan.md` items (R10), and only after the Code Reviewer has passed
  the change.
- **When dispatched:** at every commit-worthy checkpoint; at every phase-gate push (R9); for tag/release
  creation; for any repo/CI configuration change.
- **Key tools:** Bash (`git`, `gh` CLI), GitHub REST API.
- **Inputs:** reviewed, test-green changes from domain engineers + Code Reviewer's go signal.
- **Outputs:** commits/PRs/tags/releases on GitHub, a structured summary (SHAs, PR links, tag names) for
  CA to relay to the user and log into `memory.md`.

### 13. Technical Writer

- **Classic role mapping:** a documentation specialization pulled out of the Programmer role, per
  `deployment-docs-maintenance.md`'s split of product docs (system docs for maintainers, user docs for
  end users) — both are first-class deliverables, not a Programmer afterthought.
- **Mandate / responsibilities:** Writes and maintains **user documentation** (feature how-tos, keyboard
  shortcut reference, onboarding for a new company) and **system/maintainer docs** (module READMEs,
  ADº index, setup/build instructions). Keeps `docs/` in sync with what actually shipped each phase —
  documents as you go, not after the fact (`software` skill cross-cutting principle). Also maintains the
  user-facing changelog tied to GitHub Expert's releases.
- **When dispatched:** at the close of every phase (R11 — "docs/user-notes updated" is part of Definition
  of Done); whenever a new feature/screen/shortcut ships.
- **Key tools:** Read/Write/Edit on `docs/` and README-style files, screenshots via
  `mcp__Claude_Preview__preview_screenshot` for user docs.
- **Inputs:** shipped feature list from CA, catalog behavior descriptions, Frontend Engineer's UX notes.
- **Outputs:** updated user docs, system/maintainer docs, changelog entries — all handed to GitHub Expert
  to commit.

### 14. Verification / Completeness Critic

- **Classic role mapping:** an independent-audit specialization of the Tester role, elevated to a
  standalone agent because this project's history shows "green gates hide real bugs" (project memory) —
  a dedicated adversarial completeness check, distinct from QA's day-to-day green/red test running.
- **Mandate / responsibilities:** Before any phase is declared closed, audits the phase's actual work
  against `plan.md`'s stated scope for that phase **and** the full catalog fidelity matrix — looking for
  silently-dropped scope, gaps between "tests pass" and "feature actually matches Tally," and stale
  claims in docs/memory that workflow agents missed (the exact class of bug this project has been burned
  by before: leftover scratch files, non-deterministic helpers, IP-leak quotes, stale doc-claims per
  project memory). Explicitly re-derives the fidelity-matrix gap list rather than trusting the last
  agent's self-report.
- **When dispatched:** mandatorily at the end of every phase, after QA and Code Reviewer both report
  green, and before CA declares the gate open (R9).
- **Key tools:** Read/Grep across `plan.md`, `docs/tally-feature-catalog.md`, the fidelity-matrix doc,
  and the actual shipped code/tests; Bash for spot-checks (running the app, re-running fixtures).
- **Inputs:** everything the phase produced, `plan.md`, the catalog, `memory.md` history of prior misses.
- **Outputs:** a structured audit verdict (complete / gaps-found-with-list), which blocks or unblocks
  the phase gate independent of QA's own sign-off.

---

## Dispatch conventions

- Agents are instantiated **inside `Workflow` scripts** as **structured-output subagents** — never as
  free-form chat delegation from the main loop.
- Every dispatch **names the agent** from this roster and **cites its mandate** (copy the relevant
  bullet from its "Mandate / responsibilities" above) directly in the subagent prompt, so the subagent
  knows its scope, authority boundary, and what NOT to touch (e.g. every non-GitHub-Expert agent is
  explicitly told not to run git commands).
- The CA sequences dependent agents serially (e.g. Data-Model Engineer → Accounting-Engine Engineer) and
  fans out independent agents in parallel (e.g. GST Engineer + Payroll Engineer + Technical Writer on
  unrelated files) within one `Workflow` script.
- Every agent returns a compact structured result (pass/fail, findings, file paths touched, open
  questions) — never a raw file dump — so the main loop's synthesis stays token-lean (R14).
- If a task doesn't fit any agent above, the CA (or the main loop, if CA itself is missing scope) adds a
  new agent section to this file — following the same five-part template (Mandate, When dispatched, Key
  tools, Inputs/Outputs, classic-role mapping) — before doing the work.
