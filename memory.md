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
- **Current status:** Pre-build. Study complete; governance + plan being set up; awaiting user go-ahead.
- **Current phase in plan.md:** **Phase 0 — APPROVED (user 2026-07-02); starting.**
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

### Phase 3 slice 3.1 — Inventory Masters (2026-07-04)
- **Commit/branch:** `afe256c` on `claude/interesting-mirzakhani-30e51e` (pushed to origin; **no PR yet** —
  Phase 3 has more slices, PR at phase end). **29 files**, **299 tests green** (Apex.Ledger 161 +
  Apex.Persistence.Sqlite 33 + Apex.Desktop 105). SQLite schema bumped **v8 → v9** (idempotent
  `MigrateV8ToV9`).
- **Delivered:** stock group (nesting + "add quantities?" flag), stock category, units (simple +
  compound with conversion factor / UQC / decimals 0–4), godowns (default **"Main Location"** seed +
  third-party flag), stock items (under group/category/base-unit, HSN, taxability, valuation method
  default **Average Cost**, simple reorder level + min-order-qty), and opening balances by godown + batch
  label. Engine **`InventoryService`** centralizes guards (uniqueness, parent-cycle, delete-if-referenced).
  New domain helpers **`HierarchyOrdering`** (topological parent-before-child + cycle detection) and
  **`Quantities`** (6dp precision) + `Money.IsPaisaExact`.
- **UI:** five create screens wired into the cascading Miller-column nav under a new **"Inventory Masters"**
  section (Stock Group, Stock Category, Unit, Godown, Stock Item); create-only convention (matches existing
  masters; engine already supports alter/delete for the future app-wide Alter/Delete UI pass); Stock Item
  screen captures an opening balance (godown default Main Location, qty, rate, batch); UI pre-validates
  precision to avoid domain exceptions. De-branded (zero "Tally").
- **Approved decision points (user, 2026-07-04):** default valuation = **Average Cost**; lightweight scope —
  simple reorder + batch **LABELS** in Phase 3, deep batch (mfg/expiry) + period-based reorder deferred to
  **Phase 6**.
- **Adversarial review (A10)** found + fixed **4 real defects** the green suite missed: (1) child-before-parent
  self-FK crash on Save (topological insert), (2) opening-balance rate >2dp crashed Save (domain paisa-exact
  guard), (3) godown/category re-parent had no cycle guard (added `SetGodownParent`/`SetStockCategoryParent`
  + Save-time backstop), (4) reorder fields >6dp crashed Save (6dp guard). Also removed a dead write-only
  `value_paisa` column. Plus **2 UI text-overlap fixes** (shortcut-bar chord/label gap; Unit "Decimals (0-4)"
  clip), verified by headless Skia render.
- **Requirements doc:** `docs/phase3-inventory-requirements.md` (36 RQ / 10 ER / 10 DP + Bright sign-off gate).
- **Next:** slice 3.2 (inventory & order voucher types + stock-movement engine, schema v10).

### Phase 3 slice 3.2 — Inventory & Order Vouchers (2026-07-04)
- **Commit/branch:** `2a1eaea` on `claude/interesting-mirzakhani-30e51e` (pushed to origin; **no PR yet** —
  Phase 3 continues). **26 files**, **357 tests green** (Apex.Ledger 198 + Apex.Persistence.Sqlite 37 +
  Apex.Desktop 122). SQLite schema bumped **v9 → v10** (idempotent `MigrateV9ToV10`: 2 ALTER `voucher_types`
  + 4 new tables `inventory_vouchers` / `inventory_allocations` / `order_lines` / `physical_stock_lines`).
- **Engine:** the 8 order/stock voucher types — Purchase Order (Ctrl+F9), Sales Order (Ctrl+F8),
  Receipt Note/GRN (Alt+F9), Delivery Note (Alt+F8), Rejection Out (Ctrl+F5), Rejection In (Ctrl+F6),
  Stock Journal (Alt+F7), Physical Stock (F10 menu) — now carry `AffectsAccounts` / `AffectsStock`
  effect flags (predefined type count stays **24** — flags added to existing seeded types, not duplicates).
  Separate **`InventoryVoucher`** aggregate posted via **`InventoryPostingService`** (no Dr/Cr balancing).
  **`InventoryLedger`** on-hand engine: opening + Σ inward − Σ outward over stock-affecting vouchers dated
  ≤ asOf (as-of / post-dated aware, cancelled excluded, compound-unit normalized to base). Hard
  **no-negative-stock** guard (DP-7). Stock Journal source total = destination total (base unit). Physical
  Stock sets on-hand to counted qty as an end-of-day checkpoint (DP-3).
- **Adversarial review (A10)** found + fixed a **CRITICAL** no-negative-stock bypass: the guard sampled only
  end-of-date on-hand, but a same-date Physical-Stock count is applied last and resets on-hand to the count,
  masking an intra-day negative from a same-date outward (e.g. count 5 then deliver 100 was wrongly accepted).
  Fixed by sampling the running balance BEFORE the same-date count checkpoint (`PreCountOnHandForKey`);
  DP-3 reporting / carry-forward unchanged. **7 regression tests.** A10 cleared everything else (back-dated,
  cancel/delete, compound units incl. fractional factor, batch guard, effect flags, migration).
- **UI:** 8 keyboard-first voucher entry screens wired into the Miller-column nav under **Vouchers →
  "Order Vouchers" / "Inventory Vouchers"** groups; one `InventoryVoucherEntryViewModel` parameterized by
  base type; Stock Journal shows source + destination grids with a live "source = destination (base unit)"
  balance indicator gating Accept; Physical Stock shows "Counted Qty" (no Rate). Two UI fixes verified by
  headless Skia render: the earlier voucher screens plus a Stock-Item combobox left-clip (MinWidth exceeded
  the cell → FluentTheme centered it at negative x; removed MinWidth + rebalanced columns). De-branded
  (zero "Tally").
- **Next:** slice 3.3 (valuation engine + accounts↔inventory integration: 6 valuation methods,
  Stock-in-Hand derived = Σ item closing values, P&L Trading/COGS from derived closing stock), then
  item-invoice mode, reports, and the Bright re-verification gate.

### Phase 3 slice 3.3a — Stock Valuation Engine + Accounts↔Inventory Integration (2026-07-05)
- **Branch:** `claude/interesting-mirzakhani-30e51e` — will be committed by the GitHub Expert (**no PR yet**;
  Phase 3 continues). **381 tests green** (Apex.Ledger 221 + Apex.Persistence.Sqlite 38 + Apex.Desktop 122).
  SQLite schema bumped **v10 → v11** (idempotent `MigrateV10ToV11`: `ALTER TABLE stock_items ADD COLUMN
  standard_cost_paisa INTEGER NULL`).
- **Engine:** `StockValuationService` (pure; reuses `InventoryLedger` so qty & value share the same
  as-of / post-dated / cancelled conventions) computes closing qty + paisa-exact value per the item's method —
  **perpetual moving-average** (default AverageCost), FIFO / LIFO cost layers, **StandardCost** (new nullable
  per-item `StockItem.StandardCost`, falls back to last-purchase when unset), LastPurchaseCost, LastSaleCost.
  `TotalClosingStockValue` = Σ items each by its own method. FIFO ₹800 worked example passes. Graceful
  **best-available-cost fallback** for no-rate inwards (running avg → standard → last rated inward → 0) so real
  stock is never silently ₹0; LastSale/LastPurchase similarly degrade.
- **Accounts↔inventory:** `ProfitAndLoss.Build` gained a `ClosingStockMode.InventoryDerived` branch
  (COGS = Opening + Purchases − derived Closing; posted-ledger default path byte-for-byte unchanged so
  Robert/Bright stay green); `BalanceSheet.Build` gained a mode param that replaces Stock-in-Hand ledgers with
  a single derived "Stock-in-Hand" line = `TotalClosingStockValue` and folds net profit — **Balance Sheet
  balances to the paisa** (proven by test).
- **Recovery note:** the first agent run died mid-response on an API connection drop; its files survived; the
  orchestrator fixed a 1-line namespace-collision compile error and **RESUMED the same agent via SendMessage**
  to finish report-wiring + persist StandardCost + green the gate (lesson: resume, don't restart).
- **Adversarial review (A10):** valuation core is SOUND (FIFO/LIFO cross-lot, moving-average multi-movement,
  physical-count-into-layers up/down, paisa-exactness, as-of, multi-SIH & loss-making BS balance — none
  breakable). Found + fixed the confirmed no-rate-only → ₹0 and LastSale/LastPurchase-no-movement → ₹0
  (graceful fallback, **+7 tests**). Two **HIGH design *preconditions*** (PLAUSIBLE, not engine bugs)
  documented, to be ENFORCED next: derived-closing mode assumes (a) every stock inward is paired to an
  accounting posting and (b) opening stock is booked to a Stock-in-Hand ledger — else reports balance while
  masking phantom profit; the item-invoice voucher must post both arms atomically, and the "Bright" gate must
  book opening stock to SIH.
- **Next:** slice 3.3b = **item-invoice mode** (accounting Purchase/Sales carry inventory lines that post
  accounts AND move stock atomically — enforcing the A10 precondition), then 3.4 reports (Stock Summary +
  registers), then 3.5 Bright re-verification gate.

### Phase 3 slice 3.3b — Item-Invoice Mode (2026-07-05)
- Committed by the GitHub Expert (branch `claude/interesting-mirzakhani-30e51e`; **no PR yet**). **400 tests
  green** (Apex.Ledger 236 + Apex.Persistence.Sqlite 42 + Apex.Desktop 122). SQLite schema **v11 → v12**
  (idempotent `MigrateV11ToV12`: new `voucher_inventory_lines` child table).
- **Engine:** the accounting `Voucher` (Purchase/Sales) now carries optional inventory lines
  (`VoucherInventoryLine`: item, godown, qty, rate, direction, batch) — absent ⇒ voucher behaves exactly as
  before (Robert/Bright preserved). `LedgerService.Post` is **atomic** (provisional append → validate → reuse
  the no-negative-stock guard → roll back the whole voucher on any failure, so a Sales item-invoice that
  over-sells persists NOTHING). Direction stamped from voucher nature (Purchase⇒inward, Sales⇒outward),
  overriding any caller value.
- **Pairing invariant** (`VoucherValidator.EnsureItemInvoiceValid`): item lines valid only on Purchase/Sales;
  Σ(item qty×rate) must equal the accounting stock leg (Purchase-Accounts/Stock-in-Hand debit for Purchase,
  Sales-Accounts credit for Sales); **rate must be > 0** — so no item-invoice line can move stock without a
  backing accounting amount ⇒ no unbacked stock, no phantom profit. This ENFORCES the A10 precondition BR-1
  for the item-invoice path.
- `InventoryLedger.OnHand` + `StockValuationService` merge item-invoice movements (`ItemInvoiceStock.Movements`)
  with `InventoryVoucher` movements + openings, honoring as-of / post-dated / cancelled and FIFO/LIFO layering
  off item-invoice rates. Precondition-proof test: opening stock booked to Stock-in-Hand + item-invoice purchase
  + sale → derived FIFO closing ₹800, P&L COGS = Opening + Purchases − Closing = 800, **no phantom profit,
  Balance Sheet balances to the paisa**.
- **Adversarial review (A10):** found + fixed a **CRITICAL** zero-rate hole — a `rate=0` item line added
  quantity but ₹0 value, invisible to the pairing sum, injecting unbacked stock (phantom profit under
  Standard/Last-cost). Fixed by requiring rate>0 at the domain ctor + validator (**+3 tests**; phantom scenario
  now yields on-hand 0 / ₹0). A10 cleared everything else (atomicity, mismatch rejection, direction stamping,
  non-Purchase/Sales rejection, post-dated, cancel/delete reversal, valuation merge, persistence). Note:
  non-item inward paths (GRN/stock-journal w/o accounting posting; opening stock not on SIH) remain the
  documented BR-1 precondition — out of scope, enforced by fixtures.
- **Next:** slice 3.4 = inventory reports (Stock Summary w/ drill, Godown Summary, Movement/Ageing, Batch,
  Reorder Status, Receipt/Delivery/Rejection/Physical/Order registers); then item-invoice UI on the
  Purchase/Sales screen; then the **Bright re-verification gate** (opening stock booked to Stock-in-Hand,
  closing derived, BS/P&L reconcile to the paisa) + full app run + Phase 3 wrap.

### Phase 3 slice 3.4a — Inventory Report Projections (2026-07-05)
- Committed by the GitHub Expert (branch `claude/interesting-mirzakhani-30e51e`; **no PR yet**). **428 tests
  green** (Apex.Ledger 264 + Apex.Persistence.Sqlite 42 + Apex.Desktop 122). Schema unchanged (**v12** —
  reports read existing data).
- **Engine:** pure report projections in `src/Apex.Ledger/Reports/` (TrialBalance/DayBook pattern; row record
  + root record + static `Build` + `Reports.cs` façade): **StockSummary** (per item: opening / inward / outward
  / closing qty + closing value by the item's method, grand total), **GodownSummary** (per-godown qty+value,
  apportion-by-quantity with the last godown absorbing the paisa remainder so Σ == item value exactly),
  **StockItemMovement** (chronological running-balance journal, the Stock-Summary drill target), Day-Book-style
  **registers** (Receipt Note, Delivery Note, Rejection In/Out, Physical Stock, Order), **ReorderStatus**
  (closing ≤ reorder level, shortfall). Helper `InventoryMovements` flattens pure-stock (incl. Stock-Journal
  src/dest, rejections) + item-invoice movements, mirroring `InventoryLedger` as-of / cancelled / post-dated /
  base-unit rules.
- **Adversarial review (A10)** found + fixed a **HIGH** reconciliation break: a mid-period Physical-Stock count
  made `opening + inward − outward ≠ closing` (inward/outward exclude counts per DP-3, but closing applies them).
  Fixed by surfacing each in-period count's variance (`InventoryLedger.PhysicalStockAdjustments`) as a synthetic
  adjustment — folded into inward (found stock) / outward (shrinkage) so StockSummary foots, and emitted as a
  "Physical Stock" row in StockItemMovement so running balance ties to counted on-hand (**+8 tests**). A10
  cleared godown apportionment (paisa-exact), registers, reorder boundary (inclusive, Tally-faithful), and
  non-count movement sources.
- **Judgment:** Order register outstanding-qty = full ordered qty (orders carry no persisted fulfilment link yet
  — deferred to a tracking-number slice).
- **Next:** slice 3.4b = the report UI (ReportKind values + "Inventory Reports" submenu + ReportsViewModel
  projections + Miller-column render + Stock-Summary→Movement drill); then item-invoice UI on the Purchase/Sales
  screen; then the **Bright re-verification gate** + full app run + Phase 3 wrap.

### Phase 3 slice 3.4b — Inventory Report UI (2026-07-05)
- Committed by the GitHub Expert (branch `claude/interesting-mirzakhani-30e51e`; **no PR yet**). **448 tests
  green** (Apex.Ledger 264 + Apex.Persistence.Sqlite 42 + Apex.Desktop 142, **+21 Desktop**). Schema unchanged
  (**v12**).
- **UI:** 9 `ReportKind` values (StockSummary, GodownSummary, StockItemMovement, Receipt/Delivery/Rejection/
  PhysicalStock/Order registers, ReorderStatus) wired into the Miller-column nav under a nested **"Inventory
  Reports"** group (sub-sections Stock / Analysis / Registers); menu labels matched to `OpenPageOf`→`OpenReport`
  routing. One `ReportsViewModel` + `Screen.Report`; extended `ReportRow` (Col1..Col8 + drill id) rendered by
  **per-ReportKind inventory DataTemplates** (Stock Summary: Item|Inward|Outward|Closing Qty|Rate|Value;
  registers: Date|No.|Party|Item|Godown|Qty|Rate|Value|Batch; Movement: Date|Voucher Type|In|Out|Balance|Value;
  etc.), right-aligned numerics, grand totals. New `IndianFormat.Quantity`.
- **Drill-down:** Stock Summary rows carry a stock-item id; Enter/double-click → `OpenReport(ReportKind.
  StockItemMovement, itemId)` (an `OpenReport` overload) replaces the page with that item's movement journal —
  the app's **first row-level report drill**.
- **Visual verification (headless Skia render):** fixed two column-clipping defects — Stock Summary Item column
  starved ("Sto"/"…") and Stock Movement Voucher-Type column ("Vouc"/elided) + a clipped "Closing Balance" label;
  root cause included a mismatched header-vs-row `ColumnDefinitions` on the Movement template. Rebalanced widths
  (text columns wide, numerics compact); re-render confirms full "Stock Item"/"Copper Gadget"/"Steel Widget"/
  "Voucher Type" text. Godown Summary, Reorder Status, Receipt Register, and the menu already rendered clean.
  **De-branded.**
- **Next (Phase 3 remainder):** item-invoice UI on the Purchase/Sales voucher screen (inventory-lines panel that
  computes the value line, enforcing the pairing invariant), then the **Bright re-verification gate** (book
  opening stock to Stock-in-Hand, closing derived, BS/P&L reconcile to the paisa) + full live app run + Phase 3
  wrap; then Phase 4 (GST).

### Phase 3 — Bright re-verification GATE (2026-07-05)
- **DONE & green.** Proved the accounts↔inventory engine DERIVES Bright's closing stock (₹15,000) from real
  inventory under `ClosingStockMode.InventoryDerived` (no hand-posted closing-stock Journal). **455 tests green**
  (Apex.Ledger **271** = 264 + **7 new** BrightReVerification, Sqlite 42, Desktop 142). Schema unchanged (**v12**).
  No "Tally"; clean tree.
- **Bright inventory added (additive/derived-only; accounting UNCHANGED under AsPostedLedger):** 1 stock group
  "Trading Goods", 1 unit "Nos", 1 item **"General Merchandise"** (Average Cost = DP-1 default). Opening **250 @
  ₹100 = ₹25,000** (booked to inventory opening balance; = "Opening Stock" SIH ledger opening debit — BR-1).
  Purchase #1 as **item-invoice** 400 @ ₹100 = ₹40,000 inward (= Purchases Dr leg — pairing ✓). Credit sale #3 as
  **item-invoice** 400 @ ₹137.50 = ₹55,000 outward (= Sales Cr leg — pairing ✓). Cash sale #5 is a **Receipt**
  (can't carry item lines) → its 100-unit stock-out is a **stock-only Delivery Note** (no accounting), so the
  ledger side stays byte-for-byte. Since every inward is ₹100, the moving average is ₹100 throughout; closing =
  250+400−400−100 = **150 units × ₹100 = ₹15,000 DERIVED**.
- **Root cause of an initial ×2 (₹30,000) bug + fix:** the derived `TotalClosingStockValue` was correctly ₹15,000,
  but the P&L/BS double-counted because Bright's fixture still carries the **manual closing-stock Journal #11**
  (Cr "Closing Stock Adjustment (P&L)" under Direct Incomes). `ProfitAndLoss.Build` under InventoryDerived adds
  derived closing back explicitly AND `ComputeGrossProfit` still counted the manual Direct-Income adjustment →
  ₹15k twice. Engine is correct (its InventoryDerived math assumes NO manual journal). **Fix (no engine change):**
  tagged voucher #11 `"manualClosingStock": true` in `bright.json`; added `FixtureLoader.Load(fileName,
  skipManualClosingStock=false)` — the re-verification loads with `true` (journal excluded, closing derived),
  the existing AsPostedLedger tests load with the default `false` (journal kept, unchanged).
- **Reconciled figures (all paisa-exact):** derived closing **₹15,000**; COGS = 25,000+40,000−15,000 = **₹50,000**;
  **gross profit ₹15,000**; **net profit −₹1,000**; Balance Sheet **₹1,84,000** both sides, balanced; derived
  Stock-in-Hand asset line = ₹15,000. `The_derived_statements_equal_the_hand_posted_statements…` proves derived
  (skip-journal) ≡ AsPostedLedger (with-journal) to the paisa — the manual journal is now redundant.
- **Files:** `tests/Apex.Ledger.Tests/Fixtures/bright.json` (+`inventory` block, `inventoryLines` on #1/#3,
  `manualClosingStock` on #11, `expected.inventoryDerived` targets), `tests/Apex.Ledger.Tests/FixtureLoader.cs`
  (parse inventory masters/opening/item-invoice/delivery-notes; `skipManualClosingStock`; `StockItemId` helper),
  `tests/Apex.Ledger.Tests/BrightReVerificationTests.cs` (new — 7 tests: BR-1..BR-6 + redundancy). Robert
  untouched (no inventory block; BR-6 asserts it stays ₹5,000 net / ₹1,05,000 balanced). **Did NOT touch git.**
- **Next:** GitHub Expert (A12) commits the gate; then Phase 3 wrap (live app run) → Phase 4 (GST).

### Phase 3 — Bright re-verification gate — the hard Phase-3 sign-off, PASSED (2026-07-05)
- Committed by the GitHub Expert (branch `claude/interesting-mirzakhani-30e51e`; **no PR yet**). **455 tests green**
  (Apex.Ledger 271 + Apex.Persistence.Sqlite 42 + Apex.Desktop 142, **+7 Bright tests**). Schema unchanged (**v12**).
- Gave the "Bright" trading fixture real inventory (1 item **"General Merchandise"**, Average Cost, all inwards @
  ₹100): opening **250@₹100 = ₹25,000** booked to the Stock-in-Hand ledger (BR-1); item-invoice purchase **400@₹100
  = ₹40,000** (pairs the Purchases Dr leg); item-invoice credit sale **400@₹137.50 = ₹55,000** (pairs the Sales Cr
  leg, outward); the cash sale (a Receipt type, can't carry item lines) records its 100-unit stock-out as a
  stock-only **Delivery Note** so the ledger side stays byte-identical. Closing = 250+400−400−100 = **150 units ×
  ₹100 = ₹15,000 DERIVED**.
- Under `ClosingStockMode.InventoryDerived`, 7 `BrightReVerificationTests` assert paisa-exact: opening ₹25,000,
  on-hand 150, **derived closing ₹15,000**, Balance-Sheet Stock-in-Hand = ₹15,000 and **totals ₹1,84,000 =
  ₹1,84,000 balanced**, **COGS = 25,000+40,000−15,000 = ₹50,000, gross ₹15,000, net −₹1,000**, and derived ≡
  AsPostedLedger (the manual journal is redundant); Robert stays green.
- Root cause of the initial derived-closing ×2 (₹30,000 vs ₹15,000): Bright's hand-posted closing-stock **Journal
  #11** was double-counted under InventoryDerived (P&L adds derived closing back AND ComputeGrossProfit still
  counted the manual adjustment). Fixed WITHOUT an engine change: tagged #11 `manualClosingStock:true` +
  `FixtureLoader.Load(file, skipManualClosingStock)`; the re-verification loads with skip=true (journal excluded →
  closing derived), existing AsPostedLedger tests use skip=false (journal kept → unchanged & green). Diagnosis
  after a session-limit interruption: orchestrator saw the derived closing was exactly 2× target, resumed the same
  agent via SendMessage with the numbers, it found the double-count.
- **PHASE 3 (Inventory) IS NOW FEATURE-COMPLETE + GATED** (6 slices 3.1–3.4b + this gate; masters, order/stock
  vouchers, valuation, item-invoice mode, inventory reports engine+UI, Bright derived-closing reconciliation).
  Remaining Phase-3 polish: item-invoice voucher UI (inventory-lines panel on the F9/F8 screen), a full live app
  run for the user, then commit the `memory.md` slice logs + open the Phase-3 PR to `main`. Then **Phase 4 (GST)**.

### Phase 3 — Item-Invoice Voucher UI (2026-07-05)
- Committed by the GitHub Expert (branch `claude/interesting-mirzakhani-30e51e`; **no PR yet**). **468 tests green**
  (Apex.Ledger 271 + Apex.Persistence.Sqlite 42 + Apex.Desktop 155, **+13 Desktop**). Schema unchanged (**v12**).
- **UI:** a **Ctrl+I "Item Invoice" toggle** on the Purchase (F9) / Sales (F8) accounting voucher entry screen
  (also a "Ctrl+I · As Invoice" button-bar entry; **no-op on non-Purchase/Sales**). In item-invoice mode the plain
  Dr/Cr grid is hidden and an inventory-lines panel shows: a **party picker** (supplier/customer), a **value-ledger
  picker** (constrained to the engine's valid stock-leg groups — Purchase Accounts/Stock-in-Hand for Purchase,
  Sales Accounts for Sales; user-overridable), the **Item/Godown/Qty/Rate/Batch grid** (reuses
  `InventoryVoucherLineViewModel`), a running **Items Total**, and a derived **Dr/Cr summary**. The VM
  **auto-derives the two accounting legs** (both = Σ item qty×rate) so **Σ Dr = Σ Cr AND the pairing invariant
  hold by construction** — no hand-balancing. Accept builds the `Voucher` with `InventoryLines` and posts via
  `LedgerService` (**atomic accounts+stock**; no-negative-stock enforced; try/catch surfaces domain messages).
  Plain accounting mode unchanged (all existing voucher tests green).
- **Two UI fixes verified by headless Skia render:** the header toggle row (Item Invoice/Optional/Post-Dated) was
  overprinting → **restructured to a two-row header** (type+No.+date on top, spaced toggles below); the
  item-invoice Stock Item column widened so long names ("Steel Widget Assembly") show in full. **De-branded.**

**✅ PHASE 3 (INVENTORY) COMPLETE (2026-07-05)** — 6 slices (3.1 masters, 3.2 order/stock vouchers, 3.3a valuation
engine, 3.3b item-invoice mode, 3.4a report engine, 3.4b report UI) + the item-invoice voucher UI + the **Bright
re-verification gate** (`b95022c`), all committed & pushed on `claude/interesting-mirzakhani-30e51e`. Schema **v12**,
**468 tests green**, de-branded, Miller-column UI, adversarially verified (A10 caught ~10 real defects incl. 2
criticals across the phase). Delivered: stock masters (groups/categories/units [simple+compound]/godowns/items +
opening balances); order & stock vouchers (PO/SO/GRN/Delivery/Rejections/Stock Journal/Physical Stock) with an
on-hand engine + hard no-negative-stock guard; six valuation methods + derived Stock-in-Hand + P&L COGS integration
(BS balances to the paisa); item-invoice mode (accounts+stock atomic, pairing invariant); the full inventory report
suite (Stock Summary w/ drill, Godown Summary, Movement, registers, Reorder Status). **Next: Phase 4 (GST)** per
`plan.md` — CA/A14-led, web-verify current GST law/rates.
