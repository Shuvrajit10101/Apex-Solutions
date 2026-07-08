# Phase 6 — Advanced Inventory Requirements

> **Authored by A13 (Chief Architect / CA — requirements sign-off) + A14 (Tally-domain fidelity), per
> `plan.md` §5 Phase 6 and the `/software` lifecycle (requirements → design → …).** This is the up-front
> requirements slice for **Phase 6 (Advanced Inventory)**. It fills SRS §4.6 and traces every requirement to
> the feature catalog (`docs/tally-feature-catalog.md`, "catalog §11" and §9–§10 context) and its
> verification report (`docs/tally-feature-catalog-verification-report.md`, "VR item N"), grounded by A14's
> cited fidelity spec (Book = Tally-Prime-Book.pdf; Study Guide = TALLY-PRIME-STUDY-GUIDE.pdf; Tally-Book =
> Tally-Book.pdf — all git-ignored, read locally by A14, never reproduced verbatim). Requirements follow the
> "good requirement" checklist: uniquely identified, atomic, testable, unambiguous, traceable.
>
> **Fidelity & IP discipline (R4/R7):** behaviour below is described in **our own words**, grounded in the
> catalog + the `tally/` corpus. The shipped app and code — including every master screen, voucher screen,
> printed receipt, and exported artefact — must **never** contain the word "Tally"; our product is **Apex
> Solutions** (e.g. "Gateway of Apex Solutions"). This is a hard, testable gate (ER-11).
>
> **Web-verification note (R7):** Phase 6 is **inventory mechanics, not statutory law**, so it carries
> essentially **no tax-law facts to web-verify**. The one externally-verified fact A14 leaned on is the F11
> flag name **"Enable Job Order Processing"** and its auto-enablement of Material In/Out vouchers (verified
> against help.tallysolutions.com — Cluster 8). GST-on-free-supplies (Cluster 4 freebies) and GST
> assessable-value inclusion of additional-cost lines (Cluster 3) are **advanced-GST** concerns **deferred to
> Phase 9** — Phase 6 implements **no tax rules** for them.
>
> **Reading order for a resuming session:** `memory.md` → `plan.md` (Phase 6) → this file.

---

## 1. Purpose & scope

### 1.1 Purpose
Extend the Phase-3 inventory engine (masters + stock/order vouchers + valuation, at schema v15) with the
**eight advanced-inventory clusters** the catalog §11 requires, so a user can, entirely **offline** and
**keyboard-first**:

- Track stock by **batch/lot** with **manufacturing and expiry dates**, and see batch-wise stock plus an
  **age analysis of expiring batches**;
- Define a **Bill of Materials (BOM)** on a finished good and **manufacture** it via a **Manufacturing
  Journal**, auto-scaling and consuming components, carving out **by-product/co-product/scrap** value, and
  blending in **additional production costs**, so the finished good is valued correctly;
- **Apportion additional costs of purchase** (freight/insurance/cartage) into the landed stock rate by
  **quantity** or by **value**;
- Enter **zero-valued** free-goods lines and split a line into **Actual (stock)** vs **Billed (accounts/GST)**
  quantities;
- Define **Price Levels / Price Lists** with quantity slabs, discounts, and an **Applicable-From** date, and
  auto-fill the rate at sales entry;
- Define **Reorder Levels** (simple and consumption-based) and produce a **Reorder Status** report that
  suggests purchase quantities;
- Bill retail sales through a **POS** voucher type with **single- and multi-tender** payment (**Alt+I** toggle);
- Run **Job Work** (in/out orders + Material In/Out vouchers + third-party godowns) tracking material sent to
  / received from sub-contractors.

Everything here **builds on the existing inventory infrastructure** (survey B): the `StockItem`, `Godown`,
`StockGroup/Category`, `Unit`, `InventoryService`, `InventoryLedger` (on-hand), `StockValuationService`
(paisa-exact valuation), `InventoryPostingService`, and `VoucherValidator`. Phase 6 **reuses** these, never
duplicates them. The `Godown.ThirdParty` flag, the `batch_label` text carried on allocations, the
`mfg_date`/`expiry_date` forward-compat columns, and the seeded-but-inactive `MaterialIn`/`MaterialOut`/
`JobWorkInOrder`/`JobWorkOutOrder`/POS/Manufacturing-Journal base types are the seams Phase 6 activates.

Persistence adds SQLite **schema v16** over the current **v15** (`Schema.cs` `CurrentVersion = 15`) to store
batch masters, BOMs, price lists, reorder definitions, POS tender allocations, and job-work order/material
tracking (§ER-1). No existing table is rewritten.

### 1.2 In scope (Phase 6) — the eight clusters, grounded in catalog §11 (lines 260–283) + §9–§10 context

**Cluster 1 — Batches & Expiry** (catalog §11 lines 262–263; Book pp.129–132; VR item 61/70).
Batch/lot tracking per stock item with optional **Mfg date** and **Expiry date** (absolute date *or* a
resolvable period); batch-allocation sub-screen at Purchase/Sales/stock-voucher entry; batch-wise stock
report + **age-analysis of expiring stock**. Item switches: **Maintain in Batches**, **Track date of
Manufacturing**, **Use Expiry dates** (F12-item gated). Company flag: **Maintain Batch-wise details**.

**Cluster 2 — BOM & Manufacturing Journal** (catalog §11 lines 264–267; Book pp.136–141; Study Guide
pp.126–130). Named component list on a finished good (**multiple BOMs per item**), with By-Product/Co-Product/
Scrap and additional-cost lines; a **Manufacturing Journal** voucher type (base = Stock Journal, **Use as
Manufacturing Journal = Yes**) that auto-scales consumption by output qty and values the finished good.

**Cluster 3 — Additional Cost of Purchase** (catalog §11 lines 268–269; Book pp.133–135). Freight/packing/
insurance apportioned into the **landed stock rate** by **Appropriate by Quantity** or **Appropriate by
Value**; voucher-type flag **Track Additional Costs for Purchases**; the cost ledger has **Inventory values
are affected = No** yet still changes stock rate via apportionment. Also applies on stock-journal transfers.

**Cluster 4 — Zero-valued & Actual-vs-Billed quantity** (catalog §11 lines 270–272; Book pp.142–147). Item
lines with **value = 0** (free goods) — stock updates, value/GST does not — allowed **only in Sales/Purchase**;
and a **Quantity (Actual)** vs **Quantity (Billed)** split where **stock keys off Actual, value/GST off
Billed**. Company flag: **Use Separate Actual and Billed Quantity Columns**; voucher-type flag: **Allow
zero-valued transactions**.

**Cluster 5 — Price Levels / Price Lists** (catalog §11 lines 273–274; Tally-Book pp.33–35; Study Guide
line 1700). Named **Price Levels**; per-level **Price List** with quantity **slabs** (From–To → rate),
**Discount %**, and an **Applicable-From** date; auto-fill rate+discount at Sales entry from party/level.
Company flag: **Enable multiple Price Levels**.

**Cluster 6 — Reorder Levels & Reorder Status** (catalog §11 lines 275–276; Book pp.158–162). Master-level
reorder definitions per item/group/category — **Simple** (fixed) or **Advanced** (consumption over a
Period × Higher/Lower criteria) — plus **Minimum Order Quantity**; a **Reorder Status** report whose "Order
to be placed" respects Min-Order-Qty and nets pending purchase orders. This is the **full** feature over the
existing basic `ReorderStatus.cs`.

**Cluster 7 — POS (Point of Sale)** (catalog §11 lines 277–279; Study Guide pp.228–242; Tally-Book pp.66–69).
POS voucher type (base = Sales, **Use for POS invoicing = Yes**) with a default godown, print-after-save,
receipt messages/declaration; **Single-Mode** (Cash + Tendered + auto Change) or **Multi-Mode** payment
(Gift Voucher + Card + Cheque/DD + Cash) reconciling to the bill total; **Alt+I** toggles Single ⇄ Multi;
tender-ledger grouping is load-bearing.

**Cluster 8 — Job Work** (catalog §11 lines 280–281; §9 line 234; §10 line 250; VR item 15; TallyHelp).
**Job Work In/Out Order** + **Material In/Out** vouchers; **third-party godowns** ("Our stock with third
party") keep stock on our books at the worker's site; **Allow Consumption** on Material In consumes components
when finished goods are received; orders affect **neither accounts nor stock**. Company flag: **Enable Job
Order Processing** (auto-sets Use Material In/Out).

### 1.3 Explicitly deferred / boundary notes (so a gap is not mistaken for a defect)

Stated so the Phase-6 boundary is unambiguous (R6/R12):

- **GST on free supplies (Cluster 4)** and **GST assessable-value inclusion of additional-cost lines
  (Cluster 3)** — **deferred to Phase 9 (GST advanced).** Phase 6 computes stock/valuation effects only; it
  does **not** auto-tax freebies nor decide assessable-value inclusion. A zero-valued line contributes **zero
  tax** in Phase 6 (RQ-16).
- **ABC analysis, vendor lead-time, seasonal reorder** (survey B "MISSING") — **out of scope.** Phase 6's
  reorder feature is the catalog §11 set (simple/consumption-based + Min-Order-Qty + net-of-pending-PO), not a
  broader planning suite.
- **POS loyalty points / cash-drawer hardware / day-close session accumulation as a persisted "session"
  object** — **out of scope.** Phase 6 delivers the POS *voucher* (single/multi-tender, Alt+I) and a POS
  register/summary report, not a hardware till or a loyalty engine. (§DP-6.)
- **Job-work costing of the job-charge invoice** — the actual **Purchase/Sales/Journal for job charges**
  (accounting side) rides the existing Phase-1/4 voucher path; Phase 6 owns only the **material movement**
  (Material In/Out) and **order tracking**, per catalog §10 line 250 ("orders affect neither accounts nor
  stock"). Job-wise P&L costing beyond material valuation is not in scope.
- **Batch as a hard expiry block** — Tally **warns, does not block** (Cluster 1 subtlety c); Phase 6 flags
  expired/near-expiry batches in the age report and at selection but **does not prevent** issuing them.
- **Print/export of the new reports** rides the **Phase-5 report-generic print/export/config machinery**
  (ER-5 of Phase 5); Phase 6 adds the report *projections* and their config, not a new IO stack. New POS/
  batch/job-work receipts reuse the Phase-5 PDF writer.

> **Scope reconciliation (plan.md vs catalog) — see §7.** plan.md §5 Phase 6 lists exactly these eight
> clusters; the only narrowings are the deferrals above (GST interactions → Phase 9; ABC/loyalty/hardware
> out), each logged in §7 for go/no-go (R6).

---

## 2. Numbered functional requirements

> Each RQ is testable and cites its catalog/Book/VR origin. "SHALL" = mandatory Phase-6 behaviour. Money is
> **integer paisa** at the SQLite boundary and **exact decimal rupees** in-memory (ER-2). Quantities are
> exact (the existing micro-unit `qty_micro` integer convention). Every valuation figure derives from the
> **same `StockValuationService` projection** the screen shows, so on-screen, printed, and exported numbers
> can never disagree (ER-4).

### 2.1 Cluster 1 — Batches & Expiry (catalog §11 lines 262–263; Book pp.129–132)

- **RQ-1 — Batch master & per-item uniqueness.** The system SHALL store a **batch** per stock item as a
  first-class record (batch number, optional mfg date, optional expiry date, optional inward cost layer),
  replacing today's bare `batch_label` text. Batch numbers SHALL be **unique within an item**, **not
  globally** (Cluster 1 subtlety d). *(Book p.129; survey "Batch master — MISSING")*
- **RQ-2 — Item batch switches (gated).** A stock item SHALL expose three independent switches — **Maintain
  in Batches**, **Track date of Manufacturing**, **Use Expiry dates** — where "Use Expiry" MAY be on
  **without** "Track Mfg" (subtlety a). These switches SHALL appear only when the company flag **Maintain
  Batch-wise details** and the F12-item "Enable Batches / Provide Mfg & Expiry dates" config are on. *(Book
  pp.129–130)*
- **RQ-3 — Batch-allocation sub-screen at voucher entry.** On Purchase/Sales and stock vouchers, after
  item+godown+qty the system SHALL open a **batch-allocation sub-screen** capturing, per batch line: **Batch/
  Lot No.** (pick existing or "New Number"), **Mfg Dt.**, **Expiry Date**, **Quantity**, **Rate** —
  repeatable so **one item line allocates across several batches**. *(Book pp.130–132; existing
  `InventoryVoucherLine`/`VoucherInventoryLine` carry `batch_label` to extend)*
- **RQ-4 — Expiry entered as absolute date OR period.** The expiry field SHALL accept either an **absolute
  date** or a **period** (e.g. "12 Months" from mfg); a period SHALL be **resolved to a concrete date** by the
  engine (mfg + period). *(subtlety b)*
- **RQ-5 — Batch-aware on-hand & FEFO issue order.** `InventoryLedger` SHALL compute on-hand **per (item,
  godown, batch)**; on an outward movement the natural selection order SHALL be **FIFO-by-expiry (FEFO)**
  where the user does not pin a batch, and the user MAY select a specific batch to issue. *(catalog §11 line
  263; survey "batch-aware on-hand — MISSING")*
- **RQ-6 — Per-batch valuation.** Batch valuation SHALL follow the **batch's own inward rate**, so per-batch
  cost MAY differ from the item's overall average; `StockValuationService` SHALL be extended to carry
  per-batch cost layers. *(subtlety e; survey "batch costing layer — MISSING")*
- **RQ-7 — Warn-not-block on expired batches.** Selecting an expired or near-expiry batch SHALL be
  **permitted** but **flagged** (a non-blocking warning at selection); the system SHALL **not** hard-block the
  issue. *(subtlety c)*
- **RQ-8 — Batch-wise report + age analysis.** The system SHALL provide a **Batch-wise report** (per item,
  per batch: inwards/outwards/closing with mfg & expiry) and an **Age Analysis of expiring batches** (batches
  expiring within N days, past-expiry flagged distinctly). Report path nests under Reports → Inventory Books →
  Batch. *(Book p.132; catalog §11)*

### 2.2 Cluster 2 — BOM & Manufacturing Journal (catalog §11 lines 264–267; Book pp.136–141)

- **RQ-9 — BOM master (multiple per item).** A stock item SHALL store one or more named **BOMs**, each with a
  **Unit of manufacture** (block size, e.g. 1 or 10) and component lines: **Item**, **consumption godown**,
  **Type of Item** ∈ {Component, By-Product, Co-Product, Scrap}, **Quantity** (per block). Multiple BOMs per
  item SHALL be supported and selectable at manufacture. *(Book pp.136–140; Study Guide pp.126–130)*
- **RQ-10 — BOM item switch (F12-gated).** The item's **Set Components (BOM) = Yes** field SHALL appear only
  after F12-item "Set Components (BOM)"; the By-Product/Co-Product/Scrap type picker SHALL appear only after
  F12 "Define type of component for BOM". *(Book p.136)*
- **RQ-11 — Manufacturing Journal voucher type.** The system SHALL support creating a **Manufacturing
  Journal** voucher type whose **base = Stock Journal** with **Use as Manufacturing Journal = Yes** (it is
  **not** one of the predefined types; the user creates it). *(Book p.141; VoucherType flags in domain §4.1)*
- **RQ-12 — Auto-scaled component consumption.** On a Manufacturing Journal for **N** output units, component
  consumption SHALL be computed as **(per-block qty ÷ unit-of-manufacture) × N** and auto-captured; a
  Unit-of-manufacture > 1 SHALL scale correctly (subtlety e — the classic off-by-scale error). *(Book
  pp.140–141)*
- **RQ-13 — Finished-good valuation = components + additional cost − carve-outs.** The finished good's stock
  value SHALL equal **Σ(consumed component cost) + Σ(additional-cost lines) − Σ(by-product/co-product/scrap
  value)**. Additional-cost lines (labour/freight/overhead) SHALL **add to the finished-good stock value**
  (not book a separate P&L expense at manufacture); by-product/co-product/scrap value SHALL be **carved out**
  of the main finished good's cost (subtleties a, b — the classic residual-split bug). *(Book pp.138–141;
  Study Guide p.130; TOP RISK #2)*
- **RQ-14 — Inter-godown implied movement.** When the consumption godown and production godown differ, the
  Manufacturing Journal SHALL move component stock out of the consumption godown and finished-good stock into
  the production godown consistently (subtlety d). *(Book p.140)*
- **RQ-15 — Manufacturing reflects in reports.** After a Manufacturing Journal, **Stock Summary** SHALL show
  the finished good valued per RQ-13, components reduced, and by-products/scrap added; the finished-good cost
  SHALL be visible in the item cost/valuation reports. *(Book p.141)*

### 2.3 Cluster 3 — Additional Cost of Purchase (catalog §11 lines 268–269; Book pp.133–135)

- **RQ-16 — Additional-cost ledger master.** An additional-cost ledger SHALL be a Direct-Expenses ledger with
  **Inventory values are affected = No** and a **Method of Appropriation** ∈ {**Appropriate by Quantity**,
  **Appropriate by Value**}. *(Book pp.133–134)*
- **RQ-17 — Voucher-type flag.** A Purchase voucher type SHALL carry **Track Additional Costs for Purchases =
  Yes**; only then does the purchase entry expose the additional-cost line area after the item lines. *(Book
  p.134)*
- **RQ-18 — Apportionment into landed rate.** On a tracked purchase, the additional-cost amount SHALL be
  **apportioned across item lines** by the ledger's method — **by Quantity** (proportional to units) or **by
  Value** (proportional to line value) — and each item's **effective stock rate** SHALL become
  purchase-rate + its share (worked example: 10,000 → 10,300 after 1,500 ÷ 5 units). The two methods SHALL
  give different per-unit loading and be **selectable per ledger**. *(Book p.135; TOP RISK #3)*
- **RQ-19 — Inventory-affected=No yet changes stock value.** The additional-cost ledger SHALL flow into stock
  value **via the apportionment mechanism** despite "Inventory values are affected = No"; a plain freight
  expense ledger **without** the tracking flag SHALL stay purely in P&L and **not** touch the stock rate
  (subtleties b, c). *(Book p.135)*
- **RQ-20 — Additional cost on stock-journal transfers.** The same apportionment SHALL be available on
  **inter-godown Stock Journal transfers** ("Additional Cost/Expenses involved in transfer of goods"),
  loading the transferred stock's rate (subtlety d). *(Book — transfer variant)*

### 2.4 Cluster 4 — Zero-valued & Actual-vs-Billed quantity (catalog §11 lines 270–272; Book pp.142–147)

- **RQ-21 — Zero-valued line acceptance (Sales/Purchase only).** When the voucher type carries **Allow
  zero-valued transactions = Yes**, an item line with **Rate = 0 / Amount = 0** SHALL be accepted; **stock
  updates, accounting/GST value does not**. Zero-valued lines SHALL be **rejected outside Sales/Purchase**
  (not allowed in Journal/Stock Journal). This relaxes the existing `VoucherValidator` rate>0 rule **only**
  for zero-valued-enabled Sales/Purchase types (subtleties c; survey "rate>0 enforced — DP-11"). *(Book
  p.142; Note2)*
- **RQ-22 — Actual vs Billed columns (company-gated).** When the company flag **Use Separate Actual and
  Billed Quantity Columns = Yes**, each Sales/Purchase item line SHALL expose **Quantity (Actual)** and
  **Quantity (Billed)**; this feature is allowed **only in Sales/Purchase** (Note2). *(Book p.145)*
- **RQ-23 — Value off Billed, stock off Actual.** Line **value/GST** SHALL derive from **Billed** qty × rate;
  **stock movement** SHALL derive from **Actual** qty. A `value = qty × rate` shortcut is prohibited
  (subtlety a; **TOP RISK #1**). *(Book pp.145–147; Note1)*
- **RQ-24 — Free-goods drag average cost down.** Average-cost recomputation SHALL use **Actual** qty with the
  **billed/zero** value, so a free-goods inward at 0 value **drags the moving-average cost down**, matching
  Tally's averaging (subtlety b). *(Book p.147; StockValuationService AverageCost path)*
- **RQ-25 — Actual may be less than Billed.** The engine SHALL accept **Actual < Billed** (rare quality
  shortfall), not only Actual > Billed (subtlety e). *(Book p.146)*

### 2.5 Cluster 5 — Price Levels / Price Lists (catalog §11 lines 273–274; Tally-Book pp.33–35)

- **RQ-26 — Price Levels master (company-gated).** When the company flag **Enable multiple Price Levels =
  Yes**, the system SHALL let the user define named **Price Levels** (e.g. Wholesale, Retail). *(Tally-Book
  p.33; Study Guide line 1700)*
- **RQ-27 — Price List with slabs, discount, Applicable-From.** A **Price List** SHALL store, per level, per
  item: an **Applicable From** date and one or more slabs **From qty → To qty → Rate → Discount %**. Revising
  a list SHALL **add a new dated version** (older entries retained), never overwrite. *(Tally-Book pp.33–34)*
- **RQ-28 — Quantity-slab resolution (From ≥, To <).** At rate lookup the engine SHALL pick the slab whose
  **From ≤ line-qty < next-slab From** (Tally treats From as ≥ and To as < the next slab); worked example:
  Retail 0–2 → 16,000, 2–4 → 14,850, a sale of 3 resolves to 14,850 (subtlety a; **TOP RISK #5**). *(Tally-Book
  p.34)*
- **RQ-29 — Applicable-From date resolution.** The engine SHALL select the **latest** price list whose
  Applicable-From ≤ **voucher date** (subtlety b). *(Tally-Book p.34)*
- **RQ-30 — Party default level + per-voucher override + auto-fill.** A party ledger MAY carry a **default
  Price Level**; the Sales voucher SHALL expose a **Price Level** header field (auto from party, or chosen)
  and SHALL **auto-fill item Rate + Discount %** from the resolved slab, which the operator MAY **override**
  (subtleties c, d). *(Tally-Book pp.34–35)*
- **RQ-31 — Price List report.** The system SHALL provide a **Price List report** (per level, per date). Price
  lists exist for **inventory items only** and only when inventory+invoicing are on. *(Tally-Book p.35)*

### 2.6 Cluster 6 — Reorder Levels & Reorder Status (catalog §11 lines 275–276; Book pp.158–162)

- **RQ-32 — Master-level reorder definitions (dedicated Alter screen).** Reorder definitions SHALL be stored
  **per item / stock group / stock category** as **separate master records**, edited via a dedicated
  **Alter → Reorder Levels** screen (distinct from the item-creation screen). *(Book pp.158–159; subtlety e;
  survey "master-level reorder — MISSING")*
- **RQ-33 — Simple vs Advanced mode.** Each definition SHALL support a **Simple** mode (fixed Reorder Qty /
  Min Order Qty) and an **Advanced** mode where the reorder/min-order quantity is derived from **consumption
  over a Period** (Days/Weeks/Months/Years) with a **Criteria** (**Higher**/**Lower**) choosing between the
  fixed figure and the consumption-derived figure. **Alt+S** toggles Simple/Advanced Reorder; **Alt+V**
  toggles Simple/Advanced Min Qty. *(Book pp.159–161)*
- **RQ-34 — Consumption-over-period basis.** In Advanced mode the reorder qty SHALL be computed by **summing
  issues over the defined period ending at the report date** (get the period window right — subtlety b).
  *(Book p.160)*
- **RQ-35 — Higher/Lower criteria.** "**Higher**" SHALL pick the **larger** of fixed vs consumption-derived
  (safety stock); "**Lower**" the **smaller** (subtlety c). *(Book p.160)*
- **RQ-36 — Group/category rollup.** Reorder defined at item, group, **or** category level SHALL be
  resolved/rolled up correctly in the report (subtlety d). *(Book p.161)*
- **RQ-37 — Reorder Status report ("Order to be placed").** The **Reorder Status** report (path: Reports →
  Statement of Inventory → Reorder Status, pick Stock Group/Category) SHALL show, per item: **Closing stock,
  Reorder Level, pending Purchase Orders, Sales Orders due, Shortfall**, and **"Order to be placed"**. "Order
  to be placed" SHALL **not** be a naive (reorder − stock): it SHALL be **bounded below by Minimum Order
  Quantity** and **net existing pending purchase orders**, and (Advanced) use the consumption/criteria figure
  (subtlety a; **TOP RISK #4**). Purchase Order must be active for the pending-order column to be meaningful.
  **F8** filters "Reorder only"; **Ctrl+F9** raises a Purchase Order. *(Book pp.158–162; extends the basic
  `ReorderStatus.cs` which ignores Min-Order-Qty and pending POs)*

### 2.7 Cluster 7 — POS (catalog §11 lines 277–279; Study Guide pp.228–242; Tally-Book pp.66–69)

- **RQ-38 — POS voucher type.** The system SHALL support a POS voucher type (**base = Sales**, **Use for POS
  invoicing = Yes**) with a **default godown**, optional **default party** (walk-in allowed), **Print voucher
  after saving**, **print messages 1/2**, **default title**, and a **declaration**. *(Study Guide pp.229–230)*
- **RQ-39 — Single-Mode payment.** In Single-Mode the POS voucher SHALL accept one tender (e.g. Cash) with
  **Cash Tendered** and an **auto-computed Balance/Change** (= Tendered − payable). *(Study Guide p.237)*
- **RQ-40 — Multi-Mode / multi-tender payment.** In Multi-Mode the bill SHALL be splittable across **Gift
  Voucher + Credit/Debit Card (+ Card No.) + Cheque/DD (+ bank, Cheque No.) + Cash (Tendered/Change)**, each
  tender mapped to a ledger; **Σ(Gift + Card + Cheque + Cash) SHALL equal the bill total**, the **Cash line
  SHALL auto-fill the residual** payable, and **Cash Tendered − payable = Change** (worked example: tendered
  1,600, refundable 34.50). *(Study Guide pp.234–237; Tally-Book p.68; **TOP RISK #6**)*
- **RQ-41 — Tender-ledger grouping (load-bearing).** Tender ledgers SHALL post to correctly-grouped accounts:
  **Gift Voucher → Sundry Debtors**, **Credit/Debit Card → Bank Accounts or Bank OD/OCC**, **Cheque/DD → Bank
  Accounts or Bank OD/OCC**, **Cash → Cash-in-Hand**. Change SHALL be **informational** (no separate ledger
  line); the cash ledger posts the **payable**, not the tendered (subtleties b, d, e). *(Study Guide p.234
  "Note"; TOP RISK #6)*
- **RQ-42 — Alt+I toggles Single ⇄ Multi both ways.** **Alt+I** SHALL **toggle** Single ↔ Multi-Mode payment
  in **both** directions (not a one-way switch). *(Study Guide p.237; catalog §11 line 277–279; subtlety c)*
- **RQ-43 — POS GST identical to normal sales.** Because POS is base-Sales, GST SHALL compute **identically to
  a normal sales invoice** (Alt+A tax analysis available); standard Sales/GST reports SHALL include POS bills.
  *(subtlety f)*
- **RQ-44 — POS register/receipt.** The system SHALL provide a **POS register/summary** report and print the
  bill as a **retail receipt** (title + thank-you messages + declaration) via the Phase-5 PDF writer. *(Study
  Guide pp.240–242)*

### 2.8 Cluster 8 — Job Work (catalog §11 lines 280–281; §9 line 234; §10 line 250; VR item 15)

- **RQ-45 — Company flag enables Job Work (auto-sets Material In/Out).** The F11 CompanyFeature **Enable Job
  Order Processing = Yes** SHALL surface **Job Work In Order** + **Job Work Out Order** and **auto-set "Use
  Material In and Out vouchers = Yes"** (surfacing Material In / Material Out). Job-work types SHALL be reached
  via **F10 (Other Vouchers)** once enabled — **not** via a stale "Show Inactive" path. *(VR item 15;
  TallyHelp; activates the seeded-inactive base types from survey B)*
- **RQ-46 — Third-party godown keeps stock on our books.** A godown flagged **"Our stock with third party =
  Yes"** SHALL hold stock **at the worker's location but on our books** — a **location move, not a stock
  reduction** (subtlety b; extends the existing `Godown.ThirdParty` flag). *(catalog §9 line 234; Book)*
- **RQ-47 — Job Work In/Out Order (no accounts/stock effect).** A **Job Work In Order** (we are the job worker)
  and **Job Work Out Order** (we are the principal) SHALL capture: Date, Party, **Duration of Process**,
  **Nature of Processing**, **Order No.**, finished-good item + qty + due-on + location, **Tracking Components
  = Yes**, **Fill Components using** (Not Applicable = manual, or a **BOM** name → links Cluster 2), then
  component lines (item, **Track = Pending to Receive/Issue**, due-on, godown, qty, rate), and finished-good
  rate. Orders SHALL affect **neither accounts nor stock** (like PO/SO) (subtlety d). *(Book pp.83–90; catalog
  §10 line 250)*
- **RQ-48 — Material Out / Material In (stock movement).** **Material Out** SHALL dispatch raw materials to a
  job worker (or a worker dispatch finished goods back); **Material In** SHALL receive raw materials (or a
  principal receive finished goods), auto-filled from the linked **Order No(s)**, moving stock. The
  **Material In** voucher type SHALL carry **Use for Job Work = Yes** and **Allow Consumption = Yes**. *(Book
  pp.88–95)*
- **RQ-49 — Allow Consumption consumes third-party components.** When a principal receives finished goods via
  Material In with **Allow Consumption**, the components held at the **third-party godown** ("Source Location"
  shown as "Consumption Godown") SHALL be **consumed** against the finished goods, leaving no phantom raw
  material (subtlety c; **TOP RISK #7**). *(Book pp.94–95)*
- **RQ-50 — Role symmetry not hard-coded.** The engine SHALL treat Material In / Material Out symmetrically:
  the same voucher serves **principal and job worker** with opposite meaning; the role SHALL **not** be
  hard-coded (subtlety a; **TOP RISK #7**). *(catalog §11; Book pp.83–96)*
- **RQ-51 — Job Work reports.** The system SHALL provide **Job Work In Order Book**, **Job Work Out Order
  Book**, **Material In Register**, **Material Out Register** (path: Reports → Job Work Reports), showing
  materials pending / consumed / finished-goods received. *(Book pp.86, 88, 91, 95)*

### 2.9 Cross-cutting — gating, keyboard, navigation

- **RQ-52 — F11/F12 gating is real.** Every cluster's fields/vouchers/reports SHALL be **gated** by the
  correct **F11 CompanyFeature** and/or **F12 configuration** per the flags table (§3), hidden when off and
  shown when on — matching Tally's config-driven visibility (catalog §20 clone-note; domain §4.2 config
  layer). *(A14 flags summary)*
- **RQ-53 — Keyboard shortcuts bound.** The cluster shortcuts SHALL be bound and keyboard-reachable (NFR-2):
  **Alt+F7** (Stock Journal → Manufacturing Journal), **Alt+C** (inline-create component item), **Alt+I** (POS
  single/multi toggle), **Alt+A** (POS tax analysis), **Alt+S / Alt+V** (reorder simple/advanced toggles),
  **F8** (reorder-only filter), **Ctrl+F9** (Purchase Order from reorder), **F10** (Other Vouchers → Job Work /
  Material In/Out), **Alt+D** (delete a register entry). *(catalog §11/§21; Book/Study Guide shortcut refs;
  Short-Key.pdf)*
- **RQ-54 — Cascading Miller-column navigation nested under professional parents.** New masters (Batches, BOM,
  Price Lists, Reorder Levels), new voucher types (Manufacturing Journal, POS, Job Work In/Out, Material
  In/Out), and new reports (Batch, Age Analysis, Reorder Status, Price List, Job Work Reports) SHALL nest
  under the existing **Masters / Vouchers / Reports** sections in the cascading **Miller-column** hierarchy
  (e.g. Masters → Inventory Masters → Price Lists; Reports → Inventory Books → Batch), **never a flat dump**,
  reusing the approved cascade pattern. *(project professional-hierarchy + cascading-nav rules)*

---

## 3. Gating-flags matrix (F11 CompanyFeature / F12 / voucher-type / master flags) — cited

> From A14's cited flags summary. Every flag is either a `CompanyFeature` (F11), an F12 per-screen config, a
> per-`VoucherType` setting, or a per-master field. RQ-52 makes this gating testable.

| Cluster | F11 CompanyFeature | F12 / master / voucher-type flags | New voucher types |
|---|---|---|---|
| 1 Batches | **Maintain Batch-wise details** | F12-item Enable Batches / Provide Mfg & Expiry; item: Maintain in Batches / Track Mfg / Use Expiry | — |
| 2 BOM/Mfg | — | F12-item **Set Components (BOM)**, **Define type of component**; VType: base Stock Journal + **Use as Manufacturing Journal** | **Manufacturing Journal** |
| 3 Add'l cost | — | VType(Purchase): **Track Additional Costs**; ledger: **Method of Appropriation**, Inventory affected = No | — |
| 4 Zero / Actual-Billed | **Use Separate Actual & Billed Qty Columns** (for A/B) | VType: **Allow zero-valued transactions** (for zero) | — |
| 5 Price levels | **Enable multiple Price Levels** | party ledger: default Price Level | — |
| 6 Reorder | — (Purchase Order active) | master: Reorder Qty / Min Order Qty (Simple/Advanced), Period, Higher/Lower | — |
| 7 POS | — | VType(Sales): **Use for POS invoicing** (+ optional POS Class tender-ledger table) | **POS voucher type** |
| 8 Job Work | **Enable Job Order Processing** (auto-sets Use Material In/Out) | VType(Material In): **Use for Job Work** + **Allow Consumption**; godown: **Our stock with third party** | **Job Work In/Out Order, Material In, Material Out** |

*(Source: A14 flags summary; catalog §11; Book/Study Guide/Tally-Book page refs inline in §2; TallyHelp for
the Job-Work F11 name.)*

---

## 4. Engineering rules (ER — hard constraints)

- **ER-1 — Idempotent v15→v16 migration.** Phase 6 SHALL add SQLite **schema v16** via a `MigrateV15ToV16`
  block that runs inside one transaction bumping `schema_version` to 16, using only new `CREATE TABLE` /
  `CREATE INDEX` (batch_masters, bill_of_materials + bom_lines, additional_cost / landed_cost lines,
  price_lists + price_list_lines, reorder_definitions, pos_tender_allocations, job_work_orders +
  material_in/out lines), **never** `ALTER`-ing or rewriting existing rows destructively. Any needed new
  columns on existing tables (e.g. actual/billed qty on `inventory_allocations`) SHALL be **additive** with
  NULL/0 defaults. A fresh DB is stamped straight to v16 via the consolidated `CreateV1` DDL, following
  `MigrateV1ToV2 … MigrateV14ToV15` exactly. **Bump `Schema.CurrentVersion` to 16.** Existing round-trip
  tests already assert against `Schema.CurrentVersion` (not a literal 15), so no hard-coded version test needs
  repointing — but any that appears SHALL reference `Schema.CurrentVersion`. *(mirrors
  `src/Apex.Persistence.Sqlite/Schema.cs` `CurrentVersion = 15`; survey migration recipe)*
- **ER-2 — Money = exact decimal rupees in-memory, integer paisa at the boundary.** Every amount (component
  cost, landed rate, price-list rate, tender amount, additional cost) SHALL be an **exact decimal-rupee**
  value in-memory (via `Money`) and stored as **integer paisa** at the SQLite boundary — **never a float**.
  Apportionment (RQ-18) and residual carve-out (RQ-13) SHALL be computed with exact arithmetic and a
  **deterministic paisa rounding** that reconciles (Σ apportioned shares = total, to the paisa). *(NFR-3;
  `Money.cs`; TOP RISKS #2/#3)*
- **ER-3 — Quantities exact (micro-unit integers).** All quantities SHALL use the existing exact `qty_micro`
  integer convention; batch/BOM/actual-billed/price-slab math SHALL be exact (no float qty). Compound-unit
  factors SHALL remain exact integers. *(survey; existing `Unit` compound factors)*
- **ER-4 — One projection: screen ⇄ report ⇄ print/export consistency.** Batch/BOM/landed-cost/reorder
  valuation figures SHALL be produced by the **same `StockValuationService` / `InventoryLedger` projection**
  the UI shows, so Stock Summary, Balance Sheet Stock-in-Hand, printed receipts, and exports cannot disagree.
  There SHALL be **one** source of each figure. *(NFR-3; Phase-5 ER-4 parallel)*
- **ER-5 — Reuse the existing inventory infra; do not duplicate.** Phase 6 SHALL build **on** `StockItem`,
  `Godown` (incl. `ThirdParty`), `StockGroup/Category`, `Unit`, `InventoryService`, `InventoryLedger`,
  `StockValuationService`, `InventoryPostingService`, `ItemInvoiceStock`, and `VoucherValidator` — extending
  them (batch-aware on-hand, per-batch cost layers, BOM explosion, landed-cost apportionment, actual/billed
  split) rather than creating parallel engines. The seeded-inactive base types (Material In/Out, Job Work In/
  Out Order, POS, Manufacturing Journal) SHALL be **activated**, not re-declared. *(survey B; implementation.md
  reuse)*
- **ER-6 — Framework-agnostic engine.** All new domain models, services, and report projections (Batch, BOM,
  landed cost, price list, reorder, POS tender split, job-work) SHALL live in **`Apex.Ledger`** with **no
  Avalonia and no SQLite dependency** — persistence via the existing repository/port pattern; UI via
  ViewModels only. Every valuation/apportionment/reorder rule SHALL be **unit-tested headlessly** in
  `Apex.Ledger.Tests`. *(NFR-6; plan.md §3 framework-agnostic core)*
- **ER-7 — Posting stays engine-routed & invariant-safe.** Manufacturing Journal, POS, Material In/Out, and
  zero-valued/actual-billed vouchers SHALL post **through `InventoryPostingService` + `VoucherValidator`**
  (the same path the keyboard UI uses), preserving existing invariants (no-negative-stock guard where it
  applies, item-invoice Dr/Cr pairing, Stock-Journal source=dest balance). The **rate>0** rule SHALL be
  relaxed **only** for zero-valued-enabled Sales/Purchase lines (RQ-21), not globally. *(survey
  `VoucherValidator` ER-6; DP-11)*
- **ER-8 — No "Tally" in any output.** No master screen label, voucher screen, POS receipt, batch/reorder/
  job-work report, PDF metadata, or exported cell/field SHALL contain the word "Tally"; the product reads
  **Apex Solutions** ("Gateway of Apex Solutions"). This is a **tested** gate (assert over produced UI text /
  bytes). *(project rule; strongest concern for customer-facing screens & receipts)*
- **ER-9 — Keyboard-first cascade Miller-column UI under professional parents.** All new screens SHALL be
  reachable keyboard-only (NFR-2) and nested in the cascading Miller-column hierarchy under Masters / Vouchers
  / Reports (RQ-54); no flat menu dump; clean per-column rendering; prior panes persist. *(project cascade +
  hierarchy rules)*
- **ER-10 — Deterministic, culture-invariant computation.** Apportionment rounding, price-slab boundary
  resolution, consumption-period windows, and expiry period→date resolution SHALL be **culture-invariant and
  deterministic** so tests are reproducible in CI and exports are byte-stable. *(NFR-3; Phase-5 ER-10 parallel)*
- **ER-11 — De-branded, self-contained, cross-platform.** New IO (POS receipt, batch/job-work report print/
  export) SHALL reuse the **Phase-5 hand-rolled dependency-free writers** (no new NuGet), run headless and
  cross-platform, with **no file dialog / message box in the engine** (those live only in the thin Avalonia
  layer). *(NFR-1/5; Phase-5 ER-3/ER-8/ER-12)*
- **ER-12 — TDD Red→Green, adversarial review every slice.** Every RQ SHALL be delivered **test-first**
  (failing test → code to green → refactor); every slice SHALL get a **Code Reviewer + Verification/
  Completeness Critic** adversarial pass before merge — heeding the recorded lesson that a green gate can hide
  real bugs (~10 caught last phase). Reproduce any suspected bug with a throwaway test first. *(R8/R10;
  superpowers:test-driven-development; memory lesson)*
- **ER-13 — Zero regression to Phases 1–5.** Existing report figures SHALL be unchanged for non-advanced
  data; **Robert, Bright, and the GST golden set SHALL stay green**, and every existing inventory report
  SHALL render the same numbers it did before (§6). Bright SHALL be **extended** (not broken) with advanced-
  inventory flows. *(R8; NFR-3)*

---

## 5. Decision points (DP) — defaults recommended for A13/user approval

> Each DP states a genuine design choice or catalog-silent behaviour, our recommended default (grounded in the
> codebase + offline-desktop ethos + the corpus), options with trade-offs, and a rationale. **These require
> the user's approval before implementation** (R12).

- **DP-1 — Default batch-consumption order when the user does not pin a batch.**
  *Question:* which batch does an outward movement consume by default?
  *Options:* **(a) FEFO** (First-Expiry-First-Out) — sell soonest-to-expire first; matches pharma/perishable
  intent, minimises waste, but needs expiry on every batch. **(b) FIFO** (First-In-First-Out) by inward date
  — simpler, matches non-expiry items, but can ship stock that expires sooner. **(c) Force manual batch pick
  every time** — most faithful to "user selects", but slows entry.
  *Recommended default:* **FEFO when expiry is tracked, else FIFO-by-inward, with the user always able to pin a
  batch manually** (RQ-5). *Rationale:* matches Tally's "natural selection order is FIFO-by-expiry" (Cluster 1
  behaviour) while degrading gracefully for non-expiry items; manual override preserves control.

- **DP-2 — Additional-cost apportionment rounding remainder allocation.**
  *Question:* after splitting a cost by quantity/value, the last paisa may not divide evenly — where does the
  remainder go?
  *Options:* **(a) Largest-remainder to the largest line** — standard, keeps the biggest line closest to
  exact. **(b) Remainder to the last line** — simplest, deterministic, but arbitrary. **(c) Spread ±1 paisa
  across the first N lines** — most even, more code.
  *Recommended default:* **largest-remainder method, deterministic tie-break by line order** (ER-2/ER-10).
  *Rationale:* guarantees Σ shares = total to the paisa (RQ-18), is deterministic for byte-stable tests, and
  is the least surprising loading. (Tally's exact tie-break is not documented in the corpus — this is a
  catalog-silent choice, flagged.)

- **DP-3 — By-Product / Co-Product / Scrap valuation basis in the Manufacturing Journal.**
  *Question:* at what value are by-products/co-products/scrap carved out of the finished-good cost?
  *Options:* **(a) User-entered rate/percentage per line** (the Book's "Quantity / Rate(%)" field) — faithful,
  explicit, matches the corpus. **(b) Auto at standard/last cost of that item** — less input, but can drift
  from the operator's intent. **(c) Zero (scrap = free)** — simplest, but wrong for co-products with real
  value.
  *Recommended default:* **user-entered rate/% per carve-out line (a), defaulting to the item's standard cost
  where blank** (RQ-13). *Rationale:* the corpus shows an explicit rate/% field; carving out the operator's
  stated value is the faithful behaviour and makes the residual-split testable (TOP RISK #2).

- **DP-4 — POS tender-ledger mapping: fixed convention vs per-company Voucher Class.**
  *Question:* how are the four tender ledgers (Gift/Card/Cheque/Cash) chosen?
  *Options:* **(a) Optional POS Voucher Class pre-maps the tender ledgers** (the Study-Guide/Tally-Book
  approach) — most faithful, one-time setup, correct grouping guaranteed. **(b) Pick each tender ledger
  ad-hoc at entry** — flexible, but risks wrong grouping every bill. **(c) Hard-code four seeded tender
  ledgers** — fast, but not faithful and fragile.
  *Recommended default:* **support a POS Voucher Class that pre-maps tender ledgers (a), with ad-hoc override
  at entry** (RQ-41). *Rationale:* the corpus's POS Class is exactly this; pre-mapping enforces the
  load-bearing grouping (Gift→Sundry Debtors, Card/Cheque→Bank, Cash→Cash-in-Hand — TOP RISK #6) while
  allowing per-bill flexibility.

- **DP-5 — Consumption-period window for Advanced reorder.**
  *Question:* over what window is consumption summed for the consumption-based reorder qty?
  *Options:* **(a) The last N Days/Weeks/Months/Years ending at the report date** (rolling window) — matches
  "consumption over the defined period ending at the report date" (Cluster 6 subtlety b). **(b) Calendar
  period (this month/quarter)** — simpler to explain, but not what the corpus says. **(c) Since financial-year
  start** — easy, but ignores the Period × Higher/Lower design.
  *Recommended default:* **rolling window of N units ending at the report date (a)** (RQ-34). *Rationale:* it
  is the literal corpus behaviour and makes "Order to be placed" reflect recent demand; deterministic given a
  report date (ER-10).

- **DP-6 — POS "session" scope in Phase 6.**
  *Question:* do we model a persisted POS day-close **session** object (running drawer, session totals)?
  *Options:* **(a) No session object — POS is just a Sales voucher; provide a POS register/summary report**
  (day filter) for the day-close view. **(b) Full session object** with open/close, drawer float, and
  per-session tender totals — richer retail feel, much more scope. **(c) Session-lite** — a per-day tender
  summary persisted.
  *Recommended default:* **(a) no persisted session; deliver the POS voucher + a POS register/summary report**
  (RQ-44; §1.3 deferral). *Rationale:* the catalog §11 POS requirement is the multi-tender *voucher* and
  Alt+I toggle, not a till/drawer subsystem; a register report gives the day-close view without a new stateful
  entity. Loyalty/hardware stay out (§1.3).

- **DP-7 — Actual-vs-Billed persistence shape.**
  *Question:* how to store the actual/billed split on the existing allocation row?
  *Options:* **(a) Add `actual_qty_micro` + `billed_qty_micro` columns to `inventory_allocations` /
  `voucher_inventory_lines`**, defaulting billed=actual when the feature is off — additive, one row per line,
  simplest join. **(b) A separate `qty_split` side-table** — cleaner if rare, but an extra join on every
  valuation. **(c) Overload `batch_label`-style text** — rejected (lossy, not numeric).
  *Recommended default:* **(a) additive nullable columns with billed defaulting to actual** (ER-1/RQ-22).
  *Rationale:* it is additive (safe v15→v16 migration), keeps stock-vs-value on one row for the valuation
  engine (TOP RISK #1), and defaults transparently for all existing data.

- **DP-8 — Batch cost layer vs item valuation method interaction.**
  *Question:* when an item has both a valuation method (Avg/FIFO/…) and batches, which drives cost?
  *Options:* **(a) Per-batch inward rate is authoritative for that batch's issues; the item method aggregates
  across batches for item-level reports** — matches "batch valuation follows the batch's own inward rate"
  (Cluster 1 subtlety e). **(b) Item method ignores batches** — simpler, but loses per-batch cost fidelity.
  *Recommended default:* **(a) batch inward rate authoritative per batch; item method aggregates** (RQ-6).
  *Rationale:* the corpus is explicit that per-batch cost can differ from the item average; batch-level cost
  is the faithful behaviour and the aggregate still reconciles into Stock Summary (ER-4).

---

## 6. Bright / Robert / regression impact (Phase 6 must NOT change existing figures)

> Phase 6 adds advanced-inventory posting/valuation; it changes **no** existing posting rule for
> non-advanced data. All existing fixtures SHALL stay green, and Phase 6 adds its own advanced fixtures/gates.

Phase 6 SHALL prove, to the paisa (NFR-3):

- **PR-1 — Robert stays green (accounts-only).** The 13-voucher **Robert** fixture SHALL reproduce its known
  Trial Balance / P&L / Balance Sheet totals **unchanged** — advanced-inventory code SHALL not touch an
  accounts-only company. *(R8; ER-13)*
- **PR-2 — Bright stays green + extended.** The **Bright** fixture (closing stock ₹15,000, BS Stock-in-Hand
  ₹15,000, gross profit ₹15,000, net profit −₹1,000) SHALL remain green under the batch-aware engine, and
  SHALL be **extended** with at least one batch movement, one Manufacturing Journal, and one Material In/Out
  flow that reconcile into Stock Summary + Balance Sheet **to the paisa**. *(R8; RQ-1/9/48)*
- **PR-3 — Batch sale picks the right batch & flags expiry (hard gate).** A batch-tracked item purchased into
  two batches (different expiry) then sold SHALL, on default issue, consume the **FEFO** batch (DP-1), value
  the issue at **that batch's cost** (RQ-6), and the **age-analysis report SHALL flag** the near/past-expiry
  batch distinctly; selecting an expired batch SHALL **warn, not block** (RQ-5/7/8). *(hard gate)*
- **PR-4 — Manufacture a finished good (hard gate).** A Manufacturing Journal producing **N** units from a BOM
  SHALL: **consume components** scaled by N and unit-of-manufacture (RQ-12); value the finished good =
  **Σ component cost + Σ additional cost − Σ by-product/co-product/scrap value** (RQ-13); book **additional
  costs into stock value, not P&L**; and **reconcile into Stock Summary and the Balance Sheet to the paisa**.
  *(hard gate; TOP RISK #2)*
- **PR-5 — Additional cost of purchase loads the landed rate (hard gate).** A purchase with a tracked
  additional-cost ledger SHALL raise each item's stock rate by its apportioned share — **by Quantity** and
  **by Value** giving the documented different loadings (RQ-18: 10,000 → 10,300 worked example) — and the
  cost ledger (Inventory affected = No) SHALL still change stock value while a plain freight ledger does not
  (RQ-19). *(hard gate; TOP RISK #3)*
- **PR-6 — Actual-vs-Billed / zero-valued split (hard gate).** Receiving **60 Actual / 50 Billed** SHALL post
  **stock +60** and **payable/GST on 50** (RQ-23); a **zero-valued free-goods** inward SHALL update stock and
  **drag the moving-average cost down** (RQ-24) and contribute **zero tax** (§1.3); zero-valued SHALL be
  rejected outside Sales/Purchase (RQ-21). *(hard gate; TOP RISK #1)*
- **PR-7 — Price list auto-fills the right slab (hard gate).** With Retail slabs 0–2 → 16,000 and 2–4 →
  14,850, a sale of **qty 3** on the Retail level SHALL auto-fill **14,850** (From ≥, To < — RQ-28), the
  **latest Applicable-From ≤ voucher date** list SHALL win (RQ-29), and the operator override SHALL stick
  (RQ-30). *(hard gate; TOP RISK #5)*
- **PR-8 — Reorder Status suggests the correct qty (hard gate).** The Reorder Status report SHALL compute
  **shortfall** and **"Order to be placed"** **bounded by Minimum Order Quantity** and **net of pending
  purchase orders**, with Advanced mode using **consumption-over-period × Higher/Lower** (RQ-37; Book "Order
  to be Placed 25 Pcs" worked example). *(hard gate; TOP RISK #4)*
- **PR-9 — POS multi-tender balances (hard gate).** A POS bill split across **Cash + Card (+ Cheque + Gift)**
  SHALL satisfy **Σ tenders = bill total**, auto-fill the **Cash residual**, compute **Change = Tendered −
  payable**, post each tender to the **correctly-grouped ledger** (Gift→Sundry Debtors, Card/Cheque→Bank,
  Cash→Cash-in-Hand), and **Alt+I SHALL toggle both ways** (RQ-40/41/42). *(hard gate; TOP RISK #6)*
- **PR-10 — Job Work third-party consumption (hard gate).** Sending raw material to a job worker via
  **Material Out** SHALL keep stock **on our books at the third-party godown** (RQ-46); receiving finished
  goods via **Material In with Allow Consumption** SHALL **consume** the third-party components leaving **no
  phantom raw material** (RQ-49); the **orders** SHALL move neither accounts nor stock (RQ-47); and the same
  types SHALL work symmetrically for principal vs worker (RQ-50). *(hard gate; TOP RISK #7)*
- **PR-11 — v15→v16 migration is lossless & idempotent.** A legacy **v15** database SHALL migrate to **v16**
  preserving every existing row, and a fresh DB SHALL stamp straight to v16; `schema_version` SHALL read
  `Schema.CurrentVersion` (= 16) after either path (ER-1). *(hard gate)*

> **Standing rule (R8):** **Robert & Bright stay green in every subsequent phase** — any red = **stop** at the
> last committed checkpoint (recorded lesson). New tests are added as bugs are found (pesticide-paradox guard).

---

## 7. Plan-vs-catalog divergences & narrowings (R6 — logged for go/no-go)

> Anything where plan.md, the catalog, and the existing code disagree, or where scope was narrowed/deferred,
> with the reason. Surface at the Phase-6 kickoff gate (R12).

- **D-1 — GST interactions deferred to Phase 9.** catalog §11's Cluster 3 (additional-cost inclusion in GST
  assessable value) and Cluster 4 (GST on free supplies / freebies) touch GST law. **Narrowed:** Phase 6
  implements the **stock/valuation** effects only and does **not** implement any GST rule for them; those ride
  Phase 9 (GST advanced). *Reason:* keeps Phase 6 to inventory mechanics (no law drift), matches A14's
  explicit deferral; plan.md Phase 9 owns advanced GST.
- **D-2 — Reorder scope narrowed to the catalog set.** The survey's capability map lists ABC analysis, vendor
  lead-time, and seasonal reorder as "MISSING". **Narrowed:** these are **out of scope**; Phase 6 delivers the
  catalog §11 reorder feature (Simple/Advanced consumption-based + Min-Order-Qty + net-of-pending-PO + group/
  category rollup). *Reason:* catalog §11 does not require ABC/lead-time; they are a separate planning suite,
  not advanced inventory. (plan.md §5 Phase 6 lists only "Reorder (+status report)".)
- **D-3 — POS narrowed to voucher + register (no session/loyalty/hardware).** The survey lists "drawer/session
  tracking, loyalty points" as MISSING. **Narrowed** to the catalog §11 POS voucher (single/multi-tender,
  Alt+I) + a POS register/summary report (DP-6). *Reason:* catalog §11 requires the multi-mode payment voucher
  and Alt+I toggle, not a till subsystem; loyalty/hardware are not in the catalog.
- **D-4 — Job-work costing narrowed to material movement + order tracking.** **Narrowed:** Phase 6 owns
  Material In/Out stock movement, third-party godown ownership, and order registers; the **accounting** for
  job charges rides the existing Purchase/Sales/Journal path (catalog §10 line 250: orders affect neither
  accounts nor stock). *Reason:* faithful to the corpus; avoids inventing a job-costing subsystem the catalog
  does not specify.
- **D-5 — Books' "Show Inactive" path for Job Work is stale — use F11.** The Tally books show reaching Job
  Work via "F10 → Show Inactive". **Corrected (VR item 15 + web-verified TallyHelp):** Job Work is gated by
  the **F11 "Enable Job Order Processing"** feature (which auto-enables Material In/Out), reached via **F10
  (Other Vouchers)** once active. *Reason:* the authoritative gating is the F11 feature, not the stale
  shortcut (A14 fidelity ruling; RQ-45).
- **D-6 — Batch expiry warns, does not block.** A naive reading might hard-block expired-batch sales.
  **Clarified (Cluster 1 subtlety c):** Tally **warns via the age report and at selection but does not block**
  the issue. *Reason:* faithful behaviour; a hard block would diverge from Tally (RQ-7).
- **D-7 — Schema numbering.** The survey's example DDL suggested "v16 (or higher per feature count)". **Fixed
  at a single v16 bump** for the whole phase (one migration adding all new tables/columns), following the
  established one-migration-per-phase precedent (v9→v15). *Reason:* simpler migration surface, one atomic
  upgrade, matches prior pattern; no need for per-cluster version bumps. (ER-1.)

---

## 8. Ordered slice plan (the /loop executes these, in order)

> Each slice = a coherent vertical (engine + UI + tests) tied to specific RQs, sequenced by **dependency**
> (masters before vouchers; batches before batch-aware manufacturing; the shared schema bump first). Each
> slice ends at the R9 gate (tests green shown → adversarial review → A12 commits/pushes → run the app →
> memory.md updated). "Schema bump?" is **Yes** only for the slice that lands the v16 migration.

- **Slice 1 — Schema v16 + Batch master & batch-aware engine.**
  *Scope:* land the **`MigrateV15ToV16`** migration + `Schema.CurrentVersion = 16` (all new tables/columns,
  additive) — this is the phase's single schema bump; then the **Batch master**, item batch switches
  (gated), batch-allocation sub-screen, expiry period→date resolution, **batch-aware on-hand (FEFO)**,
  per-batch cost layers, warn-not-block expiry, and the **Batch-wise + Age-Analysis reports**.
  *RQs:* RQ-1..RQ-8, RQ-52 (batch flags), RQ-54 (nav). *Schema bump?* **Yes (v16 — the only one).*
  *Exit check:* **PR-3** (FEFO batch pick + expiry flag) and **PR-11** (migration lossless/idempotent) green;
  Robert/Bright green (PR-1/2 baseline).

- **Slice 2 — BOM master + Manufacturing Journal.**
  *Scope:* **BOM master** (multiple per item, unit-of-manufacture, component/by-product/co-product/scrap
  lines, F12-gated); **Manufacturing Journal** voucher type (base Stock Journal + Use as Manufacturing
  Journal); **BOM explosion + auto-scaled consumption**; finished-good valuation = components + additional
  cost − carve-outs; inter-godown movement; Stock Summary reflection. Depends on Slice 1 (batch-aware
  valuation, per-batch cost).
  *RQs:* RQ-9..RQ-15, RQ-53 (Alt+F7/Alt+C). *Schema bump?* No (tables added in Slice 1).
  *Exit check:* **PR-4** (manufacture reconciles to the paisa) green; Bright extended with a manufacture flow.

- **Slice 3 — Additional Cost of Purchase.**
  *Scope:* additional-cost **ledger master** (Direct Expenses, Inventory-affected=No, Method of Appropriation);
  Purchase voucher-type **Track Additional Costs** flag; **apportionment engine** (by Quantity / by Value) into
  the landed stock rate with deterministic paisa rounding (DP-2); stock-journal-transfer variant.
  *RQs:* RQ-16..RQ-20. *Schema bump?* No.
  *Exit check:* **PR-5** (landed rate by qty & by value; cost ledger changes stock, freight ledger does not).

- **Slice 4 — Zero-valued & Actual-vs-Billed quantity.**
  *Scope:* voucher-type **Allow zero-valued** (relax rate>0 only there); company **Actual/Billed columns**
  flag; **actual/billed persistence** (additive columns, DP-7); **value off Billed, stock off Actual**;
  free-goods **drags average cost down**; Sales/Purchase-only guard; Actual < Billed allowed.
  *RQs:* RQ-21..RQ-25, RQ-52. *Schema bump?* No (columns added in Slice 1 migration).
  *Exit check:* **PR-6** (60 Actual / 50 Billed splits stock vs value; zero-valued drags average, zero tax).

- **Slice 5 — Price Levels / Price Lists.**
  *Scope:* company **Enable multiple Price Levels** flag; **Price Levels** master; **Price List** with
  quantity slabs, discount %, **Applicable-From** dated versions; **slab resolution (From ≥, To <)** +
  **latest-date-≤-voucher** resolution; party default level; Sales header **Price Level** field with
  auto-fill rate/discount + override; Price List report.
  *RQs:* RQ-26..RQ-31. *Schema bump?* No.
  *Exit check:* **PR-7** (qty 3 → 14,850 slab; latest applicable-from wins; override sticks).

- **Slice 6 — Reorder Levels + Reorder Status (full).**
  *Scope:* master-level **reorder definitions** (item/group/category) via a dedicated **Alter → Reorder
  Levels** screen; **Simple vs Advanced** (Period × Higher/Lower); **consumption-over-period** window (DP-5);
  **Min-Order-Qty** + **net-of-pending-PO** in "Order to be placed"; group/category rollup; the full
  **Reorder Status** report (Alt+S/Alt+V/F8, Ctrl+F9 → PO). Extends the basic `ReorderStatus.cs`.
  *RQs:* RQ-32..RQ-37, RQ-53. *Schema bump?* No.
  *Exit check:* **PR-8** (correct shortfall + qty-to-order, bounded by MOQ, net of pending POs).

- **Slice 7 — POS (single/multi-tender, Alt+I).**
  *Scope:* **POS voucher type** (base Sales, Use for POS invoicing, default godown/party, print-after-save,
  messages/declaration); optional **POS Voucher Class** pre-mapping tender ledgers (DP-4); **Single-Mode**
  (Cash Tendered/Change) and **Multi-Mode** (Gift/Card/Cheque/Cash) with **Σ tenders = total**, cash residual,
  change; **tender-ledger grouping**; **Alt+I** two-way toggle; POS register/summary + retail-receipt print.
  *RQs:* RQ-38..RQ-44, RQ-53 (Alt+I/Alt+A). *Schema bump?* No (tender-allocation table added in Slice 1).
  *Exit check:* **PR-9** (multi-tender balances, correct grouping, Alt+I toggles both ways).

- **Slice 8 — Job Work (orders + Material In/Out + third-party godowns).**
  *Scope:* F11 **Enable Job Order Processing** (auto-sets Use Material In/Out; activate the seeded-inactive
  types); godown **"Our stock with third party"**; **Job Work In/Out Order** (no accounts/stock effect,
  Tracking Components via BOM or manual); **Material Out / Material In** stock movement with **Use for Job
  Work + Allow Consumption**; **third-party consumption** (no phantom material); **role symmetry**; the four
  **Job Work reports**. Depends on Slice 2 (BOM link for component fill).
  *RQs:* RQ-45..RQ-51, RQ-53 (F10/Alt+D), RQ-54. *Schema bump?* No (tables added in Slice 1).
  *Exit check:* **PR-10** (third-party stock stays on our books, Material-In consumes components, orders move
  nothing).

- **Slice 9 — Phase-6 regression & fixture hardening (gate).**
  *Scope:* extend **Bright** with the full advanced-inventory set (batch + manufacture + additional cost +
  actual/billed + price list + reorder + POS + job work) as a persisted regression fixture; re-run Robert +
  Bright + GST golden; de-brand sweep over all new screens/receipts/reports (ER-8); run the real app end-to-end
  for each cluster with evidence.
  *RQs:* consolidation of all; **ER-13**. *Schema bump?* No.
  *Exit check:* **PR-1..PR-11** all green (shown); no "Tally" in any output; app run recorded; A13 sign-off.

---

## 9. Sign-off checklist (A13 / CA ticks to close Phase 6)

> A13 signs only when **every** box is ticked with shown evidence (tests green displayed, app run, review
> passed, de-branded, committed by A12, memory.md updated).

- ☐ **1. Batches & Expiry (RQ-1..RQ-8)** — batch master + per-item uniqueness; item switches (gated); batch
  sub-screen; period→date; FEFO on-hand; per-batch cost; warn-not-block; batch + age reports.
- ☐ **2. BOM & Manufacturing Journal (RQ-9..RQ-15)** — BOM master (multiple, F12-gated); Manufacturing
  Journal type; auto-scaled consumption; FG value = components + add'l cost − carve-outs; inter-godown; Stock
  Summary.
- ☐ **3. Additional Cost of Purchase (RQ-16..RQ-20)** — cost ledger + method; Track-Additional-Costs flag; by
  Quantity / by Value landed rate; Inventory-affected=No still changes stock; transfer variant.
- ☐ **4. Zero-valued & Actual-vs-Billed (RQ-21..RQ-25)** — zero-valued (Sales/Purchase only); Actual/Billed
  columns; value off Billed / stock off Actual; free-goods drag average; Actual < Billed.
- ☐ **5. Price Levels / Lists (RQ-26..RQ-31)** — levels + lists; slabs (From ≥, To <) + discount +
  Applicable-From; latest-date resolution; party default + override + auto-fill; Price List report.
- ☐ **6. Reorder (RQ-32..RQ-37)** — master defs (item/group/category); Simple/Advanced; consumption window;
  Min-Order-Qty + net-pending-PO in "Order to be placed"; Reorder Status report.
- ☐ **7. POS (RQ-38..RQ-44)** — POS type; single/multi tender; Σ tenders = total + cash residual + change;
  tender-ledger grouping; Alt+I two-way; POS register + receipt.
- ☐ **8. Job Work (RQ-45..RQ-51)** — F11 enable (auto Material In/Out); third-party godown; In/Out orders
  (no accounts/stock); Material In/Out movement; Allow-Consumption; role symmetry; four reports.
- ☐ **9. Cross-cutting (RQ-52..RQ-54)** — F11/F12 gating real; shortcuts bound (Alt+F7/C/I/A/S/V, F8, Ctrl+F9,
  F10, Alt+D); cascade Miller-column nav under Masters/Vouchers/Reports.
- ☐ **10. Engineering rules (ER-1..ER-13)** — idempotent v15→v16 (CurrentVersion=16); exact decimal-rupee /
  integer-paisa; exact qty; one-projection consistency; reuse-not-duplicate; framework-agnostic engine;
  engine-routed posting; no "Tally"; keyboard-first cascade UI; deterministic culture-invariant; de-branded
  self-contained cross-platform IO; TDD + adversarial review; zero regression.
- ☐ **11. Decision points (DP-1..DP-8)** — each default **approved by the user** (R12) or the approved variant
  recorded in `memory.md`; the §7 divergences (GST→Phase 9; ABC/loyalty/hardware out; job-costing narrowed;
  Show-Inactive→F11; warn-not-block; single v16 bump) explicitly approved.
- ☐ **12. Fixture / regression gate (PR-1..PR-11)** — Robert & Bright green (Bright extended); all ten
  advanced hard gates green; v16 migration lossless/idempotent. *(hard gate)*
- ☐ **13. Tests green — shown.** Full unit + integration suite green (displayed), including Robert, Bright,
  the GST golden set, and the new advanced-inventory tests, per R9.
- ☐ **14. Review passed.** Code Reviewer + Verification/Completeness Critic adversarial pass, no open findings;
  fidelity gap list re-derived (R9/R10).
- ☐ **15. De-branded.** No occurrence of "Tally" in any shipped app UI, code, or produced artefact (screens/
  POS receipts/reports/exports); product reads "Apex Solutions" throughout — asserted.
- ☐ **16. App run.** The real app launched; each cluster exercised (batch sale, manufacture, additional-cost
  purchase, actual/billed, price-list sale, reorder status, POS multi-tender, job-work in/out) — evidence
  recorded (R9/R11).
- ☐ **17. Committed by the GitHub Expert.** Small conventional commits tied to Phase-6 slices, pushed by A12
  (the **only** git actor, R4); `memory.md` updated (R5), including the DP/divergence decisions.

---

*Traceability: every RQ cites its catalog §11 line(s) + Book / Study-Guide / Tally-Book page(s) + VR item, via
A14's cited fidelity spec; the gating matrix (§3) maps each cluster to its F11/F12/voucher-type/master flag;
ERs cite `Schema.cs` (`CurrentVersion = 15` → v16), `Money.cs` (exact decimal-rupee / integer-paisa), and the
existing inventory infra (`StockItem`, `Godown.ThirdParty`, `InventoryLedger`, `StockValuationService`,
`InventoryPostingService`, `VoucherValidator`, the seeded-inactive base types, the `batch_label` /
`mfg_date`/`expiry_date` forward-compat columns); the fixture/regression gate maps to
`tests/Apex.Ledger.Tests/Fixtures/{robert,bright}.json` and the GST golden set. This doc fills SRS §4.6. Any
build-time deviation — and the DP / divergence decisions — are logged in `memory.md` with their reason (R6).*
