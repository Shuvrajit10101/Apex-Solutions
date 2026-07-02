# Tally Prime — Feature Catalog (Clone Requirements Backbone)

> **Purpose.** This is the consolidated, developer-facing inventory of every feature Tally Prime
> offers, distilled from the 10 Tally Prime / Tally.ERP9 reference PDFs in `tally/`. It is the
> **requirements backbone** for building a faithful clone. Each section lists the module's purpose,
> navigation, data-model entities & fields, voucher types, keyboard shortcuts, reports, and
> configuration toggles, plus **clone-notes** calling out behaviour a developer must replicate.
>
> **Sources studied** (extracted via `pdftotext -layout`, then read by 5 parallel study agents):
> Tally-Prime-Book (433 pp, primary), Tally-Prime-Study-Guide (283 pp), Tally-Prime-with-GST-Notes
> (×3), Tally-Book + Fundamentals-of-Accounting, Case-Study-1 + Practical-Problems, Short-Key ref.
>
> **Fidelity markers used below:** `⚠verify` = fact has OCR ambiguity or edition drift and is being
> cross-checked against authoritative Tally docs in the verification pass. `[legacy]` = pre-GST
> feature, likely out of scope for a modern clone.

### ✅ Verification status (2026-07-02)

The `⚠verify` facts were cross-checked against official TallyHelp / EPFO / GST sources; full
corrections and a missing-feature list are in
[tally-feature-catalog-verification-report.md](tally-feature-catalog-verification-report.md). Key resolutions:

1. Exactly **28** predefined groups — *Bank OCC A/c* is an **alias of Bank OD A/c** (sub-group of
   *Loans (Liability)*), not a 29th group; the 15-Primary / 13-Sub split in §3 is confirmed correct.
2. **Physical Stock** has **no dedicated key** — reached via **F10 → Other Vouchers** (Debit Note
   **Alt+F5**, Credit Note **Alt+F6**, Contra **F4**, Payment **F5**, Receipt **F6** all confirmed).
3. GST **ITC set-off** follows **Rule 88A**: IGST credit is used first (against IGST, then CGST/SGST in
   any order/proportion); CGST↔SGST can never be cross-used — supersedes the fixed order in §12.
4. **GSTR-4 = annual**, **CMP-08 = quarterly**; **GSTR-2A / 2B** are auto-drafted ITC statements
   (download & reconcile), not filed returns; e-Invoice (IRN/QR) and e-Way Bill work online **and**
   offline (JSON) — the §12 `⚠verify` flags are cleared.
5. **VAT / CST / Excise / Service Tax remain** as optional `F11` legacy modules (not removed post-GST) —
   still out of scope for a modern clone, but present in real Tally Prime.
6. Current release is **TallyPrime 7.0 (19-Dec-2025)**; the modern baseline the training PDFs miss
   includes **Edit Log / audit trail**, **Connected GST**, **IMS** (Invoice Management), **graphical
   dashboards**, **WhatsApp sharing**, **Go To multi-tasking**, **Save View**, and **More Details**.
7. Employer PF is **computed**, not a flat 3.67%: EPS = 8.33% × min(wage, ₹15,000) capped ₹1,250;
   employer-EPF = (12% × PF-wage) − EPS. Other §14 payroll constants confirmed.
8. **GST 2.0** (22-Sep-2025) is reported to have restructured slabs to **5 / 18 / 40 %** (from
   0/5/12/18/28) — this is `⚠` **not yet confirmed against an official CBIC notification**; confirm
   which slab set to target before relying on it.

---

## 0. Product overview & platform

Tally Prime is a **keyboard-first, single-window desktop double-entry accounting + inventory + statutory
(GST/TDS/TCS/Payroll) system** for Indian SMBs. Defining UX characteristics a clone must preserve:

- **Gateway of Tally (GOT)** is the home hub. Everything is reached by menus or shortcuts from here.
- **Two universal action verbs**: **Create** / **Alter** (edit) applied to every master & voucher.
- **Keyboard-driven**: nearly every action has an `F`/`Alt`/`Ctrl` shortcut; mouse optional.
- **Right-hand button bar** exposing context actions (the shortcuts).
- **Go To (`Alt+G`)** and **Switch To (`Ctrl+G`)** for jump-anywhere navigation.
- **`F11` = Company Features** (enable modules), **`F12` = Configuration** (context-sensitive options).
- **Drill-down everywhere**: any report figure `Enter`s down to the underlying voucher.
- **Multi-company**, single financial-year-centric data, Indian FY (1-Apr → 31-Mar) default.

**Target platform for the clone** — to be decided (see companion scope doc). Options: desktop (Electron/
Tauri/.NET), web (SPA + API), or cross-platform. The catalog is platform-agnostic.

---

## 1. Accounting domain model (foundations)

The clone's core engine is a **double-entry ledger**. Fundamentals the corpus teaches:

- **Account types & golden rules** (classical classification the UI/help should honour):
  - **Personal** (persons, firms, banks, capital): *Debit the receiver, credit the giver.*
  - **Real** (assets/cash/goods): *Debit what comes in, credit what goes out.*
  - **Nominal** (expenses, losses, incomes, gains): *Debit expenses & losses, credit incomes & gains.*
- **Double entry**: every voucher balances Dr = Cr. Tally shows entries as **"To" (Cr)** / **"By" (Dr)**
  by default; `F12 → Use Cr/Dr instead of To/By` switches the labels.
- **Reporting chain**: Vouchers → Ledgers → **Trial Balance** → **Trading & P&L** → **Balance Sheet**.
- **Dr/Cr nature by group** drives sign conventions and where a balance lands in the financial statements.

**Clone-note:** model the ledger posting engine first (accounts, groups with nature, vouchers with
balanced entries, opening balances Dr/Cr). All reports are projections over this ledger.

---

## 2. Company management

- **Purpose:** the top-level entity owning all books.
- **Navigation:** `GOT → Alt+K (Company) → Create / Alter / Select / Shut / Delete`; also `Alt+F3`
  (select), `Ctrl+F3` (shut), `F3` (change company).
- **Create fields:** Data Directory (storage path), Name, Mailing Name (auto, editable), Address,
  Country (India), State, Pin; Contact (Telephone, Mobile, Fax, Email, Website); **Financial year
  begins from** (default 1-Apr), **Books beginning from** (mid-year start supported); Security Control
  (TallyVault password, Control User Access); **Base Currency** (Symbol default ₹, Formal Name INR,
  suffix symbol?, space between amount & symbol?, show amount in millions?, decimal places = 2,
  word after decimal = "Paisa", decimal places for amount in words).
- **After save** → Company Features (`F11`) screen appears; "Show more / all features" reveal toggles.
- **Group Company** (multi-company consolidation): `Alt+K → Create → Alt+R`; select **Member Companies**;
  produces **Consolidated Balance Sheet / P&L**; compare members via `Alt+N` (Auto Column) or `Alt+C`
  (New Column). Requires matching base currency & master names across members. Deleting group keeps members.
- **Clone-note:** company = tenant/dataset boundary; all masters & vouchers scope to a company.

---

## 3. Chart of Accounts — Groups & Ledgers

### Groups (classification hierarchy)
- **28 predefined groups = 15 Primary + 13 Sub-groups**, auto-created per company. Custom groups nest
  under any of these (or a custom parent).
- **15 Primary** (9 Balance-Sheet, 6 P&L): Capital Account, Loans (Liability), Current Liabilities,
  Fixed Assets, Investments, Current Assets, Branch/Divisions, Misc. Expenses (Asset), Suspense A/c,
  Sales Accounts, Purchase Accounts, Direct Incomes, Indirect Incomes, Direct Expenses, Indirect Expenses.
- **13 Sub-groups** (with canonical parent): Reserves & Surplus *(Capital Account)*; Bank OD A/c,
  Secured Loans, Unsecured Loans *(Loans Liability)*; Duties & Taxes, Provisions, Sundry Creditors
  *(Current Liabilities)*; Bank Accounts, Cash-in-Hand, Deposits (Asset), Loans & Advances (Asset),
  Stock-in-Hand, Sundry Debtors *(Current Assets)*.
- `⚠verify` **"Bank OCC A/c"** appears in one source's alphabetic list — confirm whether it's a 29th
  predefined sub-group or a naming variant of Bank OD A/c in current Tally Prime.
- **Fields:** Name, Alias, Under (parent). Predefined groups cannot be deleted.

### Ledgers (transactional accounts)
- **2 default ledgers auto-created:** **Cash** and **Profit & Loss A/c**. Multiple cash-type ledgers
  allowed (e.g. Petty Cash); only one P&L A/c.
- **Fields:** Name, Alias, **Under (group)**, Opening Balance (Dr/Cr); plus feature-gated blocks:
  Maintain balances **bill-by-bill** + Default Credit Period + "check credit days"; **Interest
  Calculation** parameters; **Bank details** (A/c holder, A/c no., IFSC, SWIFT, cheque printing);
  **GST/TDS/TCS** statutory sub-screens; Currency/Country (foreign parties); "Cost centres applicable?".
- **Creation modes:** single (`Create → Ledger`), **multi** (`Chart of Accounts → Ledgers → Alt+H
  Multi-Master → Multi Create/Alter`), **inline during voucher** (`Alt+C`).
- **Delete guards:** cannot delete a ledger/group with transactions, sub-groups, or contained ledgers;
  cannot alter closing balance of Stock-in-Hand ledgers.
- **Clone-note:** seed every new company with the 28 groups (with nature + parent) and the 2 ledgers.

---

## 4. Accounting vouchers

- **Entry point:** `GOT → Vouchers → [F-key]`. **Modes** toggled by **`Ctrl+H` (Change Mode)**.
- **Core types & default shortcuts** `⚠verify` shortcuts against current Tally Prime:

| Voucher | Shortcut | Use |
|---|---|---|
| Contra | **F4** | Cash↔Bank / Bank↔Bank transfers |
| Payment | **F5** | Money out (cash/cheque/online) |
| Receipt | **F6** | Money in |
| Journal | **F7** | Non-cash adjustments (depreciation, provisions, credit asset purchase) |
| Sales | **F8** | Sales (Item / Accounting / As-Voucher modes) |
| Purchase | **F9** | Purchases (Item / Accounting / As-Voucher modes) |
| Credit Note | **Alt+F6** | Sales return / customer credit |
| Debit Note | **Alt+F5** | Purchase return / supplier debit |
| Stock Journal | **Alt+F7** | Inter-godown transfer / manufacturing |
| Physical Stock | **F10** → Physical Stock | Physical count reconciliation |
| Sales Order | **Ctrl+F8** | Order from customer (non-accounting, non-inventory) |
| Purchase Order | **Ctrl+F9** | Order to supplier (non-accounting, non-inventory) |
| Delivery Note | **Alt+F8** | Goods out vs Sales Order (stock only) |
| Receipt Note (GRN) | **Alt+F9** | Goods in vs Purchase Order (stock only) |
| Rejection Out | **Ctrl+F5** | Goods returned to supplier (stock only) |
| Rejection In | **Ctrl+F6** | Goods returned by customer (stock only) |

- **Additional predefined types (via `F10 Other Vouchers → Show Inactive`):** Memorandum,
  Reversing Journal, Job Work In Order, Material In, Job Work Out Order, Material Out, Attendance,
  Payroll — bringing the total to **24 predefined voucher types**.
- **Invoice modes** (Purchase/Sales/Debit/Credit Note): **Item Invoice** (stock lines), **Accounting
  Invoice** (ledger lines only — services/fixed assets), **As Voucher** (classic Dr/Cr).
- **Single vs Double entry** (Contra/Payment/Receipt): single-entry semantics — Payment: Cr=Account,
  Dr=Particulars; Receipt/Contra: Dr=Account, Cr=Particulars.
- **Voucher Type master** (`Create → Voucher Type`): Name, base type, Abbreviation, Active?, Method of
  Voucher Numbering (Automatic/Manual/None), Use Common Narration, Print after saving, **Use for POS
  invoicing**, **Use as Manufacturing Journal** (Stock Journal), **Use for Job Work**, **Track
  Additional Costs**, **Allow zero-valued transactions**, Name of Class (voucher classes), print
  messages/title/declaration.
- **Universal save:** **`Ctrl+A`**. Delete voucher: **`Alt+D`**. Cancel voucher: **`Alt+X`**.
- **Clone-note:** every voucher = header (type, no., date, party, narration) + balanced entry lines
  (± optional inventory allocations, bill-wise, cost-centre, GST sub-allocations).

---

## 5. Bill-wise accounting (receivables / payables)

- **Enable:** `F11 → Bill-wise Details = Yes`; ledger `Maintain balances bill-by-bill = Yes` + Default
  Credit Period + "check credit days during entry".
- **4 reference types** (`Type of Ref`): **New Ref** (new bill), **Agst Ref** (settle a pending bill
  from the list), **Advance** (advance pay/receive, no due date), **On Account** (unallocated/suspense).
- **Reports:** `Display More Reports → Statements of Accounts → Outstandings → Receivables / Payables /
  Ledger / Group`; overdue analysis; `Alt+G → Receivables & Payables`.
- **Clone-note:** bill references carry the **GST-inclusive** amount; ageing = due-date arithmetic.

---

## 6. Cost Categories & Cost Centres

- **Enable:** `F11 → Enable Cost Centres`.
- **Cost Category:** Name, Allocate Revenue Items (Y/N), Allocate Non-Revenue Items (Y/N) — ≥1 must be
  Yes. Default: **"Primary Cost Category"**.
- **Cost Centre:** Category, Name, Under (Primary or parent centre) — hierarchical.
- **Usage:** ledger "Cost centres applicable = Yes" (auto for income/expense); at entry, amount pops a
  **Cost Allocation** window (Category → Centre → Amount, multiple per line).
- **Reports:** Category Summary, Cost Centre Break-up, Ledger/Group Break-up, Monthly Summary.

---

## 7. Budgets, Scenarios, Reversing Journals, Memoranda, Interest

- **Budgets:** `Create → Budget`: Name, Under (Primary), Period From/To; set budgets **On Groups** and
  **On Ledgers** (Type: On Closing Balance / On Nett Transactions, Amount). Compare actual vs budget via
  Trial Balance / P&L columns.
- **Scenarios + Reversing Journals + Optional vouchers:** what-if/provisional entries that don't touch
  real books. **Reversing Journal** has an "Applicable upto" date; **Memorandum** holds suspense
  entries convertible to real vouchers; **Optional** voucher (`Ctrl+L`) excluded until made regular.
  Scenario master (`Create → Scenario`: Include Actuals?, Include/Exclude voucher types) surfaces them
  in report columns (`Alt+C`).
- **Interest Calculation:** `F11` + ledger `Activate Interest Calculation`; simple/compound; Advance
  parameters — Rate, Per (30-day/365-day/calendar month/year), On (all/Dr/Cr balance), Applicability
  (always/post-due), Calc From (applicability/due/effective date), Rounding. Auto-post via Debit/Credit
  Note **voucher class** "Use Class for Interest Accounting".

---

## 8. Banking

- **Bank Reconciliation (BRS):** `GOT → Banking → Bank Reconciliation → select bank`, or ledger `Alt+R`;
  enter **Bank Date** per line; screen shows *Balance as per Company Books* vs *as per Bank*.
- **Cheque printing:** ledger → enable cheque printing + configuration (bank format); print via
  `Banking → Cheque Printing`.
- **Bank Allocation:** on bank payments/receipts — transaction type (cheque/NEFT/RTGS/…), instrument
  no./date — feeds BRS and statutory challans.
- **Other banking tools referenced:** deposit slips, payment advice, post-dated cheque management.

---

## 9. Inventory masters

- **Stock Group:** Name, Alias, Under, "Should quantities be added?", Set/Alter GST (group-level rate).
- **Stock Category:** parallel, independent classification axis (not nested under groups).
- **Units of Measure:** **Simple** (Symbol, Formal Name, UQC for GST, decimals 0–4) or **Compound**
  (First Unit × Conversion Factor + Second/Tail Unit), e.g. Dozen = 12 Pcs, Kg = 1000 g, Box = 20 Pcs.
- **Godown / Location:** default **"Main Location"**; hierarchical; flag "Our stock with third party"
  for job-work godowns.
- **Stock Item:** Name, Alias, Under (group), Category, Units; Opening Balance + godown/batch allocation;
  GST details (HSN/SAC, rate, taxability, type of supply); Batch tracking; **BOM**; **Reorder levels**;
  TCS applicability.
- **Creation modes:** single / multi (`Alt+H`) / inline (`Alt+C`).
- **Clone-note:** inventory valuation methods (FIFO/Avg/…) drive stock value in reports and integrate
  with accounts when "Integrate Accounts & Inventory" is on.

---

## 10. Inventory vouchers & order processing

- **Order-to-invoice (purchase):** Purchase Order (`Ctrl+F9`) → Receipt Note/GRN (`Alt+F9`) → Purchase
  (`F9`); tracking numbers link the chain. **Rejection Out** (`Ctrl+F5`) for returns to supplier.
- **Order-to-invoice (sales):** Sales Order (`Ctrl+F8`) → Delivery Note (`Alt+F8`) → Sales (`F8`);
  **Rejection In** (`Ctrl+F6`) for customer returns.
- **Effect rules:** PO/SO affect **neither** accounts nor stock; GRN/Delivery affect **stock only**;
  Purchase/Sales affect both. Delivery Note carries dispatch details (transporter, LR/RR no., vehicle).
- **Stock Journal (`Alt+F7`):** Source (Consumption) vs Destination (Production) — inter-godown transfer,
  manufacturing, wastage.
- **Physical Stock (`F10` → Physical Stock):** record counted qty; system posts the book-vs-physical adjustment.
- **Reports:** Order books (outstanding/all), Receipt/Delivery Note registers, Stock Transfer Journal
  register, Physical Stock register.

---

## 11. Advanced inventory

- **Batches & expiry:** stock item "Maintain in Batches / Track Mfg Date / Use Expiry"; batch/lot no.,
  mfg & expiry dates at entry; batch reports; expiry-based valuation.
- **Bill of Materials (BOM) & Manufacturing:** define components per finished unit (Type of Item:
  Component / By-Product / Co-Product / Scrap; qty or rate%). **Manufacturing Journal** = Stock Journal
  voucher type with "Use as Manufacturing Journal = Yes"; captures product, BOM, qty, godown, and
  **additional cost** components (labour/freight/overhead).
- **Additional cost of purchase:** allocate packing/freight/cartage into item cost — voucher type
  "Track Additional Costs", ledger method Appropriate **by Quantity / by Value**; blends into stock rate.
- **Zero-valued transactions:** free goods — inventory affected, accounts not (voucher type toggle).
- **Actual vs Billed quantity:** `F11` toggle; Actual updates stock, Billed drives accounts/GST (schemes
  like "buy 5 get 1 free").
- **Price Levels & Price Lists:** tiered pricing (Wholesale/Retail/…) with quantity-slab rates &
  discount %; sales voucher "Price Level" auto-applies.
- **Reorder levels:** simple (reorder qty, min order qty) or advanced (with period + higher/lower
  criteria); **Reorder Status** report suggests POs.
- **Point of Sale (POS):** POS voucher type ("Use for POS invoicing"); **`Alt+I`** toggles Single vs
  **Multi-Mode Payment** (Gift Voucher / Card + card no. / Cheque / Cash with tendered & change);
  tender ledgers mapped to correct groups; print/preview receipt.
- **Job Work:** Job Work In/Out Orders + Material In/Out vouchers; principal↔job-worker stock tracking;
  "Allow Consumption" on Material In; third-party godowns.

---

## 12. GST (Goods & Services Tax)

**The statutory centrepiece.** A clone targeting India must implement this thoroughly.

### Enable & configure
- `F11 → Enable GST`; Company GST Details: State (drives local vs interstate), **Registration Type**
  (Regular / Composition / Unregistered / Consumer), GSTIN/UIN, GST applicable-from, **Periodicity of
  GSTR-1** (Monthly/Quarterly), Set/Alter GST rate, Enable tax liability on **advance receipt**, on
  **reverse charge**, **GST classifications**, LUT/Bond details, **e-Way Bill** (threshold default
  ₹50,000; basis = invoice value / taxable+exempt / taxable only; intra-state applicability),
  **e-Invoicing** (applicable-from, default report period, send e-Way Bill with e-Invoice).
- **Composition** extra fields: tax rate for turnover (1% traders/mfg, 5% restaurants, 6% services),
  basis (taxable / taxable+exempt+nil), enable purchase/RCM tax rate.

### Rate resolution (5 levels, most-granular wins)
Company → Stock Group → **Stock Item** (most common) → Ledger → **GST Classification** (reusable HSN/rate
template). `⚠verify` exact override precedence.

### Masters
- **Tax ledgers** under **Duties & Taxes**, Type of Duty = GST, Tax Type ∈ {Central (CGST), State
  (SGST/UTGST), Integrated (IGST), **Cess**}; Cess valuation by value or quantity.
- **Party ledgers:** Registration Type, GSTIN/UIN, State; flags — e-commerce operator, deemed export,
  Party Type (SEZ/Embassy), is transporter.
- **Stock items / sales-purchase ledgers:** HSN/SAC, Taxability (Taxable/Nil-Rated/Exempt/Non-GST),
  Calculation Type (On Value / **On Item Rate** for slab-rate goods like footwear), rates
  (Integrated/Central/State/Cess %), Type of Supply (Goods/Services), reverse-charge applicable,
  ineligible-for-ITC, Nature of Transaction (imports/exports/SEZ/deemed).

### Transactions
- **Routing rule (core logic):** party State = company State → **CGST + SGST** (each = rate ÷ 2);
  different State → **IGST** (full rate). Computed on **assessable value** (item value + cost lines
  flagged "include in assessable value").
- **Tax entry model:** user selects the **duty ledger(s)** as extra voucher lines; Tally auto-computes
  the amount from item rates & values, splitting proportionally across mixed-rate items in one invoice.
- **Verify on voucher:** **`Alt+A` / `Ctrl+I` Tax Analysis** → `Alt+F1` detailed.
- **Scenarios covered:** B2B; **B2C Large** (interstate, unregistered, > ₹2.5 L) vs **B2C Small**;
  Nil-rated / Exempt / Non-GST; **RCM** (unregistered purchase, notified goods/services, imports);
  **Imports** (Bill of Entry, port code, BCD, ITC on IGST only); **Exports** (Taxable / LUT-Bond /
  Exempt / Nil — shipping bill); **SEZ** (with/without LUT); **Deemed Exports**; services;
  **advance receipt** GST (services, via `Alt+J → Advance Receipt`); **advance payment RCM**; discounts.
- **Adjustments & payment:** ITC set-off & liability via Journal **`Alt+J` (Stat Adjustment)**; GST
  payment via Payment **`Ctrl+F` (Autofill) → Stat Payment** (CPIN/CIN/BRN).
- **ITC set-off order** `⚠verify` current rules: IGST → (IGST, then CGST, then SGST); CGST → (CGST, then
  IGST); SGST → (SGST, then IGST); no CGST↔SGST cross-use.

### Returns & reports
`Display More Reports → GST Reports`: **GSTR-1** (outward: B2B, B2C-L, B2C-S, CDN-registered, Nil,
Export sections), **GSTR-2** (inward), **GSTR-3B** (summary), **GSTR-4 / CMP-08** (composition),
**GSTR-9 / 9A** (annual). `⚠verify` **GSTR-2A / 2B** (auto-drafted) support & **HSN Summary** report.
e-Way Bill export = **JSON**; e-Invoice = IRN/QR (toggle present; workflow depth `⚠verify`).

### Key constants
GSTIN = 15 chars `[2 state][10 PAN][entity][Z][checksum]`; slabs **0 / 5 / 12 / 18 / 28 %** + Cess;
composition turnover limit ₹1.5 cr (₹75 L NE/HP); HSN digits scale with turnover.

---

## 13. TDS & TCS

- **TDS (Tax Deducted at Source):** `F11 → Enable TDS`; company Deductor details (TAN, deductor type,
  responsible person, surcharge/cess); **Nature of Payment** master (Section e.g. 194J, rates with/
  without PAN, threshold). Ledgers: expense "Is TDS applicable + Nature"; party "Deductee type + PAN +
  deduct in same voucher"; duty ledger under Duties & Taxes (Type = TDS). Flow: deduct via Journal →
  pay party (Agst Ref) → deposit via Payment `Ctrl+F` Stat Payment. **Challan Reconciliation** (`Alt+R`).
  Report: **Form 26Q** (`Alt+B` to save return). No-PAN → 20%.
- **TCS (Tax Collected at Source):** mirror of TDS; **Nature of Goods** (Section 206C, e.g. scrap,
  timber, liquor); stock item / sales ledger / buyer ledger TCS flags; Sales voucher auto-computes;
  deposit via Stat Payment. Report: **Form 27EQ**; certificate Form 27D.
- **Clone-note:** both follow the same shape — statutory master (nature + rate + threshold) → applicability
  flags on ledgers/items → auto-computation → challan/return reporting.

---

## 14. Payroll

- **Enable:** `F11 → Maintain Payroll + Enable Payroll Statutory`; statutory details for **PF** (codes),
  **ESI** (codes, standard working days default 26), **NPS**, **Income Tax** (TAN, circle/ward,
  responsible person).
- **Masters:** Employee Category (revenue/non-revenue allocation), Employee Group (define salary?),
  **Employee** (joining date, PAN/Aadhaar/UAN/PF/ESI/PRAN, bank, contract, passport/visa), Payroll
  Units (simple/compound), Attendance/Production Types (Attendance/Leave-with-pay, Leave-without-pay,
  Production, User-defined Calendar).
- **Pay Heads:** Earnings (Basic, DA, HRA, Conveyance, Transport, Overtime, Bonus, Variable) with
  Calculation Type (On Attendance / Flat Rate / As Computed Value / On Production / User Defined) and
  formulas/slabs; Employee Deductions (Professional Tax slab, Income Tax, PF/ESI/NPS employee);
  Employer Contributions (**EPF 3.67%**, **EPS 8.33%** or flat ₹1250 > ₹15k, **ESI 3.25%**, NPS);
  Employer Other Charges (**EDLI 0.5%**, EDLI Admin, PF Admin 1.10%); Payable heads (Salary/PF/ESI
  Payable under Current Liabilities).
- **Salary structure:** define at Employee Group and/or Employee (Copy from Parent/Group / Start Afresh).
- **Process:** **Attendance voucher** (`F10 → Attendance`, or `Ctrl+F` Autofill) → **Payroll voucher**
  (`Ctrl+F4`, Autofill: Salary / PF / ESI / NPS contribution) → **Payment voucher** (`F5`, Autofill:
  Salary Payment / PF Challan / ESI Challan / PT Payment) with per-employee bank allocation.
- **Reports:** Payslip, Pay Sheet, Attendance Sheet, Payment Advice, Payroll Register/Statement, Employee
  Profile/Head Count, Expat (passport/visa/contract expiry). **Statutory:** PF (Form 5/10/12A/3A/6A/ECR),
  ESI (Form 3/5/6/monthly), Professional Tax, NPS, Gratuity, Income Tax computation.
- **Key constants:** EPF employee 12%; PF/EPS ceiling ₹15,000; ESI wage ceiling ₹21,000 (≥10 employees);
  Gratuity (15 × last salary × years)/26; PT max ₹2,500/yr. `⚠verify` exact current thresholds.

---

## 15. Legacy statutory modules `[legacy]`

The corpus (older editions) documents **VAT, Service Tax, Excise (dealer/manufacturer), CST**. These are
**superseded by GST (1-Jul-2017)** and are **out of scope** for a modern Tally Prime clone unless
historical fidelity is explicitly required. Captured here only so the scope decision is explicit.
`⚠verify` whether current Tally Prime still ships any VAT/Excise for legacy states/edge cases.

---

## 16. Reports & financial statements

- **Primary statements:** **Balance Sheet** (`GOT → Balance Sheet`; `Alt+F1` detailed / condensed;
  horizontal/vertical; show %; exclude zero-balances), **Profit & Loss A/c** (`Alt+C` add column;
  stock valuation method), **Trial Balance**, **Day Book** (chronological all-vouchers).
- **Report families (`Display More Reports`):** Account Books (Cash/Bank Book, Ledger, per-voucher
  registers), Inventory Books (Stock Summary, Group/Category/Item, Godown, Batch, Movement/Ageing,
  Reorder Status), Statements of Accounts (Outstandings, Interest, Cost Centres, Statistics, **Tally
  Audit**), Statements of Inventory, Exception Reports (Memo/Reversing/negative stock/cash), GST/TDS/TCS
  Reports, Payroll & Payroll Statutory Reports, Job Work Reports.
- **Cross-cutting report actions:** drill-down (`Enter`), period change (`Alt+F2`), new/auto column
  (`Alt+C` / `Alt+N`), filter/range (`Alt+F12`), detailed/condensed (`Alt+F1`), export (`Alt+E`),
  email (`Ctrl+M`), print (`Ctrl+P`), configure (`F12`).
- **Ratio Analysis** dashboard; **Cash/Funds Flow**; **Comparative/columnar** period analysis.

---

## 17. Printing, export/import, email

- **Print:** `Ctrl+P` (voucher/report); invoice print config (title, declaration, logo top-left — JPEG/
  BMP ~96×80 px, enabled via `Alt+E → Configuration → include company logo`).
- **Export:** `Alt+E` → Master / Transactions / current report; formats include **PDF, Excel/Spreadsheet,
  XML, JSON, HTML**; folder path & file name configurable. (Tally master/transaction interchange uses
  **XML**.)
- **Import:** `Alt+O` → Master / Transactions (XML); duplicate behaviour: Combine Opening Balance /
  Ignore Duplicates / Modify with new data.
- **Email:** `Ctrl+M` per voucher/report; SMTP profile (server e.g. Gmail, port, SSL/TLS); PDF attach.

---

## 18. Security & administration

- **TallyVault:** `Alt+K → TallyVault` — password-encrypts company data (option to keep unencrypted copy).
- **Security Control:** `Alt+K → Security` — Control User Access; Administrator username/password; browser-
  access email; **Enable Tally Audit**; disallow Educational-mode opening.
- **User Roles:** custom levels (e.g. Manager) with basic-facility inheritance, back-dated-voucher limits/
  cut-off, print-before-save rules, override-tax permission, allow/disallow feature lists, Tally.NET auth.
- **Users & Passwords:** map users→roles; allow browser/remote access; allow TDL.
- **Password Policy:** min strength (default 8), expiry (default 90 days), old-password restriction,
  change-on-first-login.
- **Tally Audit:** track master/voucher changes (audit trail) — `Statement of Accounts → Tally Audit`.

---

## 19. Data management

- **Backup / Restore:** `Alt+Y → Backup / Restore` (destination path, select companies); archive naming
  pattern e.g. `TBK900_10016.001`.
- **Split company data** by financial year: `Alt+Y → Split → Verify Data` (must be error-free) → `Split
  Data` (split-from date) → two per-year companies + original retained. Pre-reqs: backup, journalise
  unadjusted forex, clear pending bills.
- **Group company** consolidation (see §2).
- **Repair / rewrite** company data (`Ctrl+Alt+R`).

---

## 20. Configuration model

- **`F11` Company Features** (per-company module switches): Cost Centres, Bill-wise, Interest
  Calculation, GST, TDS, TCS, Payroll (+ statutory), Separate Actual/Billed Qty, Discount Column,
  Batches, Multiple Price Levels, Multi-currency, Cheque printing, e-Way Bill / e-Invoice, etc.
- **`F12` Configuration** (context-sensitive, per-screen): To/By vs Dr/Cr, Maintain balance bill-by-bill,
  Advance interest parameters, BOM component types, appropriation method, discount ledger auto-calc,
  common ledger for item allocation, show more/all GST details, and hundreds more.
- **Clone-note:** feature flags gate which fields/vouchers/reports appear — a first-class settings layer.

---

## 21. Keyboard shortcut reference (reconciled — `⚠verify` against current build)

**Global:** `Alt+G` Go To · `Ctrl+G` Switch To · `Alt+K` Company menu · `F11` Features · `F12` Configure ·
`Alt+F3` Select company · `Ctrl+F3` Shut company · `F3` Change company · `Alt+Y` Data (backup/restore/
split) · `Alt+E` Export · `Ctrl+M` Email · `Ctrl+P` Print · `Alt+F2` Change period · `F2` Change date ·
`Ctrl+N` Calculator · `Ctrl+A` Accept/Save · `Alt+D` Delete · `Alt+X` Cancel voucher · `Ctrl+Q` Quit
window · `Alt+F4` Close.
**Masters:** `Alt+H` Multi-Master · `Alt+C` Create-on-the-fly / New Column · `Alt+V` Alter from drilldown.
**Vouchers:** `F4` Contra · `F5` Payment · `F6` Receipt · `F7` Journal · `F8` Sales · `F9` Purchase ·
`Alt+F5` Debit Note · `Alt+F6` Credit Note · `Alt+F7` Stock Journal · Physical Stock via `F10` ·
`Ctrl+F8` Sales Order · `Ctrl+F9` Purchase Order · `Alt+F8` Delivery Note · `Alt+F9` Receipt Note ·
`Ctrl+F5` Rejection Out · `Ctrl+F6` Rejection In · `Ctrl+F4` Payroll · `F10` Other Vouchers · `Ctrl+H`
Change Mode · `Ctrl+L` Optional · `Ctrl+T` Post-dated · `Alt+A` Tax Analysis / Add column · `Alt+J` Stat
Adjustment · `Ctrl+F` Autofill · `Alt+I` Insert / POS payment-mode toggle.
**Reports:** `Alt+F1` Detailed/Condensed · `Alt+F2` Period · `Alt+C`/`Alt+N` Column · `Alt+F12` Filter ·
`Enter` Drill-down · `F5` toggle views.

> **Note:** the `tally/659947760-Short-Key.pdf` coaching handout has **garbled shortcut mappings**
> (e.g. it lists F7=Payment, F8=Stock Journal) that contradict the authoritative Book/Study-Guide. The
> table above follows the Book/Study-Guide; the verification pass confirms against official docs.

---

## 22. Seed data appendix (for a fresh company)

- **28 predefined groups** with nature & parent (see §3) — seed on company create.
- **2 default ledgers:** Cash (Cash-in-Hand), Profit & Loss A/c.
- **Default Cost Category:** "Primary Cost Category". **Default Godown:** "Main Location".
- **24 predefined voucher types** with base type + default shortcut + numbering (see §4).
- **Base currency:** ₹ / INR, 2 decimals, "Paisa".
- **Financial year:** 1-Apr → 31-Mar (India).

---

## 23. Scope, open questions & to-verify

**High-value must-haves (MVP core):** company; 28 groups + ledgers; accounting vouchers (Contra/Payment/
Receipt/Journal/Sales/Purchase) with modes; bill-wise; inventory masters + stock vouchers; **GST** (regular
intrastate/interstate, GSTR-1/3B); Balance Sheet / P&L / Trial Balance / Day Book / Stock Summary /
Outstandings; backup/restore.

**Phase-2+:** order/job-work, advanced inventory (BOM/batch/POS/price levels/reorder), cost centres,
budgets/scenarios, interest, TDS/TCS, Payroll, multi-currency, group company, security/roles, Tally Audit,
e-Way Bill / e-Invoice, GST advanced (RCM/imports/exports/SEZ/composition/annual returns).

**Explicitly deferred / out of scope (pending decision):** legacy VAT/Service Tax/Excise `[legacy]`;
TDL add-on language; Tally.NET remote/ODBC/API; mobile & browser Tally.

**To-verify (`⚠verify`) — resolved in the verification pass:** canonical 28-group names & the "Bank OCC
A/c" question; exact current voucher shortcuts; GST-2A/2B & HSN-summary support; current ITC set-off
order; current PF/ESI/gratuity thresholds; whether any legacy tax module still ships.

**Two ready-made regression fixtures** (from the practical problems) for the ledger engine: *Robert*
(transport, accounts-only, 13 deterministic vouchers) and *Bright* (trading, opening balances +
depreciation + closing stock).
