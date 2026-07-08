# memory.md — Apex Solutions running log

> **Read this file first in any new session.** It is the complete, chronological record of everything we
> have done and decided, so a fresh session can resume with zero re-explanation (per CLAUDE.md R5).
> Newest entries at the bottom of each session. Every task appends here — without fail.

---

## Project snapshot (keep current)
- **Goal:** Clone Tally Prime with the same features.
- **Feature reference:** `docs/tally-feature-catalog.md` (+ `...-verification-report.md`).
- **Source corpus:** `tally/` — 10 Tally Prime / Tally.ERP9 PDFs (git-ignored, never commit).
- **Repo:** https://github.com/Shuvrajit10101/Apex-Solutions (managed solely by the GitHub Expert agent).
- **Operating model:** agentic workflows do the work; the main window only orchestrates (CLAUDE.md R2).
- **Current status:** **Phases 0–2 COMPLETE & shipped.** Accounting core + advanced accounting all live;
  main `84aa3d3`, **237 tests**, CI ×3 OS, de-branded (**Apex Solutions**), cascading Miller-column UI.
- **Current phase in plan.md:** **Phase 2 COMPLETE** (Bill-wise, Cost Centres, Budgets, Banking, Scenarios,
  Interest, Multi-currency — PRs #10–#17; SQLite schema **v8**). Repo public (revisit private+Pro at launch).
  **Phase 3 (Inventory) — pending user go-ahead.**
- **Confirmed stack:** C#/.NET + **Avalonia** (Win+Linux+macOS) + **SQLite**; core lib `Apex.Ledger`; xUnit;
  GitHub Actions. UI = **pixel-level** Tally mimicry. GST slabs config-driven (classic 0/5/12/18/28 seeded).

---

## Session S1 — 2026-07-02

### Context at start
- Fresh start. Working directory contained only `tally/` (10 PDFs) and `.claude/`. No prior source code,
  docs, or git repo (the auto-memory's "v0.3.0 / 411 tests" project was **not** present here).

### Tasks done
1. **Studied all 10 `tally/` PDFs.** Extracted text via `pdftotext -layout`; read by 5 parallel study
   agents (Book, Study Guide, GST notes ×3, Tally-Book+Fundamentals, Case-Study+Practical). Noted the
   `Short-Key.pdf` shortcut list is garbled/unreliable.
2. **Wrote `docs/tally-feature-catalog.md`** — a 24-section developer-facing inventory of Tally Prime's
   entire feature surface (foundations & double-entry, company, 28-group chart of accounts, 24 voucher
   types, bill-wise, cost centres, budgets/scenarios, banking, inventory + advanced inventory, order &
   job-work, GST, TDS/TCS, payroll, reports, printing/export, security, data management, F11/F12 config,
   keyboard reference, seed-data appendix, scope map).
3. **Ran a verification workflow** (5 web-verifiers + 5 completeness critics + synthesizer) →
   `docs/tally-feature-catalog-verification-report.md` (27 sourced corrections). Folded resolutions into
   a verification banner in the catalog and fixed the Physical Stock shortcut. (2 critics —
   Reports/Admin and Payroll/Costing — dropped on connection errors; those areas got lighter auditing.)
   - Key verified facts: **28** predefined groups (Bank OCC A/c = alias of Bank OD A/c); Physical Stock
     via **F10** (no dedicated key); GST ITC set-off per **Rule 88A**; **GSTR-4** annual / **CMP-08**
     quarterly; GSTR-2A/2B auto-drafted; legacy VAT/CST/Excise/Service Tax **retained** (optional);
     current release **TallyPrime 7.0 (19-Dec-2025)**; employer PF is **computed** (not flat 3.67%);
     **GST 2.0** slab change (5/18/40%) **unconfirmed** — needs official CBIC check before use.
4. **Established governance files:**
   - `CLAUDE.md` — strict rules **R1–R14** (R1–R6 = user's rules; R7–R14 = added engineering discipline).
   - `memory.md` — this running log.
   - `agents.md` — agent roster incl. the **GitHub Expert** (drafting delegated to a `/software`
     workflow — *in progress*).
   - `plan.md` — master plan built from the study via `/software` (drafting delegated — *in progress*).

### Decisions
- **D1:** Repo = https://github.com/Shuvrajit10101/Apex-Solutions; all git/GitHub via the GitHub Expert
  agent only, no permission prompts (CLAUDE.md R4).
- **D2:** Operating model = agentic-first, clean main window (CLAUDE.md R2).
- **D3:** `tally/` PDFs are never committed (IP; git-ignored).

### Open / pending user
- **Confirm the tech stack / target platform** (proposed in `plan.md`) before any building begins.
- After approval: begin **Phase 0** (repo init by GitHub Expert + project scaffold).

### User feedback captured this session
- "I only said to study them first" → honour the exact scope of each ask; don't run ahead into build
  planning/architecture until explicitly asked. (Also saved to auto-memory.)
- Set up strict governance (this session's CLAUDE.md/agents.md/memory.md/plan.md request).

### S1 update — governance & plan complete (later same session)
- `agents.md` written: **14-agent roster** incl. the **GitHub Expert** (sole git/GitHub authority over the
  repo, no permission prompts). Roster: CA/Orchestrator, Software Architect, Data-Model/DB, Accounting-Engine,
  Inventory, GST/Statutory, Payroll, Frontend/UX, QA/Test, Code Reviewer, Tally Corpus Expert (A14), GitHub
  Expert, Technical Writer, Verification/Completeness Critic.
- `plan.md` written via `/software`: **12 phases (0–11)** — 0 Setup · 1 Accounting core (Robert/Bright) ·
  2 Bill-wise/Banking/Cost/Budgets/Scenarios/Interest/Multi-currency · 3 Inventory · 4 GST MVP (GSTR-1/3B) ·
  5 Reports/print/export · 6 Advanced inventory · 7 TDS/TCS · 8 Payroll · 9 GST-advanced/returns/e-invoice ·
  10 Security/audit/data-mgmt · 11 Hardening & v1.0.0. Includes stack-agnostic domain model, testing strategy,
  GitHub-Expert release flow, risk table, and **7 open questions**.
- **Proposed stack (pending user confirm):** TypeScript end-to-end — framework-agnostic `@apex/ledger-core`
  library + SQLite (better-sqlite3, migrations) + **Tauri** desktop shell (Electron fallback) + React
  keyboard-first UI + Vitest/Playwright + GitHub Actions CI.
- Coverage critic reviewed plan.md; gaps folded into **plan.md §10 (C-1…C-9)**: multi-currency (was missing),
  Budgets/Scenarios/Interest → Phase 2, Bill Settlement (Ctrl+B), party multi-address, TDS/TCS ancillary
  forms, GST Rate Setup + GSTR-9C + IMS-local, Edit-Log-vs-Tally-Audit split, composition interim note,
  and re-verify phase-critical law at Phase 4/7 kickoffs.

### Next action
- Present the setup report + confirmation questions (tech stack, OS target, fidelity bar, GST slab target).
  **Do NOT start building or touch GitHub until the user approves.** On approval → **Phase 0** (GitHub Expert
  initialises the repo + scaffold; finalise dispatchable agent stubs).

### S1 decisions — stack LOCKED (user, 2026-07-02)
- **Q1 Stack:** C# / .NET (latest LTS) + **Avalonia** (cross-platform XAML) + **SQLite**; accounting core =
  framework-agnostic C# class library **`Apex.Ledger`**; tests **xUnit**; CI **GitHub Actions**.
- **Q5 OS target:** **Windows + Linux + macOS** at v1.0 (Avalonia chosen over WPF for cross-platform).
- **Q7 Fidelity:** **pixel-level mimicry** of Tally Prime's actual screens.
- **Q2 GST slabs:** config-driven; seed classic **0/5/12/18/28 + Cess**; add GST 2.0 (5/18/40) after CBIC
  confirmation at Phase 4.
- `plan.md` §3 updated to **CONFIRMED** (§3.0 locked-decisions table); §9.2 marks Q1/Q5/Q7/Q2 resolved.
  Still-open Qs: 206C(1H) status (Phase 7), online round-trips (offline-only unless changed), legacy
  VAT/Excise scope (out).
- **Toolchain (verified 2026-07-02):** git 2.53 ✅ · gh 2.95 ✅ (authed as `Shuvrajit10101` = repo owner,
  `repo` scope → GitHub Expert ready) · **.NET SDK 10.0.301** installed user-local at `~/.dotnet` (user PATH
  + DOTNET_ROOT set). **Gotcha:** the harness env predates the install, so new tool shells must prepend
  `export PATH="$HOME/.dotnet:$PATH"` (Bash) to see `dotnet`.
- **Now starting Phase 0** — scaffold via an agentic workflow; GitHub Expert inits/pushes the repo.

### S1 — Phase 0 COMPLETE (scaffold built + repo pushed) 2026-07-02
- **Scaffold (green):** .NET 10 solution **`Apex.slnx`** (new .slnx format) — `src/Apex.Ledger` (core: `Money`
  readonly struct + `DrCr` enum), `src/Apex.Desktop` (Avalonia 12.0.5 MVVM shell), `tests/Apex.Ledger.Tests`
  (xUnit: 2 passing + 2 skipped fixture-loaders). **Independently re-verified in main loop:**
  `dotnet build -c Release` = 0 warn/0 err; `dotnet test -c Release` = Passed 2, Skipped 2, Failed 0.
- **Docs:** `docs/adr/0001-tech-stack.md`, `docs/srs/SRS-0-skeleton.md`, `README.md`, `.gitignore`
  (excludes `tally/`, bin, obj, .dotnet, *.db), `.editorconfig`.
- **Fixtures (data only):** `tests/Apex.Ledger.Tests/Fixtures/robert.json` (137000=137000, 13 vouchers) and
  `bright.json` (273000=273000; 10% machinery deprec 6000; closing stock 15000; ~₹1000 loss). Real engine
  assertions deferred to Phase 1 (skipped test `Skip="ledger engine arrives in Phase 1"`).
- **Repo:** remote `Shuvrajit10101/Apex-Solutions` was **PRIVATE** with an **EMPTY** "Initial commit"
  (ae19c94, 0 files, authored by owner today). GitHub Expert **force-pushed with lease** (safe — nothing
  lost) → remote HEAD = **53bcd1c** = local. **`tally/` confirmed ABSENT from the remote tree.** CI
  (`.github/workflows/ci.yml`, ubuntu/windows/macos matrix, setup-dotnet 10.0.x) triggered → run
  **28570654833** (watching for green). 32 files tracked.
- **Gotcha reminders:** all `dotnet` calls need `export PATH="$HOME/.dotnet:$PATH"` first; solution is
  `.slnx` not `.sln`.

### Phase 0 gate (pending user go-ahead)
- **CI GREEN** on run 28570654833 — Build & Test success on **ubuntu + windows + macos**. Phase 0 gate met;
  presented to the user 2026-07-02. **Do NOT start Phase 1 until the user approves.** Phase 1 = accounting
  core (Company + 28-group/2-ledger seed + core vouchers + Trial Balance/Day Book/Balance Sheet/P&L;
  Robert & Bright become **live** regression tests).

### Phase 0 adjustments (user-approved, applied 2026-07-02)
- **Repo is now PUBLIC.**
- **Hygiene files** added + pushed to main (commit **c790716**): `LICENSE` (**MIT**), `CONTRIBUTING.md`,
  `.github/CODEOWNERS` (`* @Shuvrajit10101`), issue templates (bug/feature), PR template.
- **Branch protection on `main`:** PR required (0 approvals — solo owner not blocked); required status checks
  = the 3 CI contexts (ubuntu/windows/macos), strict; **enforce_admins = true → NO direct pushes to main**;
  force-push & deletion blocked. → **From Phase 1 on, ALL changes land via feature branch → PR → green CI →
  merge** (GitHub Expert drives this; R10).
- **LICENSE → PROPRIETARY (resolved).** User will SELL the software, so MIT was replaced with an
  "Apex Solutions — Proprietary License / All Rights Reserved" via **PR #1** (protected-branch flow: CI
  green on ubuntu/windows/macos, squash-merged). **main HEAD = `af1d0cf`.** README needed no change (its
  MIT/license mentions were about the git-ignored `tally/` PDFs). Validated the R10 branch→PR→CI→merge flow.
- **CI green** confirmed on `c790716` (hygiene commit) across all 3 OSes.
- **DECISION — repo visibility (user 2026-07-02): STAY PUBLIC for now.** On the Free account, going private
  would DISABLE server-side branch protection (verified: on Free, both classic protection AND rulesets are
  public-repo-only; private needs **GitHub Pro ~$4/mo**). Staying public keeps enforced protection free while
  building. **Revisit before sale/launch:** upgrade `Shuvrajit10101` to Pro, then flip private (protection
  re-activates). Until then the source is publicly visible — public is NOT the final state.

## Session S1 — Phase 1: accounting-core engine COMPLETE (merged) 2026-07-02
- **PR #2 merged → `main` `ff64327`** (CI green ubuntu/windows/macos). Independently re-verified locally:
  `dotnet test -c Release` = **Passed 30 / Failed 0 / Skipped 0**.
- **Delivered (`src/Apex.Ledger`, framework/DB-independent — no Avalonia/SQLite refs, no double/float):**
  domain (Company/Group/Ledger/VoucherType/Voucher/EntryLine; stable Guid ids; `Money`=decimal),
  `CompanyFactory.CreateSeeded` (EXACTLY 28 groups + 2 ledgers + 24 voucher types + Primary Cost Category +
  Main Location + ₹/INR 2-dp + FY 1-Apr), `LedgerService` (balanced-posting fail-fast, Cancel keeps number /
  Delete gaps, auto per-type numbering), reports **TB / P&L / BalanceSheet / DayBook / LedgerBook**. Design:
  `docs/design/accounting-core.md`. `Persistence/Repositories.cs` = ports only (no SQLite impl yet).
- **Fixtures now LIVE regression tests (to the paisa, per-ledger + per-line):** Robert TB 137000=137000, net
  5000, BS 105000; Bright TB 273000=273000, gross 15000, net −1000, BS 184000. Bright uses periodic-inventory
  trading treatment (opening stock charged; closing stock via a paired Direct-Income adjustment).
- **Reviewer APPROVE, 0 critical.** Minor follow-ups (in PR #2 body — address before custom-master phases):
  1. ⚠ **Gross-profit/stock classification keys off group NAME** (`ProfitAndLoss.cs`, `ClassificationRules.cs`)
     — contradicts the design's rename-safe identity guarantee; latent bug if a predefined primary group is
     renamed (does NOT affect NetProfit/BS; fixtures use canonical names). **Fix before Phase 3+.**
  2. Delete invariants stored (`IsPredefined`) but NOT enforced (no master-delete surface yet).
  3. `LedgerService.Post` mutates caller's `Voucher.Number` in place (public setter).
  4. Closing-stock periodic-inventory pairing is an undocumented-in-code precondition.
  5. `VoucherValidator` doesn't check duplicate line ledgers / PartyId refs.
- **Remaining Phase 1 slices (before the phase gate):** (a) **SQLite persistence** (implement the repository
  ports → one `.db` per company + round-trip test); (b) **runnable Avalonia UI** (GOT → create company →
  enter Robert's 13 vouchers → show Trial Balance/Balance Sheet; pixel-level Tally look) — satisfies R9
  'run the real app' and the Phase-1 exit gate.
- **Process notes:** every change lands via feature branch → PR → CI → squash-merge (main protected). Local
  `main` = `ff64327`. **`memory.md` is left uncommitted by the GitHub Expert on feature PRs** → the committed
  copy on `main` is stale; sync it via a `docs: update project log` PR at each phase gate so a fresh clone
  resumes correctly.

### Phase 1 — SQLite persistence COMPLETE (merged) 2026-07-02
- **PR #3 merged → main `af8ede7`** (CI green ×3 OS). Verified locally: **33 tests** (30 core + 3
  persistence); persistence source IS tracked; `tally/` still excluded (0 tracked).
- **New `src/Apex.Persistence.Sqlite`** (Microsoft.Data.Sqlite; **one `.db` per company**; money = INTEGER
  **paisa**, no float; FKs; `schema_version`=v1; transactional save; Load re-posts vouchers via LedgerService).
  `Apex.Ledger` stays SQLite-free. Round-trip test: seed → post Robert → save → reload-fresh → TB 137000 /
  BS 105000 / net 5000 to the paisa, counts survive.
- **Reviewer caught a CRITICAL (fixed pre-merge):** a bare `*.sqlite` in `.gitignore` matched the DIRECTORY
  `src/Apex.Persistence.Sqlite/` → would have git-ignored the entire persistence source (broken clones).
  Fixed to `*.sqlite3` + `*.db`. **Minor follow-up:** bare `.sqlite` files no longer ignored → name the app
  DB `.db` (or re-add an anchored ignore). Other minors: Paisa hardcodes 2dp; enums persisted as ordinals
  (bump schema_version on reorder); Save assumes parent-before-child group insert order.
- **Phase 1 remaining:** the runnable **Avalonia UI** (in progress) → then RUN the app = Phase 1 gate.

### Phase 1 — Avalonia UI COMPLETE (merged) 2026-07-02
- **PR #4 merged → main `c370415`** (CI green ×3 OS). Verified locally: **41 tests** (30 core + 3 persistence
  + 8 desktop, incl. a HEADLESS Avalonia UI test that boots the real window and drives Load Robert Demo →
  Balance Sheet = 1,05,000; + a live key-input test). Full solution builds 0/0. Apex.Desktop = 24 files
  tracked; `tally/` still 0.
- **`src/Apex.Desktop`** (Avalonia 12 MVVM, keyboard-first single window, wired to Apex.Ledger +
  Apex.Persistence.Sqlite): **Tally look** — navy (#1B3A6B) top/status bars, right F-key button bar
  (#E8E8E8), cream (#FDFDF5) work area, amber (#FFD54F) menu highlight, Indian lakh/crore grouping,
  monospace grids. Screens: Company Select (list `.db` / Create / **Load Robert Demo**), Create Company
  (CreateSeeded → save `.db`), Gateway of Tally, reports **Trial Balance / Balance Sheet / P&L / Day Book**
  computed from the real engine over posted vouchers (no canned numbers). Companies →
  `%AppData%/ApexSolutions/Companies/<name>.db`. Robert demo embedded (no tests/ runtime dep). Added additive
  `SqliteCompanyStore.ListCompanies()`.
- **Reviewer APPROVE, 0 critical.** Minor: F4–F9 button keys labeled Contra/Payment/… but all open Day Book
  (PLACEHOLDER — voucher entry deferred); 'Display More Reports' placeholder; report-grid Credit/Amount share
  a column (fragile); design-time VM touches filesystem.
- **Run cmd:** `export PATH="$HOME/.dotnet:$PATH" && dotnet run --project src/Apex.Desktop`.

## ✅ PHASE 1 COMPLETE — accounting core: double-entry engine + SQLite persistence + runnable Tally-styled UI. main `c370415`, **41 tests green**.
- **Honest scope note:** the app RUNS, loads Robert's 13 vouchers (via demo, posted through the engine), and
  shows correct Trial Balance/Balance Sheet/P&L/Day Book. **Deferred (early Phase 2 or a Phase-1 top-up):**
  manual voucher-ENTRY screens (F4–F9 are placeholders), ledger master create/list, F11/F12 panels,
  ledger-book drill-down, and the 5 engine minor follow-ups (esp. name-based classification).
- **Next gate:** user reviews the running app → then **Phase 2** (Bill-wise + Banking + Cost Centres +
  Budgets/Scenarios/Interest + Multi-currency), OR a short Phase-1 top-up to add voucher-entry screens first.

### Phase 1 — voucher entry + de-brand COMPLETE (merged) 2026-07-02
- **PR #5 (`31263ad`):** voucher ENTRY screens (Contra F4 / Payment F5 / Receipt F6 / Journal F7 / Sales F8 /
  Purchase F9) — Dr/Cr grid + ledger picker + live balance check + **Ctrl+A** save via LedgerService.Post +
  SQLite persist; **Ledger master** (create/list, Alt+C); F4–F9 rewired to real vouchers. Tests: Receipt
  posts→persists→shows in Day Book; unbalanced rejected. **50 tests** green. Reviewer APPROVE / 0 critical.
- **PR #6 (`0cdfb2f`) — DE-BRANDED:** removed ALL user-facing + code "Tally" from `src/`+`tests/`
  (tracked-source count = **0**; the 68 raw grep hits were vendored-binary false positives). Brand =
  **Apex Solutions** ("Gateway of Apex Solutions", window title "Apex Solutions"); 22 App.axaml resource
  keys renamed (TallyNavy→BrandNavy, TallyRed→AlertRed, …) with all refs updated. **UI text bugs fixed:**
  report Credit/Amount column overlap (ReportRow.IsTwoColumn per-cell visibility); inverted
  unbalanced-difference color (BalancedToBrushConverter → red #B00020, bold); + 6 defensive TextTrimming
  fixes. 50 tests green.
- **Scope kept (user):** internal `docs/`/`tally/` still reference Tally; revisit docs + repo privacy before
  launch. (memory.md itself still contains "Tally" — internal log, out of product scope.)

## ✅✅ PHASE 1 DONE — engine + SQLite persistence + interactive de-branded UI. main `0cdfb2f`, **50 tests green**, CI ×3 OS.
- App relaunched as **Apex Solutions**. Non-blocking follow-ups (address opportunistically): the 5 engine
  minors (esp. **name-based gross-profit classification** — fix before custom masters), voucher-entry
  cosmetic nits (global Ctrl+A, live-ledger combo refresh), item/accounting voucher modes (Phase 3),
  ledger alter/delete, F11/F12 config panels, ledger-book drill-down, the `*.sqlite`-ignore gap.
- **Next: Phase 2** — Bill-wise + Banking + Cost Centres + Budgets/Scenarios/Reversing/Memoranda/Interest +
  Multi-currency (plan §5 + §10 C-1..C-3) — pending user go-ahead.

### Phase 1 — professional Gateway hierarchy (merged) 2026-07-03
- **PR #7 (`5b6ed24`)** — user feedback: the flat menu looked unprofessional. Reorganized the Gateway into
  **sections Masters / Transactions / Reports** with items nested under each; all 6 voucher types now under a
  **"Vouchers"** submenu (Transactions → Vouchers → Contra/Payment/Receipt/Journal/Sales/Purchase);
  **Create → Ledger** submenu; new **Chart of Accounts** screen nesting the 13 sub-groups under their 15
  primary parents (ledgers under their group). Section headers non-selectable, arrow-nav skips them; F4–F9
  still global; Esc steps back. Still de-branded (tracked-source "Tally" = 0). **59 tests** green (26 desktop;
  +9 hierarchy tests). Reviewer approved; 3 cosmetic minors (chart converter semantic mislabel; Create→Group
  is a Ledger-master placeholder; dual P&L-head source). main now **`5b6ed24`**.
- **Standing rule saved:** professional hierarchy on every screen (nest under parent sections; no flat dumps)
  — auto-memory [[ui-professional-hierarchy]]. Also: [[no-tally-branding-in-product]] (no "Tally" in product).

### Phase 1 — cascading Miller-column navigation (merged; user-approved "closure") 2026-07-03
- **PR #8 (`abd12ab`)** — replaced the single-pane Gateway with a **cascading multi-column ("Miller")** nav:
  main menu (left) → open a Group → submenu column (middle) → open a leaf → page column (right); earlier
  columns persist (parent dim "selected-but-inactive", active column amber); changing an earlier selection
  discards downstream columns. Keyboard: Up/Down (skip headers) / Right·Enter drill / Left·Esc pop. Each column
  has its own header + independent scroll; viewport auto-scrolls to the active rightmost; clean/un-clipped
  (Trial Balance Dr/Cr now separate columns; Balance Sheet clean). Impl: `GatewayColumn` + `MenuItemKind`
  (Action/Group/Page). Built LIVE via a `/loop` (iterated + relaunched for the user to watch); user said **closure**.
- **PR #9 (`8b8e075`)** — review-caught fix: opening a page via an **F-key/report-hotkey while a page was
  already open** appended a 2nd page instead of REPLACING it (violated "the page should get replaced"). Fixed:
  `OpenPageColumn` trims to `LastMenuColumnIndex()` first → at most one page column; replace on every path.
  +3 regression tests. main **`8b8e075`**; CI ×3 OS; **verified locally: build 0/0, 73 tests green, 0 "Tally".**
- **STANDING RULE (user 2026-07-03):** apply this SAME cascading-column pattern to **ALL future features** —
  auto-memory [[cascading-column-navigation]]. Cosmetic follow-ups (non-blocking): string-based menu dispatch
  (carry action/kind on the item vs label-match), OpenGroupOf default-switch guard, stale CurrentGatewayMenu on
  step-back, ColumnWidthConverter doc says 560 but returns 640.

## ▶ PHASE 1 fully complete & polished — engine + SQLite + interactive de-branded UI + professional cascading navigation. main `8b8e075`, **73 tests green**, CI ×3 OS.
- **NEXT: Phase 2** — Bill-wise + Banking + Cost Centres + Budgets/Scenarios/Reversing/Memoranda/Interest +
  Multi-currency (plan §5 + §10 C-1..C-3). Each new feature plugs into the **cascade** + **hierarchy** rules
  and stays de-branded. **STARTED 2026-07-03 via `/loop` ("start and finish Phase 2 in one go, until perfect").**

### Phase 2 — progress tracker (self-paced loop; each slice = engine TDD + cascade UI + reviewed PR)
- [x] **2.1 Bill-wise** ✅ — **PR #10 (`1645feb`)**; 4 ref types + split, Outstandings (Receivables/Payables)
      + ageing, **Bill Settlement Ctrl+B**, SQLite schema **v2**. Review APPROVE/0-critical; **97 tests**.
- [x] **2.2 Cost Categories & Cost Centres** ✅ — **PR #11 (`183f00c`)**; category + hierarchical centres,
      per-line cost allocation, 3 cost reports, SQLite **v3**. Review APPROVE/0-issues; **125 tests**.
- [x] **2.3 Budgets** ✅ — **PR #12 (`5000419`)**; budgets on groups/ledgers (On-Closing / On-Nett) +
      variance report, SQLite **v4**. Review APPROVE/0-issues; **143 tests**.
- [x] **2.4 Banking** ✅ — **PR #13 (`1ae0f20`)**; bank allocation, post-dated (Ctrl+T), BRS (book-vs-bank),
      statement CSV import/auto-match, SQLite **v5**. Review APPROVE; **162 tests**. *(Deferred: cheque-print
      physical layout + CSV file-picker dialog.)* (2 session-limit + 1 rate-limit retry before it landed.)
- [x] **2.5 Scenarios / Reversing Journal / Memoranda** ✅ — **PR #14 (`92875dc`)**; Optional (Ctrl+L) /
      Reversing (applicable-upto) / Memorandum (+ConvertToRegular) + Scenario master & scenario-aware reports,
      SQLite **v6**; fixed a real bug (Memoranda leaked into actuals). **185 tests**.
- [x] **2.6 Interest calculation** ✅ — **PR #15 (`1e7ec94`)**; ledger interest params + Interest Calculation
      report (simple/compound, per-basis, post-due via bill dates), SQLite **v7**. **210 tests**.
- [x] **2.7 Multi-currency** ✅ — **PR #16 (`12f51c5`)**; Currency master + Rates of Exchange + forex voucher
      lines + Forex Gain/Loss & period-end revaluation, SQLite **v8**. Review caught a **CRITICAL** (non-round
      rate → base not paisa-constrained → crashed on Save) → fixed (`Money.ForexBase`/`RoundToPaisa`) with 3
      regression tests (fail→pass) and shipped. **237 tests**.

**✅ ALL 7 PHASE-2 SLICES MERGED. main `12f51c5`, 237 tests, 0 "Tally", schema v8.** Verified locally
(build 0/0, 121+24+92). **Before the Phase-2 gate:** fix the flaky Desktop test
`VoucherEntryViewModelTests.Unbalanced_voucher_is_rejected...` (headless-Avalonia/SQLite parallel
test-isolation race, pre-existing PR #5; user-directed) → then relaunch app + present Phase 2. Cosmetic
follow-ups noted: duplicated doc-comments in `ForexGainLoss.cs`; various per-slice UI deferrals (cheque-print
layout, alter/delete masters, interest calc-from/rounding UI, etc.).

**✅ FLAKY TEST FIXED — PR #17 (`84aa3d3`):** disabled `Apex.Desktop.Tests` parallelization (process-global
`ClearAllPools()` race across parallel DB-touching classes); reproduced then **12× clean full-suite runs**.
**PHASE 2 COMPLETE & STABLE** — main `84aa3d3`, **237 tests** (121+24+92), 0 "Tally", SQLite schema **v8**,
CI ×3 OS. Built via a self-paced `/loop` (PRs #10–#17; survived 2 session-limit + 1 rate-limit retries; one
critical forex-paisa bug caught by review + fixed pre-merge). **Next: Phase 3 (Inventory)** — pending user go-ahead.
Each merges via PR (main protected); all plug into the **cascade** + **hierarchy**; stay **de-branded**; keep tests
green. On completion: relaunch app + present the Phase 2 gate. Baseline at start: main `8b8e075`, 73 tests.

### Phase 2.5 — Scenarios / Reversing / Memoranda / Optional UI (Frontend, in progress)
- **Engine/persistence for 2.5 already present in the working tree** (Scenario domain, provisional CountsAsOf,
  scenario-aware TB/P&L/BS, ConvertToRegular, SQLite schema v6 scenarios tables + round-trip tests).
  This task added the **Avalonia cascade UI** on top (no engine changes needed).
- **Voucher entry** (`VoucherEntryViewModel` + `MainWindow.axaml` header): **Optional toggle (Ctrl+L)**
  alongside Post-Dated (both hidden for provisional types, which are off-books already); **Applicable-Upto**
  date field shown only for a **Reversing Journal** (validated ≥ voucher date, defaults to FY-end); a
  "Provisional — off books" badge for Memorandum/Reversing. Flags flow into the built `Voucher`
  (`optional`/`applicableUpto`). Ctrl+L wired in code-behind.
- **Vouchers submenu** now nests an **"Other Vouchers"** group (Transactions → Vouchers → Other Vouchers →
  Reversing Journal / Memorandum) — new `GatewayMenu.OtherVouchers`, `BuildOtherVouchersColumn`,
  `ShowOtherVouchersMenu`, OpenPageOf cases opening those base types.
- **Masters → Create → Scenario**: new `ScenarioMasterViewModel` + page template — Name, **Include Actuals**
  checkbox, per-voucher-type **Include** toggles (Journal/Reversing/Memorandum pre-ticked & listed first),
  Create persists via `AddScenario`+Save, lists existing (Name / Actuals / Includes). `Screen.ScenarioMaster`,
  `GatewayColumn.ScenarioMaster`, "Scn" button-bar quick-jump.
- **Reports under a Scenario**: `ReportsViewModel` gained a **scenario picker** (`Scenarios` =
  "Actual (no scenario)" + each company scenario; `SelectedScenario` rebuilds under it); TB/P&L/BS `Build`
  calls now pass the chosen `Scenario`; subtitle shows "under scenario X"; Day Book stays actual
  (`SupportsScenario` false). Picker rendered above the report grid (clean, right-aligned figures).
- **Memorandum convert**: `MainWindowViewModel.ConvertMemorandum(id, targetBaseType=Journal)` surfaces the
  engine's `LedgerService.ConvertToRegular` (removes memo, posts a real voucher, persists).
- **Tests**: new `tests/Apex.Desktop.Tests/ScenarioViewModelTests.cs` (8 VM/headless tests): Optional drops
  from actual TB but shows under an including scenario (+ report picker switch); Scenario master create +
  **reload** survives (name/include-actuals/types); blank/dup rejected; Memorandum non-affecting → convert →
  affects books + reload; convert rejects a non-memo; Reversing captures Applicable-Upto & counts only within
  it; rejects upto < date; **cascade correctness** (Other Vouchers nested under Vouchers, Scenario under
  Create, single replacing page column). Updated 2 existing hierarchy tests for the new "Other Vouchers" group.
- **Verified:** `dotnet build` 0/0, `dotnet build src/Apex.Desktop -c Release` 0/0, full suite **GREEN**
  (Apex.Ledger 92 + Persistence 17 + Desktop 76 = **185**); 7 headless window-boot tests confirm the new XAML
  renders. **No "Tally" in src/tests** (caught & fixed one leaked code comment). Cascade + professional
  hierarchy + de-brand rules all honoured. (One transient Desktop red seen only when a Release build ran
  concurrently with the test run in the same message — sequential runs are 5/5 green; harness artifact.)
- **Deferred (non-blocking):** a dedicated "Memoranda register" page with an inline Convert button (the
  convert path is engine-tested via `ConvertMemorandum`; no list-with-button UI yet); F10/Ctrl+F10 Tally
  hotkeys for Reversing/Memo (opened via the cascade instead); scenario Alter/Delete surface.

### Governance — 2026-07-04
- **CLAUDE.md R5 strengthened (user directive):** log EVERY change to `memory.md`, even a single-line edit —
  nothing is "too small to log"; entries record what/why/resulting-state; keep the START-HERE block current.
- **Decision (user 2026-07-04): keep `memory.md` + the R5-strengthened `CLAUDE.md` LOCAL to this session
  folder only — NOT committed to the repo.** A new session opened in THIS folder reads them fine; a fresh
  clone elsewhere will not have them. (So: resume from this working directory.)
- **CLAUDE.md tightened further (user 2026-07-04):** **R2 hardened** — main loop does ONLY decide / sequence /
  synthesize / talk; ALL heavy reading/impl/build/test/review goes to Workflows, to protect the session token
  budget. **New-session bootstrap** now resumes from a **single-line prompt** via this START-HERE block, with
  zero re-explanation, and the **user talks only to the main loop**.

---

## ▶▶ NEXT-SESSION START HERE (authoritative current state — 2026-07-04)

> The detailed per-slice blocks ABOVE are historical logs; some agent-appended ones say "in progress" but
> are SUPERSEDED — every Phase-2 slice is merged. This block is the source of truth for where we are.

**Where we are:** **Phases 0, 1, 2 are COMPLETE and shipped.** `main` = **`84aa3d3`**, **237 tests** green
(Apex.Ledger 121 + Apex.Persistence.Sqlite 24 + Apex.Desktop 92), CI green on Windows/Linux/macOS, **0 "Tally"
in the product**, SQLite **schema v8**. Repo **public** (revisit private+GitHub-Pro before sale/launch).

**Stack:** C#/.NET 10 + **Avalonia** (Win/Linux/macOS) + **SQLite**. Projects: `src/Apex.Ledger` (framework-
agnostic double-entry engine), `src/Apex.Persistence.Sqlite` (one `.db` per company), `src/Apex.Desktop`
(Avalonia MVVM cascade UI), tests under `tests/`. Solution `Apex.slnx`.

**Delivered so far:** company + 28-group/2-ledger/24-voucher-type seed; core vouchers + Trial Balance /
Balance Sheet / P&L / Day Book / Ledger book; bill-wise + Outstandings (Ctrl+B); cost centres; budgets;
banking (BRS + statement import + post-dated); scenarios/reversing/memoranda/optional; interest; multi-
currency + forex gain/loss. UI = cascading **Miller-column** navigation with professional Masters/
Transactions/Reports hierarchy.

**Read order for a new session (per CLAUDE.md):** **memory.md (this file, fully)** → **plan.md** (master plan)
→ **CLAUDE.md** (rules R1–R14) → **agents.md** (roster). Standing rules also in auto-memory
([[no-tally-branding-in-product]], [[cascading-column-navigation]], [[ui-professional-hierarchy]],
[[apex-operating-preferences]]).

**Hard rules to keep:** (1) product never shows "Tally" (brand "Apex Solutions"); (2) every screen uses the
cascade + nested hierarchy; (3) max work via **Workflow** agents, main window lean; (4) ALL git/GitHub via the
**GitHub Expert** only — `main` is protected, so every change lands via feature-branch → PR → green CI →
squash-merge; **never commit `tally/`**; (5) TDD, nothing "done" without tests green + review + app run;
(6) **`export PATH="$HOME/.dotnet:$PATH"`** before ANY dotnet command (SDK 10.0.301 at `~/.dotnet`); solution
is `.slnx`.

**Build/run:** `export PATH="$HOME/.dotnet:$PATH" && dotnet build -c Release && dotnet test -c Release`;
run the app: `dotnet run --project "src/Apex.Desktop" -c Release`.

**NEXT: Phase 3 — Inventory** (plan.md §5 Phase 3): Stock Group/Category, Units (simple+compound), Godown,
Stock Item; stock + order-processing vouchers (Purchase/Sales Order, GRN/Delivery Note, Rejection In/Out,
Stock Journal, Physical Stock); Stock Summary + inventory registers; Accounts↔Inventory integration &
valuation. Build slice-by-slice (engine TDD → cascade UI → review → GitHub Expert PR), integrated into the
cascade + hierarchy, de-branded. (Advanced inventory — BOM/batch/POS/price-levels/reorder/job-work — is
Phase 6.) **Do not start building until the user confirms.**

**Non-blocking follow-ups (backlog):** cheque-print physical layout; master Alter/Delete UIs; interest
calc-from + rounding fields on the ledger form; forex realized-G/L at settlement UI; duplicated doc-comments
in `src/Apex.Ledger/Reports/ForexGainLoss.cs`; consider committing `memory.md` to the repo at each phase gate
(currently local-only — the GitHub Expert excludes it from feature PRs).
