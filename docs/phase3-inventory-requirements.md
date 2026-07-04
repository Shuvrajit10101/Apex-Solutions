# Phase 3 — Inventory Requirements

> **Authored by A13 (CA / requirements sign-off) + A14 (Tally fidelity), per `plan.md` §5 Phase 3 and
> the `/software` lifecycle (requirements → design → …).** This is the up-front requirements slice for
> **Phase 3 (Inventory)**. It fills SRS §4.3 and traces every requirement to the feature catalog
> (`docs/tally-feature-catalog.md`, "catalog §N") and its verification report
> (`docs/tally-feature-catalog-verification-report.md`, "VR item N"). Requirements follow the "good
> requirement" checklist: uniquely identified, atomic, testable, unambiguous, traceable.
>
> **Fidelity & IP discipline (R4/R7):** behaviour below is described in our own words, grounded in the
> catalog + the `tally/` corpus (read locally by A14, never reproduced verbatim). The shipped app and
> code must **never** contain the word "Tally" — our product is **Apex Solutions**.
>
> **Reading order for a resuming session:** `memory.md` → `plan.md` (Phase 3) → this file.

---

## 1. Purpose & scope

### 1.1 Purpose
Add **inventory keeping, integrated with the double-entry accounts**, on top of the Phase-1/2 ledger
engine. After Phase 3 a trading company can maintain stock masters, record stock and order vouchers,
sell and purchase in **Item-Invoice mode** (one voucher touching both stock and accounts), derive a
**closing-stock valuation** by a chosen method, and see that value flow into the **Stock-in-Hand** line
of the Balance Sheet and the **Trading/COGS** block of the P&L — all to the paisa (NFR-3).

The heart remains the framework-agnostic `Apex.Ledger` core; inventory is a projection/extension over
it (plan.md §1.1). Persistence adds SQLite **schema v9** over the current **v8** (multi-currency).

### 1.2 In scope (Phase 3) — grounded in plan.md §5 Phase 3, catalog §9–§10 + §16
- **Inventory masters (catalog §9):** Stock Group (with "add quantities?" flag + nesting), Stock
  Category (independent axis), Units of Measure (Simple + Compound), Godown/Location (default
  "Main Location" + third-party flag), Stock Item (group/category/units/HSN placeholder/opening balance
  by godown+batch/reorder). *Note:* HSN/taxability fields are **captured but inert** until Phase 4 (GST).
- **Stock & order vouchers (catalog §10):** Purchase Order, Sales Order, Receipt Note (GRN), Delivery
  Note, Rejection In, Rejection Out, Stock Journal (source/destination), Physical Stock.
- **Order-processing chain (catalog §10):** PO→Receipt→Purchase and SO→Delivery→Sales, with the
  effect-on-accounts-vs-stock rules and the point at which stock actually moves.
- **Item-Invoice mode** in Purchase (F9) and Sales (F8): item lines that update stock **and** post the
  accounting effect in one voucher, gated by the ledger "Inventory values are affected?" flag (VR item 125).
- **Valuation methods (catalog §9 clone-note):** FIFO, LIFO, Average Cost, Standard Cost, Last Purchase
  Cost, Last Sale Cost — selectable per stock item; deterministic & pure.
- **Accounts↔Inventory integration:** Stock-in-Hand ledger closing balance **derived** from Σ item
  closing values (VR item 10); the P&L closing-stock figure sourced from the inventory engine
  (`ClosingStockMode.InventoryDerived`, already seamed in `ProfitAndLoss.Build`).
- **Inventory reports (catalog §16):** Stock Summary (with drill-down to item movement), Godown Summary,
  Movement/Ageing analysis, Batch analysis, order/receipt/delivery/rejection/physical **registers**,
  Reorder-status report.

### 1.3 Explicitly deferred to Phase 6 (Advanced inventory) — plan.md §5 Phase 6, catalog §11
The boundary is stated so it is not mistaken for a Phase-3 gap:
- **Bill of Materials (BOM) & Manufacturing Journal** — a Stock Item may *record* a reorder/opening
  shape now, but component definitions, the "Use as Manufacturing Journal" Stock-Journal mode, and
  additional-cost apportionment are **Phase 6**.
- **Batches — deep behaviour:** Phase 3 stores a **batch/lot label** on an opening balance and on
  inventory allocations (so multi-batch opening stock reconciles), but **mfg/expiry dates,
  expiry-based valuation, and batch-expiry reports are Phase 6.**
- **POS** (multi-mode payment, Alt+I) — Phase 6.
- **Job Work** (Job Work In/Out orders, Material In/Out, principal↔job-worker tracking) — Phase 6; the
  godown "our stock with a third party" flag is *captured* in Phase 3 but its job-work workflow is Phase 6.
- **Additional cost of purchase**, **zero-valued transactions**, **actual-vs-billed quantity**,
  **Price Levels/Lists**, **advanced (period-based) reorder** — all **Phase 6**. Phase 3 reorder is the
  **simple** form (reorder level + minimum order qty).

> **Scope reconciliation (plan.md vs catalog):** plan.md §5 Phase 3 lists catalog **§9 and §10** only;
> catalog **§11 (advanced inventory)** is assigned to Phase 6 by plan.md. This doc honours that split.
> The one item plan.md places in Phase 3 that the catalog files under §11 is **simple Reorder + Reorder
> Status** (plan.md Phase 3 "Stock Summary + inventory registers" and Phase 6 "Reorder (+status report)").
> See §DP-9 for the resolution (simple reorder in Phase 3; advanced reorder Phase 6).

---

## 2. Numbered functional requirements

> Each RQ is testable and cites its catalog/VR origin. "SHALL" = mandatory Phase-3 behaviour.
> Money is **integer paisa**; quantity is stored scaled to the unit's decimals (see ER-2/ER-3).

### 2.1 Inventory masters (catalog §9)

- **RQ-1 — Stock Group.** The system SHALL support Stock Group masters with Name, optional Alias,
  optional parent (`Under`, nesting to any depth), and a boolean **"Should quantities be added?"**
  (when false, a group holding items of unlike units does not roll a summed quantity into its parent —
  only value aggregates). Predefined "Primary" is the implicit root. *(catalog §9)*
- **RQ-2 — Stock Category.** The system SHALL support Stock Category masters as an **independent
  classification axis** (Name, optional Alias, optional parent) that is **not** nested under Stock
  Groups; a Stock Item may carry a Category orthogonally to its Group. *(catalog §9)*
- **RQ-3 — Simple Unit.** The system SHALL support Simple Units of Measure with Symbol (e.g. "Nos"),
  Formal Name, **UQC** (Unit Quantity Code placeholder for GST, inert until Phase 4), and **decimal
  places 0–4**. Quantities of that unit SHALL round/store to those decimals. *(catalog §9)*
- **RQ-4 — Compound Unit.** The system SHALL support Compound Units defined as **First Unit ×
  Conversion Factor + Tail Unit** (e.g. Dozen = 12 Nos; Box = 20 Nos; Kg = 1000 g). Conversion SHALL be
  exact (integer-factor arithmetic, no float) and reversible for display in either unit. *(catalog §9)*
- **RQ-5 — Godown / Location.** The system SHALL support Godown masters with Name, optional Alias,
  optional parent (hierarchical), and a boolean **"Our stock with a third party"** (job-work flag,
  captured only in Phase 3). A default **"Main Location"** SHALL exist on every company (already seeded —
  `Company.MainLocationName`). *(catalog §9; plan.md §4.4)*
- **RQ-6 — Stock Item.** The system SHALL support Stock Item masters with Name, optional Alias, **Under**
  (Stock Group), optional **Category**, **Unit** (simple or compound), **opening balance** expressed as
  **quantity + rate → value**, allocatable **per godown and per batch label**, optional **reorder level
  + minimum order quantity**, and GST placeholder fields (HSN/SAC, taxability — captured, inert until
  Phase 4). Opening value SHALL equal Σ(godown/batch qty × rate) to the paisa. *(catalog §9)*
- **RQ-7 — Master creation modes.** Every inventory master (RQ-1..RQ-6) SHALL be creatable in **single**,
  **multi** (`Alt+H`), and **inline** (`Alt+C`, from within a voucher field) modes, matching the
  accounting-master convention already shipped. *(catalog §9)*

### 2.2 Stock & order vouchers — effect on accounts vs stock (catalog §10)

> **Effect rules (catalog §10, the core logic):** PO/SO affect **neither** accounts nor stock;
> Receipt Note (GRN)/Delivery Note affect **stock only**; Purchase/Sales affect **both**; Rejection
> In/Out affect **stock only**; Stock Journal and Physical Stock affect **stock only** (Stock Journal
> may re-value via a difference in source/destination rates, but posts no accounting entry in Phase 3).

- **RQ-8 — Purchase Order (Ctrl+F9).** A Purchase Order SHALL record ordered items (qty, rate, godown)
  against a supplier and a due/expected date, and SHALL affect **neither stock nor accounts** — it is an
  outstanding order only, tracked for fulfilment. *(catalog §10)*
- **RQ-9 — Sales Order (Ctrl+F8).** A Sales Order SHALL record ordered items against a customer and
  SHALL affect **neither stock nor accounts** — outstanding order only. *(catalog §10)*
- **RQ-10 — Receipt Note / GRN (Alt+F9).** A Receipt Note SHALL **increase stock** at the receiving
  godown/batch and SHALL post **no accounting entry**; it MAY reference a Purchase Order (tracking link)
  to close the ordered quantity. *(catalog §10)*
- **RQ-11 — Delivery Note (Alt+F8).** A Delivery Note SHALL **decrease stock** from the issuing
  godown/batch and SHALL post **no accounting entry**; it MAY carry dispatch details (transporter,
  LR/RR no., vehicle) and MAY reference a Sales Order. *(catalog §10)*
- **RQ-12 — Rejection Out (Ctrl+F5).** A Rejection Out SHALL **decrease stock** (goods returned to a
  supplier) with no accounting entry. *(catalog §10; VR item 63 confirms Ctrl+F5)*
- **RQ-13 — Rejection In (Ctrl+F6).** A Rejection In SHALL **increase stock** (goods returned by a
  customer) with no accounting entry. *(catalog §10; VR item 63 confirms Ctrl+F6)*
- **RQ-14 — Stock Journal (Alt+F7).** A Stock Journal SHALL move stock from **Source (Consumption)** to
  **Destination (Production)** lines — inter-godown transfer, manufacturing consumption, or wastage. It
  SHALL affect **stock only** (no accounting posting in Phase 3). Total source value and destination
  value SHALL be reconcilable (a documented difference = wastage/absorption). *(catalog §10)*
- **RQ-15 — Physical Stock (via F10 → Other Vouchers → Physical Stock).** A Physical Stock voucher SHALL
  record a **counted quantity** for an item/godown/batch as of a date. Its effect on the book quantity
  is governed by **DP-3**. Physical Stock has **no dedicated function key**; it is reached through the
  **F10 (Other Vouchers)** menu. *(catalog §10; VR items 57 & 173 — Alt+F10 is legacy/uncertain, use F10)*
- **RQ-16 — Item-Invoice Purchase (F9).** In Item-Invoice mode, a Purchase voucher SHALL carry item
  lines (item, qty, rate, godown, batch) that **increase stock** and, in the **same voucher**, post the
  accounting effect (Dr Purchases/asset, Cr supplier) so Σ Dr = Σ Cr. It is enabled only when the
  purchase ledger has **"Inventory values are affected?" = Yes**. *(catalog §10; VR item 125)*
- **RQ-17 — Item-Invoice Sales (F8).** In Item-Invoice mode, a Sales voucher SHALL carry item lines that
  **decrease stock** and, in the same voucher, post the accounting effect (Dr customer, Cr Sales). Stock
  moves at **invoice time** (the Sales voucher), not at Delivery-Note time, when the chain skips or has
  already delivered — see RQ-19. *(catalog §10; VR item 125)*
- **RQ-18 — Tracking-number chain.** Receipt/Delivery Notes and their Purchase/Sales invoices SHALL be
  linkable by a **tracking number** so a later invoice does **not** double-move stock already moved by
  its Note (see RQ-19). *(catalog §10)*

### 2.3 Order-processing chain & when stock moves (catalog §10)

- **RQ-19 — Single stock movement per physical event.** For a given tracked quantity, stock SHALL move
  **exactly once** across the chain: if a **Receipt/Delivery Note** already moved it, the linked
  **Purchase/Sales** invoice SHALL post the **accounting** effect **without** moving stock again; if
  there is **no** Note (invoice-only flow), the **Purchase/Sales** invoice SHALL move the stock. Orders
  (PO/SO) never move stock. *(catalog §10 effect rules; this is the anti-double-count invariant)*
- **RQ-20 — Order fulfilment tracking.** The system SHALL track outstanding order quantity per PO/SO
  line, reducing it as linked Receipt/Delivery Notes (or direct invoices) fulfil it, and SHALL surface
  the remainder in the order books (RQ-31). *(catalog §10)*

### 2.4 Valuation (catalog §9 clone-note)

- **RQ-21 — Per-item valuation method.** Each Stock Item SHALL carry a **Costing/Valuation method**
  selectable from **{FIFO, LIFO, Average Cost, Standard Cost, Last Purchase Cost, Last Sale Cost}**. The
  method SHALL drive the item's **closing value** in every inventory and financial report. *(catalog §9)*
- **RQ-22 — Deterministic valuation.** For a fixed set of inventory movements, each method SHALL yield a
  **deterministic, paisa-exact** closing value, computed by a **pure** function of the movement history
  (no ambient state, no float). FIFO/LIFO consume cost layers in order; Average Cost uses the running
  weighted average; Standard Cost uses the item's standard rate; Last Purchase/Sale Cost uses the most
  recent purchase/sale rate. *(catalog §9 clone-note; NFR-3)*
- **RQ-23 — Default valuation method.** A new Stock Item's default valuation method SHALL be the
  company-wide default resolved per **DP-1** (recommended: **Average Cost**). *(catalog §9; DP-1)*
- **RQ-24 — Valuation-method change policy.** Changing an item's valuation method SHALL follow **DP-2**
  (recommended: allowed, applies from the change forward; the report engine recomputes closing value on
  the new method as of any date, since valuation is a pure function of history). *(DP-2)*

### 2.5 Stock-in-Hand integration (catalog §9 clone-note; VR item 10)

- **RQ-25 — Derived Stock-in-Hand.** When "Integrate Accounts & Inventory" is on (default), the
  **Stock-in-Hand** ledger closing balance SHALL be **derived** = Σ over all stock items of
  (item closing quantity × item valuation rate) as of the report date. It SHALL **not** be a manually
  posted or manually editable figure. *(VR item 10; catalog §9)*
- **RQ-26 — Balance Sheet Stock-in-Hand.** The Balance Sheet SHALL show the derived Stock-in-Hand value
  (RQ-25) on the assets side; drilling into it (`Enter`) SHALL reach the **Stock Summary** (RQ-27).
  *(catalog §16; plan.md Phase 3 exit gate "valuation reconciles into the Balance Sheet")*
- **RQ-27 — P&L closing stock.** The Trading/P&L SHALL source its **closing stock** from the inventory
  engine via **`ClosingStockMode.InventoryDerived`** (the seam already present in `ProfitAndLoss.Build`),
  so gross profit = Sales + Direct Income − (Opening Stock + Purchases + Direct Expenses − Closing Stock).
  *(catalog §16; existing `src/Apex.Ledger/Reports/ProfitAndLoss.cs`)*

### 2.6 Inventory reports (catalog §16)

- **RQ-28 — Stock Summary.** The system SHALL produce a **Stock Summary** (per item: opening qty/value,
  inwards, outwards, closing qty/value at the item's valuation method), groupable by Stock Group /
  Category, with **drill-down (`Enter`) to item movement** (the ordered list of inward/outward
  transactions). *(catalog §16)*
- **RQ-29 — Godown Summary.** The system SHALL produce a **Godown Summary** (item balances per godown),
  with drill-down to that godown's item movement. *(catalog §16)*
- **RQ-30 — Movement / Ageing analysis.** The system SHALL produce **Movement Analysis** (inwards/
  outwards per item over a period) and **Stock Ageing** (closing quantity bucketed by age of the
  on-hand layers). *(catalog §16)*
- **RQ-31 — Registers & order books.** The system SHALL produce voucher **registers** for Receipt Note,
  Delivery Note, Rejection In, Rejection Out, Stock Journal, and Physical Stock, plus **Order books**
  (Purchase-Order and Sales-Order, outstanding and all). *(catalog §10, §16)*
- **RQ-32 — Batch analysis.** The system SHALL produce a **Batch Summary** (closing balance per batch
  label per item). *Mfg/expiry columns are Phase 6* (see §1.3). *(catalog §16)*
- **RQ-33 — Reorder Status.** The system SHALL produce a **Reorder Status** report listing items whose
  closing quantity is at/below reorder level, with the shortfall and the suggested order quantity
  (max of the shortfall and the minimum order quantity). **Simple reorder only** (DP-9). *(catalog §16)*
- **RQ-34 — No negative stock surfaced.** All quantity figures in the above reports SHALL be consistent
  with the no-negative-stock guarantee (ER-5); a report SHALL never present a negative closing quantity
  that the engine would have rejected at entry. *(ER-5)*

### 2.7 Keyboard & navigation (catalog §10, §21)

- **RQ-35 — Voucher shortcuts.** Each stock/order voucher SHALL bind the shortcut in §3, reproduced on
  the right-hand button panel, reachable keyboard-only (NFR-2). *(catalog §10, §21; VR item 63)*
- **RQ-36 — Inline master create in voucher fields.** From an item/godown/unit field inside a voucher,
  `Alt+C` SHALL create the referenced master inline without leaving the voucher (RQ-7). *(catalog §9)*

---

## 3. Keyboard shortcuts (verified against catalog §10, §21 + VR item 63)

| Voucher / master | Shortcut | Catalog / VR confirmation |
|---|---|---|
| Purchase Order | **Ctrl+F9** | catalog §10; VR item 63 (confirmed-correct) |
| Sales Order | **Ctrl+F8** | catalog §10; VR item 63 |
| Receipt Note (GRN) | **Alt+F9** | catalog §10; VR item 63 |
| Delivery Note | **Alt+F8** | catalog §10; VR item 63 |
| Rejection Out | **Ctrl+F5** | catalog §10; VR item 63 |
| Rejection In | **Ctrl+F6** | catalog §10; VR item 63 |
| Stock Journal | **Alt+F7** | catalog §10; VR item 63 |
| Physical Stock | **F10 → Other Vouchers → Physical Stock** (no dedicated key) | VR items 57 & 173 (**correction**: the prompt's "via F10" is right *as a menu path*; **Alt+F10 is legacy/uncertain and must not be bound**) |
| Purchase (Item-Invoice) | **F9** | catalog §4, §10 |
| Sales (Item-Invoice) | **F8** | catalog §4, §10 |
| Multi-create master | **Alt+H** | catalog §9 |
| Inline-create master | **Alt+C** | catalog §9 |

> **Correction folded in:** all order/stock shortcuts match the prompt. The **only** adjustment is
> **Physical Stock**: it is reached via the **F10 (Other Vouchers)** menu — it has **no dedicated
> function key** — so we bind it under that menu and do **not** wire a standalone key (VR items 57/173).
> This matches the existing seed (`SeedVoucherTypes` gives Physical Stock the shortcut string "F10",
> denoting the Other-Vouchers menu entry, not a distinct hotkey).

---

## 4. Engineering rules (ER)

- **ER-1 — Idempotent v8→v9 migration.** Phase 3 SHALL add SQLite **schema v9** via a `MigrateV8ToV9`
  block that: runs inside one transaction that bumps `schema_version` to 9; uses `ALTER TABLE … ADD
  COLUMN` with defaults + new `CREATE TABLE`/`CREATE INDEX` only (never rewrites existing rows); leaves
  every existing v8 database's data intact (new columns default to inventory-off / NULL). A fresh DB is
  stamped straight to v9 via the consolidated create DDL. Follows the exact pattern of MigrateV1ToV2 …
  MigrateV7ToV8 in `src/Apex.Persistence.Sqlite/Schema.cs`. *(mirrors schema.cs convention)*
- **ER-2 — Paisa-integer money.** Every inventory **value** (opening value, rate×qty, closing value,
  Stock-in-Hand) SHALL be stored and computed as **INTEGER paisa** — never REAL/float. Rate is stored at
  a fixed integer scale sufficient for paisa-exact value (see ER-3). *(NFR-3; schema.cs money convention)*
- **ER-3 — Quantity & rate precision.** Quantity SHALL be stored as an **integer scaled to the unit's
  decimals** (0–4, RQ-3), and rate at a fixed integer scale (recommend **micros**, ×1,000,000, matching
  the existing forex-rate scale) so that `value = round_to_paisa(qty × rate)` is deterministic. No binary
  float anywhere in the valuation path. *(NFR-3; schema.cs `ForexScale` precedent)*
- **ER-4 — Deterministic, pure valuation.** Valuation (RQ-21/22) SHALL be implemented as **pure
  functions** in `Apex.Ledger` (given the movement history + method → closing value), with **no**
  Avalonia, DB, clock, or RNG dependency, so each method is unit-testable in isolation. *(plan.md §1.1;
  NFR-6)*
- **ER-5 — No negative stock.** The engine SHALL **guard every stock-decreasing path** (Delivery Note,
  Sales Item-Invoice, Rejection Out, Stock-Journal source, Physical-Stock downward per DP-3) and reject
  a movement that would drive an item's on-hand quantity **negative** at the affected godown/batch as of
  that date. The guard is centralised so no mutation path bypasses it. *(catalog §16 "negative stock"
  exception is a report of a *guarded* condition; DP-7)*
- **ER-6 — Stock-in-Hand read-only/derived.** The Stock-in-Hand ledger closing balance SHALL be computed
  by the report engine (RQ-25) and SHALL be **read-only** in the UI — no voucher may post a manual
  Stock-in-Hand closing figure when integration is on. The existing `IsStockInHandLedger` helper
  (`ClassificationRules`) identifies these ledgers. *(VR item 10)*
- **ER-7 — Delete-blocked-if-referenced.** An inventory master (Stock Group, Category, Unit, Godown,
  Stock Item) SHALL **not** be deletable while referenced (an item under a group; a movement/opening on
  an item/godown/unit; a batch label in use), mirroring the ledger/group delete guards already shipped.
  *(catalog §9; existing delete-guard convention)*
- **ER-8 — Inventory logic stays Avalonia-free.** All inventory domain types, valuation, movement
  aggregation, and report projections SHALL live in `Apex.Ledger` (or a sub-namespace) with **no UI
  dependency**, unit-tested via xUnit exactly like the accounting core. *(plan.md §1.1/§3; NFR-6)*
- **ER-9 — Integration flag.** "Integrate Accounts & Inventory" SHALL be a company-level flag (default
  **on**); when off, Stock-in-Hand is a plain manually-posted ledger (Phase-1 behaviour) and inventory
  vouchers track quantity/value without driving the financial statements. Default-on matches a trading
  company and keeps the Bright gate meaningful. *(catalog §9 clone-note)*
- **ER-10 — Tracking-link integrity.** The anti-double-count invariant (RQ-19) SHALL be enforced in the
  domain (a tracked quantity carries at most one stock-moving event), not left to the UI. *(RQ-19)*

---

## 5. Decision points (DP) — defaults recommended for A13/user approval

> Each DP states the ambiguity, our recommended default, and a one-line rationale. **These require the
> user's approval before implementation** (R12).

- **DP-1 — Default valuation method.** *Ambiguity:* which method a new item defaults to.
  **Recommend: Average Cost.** *Rationale:* it is the most forgiving, order-insensitive default, avoids
  layer-tracking surprises for casual users, and reconciles cleanly with periodic-inventory P&L; FIFO
  remains one click away per item.
- **DP-2 — Change valuation method mid-year.** *Ambiguity:* may an item's method change within an FY.
  **Recommend: allowed; applies going forward, recomputed purely from history.** *Rationale:* valuation
  is a pure function of the movement history (ER-4), so switching method just changes how the same
  history is valued as of any date — no destructive migration; we log the change for audit.
- **DP-3 — Physical Stock effect.** *Ambiguity:* does Physical Stock auto-post an adjustment or only
  record the count. **Recommend: it records the counted quantity and the engine treats it as the new
  book quantity as of that date (an implicit adjustment to the difference), with the difference visible
  in the Physical Stock register.** *Rationale:* matches the catalog's "system posts the book-vs-physical
  adjustment" (§10); keeps Stock-in-Hand derived (no separate manual value entry). The **value** of the
  adjustment follows the item's valuation method.
- **DP-4 — Rejection In/Out: separate voucher types or modes.** *Ambiguity:* model as distinct voucher
  types or as modes of Receipt/Delivery. **Recommend: separate predefined voucher types (Rejection In,
  Rejection Out).** *Rationale:* the catalog and our seed already define them as distinct types with their
  own shortcuts (Ctrl+F6 / Ctrl+F5) and registers; keeping them separate matches fidelity and the
  existing `SeedVoucherTypes`.
- **DP-5 — Stock Journal accounting effect.** *Ambiguity:* does a Stock Journal post to accounts (e.g.
  manufacturing value absorption). **Recommend: stock-only in Phase 3; no accounting posting.**
  *Rationale:* catalog §10 lists Stock Journal as a stock movement; value absorption/additional-cost is
  Manufacturing-Journal territory (Phase 6). A source/destination value difference is reported as
  wastage/gain, not posted to a P&L ledger, until Phase 6.
- **DP-6 — Compound-unit closing display.** *Ambiguity:* report closing quantity in base or compound
  unit. **Recommend: store in the base unit, display in the item's declared (compound) unit, with base
  in the drill-down.** *Rationale:* one canonical stored quantity (ER-3) avoids rounding drift;
  presentation converts exactly via the integer factor (RQ-4).
- **DP-7 — Negative stock at entry vs. warn-and-allow.** *Ambiguity:* hard-block or warn. **Recommend:
  hard-block by default (ER-5); a company flag to "allow negative stock (warn only)" is deferred to
  Phase 6.** *Rationale:* the paisa-exact valuation methods (FIFO/LIFO/Average) are ill-defined against
  negative on-hand; blocking keeps valuation deterministic. (Real-world "allow negative" is an advanced
  toggle; out of Phase-3 scope.)
- **DP-8 — Godown mandatory on item lines.** *Ambiguity:* must every item line name a godown when only
  "Main Location" exists. **Recommend: default to "Main Location" automatically; require an explicit
  godown only once a second godown exists.** *Rationale:* keeps single-location companies frictionless
  while staying multi-godown-correct.
- **DP-9 — Reorder scope in Phase 3.** *Ambiguity:* plan.md lists Reorder Status in both Phase 3
  (registers/reports) and Phase 6 (advanced). **Recommend: ship *simple* reorder (reorder level +
  minimum order qty + Reorder Status report) in Phase 3; defer *advanced* reorder (period-based,
  higher/lower consumption criteria) to Phase 6.** *Rationale:* the simple form needs only fields already
  on the Stock Item (RQ-6) and closing quantity we already compute; advanced reorder needs
  consumption-window analytics that belong with Phase-6 depth.
- **DP-10 — Batch label in Phase 3.** *Ambiguity:* how much batch behaviour lands now. **Recommend: a
  plain batch/lot *label* on opening balances and inventory allocations (so multi-batch opening stock and
  a Batch Summary reconcile), with mfg/expiry dates and expiry valuation deferred to Phase 6.**
  *Rationale:* lets Bright-style multi-batch openings reconcile without pulling forward Phase-6 expiry
  logic; the column is nullable so non-batch items are unaffected.

---

## 6. Bright fixture re-verification (the hard Phase-3 gate)

> **Context.** The Bright fixture (`tests/Apex.Ledger.Tests/Fixtures/bright.json`) today posts closing
> stock (₹15,000) **manually** via Journal voucher #11 (Dr "Closing Stock" / Cr "Closing Stock
> Adjustment (P&L)") and the P&L runs in `ClosingStockMode.AsPostedLedger`. The fixture's own note says
> the closing-stock treatment is **"finalised by the Phase-3 inventory-integrated engine."** Phase 3
> must make that ₹15,000 **fall out of inventory**, not a hand-posted Journal.

Phase 3 SHALL re-verify Bright by proving **all** of the following, to the paisa (NFR-3):

- **BR-1 — Opening stock reconciles from inventory.** Bright's opening stock (₹25,000) SHALL be
  expressible as stock-item opening balances (qty × rate per godown/batch) whose Σ value = **₹25,000**,
  equal to the opening debit of the "Opening Stock" Stock-in-Hand ledger.
- **BR-2 — Purchases/sales move stock at invoice time.** Bright's purchase and sale SHALL be re-recorded
  (or shadowed) as **Item-Invoice** vouchers so stock **increases on Purchase** and **decreases on Sales**
  in the same voucher that posts the accounting effect (RQ-16/RQ-17), with **exactly one** stock movement
  per physical event (RQ-19).
- **BR-3 — Closing stock computed by the default method.** The closing stock SHALL be **computed by the
  engine** under the default valuation method (DP-1, Average Cost) from the opening + movements, and SHALL
  equal **₹15,000** to the paisa — **without** a manual closing-stock Journal.
- **BR-4 — Balance Sheet Stock-in-Hand = computed closing value.** The Balance Sheet **Stock-in-Hand**
  line SHALL equal the derived closing value (RQ-25/26) = **₹15,000**, and total assets SHALL stay
  **₹1,84,000** balanced against total liabilities **₹1,84,000**.
- **BR-5 — P&L Trading/COGS identity.** The Trading block SHALL satisfy **COGS = Opening + Purchases −
  Closing** and gross profit = Sales + Direct Income − (Opening + Purchases + Direct Expenses − Closing),
  reproducing Bright's **gross profit ₹15,000** and **net profit −₹1,000** under
  `ClosingStockMode.InventoryDerived`.
- **BR-6 — Robert stays green.** The accounts-only **Robert** fixture SHALL remain green and unchanged
  (it has no stock; inventory must not perturb the accounts-only path). *(R8)*

> **Gate rule (R9 / verification-before-completion):** any red on BR-1..BR-6 — or a Trial Balance that no
> longer balances, or a manual-closing-stock path still required — **stops** the phase. The ₹15,000 must
> be *derived*, and the whole Bright statement set (TB, P&L, BS) must reconcile to the paisa.

---

## 7. Sign-off checklist (A13 / CA ticks to close Phase 3)

> One line per RQ-group + the Bright gate + the R9/R11 gate items. A13 signs only when **every** box is
> ticked with shown evidence (tests green displayed, app run, review passed, de-branded, committed).

- ☐ **1. Inventory masters (RQ-1..RQ-7)** — Stock Group (+add-qty flag, nesting), Category, Simple &
  Compound Units, Godown (+Main Location, third-party flag), Stock Item (group/category/unit/opening by
  godown+batch/reorder), and single/multi/inline create — implemented, tested, catalog-faithful.
- ☐ **2. Stock & order vouchers (RQ-8..RQ-18)** — PO, SO, GRN, Delivery, Rejection In/Out, Stock Journal,
  Physical Stock, Item-Invoice Purchase/Sales — each with its correct **effect on accounts vs stock** and
  its shortcut (§3) — implemented and tested.
- ☐ **3. Order-processing chain (RQ-19..RQ-20)** — single-movement-per-event invariant and order
  fulfilment tracking — implemented and tested (no double-count).
- ☐ **4. Valuation (RQ-21..RQ-24)** — all six methods selectable per item, deterministic & pure, default
  = Average Cost (DP-1), change policy per DP-2 — implemented and unit-tested per method.
- ☐ **5. Stock-in-Hand integration (RQ-25..RQ-27)** — derived Stock-in-Hand, Balance Sheet line +
  drill-down, P&L closing via `InventoryDerived` — implemented and reconciled.
- ☐ **6. Inventory reports (RQ-28..RQ-34)** — Stock Summary (+movement drill-down), Godown Summary,
  Movement/Ageing, Batch Summary, registers, order books, Reorder Status — implemented and tested.
- ☐ **7. Keyboard & navigation (RQ-35..RQ-36)** — every voucher shortcut per §3 bound and keyboard-only
  reachable; Physical Stock under F10 (Other Vouchers), no stray Alt+F10.
- ☐ **8. Engineering rules (ER-1..ER-10)** — idempotent v8→v9 migration; paisa/qty precision; pure
  deterministic valuation; no-negative-stock guard on all paths; read-only derived Stock-in-Hand;
  delete-guards; Avalonia-free core — all satisfied.
- ☐ **9. Decision points (DP-1..DP-10)** — each default **approved by the user** (R12) before build, or
  the approved variant recorded in `memory.md`.
- ☐ **10. Bright gate (BR-1..BR-6)** — closing stock **derived** to ₹15,000; BS Stock-in-Hand = ₹15,000;
  TB/P&L/BS reconcile to the paisa; **Robert stays green**. *(hard gate)*
- ☐ **11. Tests green — shown.** Full unit + integration suite green (displayed), including Robert &
  Bright, per R9.
- ☐ **12. Review passed.** Code Reviewer + Verification/Completeness Critic adversarial pass, no
  open findings, fidelity-matrix gap list re-derived (R9/R10).
- ☐ **13. De-branded.** No occurrence of the word "Tally" in shipped app UI or code; product reads
  "Apex Solutions" throughout (project rule).
- ☐ **14. Committed by the GitHub Expert.** Small conventional commits tied to Phase-3 plan items,
  pushed by A12 (the **only** git actor, R4); `memory.md` updated (R5).

---

*Traceability: every RQ cites its catalog §/VR item; ERs cite the schema.cs convention + NFRs; the Bright
gate maps to `tests/Apex.Ledger.Tests/Fixtures/bright.json` and the `ClosingStockMode.InventoryDerived`
seam in `src/Apex.Ledger/Reports/ProfitAndLoss.cs`. This doc fills SRS §4.3. Any build-time deviation is
logged in `memory.md` with its reason (R6).*
