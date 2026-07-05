# Phase 4 — GST (core) Requirements

> **Authored by A13 (CA / requirements sign-off) + A14 (fidelity + statutory-law verification), per
> `plan.md` §5 Phase 4 and the `/software` lifecycle (requirements → design → …).** This is the up-front
> requirements slice for **Phase 4 (GST — core)**. It fills SRS §4.4 and traces every requirement to the
> feature catalog (`docs/tally-feature-catalog.md`, "catalog §N"), its verification report
> (`docs/tally-feature-catalog-verification-report.md`, "VR item N"), and — for every **statutory-law**
> fact — an **official/current source cited inline** (R7: never assert tax law or rates from memory).
> Requirements follow the "good requirement" checklist: uniquely identified, atomic, testable,
> unambiguous, traceable.
>
> **Fidelity & IP discipline (R4/R7):** behaviour below is described in **our own words**, grounded in
> the catalog + the `tally/` corpus (read locally by A14, never reproduced verbatim). The shipped app and
> code must **never** contain the word "Tally" — our product is **Apex Solutions**.
>
> **Reading order for a resuming session:** `memory.md` → `plan.md` (Phase 4) → this file.

---

## 1. Purpose & scope

### 1.1 Purpose
Add **core GST**, integrated with the double-entry accounts (Phase 1/2) and the item-invoice engine
(Phase 3), on top of the framework-agnostic `Apex.Ledger` core. After Phase 4 a **regularly-registered**
company can: enable GST once (F11), keep GST tax ledgers and party/stock-item GST details, raise a
**GST sales or purchase invoice** whose tax is **computed from the taxable value and split correctly**
into **CGST+SGST** (intra-state) or **IGST** (inter-state), see a per-voucher **Tax Analysis**, and
project the posted GST vouchers into the two headline returns — **GSTR-1** (outward supplies) and
**GSTR-3B** (summary) — all to the paisa (NFR-3).

GST is a **projection/extension over the ledger engine** (plan.md §1.1): tax is posted to real ledgers
under **Duties & Taxes**, and the returns are read-only report projections over already-posted vouchers.
Persistence adds SQLite **schema v13** over the current **v12** (multi-currency; §Schema.cs
`CurrentVersion = 12`).

### 1.2 In scope (Phase 4) — grounded in plan.md §5 Phase 4, catalog §12 (core subset)
- **F11 "Enable GST" company config:** GSTIN/UIN, home State (drives place of supply), **Registration
  Type = Regular** (the only working type in Phase 4 — see §1.3), GST-applicable-from date, **GSTR-1
  periodicity** (Monthly / Quarterly-QRMP as a stored election). *(catalog §12 "Enable & configure")*
- **Tax ledgers** under **Duties & Taxes** — Output/Input **CGST**, **SGST/UTGST**, **IGST** — each with
  Tax Type ∈ {Central, State/UT, Integrated}, auto-creatable on enabling GST (DP-1). *(catalog §12 "Masters")*
- **Party GST details** on a party ledger: Registration Type (Regular / Unregistered / Consumer),
  GSTIN/UIN, **State** — the party State is the place-of-supply driver for goods delivered to the party.
  *(catalog §12 "Masters")*
- **Stock-item / sales-purchase-ledger GST details:** **HSN/SAC**, Taxability (Taxable / Nil-Rated /
  Exempt / Non-GST), GST **rate %** (integrated rate; CGST/SGST each derived as half). *(catalog §12)*
- **Rate resolution** (core subset of the 5-level chain): **Stock Item → Sales/Purchase Ledger → Company**
  default, most-granular-wins (DP-6). *(catalog §12 "Rate resolution")*
- **Intra vs inter determination:** company home State vs party State → **CGST + SGST/UTGST** (each = rate
  ÷ 2) or **IGST** (full rate), computed on the **taxable (assessable) value**. *(catalog §12 "Routing rule")*
- **Tax computation & split on a GST item-invoice:** taxable value → rate → CGST/SGST halves **or** full
  IGST → postings to the tax ledgers, **additive** to the Phase-3 stock leg (§4 ER-8, the pairing invariant
  is preserved). Mixed-rate invoices split tax **per line**. *(catalog §12 "Transactions")*
- **Rounding:** paisa-exact tax, a defined per-line-vs-invoice rule (DP-4), optional invoice round-off line.
- **Tax Analysis** view per voucher (**Alt+A**): taxable value, rate, CGST/SGST/IGST amounts. *(catalog §12)*
- **GSTR-1** (outward): B2B vs B2C split, rate-wise summary, **HSN summary** — as a report projection over
  posted sales GST vouchers. *(catalog §12 "Returns & reports"; VR item 19 — HSN summary is confirmed mature)*
- **GSTR-3B** (summary): outward tax by head (CGST/SGST/IGST), eligible **ITC** from purchases, and **net
  tax payable** — as a report projection. *(catalog §12; ITC set-off mechanics themselves are Phase 9, DP-9)*
- **B2C** = a supply to an **unregistered / consumer** party (no GSTIN) — routed to the B2C section of
  GSTR-1 (DP-8). *(catalog §12 "Scenarios")*

### 1.3 Explicitly deferred to Phase 9 (GST advanced) — plan.md §5 Phase 9, catalog §12 (advanced)
Stated so the boundary is not mistaken for a Phase-4 gap. Phase 4 is **core GST only**:
- **Reverse Charge Mechanism (RCM)** — unregistered purchase, notified goods/services, import RCM,
  advance-payment RCM. **Phase 9.**
- **Composition** scheme (Registration Type = Composition; GSTR-4 annual / CMP-08 quarterly; composition
  tax rates). A company *may be created* as Composition but has **no working composition tax path until
  Phase 9** (plan.md C-8 gate note). **Phase 9.**
- **e-Invoice (IRN/QR)** and **e-Way Bill** (offline JSON export). **Phase 9.**
- **GSTR-2A / GSTR-2B download & reconciliation** (auto-drafted ITC statements; VR item 19) and the **IMS**
  local accept/reject workflow. **Phase 9.**
- **Compensation Cess** (and the special niche rates 3% bullion / 0.25% rough diamonds). Cess exists in law
  but is **out of Phase-4 scope**; the tax model leaves a seam for it (ER-9). **Phase 9.**
- **Full ITC set-off engine** (Rule 88A cross-utilisation order) and **Stat Payment/Adjustment** journals
  (Alt+J stat adjustment, Ctrl+F stat payment). Phase 4's GSTR-3B **displays** eligible ITC and net payable
  as a *computation*, but the **posting** of the set-off / cash-ledger payment is **Phase 9** (DP-9).
- **Imports / Exports / SEZ / Deemed exports**, **advance-receipt GST**, **multi-GSTIN / branch
  registration**, **GST Rate Setup bulk screen**, **per-tax-ledger rounding method**, **annual returns
  (GSTR-9/9A/9C)**. All **Phase 9** (catalog §12 advanced; VR items 141–154).

> **Scope reconciliation (plan.md vs catalog) — the ONE divergence to flag.** `plan.md` §5 **Phase 4**
> explicitly names *"ITC set-off per **Rule 88A**"* and *"stat adjustment (Alt+J) + stat payment (Ctrl+F)"*
> as Phase-4 deliverables, whereas `plan.md` §5 **Phase 9** lists the advanced-GST breadth and the prompt
> (A13's charter) places *"RCM, composition, e-invoice/e-way, GSTR-2A/2B"* in Phase 9 and asks Phase 4 to
> stay **core**. **Resolution (DP-9, recommended):** Phase 4 delivers GSTR-3B as a **read-only
> computation** that *shows* eligible ITC and net tax payable (the arithmetic every core-GST user needs to
> read a 3B), but the **posting** of the ITC set-off journal and the stat cash-payment voucher — i.e. the
> Rule-88A cross-utilisation *engine* and Alt+J/Ctrl+F *vouchers* — move to **Phase 9**, where the plan
> already schedules "RCM … ITC set-off per Rule 88A" breadth and the offline stat-payment paths. This keeps
> Phase 4 genuinely *core* (compute + report) without a half-built set-off engine, and is logged for the
> user's go/no-go (R6/R12). If the user prefers the literal plan.md text, the Alt+J/Ctrl+F posting can be
> pulled into Phase 4 — flagged, not silently decided.

---

## 2. Numbered functional requirements

> Each RQ is testable and cites its catalog/VR origin; every **law** fact cites its official source inline.
> "SHALL" = mandatory Phase-4 behaviour. Money is **integer paisa**; a GST **rate** is stored as an exact
> scaled integer (basis points, ER-2). GST is **opt-in**: with F11 GST off, nothing in this doc activates
> and the Phase-1/2/3 behaviour is byte-for-byte unchanged (ER-10, §5 Bright/Robert).

### 2.1 Enable & configure GST (F11) — catalog §12 "Enable & configure"

- **RQ-1 — Enable GST (F11).** The system SHALL provide a company-level **"Enable GST"** feature flag
  (F11 → Taxation). When off (default for existing companies), no GST field, ledger, or report is active.
  When turned on, the company SHALL capture the GST config of RQ-2. *(catalog §12; VR — F11 gating)*
- **RQ-2 — Company GST details.** On enabling GST, the system SHALL capture: **home State** (from the Indian
  state/UT list, each with its 2-digit **state code**), **Registration Type** (Phase 4: **Regular**;
  Composition/Unregistered stored but inert per §1.3), **GSTIN/UIN**, **GST-applicable-from** date, and
  **GSTR-1 periodicity** (Monthly / Quarterly). The home State is the supplier location for place-of-supply
  (RQ-11). *(catalog §12; corpus fidelity: State code = first two digits of GSTIN)*
- **RQ-3 — GSTIN structure validation.** A GSTIN SHALL be validated as **15 characters** =
  **[2-digit State code][10-char PAN][1 entity code][1 default char, normally 'Z'][1 checksum]**; the
  leading 2 digits SHALL match a valid state code and the 15th char SHALL satisfy the **Luhn-mod-36**
  checksum. An invalid GSTIN SHALL be rejected with a clean domain error (fail-fast). *Law source:*
  official GSTIN format — 2-digit state code + 10-char PAN + entity + 'Z' + checksum (Luhn mod 36).
  ([cleartax.in/s/know-your-gstin](https://cleartax.in/s/know-your-gstin)) *(catalog §12 "Key constants")*

### 2.2 Tax ledgers (Duties & Taxes) — catalog §12 "Masters"

- **RQ-4 — GST tax ledgers.** The system SHALL support GST tax ledgers under the **Duties & Taxes** group
  with **Type of Duty = GST** and **Tax Type ∈ {Central (CGST), State/UT (SGST/UTGST), Integrated (IGST)}**.
  These ledgers receive the computed tax postings (RQ-12/13). *(catalog §12; plan.md §4.1 Duties & Taxes)*
- **RQ-5 — Auto-create tax ledgers.** On enabling GST (RQ-1), the system SHALL, per **DP-1**, auto-create the
  standard GST tax ledgers (Output CGST, Output SGST, Output IGST for sales; Input CGST, Input SGST, Input
  IGST for purchases) under Duties & Taxes, so a user can raise a GST invoice without manual ledger setup.
  Names follow **DP-3**. *(catalog §12; DP-1/DP-3)*
- **RQ-6 — UTGST parity.** For a company whose home State is a **Union Territory without legislature**, the
  "State" tax leg SHALL be **UTGST** (functionally parallel to SGST, same half-rate); the engine SHALL treat
  SGST and UTGST as the one "State/UT tax" head. *Law source:* UTGST applies in UTs in place of SGST, at the
  same rate. ([taxsummaries.pwc.com/india/corporate/other-taxes](https://taxsummaries.pwc.com/india/corporate/other-taxes))
  *(catalog §12 "Tax Type ∈ … State (SGST/UTGST)")*

### 2.3 Party & stock-item GST details — catalog §12 "Masters"

- **RQ-7 — Party GST details.** A party (Sundry Debtor/Creditor) ledger SHALL carry optional GST details:
  **Registration Type** (Regular / Unregistered / Consumer), **GSTIN/UIN** (validated per RQ-3 when
  Registration Type = Regular), and **State**. A party with **no GSTIN** (Unregistered/Consumer) is a **B2C**
  party (DP-8). *(catalog §12 "Party ledgers")*
- **RQ-8 — Stock-item GST details.** A Stock Item SHALL carry: **HSN/SAC** code, **Taxability**
  (Taxable / Nil-Rated / Exempt / Non-GST), **GST rate %** (the integrated rate; the CGST and SGST rates are
  each derived as half — RQ-12), and **Type of Supply** (Goods / Services → SAC for services). The Phase-3
  placeholder HSN/taxability fields (RQ-6/RQ-8 of Phase 3, then inert) become **active** here.
  *(catalog §12 "Stock items"; phase3 RQ-6 "captured, inert until Phase 4")*
- **RQ-9 — Sales/purchase-ledger GST details.** A sales or purchase ledger MAY carry GST details (HSN/SAC,
  taxability, rate) so a **service** or an accounting-only supply (no stock item) can still resolve a rate
  (see RQ-10, DP-11). *(catalog §12 "sales-purchase ledgers")*
- **RQ-10 — Rate resolution (core, most-granular-wins).** For a taxable line the effective GST rate SHALL
  resolve in order **Stock Item → Sales/Purchase Ledger → Company default**, the **most granular non-null
  wins** (DP-6). The 5-level catalog chain (adding Stock Group and GST Classification) is **narrowed** to
  these three in Phase 4; the wider chain is Phase 9. *(catalog §12 "Rate resolution"; DP-6)*

### 2.4 Intra vs inter determination + tax computation — catalog §12 "Transactions"

- **RQ-11 — Intra vs inter routing.** For a GST supply the system SHALL determine the nature of supply by
  comparing **company home State** (supplier location, RQ-2) with the **place of supply** (Phase 4: the
  **party State**, RQ-7): **same State/UT ⇒ intra-state ⇒ CGST + SGST/UTGST**; **different State/UT ⇒
  inter-state ⇒ IGST**. *Law source:* IGST Act §8 (intra-state = supplier and place of supply in the same
  State/UT) and §7 (inter-state = different States/UTs).
  ([caclubindia.com/articles/place-of-supply-under-gst-53456.asp](https://www.caclubindia.com/articles/place-of-supply-under-gst-53456.asp))
  *(catalog §12 "Routing rule")*
- **RQ-12 — Tax computation & split.** On a taxable line with taxable value **V** and rate **r%**:
  **intra-state** SHALL post **CGST = round_paisa(V × r/2 %)** and **SGST/UTGST = round_paisa(V × r/2 %)**;
  **inter-state** SHALL post **IGST = round_paisa(V × r %)**. Per line, **IGST amount = CGST + SGST** for the
  same V and r. *Law source:* CGST and SGST are each half of the IGST (integrated) rate; IGST rate = CGST +
  SGST. ([cleartax.in/s/gst-rates](https://cleartax.in/s/gst-rates))
  *(catalog §12 "party State = company State → CGST+SGST (each = rate ÷ 2); different → IGST (full rate)")*
- **RQ-13 — Mixed-rate invoice (per-line tax).** For an invoice with **multiple items at different rates**,
  tax SHALL be computed **per line** on that line's taxable value at that line's resolved rate, then
  aggregated per tax head (Σ CGST, Σ SGST, Σ IGST) into the tax-ledger postings — never a single blended
  rate on the invoice total. *(catalog §12 "splitting proportionally across mixed-rate items")*
- **RQ-14 — Taxable (assessable) value.** The taxable value **V** for a line SHALL be the item taxable value
  (qty × rate) as computed by the Phase-3 item engine, plus/minus any line discount, and (Phase 4 seam) any
  cost line flagged "include in assessable value". GST is charged on **V**, not on V-plus-tax. *(catalog §12
  "Computed on assessable value")*
- **RQ-15 — Non-taxable lines.** For a **Nil-Rated / Exempt / Non-GST** item/line, the system SHALL post
  **zero tax** and SHALL still record the taxable/exempt value for the returns (GSTR-1 nil/exempt columns,
  GSTR-3B exempt outward). *(catalog §12 "Nil-rated / Exempt / Non-GST")*

### 2.5 GST invoice ⟂ item-invoice pairing invariant — catalog §10/§12; engine §VoucherValidator

- **RQ-16 — Additive tax on an item-invoice.** On a **GST sales (F8) or purchase (F9) item-invoice**, the
  GST tax lines SHALL post to the **Duties & Taxes** tax ledgers (RQ-4) **in addition to** the Phase-3 stock
  leg, such that the **item-invoice pairing invariant is preserved unchanged**: the stock leg still equals
  **Σ item taxable value** (Σ qty × rate), and the tax ledgers are **excluded** from that equality. The
  party (debtor/creditor) total SHALL equal **taxable value + total tax (+ round-off)**. Formally, on a
  sale: **Dr Party (V + tax)** = **Cr Sales (V)** + **Cr Output CGST + Cr Output SGST** (intra) *or*
  **Cr Output IGST** (inter); the existing `EnsureItemInvoiceValid` check (Σ stock-leg credit == Σ item
  value) SHALL continue to pass because tax ledgers are neither Sales-Accounts nor Stock-in-Hand.
  *(catalog §10 pairing; `src/Apex.Ledger/Services/VoucherValidator.cs` `EnsureItemInvoiceValid`; ER-8)*
- **RQ-17 — Balanced GST voucher.** A GST voucher SHALL remain **Σ Dr = Σ Cr** to the paisa after the tax
  and any round-off line are added (the golden invariant is unaffected by GST). *(catalog §1; VoucherValidator
  `IsBalanced`)*
- **RQ-18 — Purchase-side (Input) GST.** A GST **purchase** item-invoice SHALL post **Input** CGST/SGST or
  IGST (Dr, ITC ledgers) additive to the stock-in leg — **Dr Purchases/Stock (V) + Dr Input taxes** =
  **Cr Supplier (V + tax)** — mirroring RQ-16 on the debit side. The purchase-side pairing (Σ debit stock
  leg == Σ item value) SHALL still hold with Input-tax ledgers excluded. *(catalog §10/§12; VoucherValidator)*
- **RQ-19 — Rounding.** Tax SHALL be **paisa-exact** (ER-2). Per **DP-4**, tax is rounded **per line** to the
  paisa (matching per-line computation, RQ-13); an optional **invoice round-off** line MAY round the invoice
  **grand total** to the nearest rupee, posted to a Round-Off ledger, keeping the voucher balanced (RQ-17).
  *(catalog §12; DP-4)*

### 2.6 Tax Analysis & GST reports — catalog §12 "Verify on voucher" + "Returns & reports"

- **RQ-20 — Tax Analysis (Alt+A).** From an open GST voucher, **Alt+A** SHALL show a **Tax Analysis**:
  per-line taxable value, resolved rate, and the CGST/SGST/IGST amounts, plus invoice totals. It is a
  read-only projection of the same computation that posts the tax (RQ-12). *(catalog §12 "Alt+A Tax Analysis")*
- **RQ-21 — GSTR-1 (outward supplies).** The system SHALL produce **GSTR-1** as a read-only projection over
  posted **outward** (Sales, Debit/Credit-Note) GST vouchers in a period, sectioned into at least: **B2B**
  (party has a GSTIN), **B2C** (no GSTIN), a **rate-wise tax summary**, and an **HSN summary** (qty & value &
  tax grouped by HSN/SAC). *Law source:* GSTR-1 = statement of **outward supplies**, filed monthly or
  quarterly. ([tutorial.gst.gov.in/userguide/returns/GSTR_1.htm](https://tutorial.gst.gov.in/userguide/returns/GSTR_1.htm);
  [gstcouncil.gov.in GST flyer ch.33](https://gstcouncil.gov.in/sites/default/files/e-version-gst-flyers/51_GST_Flyer_Chapter33.pdf))
  *(catalog §12 "GSTR-1 … B2B, B2C … HSN Summary"; VR item 19 HSN summary confirmed)*
- **RQ-22 — GSTR-3B (summary).** The system SHALL produce **GSTR-3B** as a read-only summary over a period:
  **outward tax by head** (Σ CGST, Σ SGST, Σ IGST on sales), **eligible ITC** (Σ Input CGST/SGST/IGST on
  purchases), and **net tax payable** = outward tax − eligible ITC per head (a *computation*, not a posted
  set-off — see DP-9). *Law source:* GSTR-3B = a **summary return** declaring summary GST liabilities and ITC
  for a tax period. ([tutorial.gst.gov.in/userguide/returns/GSTR3B.htm](https://tutorial.gst.gov.in/userguide/returns/GSTR3B.htm))
  *(catalog §12 "GSTR-3B (summary)")*
- **RQ-23 — Return periodicity.** GSTR-1 (and the paired GSTR-3B election) SHALL respect the company's
  **Monthly / Quarterly (QRMP)** periodicity (RQ-2) when defining the report period. *Law source:* GSTR-1 is
  filed monthly (turnover > ₹1.5 cr) or quarterly (QRMP) — the report period boundary.
  ([cleartax.in/s/gstr-3b-vs-gstr-1-comparison](https://cleartax.in/s/gstr-3b-vs-gstr-1-comparison))
  *(catalog §12 "Periodicity of GSTR-1 Monthly/Quarterly")*
- **RQ-24 — Drill-down.** Every GSTR-1/3B/Tax-Analysis figure SHALL **drill down (Enter)** to the underlying
  voucher(s), matching the app-wide "any report figure Enters to its voucher" rule. *(plan.md §1.1
  drill-down; catalog §16)*
- **RQ-25 — GST slabs are configuration.** The GST **rate slabs** SHALL be **seeded configuration**, not
  hardcoded constants, so the slab set can be maintained without code change. Phase 4 seeds the **current
  GST 2.0 set: 0 / 5 / 18 / 40 %** (see §3 law note and DP-2). *(plan.md §3.0 "GST slabs config-driven"; §10 C-9)*

### 2.7 Keyboard & navigation — catalog §12, §21

- **RQ-26 — GST config nav.** F11 → **Taxation → Enable GST** SHALL open the company GST-details screen
  (RQ-2) in the Miller-column hierarchy (Company → Features → Taxation). *(catalog §12, §20)*
- **RQ-27 — GST masters nav.** GST tax ledgers live under **Masters → Accounting → Ledgers** (Under = Duties
  & Taxes); party/stock-item GST details are **sub-screens** of the existing ledger / stock-item create/alter
  screens — not new top-level nodes. *(plan.md professional-hierarchy rule; catalog §12)*
- **RQ-28 — GST reports nav.** GSTR-1, GSTR-3B, and HSN Summary live under **Reports → Statutory → GST
  Reports** (a new "GST Reports" node), cascading Miller-column, each drill-down-able (RQ-24). *(catalog §12
  "Display More Reports → GST Reports"; plan.md cascading-nav rule)*
- **RQ-29 — Tax Analysis shortcut.** **Alt+A** SHALL be bound on an open GST voucher to the Tax Analysis
  (RQ-20), reproduced on the right-hand button panel, keyboard-only reachable (NFR-2). *(catalog §12)*

---

## 3. Verified GST law (as of FY 2025-26 / today 2026-07-05) — cited

> **R7 discipline:** every fact below is web-verified against an official/current source and cited; none is
> asserted from memory. Summaries are in our own words (no copyrighted text reproduced).

| # | Law fact (verified) | Source |
|---|---|---|
| L-1 | **GST 2.0 slabs are 0 / 5 / 18 / 40 %** — the 12% and 28% slabs were removed; most 12% items moved to 5%, most 28% to 18%, and a new **40%** rate replaced 28%+cess on luxury/sin goods. Niche **3%** (bullion/jewellery) and **0.25%** (rough diamonds) remain. | [cleartax.in/s/gst-rates](https://cleartax.in/s/gst-rates); [cashfree.com/blog/new-gst-rates](https://www.cashfree.com/blog/new-gst-rates/) |
| L-2 | **Effective 22 September 2025**, approved at the **56th GST Council meeting (3 Sep 2025)**, per CBIC notification. | [cleartax.in/s/gst-rates](https://cleartax.in/s/gst-rates); [tallysolutions.com/gst/gst-rates](https://tallysolutions.com/gst/gst-rates/) |
| L-3 | **Intra-state supply → CGST + SGST**; **inter-state supply / imports → IGST**. Determined by comparing **location of supplier** and **place of supply**: same State/UT = intra (IGST Act §8), different = inter (IGST Act §7). | [caclubindia.com place-of-supply](https://www.caclubindia.com/articles/place-of-supply-under-gst-53456.asp) |
| L-4 | **CGST and SGST are each half of the IGST rate**; **IGST rate = CGST rate + SGST rate** (e.g. 18% ⇒ 9% + 9% intra, or 18% IGST inter). | [cleartax.in/s/gst-rates](https://cleartax.in/s/gst-rates); [bajajfinserv.in/gst-rates-in-india](https://www.bajajfinserv.in/gst-rates-in-india) |
| L-5 | **UTGST** applies in Union Territories (without legislature) in place of SGST, at the same rate; SGST/UTGST is the one "State/UT" tax head. | [taxsummaries.pwc.com/india/corporate/other-taxes](https://taxsummaries.pwc.com/india/corporate/other-taxes) |
| L-6 | **GSTIN = 15 chars**: [2-digit **state code**][10-char **PAN**][1 **entity code**][1 default char, normally **'Z'**][1 **checksum** (Luhn mod 36)]. | [cleartax.in/s/know-your-gstin](https://cleartax.in/s/know-your-gstin) |
| L-7 | **HSN** (goods) / **SAC** (services) classify supplies; HSN-digit count scales with turnover; an **HSN summary** is part of GSTR-1. | [tutorial.gst.gov.in GSTR-1](https://tutorial.gst.gov.in/userguide/returns/GSTR_1.htm) |
| L-8 | **GSTR-1** = statement of **outward supplies** (sales), filed **monthly** (turnover > ₹1.5 cr) or **quarterly (QRMP)**. | [tutorial.gst.gov.in GSTR-1](https://tutorial.gst.gov.in/userguide/returns/GSTR_1.htm); [cleartax GSTR-3B vs GSTR-1](https://cleartax.in/s/gstr-3b-vs-gstr-1-comparison) |
| L-9 | **GSTR-3B** = a **summary return** declaring summary output-tax liability and ITC for the period; portal auto-populates parts of 3B from GSTR-1. | [tutorial.gst.gov.in GSTR-3B](https://tutorial.gst.gov.in/userguide/returns/GSTR3B.htm) |
| L-10 | **Compensation Cess** still exists in law (esp. tobacco), continuing until compensation-loan obligations are discharged — **but is out of Phase-4 scope** (Phase 9). | [cleartax.in/s/gst-rates](https://cleartax.in/s/gst-rates) |

> **Slab decision (plan.md Open Q2 / §10 C-9, "re-verify at Phase 4 kickoff"):** the re-verification is
> **done here** and resolves Q2 — **GST 2.0 (0/5/18/40) is now the live, official slab set** (L-1/L-2), so
> Phase 4 seeds **0/5/18/40** (not the legacy 0/5/12/18/28). Slabs stay **config-driven** (RQ-25) so a
> future council change is a data edit. This is a **plan-relevant update**: plan.md §3.0/§9.2 still says
> "seed classic 0/5/12/18/28 now, add 5/18/40 after CBIC confirmation" — that confirmation **now exists**,
> so the recommended seed flips to 0/5/18/40 (logged for the user, R6).

---

## 4. Engineering rules (ER)

- **ER-1 — Idempotent v12→v13 migration.** Phase 4 SHALL add SQLite **schema v13** via a `MigrateV12ToV13`
  block that runs inside one transaction bumping `schema_version` to 13, using only `ALTER TABLE … ADD
  COLUMN` (with GST-off / NULL defaults) + new `CREATE TABLE`/`CREATE INDEX` (GST config, tax-ledger tags,
  party/item GST details, HSN table) — **never** rewriting existing rows. A fresh DB is stamped straight to
  v13 via the consolidated create DDL. Follows `MigrateV1ToV2 … MigrateV11ToV12` exactly.
  *(mirrors `src/Apex.Persistence.Sqlite/Schema.cs` `CurrentVersion = 12`)*
- **ER-2 — Paisa-exact tax; integer rate scale.** Every GST tax **amount** SHALL be computed and stored as
  **INTEGER paisa** (via `Money.RoundToPaisa` / `IsPaisaExact`, already in `Money.cs`) — never REAL/float. A
  GST **rate** SHALL be stored as an exact scaled integer (**basis points**, e.g. 18% = 1800 bp; 2.5% =
  250 bp) so `CGST = V × rate/2` is exact and half-rate splitting never loses a paisa. *(NFR-3; `Money.cs`)*
- **ER-3 — Deterministic tax split.** For fixed (taxable value, rate, intra/inter), the tax computation
  SHALL be a **pure function** yielding a deterministic paisa-exact result: intra ⇒ CGST = SGST =
  round_paisa(V × rate/2); inter ⇒ IGST = round_paisa(V × rate). Where V × rate/2 has a sub-paisa tail, the
  **defined rounding** (DP-4, per-line, away-from-zero to match `Money.RoundToPaisa`) SHALL apply
  identically to CGST and SGST so **CGST == SGST** always holds. *(RQ-12; `Money.RoundToPaisa`)*
- **ER-4 — GST logic lives in `Apex.Ledger`.** All GST domain types (GST config, tax-ledger classification,
  party/item GST details, rate resolution, intra/inter routing, tax computation, GSTR-1/3B projections)
  SHALL live in `Apex.Ledger` (framework-agnostic, **no** Avalonia/DB/clock/RNG dependency) and be
  **unit-testable** via xUnit exactly like the accounting/inventory core. *(plan.md §1.1/§3; NFR-6)*
- **ER-5 — Rate resolution is pure & total.** Rate resolution (RQ-10) SHALL be a pure function of
  (item, ledger, company) returning a resolved rate or an explicit "non-taxable"/"unresolved" result — never
  a silent default that would post wrong tax; an unresolved taxable line is a fail-fast domain error at
  posting. *(RQ-10; implementation.md fail-fast)*
- **ER-6 — GSTIN validation is pure & fail-fast.** GSTIN structural + checksum validation (RQ-3) SHALL be a
  pure, unit-tested function; an invalid GSTIN is rejected at the master-save boundary with a clean domain
  error (never persisted). *(RQ-3; NFR-3 fail-fast)*
- **ER-7 — Returns are read-only projections.** GSTR-1, GSTR-3B, HSN summary, and Tax Analysis SHALL be
  computed **from already-posted vouchers** (no separate mutable "return" store to drift); re-running a
  report over the same vouchers SHALL be deterministic. *(RQ-21/22; testing.md)*
- **ER-8 — Item-invoice pairing invariant preserved.** GST tax SHALL be **additive** and post **only** to
  Duties & Taxes tax ledgers, which are **excluded** from the `EnsureItemInvoiceValid` stock-leg sum
  (`Σ Sales-credit / Purchases-debit == Σ item value`). No change to that check SHALL be required; a test
  SHALL prove a GST item-invoice still passes it (RQ-16/18). *(`VoucherValidator.EnsureItemInvoiceValid`)*
- **ER-9 — Cess seam, no Cess logic.** The tax model SHALL leave a **seam** for a fourth "Cess" tax head
  (nullable rate/ledger, unused in Phase 4) so Phase 9 adds Cess without reshaping the tax record — but
  **no Cess computation** ships in Phase 4 (L-10; §1.3). *(forward-compat; plan.md Phase 9)*
- **ER-10 — GST is opt-in; zero regression.** With F11 GST **off**, none of the above activates and the
  Phase-1/2/3 posting, validation, and report paths SHALL be **bit-for-bit unchanged**; the non-GST Robert
  and Bright fixtures SHALL stay green (§5). *(R8; NFR-3)*
- **ER-11 — No "Tally" in GST code/UI.** No GST field label, ledger name, report title, error message, or
  identifier SHALL contain the word "Tally"; the product reads **Apex Solutions** throughout. *(project rule)*
- **ER-12 — Delete-guard tax ledgers.** A GST tax ledger SHALL **not** be deletable while referenced by a
  posted voucher, mirroring the existing ledger delete-guard. *(existing delete-guard convention)*

---

## 5. Decision points (DP) — defaults recommended for A13/user approval

> Each DP states the ambiguity, our recommended default (grounded in Tally behaviour + the verified law),
> and a one-line rationale. **These require the user's approval before implementation** (R12).

- **DP-1 — Auto-create tax ledgers on enabling GST.** *Ambiguity:* seed the 6 GST tax ledgers automatically
  or make the user create them. **Recommend: auto-create** Output & Input CGST/SGST/IGST under Duties & Taxes
  on F11-enable (idempotent; skip if already present). *Rationale:* matches the corpus flow (a user raises a
  GST invoice immediately after enabling GST) and removes a fidelity foot-gun; the ledgers are ordinary
  editable/renamable ledgers.
- **DP-2 — Seed slab set.** *Ambiguity:* seed legacy 0/5/12/18/28 or GST 2.0 0/5/18/40. **Recommend: seed
  GST 2.0 = 0 / 5 / 18 / 40 %** (config-driven, RQ-25). *Rationale:* GST 2.0 is the **live, official** set
  since 22-Sep-2025 (L-1/L-2), and this Phase-4 re-verification is exactly the C-9 checkpoint the plan
  scheduled; slabs stay editable data so a future change is not a code change.
- **DP-3 — Tax-ledger naming.** *Ambiguity:* what to name the auto-created ledgers. **Recommend:**
  **"Output CGST" / "Output SGST" / "Output IGST"** (sales/liability side) and **"Input CGST" / "Input SGST"
  / "Input IGST"** (purchase/ITC side), all under Duties & Taxes. *Rationale:* unambiguous, de-branded,
  matches accountant convention (output = liability, input = credit) and keeps GSTR-3B mapping obvious.
- **DP-4 — Per-line vs per-invoice rounding.** *Ambiguity:* round tax per line or on the invoice total.
  **Recommend: compute & round tax PER LINE to the paisa** (RQ-13/ER-3), then an **optional invoice
  round-off** line to the nearest rupee on the grand total. *Rationale:* per-line matches mixed-rate
  computation and how each line's tax must appear in GSTR-1; the invoice round-off keeps the customer total
  tidy without disturbing per-line tax. (Per-tax-ledger rounding *method* choice is Phase 9 — VR item 143.)
- **DP-5 — Default HSN length.** *Ambiguity:* how many HSN digits to require/store. **Recommend: store the
  HSN/SAC as free text but validate to a valid length (4 / 6 / 8 digits), defaulting the input hint to
  6 digits;** do not hard-enforce a turnover-based minimum in Phase 4. *Rationale:* HSN-digit requirements
  scale with turnover (L-7); a 6-digit default fits most registered dealers, and strict turnover-based
  enforcement is a Phase-9 GSTR-1 validation concern.
- **DP-6 — Rate-resolution chain in Phase 4.** *Ambiguity:* the full 5-level chain or a subset. **Recommend:
  Stock Item → Sales/Purchase Ledger → Company default (3 levels, most-granular-wins);** defer Stock Group
  and GST Classification levels to Phase 9. *Rationale:* the item and ledger levels cover the overwhelming
  majority of real invoices; the extra two levels add reusable-template machinery that belongs with Phase-9
  breadth (GST Rate Setup bulk screen, VR item 141).
- **DP-7 — Place of supply source in Phase 4.** *Ambiguity:* full place-of-supply rules (§10–§14 IGST Act:
  billing vs shipping, service-specific rules) or a simple driver. **Recommend: use the party's State as the
  place of supply for goods** (the common case); a **shipping-address override** and service-specific POS
  rules are Phase 9. *Rationale:* keeps core routing correct for standard B2B/B2C goods invoices (L-3)
  without pulling the full POS rule matrix into core; the party-State field already exists (RQ-7).
- **DP-8 — Party with no GSTIN = B2C.** *Ambiguity:* how to classify a party lacking a GSTIN. **Recommend:
  treat Registration Type ∈ {Unregistered, Consumer} (or a blank GSTIN) as **B2C**;** such supplies go to
  the **B2C** section of GSTR-1 and pay CGST+SGST/IGST normally by place of supply. *Rationale:* matches the
  law (B2C = supply to unregistered person) and the catalog's B2B/B2C split; the fuller **B2C-Large vs
  B2C-Small** interstate split (> ₹2.5 L) is a Phase-9 GSTR-1 refinement.
- **DP-9 — GSTR-3B: compute-only vs post the set-off (the plan.md divergence).** *Ambiguity:* plan.md §5
  Phase 4 names "ITC set-off per Rule 88A" + Alt+J/Ctrl+F, but the charter keeps Phase 4 core. **Recommend:
  Phase 4 GSTR-3B COMPUTES and DISPLAYS eligible ITC and net tax payable (arithmetic only); the Rule-88A
  cross-utilisation ENGINE and the Alt+J stat-adjustment / Ctrl+F stat-payment POSTING move to Phase 9.**
  *Rationale:* a core-GST user needs to *read* a correct 3B (output tax − ITC = payable), but a
  half-implemented cross-utilisation/posting engine is exactly the advanced breadth Phase 9 owns; this keeps
  the phase boundary clean and is logged for go/no-go (§1.3 reconciliation).
- **DP-10 — GST on as-voucher sales (not just item-invoices).** *Ambiguity:* does Phase-4 GST apply only to
  item-invoices or also to **accounting-only** (as-voucher) sales/purchases (e.g. a service billed without a
  stock item). **Recommend: support both** — an as-voucher sale/purchase resolves its rate from the **sales/
  purchase ledger's** GST details (RQ-9) and posts additive tax the same way (RQ-16 without a stock leg).
  *Rationale:* services and expense reimbursements are commonly billed as-voucher; the tax computation is
  identical (V × rate), and excluding them would be a real fidelity gap. Item-invoice GST is the headline,
  but as-voucher GST is cheap and correct to include.
- **DP-11 — Tax direction from voucher base type.** *Ambiguity:* how the engine knows output vs input tax.
  **Recommend: derive it from the voucher base type** — Sales/Credit-Note ⇒ **Output** tax (liability);
  Purchase/Debit-Note ⇒ **Input** tax (ITC) — never a manual per-voucher toggle. *Rationale:* deterministic,
  matches the existing Purchase⇒inward / Sales⇒outward direction stamping (`VoucherValidator`), and keeps
  GSTR-1 (outward) vs GSTR-3B-ITC (inward) sourcing unambiguous.

---

## 6. Bright / Robert impact (Phase 4 must NOT break existing fixtures)

> Both baseline fixtures are **non-GST**. GST is **opt-in** (F11 off by default). Phase 4 must add GST as a
> pure extension that leaves the accounts-only and inventory-integrated paths untouched (ER-10).

Phase 4 SHALL prove, to the paisa (NFR-3):

- **GR-1 — Robert stays green (accounts-only).** The 13-voucher accounts-only **Robert** fixture SHALL
  reproduce its known Trial Balance / P&L / Balance Sheet totals **unchanged**; with F11 GST off, no GST
  code path executes. *(R8)*
- **GR-2 — Bright stays green (inventory-integrated).** The **Bright** trading fixture (opening stock,
  purchases/sales, derived closing stock ₹15,000, BS Stock-in-Hand ₹15,000, gross profit ₹15,000, net profit
  −₹1,000 under `ClosingStockMode.InventoryDerived`) SHALL remain green; the Phase-3 item-invoice pairing
  invariant SHALL still pass because Bright posts **no** GST. *(R8; phase3 BR-1..BR-6)*
- **GR-3 — Item-invoice invariant intact for a NEW GST fixture.** A **new** Phase-4 GST fixture (a small
  registered trading company) SHALL post a GST item-invoice and prove: stock leg == Σ item value (invariant
  passes, ER-8); party total == taxable + tax; the voucher balances (RQ-17); intra splits CGST==SGST and
  CGST+SGST==the inter-state IGST for the same value/rate (RQ-12); and GSTR-1/3B reproduce the worked figures
  exactly. *(new golden set; plan.md Phase 4 exit gate "a golden-set of GST invoices produces exact
  GSTR-1/3B figures")*
- **GR-4 — No fixture edit required.** Robert and Bright JSON fixtures SHALL require **no edit** to stay
  green (GST columns default off/NULL under the v12→v13 migration, ER-1). *(ER-1/ER-10)*

---

## 7. Sign-off checklist (A13 / CA ticks to close Phase 4)

> One line per RQ-group + the law-verification gate + the Bright/Robert gate + the R9/R11 gate items. A13
> signs only when **every** box is ticked with shown evidence (tests green displayed, app run, review
> passed, de-branded, committed).

- ☐ **1. Enable & configure GST (RQ-1..RQ-3)** — F11 Enable GST; company GST details (State, Reg Type =
  Regular, GSTIN, applicable-from, periodicity); 15-char GSTIN + Luhn-mod-36 validation — implemented,
  tested, catalog-faithful.
- ☐ **2. Tax ledgers (RQ-4..RQ-6)** — CGST/SGST/IGST tax ledgers under Duties & Taxes, auto-created on
  enable (DP-1), UTGST parity — implemented and tested.
- ☐ **3. Party & item GST details (RQ-7..RQ-10)** — party (Reg Type/GSTIN/State), stock-item (HSN/SAC,
  taxability, rate, supply type), sales/purchase-ledger GST, 3-level rate resolution (DP-6) — implemented
  and tested.
- ☐ **4. Intra/inter routing + computation (RQ-11..RQ-15)** — company-State vs party-State routing;
  CGST/SGST halves vs full IGST; per-line mixed-rate; taxable value; nil/exempt/non-GST zero-tax —
  implemented and unit-tested (CGST==SGST; CGST+SGST==IGST).
- ☐ **5. GST invoice ⟂ pairing invariant (RQ-16..RQ-19)** — additive tax preserves the item-invoice
  invariant (ER-8); Input GST on purchase; balanced voucher; paisa-exact per-line rounding + invoice
  round-off (DP-4) — implemented and tested.
- ☐ **6. Tax Analysis & reports (RQ-20..RQ-25)** — Alt+A Tax Analysis; GSTR-1 (B2B/B2C/rate-wise/HSN);
  GSTR-3B (outward tax, eligible ITC, net payable per DP-9); periodicity; drill-down; config-driven slabs
  (0/5/18/40) — implemented and tested against a golden GST set.
- ☐ **7. Keyboard & navigation (RQ-26..RQ-29)** — F11 GST config, GST masters under Ledgers/Stock-Item
  sub-screens, GST Reports node under Reports → Statutory, Alt+A bound — keyboard-only reachable, Miller-
  column hierarchy.
- ☐ **8. Engineering rules (ER-1..ER-12)** — idempotent v12→v13 migration; paisa-exact tax + bp rate scale;
  deterministic pure split; GST logic Avalonia-free; pure total rate-resolution; pure fail-fast GSTIN;
  read-only return projections; pairing invariant preserved; Cess seam only; opt-in zero-regression;
  no "Tally"; tax-ledger delete-guard — all satisfied.
- ☐ **9. Law verified (§3 L-1..L-10)** — slabs (0/5/18/40, eff. 22-Sep-2025), intra/inter split, half-rate
  CGST/SGST, UTGST, GSTIN structure, HSN/SAC, GSTR-1/3B purpose & periodicity — each cited to an official/
  current source; **Open Q2 / C-9 slab re-verification recorded** (seed 0/5/18/40).
- ☐ **10. Decision points (DP-1..DP-11)** — each default **approved by the user** (R12) before build, or the
  approved variant recorded in `memory.md`; **DP-9 plan.md-divergence resolution explicitly approved**.
- ☐ **11. Bright/Robert gate (GR-1..GR-4)** — Robert & Bright stay green with **no** fixture edit; a new GST
  golden fixture proves invariant-intact + exact GSTR-1/3B. *(hard gate)*
- ☐ **12. Tests green — shown.** Full unit + integration suite green (displayed), including Robert & Bright
  and the new GST golden set, per R9.
- ☐ **13. Review passed.** Code Reviewer + Verification/Completeness Critic adversarial pass, no open
  findings, fidelity-matrix gap list re-derived (R9/R10).
- ☐ **14. De-branded.** No occurrence of the word "Tally" in shipped app UI or code; product reads "Apex
  Solutions" throughout (project rule).
- ☐ **15. Committed by the GitHub Expert.** Small conventional commits tied to Phase-4 plan items, pushed by
  A12 (the **only** git actor, R4); `memory.md` updated (R5), including the C-9 slab decision.

---

*Traceability: every RQ cites its catalog §/VR item; every law fact (§3) cites an official/current source;
ERs cite the `Schema.cs` (`CurrentVersion = 12`), `Money.cs`, and `VoucherValidator.EnsureItemInvoiceValid`
conventions + NFRs; the Bright/Robert gate maps to `tests/Apex.Ledger.Tests/Fixtures/{robert,bright}.json`
and the `ClosingStockMode.InventoryDerived` seam. This doc fills SRS §4.4. Any build-time deviation — and
the DP-9 / C-9 decisions — are logged in `memory.md` with their reason (R6).*
