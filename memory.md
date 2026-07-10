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

## Phase 4 slice 4a — CORE GST ENGINE (engine + persistence only, no UI) — DONE (green)
Implemented per `docs/phase4-gst-requirements.md` (RQ-1..RQ-19) with the USER-APPROVED DPs. **Schema v13.**
Tests: Apex.Ledger 303 (was 271; +32 GST), Apex.Persistence.Sqlite 46 (+4 GST round-trip/migration),
Apex.Desktop 155 — **504 total, all green** (+36 new). Build 0 warnings. No "Tally" in code. No scratch.

- **Domain (new):** `GstEnums.cs` (RegistrationType/Taxability/ReturnPeriodicity/TaxHead/TaxDirection/SupplyType),
  `IndianState.cs` (official 2-digit GST state codes + UT flag → UTGST folds into the State head), `Gstin.cs`
  (15-char + state-code + PAN + 'Z' + **Luhn-mod-36** checksum, pure/fail-fast; verified against real GSTINs),
  `GstRateSlab.cs` (bp), `GstConfig.cs` (company config on `Company.Gst`, seeds slabs), `PartyGstDetails.cs`
  (on `Ledger.PartyGst`, IsB2C), `StockItemGstDetails.cs` (on `StockItem.Gst`/`Ledger.SalesPurchaseGst`),
  `LedgerGstClassification.cs` (tax-ledger head+direction), `GstLineTax.cs` (on `EntryLine.Gst`). All additive
  nullable trailing ctor params — existing ctors unbroken.
- **Service:** `GstService.cs` — `EnableGst` (idempotent: seeds 0/5/18/40, auto-creates 6 Output/Input tax
  ledgers under Duties & Taxes + a "Round Off" ledger under Indirect Expenses), `ResolveRate` (item→S/P
  ledger→unresolved, most-granular-wins; exempt short-circuits; taxable-unresolved = fail-fast sentinel),
  `IsInterState` (home vs party state), `ComputeInvoiceTax` (per-line CGST=SGST=round_paisa(V*halfBp/10000)
  intra / IGST full inter; optional invoice round-off nearest-rupee via Round Off line). `SeedGstRates.cs`.
- **Additive/pairing (ER-8):** tax posts ONLY to Duties & Taxes ledgers; `ClassificationRules.IsDutiesAndTaxesLedger`
  added; `VoucherValidator.EnsureItemInvoiceValid` UNCHANGED — its stock-leg sum already excludes non
  Sales/Purchase/Stock-in-Hand ledgers, so the invariant holds (proven by `GstTests.Intra_state_gst_sales_item_invoice_is_additive_and_pairing_invariant_holds` asserting stock leg == Σ item value == ₹1000, tax excluded).
- **Persistence v13 (`MigrateV12ToV13`, idempotent):** company GST cols + `gst_rate_slabs` table; ledger
  party/S-P/tax-classification cols; stock_item GST cols; entry_line tax-line cols. Dual-written (CreateV1 +
  migration ALTERs). Full round-trip + v12→v13 data-intact tests green. Bumped 3 existing schema-version-literal
  asserts 12→13; extended `DowngradeToV9`/`DowngradeToV11` test helpers to strip v13 artifacts (they save-at-
  current-then-downgrade, so re-migration would otherwise collide on the bare CREATE TABLE gst_rate_slabs).
- **Judgment calls:** (a) UTGST folded into single State head (RQ-6, documented on IndianState). (b) Round-Off
  ledger auto-created under Indirect Expenses (P&L; round-off can be Dr or Cr); side derived from voucher
  direction. (c) Company-level rate resolution = unresolved fail-fast (no single "company default rate" field
  in Phase 4; item/ledger cover real invoices). (d) null/blank party state ⇒ treated inter-state (safe full IGST).

### Phase 4 slice 4a — Core GST Engine (2026-07-05)
- Committed by the GitHub Expert (branch `claude/interesting-mirzakhani-30e51e`; **no PR yet**). **514 tests green**
  (Apex.Ledger 313 + Apex.Persistence.Sqlite 46 + Apex.Desktop 155). SQLite schema **v12 → v13** (idempotent
  `MigrateV12ToV13`: GST columns on company/ledger/stock-item/entry-line + `gst_rate_slabs` table). Requirements
  doc `docs/phase4-gst-requirements.md` (**29 RQ / 12 ER / 11 DP**) committed with the slice.
- **User-approved DPs:** GST 2.0 slabs **0/5/18/40%** (web-verified current: 12% & 28% removed 22-Sep-2025, 56th
  GST Council/CBIC); Rule-88A ITC set-off engine + Alt+J/Ctrl+F posting **DEFERRED to Phase 9** (Phase 4
  computes/displays ITC + net payable). Also: auto-create 6 tax ledgers on F11-enable; per-line paisa rounding +
  optional invoice round-off; HSN/SAC validated text; rate resolution item→ledger→company; place of supply = party
  State; no/blank GSTIN ⇒ B2C; support both item-invoice & as-voucher GST; tax direction from base type.
  RCM/composition/cess/e-invoice/GSTR-2A-2B = **Phase 9**.
- **Engine (`src/Apex.Ledger/`):** `GstConfig`/`GstEnums`/`IndianState`/`Gstin` (15-char Luhn-mod-36)/`GstRateSlab`/
  `PartyGstDetails`/`StockItemGstDetails`/`LedgerGstClassification`/`GstLineTax` domain + `GstService` (EnableGst
  idempotent, ResolveRate, IsInterState, ComputeInvoiceTax/ComputeLineTax) + `SeedGstRates`. GST is **opt-in / off
  by default** so Robert/Bright + all existing companies are byte-unchanged. Tax is **additive** — posts only to
  Duties & Taxes ledgers, so the item-invoice pairing invariant (stock leg = Σ taxable value) holds unchanged;
  party total = taxable + tax.
- **Adversarial review (A10) found + fixed 2 confirmed defects:** (1) **CRITICAL** CGST+SGST ≠ IGST to the paisa —
  halves were rounded independently (280k breaking values, e.g. ₹1.05@5% gave 0.06 vs IGST 0.05); fixed to
  **compute-total-then-split** (`total=round(V×rate)`, `CGST=round(total/2)`, `SGST=total−CGST`) so CGST+SGST ==
  IGST == round(V×rate) by construction (CGST==SGST except a forced 1-paisa on odd totals), verified by an
  exhaustive paise sweep at 5/18/40%; (2) **HIGH** blank/unknown party State defaulted to IGST — mis-taxed B2C
  local sales; fixed to default unknown place-of-supply to the company home State ⇒ intra (CGST+SGST). A10 cleared
  rounding sides, GSTIN checksum, additivity/pairing, rate resolution, exempt/nil handling, Output/Input direction,
  non-GST-untouched.
- **Phase 4 slice 4b — GST REPORT PROJECTIONS (pure, `src/Apex.Ledger/Reports/`, no UI):** DONE, green. Three
  read-only projections over already-posted GST vouchers, each = row record(s) + root record + pure
  `Build(Company, DateOnly from, DateOnly to)` + a `Report.*` façade wrapper (`BuildTaxAnalysis`/`BuildGstr1`/
  `BuildGstr3b`). New files: `TaxAnalysis.cs`, `Gstr1.cs`, `Gstr3b.cs`, shared `GstReportSupport.cs`; modified
  `Reports.cs` (façade). Key design (matches slice-4a intent — **reads posted tax, never recomputes**): every
  figure is read off each tax `EntryLine`'s `GstLineTax` (TaxHead, applied RateBasisPoints, TaxableValue) + the
  line's `Amount` (the tax); direction = `GstReportSupport.DirectionOf(baseType)` (Sales/CreditNote⇒Output,
  Purchase/DebitNote⇒Input, DP-11); cancelled/optional/provisional/post-dated-after-`to` filtered via
  `LedgerBalances.CountsAsOf`. **(1) TaxAnalysis** — outward+inward sides, per-head totals + rate-wise rows
  (by head + head-rate); **(2) GSTR-1** — B2B (one row per registered-party invoice, party has GSTIN vs B2C
  by `PartyGst.IsB2C`, DP-8), B2C consolidated rate-wise, rate-wise summary, HSN summary (from item-invoice
  stock lines; invoice's posted tax apportioned to lines by value share, last line absorbs remainder =
  paisa-exact, UQC from Unit.UnitQuantityCode; exempt outward = no-tax outward vouchers' stock value);
  **(3) GSTR-3B** — §3.1 outward by head, taxable vs exempt/nil/non-GST outward value, §4 ITC by head, and
  **net payable = output − ITC per head, DISPLAY-ONLY (DP-9)** — negative head = carried-forward credit, XML doc
  labels it indicative; NO Rule-88A set-off / Alt+J-Ctrl+F posting (Phase 9). **Reconciliation asserted to the
  paisa:** Σ GSTR-1/TaxAnalysis/GSTR-3B output tax by head == Σ Output tax-ledger postings for the period;
  GSTR-3B ITC == Σ Input postings; CGST==SGST foot. Tests: `tests/Apex.Ledger.Tests/GstReportsTests.cs` (+17):
  synthetic GST co (home MH 27; in-state B2B, Gujarat B2B, B2C consumer, in-state supplier) posts intra B2B
  (CGST+SGST), inter B2B (IGST), B2C intra, exempt sale, purchase (ITC) — with opening stock for the B2C/exempt
  items; asserts every reconciliation + cancelled/post-dated excluded + non-GST company empty/no-crash + façade.
  **Gate green:** `dotnet build -c Release` + `dotnet test -c Release` = **531 tests** (Ledger 330, Sqlite 46,
  Desktop 155), 0 fail (514 baseline + 17). Schema unchanged (**v13** — reports read existing data). No "Tally"
  in new code/tests. `git status`: only the 5 new/modified legit files + memory.md. **Judgment calls:**
  (a) HSN per-line tax on multi-item invoices = apportion the invoice's *posted* head totals by line-value share
  (never re-derive from rate) — exact for the single-item Phase-4 fixtures; (b) intra vs inter inferred per tax
  line from the head (Central/State⇒intra, Integrated⇒inter) so no re-routing; (c) invoice taxable value =
  max over the tax lines' TaxableValue (CGST & SGST each carry the whole-invoice taxable, so summing would
  double-count).
- **Next:** GST UI (F11 config screen, party/item GST fields, GST reports UI cascading Miller-column, item-invoice
  tax display / Alt+A). Then Phase 5.
- **Phase 4 slice 4c — GST CONFIG + MASTER GST FIELDS UI:** DONE, green. Committed by the GitHub Expert (branch
  `claude/interesting-mirzakhani-30e51e`; no PR yet). New `GstConfigViewModel` + **"GST — Statutory Configuration"**
  screen reachable via **F11 (Features)** and a **Statutory → GST** menu item: Enable-GST toggle; GSTIN (Luhn-validated,
  auto-fills Home State from the leading 2 digits); Home State/UT dropdown (`IndianState`); Regular reg-type +
  Monthly/Quarterly periodicity. On Enable → `GstService.EnableGst` (creates the 6 Output/Input CGST/SGST/IGST tax
  ledgers + Round-Off, seeds slabs 0/5/18/40) + persists, then shows the created-ledgers list. Verified by headless Skia
  render (all fields + the "7 tax ledgers ready; slabs 0/5/18/40 seeded" confirmation render clean).
- **Master GST fields:** Party GST fields (GSTIN / reg-type / State) added to the **Ledger master** (gated on GST-on AND
  party group); HSN/SAC + GST-rate + taxability added to the **Stock Item master** — both pre-validated (GSTIN/HSN),
  hidden/no-op when GST is off so existing masters are byte-unchanged.
- **Gate green:** **551 tests** (Apex.Ledger 339 + Apex.Persistence.Sqlite 46 + Apex.Desktop 166, +11 Desktop over the
  531 baseline), 0 fail. Schema unchanged (**v13**).
- **Known minor UI nit (fix in 4d):** the GST config **GSTIN textbox is a touch narrow** — clips the last ~3 of 15 chars
  in the render (value + validation are correct); widen it.
- **Next:** slice 4d = GST reports UI (Tax Analysis / GSTR-1 / GSTR-3B into the Miller-column nav under a GST/Statutory
  reports section) + item-invoice GST tax display (show computed CGST/SGST/IGST on the Purchase/Sales item-invoice screen
  when GST is enabled) + the GSTIN-width fix — closes Phase 4's UI. Then Phase 4 wrap + Phase 5.
- **Phase 4 slice 4d — GST REPORTS UI:** DONE, green. Committed by the GitHub Expert (branch
  `claude/interesting-mirzakhani-30e51e`; no PR yet). Three `ReportKind`s (TaxAnalysis, Gstr1, Gstr3b) wired into the
  Miller-column nav under a **"GST Reports"** submenu (Reports section), with per-kind DataTemplates + section headers,
  reading the slice-4b report engine (reconciled to the tax ledgers): **Tax Analysis** (Outward/Inward, rate×head grid),
  **GSTR-1** (B2B / B2C / rate-wise / HSN sections), **GSTR-3B** (§3.1 outward + exempt, eligible ITC, net payable per
  head — display-only, no set-off). GST-off opens a friendly empty state.
- **Visual verification (headless Skia render) caught + fixed real layout defects:** the GST reports are wider than the
  report pane, so wide statutory reports now get a **horizontal ScrollViewer** (Tally-like) — Tax Analysis first
  "Rate/Head" column no longer collapses to zero (fixed 170px), GSTR-1's Taxable/CGST/SGST/IGST amount columns are
  reachable via h-scroll, GSTR-3B "Particulars" labels show in full. Also widened the GST-config **GSTIN textbox** (from
  slice 4c) so the full 15-char GSTIN shows. De-branded.
- **Gate green:** **561 tests** (Apex.Ledger 339 + Apex.Persistence.Sqlite 46 + Apex.Desktop 176, +10 Desktop over the
  551 baseline), 0 fail. Schema unchanged (**v13**).
- **Next:** slice 4e = item-invoice GST tax display — when GST is enabled, the Purchase/Sales item-invoice (Ctrl+I) screen
  computes + shows CGST/SGST/IGST and posts the tax lines (party total = taxable + tax) via `GstService`/`LedgerService`,
  so GST invoices created in the UI flow through to the GST reports. Then Phase 4 wrap (commit memory.md, tag/PR decision)
  + Phase 5.
- **Phase 4 slice 4e — ITEM-INVOICE GST INTEGRATION:** DONE, green. Committed by the GitHub Expert (branch
  `claude/interesting-mirzakhani-30e51e`; no PR yet). **570 tests** (Apex.Ledger 339 + Apex.Persistence.Sqlite 46 +
  Apex.Desktop 185, +9 Desktop over the 561 baseline), 0 fail. Schema unchanged (**v13**). **Closed the GST-in-UI gap:**
  when GST is enabled (`IsGstInvoice`), the Purchase/Sales item-invoice (Ctrl+I) screen resolves each line's
  rate/taxability (item→ledger→company), determines intra/inter (party `PartyGst.State` vs `Company.Gst.HomeStateCode`),
  computes tax via `GstService.ComputeInvoiceTax` (per head+rate — multi-rate splits correctly), **DISPLAYS a GST Summary
  band** (Taxable, CGST, SGST, IGST, Party Total = taxable+tax; verified by headless render — e.g. 15 Widget @₹100 @18% →
  CGST 135 / SGST 135 / Party Total 1,770), and on Accept posts the voucher = stock leg (Σ taxable) + additive tax
  `EntryLine`s (Output/Input CGST/SGST/IGST, direction from base type, `GstLineTax` metadata) + party leg (taxable+tax) +
  inventory lines via `LedgerService`. So UI-created GST invoices now flow into GSTR-1/3B/Tax Analysis (tests assert a UI
  purchase shows ITC in GSTR-3B, a UI sale a GSTR-1 B2B row). Multi-rate, inter-state (IGST), B2C all handled; pairing
  invariant intact; **GST-off unchanged** (Phase-3 two-leg behavior). A render caught + fixed a display defect (the GST
  amounts were computed but not bound to any control — added the summary band + wrapped the derived line).

- **✅ PHASE 4 (GST — core) COMPLETE (2026-07-05)** — 5 slices: **4a** core GST engine (schema v13; GST 2.0 slabs
  0/5/18/40; CGST/SGST/IGST compute-total-then-split; GSTIN Luhn validation; rate resolution; place of supply), **4b** GST
  reports engine (Tax Analysis, GSTR-1, GSTR-3B, reconciled to tax ledgers, per-(head,rate) multi-rate lines), **4c** GST
  config + master GST fields UI (F11/Statutory), **4d** GST reports UI (h-scroll statutory layouts), **4e** item-invoice
  GST integration + display. Commits `d9ef005`(4a) → `be58ab4`(4b) → `24b04e9`(4c) → `cdd06f0`(4d) → 4e (this). **570
  tests green, schema v13, de-branded, adversarially verified** — A10 caught ~7 real GST defects across the phase incl. a
  CRITICAL CGST+SGST≠IGST parity bug, a B2C place-of-supply mis-classification, and a multi-rate rate-attribution bug.
  **DEFERRED to Phase 9 per approved DPs:** RCM, composition, cess, e-invoice/e-way, GSTR-2A/2B, Rule-88A ITC set-off +
  Alt+J/Ctrl+F posting. **Next: Phase 5** per plan.md.

### Phase 5 kickoff — reports depth + print/export/import/email (2026-07-05)
- **Session resumed** on branch `claude/interesting-mirzakhani-30e51e` @ `cfc2b1d`. **Baseline re-verified GREEN by
  the orchestrator itself:** `dotnet build -c Release` = **0 warn / 0 err**; `dotnet test -c Release` =
  **570 passed / 0 failed** (Apex.Ledger 339 · Apex.Persistence.Sqlite 46 · Apex.Desktop 185), SQLite **schema v13**.
- **Phase 5 (reports depth + print/export/import/email) STARTED.** The **10 requirements-doc decision points** were
  resolved with the user:
  - **DP-3 Export formats → PDF + XLSX + CSV + JSON + XML** (5 formats). **HTML export DEFERRED (tracked).**
    CSV = **RFC-4180, UTF-8-with-BOM**.
  - **DP-4 Canonical round-trip → BOTH JSON and XML** are lossless round-trip formats (both must pass the **PR-4 hard
    gate**; the importer therefore accepts **JSON + CSV + XML**).
  - **DP-6 Email → compose + `.eml`/mail-client hand-off NOW; capture SMTP profile (no password in repo, R13). LIVE
    SMTP SEND DEFERRED** — tracked on the checklist to wire in a later phase.
  - **DP-8 Print → render-to-PDF + on-screen preview** (reuses the PDF writer; OS-native print spooler deferred).
  - **DPs adopted at the requirements-doc recommended defaults** (author's discretion, no user conflict):
    **DP-1** hand-rolled minimal PDF writer (no NuGet); **DP-2** hand-rolled XLSX OPC via built-in
    `System.IO.Compression.ZipArchive` (no NuGet); **DP-5** import scope = core masters (groups / ledgers / stock
    items / parties / voucher types) + accounting & item vouchers, **engine-routed**, duplicate policy
    skip / merge-opening-balance / reject-batch; **DP-7** Saved Views persist config-tuple only, per company
    (**schema v14**); **DP-9** single built-in tax-invoice template + paisa-accurate Indian (lakh/crore)
    amount-in-words in the pure IO layer; **DP-10** comparatives span periods (+ scenarios via `SupportsScenario`);
    multi-company comparatives wait for Phase 10.
- **Deviations from LITERAL plan.md text (R6, recorded):** **HTML export deferred**; **live SMTP send deferred**
  (both tracked). Everything else matches or exceeds the plan text (**XML kept per user**).
- **R3:** added agent **A15 "Reporting & I/O Engineer"** to `agents.md` to own the print / export / import / email
  IO layer (a new `Apex.Ledger.Io` project) — no prior agent covered it.
- **Next:** Phase 5 **slice 1 = report config & depth (RQ-1 / 2 / 6)** under way via the gated workflow.

### Phase 5 slice 1 — Report config & depth (RQ-1/2/6) ✅ (2026-07-05)
- **Workspace consolidated:** the session worktree `keen-albattani-a09dfd` (branch `claude/keen-albattani-a09dfd`) was fast-forwarded to the Phase-4 tip `cfc2b1d` (lossless; `main` was its ancestor) and is now the SINGLE live workspace at schema v13, with governance commit `6bb6bc3` on top. The `interesting-mirzakhani-30e51e` worktree is left clean as a snapshot. **Lesson:** workflow subagents operate in the SESSION's own worktree cwd — the first slice-1 attempt wrote to the wrong base (main/v8); fixed by consolidating. Point agents at their own cwd and have them verify the branch before editing.
- **Delivered:** RQ-1 period (F2 as-of / Alt+F2 [from,to]), RQ-2 detailed/summary (Alt+F1) on BS/P&L/TB/Stock Summary, RQ-6 F12 config column (hide-zero, show-%, closing-stock valuation basis). Engine: new `ReportOptions`/`PeriodRange`/`ReportConfig`/`ReportGrouping` (immutable options; defaults reproduce legacy exactly) + overloads on TrialBalance/ProfitAndLoss/BalanceSheet. UI: `ReportConfigViewModel` + F12 panel as a cascading Miller-column column (report stays live to the left); keyboard-first; de-branded (headless Skia render verified — "Gateway of Apex Solutions", zero "Tally").
- **Adversarial review (A10, 4 lenses) caught 4 confirmed defects, all fixed + regression-tested:** (HIGH) windowed P&L ignored `period.From` → now windows income/expense via `SignedMovement`, opening stock @From−1, closing @To; (HIGH, fidelity) period Trial Balance used in-window movement dropping opening-only ledgers → now closing-as-at `period.To` (opening carried forward, like Balance Sheet), TB clause relabeled "as at {To}", `period.From` has no effect on TB by design; (LOW) summary TB blank row for net-zero groups → suppressed to match legacy detailed; (LOW) F12 Apply falsely reported success on inverted/unparseable dates → now a validation-error status.
- **Gate (orchestrator-re-run):** `dotnet build -c Release` 0/0; `dotnet test -c Release` = **614 passed / 0 failed** (Ledger 356 · Sqlite 46 · Desktop 212). Robert & Bright green. No schema change (still v13).
- **Next:** Phase 5 slice 2 — sort/filter (Alt+F12) + comparative/columnar (Alt+C/Alt+N across periods & scenarios) (RQ-3, RQ-4).

### Phase 5 slice 2 — Report sort & filter (RQ-3) ✅ (2026-07-05)
- **Delivered RQ-3:** Alt+F12 Sort/Filter panel (cascading Miller-column column) — sort by Name (ordinal, case-insensitive) or Amount (magnitude), asc/desc, stable; value-range filter [min,max] (either bound optional) + name-substring filter. Engine: new `ReportSortFilter` (immutable; `Apply<T>` = filter-then-sort; identity = source unchanged) + `ReportConfig.SortRows`/`FilterRows`. UI: `ReportSortFilterViewModel` + panel mirroring the slice-1 F12 pattern; validates bounds (rejects unparseable/negative/inverted; no false "Applied"). Applied to TB/BS/P&L/Stock Summary/Day Book row sections AFTER build+hide-zero; **filter is a VIEW — Grand Totals stay engine-computed over the FULL set** (a filtered view may not itself balance, by design).
- **Outage note:** an API ConnectionRefused outage killed the original workflow's render+review stages (the build stages had already completed and their edits persisted). Recovered by re-running a review-only workflow after re-gating. Lesson: verify the tree and re-run only the missing stages.
- **Render-check PASS** (headless Skia): panel is a clean column beside the live report; filter/sort visibly applied; Grand Total stays full/balanced; zero "Tally".
- **Adversarial review (A10, 4 lenses): no critical/high.** Fixed 2 confirmed LOW Day Book UX defects (+3 regression tests): (a) filter/sort now targets the DISPLAYED particulars ("{type} No. {number}" + party), not a hidden internal string; (b) distinct empty-state — "No rows match the current filter." for a filtered-empty non-empty period vs "No vouchers in this period." for a genuinely empty period.
- **Recorded decisions (won't-fix, documented):** amount filter uses MAGNITUDE (|amount|), not signed — catalog-silent, defensible as a row-weight view. **Model correction:** `Money.cs` stores EXACT decimal RUPEES in-memory (integer paisa only at the SQLite persistence boundary) — `decimal` is exact, no float; the "integer-paisa via Money.cs" phrasing was aspirational. Use exact decimal-rupees Money; never double/float.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **650 passed / 0 failed** (Ledger 375 · Sqlite 46 · Desktop 229). Schema v13 unchanged. Robert & Bright green.
- **Next:** Phase 5 slice 3 — comparative/columnar (Alt+C add column, Alt+N auto-columns across periods & scenarios) (RQ-4).

### Phase 5 slice 3 — Comparative/columnar reports (RQ-4) ✅ (2026-07-05)
- **Delivered RQ-4:** Alt+C add a comparison column (period and/or scenario) + Alt+N auto-columns (By month over the current period, or By scenario) for Trial Balance, Balance Sheet, P&L, Stock Summary. Engine: new `ComparativeReport` (composes the existing single-column builders per `ColumnSpec`; merges rows by stable key aligned to column order — a null cell = key absent, distinct from a real zero; per-column totals; `MonthlyColumns` clamps partial months; `ScenarioColumns` prepends "Actual"). UI: `AddComparisonColumnViewModel` + `AutoColumnsViewModel` panels (cascade Miller-column), multi-column grid reusing the GST-report horizontal-scroll pattern (header offset OneWay-synced to the body scroller), "Single Column" reset. Zero-extra-column path leaves the single-column report untouched.
- **Render-check PASS** (headless Skia, base + 12 monthly columns): columns render side by side with h-scroll, header scrolls in lockstep with data, aligned rows, no overlap, zero "Tally".
- **Adversarial review (A10, 4 lenses): fidelity + de-brand PASS.** Found + fixed 3: (HIGH) comparative BASE column used the engine FY-end default as-of instead of the report's actual as-of → dropped vouchers dated after FY-end; (MED) base column dropped the slice-1/F12 options (Detailed/HideZero/%/ClosingStock basis); (MED) header ScrollViewer "Auto" duplicated/fought the body scrollbar. Fix: `ColumnSpec` now carries the report's full `ReportOptions` (`OptionsFor` threads as-of + flags + ClosingStock); `BaseColumnSpec` passes `_options`; added/auto columns inherit display flags; header ScrollViewer → "Hidden". Each locked with a regression test (incl. the Rent-after-FY-end repro). A stray implementer scratch file was caught & removed during review.
- **Recorded (won't-fix, catalog-unspecified):** Alt+N monthly keeps the base column alongside the month columns (defensible); Tally's other Alt+N axes (Company/Currency/Quarter/Stock Item) are out of this slice's scope.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **686 passed / 0 failed** (Ledger 385 · Sqlite 46 · Desktop 255). Schema v13 unchanged. Robert & Bright green.
- **Next:** Phase 5 slice 4 — new report families part 1: Cash Flow, Funds Flow, Ratio Analysis (RQ-5); slice 5 = Exception reports.

### Phase 5 slice 4 — New report families pt.1: Cash Flow / Funds Flow / Ratio Analysis (RQ-5) ✅ (2026-07-05)
- **Delivered 3 new statement reports** (engine projections composed from BalanceSheet/ProfitAndLoss/LedgerBalances, no re-derivation; nested under Reports → Statements):
  - **Cash Flow** — opening→closing cash+bank reconciliation over a period (Inflows/Outflows sections, Net Cash Flow); Opening+Net==Closing by double entry.
  - **Funds Flow** — Sources vs Applications (Funds From Operations = balancing residual); Total Sources==Total Applications.
  - **Ratio Analysis** — **Tally-faithful, web-verified against official TallyHelp**: Principal Groups (Working Capital, Cash-in-Hand, Bank, Sundry Debtors/Creditors, Sales/Purchase, Stock-in-Hand, Nett Profit, Capital) + Principal Ratios (Current, Quick, Debt/Equity, GP%, NP%, Operating Cost%, Receivables Turnover days, ROI% = NettProfit/(Capital+NettProfit), Return on Working Capital%, Inventory Turnover, Working Capital Turnover). All divide-by-zero guarded (→ "N/A").
- **Render-check PASS** (headless Skia, Bright): all 3 render cleanly under Reports → Statements, figures reconcile on-screen, no overlap, zero "Tally".
- **Adversarial review (A10, 4 lenses):** regression + de-brand PASS. Fidelity (R7) caught real Ratio-Analysis gaps → fixed: added 4 missing ratios (Working Capital Turnover, Operating Cost %, Receivables Turnover days, Return on Working Capital %), corrected ROI% denominator to Tally's definition, added the Principal-Groups breakdown; all formulas web-verified (TallyHelp, cited). Code-quality: classify by group ID not name (BalanceSheetLine gained GroupId), removed a dead branch, fixed a misleading test comment. Known limitation documented: scenario+period Sales uses SignedMovement (no scenario overload), same as P&L.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **703 passed / 0 failed** (Ledger 392 · Sqlite 46 · Desktop 265). Schema v13 unchanged. Robert & Bright green.
- **Next:** Phase 5 slice 5 — Exception reports (Negative Stock, Negative Cash/Bank, Memorandum register, Reversing-Journal register).

### Phase 5 slice 5 — Exception reports (RQ-5 pt.2) ✅ (2026-07-05)
- **Delivered 4 exception reports** (engine projections composed from the inventory on-hand engine + LedgerBalances + voucher-type filters; nested under Reports → Exception Reports):
  - **Negative Stock** — items with negative on-hand (as-at): item/godown/qty/value; shortfall valued at best-available unit cost incl. item-invoice purchase rates.
  - **Negative Cash/Bank** — cash/bank ASSET-nature ledgers with a credit (negative) balance; Bank OD/OCC (liability-nature) correctly EXCLUDED (their credit balance is by design).
  - **Memorandum Register** — Memorandum vouchers over a period (date/no/party/amount + Total).
  - **Reversing Journal Register** — Reversing Journal vouchers with ApplicableUpto (effective date) + Total.
- **Render-check ALL PASS** (headless Skia): each renders cleanly under Reports → Exception Reports, empty-state clean, no overlap, zero "Tally".
- **Adversarial review (A10, 4 lenses): fidelity/de-brand/regression PASS.** Fixed 2 MEDIUM + 2 LOW: (MED) Negative Cash/Bank false-positive on Bank OD/OCC → nature-based exclusion (only asset-nature credit balances are exceptions); (MED) Negative Stock valued item-invoice-purchased items at ₹0 → `ReferenceUnitCost` now includes item-invoice inward rates; (LOW) wrong catalog §17→§16 doc refs + stripped 2 stray "Tally" mentions from comments; (LOW) batch-level negative masking documented as a Phase 6 limitation. Each locked with a test.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **729 passed / 0 failed** (Ledger 405 · Sqlite 46 · Desktop 278). Schema v13 unchanged. Robert & Bright green. **RQ-5 (new report families) COMPLETE.**
- **Next:** Phase 5 slice 6 — RQ-7 universal drill-down (Enter on TB/BS/P&L rows → ledger vouchers → voucher).

### Phase 5 slice 6 — Universal drill-down (RQ-7) ✅ (2026-07-06)
- **Delivered RQ-7:** Enter (keyboard-first) or double-click on a Trial Balance / Balance Sheet / P&L ledger row drills to that ledger's vouchers (new `LedgerVouchersViewModel`, a cascading Miller-column with running balance), then Enter on a posting → the voucher (`VoucherDetailViewModel`, read-only). Day Book Enter → voucher directly. Stock Summary → Movement drill unchanged.
- **Engine:** appended `Guid LedgerId` to TrialBalanceRow/BalanceSheetLine/ProfitAndLossLine (+ `IsDrillable`), `Guid VoucherId` to DayBookRow/LedgerBookRow; synthetic heads (folded Net Profit, derived Stock-in-Hand) non-drillable. `LedgerBook.Build` gained a movement mode + Guid.Empty guard. Figures byte-for-byte unchanged.
- **UI:** ReportRow surfaces DrillLedgerId/DrillVoucherId/IsDrillable; generalized `ReportsViewModel.Drill` dispatch (DrillToLedgerRequested/DrillToVoucherRequested events); drill columns append without trimming (prior panes persist); Esc/Back pops + rehydrates.
- **Session recovery:** the first slice-6 run was interrupted by a process exit (partial engine-only tree, discarded via A12); the re-run's UI agent glitched (returned a leaked prompt, wrote nothing) so the engine landed but the UI didn't — completed via a dedicated UI workflow. Lesson: verify tree ground-truth after any interruption; a glitched agent's self-report is worthless — the tree + gate are the authority.
- **Adversarial review (A10, 4 lenses):** render ALL PASS (3-level drill cascade verified headlessly). Fixed 1 HIGH + 2 MED: (HIGH) keyboard Enter was preempted by the Window tunnel handler → two-way SelectedRow + tunnel-stage Enter check drills before cascade nav (proven by a real-keys HeadlessMainWindowTests); (MED) P&L period drill showed cumulative closing → movement mode reconciles the ledger-vouchers total to the P&L period figure ("Period Movement"); (MED) report shortcuts leaked into drill columns → IsReportContext gated to exclude LedgerVouchers/VoucherDetail. Each locked with tests incl. real-window key tests.
- **De-brand sweep (ER-11):** removed 10 stray "Tally" brand refs from code comments/XML-docs (8 in RatioAnalysis.cs from slice-4 web-verify citations that slipped in post-review, + ReportsViewModel/TrialBalance) → zero brand "Tally" in shipped src. Lesson: web-verify citations belong in the return text/memory, never in code comments.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **751 passed / 0 failed** (Ledger 416 · Sqlite 46 · Desktop 289). Schema v13 unchanged. Robert & Bright green.
- **Next:** Phase 5 slice 7 — RQ-8 Save View (persist config tuple per company; SQLite schema v14).

### Phase 5 slice 7 — Save View (RQ-8) ✅ + SQLite schema v14 (2026-07-06)
- **Delivered RQ-8:** save/list/open/delete named report VIEWS per company, persisting the CONFIG TUPLE ONLY (kind + period/as-of + detail + hide-zero/%/closing-stock + scenario name + sort/filter + comparative columns) — NEVER computed figures (ER-9/DP-7); opening a view recomputes fresh so it can never go stale.
- **Data:** framework-agnostic `SavedReportView` model (deterministic culture-invariant System.Text.Json, enums by name, tolerant of unknown enum names) + `ISavedReportViewRepository` port (upsert/list/get/delete, company-scoped, NOCASE); `ReportKind` kept out of Apex.Ledger (Desktop maps to/from a stable string token). SQLite **schema v14**: `MigrateV13ToV14` (CREATE saved_views + unique index (company,name), no ALTER), CurrentVersion→14, CreateV1 extended so fresh DBs stamp to v14; SqliteCompanyStore implements the port.
- **UI:** ToSavedView/ApplySavedView on ReportsViewModel + SaveViewViewModel (name prompt) + SavedViewsViewModel (list/apply/delete) nested under Reports; applying reproduces the exact configured report.
- **Render PASS** (headless, real-window key pipeline): save → saved-views list → apply reproduces the configured report; clean Miller-columns, zero "Tally".
- **Adversarial review (A10, 4 lenses):** 1 MEDIUM only (FromJson threw on an unknown enum name) → tolerant enum converter falls back to defaults so a corrupt/newer saved view still loads.
- **Gate discipline caught 8 Sqlite failures the build agent mis-labeled "flakes":** 4 were stale hard-coded schema-version asserts (Expected 13/Actual 14 after the v14 bump) → re-pointed to `Schema.CurrentVersion`; 4 were a connection-pool `File.Delete` lock → shared `TempDbFile.Delete` (ClearAllPools + tolerant retry) across all round-trip tests. The robust teardown UNMASKED a real test-fixture bug: DowngradeToV11/V9 helpers didn't drop the v14 saved_views table → "table already exists" on re-migration → fixed (DROP IF EXISTS). Production migration path was not at fault (disposes correctly).
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **774 passed / 0 failed** (Ledger 422 · Sqlite 52 · Desktop 300). Robert & Bright green. **Schema v14.**
- **Next:** Phase 5 slice 8 — IO foundation: new `Apex.Ledger.Io` project + hand-rolled PDF writer + render-to-PDF "print" + preview (RQ-9/10/13). [A15 Reporting & I/O Engineer]

### Phase 5 slice 8 — IO foundation: report → PDF print + preview (RQ-9/13) ✅ (2026-07-06)
- **New framework-agnostic project `Apex.Ledger.Io`** (ER-3): a hand-rolled minimal PDF writer (no NuGet) — %PDF-1.4, catalog/page-tree/content-streams, standard-14 Helvetica + Helvetica-Bold, real WinAnsi (CP1252) text encoding, xref/trailer/%%EOF, A4/Letter + portrait/landscape page model, margins, pagination, header/footer with page N of M. Deterministic, culture-invariant, byte-stable, de-branded (/Producer //Creator //Title = "Apex Solutions"; zero "tally" in bytes). A ReportPdf renderer (PrintReport/PrintColumn/PrintRow + PageConfig) with right-aligned amounts, bold section-header/total rows, and cell-text truncation-with-ellipsis to column width.
- **UI:** ReportPrintProjector (pure; maps live ReportsViewModel rows → PrintReport, folds non-WinAnsi glyphs e.g. ₹→"Rs.") + PrintPreviewViewModel (renders to PDF bytes, paginated on-screen preview, A4/Letter + orientation toggles, Save PDF) wired to **P / Ctrl+P** in report context (the header "P: Print" hint is now live). Thin Avalonia layer only (ER-12): file path + stream + preview; all IO logic in Apex.Ledger.Io.
- **Validation** (independent strict xref/trailer parser + poppler `pdftotext` v4.00): the generated PDF is structurally valid, opens in a real reader, text extracts correctly, em-dashes render (WinAnsi 0x97) not '?', long cells truncate within their column, headers/totals bold, byte-identical re-render, zero "tally".
- **Adversarial review (A10, 4 lenses):** validation PASS. Fixed 3 MED + 3 LOW output-quality issues (WinAnsi encoding vs '?', long-text overflow, header-styling no-op, PageConfig em-dash default, /Title em-dash, projector "Col N" placeholder). Each locked with tests.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **795 passed / 0 failed** (Apex.Ledger.Io 13 · Ledger 422 · Sqlite 52 · Desktop 308). Schema v14. Robert & Bright green. **4 test projects now.**
- **Next:** Phase 5 slice 9 — voucher print (RQ-10) + invoice print (RQ-11, tax-invoice + amount-in-words) + print config F12 (RQ-12). [A15]

### Phase 5 slice 9 — Voucher + tax-invoice print + F12 print config (RQ-10/11/12) ✅ (2026-07-06)
- **Delivered in Apex.Ledger.Io:** IndianAmountInWords (paisa-exact Indian lakh/crore words), VoucherPdf (RQ-10: header + Dr/Cr lines + totals + amount-in-words + narration), InvoicePdf (RQ-11: single built-in TAX INVOICE template — seller/buyer GSTIN blocks, item table Sr/Description/HSN/Qty/Rate/Amount, per-rate GST breakup CGST+SGST intra or IGST inter, taxable + tax + round-off + grand total, amount-in-words, declaration/signature), PrintConfig (RQ-12: title override, narration toggle, copy marking Original/Duplicate/Triplicate). All paginate; reuse the slice-8 PdfWriter; deterministic, byte-stable, de-branded.
- **Mandatory tax-invoice fields WEB-VERIFIED against CGST Rule 46** (CBIC taxinformation.cbic.gov.in + taxguru/gstzen secondary sources) — supplier/recipient GSTIN, invoice no/date, HSN, qty/rate, taxable value, per-head tax rate+amount, place of supply, signature, copy marking; RCM field noted (deferred to Phase 9). Logo embedding deferred (DP-9).
- **UI:** VoucherPrintProjector (pure; item-invoice→InvoicePdf routing via GstService, plain voucher→VoucherPdf) + PrintConfigViewModel (F12 print config) + P/Ctrl+P; thin Avalonia layer (ER-12).
- **Validation** (strict xref parser + pdftotext): voucher & invoice PDFs valid/open; GST figures reconcile to the engine to the paisa; amount-in-words matches the grand total; copy marking + both GSTINs present.
- **Adversarial review (A10, 4 lenses) caught 2 HIGH + 1 LOW, all fixed + tested:** (HIGH, financial) a mixed invoice with an exempt/nil line UNDER-FOOTED — the exempt value was dropped from Taxable Value & Grand Total (customer under-billed) → TotalTaxable now sums ALL line values (8,750@18% + 2,000 exempt → 12,325); (HIGH) VoucherPdf/InvoicePdf didn't paginate — long docs clipped totals/GST-breakup/words/signature off-page → now paginate (repeat header, keep the closing block together, Page N of M); (LOW) a user TitleOverride/narration containing "Tally" leaked into the PDF → new Debrand.Text sanitizes all user text (incl. /Title).
- **De-brand sweep:** shipped src is now zero brand "Tally" except the intentional Debrand.cs stripping-regex pattern.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **845 passed / 0 failed** (Apex.Ledger.Io 54 · Ledger 422 · Sqlite 52 · Desktop 317). Schema v14. Robert & Bright green.
- **Next:** Phase 5 slice 10 — export (CSV/JSON/XML/XLSX) of reports & masters (RQ-14..19). [A15]

### Phase 5 slice 10 — Tabular export: CSV + XLSX (RQ-14..18) ✅ (2026-07-06)
- **Delivered in Apex.Ledger.Io:** TabularExport model (Text/Number cells; Number carries the exact decimal), CsvWriter (RFC-4180, UTF-8-with-BOM, CRLF, formula-injection guard), XlsxWriter (hand-rolled minimal OPC via built-in System.IO.Compression.ZipArchive — no NuGet: [Content_Types].xml + _rels + workbook + sheet1 with numeric `<c t="n">` cells, XML-illegal control chars stripped, deterministic zip → byte-stable, opens without repair — verified via System.IO.Packaging), ExportConfig (folder/filename/optional timestamp passed in; no clock), TabularDebrand (newline-safe cell de-brand). **UI:** ReportTabularProjector (report → TabularExport; money as exact Number cells so a spreadsheet sums them; real on-screen column captions) + ExportViewModel + E/Alt+E export panel (format/destination/timestamp).
- **Validation** (ZipArchive + XmlDocument + System.IO.Packaging + strict RFC-4180 parser): CSV BOM + well-formed, XLSX valid OPC opens without repair, money paisa-exact, byte-stable, zero "tally".
- **Adversarial review (A10, 4 lenses) caught 3 HIGH + 1 MED + 2 LOW; fixed 4 (+ regression tests):** (HIGH) numeric cells preserve real precision (qty 10.125 / rate 3.3333, not forced 2dp); (HIGH) XLSX XML-illegal control chars stripped so Excel opens without repair; (MED) wide inventory/GST exports had blank headers → real per-kind on-screen captions; (LOW) CSV formula/macro injection (=,+,-,@) neutralized with a leading ' (negative money stays a plain summable number). **Deferred (LOW):** master-list export wiring → Phase 5 slice 13 (finalization).
- **Note:** a usage-limit interruption hit mid-fix; recovered by verifying the WIP tree still built + 68 Io tests green, then re-ran the fix workflow after the limit cleared (STOP-at-checkpoint discipline held — last commit b54ab51 stayed the clean fallback).
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **880 passed / 0 failed** (Apex.Ledger.Io 76 · Ledger 422 · Sqlite 52 · Desktop 330). Schema v14. Robert & Bright green.
- **Next:** Phase 5 slice 11 — canonical JSON + XML data export + import (JSON/CSV/XML) + lossless round-trip HARD gate (RQ-19/20..24, PR-4). [A15]

### Phase 5 slice 11 — Canonical JSON+XML export/import + lossless round-trip (RQ-19/20..24, PR-4) ✅ (2026-07-06)
- **Delivered the DATA round-trip.** Apex.Ledger.Io: CanonicalModel (a COMPLETE mirror of the Company aggregate — audited against SQLite Schema v14; money=integer paisa, rates=micros, deterministic), CanonicalMapper (export), CanonicalJson + CanonicalXml (versioned envelope {formatVersion:1, schemaVersion:14, company, payload}; XML XXE-safe DtdProcessing.Prohibit; strict ISO dates; byte-stable), CsvImport (flat). CompanyImportService (ENGINE-ROUTED apply, ER-6: masters via domain create, vouchers via LedgerService.Post/VoucherValidator; validate-before-apply with per-record messages; TRANSACTIONAL all-or-nothing via ApplyJournal; duplicate policy Skip/MergeOpeningBalance/RejectBatch).
- **Covers EVERYTHING** (review caught the round-trip silently dropping data on a narrow fixture): groups/ledgers (incl. interest, cheque-printing, currency)/voucher-types/units/stock masters/opening balances, accounting + item-invoice vouchers + GST line tax + bill/cost/bank allocations + forex, cost categories/centres, currencies + exchange rates, budgets, scenarios, inventory vouchers (GRN/Delivery/Rejections/Stock-Journal/Physical + order/physical lines). Predefined masters (Cash, P&L head/group) reused-by-name not duplicated.
- **UI:** ImportDataViewModel (O/Alt+O: file+format+duplicate-policy → read → apply, reports per-record errors, nothing on failure) + ExportDataViewModel (canonical JSON/XML backup); thin Avalonia layer (ER-12).
- **PR-4 EXIT GATE PASS** (validated independently): export Bright(rich: +cost+bank+forex+currency/rate+budget+scenario+inventory-voucher) → import into a fresh company → every report figure (TB/BS/P&L/Stock/GST) reconciles to the PAISA AND every master + per-line sub-object count is EQUAL source==target, on BOTH JSON and XML. A corrupted batch (unbalanced / missing-ledger) is rejected with a message and leaves a pre-existing GST company 100% UNCHANGED. XML DOCTYPE/entity rejected (XXE). Byte-stable; zero "tally". The gate has teeth (asserts exact counts + paisa figures).
- **Adversarial review (A10, 4 lenses) caught 2 CRITICAL + 3 HIGH + 2 MED + 2 LOW across two fix rounds:** silent data drops (cost/bank/forex/budgets/scenarios/inventory-vouchers) → complete envelope; rollback deleted pre-existing GST ledgers/config → snapshot-before + prune-only-created; XXE → DtdProcessing.Prohibit; too-narrow fixture → rich fixture + exact-count assertions; P&L-head group duplicated → FindGroupOrHeadByName reuse; + de-brand comment + strict ISO dates.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **922 passed / 0 failed** (Apex.Ledger.Io 95 · Ledger 433 · Sqlite 52 · Desktop 342). Schema v14. Robert & Bright green.
- **Next:** Phase 5 slice 12 — email (compose + .eml/mail-client hand-off; SMTP profile captured, live send deferred) (RQ-25..27). [A15]

### Phase 5 slice 12 — Email compose + .eml/mailto hand-off + SMTP profile (RQ-25..27) ✅ + SQLite schema v15 (2026-07-06)
- **Delivered email compose (OFFLINE; live SMTP send DEFERRED — tracked).** Apex.Ledger.Io: EmlComposer (RFC-5322/MIME multipart/mixed .eml — base64 body + attachment; deterministic, no clock/RNG: fixed boundary + caller-supplied Date/Message-ID; header FOLDING ≤998/≤78; RFC-2047 encoded-words split ≤75 on UTF-8 boundaries; RFC-2231 non-ASCII filenames; header-injection HARDENED — CR/LF stripped from free-text, rejected in structural fields), Mailto (RFC-6068 percent-encoded), SmtpProfile (host/port/TLS/from — NO password, R13, reflection-guarded). SQLite **schema v15**: MigrateV14ToV15 (CREATE smtp_profile, no password column) + ISmtpProfileRepository over SqliteCompanyStore.
- **UI:** EmailComposeViewModel (M/Ctrl+M: To/Cc/Subject/Body + attach the exported PDF → write .eml / open mail client; panel states nothing is sent) + SmtpSettingsViewModel (capture profile, no password field). Thin Avalonia layer (ER-12); no socket/SMTP path exists.
- **Validation** (independent MIME parser): .eml valid RFC-5322/MIME, attachment base64 decodes to the EXACT exported PDF bytes, byte-stable, zero "tally", no SmtpClient/socket; SMTP profile persists with no password column.
- **Adversarial review (A10, 4 lenses incl. a header-injection SECURITY lens) caught 3 HIGH + 1 MED, all fixed + tested:** (HIGH, security) email header injection via CR/LF in header values → stripped/rejected; (HIGH) long recipient headers exceeded RFC-5322 998 → folded; (HIGH) non-ASCII attachment filename raw bytes → RFC-2231 filename*; (MED) over-long RFC-2047 encoded-word → split ≤75.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **976 passed / 0 failed** (Apex.Ledger.Io 125 · Ledger 433 · Sqlite 59 · Desktop 359). Schema v15. Robert & Bright green.
- **Next:** Phase 5 slice 13 — wire the live P/E/M/O shortcut bar + report-config shortcuts + Reports-nav completeness + master-list export (deferred from slice 10) + Phase-5 completeness audit (RQ-28..30) [A8/A15]. LAST Phase-5 slice; then pause + launch the app for user inspection.

### Phase 5 slice 13 (final) — P/E/M/O bar + report-config shortcuts + Reports nav + master-list export (RQ-28..30) ✅ (2026-07-06) — PHASE 5 COMPLETE
- **Finalization:** (RQ-28) verified the live P/E/M/O shortcut bar (P/Ctrl+P Print · E/Alt+E Export · M/Ctrl+M E-Mail · O/Alt+O Import) — each live in context; fixed a button-bar 'O' hint collision (Outstandings → "Outs" so bare-O = Import). (RQ-29) verified all report-config shortcuts (F2/Alt+F2/Alt+F1/Alt+F12/F12/Alt+C/Alt+N/Ctrl+S Save View/Enter drill), no collisions. (RQ-30) all report families nested under Reports; ADDED an "Account Books" family (Cash Book/Bank Book/Ledger) reusing the LedgerBook drill.
- **Master-list export** (deferred from slice 10): generic `IMasterListExportSource` on 12 master VMs (Chart of Accounts, Ledgers, Stock Items, Groups, Cost Centres/Categories, Godowns, Units, Currencies, Scenarios, Budgets, Stock Groups/Categories) + Parties — E/Alt+E exports EVERY master screen to CSV/XLSX/PDF (amounts as summable Number cells, real captions).
- **Render-check PASS** (headless Skia): master-list export on non-bespoke screens; corrected 'O' hint; Account Books nested; de-branded.
- **A14 COMPLETENESS AUDIT:** all RQ-1..30 IMPLEMENTED + tested; §7 DoD + the 4 approved divergences (XML kept/HTML deferred, SMTP send deferred, print=to-PDF) satisfied; PR-1..5 hard gates each green (Robert & Bright reconcile; GST golden tax-invoice; lossless JSON+XML round-trip + corrupted-rejected; output writers headlessly validated). Minor deferrals noted (per-voucher Sales/Purchase registers; right-palette E/P buttons — top bar already has them) — non-blocking.
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **989 passed / 0 failed** (Apex.Ledger.Io 125 · Ledger 433 · Sqlite 59 · Desktop 372). Schema v15. Robert & Bright green.
- **✅ PHASE 5 (reports depth + printing/export/import/email) COMPLETE.** All of Phase 5 (slices 1–13) committed & pushed on `claude/keen-albattani-a09dfd`. Next (after the user's go-ahead following a live app inspection): **Phase 6 — Advanced inventory** (batches/expiry, BOM & Manufacturing Journal, additional cost of purchase, Price Levels/Lists, Reorder + status report, POS multi-tender, Job Work).

---

## PHASE 6 — Advanced Inventory (2026-07-06 →)

### Phase 6 kickoff (2026-07-06)
- **Workspace reconciliation:** this session started in a FRESH worktree `pensive-hellman-5627d3` on branch `claude/pensive-hellman-5627d3` @ bare `main` (84aa3d3 = Phase-2/schema-v8) — NOT the authoritative tree. A12 fast-forwarded it (lossless; main was a strict ancestor) to the `claude/keen-albattani-a09dfd` tip `4ecc05b`, so subagents (which run in the SESSION's own worktree cwd) act on the right tree. **Working branch is now `claude/pensive-hellman-5627d3`** (pushed to origin, upstream set); keen-albattani kept as a clean snapshot. Baseline **989 green re-verified by the orchestrator** before starting.
- **Requirements (A13 CA + A14 fidelity):** `docs/phase6-advanced-inventory-requirements.md` — 54 RQ / 8 clusters / 8 DP / 13 ER / 11 PR hard gates / 9-slice plan; committed `55f568a`. User **APPROVED proceeding at all 8 DP defaults + the 7 catalog-faithful narrowings** (D-1 GST-on-freebies + add'l-cost-in-assessable-value → Phase 9; D-2 reorder = catalog set only [no ABC/lead-time]; D-3 POS no session/loyalty/hardware; D-4 job-work = material movement + orders [charges ride normal vouchers]; D-5 job work gated by F11 "Enable Job Order Processing" [not stale Show-Inactive]; D-6 expiry warns-not-blocks; D-7 single-v16 — see deviation below).
- **Deviation (R6, logged):** using **per-slice incremental `MigrateVN→VN+1` bumps** (v16 batches, v17 BOM, …) instead of DP-7's single big v16 — matches the kickoff's explicit migration guidance + the Phase-5 multi-bump-per-phase precedent; avoids speculative up-front design of later clusters' tables; ZERO functional impact.
- **Per-slice loop:** backend workflow (schema→engine→persistence/round-trip→A10 review→fix) → orchestrator re-runs the gate → A12 commits+pushes backend → UI workflow (build+headless-Skia render→A10 render review→fix) → orchestrator re-gates → A12 commits+pushes UI → memory log. Big slices split backend/UI into 2 commits for safe checkpoints.

### Phase 6 slice 1 — Batches & Expiry (RQ-1..8,52,54) ✅ (2026-07-06) — SQLite schema v16
- **Backend `a471c05`:** schema **v16** — additive `MigrateV15ToV16`: `batch_masters` (id, company, stock_item, batch_no, mfg/expiry date, expiry_period text, godown, inward qty/rate — money=paisa, qty=micros) with UNIQUE(stock_item_id, batch_no) = per-item-not-global, + nullable `batch_id` FK on the 4 stock-line tables (`batch_label` text kept for back-compat); lossless/idempotent (PR-11). Engine (framework-agnostic `Apex.Ledger`): `BatchMaster`, `ExpiryPeriod`→date resolution (RQ-4), 3 independent `StockItem` switches (RQ-2 — Use-Expiry may be on w/o Track-Mfg), `BatchService` (create/delete, per-item uniqueness, delete-blocked-while-referenced), `BatchStockService` (batch-aware on-hand per (item,godown,batch); **default issue = FEFO when the ITEM's `UseExpiryDates` is on else FIFO-by-inward (DP-1)**, manual pin; **per-batch inward rate authoritative (DP-8)**; `ExpiryWarningFor` = non-blocking Expired/NearExpiry, RQ-7); `BatchwiseReport` + `BatchAgeAnalysis` (RQ-8, past-expiry flagged). Canonical JSON+XML round-trip extended for batches (PR-4 lossless: batch masters + switches survive save/reload AND export→import to a fresh company reconcile every count + figure to the paisa; corrupted rejected; XXE-safe).
- **A10 backend review (4 lenses)** caught **1 HIGH** — `DefaultIssueSelection`/`CompareForIssue` chose FEFO-vs-FIFO on whether each BATCH carried an expiry date, IGNORING the item's `UseExpiryDates` switch → a FIFO-mode item whose batches have expiry dates silently shipped soonest-to-expire + mis-valued COGS → fixed to gate on `item.UseExpiryDates` (report row-sort too); + **1 MEDIUM** doc: the XML-doc over-claimed ER-4 reconciliation for the intended DP-8 per-batch-vs-average-cost divergence → corrected + froze the intended divergence with a test. Both regression-locked.
- **UI `475396f`:** F11 "Maintain Batch-wise details" flag; three item batch switches (gated by the company flag); **Batch master** (Masters → Create → Inventory, per-item-unique, IMasterListExportSource); **batch-allocation sub-screen** (opens on a batch-tracked line via **Alt+B** / ⧉ button; repeatable per-batch lines, Σ batch qty = line qty enforced live; FEFO/FIFO default via the engine; red EXPIRED / amber near-expiry non-blocking warning); **Batch-wise + Age-Analysis reports** (Reports → Inventory Reports → Batch, h-scroll, past-expiry rows red). Cascade Miller-column, keyboard-first, figures from the engine (ER-4).
- **A10 UI review (headless render)** caught **2 HIGH** — (a) batch-allocation sub-screen had NO keyboard entry + the tooltip falsely advertised Alt+B (NFR-2) → added a real `Alt+B` handler resolving the focused line; (b) the Rate column STILL clipped despite the build agent's *claimed* fix → wrapped in an h-scroll ScrollViewer + resized (header/rows share one width) → **2 MED** (batch-master header/row ColumnDefinitions mismatch → unified; ⧉ button leaked onto non-batch lines regardless of gating → bound to a `WantsBatchAllocation` gate) → **2 LOW** (FEFO default seed missed StockJournal/MaterialOut outward lines → broadened `IsOutwardMovement`; F11 batch flag read as a GST option → added COMPANY-FEATURES/STATUTORY sub-headings). All fixed + re-rendered + tested. **Orchestrator also caught + fixed a stray "Tally" doc-comment** (`StockItemMasterViewModel.cs:140`, "matching Tally's config-driven visibility") the fix agent had misjudged as pre-existing → reworded; full de-brand grep now ZERO brand "Tally" in shipped src.
- **Gate (orchestrator-re-run, TWICE — after backend and after UI):** build 0/0; `dotnet test -c Release` = **1034 passed / 0 failed** (Apex.Ledger.Io 129 · Ledger 455 · Sqlite 65 · Desktop 385). Schema **v16**. Robert & Bright green (ER-13). **PR-3 (FEFO batch pick + expiry flag) + PR-11 (migration lossless/idempotent) met.**
- **Next:** Phase 6 slice 2 — BOM master + Manufacturing Journal (RQ-9..15; schema v17; PR-4 = manufacture reconciles to the paisa). [A5]

### Phase 6 slice 2 (UI) — BOM master + Manufacturing Journal (RQ-9..15, 53) ✅ (2026-07-07) — schema v18 unchanged (no migration this slice)
- **Workspace consolidation:** this session opened in the fresh worktree on `claude/wonderful-hellman-59520a`. The slice-2 UI work sat as UNCOMMITTED WIP in the prior `pensive-hellman-5627d3` tree; **A12 committed it as `753597a`** ("wip(inventory): Phase 6 slice 2 UI checkpoint — BOM master + Manufacturing Journal") and **fast-forwarded `wonderful-hellman` onto it** — `claude/wonderful-hellman-59520a` @ `753597a` is now the LIVE branch (parent `6edea1c` = the slice-2 BACKEND: BOM + Manufacturing-Journal engine, which carried SQLite schema → **v18**; v17 `MigrateV16ToV17` = `bill_of_materials`+`bom_lines`, v18 `MigrateV17ToV18` = two additive flags `voucher_types.use_as_manufacturing_journal` + `stock_items.set_components`).
- **Slice-2 UI finalized (BOM master + Manufacturing Journal):** BOM master (Masters → Inventory; finished-good → component lines w/ qty + optional rate/percent apportionment, per-item-unique name, `IMasterListExportSource`) + **Manufacturing Journal** voucher (Stock-Journal-derived, F11-gated; consumes components → produces FG, apportions additional/FG cost) wired into the cascade Miller-column UI, keyboard-first, figures from the engine (ER-4/ER-12).
- **Scratch cleanup:** deleted the throwaway render probe `tests/Apex.Desktop.Tests/ZZRenderProbe.cs` and reverted `tests/Apex.Desktop.Tests/TestAppBuilder.cs` to its committed form (headless-render scaffolding backed out — no stray probes left in shipped tests).
- **A10 adversarial review (4 lenses) caught + fixed 3 REAL defects a green suite hid — 2 HIGH + 1 MED, all regression-locked:**
  - **(HIGH, financial) percent-basis carve-out ₹0.01 paisa leak** — apportioning additional/FG cost on a PERCENTAGE basis left the rounding remainder unassigned, so the manufacture didn't foot to the paisa → fixed via a **generalized `ConservedInwardLines` remainder-correction** (`ManufacturingJournalService`) that drives the leftover paisa onto the carve-out lines so Σ inward value == total exactly.
  - **(HIGH, valuation) non-batch FIFO/LIFO component consumption valued FG at AVERAGE not LAYER cost** — a Manufacturing Journal issuing FIFO/LIFO (non-batch) components valued the consumed components at average cost, mis-stating FG cost and leaving PHANTOM stock value on hand → fixed via **`StockValuationService.IssueValue`** (layer-accurate issue value; qty & value kept in lock-step).
  - **(MED, UX) Alt+C on a BOM component field misfired to the accounting Ledger master** — the inline-create shortcut opened the wrong master → fixed so **Alt+C inline-creates a COMPONENT stock item (RQ-53)** on BOM screens.
  - + regression tests locking BOTH valuation fixes (carve-out paisa conservation + FIFO/LIFO layer issue value).
- **Gate (orchestrator-re-run):** build 0/0; `dotnet test -c Release` = **1082 passed / 0 failed** (Apex.Ledger.Io 134 · Ledger 481 · Sqlite 71 · Desktop 396). **Schema v18 — no migration this slice.** Robert & Bright green (ER-13).
- **Pending A12:** the 3 A10 fixes + scratch cleanup are currently applied to the working tree, verified green, and awaiting A12's commit + push (R4) — a new-session resume must re-commit these before continuing.
- **Next:** Phase 6 slice 3 — Additional Cost of Purchase. [A5]

### Phase 6 slice 3 — Additional Cost of Purchase (RQ-16..20; PR-5) ✅ (2026-07-07) — SQLite schema v18→v19
- **What was built:** the single, pure, deterministic, paisa-exact **`AdditionalCostApportionment`** engine that spreads
  an additional-cost pool (Freight, Packing, Loading, …) across item lines and raises each line's **landed** (effective)
  stock rate — the SAME engine feeds the Desktop screen and the valuation, so the displayed landed rate == the
  posted/reported rate (ER-4). Two entry points: **`ForPurchase`** (Purchase item-invoice: sweeps the voucher's Dr
  entry-lines whose ledger carries a `MethodOfAppropriation`, but ONLY when the voucher type is a Purchase with
  `TrackAdditionalCosts` on) and **`ForTransfer`** (Stock-Journal transfer: apportions `InventoryVoucher.AdditionalCostLines`
  across destination allocations, base-unit-normalised). New domain types: **`AdditionalCostLine`**,
  **`MethodOfAppropriation`** (ByQuantity=0 / ByValue=1); `Ledger.MethodOfAppropriation` (nullable) +
  `VoucherType.TrackAdditionalCosts` flag; `InventoryVoucher.AdditionalCostLines`. UI: **`AdditionalCostRowViewModel`** +
  wiring into the inventory-voucher entry cascade (Miller-column, keyboard-first; figures from the engine).
- **Apportionment method (DP-2):** **By Quantity** → weight = base-unit qty (flat ₹/unit, spread evenly); **By Value** →
  weight = line purchase value (qty×rate; dearer lines absorb more). Shares via a deterministic **largest-remainder** rule
  (`Allocate`): floor each proportional paisa share, hand leftover paisa one-at-a-time to the largest fractional remainder,
  ties broken by ascending index → Σ(shares) == pool **exactly**, no paisa lost/invented. **Landed unit rate** stays an
  exact decimal (LandedValue ÷ Quantity); the valuation snaps to paisa only on aggregation.
- **RQ-19 fidelity trap (locked by test):** a plain Direct-Expenses ledger with NO `MethodOfAppropriation` is never swept
  into either pool — it stays purely P&L and never touches a stock rate, even on a Purchase whose voucher type has
  `TrackAdditionalCosts` on. The discriminator is the ledger's method + the tracking flag, not the ledger itself.
- **PR-5 money-conservation guard (ForTransfer):** if an Appropriate-by-Value pool is positive but EVERY destination is
  rateless (by-value basis all-zero), the by-value pool falls back to a by-quantity spread rather than silently vanishing
  (a Stock Journal posts to neither stock nor P&L), so Σ(per-line loads) == pool always holds.
- **Schema v18→v19 (`MigrateV18ToV19`):** additive — `voucher_types.track_additional_costs` (0/1 default 0) +
  `ledgers.method_of_appropriation` (nullable INT; 0=ByQuantity/1=ByValue) + new child table **`additional_cost_lines`**
  (id, inventory_voucher_id→inventory_vouchers, line_order, ledger_id→ledgers, amount_paisa) + index
  `ix_additional_cost_lines_voucher`. Round-trip + schema tests added (`AdditionalCostRoundTripTests`,
  `AdditionalCostSchemaTests`); existing round-trip/schema tests updated for the two new columns.
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1107 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 134 · Ledger 493 · Sqlite 75 · Desktop 405). **Schema v19.** Robert & Bright green (ER-13). No
  scratch/probe/ZZ/temp files staged — working tree is the clean Slice-3 set (engine + schema + UI + tests). The pending
  slice-2 A10 fixes + scratch cleanup carried forward in this same tree and are captured in the slice-3 code commit.
- **Committed & pushed by A12 (R4):** two commits — (a) code+tests
  `feat(inventory): Phase 6 slice 3 — Additional Cost of Purchase (apportionment by qty/value, landed stock rate), SQLite schema v19`;
  (b) docs `docs(memory): Phase 6 slice 3 log`.
- **Next:** Phase 6 slice 4 (Price Levels / Price Lists, per plan.md). [A5]

### Phase 6 slice 4 — Zero-valued transactions + separate Actual-vs-Billed quantity (RQ-21..RQ-25; DP-7) ✅ (2026-07-07) — SQLite schema v19→v20
- **What was built:** two related item-invoice fidelity features (Book pp.142–147; catalog §11).
  **(1) Zero-valued transactions** — a per-type **`VoucherType.AllowZeroValuedTransactions`** flag (Sales/Purchase
  only). When on, a ₹0 free-goods item line (Rate/Value = ₹0) is accepted: it moves stock (Actual qty) but posts ₹0
  to accounts and ₹0 to GST. `VoucherInventoryLine` no longer unconditionally forbids a zero rate (rejects only a
  **negative** rate now); `VoucherValidator` decides permission against the flag and additionally rejects the flag on
  any non-Purchase/Sales base type (a Journal/Stock-Journal can never carry it).
  **(2) Separate Actual & Billed quantity** — a company F11 toggle **`Company.UseSeparateActualBilledQuantity`**
  (a pure persisted user toggle — DP-7, cannot be inferred). New **`VoucherInventoryLine.BilledQuantity`** (defaults
  to `Quantity`/Actual ⇒ feature-off byte-identical, ER-13). **Actual** drives on-hand stock; **Billed** drives
  accounts+GST value. `Value` now = **Billed × Rate** (NOT Actual × Rate) — a zero-valued line contributes ₹0; a
  short-billed line (recv 60/bill 50) posts less; Billed **>** Actual is allowed (RQ-25) — no ordering constraint.
- **Valuation bridge (ER-4):** new `VoucherInventoryLine.StockValuationUnitRate = Value ÷ Quantity` (billed value
  spread over Actual units). `ItemInvoiceStock` moves stock by **Actual** qty and, when Billed ≠ Actual (incl. zero-
  valued Billed 0), overrides the inward valuation rate with this so free/short-billed goods drag the moving average
  down (RQ-24) and closing stock reconciles to the billed value to the paisa. Composes correctly with slice-3
  additional-cost landed rate (landed load wins; else A/B split; else null ⇒ bare rate, byte-identical).
- **Schema v19→v20 (`MigrateV19ToV20`):** purely additive — `companies.use_separate_actual_billed_qty` (0/1 dflt 0)
  + `voucher_types.allow_zero_valued` (0/1 dflt 0) + four nullable Actual/Billed qty columns, two on each stock-line
  table (`voucher_inventory_lines` + `inventory_allocations`): `actual_qty_micro` / `billed_qty_micro` (NULL ⇒ Billed
  ≡ Actual ⇒ feature-off round-trips byte-identically). `rate_paisa` doc relaxed to allow 0 for a zero-valued line.
  No row-rewriting ALTER, no data loss (ER-13). New tests: `ActualBilledZeroValuedTests` (engine),
  `ActualBilledSchemaTests` (schema/round-trip), `ActualBilledVoucherEntryViewModelTests` (UI); existing item-invoice
  + additional-cost/BOM schema tests updated for the new columns.
- **UI:** `InventoryVoucherLineViewModel` + `VoucherEntryViewModel` gain the Actual/Billed columns (shown only when
  the company toggle is on; Miller-column cascade, keyboard-first) and the zero-valued-rate path; `MainWindow.axaml`
  wired for the extra columns.
- **PR-6 worked example (locked by test):** Purchase item-invoice, "Use separate Actual & Billed" on — receive 60
  units, bill 50 @ ₹100 ⇒ stock on-hand +60, accounts/purchase leg = ₹5,000 (50×100), stock valuation inward rate =
  5000÷60 = ₹83.333… (exact decimal; snaps to paisa only on aggregation), pairing invariant balances ₹5,000 vs
  ₹5,000. Separately, a zero-valued line (10 units @ ₹0 on a zero-valued-enabled Sales type) ⇒ +10 stock moved, ₹0
  posted, moving average dragged down (RQ-24); the same ₹0 line on a normal type is rejected.
- **A10 adversarial review:** confirmed the surgical ER-7 relaxation is scoped to zero-valued-enabled types only
  (a normal invoice still rejects a fat-finger ₹0 line, a positive-value line is untouched) and the A/B split
  survives `WithDirection` stamping (which runs before validation/valuation). No new critical bugs unfixed at gate.
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1130 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 134 · Ledger 503 · Sqlite 78 · Desktop 415). **Schema v20.** Robert & Bright green (ER-13). No
  scratch/probe/ZZ/temp files staged — clean Slice-4 set (engine + schema + UI + tests, incl. 3 new test files).
- **Committed & pushed by A12 (R4):** two commits — (a) code+tests
  `feat(inventory): Phase 6 slice 4 — zero-valued transactions + separate Actual/Billed quantity, SQLite schema v20`;
  (b) docs `docs(memory): Phase 6 slice 4 log`.
- **Next:** Phase 6 slice 5 (Price Levels / Price Lists, per plan.md). [A5]

### Phase 6 slice 5 — Price Levels & Price Lists (RQ-26..RQ-31, DP-A; PR-7) ✅ (2026-07-08) — SQLite schema v20→v21
- **Finalized from the paused WIP `8e1c22e`** (engine+schema+UI+tests were present but NOT reviewed/gated). This
  session A12 re-ran the gate, folded in A10's review fixes (with regression tests), and landed a CLEAN checkpoint on
  top of the WIP — the WIP was never reset/force-pushed.
- **What the feature is (Tally "Price Levels / Price Lists", Book p.34):** named **Price Levels** (e.g. Retail,
  Wholesale) each carrying dated, quantity-**slab** Price Lists per stock item. On a Sales item-invoice line the app
  **auto-fills** the Rate (and optional Discount%) from the resolved slab — a pure UI convenience default the operator
  can always override; it **never** enters posting/valuation.
- **Domain (`src/Apex.Ledger/Domain/`):** `PriceLevel` (id+name), `PriceList` (a dated version = level+item+
  `ApplicableFrom`+ordered slabs; `ResolveSlab(qty)`), `PriceListSlab` (half-open band **From≥ / To<**, `Rate`,
  `DiscountPercent`, deterministic `EffectiveUnitRate = Rate×(1−Disc/100)` paisa-rounded). `Company.cs` gains the
  Price* collections + `PriceListsFor(level,item)` + party default via `DomainLedger.DefaultPriceLevelId` (RQ-30).
- **`PriceResolver` (pure, zero posting coupling):** given (company, level, item, qty, voucherDate) → picks the
  **latest `ApplicableFrom ≤ voucher date`** version (RQ-29, RateInForce pattern; RQ-27 strict-increasing guard makes
  ties impossible), then the slab whose half-open band holds the qty (RQ-28), returning `ResolvedPrice` or `null`
  (auto-fill leaves the line blank). Consumed ONLY by the ViewModel auto-fill + `PriceListReport` — never by
  `InventoryPostingService`/`VoucherValidator`/`StockValuationService`/`ItemInvoiceStock`, so all posting/valuation
  invariants are untouched. Integer-scale, culture-invariant, no float, no clock (ER-10).
- **PR-7 worked example (locked by test):** Retail slabs **0–2 → ₹16,000 ; 2–4 → ₹14,850** (last slab CLOSED). qty 3
  → ₹14,850 (headline); boundary **qty 2 → the HIGHER 2–4 slab** (From≥); qty 4 → **null** (falls in no slab).
  Open-ended top slab (Wholesale 10–null → ₹900) resolves any large qty. On the Sales line qty 3 auto-fills
  14,850.00 and posts 3 × 14,850 = ₹44,550.
- **Schema v20→v21 (`MigrateV20ToV21`):** purely additive — new `price_levels`, `price_lists`, `price_list_slabs`
  tables + `ledgers.default_price_level_id` (nullable). Feature-off round-trips byte-identically; lossless
  JSON/XML + SQLite round-trip proven (`PriceListRoundTripTests`, `PriceListSchemaTests`).
- **UI (Miller-column cascade, keyboard-first):** `PriceLevelsViewModel` (create/list levels) + `PriceListsViewModel`
  (pick level+item, dated `ApplicableFrom`, editable slab grid, Save = add/revise a dated version) wired into
  `MainWindowViewModel` (new `Screen.PriceLevelsMaster`/`PriceListsMaster`) + `MainWindow.axaml`. Sales
  `VoucherEntryViewModel` gains the Price-Level header selector (defaults from the party's default level, overridable)
  + per-line auto-fill (`ApplyPriceAutoFill` writes only non-user-dirty fields).
- **A10 adversarial fixes (this session, each regression-locked):** (1) **stale-rate leak** — switching an
  un-dirtied line to an item with no price list left the prior item's auto-filled Rate lingering; now a no-slab miss
  clears the auto-fill (empty Rate/Discount) while the operator's own edits still stick
  (`Switching_line_to_item_without_price_list_clears_the_stale_rate`). (2) **party-switch header inheritance** —
  selecting a party with NO default level silently kept the previous party's level; now the header always resets to
  the new party's default, falling back to "Not Applicable" (RQ-30;
  `Selecting_party_without_default_level_resets_header_to_not_applicable`). (3) **Ctrl+A keyboard route** — wired
  `Screen.PriceLevelsMaster`→`Create()` and `PriceListsMaster`→`Save()` into `ActivateSelected` so Ctrl+A creates a
  level / saves a dated list (`Ctrl_a_creates_price_level_and_saves_price_list_via_activate_selected`).
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1162 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 134 · Ledger 515 · Sqlite 83 · Desktop 430). **Schema v21.** Robert & Bright green. No
  scratch/probe/ZZ/temp files staged — clean finalize set (2 VM fixes + 3 new regression tests on top of the WIP).
- **Committed & pushed by A12 (R4):** finalize commit `feat(inventory): Phase 6 slice 5 finalize — Price Levels &
  Price Lists green + A10 fixes (schema v21)` + docs `docs(memory): Phase 6 slice 5 log`. Branch pushed; **`main`
  NOT touched** (the ff-merge of Phases 3–6 onto `main` is a separate follow-up step).
- **Next:** Phase 6 slice 6 (Reorder level + stock-status report, schema v22 per plan.md). [A5]

### Phase 6 slice 6 — Reorder Levels + Reorder Status (RQ-32..RQ-37; PR-8) ✅ (2026-07-08) — SQLite schema v21→v22
- **What the feature is (Tally "Reorder Levels" + the Reorder Status report, Book pp.158–162):** a proper
  **Reorder Level master** replacing the Phase-3 per-item-only fields. A `ReorderDefinition` is attachable per
  **Item / Group / Category** (RQ-32), each carrying two independent figures — the **reorder level** and the
  **minimum order quantity** — and each figure is independently **Simple** (a fixed typed qty; Alt+S / Alt+V) or
  **Advanced** (the fixed figure reconciled Higher/Lower against the item's **consumption over a rolling period**,
  RQ-33/34/35). A single shared PeriodCount/PeriodUnit + Criteria triple governs both Advanced figures (DD-1).
- **Domain (`src/Apex.Ledger/Domain/`, all pure — no engine/DB/clock):** `ReorderDefinition` (Scope + TargetId +
  the two Simple/Advanced flags + fixed qtys + shared period/criteria; validates qty ≥ 0 to micro precision;
  `WindowStart(asOf)` = leap-safe calendar arithmetic, half-open `(WindowStart, asOf]`), plus enums
  `ReorderScope` (Item/Group/Category) and `ReorderCriteria` (Higher/Lower). `Company.cs` gains the
  `ReorderDefinitions` collection + `FindReorderDefinition(scope,targetId)`.
- **`ReorderStatus` report (`src/Apex.Ledger/Reports/ReorderStatus.cs`, pure projection; ER-5 one engine):**
  resolves each item's **effective** definition by specificity (RQ-36) — Item wins, else nearest ancestor **Group**
  (walk up to Primary), else nearest ancestor **Category** (Group beats Category, DD-2), else the **legacy per-item
  `StockItem.ReorderLevel`/`MinimumOrderQuantity`** (backward-compat, ER-13), else the item is excluded. Advanced
  figures pull `InventoryLedger.Consumption(item, WindowStart, asOf)` (new engine method this slice) and reconcile
  max/min against the fixed baseline. **Order to be Placed (RQ-37)** = `netShortfall = max(level−closing,0) −
  pendingPOs`, then `max(netShortfall, MinOrderQty)` — bounded **below** by the MOQ **and** net of pending purchase
  orders — dropping to 0 only when incoming POs actually cover the shortfall. Sales Orders Due shown for context
  but **not** netted (DD-4). PO/SO counting reuses the Order Register's exact predicate (cancelled/post-dated
  excluded, ER-4; partially-received PO still counts full qty — DD-5, identical to the register).
- **PR-8 exit gate (locked by test, Book pp.159–161):** Reorder Level **20** (Simple), MOQ **25** (Simple); stock
  sold below 20 with **NO** pending PO ⇒ **Order to be Placed = 25** (the MOQ floor, ER-13/Phase-3 parity — NOT
  the smaller raw shortfall, and NOT zero at closing==level). A pending PO that covers the shortfall pulls the
  order to 0; SOs Due never change the order qty (DD-4). Advanced rollup, group/category specificity, and
  legacy-fallback all regression-locked in `ReorderLevelsTests` + `InventoryReportsTests` (Slice-6 block).
- **Schema v21→v22 (`MigrateV21ToV22`):** purely additive — new `reorder_definitions` table (scope + target +
  the two flags + fixed qtys + shared period_count/period_unit/criteria). Feature-off round-trips byte-identically;
  lossless SQLite + JSON/XML round-trip proven (`ReorderRoundTripTests`, `ReorderSchemaTests`).
- **UI (Miller-column cascade, keyboard-first):** `ReorderLevelsViewModel` (pick scope + target, set Simple/Advanced
  reorder level & MOQ, shared period/criteria, Save) wired into `MainWindowViewModel` + `MainWindow.axaml`; the
  Reorder Status report screen surfaces Closing / Reorder Level / MOQ / Pending POs / SOs Due / Shortfall / Order
  to be Placed via `ReportsViewModel` + `ReportTabularProjector`.
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1204 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 134 · Ledger 539 · Sqlite 88 · Desktop 443). **Schema v22.** Robert & Bright green. No
  scratch/probe/ZZ/temp files — clean Slice-6 set (9 new files + 16 modified, all Reorder/schema/UI/tests).
- **Committed & pushed by A12 (R4):** `feat(inventory): Phase 6 slice 6 — Reorder Levels + Reorder Status (MOQ +
  net-of-pending-PO bound), SQLite schema v22` + `docs(memory): Phase 6 slice 6 log`. Branch pushed; **`main` NOT
  touched** (the origin/main ff-merge of Phases 3–6 remains a separate blocked decision).
- **Next:** Phase 6 slice 7 — POS (multi-tender / Point-of-Sale invoice), schema v23 per plan.md. [A5]

### Phase 6 slice 7 — POS single/multi-tender invoicing (RQ-38..RQ-44; PR-9; TOP RISK #6) ✅ (2026-07-08) — SQLite schema v22→v23
- **What shipped (catalog §11 POS voucher):** a **POS-flagged Sales voucher type** billed through a retail till with
  **single- and multi-tender** payment and an **Alt+I** toggle between the two modes. GST reuses the Phase-4 engine
  unchanged; the POS layer sits on top of the ordinary item-invoice Sales voucher.
- **Engine (TDD, `PosTenderService` + domain):** four tender types (`PosTenderType` = Cash / Card / Cheque /
  GiftVoucher). **Cash residual** auto-fills as `billTotal − Σ(non-cash tenders)`; **Change** = `cashTendered −
  cashPayable(residual)`. Load-bearing **tender-ledger GROUPING** (DP-4): Gift → **Sundry Debtors**, Card/Cheque →
  **Bank**, Cash → **Cash-in-Hand**. Reconciliation invariant **Σ tenders == bill total** enforced in
  `VoucherValidator` (+ over-tender and short-tender rejection). `PosConfig`/`PosTender` domain, `PosRegister` report,
  and a `PosReceiptPdf`/`PosReceiptData` till receipt in `Apex.Ledger.Io`.
- **PR-9 exit gate (hard gate, TOP RISK #6 — regression-locked in `PosTenderTests`):** bill **taxable ₹10,225 @ 18%
  intra ⇒ CGST 920.25 + SGST 920.25, total ₹12,065.50**. **Multi-tender** = Gift ₹500 (→Sundry Debtors) + Card ₹5,000
  (→Bank) + Cheque ₹5,000 (→Bank) + **Cash residual ₹1,565.50** (→Cash-in-Hand); cash **tendered ₹1,600 ⇒ change
  ₹34.50**. Proven that **single-tender AND multi-tender both foot to ₹12,065.50 with identical Sales+GST credits**,
  change ₹34.50 both ways, and **cash posts the RESIDUAL, not the tendered** amount. Over-tender (non-cash Σ > total ⇒
  negative residual) and cash-short-of-payable both rejected. Alt+I toggles both directions.
- **Schema v22→v23 (`MigrateV22ToV23`, wired `SqliteCompanyStore.cs`):** purely additive — `pos_voucher_type_config`
  (one retail-till config row per POS-flagged Sales type), `pos_tender_ledger_defaults` (the DP-4 tender-ledger class
  map, up to 4 rows per type), `pos_tender_allocations` (the per-voucher tender rows the balanced entry lines can't
  carry) + index `ix_pos_tender_allocations_voucher`. Feature-off round-trips byte-identically; lossless SQLite
  round-trip proven (`PosSchemaTests`, `PosRoundTripTests`); pre-POS round-trip tests re-asserted (v23-stamped).
- **UI (Miller-column cascade, keyboard-first):** `PosBillingViewModel` (POS billing screen, tender rows, Alt+I
  single/multi toggle, live residual/change) wired into `MainWindowViewModel` + `MainWindow.axaml`; POS register/receipt
  surfaced via `ReportsViewModel` + `PrintPreviewViewModel`.
- **A10 adversarial review:** no surviving HIGH/MED defects on the Slice-7 set at gate time (engine invariants +
  grouping + Alt+I round-trip all held under adversarial probing; POS invariant tests fail-on-pre-fix).
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1241 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 139 · Ledger 556 · Sqlite 93 · Desktop 453). **Schema v23.** Robert & Bright + GST golden green. No
  scratch/probe/ZZ/temp files — clean Slice-7 set (13 new files + 16 modified, all POS/schema/UI/tests).
- **Committed & pushed by A12 (R4):** `feat(inventory): Phase 6 slice 7 — POS single/multi-tender invoicing (Alt+I),
  SQLite schema v23` + `docs(memory): Phase 6 slice 7 log`. Branch pushed; **`main` NOT touched** (the origin/main PR
  #18 ff-merge of Phases 3–6 remains a separate blocked decision).
- **Next:** Phase 6 slice 8 — Job Work (Material Out/In, third-party godown, Allow Consumption), schema v24 per
  plan.md. [A5]

### Phase 6 slice 8 — Job Work (In/Out orders + Material In/Out + third-party godowns) (RQ-45..RQ-51; PR-10; ER-1/ER-5/ER-7/ER-13) ✅ (2026-07-08) — SQLite schema v23→v24
- **What shipped (catalog Job Work; Book1 p.83/p.90):** F11 **"Enable Job Order Processing"** activates four voucher
  types — **Job Work In/Out Order** (pure order docs) + **Material In/Out** (the physical moves) — plus the two type
  flags **"Use for Job Work"** and **"Allow Consumption"**. A **Job Work Order moves neither accounts nor stock** (it is
  a commitment doc). **Material Out** is a value-neutral **balanced transfer** that keeps stock on OUR books at a
  **third-party godown** (a location move, not a disposal, RQ-46). **Material In with Allow Consumption** is a transform
  that **consumes the third-party components leaving no phantom RM** and **produces a valued FG from LIVE component cost**
  (not the supplied/order rate, RQ-49). The **SAME four types serve both principal (Out) and worker (In) sides with no
  hard-coded branch** — direction is carried per component line by `JobWorkComponentTrack` (PendingToIssue /
  PendingToReceive), not baked into a posting rule.
- **Engine (TDD, `JobWorkService` + domain):** `JobWorkDirection` (In=0/Out=1), `JobWorkOrder` + `JobWorkOrderLine`
  (tracked component lines with godown), `JobWorkComponentTrack`. Invariants enforced: unbalanced Material Out rejected;
  consuming material the worker site never received is **rejected with rollback, no phantom** ("negative" on-hand guard);
  an Out-order book does **not** double-count consumption as a second issue; a Job Work Order type **rejects a stock-
  movement payload** and an Out-order payload filed under the In type is rejected. `JobWorkReports` renders four
  registers over the fixture.
- **PR-10 exit gate (regression-locked in `JobWorkPostingTests`):** Out Order (order 10 FG) → Material Out dispatches
  components to a third-party "Worker Site" godown (value-neutral, stock stays on our books) → **Material In consumes**
  the components (every component back to **0 at Worker Site — no phantom RM**) and **produces FG +10 at Main valued Σ
  consumed component cost paisa-exact (₹140,000)**, with **source qty ≠ dest qty** and **accounts untouched** (the job-
  charge invoice rides the separate accounting path). Value proven to come from **live component cost, not the supplied
  rate**; a diverging supplied rate leaves the Material Out transfer value-neutral.
- **Schema v23→v24 (`MigrateV23ToV24`, wired `SqliteCompanyStore.cs`):** purely additive — three new tables
  (`job_work_orders`, `job_work_order_lines`, `material_order_links`) + their indexes (incl. UNIQUE 1:1
  `ux_job_work_orders_voucher`), plus additive columns (`companies.job_order_processing_enabled`,
  `voucher_types.use_for_job_work`, `voucher_types.allow_consumption`). No rewriting `ALTER`. Fresh DB stamped straight
  to v24 via `CreateV1`. Lossless SQLite round-trip proven (`JobWorkSchemaTests`, `JobWorkRoundTripTests`); pre-Job-Work
  round-trip tests re-asserted (v24-stamped, feature-off byte-identical).
- **UI (Miller-column cascade, keyboard-first):** `JobWorkOrderEntryViewModel` + `JobWorkComponentLineViewModel` +
  `MaterialMovementEntryViewModel` wired into `MainWindowViewModel` + `MainWindow.axaml`; Job Work registers surfaced via
  `ReportsViewModel`.
- **A10 adversarial review:** no surviving HIGH/MED defects on the Slice-8 set at gate time (transfer value-neutrality,
  no-phantom consumption guard, live-cost FG valuation, and type/payload rejections all held under adversarial probing;
  invariant tests fail-on-pre-fix).
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1265 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 139 · Ledger 569 · Sqlite 97 · Desktop 460). **Schema v24.** Robert & Bright + GST golden + order-
  processing tests green. No scratch/probe/ZZ/temp files — clean Slice-8 set (13 new files + 16 modified, all Job
  Work/schema/UI/tests).
- **Committed & pushed by A12 (R4):** `feat(inventory): Phase 6 slice 8 — Job Work (In/Out orders + Material In/Out +
  third-party godowns), SQLite schema v24` + `docs(memory): Phase 6 slice 8 log`. Branch pushed; **`main` NOT touched**.
- **Next:** Phase 6 slice 9 — exit gate. [A5]

### Phase 6 slice 9a — Io losslessness catch-up: restore lossless JSON/XML round-trip for advanced-inventory masters ✅ (2026-07-08) — no schema change
- **Gap closed:** Slices 5 (Price Levels/Lists), 6 (Reorder definitions), 7 (POS config + per-voucher tenders) and 8
  (Job Work) had never been added to the `Apex.Ledger.Io` canonical model, so they were **silently dropped on JSON/XML
  export→import** — a Phase-5 PR-4 losslessness regression. This slice adds them to the canonical model, mapper, XML
  reader/writer, and apply/import plan so a full advanced-inventory company survives an export→import into a fresh company
  **paisa- and count-exact** on both wire formats.
- **Entities added to the canonical model (`CanonicalModel.cs`):** company-level `PriceLevelDto`, `PriceListDto`
  (+`PriceListSlabDto`), `ReorderDefinitionDto`, and `PosConfigDto` (retail-receipt config +`PosTenderLedgerDefaultDto`);
  voucher-level `PosTenderDto` (multi-tender split: Cash residual/tendered/change, Card no, Cheque/DD bank+no) and
  `JobWorkOrderDto` (+`JobWorkOrderLineDto`: direction In/Out, order no, process/due-date, component track, rates). Mapping
  in `CanonicalMapper.cs`; symmetric XML in `CanonicalXml.cs`; engine-routed apply/dedup in `ApplyJournal.cs` +
  `ImportPlan.cs`.
- **PR-4 re-verified:** new round-trip coverage in `CanonicalRoundTripTests.cs` + `CanonicalFixture.cs` exercises all four
  master/voucher families across JSON **and** XML, asserting paisa- and count-exact reconstruction. **Gate re-run by A12
  (R4), fully green: `dotnet test -c Release` → Ledger 569 · Io 142 (was 139, +3) · Sqlite 97 · Desktop 460 = 1268, 0
  failed.** Only Io source + Io test files changed; no SQLite schema change (still v24); no scratch/probe/temp files.
- **Session-limit note:** the prior working session was interrupted mid-slice at a usage-limit signal, leaving these Io
  edits uncommitted in the worktree; this A12 pass reconciled by re-verifying the full gate from scratch before committing.
- **Committed & pushed by A12 (R4):** `feat(io): Phase 6 — restore lossless JSON/XML round-trip for advanced-inventory
  masters (price levels/lists, reorder, POS, job work)` + `docs(memory): Phase 6 slice 9a — Io losslessness log`. Branch
  pushed; **`main` NOT touched** (PR #18 auto-tracks the branch tip).
- **Next:** Phase 6 slice 9 exit gate remainder — (9b) extend Bright with the full advanced-inventory flow + re-verify
  PR-1..PR-11 + migration v15→v24 + de-brand sweep + run the whole Desktop app; then merge PR #18 → pause for Phase-7. [A5]

### Phase 6 slice 9b — EXIT GATE: Bright advanced-inventory reconciliation + PR-gate re-verify ✅ (2026-07-08) — no schema change
- **Exit-gate regression added:** a new full-set reconciliation test on the rich Bright fixture,
  `Bright_full_advanced_inventory_set_reconciles_into_stock_summary_and_balance_sheet_to_the_paisa`
  (`tests/Apex.Ledger.Io.Tests/CanonicalRoundTripTests.cs`). It drives the FULL Phase-6 advanced-inventory set through
  the reporting engines and asserts three-way paisa-exact consistency: (a) the per-row Stock Summary identity
  `opening + inward − outward == closing` holds for every item and the grand total foots; (b) closing stock is IDENTICAL
  across three independent engines — Stock Summary total, `StockValuationService.TotalClosingStockValue`, and the
  Balance-Sheet `Stock-in-Hand` asset line (`ClosingStockMode.InventoryDerived`); (c) the ONLY Balance-Sheet imbalance is
  the fixture's deliberate ₹55,000 opening-balance gap — every Phase-6 voucher (additional-cost transfer, Actual-vs-Billed
  sale, POS multi-tender, Job Work Material Out) is self-balancing and leaks NOTHING into the statements. Concrete closings
  pinned: Gizmo 167 on-hand (200 opening − 20 transfer − 10 AB sale − 3 POS), JW Raw 300 main + 200 at third-party 'Worker
  Site' godown (Slice-8 Material Out), Assembled Gadget conserved BOM value ₹157.50 (Slice-2).
- **PR-1..PR-11 re-verified:** existing Robert/Bright PR-gate suites plus the sibling balanced-books `BrightReVerificationTests.BR4`
  (Dr = Cr, TotalAssets == TotalLiabilities to the paisa) all stay green; migration chain v15→v24 exercised by the SQLite suite.
- **Gate re-run by A12 (R4), fully green: `dotnet test -c Release` → Ledger 569 · Io 143 (was 142, +1) · Sqlite 97 ·
  Desktop 460 = 1269, 0 failed.** Only the one Io test file changed; no schema change (still v24); de-brand clean; no
  scratch/probe/ZZ/temp files.
- **Committed & pushed by A12 (R4):** `test(inventory): Phase 6 exit gate — Bright advanced-inventory regression + PR-gate
  re-verify` + `docs(memory): Phase 6 slice 9b log`. Branch pushed; **`main` NOT touched** (PR #18 auto-tracks the branch tip).
- **Next:** Phase 6 slice 9 exit-gate remainder — run the whole Desktop app (headless render each Phase-6 cluster as
  evidence, de-branded/no-clipping) → merge PR #18 (CI now green-capable, path fix in) → pause for Phase-7 go-ahead. [A5]

## PHASE 7 — TDS/TCS (2026-07-10 →)

### Phase 7 slice 1 — TDS/TCS masters + F11 config + deductor details + auto-ledgers ✅ (2026-07-10) — SQLite schema v24→v25
- **Scope (masters only, NO tax compute):** the config-driven TDS/TCS master + enable layer, mirroring the Phase-4 GST
  slice. Withholding/collection COMPUTE is Phase 7 slice 2/5. Grounded in `docs/phase7-tds-tcs-*` requirements/plan
  (D1–D7 resolved to recommended defaults @ `8d4aaa7`); every rate/threshold is A14-web-verified for **FY 2025-26
  (AY 2026-27)** and stored as **editable data** (a Finance-Act change is a data edit, not a code change).
- **Engine (framework-/DB-/clock-/RNG-free `Apex.Ledger`):** new domain types — `NatureOfPayment` (TDS §-section
  master: section code, name, with-PAN & no-PAN rates in basis-points, Form-26Q FVU code, single + cumulative
  thresholds, effective-from, isPredefined), `NatureOfGoods` (TCS §206C master: collection code, rates, threshold,
  `baseIncludesGst`, isLegacy + legacyCutoff), `TdsConfig` / `TcsConfig` (company-level deductor config hung off
  `Company`, mirroring `GstConfig`; TAN + deductor type + responsible-person + surcharge/cess seams + periodicity +
  applicable-from + seeded masters; `EnsureValid` fail-fast — TAN required+valid, PAN valid when set), `Pan` / `Tan`
  value validators, `DeductorType`/`DeducteeType`/`CollecteeType`/`TdsTcsPeriodicity`/`TdsTcsLedgerKind` enums.
  **`TdsTcsService`** — idempotent `EnableTds`/`EnableTcs`: validate config → seed the predefined masters (if none) →
  **auto-create the "TDS Payable" / "TCS Payable" liability ledger** under Duties & Taxes tagged `TdsTcsLedgerKind`
  (so `ClassificationRules.IsDutiesAndTaxesLedger` already excludes them from item-invoice pairing, exactly like GST
  tax ledgers); re-enable skips existing masters + ledger (no dupes).
- **Seed (`SeedTdsTcsRates`, A14-verified FY 2025-26):** TDS Nature-of-Payment set — §194A (10%/20%, cum ₹50k),
  §194C (1% Ind/HUF base / 20% no-PAN, single ₹30k + cum ₹1L; the 2% non-Ind/HUF branch deferred to compute),
  §194H (2% w.e.f 01-Oct-2024, cum ₹20k FA2025), §194I(a) plant/machinery 2% + §194I(b) land/building 10%
  (both cum ₹6L/FY FA2025), §194J(a) technical 2% + §194J(b) professional 10% (both cum ₹50k) — **bifurcated per
  Form-26Q section codes** (4IA/4IB, 94J-A/94J-B), §194Q purchase-of-goods 0.1% over ₹50L (no-PAN = **5%** §206AA
  2nd-proviso cap, NOT 20%). TCS Nature-of-Goods (§206C) set — scrap 6CE 1%, timber 6CB/6CC 2%, tendu 6CI 5%, liquor
  6CA 1%, minerals 6CJ 1%, §206C(1F) motor-vehicle 6CL 1% (>₹10L), and §206C(1H) sale-of-goods 6CR 0.1% no-PAN 1%
  (§206CC special cap) — **legacy year-gated, default OFF for dates ≥ 01-Apr-2025 (FA2025)**. §206AB/§206CCA non-filer
  higher rates **omitted** (FA2025). TDS base excludes separately-stated GST (Circular 23/2017); every §206C TCS base
  includes GST (Circular 17/2020).
- **Persistence — SQLite schema v24→v25** (`MigrateV24ToV25`, additive/idempotent/lossless): deductor-config columns
  on the company row + `natures_of_payment` / `natures_of_goods` master tables + the `TdsTcsLedgerKind` tag on the
  auto-created payable ledgers. A company with no TdsConfig (or Enabled=false) is a non-TDS company — every existing
  path byte-for-byte unchanged (ER-13). Schema + round-trip tests added (`TdsTcsSchemaTests`, `TdsTcsRoundTripTests`).
- **Io losslessness:** the TDS/TCS masters + deductor config folded into the `Apex.Ledger.Io` canonical model
  (CanonicalModel/CanonicalMapper/CanonicalXml/ApplyJournal/ImportPlan) so they survive JSON+XML export→import into a
  fresh company paisa- and count-exact (PR-4), guarding against the Phase-6 "silently dropped master" regression.
  Locked by `CanonicalTdsTcsRoundTripTests`.
- **UI (Avalonia, cascade Miller-column, keyboard-first):** F11 "Enable TDS"/"Enable TCS" company-config panels
  (deductor TAN/type/responsible-person/periodicity/applicable-from) via `TdsTcsOptions`; `NatureOfPaymentMasterViewModel`
  + `NatureOfGoodsMasterViewModel` masters (Masters → Statutory, `IMasterListExportSource`); wired into
  MainWindow/GatewayColumn nav. Figures from the engine (ER-4).
- **A10 adversarial review:** clean this slice — no HIGH/MED defects survived to the gate (masters-only slice, no
  money-movement surface; the compute-time rate branches are explicitly deferred to slice 2 and seam-tested).
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1329 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 147 · Ledger 599 · Sqlite 101 · Desktop 482). **Schema v25.** Robert & Bright green + GST golden
  green (ER-13). No known-flaky SQLite isolation failure this run. Working tree = the clean Slice-1 set (engine +
  seed + schema + Io + UI + tests); **no scratch/probe/ZZ/temp files staged**.
- **Committed & pushed by A12 (R4):** two commits — (a) code+tests `feat(tds): Phase 7 slice 1 — TDS/TCS masters, F11
  config, deductor details + duty-ledger auto-create, SQLite schema v25`; (b) docs `docs(memory): Phase 7 slice 1 log`.
  Branch `claude/wonderful-hellman-59520a` pushed; **`main` NOT touched**.
- **Next:** Phase 7 slice 2 — TDS compute (withholding on payment/expense vouchers, section-conditional rate branches,
  threshold accumulation, TDS Payable posting). [A5]

### Phase 7 slice 2 — TDS compute + auto-deduction (carve-out) engine + validator + voucher UI ✅ (2026-07-10) — SQLite schema v25→v26
- **Scope:** the TDS **withholding COMPUTE** layer on top of the slice-1 masters — resolve rate, apply the section
  threshold, round, and book the carve-out. Grounded in `docs/phase7-tds-tcs-*`; every rate/threshold A14-web-verified
  for **FY 2025-26 (AY 2026-27)**, stored as editable data. TCS additive-compute + returns/FVU stay for later slices.
- **Engine (framework-/DB-/clock-/RNG-free `Apex.Ledger`):** new `TdsService` — a pure, deterministic assessment over
  the `Company` aggregate, **withholding not additive** (unlike `GstService`). `ComputeWithholding(assessable, nature,
  deductee, date)` → `Withholding` record: resolves the rate (PAN ⇒ `RateWithPanBp`; no valid PAN ⇒ the §206AA 20%
  general / §194Q 5% special no-PAN rate the seed encodes), tests the section threshold (single-transaction OR
  cumulative-FY), and — when crossed — computes `TDS = round_half_up(assessable × rate / 10000)` to the **nearest
  rupee** (`NearestRupee`, income-tax `MidpointRounding.AwayFromZero`, A14). The **cumulative-FY threshold is a pure
  projection** (`ProjectPriorCumulative` over prior posted `TdsLineTax` per party×nature in the FY — deterministic, no
  clock/order side-effect, exactly like `Gstr1` YTD accumulation). TDS assessed on the **GST-exclusive** base
  (Circular 23/2017). **Carve-out posting:** `Dr Expense/Purchase = GROSS`, `Cr Party = NET` (**derived** GROSS−TDS,
  never gross×(1−rate)), `Cr "TDS Payable" = TDS` ⇒ `GROSS Dr == NET Cr + TDS Cr` to the paisa **by construction** —
  the balance invariant is the guard, a leaky independently-computed net trips `VoucherValidator`. New domain
  `TdsLineTax` value object (immutable, paisa-exact, whole-rupee withheld) rides **one** line per (voucher, party,
  nature) — the TDS-Payable credit when withheld, or the party leg when below-threshold (`TdsAmount`=0) — giving the
  projection exactly one assessable contribution per transaction, like posted `GstLineTax`. `EntryLine` carries an
  optional `TdsLineTax` (mirrors `GstLineTax`).
- **Validator:** `VoucherValidator` documented+verified that the stock-leg pairing sum is unchanged by a withholding
  purchase — Purchases stays the GROSS debit (= item-lines value); the reduced party NET leg and the TDS-Payable credit
  are both outside the stock-leg sum (TDS Payable via `IsDutiesAndTaxesLedger`, exactly like GST tax ledgers) — so the
  pairing foots unchanged and `Σ Dr == Σ Cr` guards `net + withheld == gross`.
- **S1 carry-forwards fixed (from A10 slice-1 review notes):** (1) **§194A threshold ₹50k → ₹10k** — the generic
  (non-bank) SMB cumulative threshold, not the bank/co-op/PO ₹50k. (2) **Payable-ledger relocation** — `TdsTcsService`
  now relocates a pre-existing "TDS/TCS Payable" ledger under **Duties & Taxes** whenever
  `!IsDutiesAndTaxesLedger(existing, _company)` (group-based), not merely when `GroupId == Guid.Empty`; a payable a user
  pre-created under a wrong primary group (e.g. Sundry Creditors) would otherwise be mis-counted in the item-invoice
  pairing and leak the withholding credit. Relocation guarantees the classification holds.
- **Persistence — SQLite schema v25→v26** (`MigrateV25ToV26`, additive/idempotent/lossless): one new child table
  `tds_lines` (one row per TDS-assessed entry line) + `ix_tds_lines_entry_line`; a fresh DB stamped straight to v26 via
  `CreateV1`. A voucher with no TDS carries no `tds_lines` row — every existing path byte-for-byte unchanged (ER-13).
  Schema + round-trip tests updated (`TdsTcsSchemaTests`, `TdsTcsRoundTripTests`, inventory/item-invoice round-trips).
- **Io losslessness:** `tds_lines` folded into the `Apex.Ledger.Io` canonical model (CanonicalModel/CanonicalMapper/
  CanonicalXml/ImportPlan) so withholding detail survives JSON+XML export→import into a fresh company **paisa- and
  count-exact** (PR-4), guarding the Phase-6 "silently dropped" regression. Locked by `CanonicalTdsTcsRoundTripTests`.
- **UI (Avalonia, cascade Miller-column, keyboard-first):** `VoucherEntryViewModel` + MainWindow voucher-entry surface
  gained the TDS carve-out path (nature pick + live withheld figure from the engine, ER-4). Locked by
  `TdsVoucherEntryViewModelTests`.
- **A10 adversarial review:** the carve-out surface is money-movement — the derived-NET-never-gross×(1−rate) rule and
  the balance-invariant-as-guard were the explicit defences; no HIGH/MED defect survived to the gate.
- **Gate (A12 re-ran, tree is authority):** `dotnet test -c Release` = **1350 passed / 0 failed / 0 skipped**
  (Apex.Ledger.Io 147 · Ledger 613 · Sqlite 103 · Desktop 487). **Schema v26.** Robert & Bright green + GST golden
  green (ER-13). No known-flaky SQLite isolation failure this run. Working tree = the clean Slice-2 set (engine +
  `TdsService`/`TdsLineTax` + validator + schema + Io + UI + tests); **no scratch/probe/ZZ/temp files staged**.
- **Note (process):** the prior slice-2 finalize was interrupted after the engine+UI landed but before the commit
  (a `StructuredOutput` interruption left the change set UNCOMMITTED at v26 on HEAD `ffb6b5d`=S1); this run re-verified
  the gate from the working tree and finalized the two commits.
- **Committed & pushed by A12 (R4):** two commits — (a) code+tests `feat(tds): Phase 7 slice 2 — TDS auto-deduction
  (carve-out) engine + validator + voucher UI, SQLite schema v26`; (b) docs `docs(memory): Phase 7 slice 2 log`.
  Branch `claude/wonderful-hellman-59520a` pushed; **`main` NOT touched**.
- **Next:** Phase 7 slice 3 — TCS additive-compute on sale-of-goods vouchers (§206C nature, collectee, threshold,
  TCS Payable posting). [A5]

### ▶▶ NEXT-SESSION START HERE (handoff 2026-07-05, after Phase 5 slice 4)
- **Read first:** `docs/NEXT_SESSION_KICKOFF.md` (the self-contained resume prompt), then the governance files
  `CLAUDE.md` → this `memory.md` (tail) → `plan.md` → `agents.md`, plus `docs/phase5-*-requirements.md` (+ the
  phase3/phase4 requirements docs for context).
- **State:** .NET/Avalonia (C#) desktop Tally-Prime-clone accounting app. Branch `claude/keen-albattani-a09dfd` (the
  SINGLE live workspace now), **schema v15, 989 tests green** (4 test projects: Apex.Ledger.Io 125 · Ledger 433 · Sqlite 59 · Desktop 372), de-branded, working
  tree clean. ✅ **Phases 3 (Inventory) + 4 (GST core) COMPLETE**; ✅ **Phase 5 slice 1 (report config & depth — RQ-1/2/6)
  COMPLETE**, ✅ **Phase 5 slice 2 (report sort & filter — RQ-3) COMPLETE**, ✅ **Phase 5 slice 3 (comparative/columnar —
  RQ-4) COMPLETE**, ✅ **Phase 5 slice 4 (Cash Flow / Funds Flow / Ratio Analysis — RQ-5 pt.1) COMPLETE**, ✅ **Phase 5
  slice 5 (Exception reports — RQ-5 pt.2) COMPLETE → RQ-5 DONE**, ✅ **Phase 5 slice 6 (universal drill-down — RQ-7)
  COMPLETE**, ✅ **Phase 5 slice 7 (Save View — RQ-8; SQLite schema v14) COMPLETE**, ✅ **Phase 5 slice 8 (IO
  foundation — Apex.Ledger.Io + hand-rolled PDF writer + report→PDF print/preview — RQ-9/13) COMPLETE**, ✅ **Phase 5
  slice 9 (voucher + tax-invoice print + F12 print config — RQ-10/11/12) COMPLETE**, ✅ **Phase 5 slice 10 (tabular
  export CSV RFC-4180 + XLSX hand-rolled OPC — RQ-14..18) COMPLETE**, and ✅ **Phase 5 slice 11 (canonical JSON+XML data
  export/import + lossless round-trip — RQ-19/20..24; PR-4 exit gate PASS) COMPLETE**, and ✅ **Phase 5 slice 12 (email
  compose + .eml/mailto hand-off + SMTP profile — RQ-25..27; SQLite schema v15; live send deferred) COMPLETE**, and
  ✅ **Phase 5 slice 13 (final — P/E/M/O bar + report-config shortcuts + Reports nav + master-list export — RQ-28..30)
  COMPLETE**, committed & pushed (no PR yet). **✅ PHASE 5 (reports depth + printing/export/import/email) COMPLETE.**
- **Resume at Phase 6 — Advanced inventory** (batches/expiry, BOM & Manufacturing Journal, additional cost of purchase,
  Price Levels/Lists, Reorder + status report, POS multi-tender, Job Work) — ONLY after the user's go-ahead following a
  live app inspection (Phase 5 is a gate; R9/R12). Then the rest of `plan.md`.
- **THE LOOP TO RUN (user's instruction):** `/loop complete all the phases till they are perfect, and carry out /loop
  for all the phases` — self-pace via the loop and drive Phase 5 + every remaining plan.md phase (6–11) to a perfect,
  gated, adversarially-verified finish.
- **Operating model (CLAUDE.md R1/R2 — do the MAXIMUM work through agentic workflows + subagents; main loop only
  decides/sequences/synthesizes):** per slice — CA/A14 requirements up front → engine TDD → cascade Miller-column UI →
  A10 adversarial review (reproduce bugs with throwaway tests) → fix → full gate green (`dotnet test -c Release`, re-run
  it yourself) → **GitHub Expert (A12) alone** commits+pushes → memory.md log. Verify UI by headless Skia render + Read
  the PNG. Web-verify any tax/law (R7). **Kill any running `Apex.Desktop.exe` before building** (it locks the build).
  Never write "Tally" in shipped app/code. **Checkpoint after every slice; stop at a clean committed checkpoint on any
  usage-limit signal.**
- **Deferred to Phase 9:** RCM, composition, cess, e-invoice/e-way, GSTR-2A/2B, Rule-88A ITC set-off + Alt+J/Ctrl+F
  posting.

## Phase 7 Slice 2 follow-up — expense-ledger-driven TDS detection (2026-07-10)
Corrected the S2 TDS detection contract to match Tally: TDS nature and applicability are now derived from the **debit
(expense) ledger** — the expense ledger carries the TDS nature-of-payment and the applicability flag — while the
**party** ledger drives PAN, deductee type, and the resulting rate. A "Not Applicable" escape is honoured (expense
ledger marked not-applicable → no TDS detection/deduction). Reworked `VoucherEntryViewModel` detection accordingly and
corrected the previously party-driven contract tests in `TdsVoucherEntryViewModelTests` to assert expense-driven
nature+applicability with party-driven PAN/rate. Gate re-verified fully green in Release: Io 147 · Ledger 613 (incl.
Robert/Bright + GST golden) · Sqlite 103 · Desktop 490 = 1353 total, 0 failures. Working tree clean (only the detection
fix + test files). Committed on branch `claude/wonderful-hellman-59520a` (code+tests, then this docs note); pushed to
origin, main untouched.

## Phase 7 Slice 3 — TDS Stat Payment deposit + challan + reconciliation (2026-07-10)
Third TDS slice: deposit the withheld TDS to the government and reconcile it against a challan. **SQLite schema
v26→v27** (new `tds_challans` + challan↔voucher link tables). Delivered:
- **TDS Stat Payment (deposit) voucher** — a statutory-payment Payment voucher (`is_stat_payment` flag **reuses the
  existing Payment base voucher type**, not a new type, per Tally) that Dr's the `TDS/TCS Payable` ledger and Cr's Bank,
  **zeroing the payable** for the deposited dues. `TdsDepositService` picks up the outstanding payable balance and
  builds the balanced deposit legs; `TdsStatPaymentViewModel` drives the UI.
- **Challan ITNS-281** — `TdsChallan` domain type (BSR code, challan serial no, tender date, section/nature, amount
  breakup) generated on deposit; persisted via `SqliteCompanyStore` and linked to the deposit voucher through
  `ChallanVoucherLink`.
- **Reconciliation (Alt+R)** — `ChallanReconciliation` report + `ChallanReconciliationViewModel` match deposited
  vouchers to challans and surface unmatched/partly-matched dues; reached via the **Alt+R** shortcut.
- **Io losslessness** — `tds_challans` (+ links) folded into the `Apex.Ledger.Io` canonical model (`CanonicalModel`,
  `CanonicalMapper`, `CanonicalXml`, `ApplyJournal`, `ImportPlan`) so challans round-trip **paisa- and count-exact**
  through JSON+XML export/import (`CanonicalChallanRoundTripTests`).
No A10 HIGH/MED carve-overs this slice (deposit legs balance gross by construction; payable zeroed exactly). Gate fully
green in Release: **Io 151 · Ledger 620 (incl. Robert/Bright + GST golden) · Sqlite 106 · Desktop 503 = 1380 total, 0
failures.** Working tree clean (only Slice-3 files: TDS deposit/challan/reconciliation engine + VMs + views + schema v27
migration + Io canonical + tests). Committed on branch `claude/wonderful-hellman-59520a` (code+tests, then this docs
note); pushed to origin, main untouched. **Next = S4 (Form 26Q + FVU generation).**
