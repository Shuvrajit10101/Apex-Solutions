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

## Phase 7 Slice 4 — Form 26Q quarterly return + FVU flat-file export + control totals (2026-07-10)
Fourth TDS slice: project the posted TDS data into the statutory **Form 26Q** quarterly return and export the
NSDL/Protean **FVU**-compatible offline upload file. **No schema change — projection-only over the existing v27
tables** (`CurrentVersion` stays 27); nothing was persisted, everything is derived from posted vouchers/TDS lines +
`tds_challans`. Delivered:
- **Form 26Q projection** (`src/Apex.Ledger/Reports/Form26Q.cs`) — a deductor block (TAN, deductor type,
  person-responsible identity denormalised off F11 `TdsConfig`), per-party **deductee-detail rows** read *verbatim*
  off the posted `TdsLineTax` (assessable GST-exclusive base, TDS withheld, applied rate bp, PAN-applied flag,
  §197 hook) so the rows **reconcile to the "TDS/TCS Payable" credit postings for the quarter by construction**, and
  per-challan blocks linking ITNS-281 deposits to the deductions they discharge (FIFO).
- **Quarter attribution by DEDUCTION date, not deposit date** — a March deduction deposited on 7-Apr belongs to Q4
  (the challan is listed under Q4), fixing the **cross-FY attribution** edge (deductions and their late-deposited
  challans land in the same return quarter across the FY boundary).
- **FVU flat-file writer** (`src/Apex.Ledger.Io/FvuWriter.cs`) — caret(`^`)-delimited, one-record-per-line eTDS
  format: File Header (FH) → Batch/deductor Header (BH) → per attributed challan a Challan Detail (CD) + its
  Deductee Detail (DD) records → File Trailer (FT) with control totals. Deterministic + byte-stable like
  `CsvWriter`/`TabularExport` (no clock, no RNG, invariant culture, de-branded ER-11), pinned to `FvuVersion` 8.9.
  DD/challan counts derive from the records **actually written** (undeposited/short-deposited in-quarter deductions
  produce fewer DD lines) so the FH/BH/FT counts always agree with the file body; the gross-deducted-vs-deposited
  reconciliation gap lives in `Form26QControlTotals`. Offline emulation only (no bundled govt FVU/RPU JARs, no
  TRACES/portal upload — decision D4); empty return yields a valid header-only file.
- **UI** — `Form26QViewModel` drives the return preview + FVU export; wired into `GatewayColumn`/`MainWindowViewModel`
  (`MainWindow.axaml`/`.axaml.cs`).
No A10 HIGH/MED carve-overs this slice (deductee rows are verbatim off posted TDS lines and reconcile to the payable
credits by construction; file control totals are counted from the emitted records). Gate fully green in Release:
**Io 157 · Ledger 628 (incl. Robert/Bright + GST golden) · Sqlite 106 · Desktop 511 = 1402 total, 0 failures.**
Working tree clean (only Slice-4 files: Form26Q projection + FvuWriter + Form26QViewModel + Gateway/MainWindow wiring
+ tests; no Schema.cs / migration change). Committed on branch `claude/wonderful-hellman-59520a` (code+tests, then
this docs note); pushed to origin, main untouched. **Next = S5 (TCS compute — additive collect-at-source engine).**

## Phase 7 Slice 8 — TDS/TCS Exception & Outstanding Reports (2026-07-11) — NO schema change (v29)
Eighth (report-side) TDS/TCS slice: the **exception & outstanding reports** — nine **pure report projections** off
already-posted TDS/TCS data, mirroring the existing Outstandings / Negative-Stock exception-report pattern and surfaced
through a façade on `Reports.cs`. **No schema change** (`Schema.cs` stays `CurrentVersion = 29`); nothing new is
persisted — every figure is computed live from the `Company` aggregate. Delivered:
- **Statutory interest math** (`src/Apex.Ledger/Reports/StatutoryInterest.cs`) — shared `StatutoryDueDate` (7th of the
  next month, 30-Apr for a March deduction/collection), `CalendarMonthsSpanned` / `LateMonths`
  (months = (toY*12+toM) − (fromY*12+fromM) + 1, floored at 1; **part-month = full month**), and `LateInterest`
  (interest accrues **from the deduction/collection date** to the deposit/asOf date). The statutory due date **only
  gates whether a deposit is late**; it is not the interest start.
- **Shared FIFO challan coverage** (`src/Apex.Ledger/Reports/TdsCoverage.cs` / `TcsCoverage.cs`) — `CollectUnits` walks
  posted deductions/collections and applies challan deposits **FIFO, period-attributed** (reusing the corrected S4/S6
  Form 26Q/27EQ coverage logic — **NOT** the S3 cash-in-window recon), capping each unit's coverage on
  `DepositDate ≤ asOf`; a **cancelled Stat-Payment voucher drops its challan** (the S3/S6 fix carried in). Feeds both
  the outstanding and the interest reports.
- **TDS exception reports** (`src/Apex.Ledger/Reports/TdsExceptionReports.cs`) — R1 **TDS Outstandings**
  (deducted-not-yet-deposited, per party/section), R2 **TDS Not Deducted** (expense parties that crossed the per-nature
  threshold with no TDS line — gate matches `TdsService.ThresholdCrossed`), R3 **TDS Interest u/s 201(1A)**
  (1.5%/month late-deposit), R4 **TDS Nature-of-Payment summary**.
- **TCS exception reports** (`src/Apex.Ledger/Reports/TcsExceptionReports.cs`) — R5 **TCS Outstandings**, R6 **TCS Not
  Collected** (gate matches `TcsService.ThresholdCrossed`), R7 **TCS Interest u/s 206C(7)** (1%/month), R8 **TCS
  Nature-of-Goods summary** — exact mirrors of R1–R4.
- **Ledgers without PAN** (`src/Apex.Ledger/Reports/LedgersWithoutPan.cs`) — R9 lists deductee/collectee party ledgers
  that still lack a PAN (excludes parties who have **since** added one).
- **UI** — `ReportsViewModel` + `MainWindowViewModel` + `MainWindow.axaml` + `IndianFormat.cs`: 9 new `ReportKind`s
  nested under **Statutory Reports → TDS/TCS Reports** (feature-gated on the tax feature), per-family `DataTemplate`s,
  headless-render verified. `StatutoryReportsViewModelTests` (Desktop) + `TdsTcsExceptionReportsTests` (Ledger).
Web-verified law (R7): **§201(1A)** 1.5%/month for TDS deducted-not-deposited; **§206C(7)** 1%/month for TCS
collected-not-deposited; **part-month = full month**; month count = (toY*12+toM) − (fromY*12+fromM) + 1 floored at 1;
interest runs from the deduction/collection date; the statutory due date (7th of next month, 30-Apr for March) only
gates lateness. **LIMITATION:** the **§201(1A)(i) 1% late-DEDUCTION limb is NOT computable** — the model has no
deductible-date distinct from the voucher date — so it is **omitted with an on-report footnote** (recommended default;
to confirm with user at the gate). The month-counting convention is likewise flagged to confirm with the user.
Adversarial review — **3 lenses**: interest-math (**NO FINDINGS**), coverage-invariant (**1 MED + 2 LOW**),
fidelity-edge (**1 MED + 3 LOW**). Fixes, all regression-locked: **F1 (MED)** R2/R6 threshold/shortfall now match
`TcsService`/`TdsService.ThresholdCrossed` (single-transaction vs cumulative; §206C(1F)/6CL applied per-line **not**
FY-cumulative; §194C both limbs surfaced); **F2 (MED)** test-honesty — Σ R1/R5 ≥ `OutstandingPayable`, added
multi-section over-deposit regressions proving R1/R5 correct while the netted `OutstandingPayable` under-reports;
**F3 (LOW)** stable intrinsic tie-break for report ordering; **F4 (LOW)** R9 excludes parties who since added a PAN;
**F5 (LOW)** grand totals foot to the displayed rounded rows.
**NEW CARRY-FORWARDS (for S9 / user):** (a) the Gateway `OutstandingPayable` "TDS/TCS Payable" balance **nets across
sections to a zero-floor** and **under-reports under multi-section over-deposit** — the per-section R1/R5 outstanding
reports are the correct figures; making the deposit service section-aware is S3-guard blast-radius, **deferred**
(documented in R1/R5 XML-docs). (b) FIFO coverage assumes `company.Vouchers` insertion order == chronological; a
**backdated same-section deduction mis-attributes per-row interest months** (section totals unaffected) — inherited
verbatim from Form 26Q/27EQ; fixing risks FVU byte-drift, documented on `TdsCoverage`/`TcsCoverage.CollectUnits`.
(c) R1/R5 cap coverage on the challan `DepositDate` (legally-correct basis; equals the Stat-Payment voucher date in the
normal flow) — documented, intentional.
Gate fully green in Release: **Ledger 691 (incl. Robert/Bright + GST golden) · Io 181 · Sqlite 111 · Desktop 582 =
1565 total, 0 failures**, 0 warnings, de-branded, no scratch/probe files; **Schema.cs UNCHANGED at v29**. Form 26Q/27EQ
left byte-identical (shared coverage helpers only; no return-path edits). Known-flaky SQLite isolation test passes on
isolated re-run. Committed on branch `claude/recursing-swirles-3138c6` (code+tests, then this docs note); pushed to
origin, main untouched. **Next = S9 (Phase 7 exit gate — golden 194J/TCS worked examples; migration v24→v29; de-brand
sweep; RUN THE APP + headless-render the TDS/TCS screens; audit Windows-only-passing tests for 3-OS CI; resolve
carry-forwards incl. §206C(1F) ex-vs-incl-GST trigger via A14 and the recon-report cash-in-window-vs-period-attributed
question for the user) → then A12 opens/updates a PR and merges branch→main once all 3 OS CI checks are green → PAUSE
for Phase 7→8 go-ahead.**

## Phase 7 Slice 9 — EXIT GATE (2026-07-11) — NO schema change (v29)
Ninth and final Phase-7 slice: the **exit gate** — no new feature, purely verification, hardening and merge-readiness.
No schema change (`Schema.cs` stays `CurrentVersion = 29`); the only production edits are hardening/de-brand fixes.
Delivered:
- **Golden worked examples** (`tests/Apex.Ledger.Tests/Phase7GoldenExamplesTests.cs`) — two hand-derived end-to-end
  oracles that lock the *whole* chain (S2–S7 already lock each stage individually): **TDS 194J** ₹1,00,000 professional
  fee → ₹10,000 withheld → the figure ties across Form 26Q, the FVU flat-file control totals, and the Form 16A
  certificate; **TCS scrap (6CE)** ₹1,00,000 → ₹1,000 collected **additive** (on top) → ties across Form 27EQ, FVU and
  Form 27D. Both were **adversarially verified genuine** (numbers derived by hand, not read back from the engine); no
  engine bug surfaced — the goldens confirm the stage-locks compose correctly.
- **Migration v1→v29 equivalence** (`tests/Apex.Persistence.Sqlite.Tests/SchemaMigrationEquivalenceTests.cs`) — asserts a
  DB migrated all the way from **v1→v29** is structurally identical to a **fresh v29** (order-independent set-compare of
  tables + columns + indexes). Migration v24→v29 (the Phase-7 span) is **SOUND and now COVERED**. The test **CAUGHT a
  real create-vs-migrate divergence**: `ix_gst_rate_slabs_company` was created by `MigrateV12ToV13` but **missing from
  `CreateV1`** → a freshly-created DB lacked an index that a migrated DB had. **FIXED** by adding the index to `CreateV1`
  (proven non-tautological — the test fails on the pre-fix tree). No data-shape change to any Phase-7 table.
- **De-brand sweep CLEAN** — 0 must-fix; no "Tally" in app UI or code.
- **3-OS CI hardening** — the Phase-6 path-separator / byte-stability fix re-confirmed clean. Found a **latent culture
  bug**: `dd-MMM-yyyy` date formatting was culture-sensitive (dormant on en-US CI, would drift on other locales) →
  **hardened the shared `FormatDate` with `InvariantCulture`** (`ReportsViewModel.cs`). `Bank*`/`CsvCanonicalBridge`
  culture-sensitivity is **deferred to Phase 11** (documented, non-blocking, not on the TDS/TCS path).
- **§206C(1F) ₹10L trigger RESOLVED** — carry-forward from S5. **A14 web-verified** the trigger base = **GST-inclusive**
  (dominant/conservative reading of the motor-vehicle >₹10L limb). The code was **already correct** — `TcsService` gates
  on the GST-inclusive assessable value — so **no code change**; only the misleading comment gloss on `TcsService.cs`
  was corrected.
- **RAN THE REAL APP** — launched the Release exe clean (PID alive, no crash), headless-rendered **11 TDS/TCS screens**
  (`s9-*.png`) — all de-branded, no clipping/overlap **except** a genuine **Form 16A/27D certificate-preview overlap**,
  which was **caught + fixed** (`MainWindow.axaml`). Orchestrator visually confirmed the statutory-interest report
  (450 / 600 / 300 = **1,350** ties) and the fixed 16A certificate.
- Doc-comment staleness fixed (`Schema.cs` CreateV1 index doc + `TdsTcsSchemaTests.cs` count).
Gate fully green in Release: **Ledger 693 · Io 181 · Sqlite 112 · Desktop 582 = 1568 total, 0 failures**, de-branded,
no scratch/probe files, **Schema.cs UNCHANGED at v29**.
**Carry-forwards deferred (all documented, non-blocking):** (a) recon reports use **cash-in-window** not
period-attributed — the returns (26Q/27EQ) **and** the S8 outstanding reports **ARE** period-correct; the recon-report
semantics are a **user design call** (surface at Phase-7→8 review). (b) `OutstandingPayable` section-awareness (S3-guard
blast-radius). (c) FIFO backdating mis-attributes per-row interest months (inherited from Form 26Q/27EQ; section totals
unaffected). (d) migration-equivalence could be extended to FKs/CHECK constraints. (e) `Bank*`/`CsvCanonicalBridge`
culture (Phase 11). (f) S5 multi-below-threshold-TCS only first nature persisted. (g) S7 no-PAN placeholder rendered
3 ways + rate-format screen-vs-PDF.
**Merge PENDING:** A12 opened a PR (base `main` ← head `claude/recursing-swirles-3138c6`), **awaiting all 3 OS CI checks
green + user go-ahead to merge**; `main` NOT touched. After merge → **PAUSE for Phase 7→8 go-ahead.** **PHASE 7 COMPLETE
(pending merge).**

### Phase 8 slice 1 — Payroll masters + F11 config (2026-07-11) — schema v30
First Phase-8 (Payroll) slice: the **masters foundation** — the employee/organisation master set plus the F11 gate,
mirroring how each prior module opened (GST/TDS S1). No vouchers or pay computation yet. Delivered:
- **Masters domain** (`src/Apex.Ledger/Domain/`) — **EmployeeGroup** (hierarchical, **parent-cycle-guarded**),
  **EmployeeCategory** (parallel/independent axis), **Employee** (identity + statutory + bank + DOB / PAN / UAN / ESI /
  Aadhaar / PF-a/c + `TaxRegime`), **PayrollUnit** (simple + compound), **AttendanceType** (`AttendanceTypeKind` =
  Attendance / Leave-with-pay / Production / user-defined).
- **`PayrollService`** (`src/Apex.Ledger/Services/PayrollService.cs`) — enable/disable **idempotent**; name
  **uniqueness**; parent-cycle prevention via the shared **HierarchyOrdering**; **delete-if-referenced** guards; PAN / UAN /
  ESI **structural validation**.
- **F11 flags on `Company`** — **Maintain Payroll** + **Enable Payroll Statutory** (`Company.cs`), the master gate for the
  whole module (module hidden/inert when off; turning off never deletes payroll data).
- **Schema v29→v30 — additive** (`Schema.cs` `CurrentVersion = 30`): **5 new tables** (`employee_categories`,
  `employee_groups`, `payroll_units`, `attendance_types`, `employees`) + indexes + **2 company columns**
  (`payroll_enabled`, `payroll_statutory_enabled`), added to **BOTH `CreateV1` AND `MigrateV29ToV30`** (no create-vs-migrate
  drift). **`SchemaMigrationEquivalenceTests` green at v30** (v1→v30 == fresh-v30). `SqliteCompanyStore` persists all five
  masters (`PayrollSchemaTests`, `PayrollRoundTripTests`).
- **Io canonical fold-in** — all masters folded into the `Apex.Ledger.Io` model (`CanonicalModel`/`CanonicalMapper`/
  `CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa- and count-exact JSON+XML lossless
  round-trip**; a payroll-off company **drops nothing** and stays byte-identical (**ER-13**). `CanonicalPayrollRoundTripTests`.
- **UI** — **Payroll Masters** nav (nested, **gated** on Maintain Payroll) + **5 keyboard-first create screens** (Employee
  Category / Group / Employee / Payroll Unit / Attendance Type VMs) + the two **F11 toggles** on the Company Features screen
  (`GstConfigViewModel`/`MainWindowViewModel`/`MainWindow.axaml`/`.axaml.cs`).
- **A10 adversarial review** — 2 lenses **clean**; the 3rd found **3 findings, all fixed** — notably a **MED**: the Employee
  create screen was not capturing all the statutory/bank/DOB/Aadhaar fields the model persists (now complete). The engine
  gate additionally caught + fixed a **downgrade-helper regression**.
Gate fully green in Release: **Ledger 716 · Io 186 · Sqlite 116 · Desktop 600 = 1618 total, 0 failures**, de-branded,
working tree clean (only P8-S1 files: masters domain + `PayrollService` + `Company` F11 flags + Schema/SqliteCompanyStore
v30 + Io canonical + 5 master VMs + Gateway/MainWindow wiring + tests).
**Carry-forwards (documented, non-blocking):** (a) **auto-create payroll payable ledgers** (Salary / PF / ESI / PT / NPS)
on enable is **DEFERRED to P8-S3**, where they are first posted; (b) **UAN(12) / ESI(17)** digit-lengths are
**structural-only** validation — **A14 to web-confirm** the exact statutory formats at a statutory slice.
Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 1 — payroll masters … schema v30` + `docs(memory): Phase 8
slice 1 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to `main` with the Phase-8 PR
at the phase boundary). **Next = P8-S2 (Pay Heads + dated Salary Structures, schema v31).**

### Phase 8 slice 2 — Pay Heads + dated Salary Structures (2026-07-11) — schema v31
Second Phase-8 (Payroll) slice: the **Pay Head masters** plus the **dated Salary Structure** (Salary Details) that binds
pay heads to an employee/group. **Still pure model — no pay computation** (that is P8-S3). Delivered:
- **PayHead domain** (`src/Apex.Ledger/Domain/`) — **5 calculation types** (`PayHeadCalculationType` = **OnAttendance /
  FlatRate / AsComputedValue / OnProduction / AsUserDefinedValue**) + **10 Tally pay-head types** (`PayHeadType`:
  Earnings / Deductions / EmployeesStatutoryDeductions / EmployerStatutoryContributions / Gratuity/ Bonus / LoansAndAdvances /
  ReimbursementToEmployees / NotApplicable etc.). **`AsComputedValue` computation model** = a **basis** (the computed-on
  components) × **slabs** (`PayHeadComputationSlabType` = **percentage-bp** or **flat value**), which expresses e.g.
  40%-of-Basic. **`UnderGroupId`** gives the accounting classification + a **forward-compat nullable `LedgerId`** reserved
  for S3 posting; **rounding method/limit** (`PayHeadRoundingMethod`), **income-tax component** (`IncomeTaxComponent`),
  **use-for-gratuity** flag, **calculation period** (`PayHeadCalculationPeriod`). No computation logic yet (deferred S3).
- **Dated SalaryStructure** (`SalaryStructure.cs`, "Salary Details") per **employee or group** (`SalaryStructureScope`)
  with an **`EffectiveFrom` revision** model: **`InForceOn(date)` = the latest structure ≤ date**. Line-level guards match
  each line's **value vs the pay head's calc type** (a FlatRate line needs a value; an AsComputedValue line must not carry a
  literal, etc.). `SalaryStructureStartType`.
- **`PayHeadService` / `SalaryStructureService`** guards — name uniqueness, delete-if-referenced, and critically the
  **computed-on integrity**: reject **self-reference**, **cycles**, and **dangling references** in the AsComputedValue
  basis graph.
- **Schema v30→v31 — additive** (`Schema.cs` `CurrentVersion = 31`): **5 new tables** (pay heads + their computation
  components + slabs, salary structures + their lines) + indexes, added to **BOTH `CreateV1` AND `MigrateV30ToV31`** (no
  create-vs-migrate drift). **`SchemaMigrationEquivalenceTests` green at v31** (v1→v31 == fresh-v31). `SqliteCompanyStore`
  persists all of it (`PayHeadSchemaTests`, `PayHeadRoundTripTests`).
- **Io canonical fold-in** — pay heads + salary structures folded into the `Apex.Ledger.Io` model (`CanonicalModel`/
  `CanonicalMapper`/`CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa- and count-exact JSON+XML
  lossless round-trip (ER-13)**. `CanonicalPayHeadRoundTripTests`.
- **UI** — **Pay Head** + **Salary Structure** create screens (`PayHeadMasterViewModel` / `SalaryStructureMasterViewModel`
  + Gateway/MainWindow wiring), keyboard-first, gated on Maintain Payroll.
- **A10 adversarial review — caught + fixed a MED**: the **Io import path bypassed** the engine's computed-on
  **cycle / self-reference** and **line-vs-calc-type** guards (import could inject a corrupt pay-head graph the engine would
  never accept). Fix **mirrors those guards into `CompanyImportService`**, **all-or-nothing** (a bad graph rejects the whole
  import, nothing partially applied), **regression-locked** (`CanonicalPayHeadIntegrityImportTests`).
Gate fully green in Release: **Ledger 733 · Io 199 · Sqlite 120 · Desktop 614 = 1666 total, 0 failures**, de-branded,
TestAppBuilder clean, no stray files, working tree exclusively P8-S2. Release build sanity check: **0 warnings, 0 errors**.
**Minor R6 deviation (recorded):** the plan implied one computation table; I **split it into components + slabs** (a pay
head's computed-on basis is a set of components, each carrying an ordered set of slabs) — cleaner normal form, same
behavior.
Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 2 — Pay Heads … schema v31` + `docs(memory): Phase 8 slice 2
log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to `main` with the Phase-8 PR at the
phase boundary). **Next = P8-S3 (attendance + payroll voucher + salary-computation engine + integrated ledger posting +
auto-create payroll payable ledgers [the S1/S2 carry-forward], schema v32).**

### Phase 8 slice 3 — Payroll voucher + salary computation + posting (2026-07-11) — schema v32
Third Phase-8 (Payroll) slice — the one that **makes payroll real money**: the **salary-computation engine**, the
**attendance + payroll vouchers**, and the **integrated balanced accounting posting** that lands payroll on the Balance
Sheet / P&L (BALANCE-SHEET-CRITICAL). **S3 was paused (user request) then REBUILT FRESH from the clean `dcb9623`
checkpoint** — the earlier aborted WIP in the stash was never trusted (it never passed a gate). Delivered:
- **`PayrollComputationService`** (`src/Apex.Ledger/Services/`) — pure, framework/DB/clock-free evaluation of a payslip
  over the **`InForceOn(date)` dated salary structure** (S2) + the **period attendance** (this slice). Handles **all 5
  calc types**: **FlatRate** (structure value); **AsComputedValue** (basis → **marginal-% (bp) / value slabs**, evaluated
  in **dependency order** over the computed-on graph so a head can be computed on already-computed heads); **OnAttendance**
  (**pro-rated by the clipped attendance overlap** — present days ÷ period days, clipped to the payroll period);
  **OnProduction** (production units × rate); **AsUserDefinedValue** (per-voucher entered value). **Per-head rounding**
  (S2 `PayHeadRoundingMethod`). **Critically RESPECTS `AffectsNetSalary`** — a head with `AffectsNetSalary = false` is
  **computed and shown on the payslip but EXCLUDED from net salary and from the posting** (informational heads don't move
  cash). `PayrollComputationTests` incl. the golden payslip.
- **Attendance / production voucher** — `PayrollAttendanceService` + `AttendanceEntry` domain (stored rows: employee,
  attendance/production type, value, period) persisted as data (no ledger effect); the input the OnAttendance / OnProduction
  calc types read.
- **Payroll voucher = integrated BALANCED accounting voucher** — `PayrollVoucherService` posts **Dr earnings** /
  **Cr net Salary-Payable + Cr each deduction payable**; **employer contributions** post a **Dr expense / Cr payable**
  pair (the `employer_expense_ledger_id`). Posting is **atomic** — **pre-validate the whole voucher, then a rollback
  scope** so a mid-post failure leaves nothing partially applied — and **auto-creates the payroll ledgers
  non-destructively** under the pay head's `UnderGroupId` (an existing ledger is reused, never overwritten) — this
  **resolves the S1/S2 auto-payroll-ledger carry-forward**. `PayrollVoucherPostingTests`.
- **New `Payroll` voucher base type** — audited across **all exhaustive switches** in `VoucherEffects` and the reports:
  it **hits P&L + Balance Sheet** and is **correctly skipped by GST / TDS / TCS / stock** (no double-count). The payload
  rides `EntryLine` (`PayrollLineDetail` per posted line) + `PayrollLineCategory`.
- **Schema v31→v32 — additive** (`Schema.cs` `CurrentVersion = 32`): **`employer_expense_ledger_id` column** on the pay
  head + **`attendance_entries`** + **`payroll_lines`** tables, added to **BOTH `CreateV1` AND `MigrateV31ToV32`** (no
  create-vs-migrate drift); **`SchemaMigrationEquivalenceTests` green at v32**; the **downgrade helpers**
  (`InventoryVoucherRoundTripTests` / `ItemInvoiceRoundTripTests` DowngradeTo* DROP the new tables) updated.
  `SqliteCompanyStore` persists it all (`AttendancePayrollSchemaTests`, `PayrollVoucherRoundTripTests`).
- **Io canonical fold-in** — attendance + payroll lines folded into `Apex.Ledger.Io` (`CanonicalModel`/`CanonicalMapper`/
  `CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa- and count-exact JSON+XML lossless
  round-trip** (`CanonicalPayrollVoucherRoundTripTests`), with **import pre-flight reference validation** — a payroll
  voucher referencing a missing employee / pay head / ledger **rejects the whole import, all-or-nothing**
  (`CanonicalPayrollVoucherIntegrityImportTests`).
- **GOLDEN payslip** (hand-derived, regression-locked): **Basic 30k (flat) + HRA 40%-of-Basic = 12k (AsComputedValue) +
  Advance 2k (flat deduction) → gross 42k / net 40k**, posted **Dr 42k == Cr 42k paisa-exact**, plus an **employer PF
  3,600 Dr-expense/Cr-payable pair**. (Statutory-specific PF split / ESI / PT / §192 are S4–S7, so this S3 golden uses
  non-statutory heads.)
- **UI** — `AttendanceVoucherEntryViewModel` + `PayrollVoucherEntryViewModel` + Gateway/MainWindow wiring
  (`MainWindow.axaml`/`.axaml.cs`/`MainWindowViewModel`), keyboard-first, gated on Maintain Payroll.
  `PayrollVoucherViewModelTests`.
- **A10 adversarial review — 3 lenses, posting-balance-weighted** (run **separately after the workflow's review agents hit
  a session limit**): caught + fixed **HIGH — `AffectsNetSalary` was ignored** (non-affecting heads were leaking into net
  + posting); **3 MED** — **non-atomic post** (partial legs on failure → now pre-validate + rollback scope),
  **attendance straddling-entry drop** (an attendance row straddling the period boundary was dropped instead of clipped),
  **import pre-flight gaps** (missing-reference payroll import wasn't rejected); **+ 4 LOW**. All **regression-locked**.
  **Mid-period salary revision** (a structure revision inside a payroll period) is **documented** as computed off the
  single `InForceOn(period-end)` structure (no intra-period proration of the revision) — an accepted, recorded limitation.
Gate fully green in Release: **Ledger 767 · Io 208 · Sqlite 123 · Desktop 627 = 1725 total, 0 failures**, de-branded,
TestAppBuilder clean, no stray files, working tree exclusively P8-S3. Release build sanity check: **0 warnings, 0 errors**.
**Minor R6 deviation (recorded):** `PayrollLineCategory` is a **5-value** enum (earnings / employee-deduction /
employer-contribution / net-payable / informational) — a cleaner split than the plan implied, so the posting side can
route each line to the right Dr/Cr leg; same behavior.
Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 3 — Payroll voucher … schema v32` + `docs(memory): Phase 8
slice 3 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to `main` with the Phase-8 PR
at the phase boundary). The superseded P8-S3 WIP stash was dropped. **Next = P8-S4 (PF — computed EPS/EPF split + EDLI +
admin charges + ECR, schema v33; A14 must web-verify PF admin/EDLI exact figures + the ₹15k ceiling).**

### Phase 8 slice 4 — Provident Fund (EPF/EPS/EDLI/Admin/ECR) (2026-07-12) — schema v33
Fourth Phase-8 (Payroll) slice — the first **statutory** payroll head: **Provident Fund**, computed and posted through
the S3 balanced/atomic voucher. **A14 web-verified against primary EPFO sources** the exact figures, ceilings, and
account splits. Delivered:
- **`PfContribution`** (`src/Apex.Ledger/Services/PfContribution.cs`) — pure, framework/DB/clock-free PF computation over
  **PF wages = Basic + DA** (HRA and other heads **excluded**, driven by a **per-pay-head PF-wage flag** so the wage base
  is data, not hardcoded). Computation order (all **round half-up**): **EE_EPF = round(12% × EPFwages)**;
  **EPS = round(8.33% × min(wages, ₹15,000))** capped **₹1,250**; **ER_EPF = EE_EPF − EPS** — the **anti-3.67%-hardcode
  invariant** `EPS + ER_EPF == EE_EPF` (employer share is EPF-minus-EPS by construction, never a separate 3.67% figure);
  **EDLI (A/c21) = round(0.5% × min(wages, ₹15,000))** capped **₹75**; **A/c2 admin = max(Σ 0.5% × EPFwages, ₹500)** at the
  **ESTABLISHMENT level** (the **₹500 monthly minimum / ₹75 zero-member floor**, computed on the establishment total not
  per-employee); **A/c22 NIL** (subsumed). **₹15,000 statutory ceiling**; the **12%/10% rate toggle** and a
  **cap-wages-at-ceiling** switch are per-company config. `PfComputationTests` incl. the golden.
- **Config surface** — **per-company `PfConfig`** (`src/Apex.Ledger/Domain/PfConfig.cs`: 12/10 rate toggle, establishment
  code, cap-at-ceiling flag) on `Company`; **per-employee PF details** on `Employee` (PF-applicable, higher-wage opt-in,
  PF join date). **`PfStatutoryComponent`** carries the computed A/c-wise breakup.
- **Posting integration into the S3 balanced/atomic voucher** — **employee EPF reduces net** (Cr PF-payable, like any
  employee deduction); **employer EPF / EPS / EDLI / admin** each post a **balanced Dr-expense / Cr-payable pair**; the
  whole PF contribution rides the same **pre-validate → rollback-scope atomic** post and **non-destructive auto-create**
  of the PF payable/expense ledgers from S3. `PfVoucherPostingTests`.
- **ECR 2.0 offline flat-file** — **`EcrWriter`** (`src/Apex.Ledger.Io/EcrWriter.cs`) emits the hand-rolled **`#~#`
  11-field member lines** (`PfEcr` report, `src/Apex.Ledger/Reports/PfEcr.cs`) + the **challan totals A/c1 / 2 / 10 / 21 /
  22**. Deterministic, byte-stable, de-branded, offline emulation only. `EcrWriterTests`.
- **Schema v32→v33 — additive** (`Schema.cs` `CurrentVersion = 33`): PfConfig columns on the company + the PF-wage flag on
  the pay head + per-employee PF columns, added to **BOTH `CreateV1` AND `MigrateV32ToV33`** (no create-vs-migrate drift);
  **`SchemaMigrationEquivalenceTests` green at v33**. `SqliteCompanyStore` persists it all (`PfSchemaTests`).
- **Io canonical fold-in** — PfConfig + per-employee PF + the PF-wage flag folded into `Apex.Ledger.Io`
  (`CanonicalModel`/`CanonicalMapper`/`CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa-exact
  JSON+XML lossless round-trip** (`CanonicalPfRoundTripTests`).
- **GOLDEN** (hand-derived, regression-locked): **EPFwages ₹15,000 → EE_EPF 1,800 / EPS 1,250 / ER_EPF 550 / EDLI 75 /
  admin 500 / A/c22 0**; the posted PF voucher **Dr == Cr == ₹17,375** with **net ₹13,200**; the invariant
  **EPS + ER_EPF == EE_EPF** (1,250 + 550 == 1,800) holds. (Statutory ESI / PT / §192 are S5–S7, so this golden is PF-only.)
- **UI** — F11 payroll config VM gains the PfConfig surface (+ `MainWindow`); **`PfEcrReportViewModel`** drives the ECR /
  challan report pane. `PfConfigReportViewModelTests`.
- **A10 adversarial review — posting-balance-weighted** (3 lenses): caught + fixed **HIGH — the ECR CHALLAN trailer line
  broke EPFO per-line validation** (the challan-totals line, appended after the member lines, failed the portal's
  every-line-is-a-member-record check → now **member-lines-only**, challan totals surfaced separately in the report, not
  in the uploaded file); **MED — the `CapWagesAtCeiling` flag was ignored by the computation** (wages weren't clipped to
  ₹15k when the switch was on → now honoured). Both regression-locked.
Gate fully green in Release: **Ledger 781 · Io 220 · Sqlite 126 · Desktop 634 = 1761 total, 0 failures**, schema v33,
de-branded, TestAppBuilder clean, no stray files, working tree exclusively P8-S4. Release build sanity check: **0 warnings,
0 errors**. Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 4 — Provident Fund … schema v33` +
`docs(memory): Phase 8 slice 4 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to
`main` with the Phase-8 PR at the phase boundary). **Next = P8-S5 (ESI — employee 0.75% + employer 3.25%, ₹21,000 wage
ceiling, Apr–Sep / Oct–Mar contribution periods with benefit-period continuation, monthly ESI return; schema v34; A14 to
web-verify ESI rates / ceiling / period rules).**

### Phase 8 slice 5 — Employees' State Insurance (ESI) (2026-07-12) — schema v34
Fifth Phase-8 (Payroll) slice — the second **statutory** payroll head: **Employees' State Insurance**, computed and posted
through the S3 balanced/atomic voucher. **A14 web-verified against primary ESIC sources** the rates, ceiling, rounding, and
contribution-period rules. Delivered:
- **`EsiContribution`** (`src/Apex.Ledger/Services/EsiContribution.cs`) — pure, framework/DB/clock-free ESI computation.
  Rates: **employee 0.75% + employer 3.25%**, each **CEIL (round UP) to the whole rupee INDEPENDENTLY** — deliberately
  **different from PF's nearest-rupee round-half-up** (ESIC Reg. 40 rounds each contribution up). **Coverage ceiling
  ₹21,000 gross** (**₹25,000 for a person with disability** — now **reachable** via a new `IsPersonWithDisability` flag,
  previously dead/unreachable code); **NO ₹21k cap on the contribution BASE** (once covered, both shares compute on the
  full gross, not on the clipped ceiling). **₹176/day average-daily-wage floor → the employee 0.75% share is WAIVED but
  the employer 3.25% still pays** (ESIC exemption for the low-wage worker). `EsiComputationTests` incl. the golden.
- **Two wage figures (the ESI asymmetry)** — the **coverage test uses gross EXCLUDING overtime**, but the **contribution
  base INCLUDES overtime** (ESIC: OT can't push you *into* coverage, but once covered OT is contributory). **HRA is
  included** in both. Encoded as two distinct wage rollups so OT never leaks into the ₹21k coverage test.
- **Contribution-period CONTINUATION** — the two contribution periods **CP1 Apr–Sep / CP2 Oct–Mar**; **coverage is frozen
  at the START of the contribution period for the whole period** (an IP covered at CP start stays covered every month of
  that CP even if wages later cross ₹21k), and a **mid-CP joiner is frozen at their FIRST payroll** in that CP. Drives the
  benefit-period guarantee ESIC promises the worker.
- **Config surface** — **per-company `EsiConfig`** (`src/Apex.Ledger/Domain/EsiConfig.cs`: applicability, establishment
  ESI code, rates/ceilings as data); **per-employee ESI fields** on `Employee` (ESI-applicable, **10-digit IP number**,
  `IsPersonWithDisability`); the **per-pay-head ESI-wage flags** on `PayHead` (contributory / overtime) so the wage base is
  data, not hardcoded. **`EsiStatutoryComponent`** (`src/Apex.Ledger/Domain/EsiStatutoryComponent.cs`) carries the computed
  employee/employer breakup + the frozen-coverage decision.
- **Posting integration into the S3 balanced/atomic voucher** — **employee ESI reduces net** (Cr ESI-payable, like any
  employee deduction); **employer ESI posts a balanced Dr-expense / Cr-payable pair**; rides the same **pre-validate →
  rollback-scope atomic** post and **non-destructive auto-create** of the ESI payable/expense ledgers from S3 — **no
  voucher-service change**. `EsiVoucherPostingTests`.
- **Monthly per-IP contribution file** — **`EsiContributionWriter`** (`src/Apex.Ledger.Io/EsiContributionWriter.cs`) emits
  the monthly member/IP contribution lines (`EsiContribution` report, `src/Apex.Ledger/Reports/EsiContribution.cs`).
  Deterministic, byte-stable, de-branded, offline emulation only. `EsiContributionWriterTests`.
- **Schema v33→v34 — additive** (`Schema.cs` `CurrentVersion = 34`): EsiConfig columns on the company + the ESI-wage flags
  on the pay head + per-employee ESI columns, added to **BOTH `CreateV1` AND `MigrateV33ToV34`** (no create-vs-migrate
  drift); **`SchemaMigrationEquivalenceTests` green at v34**. `SqliteCompanyStore` persists it all (`EsiSchemaTests`).
- **Io canonical fold-in** — EsiConfig + per-employee ESI + the ESI-wage flags folded into `Apex.Ledger.Io`
  (`CanonicalModel`/`CanonicalMapper`/`CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa-exact
  JSON+XML lossless round-trip** (`CanonicalEsiRoundTripTests`).
- **CORRECTED the S1 IP-length bug** — the **per-employee Insurance Number (IP) is 10 digits, not 17**; **17 is the
  establishment employer code**. Fixed the validation on `Employee` + the F11/Employee master VMs; **3 pre-existing tests
  updated** for the corrected length (`PayrollServiceTests`, `CanonicalPayrollRoundTripTests`, `PayrollRoundTripTests`).
- **GOLDEN** (hand-derived, regression-locked): **gross ₹20,000 → EE 150 / ER 650**, posted ESI voucher **Dr == Cr ==
  ₹20,650** with **net ₹19,850**; **gross ₹17,500 → EE 132 / ER 569** (proves the independent **round-UP** each side —
  0.75%×17,500 = 131.25→132, 3.25%×17,500 = 568.75→569).
- **UI** — F11 payroll config VM gains the EsiConfig surface + the Employee master VM gains the ESI/IP/disability fields
  (+ `MainWindow`); **`EsiContributionReportViewModel`** drives the monthly ESI contribution report pane.
  `EsiConfigReportViewModelTests`.
- **A10 adversarial review — posting-balance-weighted, CLEAN on balance.** ⚠️ the **workflow's review phase died on a
  session limit** (the recurring habit — S3/S4/S8), so I (orchestrator) **ran the 3 A10 lenses SEPARATELY** before A12
  committed. Caught + fixed **2 MED** — (1) the **disability ₹25,000 ceiling was unreachable dead code** (no flag ever set
  it → added `IsPersonWithDisability` so the higher ceiling is actually reachable); (2) **contribution-period continuation
  was broken for a mid-CP joiner** (coverage wasn't frozen at their first payroll → they could flip out of coverage
  mid-CP; now frozen at first payroll). Plus **2 LOW** — the **₹176/day denominator was inflated by overtime** (used the
  contribution base incl. OT instead of the coverage wage → now the OT-excluded wage); a **stale comment**. All
  regression-locked.
Gate fully green in Release: **Ledger 802 · Io 232 · Sqlite 130 · Desktop 642 = 1806 total, 0 failures**, schema v34,
de-branded, TestAppBuilder clean, no stray files, working tree exclusively P8-S5. Release build sanity check: **0 warnings,
0 errors**. Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 5 — Employees' State Insurance … schema v34` +
`docs(memory): Phase 8 slice 5 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to
`main` with the Phase-8 PR at the phase boundary). **Next = P8-S6 (Professional Tax — state-configurable slabs seeded
MH + KA + WB + None, PT deduction + payment; schema v35; A14 to web-verify per-state PT bands + the ₹2,500/yr
constitutional cap).**

### Phase 8 slice 6 — Professional Tax (PT) (2026-07-12) — schema v35
Sixth Phase-8 (Payroll) slice — the **third statutory** payroll head: **Professional Tax**, a **state-levied** tax on
employment, computed and posted through the S3 balanced/atomic voucher. **A14 web-verified the per-state PT bands and the
constitutional cap.** PT differs structurally from PF/ESI: it is **flat-amount-by-wage-band (a slab lookup), not a
percentage**, and the schedule is **state-specific and editable**. Delivered:
- **`ProfessionalTax`** (`src/Apex.Ledger/Services/ProfessionalTax.cs`) — pure, framework/DB/clock-free PT computation. A
  monthly wage falls into exactly one **band** of the active state's slab and pays that band's **flat rupee amount** (not a
  rate). **₹2,500/yr constitutional HARD CAP (Art. 276(2))** on **cumulative FY PT** — the year-to-date PT is **derived
  from posted payroll history** (like the TCS §206C(1H) / GSTR-1 YTD projection), and the current month is clamped so the
  FY total never exceeds ₹2,500. **Per-band February override** — a band may carry a distinct **February amount** (e.g.
  Maharashtra charges ₹300 in Feb vs ₹200 the other months) so the ₹200×11 + ₹300 schedule lands **exactly** on the
  ₹2,500 cap. **Maharashtra gender dimension** — a band may be scoped to a gender (`PtGenderScope`); MH **women are exempt
  at/below ₹25,000** monthly wages. `PtComputationTests` incl. the goldens.
- **State-configurable slab engine, seeded + editable** — **`ProfessionalTaxSlab`** / band rows
  (`src/Apex.Ledger/Domain/ProfessionalTaxSlab.cs`) are **data, not code**: a per-company slab set seeded **MH (men +
  women schedules), KA, WB, and None**, then **editable per company**. **`PtConfig`**
  (`src/Apex.Ledger/Domain/PtConfig.cs`) carries the per-company PT applicability + active state + registration; wage basis
  is data (`PtWageBasis`). The verified seed (band-for-band, A14): **MH men** ≤₹7,500 ₹0 / ₹7,501–10,000 ₹175 / >₹10,000
  ₹200 (₹300 in Feb); **MH women** exempt ≤₹25,000 then the men schedule; **KA** ≤₹24,999 ₹0 / ≥₹25,000 ₹200; **WB**
  banded to ₹110 at ₹15,000; **None** = no PT.
- **`PtStatutoryComponent`** (`src/Apex.Ledger/Domain/PtStatutoryComponent.cs`) carries the computed monthly PT + the
  selected band + the applied cap/Feb-override decision; the **per-pay-head `PtComponent`** role tags the PT deduction head.
- **Posting integration into the S3 balanced/atomic voucher** — PT is a pure **employee deduction**: it **reduces net**
  (Cr **Professional Tax Payable**), **no employer side** (unlike PF/ESI). Rides the same **pre-validate → rollback-scope
  atomic** post and **non-destructive auto-create** of the PT-payable ledger from S3 — **no voucher-service change**.
  `PtVoucherPostingTests`.
- **Register** — **`ProfessionalTaxRegister`** (`src/Apex.Ledger/Reports/ProfessionalTaxRegister.cs`) projects the
  per-employee monthly PT (band, amount, Feb override, YTD-vs-cap) for the state remittance; `ProfessionalTaxRegisterViewModel`
  drives the pane. `PtConfigReportViewModelTests`.
- **Schema v34→v35 — additive** (`Schema.cs` `CurrentVersion = 35`): the **`pt_slab_bands` table** (per-company slab bands)
  + `companies` **pt_* columns** (applicability/state/registration) + **`pay_heads.pt_component`**, added to **BOTH
  `CreateV1` AND `MigrateV34ToV35`** (no create-vs-migrate drift); the **downgrade helpers DROP `pt_slab_bands`**;
  **`SchemaMigrationEquivalenceTests` green at v35**. `SqliteCompanyStore` persists it all (`PtSchemaTests`).
- **Io canonical fold-in** — PtConfig + the slab bands + the pay-head PtComponent folded into `Apex.Ledger.Io`
  (`CanonicalModel`/`CanonicalMapper`/`CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa-exact
  JSON+XML lossless round-trip** with a **full import pre-flight ref guard** (`CanonicalPtRoundTripTests`).
- **GOLDEN** (hand-derived, regression-locked): **MH man ₹12,000 → ₹200/mo + ₹300 Feb = ₹2,500/yr** (lands exactly on the
  Art. 276 cap); **MH woman ₹12,000 → ₹0** (women exempt); **KA ₹30,000 → ₹200/mo**; **WB ₹15,000 → ₹110/mo**.
- **A10 adversarial review — posting-balance CLEAN, seed verified band-for-band.** ⚠️ the **workflow's review phase again
  died on a session limit** (the recurring S3/S4/S5/S8 habit), so I (orchestrator) **ran the 3 A10 lenses SEPARATELY**
  before A12 committed. Fixed **3 LOW**: (1) **band selection now half-up** so the picked band matches the register; (2)
  **`pt_slab_bands` read by `band_order`** (deterministic band sequencing, not insertion order); (3) a **UNIFORM
  role-based statutory-component guard (PF/ESI/PT)** in `PayHeadService` **and** the Io import pre-flight — hardening the
  recurring **Io-import-bypassing-engine-guards** class so a PT/PF/ESI role can't be double-assigned or imported past the
  engine invariant.
Gate fully green in Release: **Ledger 831 · Io 239 · Sqlite 134 · Desktop 649 = 1853 total, 0 failures**, schema v35,
de-branded, TestAppBuilder clean, no stray files, working tree exclusively P8-S6. Release build sanity check: **0 warnings,
0 errors**. Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 6 — Professional Tax … schema v35` +
`docs(memory): Phase 8 slice 6 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to
`main` with the Phase-8 PR at the phase boundary). **Next = P8-S7 (§192 Salary TDS + Form 24Q — REUSES the Phase-7
`TdsService`; FY2025-26 new-regime default with a per-employee old-regime toggle, standard deduction ₹75k, §87A nil ≤₹12L,
surcharge/cess; Annexure I all quarters + Annexure II in Q4 → Form 16; offline FVU; likely no/additive schema; A14 to
web-verify the FY2025-26 §192 slabs & rebate).**

### Phase 8 slice 7 — §192 Salary-TDS + Form 24Q + Form 16 (2026-07-13) — schema v36
Seventh Phase-8 (Payroll) slice — the **fourth statutory** payroll head and the payroll-side income-tax return:
**§192 salary TDS** (tax-deducted-at-source on salary), computed and posted through the S3 balanced/atomic voucher, then
filed as **Form 24Q** and certified as **Form 16**. **A14 web-verified the FY2025-26 both-regime slabs, §87A rebate,
surcharge/cess, and §206AA no-PAN floor.** §192 differs structurally from PF/ESI/PT: the deduction is not a flat rate on
the month's wage but an **average-rate spreading of the *annual* estimated tax across the remaining pay periods**, trued
up to actuals as the year progresses. Delivered:
- **`SalaryIncomeTax`** (`src/Apex.Ledger/Services/SalaryIncomeTax.cs`) — pure, framework/DB/clock-free FY2025-26 §192
  income-tax engine. **NEW regime** (default, DP): slabs **0 / 4 / 8 / 12 / 16 / 20 / 24L**, **₹75k standard deduction**,
  **§87A nil ≤₹12L taxable + a marginal-relief band above ₹12L** (rebate capped ₹60k) so ₹12,10,000 pays only the excess
  over ₹12L, surcharge **10/15/25 (NEW capped at 25%)**. **OLD regime** (per-employee toggle, DP): slabs **2.5 / 5 / 10L**,
  **₹50k standard deduction**, **§87A ≤₹5L cliff**, **senior (₹3L) / super-senior (₹5L) basic-exemption**, surcharge up
  to **37%**, and the Chapter-VI-A / HRA / housing-interest deductions off the Form-12BB declaration. Both regimes then add
  **surcharge marginal relief** + **4% health-&-education cess**; **§206AA / no-PAN forces the higher of the computed rate
  or a 20% floor**. `SalaryIncomeTaxTests`.
- **§192 average-rate spreading with a paid-to-date + projected true-up** — the monthly TDS is **annual estimated tax ÷
  remaining pay periods**, where the annual estimate = **tax already deducted year-to-date (paid-to-date, off posted
  payroll history)** + **projected tax on the remaining months**; as actual pay/deductions land, the estimate re-trues so
  the year foots to the real liability (**fix for the A10 HIGH — the naive ×12 estimate over/under-withheld; replaced with
  the paid-to-date+projected true-up**). Mirrors the TCS/GSTR-1 YTD-projection pattern used across Phases 5–8.
- **Per-employee Form-12BB tax declaration** — **`TaxDeclaration`** (`src/Apex.Ledger/Domain/TaxDeclaration.cs`) carries
  the employee's regime choice + declared Chapter-VI-A investments / HRA / housing-loan interest / other-income, feeding
  the OLD-regime taxable-income build. Persisted per employee (`employee_tax_declarations`), editable in the UI.
- **Posting integration into the S3 balanced/atomic voucher** — salary TDS is a pure **employee statutory deduction**: it
  **reduces net** (Cr **TDS on Salary Payable**), **no employer side**. Rides the same **pre-validate → rollback-scope
  atomic** post and **non-destructive auto-create** of the payable ledger from S3 — no voucher-service change.
  `SalaryTdsVoucherPostingTests` (incl. the **cancelled-voucher guard** MED fix — a cancelled payroll voucher no longer
  contributes to paid-to-date).
- **Form 24Q** (`src/Apex.Ledger/Reports/Form24Q.cs`) — the quarterly salary-TDS return, **reusing the Phase-7 deductor
  identity + FVU framing**: **Annexure I every quarter** (per-employee deduction detail per the quarter's payroll) +
  **Annexure II in Q4** (the annual salary/tax reconciliation per employee). A **new Form24Q FVU writer**
  (`FvuWriter.cs` Form24Q overload, `Fvu24QWriterTests`) emits the caret-delimited FH→BH→CD→per-employee salary-detail→FT
  layout with **control totals derived from the records actually written** (the S4/S6 counted-totals discipline);
  **no-PAN employees force the Annexure II 20% floor** (MED fix). `Form24QForm16Tests`.
- **Form 16** (`src/Apex.Ledger/Reports/Form16.cs`) — the employee's salary-TDS certificate, **Part A** (deductor/employee
  identity + quarterly deducted/deposited summary) + **Part B** (the salary breakup, deductions, and tax computation),
  reconciling to Form 24Q by construction. `Form24QForm16Tests`.
- **Schema v35→v36 — additive** (`Schema.cs` `CurrentVersion = 36`): **`companies.salary_tds_enabled`** + the
  **`employee_tax_declarations` table**, added to **BOTH `CreateV1` AND `MigrateV35ToV36`** (no create-vs-migrate drift);
  the **downgrade helpers DROP `employee_tax_declarations`**; **`SchemaMigrationEquivalenceTests` green at v36**.
  `SqliteCompanyStore` persists it all (`SalaryTdsSchemaTests`).
- **Io canonical fold-in** — the salary-TDS flag + the per-employee tax declarations folded into `Apex.Ledger.Io`
  (`CanonicalModel`/`CanonicalMapper`/`CanonicalXml`/`ApplyJournal`/`ImportPlan`/`CompanyImportService`) → **paisa-exact
  JSON+XML lossless round-trip** with a **full import pre-flight ref guard** (`CanonicalSalaryTdsRoundTripTests`).
- **UI** — F11 **§192 config** toggle (`GstConfigViewModel` F11 pane) + **Income Tax Declaration**
  (`TaxDeclarationViewModel`) + **Form 24Q** (`Form24QViewModel`) + **Form 16** (`Form16ViewModel`) panes, plus the
  `SalaryTdsOptions` VM, wired into `MainWindowViewModel`/`MainWindow.axaml(.cs)`. `SalaryTdsUiViewModelTests`.
- **GOLDEN** (hand-derived, A14-matched, regression-locked): **NEW ₹15L → ₹97,500 annual / ₹8,125 monthly**; **NEW taxable
  ₹12L → ₹0** (§87A full rebate); **NEW ₹12,10,000 → ₹10,400** (marginal-relief band); **OLD ₹15L + 80C + 80D →
  ₹2,02,800 / ₹16,900**.
- **A10 adversarial review — fixed HIGH + 3 MED + 2 LOW.** HIGH: the §192 estimate used a naive **×12** annualization →
  systematic over/under-withholding → replaced with the **paid-to-date + projected-remaining true-up**. MED: (1)
  **cancelled-voucher guard** on paid-to-date; (2) **no-PAN Annexure II 20% floor** in Form 24Q; (3) the **Form 24Q FVU
  writer** control-total derivation. Plus 2 LOW. **⚠️ CRITICAL WORKFLOW ANOMALY + LESSON:** the S7 build workflow's
  **ENGINE agent returned GARBAGE** — an injected 'memory-retrieval' instruction block (disregarded as data, not
  instructions; it wrote no report) — so the downstream **UI + review agents CORRECTLY refused/flagged 'S7 not built'**
  (the on-disk tree was still v35 at that moment). The **FIX-phase agent then improvised the FULL engine build** (24 files
  → schema v36) on top, producing a complete-*looking* engine that **never got a proper review pass and had no UI**. I
  (orchestrator) **SALVAGED it**: verified the gate + that the 4 goldens matched the A14 brief, then **ran the 3 A10
  review lenses SEPARATELY**, built the UI, and ran a proper fix pass on the final on-disk code. **LESSON: a workflow can
  silently build a whole slice (under a *later* phase) after an engine failure — always verify WHAT actually landed on
  disk (git status + schema version) vs which agent claims to have produced it, and run the adversarial review on the
  final on-disk code regardless of which agent wrote it.** (Extends the recurring S3/S4/S5/S6/S8 "review phase may die on
  a session limit — verify it ran" habit.)
Gate fully green in Release: **Ledger 864 · Io 249 · Sqlite 137 · Desktop 658 = 1908 total, 0 failures**, schema v36,
de-branded, TestAppBuilder clean, no stray files, working tree exclusively P8-S7. Release build sanity check: **0
warnings, 0 errors**. Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 7 — §192 salary TDS … schema v36`
(`c019a7f`) + `docs(memory): Phase 8 slice 7 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched**
(rides to `main` with the Phase-8 PR at the phase boundary).
**CARRY-FORWARD (documented, non-blocking):** salary TDS credits a **separate 'TDS on Salary' payable** and is **NOT yet
depositable via the Phase-7 stat-payment / challan path** — routing it there would pollute **Form 26Q** (which keys on
`TdsLineTax`). Form 24Q is a complete, self-consistent RETURN, but the **salary-TDS deposit/challan integration is
deferred** to a later refinement / the S10 exit gate.
**Next = P8-S8 (payslips + Pay Sheet / Payroll Register / Attendance / Payment Advice reports; no schema change).**

### Phase 8 slice 8 — Payslips + payroll reports (2026-07-13) — no schema change
Eighth Phase-8 (Payroll) slice — the **presentation half**: the five payroll outputs Tally ships, all **pure
projections over the already-POSTED payroll voucher data** (no compute, no schema, no Io-canonical change; `Schema.cs`
stays `CurrentVersion = 36`). Delivered:
- **`PayrollReportSupport`** (`src/Apex.Ledger/Reports/PayrollReportSupport.cs`) — the shared projection layer
  (`PostedPayrollByEmployee`) that reads each employee's pay-head figures **off the posted `PayrollLineDetail` on the
  payroll voucher**, so every report **reconciles to the voucher by construction** and **excludes cancelled / never-posted
  months**. This is the KEY design choice of the slice (see the HIGH fix below).
- **Payslip** (`src/Apex.Ledger/Reports/Payslip.cs`) — the per-employee earnings/deductions/net statement for a month,
  + a **hand-rolled payslip PDF** (`src/Apex.Ledger.Io/PayslipPdf.cs`) reusing the bespoke deterministic `PdfWriter` +
  the **Indian amount-in-words** helper; byte-stable, de-branded, no NuGet. `PayslipPdfTests`.
- **Pay Sheet** (`src/Apex.Ledger/Reports/PaySheet.cs`) — the **employee × pay-head matrix** for a period; **foots both
  ways** (per-employee net across the row, per-pay-head total down the column).
- **Payroll Register** (`src/Apex.Ledger/Reports/PayrollRegister.cs`) — the period register of payroll vouchers with
  gross / deductions / net per employee.
- **Attendance Register** (`src/Apex.Ledger/Reports/AttendanceRegister.cs`) — the period attendance/production summary
  per employee off the posted attendance entries.
- **Payment (bank) Advice** (`src/Apex.Ledger/Reports/PaymentAdvice.cs`) — the per-employee net-pay bank-transfer advice
  for the disbursement run.
- **UI** — a nested **Reports → Payroll Reports** section (gated on the payroll feature), with a shared
  **horizontally-scrolling matrix** view (Pay Sheet / Register / Attendance / Payment Advice) + a bespoke **payslip**
  view; **Print → PayslipPdf** + **Export**, wired through `ReportsViewModel` / `MainWindowViewModel` /
  `MainWindow.axaml` / `Converters` / `ReportPrintProjector` / `ReportTabularProjector` / `PrintPreviewViewModel`.
  `PayrollReportsViewModelTests`.
- **A10 adversarial review — fixed 1 HIGH + 2 MED.** **HIGH:** the reports originally **recomputed pay-head figures from
  the masters** rather than reading the posted voucher — an employee paid via an **As-User-Defined-Value** pay head (a
  value that lives only on the voucher, not derivable from the salary structure) was **silently dropped** from the Pay
  Sheet / Payroll Register / **Payment Advice**, i.e. a **paid person was omitted from the bank advice** → fixed by
  projecting every report over the posted `PayrollLineDetail` (`PostedPayrollByEmployee`). **MED (1):** a **cancelled /
  never-posted** month rendered **phantom full salary** (masters still resolved a structure) → the posted-line projection
  excludes it. **MED (2):** `PriorFinancialYearProfessionalTax` (`PayrollComputationService.cs`, ~:478) lacked the
  **cancelled-voucher guard** the other statutory paid-to-date reads have → a cancelled payroll voucher **wrongly
  consumed the ₹2,500 annual PT cap**, under-charging later months → guard added (`PtVoucherPostingTests`). **⚠️ The S8
  build workflow's FIX phase died on a session limit** — consistent with the recurring S3/S4/S5/S6/S7 "review/fix phase
  may die — verify it ran" habit — so I ran the three A10 lenses + the fixes **separately** on the final on-disk code.
Gate fully green (verified by the orchestrator before this commit): **Ledger 875 · Io 252 · Sqlite 137 · Desktop 665 =
1929 total, 0 failures**, **schema v36 UNCHANGED**, de-branded, TestAppBuilder clean, no stray files, working tree
exclusively P8-S8 (5 reports + PayrollReportSupport + PayslipPdf + report/UI/PDF tests + Reports.cs /
PayrollComputationService.cs [PT cancelled guard] + ReportsViewModel / MainWindowViewModel / MainWindow.axaml /
Converters / ReportPrintProjector / ReportTabularProjector / PrintPreviewViewModel; **NO Schema.cs / Sqlite file**).
Release build sanity: **0 warnings, 0 errors.** Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 8 —
payslips (PDF) + Pay Sheet / Payroll Register / Attendance / Payment Advice reports (no schema)` (`d762567`) +
`docs(memory): Phase 8 slice 8 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to
`main` with the Phase-8 PR at the phase boundary).
**Next = P8-S9 (PF/ESI/PT statutory reports + Gratuity provision + statutory Bonus; likely additive schema v37 for the
gratuity/bonus config; A14 to web-verify the gratuity formula [(Basic+DA)×15×completed-years/26, cap ₹20L] + the
Payment of Bonus Act [8.33%–20%, eligibility wage ₹21k, calc ceiling ₹7,000]).**

### Phase 8 slice 9 — Gratuity provision + statutory Bonus + statutory-report consolidation (2026-07-13) — schema v37
Ninth Phase-8 (Payroll) slice — the two remaining year-end statutory provisions plus the consolidated statutory
landing. Additive **schema v36→v37** (9 company-config columns; **no new tables**), all folded into the Io canonical
model. Delivered:
- **Gratuity** (`src/Apex.Ledger/Services/Gratuity.cs` + `Domain/GratuityConfig.cs` / `GratuityWageBasis.cs` /
  `GratuityProvisionPopulation.cs`) — **A14-verified** Payment of Gratuity Act formula
  **(Basic+DA via `UseForGratuity`) × 15 × completed-years / 26**; **cap ₹20,00,000** (Act ceiling, configurable);
  a **≥6-month year rounds up** to the next completed year; **vesting at 60 months** (5 completed years) — below that a
  provision may still be accrued per policy but the payable is not yet vested; **accrue-all-active** default population.
- **Gratuity provision posting** (`src/Apex.Ledger/Services/GratuityProvision.cs`) — the accrual is booked as a
  **balanced Journal for the DELTA over the prior book balance**: **Dr Gratuity Expense / Cr Gratuity Provision** when
  the accrued liability rises, and the **reverse pair when it falls** (over-provision release). The prior book balance
  is **derived from the ledger, inclusive of same-date postings**, so re-running the provision on the same date books
  **only the incremental delta** rather than double-counting the already-posted amount (see the A10 fix).
- **Statutory Bonus** (`src/Apex.Ledger/Services/StatutoryBonus.cs` + `Domain/BonusConfig.cs`) — **A14-verified**
  Payment of Bonus Act: **eligible when Basic+DA ≤ ₹21,000** and **≥ 30 days worked**; the **calc ceiling = max(₹7,000,
  minimum wage)**; the **rate is clamped 8.33%–20%** (statutory floor/ceiling, default **8.33%**); **prorated by months
  worked**; the **minimum bonus is payable even without allocable profit** (min-bonus obligation).
- **Registers** — **Gratuity Provision register** (`src/Apex.Ledger/Reports/GratuityProvisionRegister.cs`) and
  **Bonus register** (`src/Apex.Ledger/Reports/BonusRegister.cs`).
- **Consolidated Statutory Reports (Payroll)** nav — a single landing linking **PF-ECR / ESI / PT / Gratuity / Bonus**
  (register + config VMs: `GratuityProvisionRegisterViewModel` / `BonusRegisterViewModel`; F11 gratuity/bonus config +
  register UI through `MainWindowViewModel` / `MainWindow.axaml`).
- **Schema v37** — the 9 gratuity/bonus config columns added to **BOTH `CreateV1` AND `MigrateV36ToV37`** (equivalence
  test green; no new tables); **Io fold-in lossless** (`CanonicalModel` / `CanonicalMapper` / `CanonicalXml` /
  `ApplyJournal`) + **import pre-flight ref checks** in `CompanyImportService` / `ImportPlan`.
- **Goldens** — gratuity **26,000 × 15 × 10 / 26 = ₹1,50,000**; bonus **₹18,000 → capped ₹7,000 → 8.33% = ₹6,997/yr**.
- **A10 adversarial review — all 3 lenses ran** (no session-limit death this slice) — caught + fixed a **same-date
  provision re-post double-counting the prior**: the prior book balance now reads the ledger **inclusive of same-date**,
  so a same-date re-run posts only the delta (regression-locked in `GratuityProvisionPostingTests`).
- **DP defaults adopted (confirm at the gate): U1** Act cap **₹20,00,000**; **U2** gratuity population **accrue-all-active**;
  **U3** bonus minimum wage default **0 → falls back to ₹7,000** calc ceiling; **U4** bonus **prorated by months worked**;
  **U5** bonus rate default **8.33%**.
Gate fully green (verified by the orchestrator before this commit): **Ledger 919 · Io 258 · Sqlite 140 · Desktop 680 =
1997 total, 0 failures**, **schema v37**, de-branded, TestAppBuilder clean, no stray files, working tree exclusively
P8-S9. Release build sanity: **0 warnings, 0 errors.** Committed + pushed by A12 (R4): `feat(payroll): Phase 8 slice 9 —
Gratuity provision + statutory Bonus + PF/ESI/PT report consolidation, SQLite schema v37` (`d26010a`) +
`docs(memory): Phase 8 slice 9 log`. Branch `claude/recursing-swirles-3138c6` pushed; **`main` NOT touched** (rides to
`main` with the Phase-8 PR at the phase boundary).
**Next = P8-S10 (EXIT GATE — golden end-to-end payslip with all statutory [PF/ESI/PT/§192] reconciled; migration chain
v29→v37; Io losslessness; de-brand sweep; RUN THE REAL APP + headless-render the payroll screens; 3-OS CI
cross-platform audit; resolve the carry-forwards incl. the S7 salary-TDS deposit-path; then A12 opens a PR
recursing→main + merges once 3-OS CI green; PAUSE for Phase 8→9 go-ahead).**

### Phase 8 slice 10 — EXIT GATE (2026-07-13) — no schema change
Tenth and **final** Phase-8 slice — the exit gate: a **golden end-to-end payroll reconciliation** plus the four
phase-close audits and a set of culture/doc tidies. **No schema change** (stays **v37**); nothing new is persisted.
Delivered:
- **`Phase8GoldenPayrollTests`** (`tests/Apex.Ledger.Tests/`) — **2 employees** exercising the whole statutory stack
  (**PF EPS/EPF-split + EDLI + admin, ESI 0.75%/3.25%, Professional Tax state slab, §192 salary-TDS**). The posted
  payroll voucher **balances Dr==Cr==₹1,47,835 paisa-exact**, and asserts the **anti-3.67% invariant** (employer PF is
  EE−EPS, never a flat 3.67%). **Every downstream surface reconciles off the same posted data**: payslip, payroll
  register, payment advice, **PF ECR**, **ESI monthly**, **PT register**, **Form 24Q Annexure I**, gratuity provision,
  and bonus register all tie to the voucher — the single golden that proves the phase hangs together end-to-end.
- **The 4 phase-close audits — all clean.** (1) **De-brand**: 0 must-fix; no "Tally" in any shipped code/UI/PDF surface.
  (2) **Migration v29→v37**: the full additive chain is sound and covered — `SchemaMigrationEquivalenceTests` still
  proves `CreateV1` == step-migrate parity across every Phase-8 version bump. (3) **Io losslessness**: complete — every
  Phase-8 persisted surface round-trips paisa/count-exact through JSON+XML, and the **import-bypass class is closed**
  (`CompanyImportService` mirrors the engine guards, all-or-nothing pre-flight ref checks). (4) **3-OS CI**: no
  path-separator or byte-order hazard — deterministic byte-stable writers, culture-invariant formatting (see tidies).
- **6 exit-gate tidies** (culture + docs) — `InvariantCulture` pinned on **(a)** the ESI last-working-day date
  (`Reports/EsiContribution.cs`), **(b)** the Bonus register **MMM** month label (`ViewModels/BonusRegisterViewModel.cs`),
  and **(c)** the CsvCanonicalBridge FY dates (`Io/CsvCanonicalBridge.cs`) — so a non-en culture CI leg can't drift the
  formatting; **(d)** `CanonicalMapper.SchemaVersion` bumped **32→37** (had lagged the schema); **(e)** `Schema.cs` and
  **(f)** `SchemaMigrationEquivalenceTests` docstrings refreshed to **v37**.
- **RAN THE REAL APP** — launched clean; **16 payroll screens headless-rendered de-branded with no text overlap**; the
  **payslip visually confirmed net ₹40,000**. TestAppBuilder probe reverted afterwards (no stray files).
- **Gate: 2002 tests green** (Ledger 924 · Io 258 · Sqlite 140 · Desktop 680), schema **v37**, de-branded, Release build
  0 warnings / 0 errors, working tree exclusively the 7 S10 files. Committed + pushed by A12 (R4):
  `test(payroll): Phase 8 slice 10 exit gate — golden end-to-end payroll reconciliation + cross-platform/culture + doc
  tidies` (`77835bc`) + `docs(memory): Phase 8 slice 10 exit-gate log`. Branch `claude/recursing-swirles-3138c6` pushed;
  A12 opens the **Phase-8 PR recursing→main** (do NOT merge yet — awaits 3-OS CI + the Phase 8→9 go-ahead).
- **✅ PHASE 8 (PAYROLL) COMPLETE — S1–S10, SQLite schema v29→v37, 2002 tests green.** Full Indian payroll: masters +
  pay-heads (5 calc types) + dated salary structures; attendance + payroll voucher with balanced integrated posting;
  **PF** (EPS/EPF split + EDLI + admin + ECR), **ESI** (0.75%/3.25% + contribution-period continuation), **Professional
  Tax** (state slabs + ₹2,500 cap), **§192** salary-TDS (both regimes + 87A/surcharge marginal relief) + **Form 24Q** +
  **Form 16**; payslips + payroll registers; **gratuity** provision + statutory **bonus**; consolidated PF/ESI/PT reports.
- **CARRY-FORWARDS (documented, non-blocking — confirm/park at the pause):** (a) **S7 salary-TDS deposit-path** — the
  §192 TDS is computed + shown + booked, but its *deposit* still rides a separate ledger, **not yet routed through the
  Phase-7 challan/ITNS-281 stat-payment flow**; wire it in a later slice. (b) **Wide-matrix single-frame layout** — the
  Payroll Register and Payment Advice use their **intended horizontal scroll** for the wide statutory columns (not a
  layout defect). (c) The **DP defaults U1–U5** (gratuity cap ₹20L / accrue-all-active / bonus ₹7,000 calc ceiling /
  prorate-by-months / 8.33% rate) plus the **inherited Phase-7 carry-forwards** (recon-report cash-in-window,
  OutstandingPayable section-awareness, FIFO backdating) all await the user's confirmation at the Phase 8→9 pause.

## Phase 7 Slice 7 — Form 16A / 27D certificates + Form 27A control chart (PDF) (2026-07-10) — NO schema change (v29)
Seventh (final compute-side) TDS/TCS slice: the **certificates** — the deductee's/collectee's proof-of-tax and the
return's control-total cover. **No schema change** (`Schema.cs` stays `CurrentVersion = 29`); every figure is a pure
projection off already-posted data, so nothing new is persisted. **Finalized after a session-limit interruption killed
the UI mid-write** — the engine + PDF + report edits had already landed on disk; I (A12) re-verified and completed the
Desktop VM wiring, then ran the full gate green before committing. Delivered:
- **Form 16A** (`src/Apex.Ledger/Reports/Form16A.cs`) — the **TDS certificate**: a `Form16ADeductorBlock` (deductor
  name/TAN/PAN + F11 person-responsible identity), a per-party `Form16ADeducteeBlock`, and `Form16ADeductionRow`s read
  **verbatim off the matching `Form26QDeducteeRow`** so the certificate **reconciles to the 26Q return by construction**
  (GST-exclusive assessable base; §194 section + FVU code; rate/PAN-applied). `Form16ATests`.
- **Form 27D** (`src/Apex.Ledger/Reports/Form27D.cs`) — the **TCS certificate**, exact mirror of 16A on the collector
  side: `Form27DCollectorBlock` + `Form27DCollecteeBlock` + `Form27DCollectionRow`s verbatim off `Form27EQCollecteeRow`
  (GST-**inclusive** base per Circular 17/2020; §206C code; TCS additive). Reconciles to 27EQ by construction.
  `Form27DTests`.
- **Form 27A** (`src/Apex.Ledger/Reports/Form27A.cs`) — the return **control chart** for a 26Q *or* 27EQ quarter: a pure
  projection of the return's control totals (deductee/collectee count, challan count, total tax, total amount, total
  deposited) + the FVU-style cross-check messages the return itself surfaces (`Form26QControlTotals.Validate` /
  `Form27EQControlTotals.Validate`); figures **tally with the return by construction**. `Tallies` ⇒ no messages.
  `Form27ATests`.
- **PDFs** (`src/Apex.Ledger.Io/Form16APdf.cs`, `Form27DPdf.cs`, `Form27APdf.cs`, shared `CertificatePdfSupport.cs`) —
  hand-rolled, deterministic, **byte-stable, de-branded** (no NuGet), sharing header/label/amount-in-words helpers. The
  27A cover renders the **tally status** with the visible text **de-branded TALLY→AGREE** ("Control totals AGREE — the
  return cross-checks and is clear for FVU validation"). `CertificatePdfTests` assert byte-identical re-render.
- **UI** — `Form16AViewModel`/`Form27DViewModel`/`Form27AViewModel` + `CertificatePages.cs` wired into
  `GatewayColumn`/`MainWindowViewModel` (`MainWindow.axaml`/`.axaml.cs`); the panes this slice re-completed after the
  mid-write interruption. `Form16AViewModelTests`/`Form27DViewModelTests`/`Form27AViewModelTests`.
No A10 HIGH/MED carve-overs this slice (certificates are verbatim projections off the S4/S6 return rows and reconcile to
them by construction; 27A control totals derive from the same projection as the returns; no persistence, no schema). Gate
fully green in Release: **Io 181 · Ledger 665 (incl. Robert/Bright + GST golden) · Sqlite 111 · Desktop 558 = 1515 total,
0 failures.** Working tree clean (only Slice-7 files: Form16A/27D/27A reports + Form16A/27D/27A PDFs +
CertificatePdfSupport + 3 cert VMs + CertificatePages + Gateway/MainWindow wiring + tests; **Schema.cs UNCHANGED at
v29**; no scratch/probe files). Committed on branch `claude/wonderful-hellman-59520a` (code+tests, then this docs note);
pushed to origin, main untouched. **Next = S8 (TDS/TCS exception reports).**

## Phase 7 Slice 6 — TCS Stat Payment deposit + challan reconciliation + Form 27EQ + FVU (2026-07-10) — SQLite schema v28→v29
Sixth TDS/TCS slice: the **TCS deposit + statutory-return** half — the mirror of the S3 (TDS deposit/challan/recon) and
S4 (Form 26Q + FVU) slices, now on the collect-at-source side. **SQLite schema v28→v29** (new `tcs_challans` +
challan↔stat-payment-voucher link tables, additive `MigrateV28ToV29` inside the version-bump transaction, no ALTER).
Delivered:
- **TCS Stat Payment (deposit) voucher** — `TcsDepositService` picks up the outstanding "TCS Payable" balance and builds
  the balanced ITNS-281 deposit legs (Dr TCS Payable, Cr Bank), zeroing the payable for the deposited dues; the
  statutory-payment flag **reuses the existing Payment base voucher type** (per Tally, as in S3). `TcsStatPaymentViewModel`
  drives the UI (`TcsStatPaymentViewModelTests`).
- **Challan + reconciliation** — `TcsChallan` domain type (BSR code, challan serial, tender date, nature-of-goods,
  amount breakup) persisted via `SqliteCompanyStore` and linked to the deposit voucher through a challan↔voucher link.
  `TcsChallanReconciliation` sums collected TCS off every posted, **non-cancelled** voucher and matches it to challans
  whose booking Stat-Payment voucher is **still live** — the **cancelled-voucher fix reused** from the S3 recon rework
  (a cancelled/absent booking drops the challan; collected side and `LedgerBalances.SignedClosing` both already skip
  cancelled vouchers, so the two sides stay consistent). `TcsChallanReconciliationViewModel` surfaces
  unmatched/partly-matched dues.
- **Form 27EQ projection** (`src/Apex.Ledger/Reports/Form27EQ.cs`) — a collector/deductor block (TAN, person-responsible
  identity denormalised off F11 `TcsConfig`), per-party **collectee-detail rows** read *verbatim* off the posted
  `TcsLineTax` (collection code + FVU code, collection date = voucher date, assessable value, rate bp, PAN-applied,
  §206C(9) lower-collection hook) so the rows **reconcile to the "TCS Payable" credit postings for the quarter by
  construction**, plus per-challan blocks. **Quarter attribution by COLLECTION date, not deposit date** (same cross-FY
  fix as S4's Form 26Q). `Form27EQTests`.
- **FVU 27EQ writer** (`FvuWriter.cs` 27EQ path) — caret(`^`)-delimited eTDS/eTCS record layout FH→BH→per attributed
  challan CD + its collectee DD→FT with control totals; **portion-tracked + DD-derived totals reused from the S4 fix**
  (undeposited/short-deposited in-quarter collections produce fewer DD lines; FH/BH/FT counts derive from records
  actually written so the header/trailer always agree with the body). Deterministic + byte-stable + de-branded, offline
  emulation only (decision D4). `FvuWriter27EQTests`.
- **Io losslessness** — `tcs_challans` (+ links) folded into the `Apex.Ledger.Io` canonical model (`CanonicalModel`,
  `CanonicalMapper`, `CanonicalXml`, `ApplyJournal`, `ImportPlan`) so TCS challans round-trip **paisa- and count-exact**
  through JSON+XML export/import (`CanonicalTcsChallanRoundTripTests`, `TcsChallanRoundTripTests`).
- **UI** — `Form27EQViewModel` + the two TCS VMs wired into `GatewayColumn`/`MainWindowViewModel`
  (`MainWindow.axaml`/`.axaml.cs`).
No A10 HIGH/MED carve-overs this slice (deposit legs balance gross + payable zeroed by construction; collectee rows are
verbatim off posted TCS lines and reconcile to the payable credits; file control totals counted from emitted records;
cancelled-voucher and DD-derivation fixes carried in from S3/S4). Gate fully green in Release: **Io 172 · Ledger 657
(incl. Robert/Bright + GST golden) · Sqlite 111 · Desktop 538 = 1478 total, 0 failures.** Working tree clean (only
Slice-6 files: TcsDepositService + TcsChallan + TcsChallanReconciliation + Form27EQ + FvuWriter 27EQ path + 3 VMs +
Gateway/MainWindow wiring + Schema/SqliteCompanyStore v29 migration + Io canonical + tests; no scratch/probe files).
Committed on branch `claude/wonderful-hellman-59520a` (code+tests, then this docs note); pushed to origin, main
untouched. **Next = S7 (TCS/TDS certificates — Form 16A + 27D + 27A).**

## Phase 7 Slice 5 — TCS compute + auto-collection on sales (additive, goods-driven) (2026-07-10) — SQLite schema v27→v28
Fifth TDS/TCS slice: the **TCS collect-at-source** engine — the mirror of GST and the additive counterpart of the S2
withholding TDS engine. Unlike TDS (carve-out withholding), **TCS is additive**: collected *on top* of the sale, so on
a TCS-applicable Sales voucher the collector books `Dr Party = value + GST + TCS`, `Cr Sales = value` (unchanged),
`Cr Output GST` (unchanged Phase-4 engine), `Cr "TCS Payable" = TCS` (a Duties & Taxes liability). Delivered:
- **`TcsService`** (`src/Apex.Ledger/Services/TcsService.cs`) — pure, framework/DB/clock/RNG-free computation over the
  `Company` aggregate (like `GstService`). **Goods-driven Nature-of-Goods detection (the S2 lesson applied to TCS):**
  the §206C `NatureOfGoods` resolves from the STOCK ITEM's `TcsNatureOfGoodsId` first, then the sales ledger's (only
  when `TcsApplicable`), **never the party**. The **party** drives only PAN/rate + the collectee gate: PAN ⇒
  `RateWithPanBp`; **no-PAN ⇒ §206CC `RateWithoutPanBp`** (higher of 2×/5%, EXCEPT the 206C(1H) no-PAN cap of 1% the
  S1 seed already encodes). **Base includes GST** for every §206C row (Circular 17/2020, `BaseIncludesGst` flag).
  Rounding = income-tax **nearest-rupee round-half-up** (reuses `TdsService.NearestRupee`).
- **Threshold + §206C(1H) legacy gate** — a nature with no threshold collects on the full base (scrap); the legacy
  **§206C(1H)** nature applies its ₹50-lakh threshold as a **cumulative-FY receipts projection** (`ProjectPriorCumulative`
  over prior posted vouchers, like `Gstr1` YTD) and charges only receipts **exceeding** the threshold (`ChargeableBase`,
  bare-section "sale consideration exceeding fifty lakh rupees"); any other threshold-bearing nature (§206C(1F) motor
  vehicle) applies it **per single transaction**. "Exceeds" is strict. The §206C(1H) legacy nature is **non-selectable
  for dates ≥ 01-Apr-2025** (FA2025 year-gate, `IsSelectableOn`).
- **No GSTR-1 / 27EQ double-count** — TCS Payable sits under Duties & Taxes, so `ClassificationRules.IsDutiesAndTaxesLedger`
  excludes it from the item-invoice pairing sum exactly like the GST tax ledgers: the additive collection foots without
  disturbing the Sales pairing invariant (Sales credit == Σ item value). TCS is its own payable, never a sales/GST amount.
- **`TcsLineTax`** (`src/Apex.Ledger/Domain/TcsLineTax.cs`) rides the party/payable `EntryLine` (nature, collection code,
  assessable value, rate bp, TCS amount, collectee id, PAN-applied); present even below threshold (TCS 0) so the FY
  receipts projection stays exact. **Io-lossless**: `tcs_lines` folded into the canonical model (`CanonicalModel`/
  `CanonicalMapper`/`CanonicalXml`/`ImportPlan`) — paisa+count-exact JSON+XML round-trip (`CanonicalTcsLineRoundTripTests`).
- **Schema v27→v28** (`Schema.cs` `CurrentVersion = 28`, `SqliteCompanyStore` persists `tcs_lines`); migration is
  additive (new table + index inside the version-bump transaction, no ALTER).
- **UI** — `VoucherEntryViewModel` computes + shows the additive TCS leg on Sales; `MainWindow.axaml` wiring.
No A10 HIGH/MED carve-overs surfaced this slice (additive design foots by construction; goods-driven detection avoids the
S2 party-driven trap from the outset; assessable stays FULL receipts while only the charged base is carved so subsequent-
year cumulative arithmetic remains exact). Gate fully green in Release: **Io 161 · Ledger 638 (incl. Robert/Bright + GST
golden) · Sqlite 108 · Desktop 519 = 1426 total, 0 failures.** Working tree clean (only Slice-5 files: TcsService +
TcsLineTax + EntryLine.Tcs + Schema/SqliteCompanyStore v28 + Io canonical wiring + VoucherEntryViewModel/MainWindow +
tests; no scratch/probe files). Committed on branch `claude/wonderful-hellman-59520a` (code+tests, then this docs note);
pushed to origin, main untouched. **Next = S6 (Form 27EQ quarterly TCS return + TCS challan/stat-payment, schema v29).**

**Log-continuity note (2026-07-14):** this committed living log runs through **Phase 7 (TCS)** above; **Phase-7.5 → Phase-8 (Payroll, slices S1–S10, schema v29→v37) per-slice detail was kept in the per-worktree logs + git commit messages, not appended here** (a documented drift — see the `MEMORY.md` index note). That Phase-8 detail therefore lives in **git history** (branch `claude/recursing-swirles-3138c6`, merged to `main` via PR #20 merge commit `39b31e3`), not in this file. Full per-slice logging resumes with **Phase 9** below.

## Phase 9 — GST-Advanced (branch `claude/apex-phase-9-start-51d103`, off merged main `39b31e3`)

- **Planning** (2026-07-14): requirements doc `docs/phase9-gst-advanced-requirements.md` — web-verified (R7) FY2025-26 / GST-2.0 law; **9 slices S1–S9 (schema v37→v44), 34 DPs approved at recommended defaults**. GST 2.0 (eff **22-Sep-2025**): slabs **0/5/18/40** + retained specials **3/1.5/0.25** + tobacco **28%+cess carve-out** (40% = plain GST, **not** cess); cess = **3 dated FY windows**; **§9(4) RCM = real-estate-promoter-only**; e-invoice **₹5cr**. **Connectivity = HYBRID** — offline JSON baseline for all; optional live e-Invoice/e-Way via the customer's **OWN NIC creds**; pluggable `IGstPortalConnector` adapter + stubbed `GspConnector` seam for a future GSP integration (**not built**; govt ₹0/txn, future GSP ≈₹9k–50k/yr/GSTIN ~ Tally TSS ₹4.5–13.5k/seat; **security invariant ER-16**: never hold GSP/vendor cred nor customer portal password/DSC). Doc committed by A12 = **`515a821`**.
- **P9-S1 — GST 2.0 dated rate framework + Compensation-Cess seam (schema v37→v38) — COMPLETE, commit `2980b86`.** New tables `gst_rate_history` + `gst_cess_rates` + **7 cess/RSP cols** on `stock_items` and **7 `sp_` mirror** cols on `ledgers`, added to BOTH `CreateV1` and `MigrateV37ToV38` (SchemaMigrationEquivalenceTests green). **Engine:** `ResolveRate` voucher-date override applied ONLY when a matching dated row exists (byte-identical when off); Compensation-Cess seam supports **ad-valorem / specific-per-unit / RSP-factor** valuation across the 3 dated FY2025-26 windows; **lazy** Output/Input Cess ledgers; `InvoiceTax.TotalCess` **ring-fenced OUT of** `TotalTax`; advanced GST is opt-in via `SeedAdvancedGst` (base `EnableGst` byte-identical, ER-13). **Io:** `GstRateHistoryDto`/`GstCessRateDto` + extended config/stock DTOs, lossless JSON+XML round-trip, `CompanyImportService` mirrors `EnsureValid` (all-or-nothing). **UI DEFERRED** to a follow-up pass (GST Rate Setup bulk screen + cess/RSP master & item UI + voucher-date call-site threading). **A10 adversarial review (3 lenses) caught + fixed:** 1 **HIGH** — ring-fenced Cess line double-counted GSTR-1/GSTR-3B taxable value + injected a phantom rate row → exclude `GstTaxHead.Cess` from `GstReportSupport.InvoiceTaxableValue` + `Gstr1.ReadInvoiceRateGroups`; 2 **MED** — `ResolveCess` ignored taxability (cess on Exempt/Nil items → taxability short-circuit); RSP-factor with no declared RSP silently computed ₹0 → fail-fast; + 2 **LOW** (CessApplicable XML doc; canonical `SchemaVersion`→38) — all regression-locked with tests proven to fail pre-fix. Schema-parity + Io-round-trip A10 lenses found no defect. Gate (I re-ran it in Release myself): **2029 tests green — Ledger 941 · Io 265 · Sqlite 143 · Desktop 680, 0 warn/0 err**; Robert/Bright + Phase-4 base-GST fixtures unchanged; ER-13 byte-identical-when-off proven.
- **P9-S1 UI — GST Rate Setup bulk screen + cess/RSP master/item UI + voucher-date threading — COMPLETE (completes S1).** New `GstRateSetupViewModel` (dated GST-rate + Compensation-Cess grids, "Seed GST 2.0 defaults", add-window forms; nav = Gateway → Statutory → **GST Rate Setup**, shortcut **Ctrl+R**, on-screen **Ctrl+A** appends a window); cess/RSP fields on the Stock Item master GST card; `VoucherEntryViewModel` threads the voucher **Date** into `ResolveRate`/`ResolveCess` (item-invoice + TCS-base paths) and shows a ring-fenced **Compensation Cess** line. **Independent headless-Skia render-verify = CLEAN** on all 3 screens (the wide rate/cess grids use the app's accepted horizontal-scroll pattern — labels full, not clipped; cess = ₹2,20,000 on a 20-Sep-2025 car sale, ₹0 on 25-Sep, proving dated resolution flows through the UI). **A10 UI review (2 lenses) caught + fixed:** 2 **HIGH** — (a) the LIVE item-invoice recalc called `ComputeItemInvoiceGst()` UNGUARDED, so an RSP-factor-cess item with no Retail Sale Price threw during data entry and broke the voucher screen (the Accept path guarded the same call, the recalc did not) → mirrored the `try/catch (InvalidOperationException/ArgumentException)` + friendly `Message` + cleared tax/cess display; (b) the Stock Item master persisted an unsellable landmine (item on an RSP-factor cess HSN saved with no RSP) → master now rejects it via a date-agnostic `_company.Gst.CessRates` HSN lookup; + 2 **LOW** — reject RSP-valuation-basis with no RSP (guard added to `StockItemGstDetails.EnsureValid` too); informational overlapping-window note on rate/cess add (deterministic, not blocked). All regression-locked (both HIGH tests proven to fail pre-fix). Gate (I re-ran Release myself): **2042 tests green — Ledger 942 · Io 265 · Sqlite 143 · Desktop 692, 0 warn/0 err**; byte-identical when advanced-GST off. **⚠️ CARRY-FORWARD (deferred, needs R7 web-verify by A14 before changing):** whether the **§206C TCS base**, when `BaseIncludesGst=true` (e.g. 206C(1H) gross receipts), should ALSO include Compensation Cess — today the TCS-on-GST base uses `InvoiceTax.TotalTax` which is cess-excluded (ring-fence), so TCS may under-collect on cess-bearing goods sold to a collectee; a CBDT law question, left unchanged pending verification. **✅ S1 FULLY COMPLETE** (engine `2980b86` + UI/A10-fixes `4ae97c3`). **Next = P9-S2 (RCM + self-invoice/payment voucher + §34 CDN + advances, v38→v39).**
- **P9-S2a — RCM (reverse charge) core + self-invoice/payment + FULL v39 schema — COMPLETE, commit `2e2bd48`** [first of a 2-part S2; design brief recommended the split]. Lands the whole **v39** schema: 4 new tables `rcm_categories`/`rcm_documents`/`gst_cdn_links`/`gst_advance_receipts` (the last two empty until S2b) + RCM columns on stock_items/ledgers/entry_lines/voucher_types, in BOTH CreateV1 and MigrateV38ToV39 (equivalence-proven). **RCM dual-leg posting:** the recipient self-accounts an OUTPUT RCM liability in a **dedicated cash-only ledger** (excluded from output tax, never credit-settled) + an INPUT ITC; supplier charges no tax; **balances to paisa**; CGST/SGST vs IGST by POS. Import-of-services = RCM (IGST) / import-of-goods excluded. **§12(3)/§13(3) time-of-supply** (30/60-day). Dated notified-category master (Notn 13/2017, 10/2017, promoter-only 7/2019); cement 28→18% dated via HSN 2523. Self-invoice (`RcmDocument`, own series, Rule 47A 30-day) + `IsRcmPaymentVoucher`. RCM cess ring-fenced. Reports: GSTR-3B **3.1(d)/4A(2)/4A(3)** + GSTR-1 **4B**, RCM excluded from the normal outward/ITC buckets; `FindTaxLedger` disambiguated by `IsReverseCharge`. Io lossless (JSON+XML) + `CompanyImportService` structural cash-only guard. **A10 (3 lenses): 0 CRIT / 0 HIGH** — posting/paisa/cash-only/POS/cement/cess + schema-parity/ordinals/migration + Io round-trip all traced clean; **caught + fixed 3 MED + 4 LOW:** law-correct §12(3)/§13(3) ToS (fixed the CODE **and** the tests that enshrined the wrong values — goods `invoice+31` not `receipt+30`; services `invoice+61`, invoice-date-is-NOT-a-limb); outward-RCM supply double-counted in the Exempt/Nil bucket → excluded via new `GstReportSupport.IsOutwardReverseChargeSupply`; credit-note-on-outward-RCM signs Table-4B down; per-unit/RSP cess with quantity 0 on an RCM line → fail-fast (was silent ₹0); import cash-only guard made **structural** (not `RcmScheme==null`). **2066 tests green** (Ledger 955·Io 273·Sqlite 146·Desktop 692, 0 warn/0 err), I re-ran Release myself; byte-identical when RCM off (ER-13). **UI DEFERRED** to a combined RCM/CDN/advances UI pass. **⚠️ Note for S2b: verify DP-27 (does Phase-4 already post §34 CDN GST?) at kickoff.** **Next = P9-S2b — `CreditDebitNoteService` + `AdvanceReceiptService`.**
- **P9-S2b — §34 Credit/Debit Notes + GST-on-advances — COMPLETE (completes S2; NO schema change, still v39; committed with S2b code below).** **DP-27 resolved:** §34 CDN GST was **net-new** (shipped Phase-4 had no CDN-GST posting — the report sign was an unsigned magnitude), so no golden at risk; built the §34 formalization fresh. **`CreditDebitNoteService`:** §34 original-invoice link (`gst_cdn_links`); the sign is driven off the link's `CdnType` — a CREDIT note REDUCES / a DEBIT note INCREASES the supplier output tax, correctly **overriding** the base-type `DirectionOf`; §34(2) 30-Nov-following-FY guard keyed on the ORIGINAL supply date; GSTR-1 Table 9B; CDN-linked vouchers excluded from the main GSTR-1/3B sweep + **signed-folded** into the output totals (**GSTR-1 TotalTax == GSTR-3B 3.1(a) reconcile**). **`AdvanceReceiptService`:** Rule-50 receipt taxes **SERVICES only** (goods de-taxed, Notn 66/2017); `Cr Output {head}` + `Dr "Output Tax on Advances"` suspense, balances to paisa; adjust-against-invoice + Rule-51 refund; GSTR-1 11A/11B; reuses the (now law-correct) `RcmService.TimeOfSupply`. **A10 (3 lenses) — the CDN-sign lens was RE-RUN after it emitted a garbage placeholder on the first pass ⚠️ (WORKFLOW-RELIABILITY: an A10 lens agent can return a placeholder — ALWAYS eyeball lens output before trusting "no findings"). §34 CORE CONFIRMED SOUND (record-driven sign, totals reconcile, §34(2) FY/boundary, byte-identical off) — 0 CRIT / 0 HIGH in the CDN core; caught + fixed 2 HIGH + 3 MED + 2 LOW:** (H) a Rule-51 refund was reversed in the ledger but **never netted from GSTR-1** (`RefundVoucherId` ignored by 11A/11B → over-stated liability — the recurring "persisted flag ignored by the compute" class) → 11B now nets refunds; (H) a §34(2)-**override** late CDN could not be **re-imported** (lossy — import re-enforced the entry-time guard, rejecting the whole batch) → import no longer re-enforces §34(2) (**lossless restore**; §34(2) is an entry-time decision; a persisted `OverrideTimeLimit` flag = future **schema carry-forward**); (M) CDN original-invoice link now has a pre-flight resolvability guard (clean per-record error, not opaque rollback); (M) partial advance-adjustment over-reversed → **fail-fast full-only** (partial adjustment with a residual balance = **schema carry-forward**, needs an adjusted-amount column); (M) §34(2) was bypassed when the original-invoice-date was null → a liability-reducing credit note now **requires** the date; (L) GSTR-3B exempt-outward missing the CDN exclusion → added (reconciles with GSTR-1); (L) `ReadInvoiceHeads` (Table 9B) included RCM lines vs `ReadCdn` → defensive skip. All regression-locked (4 tests fail pre-fix). **2088 tests green** (Ledger 971·Io 279·Sqlite 146·Desktop 692, 0 warn/0 err), I re-ran Release myself; byte-identical when off. **UI DEFERRED** to the combined RCM/CDN/advances UI pass. **✅ S2 FULLY COMPLETE** (S2a `2e2bd48` + S2b). **⚠️ tiny pre-existing tidy:** `Schema.cs:73` doc comment still says "CurrentVersion = 37" (const at :86 = 39) — fix opportunistically in S3 (which touches Schema for v40). **Next = P9-S3 (Composition scheme + Bill of Supply + CMP-08/GSTR-4, v39→v40).**
- **P9-S3 — Composition scheme + Bill of Supply + CMP-08/GSTR-4 (schema v39→v40) — COMPLETE (committed with this slice).** Schema **v40** = 2 `companies` columns (`composition_sub_type`, `composition_opt_in_date`) in CreateV1 + `MigrateV39ToV40` (NO new table — CMP-08/GSTR-4 are recomputed projections); **fixed the stale `Schema.cs:73` "=37" doc comment → 40**. `CompositionSubType` enum {Manufacturer, Trader, Restaurant, ServiceProvider §10(2A)} + static `CompositionThreshold` (8 special states {05,11,12,13,14,15,16,17} ₹75L / §10(2A) ₹50L / else ₹1.5cr; rate 100/500/600 bp; **base = TOTAL turnover for Mfr/Restaurant, TAXABLE-only for Trader/§10(2A)**). Engine (ALL gated on `RegistrationType==Composition` → a Regular company stays BYTE-IDENTICAL): `ComputeInvoiceTax` short-circuits to empty (no output on sale, NO ITC on purchase); `EnableGst` gates the 6 GST ledgers off; **RCM still applies but routes the tax to a non-creditable COST ledger (no ITC)** while STILL posting the cash-only liability → CMP-08 (the S2↔S3 subtlety); Bill of Supply = derived `IsBillOfSupply`+declaration. `CompositionTaxService` = flat % × turnover (from posted stock/sales VALUE, not tax lines), split C+S paisa-exact. Returns: `Cmp08` (quarterly) + `Gstr4`/`Gstr9a` (annual, light); `GstReturnJson` deterministic offline JSON writer (⚠️ exact GSTN CMP-08/GSTR-4 envelope keys UNPUBLISHED → emits faithful structured JSON with a `schemaStatus:"pending A14"` field — **CARRY-FORWARD: A14 confirm the envelope, R7**); `Gstr1`/`Gstr3b` early-return EMPTY for composition. Io = 2 GstConfigDto members. **A10 (3 lenses): 0 CRIT / 0 HIGH / 0 MED** — core traced SOLID (Regular byte-identical, no-output/no-ITC, RCM-to-cost balances, turnover base per sub-type, v40 parity/ordinals, Io round-trip, import guard all clean); **caught + fixed 4 LOW over-collection/reconciliation:** exempt as-voucher over-included in the taxable-only base (`?? true`→`?? false`, base-rule-aware); GSTR-4 Table-6 annual ≠ Σ rounded quarterly CMP-08 → now sums the quarters (Table 5==Table 6 by construction); composition **sale-return credit notes never netted down turnover** → now signed by base type (Sales +, CreditNote −, floored at 0) + `OutwardSupplyValue` reads the sales-natural side/taxability. All regression-locked (fail pre-fix). **2119 tests green** (Ledger 990·Io 288·Sqlite 149·Desktop 692), I re-ran Release myself; Regular byte-identical (ER-13). **UI DEFERRED** to a combined RCM/CDN/advances/composition UI pass (planned NEXT + app relaunch for the user). **Carry-forwards:** CMP-02/CMP-04 opt-in/withdrawal + ITC-03/ITC-01 mid-year Regular↔Composition switch-over (**`CompositionOptInDate` is stored but does NOT yet period-scope the returns** — a mid-year conversion would tax the WHOLE FY at composition rates + suppress the whole-FY GSTR-1/3B); §50 CMP-08 interest (Table 3(iv), ₹0 — DP-34 flag-only); §47 GSTR-9A late fee; GSTR-4 4D import-of-services; §34 credit note against a composition sale; GSTN JSON envelope A14-confirm. **✅ S1·S2·S3 DONE.** **Next = combined Phase-9 UI pass, THEN P9-S4.**
- **P9-UI-pass-1 (Composition UI) — COMPLETE (UI/ViewModel/nav ONLY, no schema/engine change; still v40; committed with this pass).** Delivered: **F11 Composition config** (`GstConfigViewModel`: sub-type picker Manufacturer/Trader/Restaurant/§10(2A) + opt-in date + live advisory rate/base/threshold; `Apply()` persists + clears-on-switch-away); **CMP-08 + GSTR-4 report screens** (`Cmp08ReportViewModel`/`Gstr4ReportViewModel`, read-only projections of the pure engines, gated to Composition dealers, under Reports → Statutory Reports → **Composition Returns**); **Bill of Supply** label + §10 declaration on composition Sales (`VoucherDetailViewModel`). **DEFERRED to a focused voucher-UI follow-up** (need deep `VoucherEntryViewModel` posting integration → risks byte-identical): RCM-on-purchase-voucher + self-invoice + ledger RCM-category picker; §34 CDN entry screen; advance-receipt path; + Bill-of-Supply in the PDF print renderer (`Apex.Ledger.Io` `InvoicePdf`). **Independent headless-Skia render-verify = ALL 4 screens CLEAN** (the build-agent's GSTR-4 Table-5 quarter-column clip fix CONFIRMED; TestAppBuilder reverted + probe deleted). **A10 (code + render): 1 LOW fixed** — `GstReportSupport.IsBillOfSupply` ignored `Gst.Enabled` (a was-Composition-then-GST-off company kept the Bill-of-Supply badge while the reports/menu correctly hid) → now gated on `{Enabled:true, RegistrationType:Composition}` (regression-locked). **2126 tests green** (Ledger 991·Io 288·Sqlite 149·Desktop 698), I re-ran Release myself; Regular byte-identical (ER-13). App relaunched (isolated copy) for the user. **Next = focused RCM/§34-CDN/advances VOUCHER-UI follow-up, THEN P9-S4.**
- **P9-S4a — e-Invoice core + hybrid `IGstPortalConnector` seam (schema v40→v41) — COMPLETE (first of 2-part S4; committed with this slice).** Schema **v41** = new `einvoice_records` table + 15 companies cols (e-invoice config incl. AATO>₹5cr threshold, `gst_connector_mode`, B2C-QR cols [used in S4b], + 4 `nic_*_enc` protected-ciphertext blobs) in CreateV1 + MigrateV40ToV41 (parity-proven; AATO defaults ₹5cr/₹500cr pinned identically). **`IGstPortalConnector`** = transport interface with **NO ComputeIrn/SignQr** (ER-5 by construction) + **OfflineJsonConnector** (zero-cred default) + **GspConnector** stub (throws) + **CustomerNicDirectConnector** (deferred live seam). **`EInvoiceService` mode-agnostic**; **`EInvoiceRecord`** transition-methods-only (no IRN without an IRP response). CoverageOf (Covered/Excluded/Exempt/NotApplicable — excludes B2C/BoS/ISD/import, composition short-circuit); 24-h full-doc cancel + no doc-no reuse; uppercase doc-no; 30-day reporting-age = INDEPENDENT ₹10cr flag; GSTR-1 auto-populates from IRN-tagged docs (additive, byte-identical off). **INV-01 JSON writer** (`EInvoiceJson`) deterministic + integer-paisa + **<2MB auto-split**. **ER-16 (security) STRUCTURAL:** NIC secrets live ONLY behind `INicCredentialStore`/`SqliteNicCredentialStore` (cross-platform AES-CBC, **fresh random IV per encrypt**; fixed-pepper key = documented **obfuscation-grade placeholder — real OS-keystore/DPAPI hardening DEFERRED**); the 4 `nic_*_enc` cols are EXCLUDED from the company SELECT/INSERT AND the canonical mapper CANNOT reach the store → the secret NEVER hits the JSON/XML export (IRP receipts IRN/QR/SignedJson ARE exported, correctly). Io lossless + CompanyImportService all-or-nothing. **A10 (3 lenses): 0 CRIT / 0 HIGH** — the security + schema/Io lenses traced CLEAN (secret-never-in-export, fresh IV, GspConnector throws, mode-agnostic, v41 parity, INV-01 deterministic + safe <2MB split all verified); **caught + fixed 3 MED:** (1) `CoverageOf` ignored taxability → an exempt-only domestic B2B sale (a Bill of Supply) was misclassified Covered + could mint an IRN → now **Excluded when forward taxable value == 0** (scoped to Regular; a zero-rated EXPORT + RCM stay Covered); (2) `RecordIrpResponse` had no state guard → could resurrect a Cancelled IRN / overwrite a Generated one → now throws unless Pending/Failed; (3) `NicApiCredentials` record auto-`ToString` leaked ClientSecret/ApiPassword in PLAINTEXT (ER-16 log-leak surface) → `PrintMembers` redacts (Equals/hash still use the real values). All regression-locked (fail pre-fix). **2151 tests green** (Ledger 1001·Io 296·Sqlite 153·Desktop 701), I re-ran Release myself; byte-identical when e-invoicing off. **⚠️ CARRY-FORWARDS:** (a) INV-01 `GstRt` emitted as basis-points vs NIC's percentage → `schemaStatus:"pending A14"`; **A14 must confirm the INV-01 v1.1 envelope keys before any LIVE IRP submission (R7)**; (b) credential-at-rest AES placeholder → real OS-keystore/DPAPI hardening before the live CustomerNicDirect path ships; (c) SEZ/DeemedExport supply-category needs a party flag (deferred). **⚠️⚠️ SQLITE FLAKE IDENTIFIED = `JobWorkRoundTripTests.Job_work_data_survives_save_reload`** — intermittent `sqlite3_prepare_v2` error inside `EnsureSchema` on a shared PARALLEL test run; passes on an isolated re-run. **MUST fix (test-DB isolation / EnsureSchema race) before the S9 exit gate / Phase-9 PR CI** (it'll intermittently fail the 3-OS CI). UI DEFERRED. **Next = P9-S4b (B2C dynamic QR — Notn 14/2020-CT, >₹500cr, self-generated UPI QR, NO IRN, ER-15 never-in-IRP-flow; no schema).**
