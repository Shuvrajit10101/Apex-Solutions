# Voucher Entry Specification — behavioural spec and condition-level gap analysis

**Author:** A1 (Business Analyst) · **Date:** 2026-08-01 · **Status:** for review

> ## 🔴 † STALENESS NOTICE — added 2026-08-15
> This spec is dated **2026-08-01** and the code has moved a long way under it. It was already flagged as stale
> in three places by `docs/tally-fidelity-defects.md` §6; that flag is now itself stale. **At HEAD `c56e5c3`:**
> - **G-1, G-2, G-6 and G-7 are FIXED.** G-2 in particular — cost allocation is now **parallel sets, not a
>   partition** (`VoucherValidator.cs:338-345`, `:376-387`; commit `aed9a50`), so the corpus's own ₹5,000
>   Branch/Department/Executive example **posts**. The line below at §2 C-27 and every "our engine rejects it"
>   statement about cost categories is out of date.
> - **G-6's follow-on is fixed too:** Payment/Receipt/Contra now **open** in Single Entry
>   (`VoucherEntryViewModel.SeedOpeningMode` `:141-144`, called `:1194`; commit `f277318`), so §2 step 2's
>   *"No Single-Entry mode exists anywhere in `src/`"* is **FALSE**. Its two locators also drifted:
>   Ctrl+I `MainWindow.axaml.cs:481` → **`:563`**, Ctrl+H `:493` → **`:577`** (and Ctrl+H is now a real
>   Change-Mode key gated on `vm.IsChangeModeEntry`, not an Accounting-Invoice toggle).
> - **§2 step 1's G-4 claim is FALSE.** `MainWindowViewModel.cs:2756-2757` no longer exists; commit `7bfc2c6`
>   routed every voucher route through `VoucherTypeResolver.ResolveForEntry`, and `VoucherTypeResolver.cs:58`
>   **never returns an inactive type**. There is no fallback. (`F10 > Show Inactive` still does not exist —
>   zero hits in `src/` — so the gate is now a *refusal*, not a silent pass-through.)
> - **§1.3's `Alt+D` claim is FALSE** — corrected inline below.
> - **`F12Configure` `MainWindowViewModel.cs:6403-6428` → `:6669-6694`**, fall-through at `:6693`.
> - **The `Accept? Yes/No` line is now partly stale:** WI-11 shipped the prompt for **master** screens
>   (`MainWindowViewModel.cs:4882-4891`, ~24 screens); vouchers still save silently. See `invented-vs-cloned.md`
>   IV-27. The gap line moved from `:96` to **`:93`**.
> **⇒ Treat every `file:line` in this document as unverified.** The two fidelity registers were re-verified on
> 2026-08-15; this spec was not, beyond the points listed here.
**Fidelity target:** TallyPrime (user decision 2026-08-01). Tally.ERP 9 behaviour appears only where
labelled *historical*, never as the spec.

## How to read this document

This is a **behavioural** spec. It describes what happens on a screen, in order, under which conditions —
not which features exist. Section 2 (the condition matrix) is the centrepiece; everything else supports it.

**Sources and citation convention.** All fidelity claims are cited. Corpus files live in
`C:\Users\dkpho\OneDrive\Desktop\Apex Solutions(end)\tally\` (git-ignored, never committed):

| Tag | File | Note |
|---|---|---|
| **BOOK** | `664311548-Tally-Prime-Book.pdf` | printed page numbers, not PDF page numbers |
| **SG** | `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` | printed page numbers |
| **GSTN** | `703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf` | extracted-text line numbers |
| **TB2** | `719244897-Tally-Book.pdf` | |
| **TPP** | `654430402-Tally-Practical-Problems.pdf` | |
| **WEB** | `help.tallysolutions.com` | **always labelled inline as WEB** |

Claims I read myself with `pdftotext -layout` during this analysis are marked **[verified-A1]**. Claims
inherited from the upstream research agents and not independently re-read are marked **[inherited]**.
Anything I could not establish is marked **UNVERIFIED** and appears again in §8 — never silently dropped.

**Our-side citations** are `file:line`, rooted at the worktree
`C:\Users\dkpho\OneDrive\Desktop\Apex Solutions(end)\.claude\worktrees\mystifying-volhard-88445c\`.

> ⚠️ **Discard `tally/659947760-Tally-Prime-Short-Key.pdf` wholesale.** It maps F6=Contra, F7=Payment,
> F8=Stock Journal, Ctrl+A=Zoom, Alt+A=Save. The BOOK's own table of contents **[verified-A1]** gives
> F4 Contra · F6 Receipt · F5 Payment · F9 Purchase · F8 Sales · F7 Journal · Alt+F6 Credit Note ·
> Alt+F5 Debit Note · Ctrl+F9 Purchase Order · Alt+F9 Receipt Note · Ctrl+F8 Sales Order ·
> Alt+F8 Delivery Note · Alt+F7 Stock Journal · Ctrl+F7 Physical Stock · Ctrl+F6 Rejection In ·
> Ctrl+F5 Rejection Out. Every other corpus file agrees. The shortcut PDF is mis-typeset. **Our app's
> F-key map already matches the correct one** (`src/Apex.Desktop/Views/MainWindow.axaml.cs:620-690`,
> `:800-812`) — no change needed, and this note exists so nobody "fixes" it against the bad PDF.

---

## 1. The generic entry pipeline, and where it branches

### 1.1 The four-layer gate (the architectural rule everything else depends on)

TallyPrime gates a voucher field through **four independent layers**. A field appears only when all four
permit it:

1. **F11 Company Features** — *capability*. Off ⇒ the feature's fields exist nowhere, and its dependent
   options vanish from every master and every F12.
2. **F12 on a MASTER screen** — *field visibility on that master*. Turning an F11 feature on does **not**
   guarantee the master field shows. The corpus states this trap at least five separate times
   **[verified-A1 for two of them]**: "If the option is not appear, press F12 (Configure) and 'Maintain
   balance Bill-by-Bill' set to Yes" (SG p.91 step 3); "If you don't see Manufacturing & Expiry Option in
   Stock-Item Creation Screen, then Press F12 & Enable Batches" (BOOK p.130). **[inherited]** three more at
   BOOK p.109, p.113, p.134, p.186, p.236.
3. **The master's own field value** — *per-object applicability*. Bill-wise on in F11 does nothing until a
   specific ledger says "Maintain balances bill-by-bill: Yes".
4. **F12 on the VOUCHER screen** — *field visibility and behaviour*, per voucher type, further scoped by
   "Show more configurations" / "Show all configurations".

The study guide states the general form: masters vary per the features enabled under company features, and
a feature may also be enabled from the master creation screen (SG p.63 **[inherited]**).

**F11 itself has two visibility toggles**, "Show more features" and "Show all features", both default No.
They *hide* options; they do not reset them. A feature set to Yes and then hidden **stays Yes and keeps
acting on voucher entry** (SG p.60 **[inherited]**).

> **Our position on the four-layer model.** We implement **layers 3 and 4 only, partially**, and we have
> **no layer-2 concept at all**. Layer 1 exists for six features and is missing for the rest (§2.1).
> This is the structural root of most of the condition gaps below: a clone that collapses layers 1 and 2
> into one switch shows the wrong screen to every user.

### 1.2 The pipeline, step by step

```
  route in  →  type activation gate  →  Ctrl+H mode  →  header  →  party  →  party sub-screens
     →  line entry  →  per-amount sub-screens  →  tax/advisory bands  →  narration  →  Accept
```

| # | Step | TallyPrime | Ours |
|---|---|---|---|
| 0 | **Route in** | `Gateway > Vouchers` + F-key, or `Alt+G (Go To) > Create Voucher` (SG p.123 **[inherited]**) | Miller-column cascade + F-key. Alt+G equivalent not surveyed here. |
| 1 | **Type activation gate** | Many types ship **inactive**. `F10 (Other Vouchers) > Show Inactive > select > Activate this Voucher Type: Yes`. Corpus shows this for Rejection In (BOOK p.51 **[verified-A1]**), Rejection Out (BOOK p.53 **[verified-A1]**), Memorandum (p.45), Reversing Journal (p.47), the four order/note types, Material In/Out **[inherited]** | **No gate.** `MainWindowViewModel.cs:2756-2757` resolves `FirstOrDefault(t => t.BaseType == baseType && t.IsActive)` **then falls back to an inactive type anyway**. F10 opens a fixed "Other Vouchers" menu (`MainWindow.axaml.cs:813-814`), not a Show-Inactive list. |
| 2 | **Ctrl+H — Change Mode** | **One key, a picker.** Payment/Receipt/Contra → Single ⟷ Double Entry. Purchase/Sales/Credit Note/Debit Note → Item Invoice / Accounting Invoice / As Voucher. (BOOK pp.29, 31–34, 54–55 **[verified-A1]**; SG pp.76, 78–82 **[verified-A1]**) | **Two keys, toggles.** Ctrl+I = Item Invoice (`MainWindow.axaml.cs:481`), Ctrl+H = Accounting Invoice (`:493`). No Single-Entry mode exists anywhere in `src/`. |
| 3 | **Date** | **Cursor skips it.** Prefilled; `F2` to change. Every worked exercise opens with "Press F2 to change the date" (GSTN passim **[inherited]**). `Alt+F2` changes the *company period*, not the voucher date. | F2 targets the voucher date via `ISetsWorkingDate` (`VoucherEntryViewModel.cs:33-37`). Skip-on-tab not surveyed. |
| 4 | **Voucher number** | Per the type master's `Method of voucher numbering`: Automatic (skipped, non-editable) / Automatic (Manual Override) / Manual / None (WEB: `use-voucher-numbering-methods`; corpus only ever shows Automatic **[inherited]**) | `NumberingMethod` Automatic/Manual/None + prefix/suffix/width/zero-pad/prevent-duplicate (`VoucherType.cs`). **"Automatic (Manual Override)" is absent**; FY-restart is deferred. |
| 5 | **Party / Account** | Single Entry: `Account` (the one cash/bank side) then `Particulars` (the many side). **Polarity inverts by type** — Receipt/Contra: Account = Dr; Payment: Account = Cr (BOOK pp.29, 32 **[verified-A1]**). Invoice modes: `Party's A/c Name`. `Alt+C` creates the ledger inline. | Dr/Cr grid only. `Alt+C` inline master creation exists and is dispatch-tabled (`MasterCreateKind.cs`). |
| 6 | **Party sub-screens** | Supplier's/Buyer's Details → Dispatch Details → Order Details / List of Tracking Numbers (selecting an order **imports items, godown, qty, rate, amount**) **[inherited]** | **None of these exist.** No party-details screen, no dispatch screen, no order/tracking pull-in. |
| 7 | **Line entry** | Item Invoice: common `Purchase Ledger`/`Sales Ledger` (shown per F12 "Use common ledger account for item allocation"), then per item → **Item Allocations** sub-screen. Accounting Invoice: `Particulars` ledger + Amount. As Voucher: Dr/Cr lines, items still selectable. | Three grids per §5. The value ledger is always shown (no F12). "As Voucher with items" is **not** supported. |
| 8 | **Per-amount sub-screens** | Fire off the **amount**, not the ledger: Bill-wise → Bank Allocations → Cost Centre Allocation → GST/e-Way. | Fire off the **ledger**, and **only in As-Voucher mode** (`VoucherLineViewModel.cs:167-187`, `:275-299`, `:403-418`, `:461-495`). |
| 9 | **Narration** | Last field, always optional. Per-ledger narration if the type says so. | Optional. Per-ledger narration absent. |
| 10 | **Accept** | `Ctrl+A` from anywhere, or Enter through to `Accept? Yes/No`. | `Ctrl+A` (`MainWindow.axaml.cs:211`) → `Accept()`. **No `Accept? Yes/No` confirmation prompt.** |

### 1.3 Interrupts and branch points

| Point | TallyPrime | Ours |
|---|---|---|
| Any field | `Alt+C` create master · `F12` reconfigure mid-entry · `Ctrl+I` "More Details" | `Alt+C` ✅. `F12` opens **only** the voucher-numbering config (`MainWindowViewModel.cs:6403-6428`). `Ctrl+I` is taken by Item-Invoice mode. |
| Any time | `Ctrl+L` Optional (recorded, not posted) · `Ctrl+T` Post-Dated | Both present (`MainWindowViewModel.cs:4801-4811`); suppressed on provisional types. |
| Any time | `Esc` abandon · `Alt+X` cancel (keeps number, nulls entry) · `Alt+D` delete | **† Esc present; `Alt+D` ABSENT** (corrected 2026-08-15 — see below). `Alt+X` cancel-vs-delete distinction not surveyed here. |

> 🔴 **† CORRECTED 2026-08-15 — the row above previously read "Esc/Alt+D present". `Alt+D` IS NOT PRESENT, and
> never was.** `docs/invented-vs-cloned.md` **IV-4** identified this line as the false claim that let the gap
> survive review, and instructed that it be corrected; that had not been done until now. Verified at HEAD
> `c56e5c3`: **nothing in the UI deletes anything.** The only `Key.D` arm in the dispatcher is the bare-letter
> Day-Book quick jump (`MainWindow.axaml.cs:959`, and `CanQuickJump` at `:1096-1097` requires
> `e.KeyModifiers == KeyModifiers.None`, so **Alt+D is deliberately unclaimed** — `:1084-1087` reserves it for a
> later delete slice). The engine half exists and is unreachable: `LedgerService.cs:99` documents
> `/// <summary>Alt+D — remove entirely; may leave a gap in numbering.` with **no Desktop caller**; the only
> `.Delete(` calls in `src/Apex.Desktop` are `CompanyStorage.cs:105/142` and
> `MainWindowViewModel.cs:2488 DeleteSelectedSavedView`. Corroborated by `docs/full-clone-census.md` **T1-1** and
> **T1-2**. `plan.md:290` specifies the gesture correctly; only the engine half shipped.
| Payroll/Attendance | `Ctrl+F` **Autofill** replaces manual line entry (BOOK pp.371, 373 **[inherited]**) | Dedicated Attendance/Payroll screens exist; autofill parity not surveyed. |

---

## 2. THE CONDITION MATRIX

Organised **per condition**, not per voucher type — a condition is a trigger that changes the screen, and
the same trigger usually touches several voucher types.

Fidelity verdicts: **MATCH** · **PARTIAL** · **WRONG** (we do something a user would call incorrect) ·
**ABSENT** (the condition does not exist for us).

### 2.1 Company-capability conditions (F11, layer 1)

| # | Condition / trigger | TallyPrime effect on voucher entry | Touches | Ours | Verdict |
|---|---|---|---|---|---|
| C-01 | **Maintain Accounts = No** | Inventory-only company: F4/F5/F6/F7 and all ledger/amount fields unavailable | all accounting types | No such flag on `Company` | ABSENT |
| C-02 | **Maintain Inventory = No** | The whole inventory layer disappears: no item masters, no Item Invoice mode, Sales/Purchase forced to Accounting Invoice, no godown/batch/tracking sub-screens. **The single largest screen-shape switch in the product.** | Sales, Purchase, all stock types | No such flag on `Company` (`src/Apex.Ledger/Domain/Company.cs` — no `MaintainInventory`) | **ABSENT** |
| C-03 | **Integrate Accounts with Inventory = No** | Fields unchanged; *meaning* changes — closing stock is no longer computed from stock records and must be entered manually as a ledger figure (TPP **[inherited]**; WEB `stock-valuation-faq`) | valuation, not entry | No such flag | ABSENT |
| C-04 | **Enable Bill-wise entry** (F11) | Unlocks the *ledger* field "Maintain balances bill-by-bill". On its own changes no voucher screen. (SG p.91 step 1 **[verified-A1]**) | Payment, Receipt, Purchase, Sales, Cr/Dr Note, Journal | **No company-level flag.** Only `Ledger.MaintainBillByBill` (`Ledger.cs:36`) — layers 1 and 3 collapsed | PARTIAL |
| C-05 | **Enable Cost Centres** (F11) | Unlocks the cost masters and the ledger field "Cost centres are applicable" (SG p.99 step 1 **[verified-A1]**) | all accounting types | **No company-level flag.** Gate is `ledger applicable ∧ ≥1 cost centre exists` (`VoucherLineViewModel.cs:275-299`) — a defined-cost-centre count standing in for a feature switch | PARTIAL |
| C-06 | **Enable Batches** (+ *Maintain Expiry Date for Batches*) | Unlocks stock-item "Maintain in Batches" / "Track date of Manufacturing" / "Use Expiry dates"; adds the batch line to Item Allocations (BOOK pp.129–130 **[verified-A1]**) | Purchase, Sales, GRN, Delivery, Stock Journal, Physical | `Company.MaintainBatchwiseDetails` ✅ + `StockItem.MaintainInBatches` ✅ | MATCH (at this layer — see C-20) |
| C-07 | **Use separate Actual and Billed Quantity columns** | Quantity column splits into Actual / Billed. **Actual drives stock, Billed drives accounts.** Explicitly *"allowed only in Sales or Purchase vouchers only"* (BOOK pp.145–146, Note1/Note2 **[verified-A1]**) | Sales, Purchase | `Company.UseSeparateActualBilledQuantity` ✅; gated to Sales/Purchase item invoice (`VoucherEntryViewModel.cs:430-431`); stock on Actual (`:3424`), value+GST on Billed (`:2676-2688`, `:2727`) | **MATCH** |
| C-08 | **Use Discount column in invoices** (F11) | Inserts a **Disc%** (or Disc Amt, or both, per a "Discount format" sub-field) column inside the item line of Item-Invoice Sales **and Purchase**. Line Amount is net of it and **GST computes on the discounted value** (BOOK p.148, p.232 **[inherited]**; WEB Sales FAQ) | Sales, Purchase, Cr/Dr Note | **No such flag.** Disc% appears only when `EnableMultiplePriceLevels` is on **and** the voucher is a **Sales** item invoice (`VoucherEntryViewModel.cs:185-186`). Percentage only — no amount, no format choice. Purchase can never show it. | **WRONG** (right column, wrong trigger, wrong scope) |
| C-09 | **Enable multiple Price Levels** | Price Level / Price List masters; a party carries a level; the **Rate** auto-populates from the applicable price list, and the list's discount % can pre-fill Disc% | Sales (and Purchase in Tally) | `Company.EnableMultiplePriceLevels` ✅, header level picker, party default level, auto-fill respecting operator-dirty flags (`:2398-2470`) | MATCH for Sales; **conflated with C-08** |
| C-10 | **Enable Job Order Processing** | Job Work In/Out Order types + material transfer/consumption screens | Job Work, Material In/Out | `Company.EnableJobOrderProcessing` ✅ (`MainWindowViewModel.cs:3304`, `:3336`) | MATCH |
| C-11 | **Maintain Payroll / Enable Payroll Statutory** | Payroll (Ctrl+F4) and Attendance vouchers appear; statutory pay-head sub-screens | Payroll, Attendance | `Company.PayrollEnabled` / `PayrollStatutoryEnabled` ✅ | MATCH |
| C-12 | **Enable GST** | Company GST Details sub-screen; ledgers/items gain GST blocks; vouchers gain tax lines, **Alt+A Tax Analysis**, e-Way/e-Invoice sub-screens | Sales, Purchase, Cr/Dr Note, Journal, Receipt | `Company.GstEnabled` ✅, deep support | MATCH |
| C-13 | **Enable TDS / TCS** | Nature masters; expense/party ledger fields; a **TDS Details** sub-screen on the qualifying line | Purchase, Journal, Payment / Sales | `TdsEnabled`/`TcsEnabled` ✅; advisory panels, `ShowTdsPanel` `:819-858`, `ShowTcs` `:3066-3079` | MATCH (advisory shape differs — §4) |
| C-14 | **Enable multiple addresses** | The Party Details / Dispatch screen offers an **address picker** instead of one fixed address | Sales, Purchase, Delivery | ABSENT (no party-details screen at all) | ABSENT |
| C-15 | **"Show more/all features" hides but does not reset** | A Yes feature that is then hidden **stays Yes and keeps acting** | all | We have no hide/show layer, so no risk of the inverse bug | N/A |

### 2.2 Voucher-type-master conditions

| # | Condition / trigger | TallyPrime effect | Touches | Ours | Verdict |
|---|---|---|---|---|---|
| C-16 | **Activate this Voucher Type = No** | The type does not appear; `F10 > Show Inactive` is the only route, and activating is an explicit Yes | Memorandum, Reversing Journal, Rejection In/Out, orders, notes, Material In/Out | `IsActive` exists but **`OpenVoucher` falls back to an inactive type and opens it silently** (`MainWindowViewModel.cs:2756-2757`, and identically `:2793`, `:3311`, `:3340`) | **WRONG** |
| C-17 | **Two types on one base** (e.g. "Sales — Export" and "Sales — Domestic") | The F-key offers a **type list**; each carries its own numbering series, class and print config | every base type | **Voucher-type identity is discarded everywhere** — `FirstOrDefault(BaseType == …)`. The second Sales type is **unreachable**. | **WRONG** |
| C-18 | **Track additional costs of purchases** | Additional-cost entry area; cost ledgers apportion into landed stock rate | Purchase | `VoucherType.TrackAdditionalCosts` ✅ + apportionment engine + read-only Landed Rate/Value (`:476`, `:3305-3367`) | MATCH |
| C-19 | **Allow zero-valued transactions** | A ₹0 item line is accepted: stock moves, ₹0 to books and GST | Sales, Purchase | `VoucherType.AllowZeroValuedTransactions` ✅, rejected on any other base (`VoucherValidator.cs:62-66`) | MATCH |
| C-19b | **Use for POS invoicing / Manufacturing Journal / Stat Payment / RCM Payment / GST Stat Adjustment** | Type-flag behaviours | POS Sales, Stock Journal, Payment, Journal | All present as `VoucherType` flags with dedicated screens | MATCH |

### 2.3 Master-value conditions (layer 3) — the sub-screen triggers

| # | Condition / trigger | TallyPrime effect | Touches | Ours | Verdict |
|---|---|---|---|---|---|
| C-20 | **Stock item `Maintain in Batches` = Yes** | On selecting the item **in a Purchase (F9) or Sales (F8) invoice**, the **Stock Item Allocations** sub-screen opens with `Mfg Dt.` · `Batch/Lot No.` (existing, or **"New Number"**) · `Expiry Date` · `Quantity` · `Rate` · `Amount`. Inward types a new batch; outward selects from batches carrying a balance. (BOOK pp.130–132 **[verified-A1]** — the corpus walks exactly F9 then F8) | **Purchase, Sales**, GRN, Delivery, Stock Journal, Physical | Real batch sub-screen exists **only on the inventory screen** (`InventoryVoucherEntryViewModel.cs:205-208`, `BatchAllocationViewModel.cs`). On the **Purchase/Sales item invoice the batch is a bare free-text label** (`InventoryVoucherLineViewModel.cs:327-328`) — no picker, no Mfg/Expiry, no split, no balance check | **WRONG** |
| C-21 | **Ledger `Maintain balances bill-by-bill` = Yes** | The **Bill-wise Details** sub-screen fires after the amount (Payment/Receipt/Cr-Dr Note) or after the invoice total (Sales/Purchase invoice) — **in every mode**. SG places it explicitly at step 7 of Purchase Item Invoice (p.79), step 6 of Purchase Accounting Invoice (p.80), step 6 of Sales Item Invoice (p.81), step 5 of Sales Accounting Invoice (p.82) **[verified-A1]** | Payment, Receipt, Purchase, Sales, Cr/Dr Note, Journal | **As-Voucher mode ONLY.** The item-invoice and accounting-invoice Accept paths construct the party `EntryLine` with **no bill allocations at all** (`VoucherEntryViewModel.cs:3514-3518`; allocations are built only at `:2091-2097`, inside the plain-grid `PostAndSave`) | **WRONG — see §7 gap G-1** |
| C-22 | **Ledger `Cost centres are applicable`** | Auto-**Yes** for revenue ledgers (income & expense), **No** by default for non-revenue (assets & liabilities); settable manually (SG p.100 **[verified-A1]**). The **Cost Allocation** window opens **immediately after the Amount** | all accounting types; allocatable on a Delivery Note but **not** a Receipt Note (WEB Cost Centres FAQ **[inherited]**) | `Ledger.CostCentresApplicable` nullable, defaulting to P&L nature (`ClassificationRules.cs:57-58`) — **the defaulting rule matches the corpus exactly**. Fires on the ledger pick, seeding a row (`VoucherLineViewModel.cs:275-299`) | MATCH at this layer; **the reconciliation rule is WRONG — C-27** |
| C-23 | **Ledger is a bank** | **Bank Allocations**: `Transaction Type` (Cheque / e-Fund Transfer / Electronic Cheque), `Inst. No.`, `Inst. Date`, `Bank Date`, `Favouring Name`/`Received From` | Payment, Receipt, Contra | `IsBankLine` (`VoucherLineViewModel.cs:403-418`) — As-Voucher only | PARTIAL (mode-scoped; field set not compared here) |
| C-24 | **Ledger carries a non-base currency** | Forex Amount / Rate of Exchange on the amount. **Not an F11 flag in TallyPrime** — currency masters exist by default and the trigger is the ledger's "Currency of Ledger" | all accounting types | `IsForexLine` when `Ledger.CurrencyId` resolves; rate defaulted from the rate in force on the voucher date (`:461-495`, `:557-567`) — **the trigger model matches TallyPrime, not ERP 9** | MATCH (As-Voucher only) |
| C-25 | **Ledger `Default Credit Period` + `Check for credit days during voucher entry`** | The credit-days field is used to auto-derive the bill due date, and — when the check is on — **warns** when the party exceeds the credit period / has pending outstanding (SG p.91 steps 4–5 **[verified-A1]**) | Payment, Receipt, Sales, Purchase | `Ledger.DefaultCreditPeriodDays` exists (`Ledger.cs:42`) and derives a due date at posting when the field is blank. **The "check for credit days" warning does not exist** — no such property, no warning path | PARTIAL |
| C-26 | **Ledger `Type of Ledger` = Discount / Invoice Rounding** | Auto-computed value in invoice mode; revealed by a ledger-screen F12 | Sales, Purchase | Not surveyed; no `Ledger.TypeOfLedger` seen | UNVERIFIED-OURS → §8 |

### 2.4 Reconciliation conditions (what must add up)

| # | Condition | TallyPrime rule | Ours | Verdict |
|---|---|---|---|---|
| C-27 | **Cost allocation total** | **Cost categories are PARALLEL SETS, not a partition.** The corpus worked example is unambiguous: Travelling Expenses **₹5,000** allocated as *Branch→Kolkata **5000*** **and** *Department→Marketing **5000*** — i.e. the **full line amount once per category**, split across centres *within* a category (SG pp.101–102 steps 3–9 **[verified-A1]**; the "parallel sets" wording is BOOK p.98 **[inherited]**). Failure is a blocking error naming the ledger and the category: *"Cost Break-up Total does not match for ledger (under Category …)"* (WEB Cost Centres FAQ **[inherited]**) | **We sum ALL allocations across ALL categories and require the total == the line amount** (`VoucherValidator.cs:326-330`; UI mirror `VoucherLineViewModel.cs`). The corpus's own worked example — 5000 + 5000 on a 5000 line — **is rejected by our engine.** | **WRONG — see §7 gap G-2** |
| C-28 | **Bill allocation total** | Rows must sum to the party line amount; splitting across several bills is explicitly supported (SG p.92 **[verified-A1]**) | Exact-sum enforced, engine + UI (`VoucherValidator.cs:295-299`, `VoucherLineViewModel.cs:228-238`) | MATCH |
| C-29 | **Batch quantity total** | Σ allocated qty = the line's **Actual** quantity | `BatchAllocationViewModel.Apply()` requires Σ = line qty **[verified-A1 in code]** | MATCH (where the sub-screen exists — C-20) |
| C-30 | **Godown split within one line** | One line may split across godowns — *"buy 1000 tons … 300 in Warehouse A, 700 in Warehouse B using the same purchase transaction … identical to the way you would allocate Cost Centre details"* (WEB Inventory/Godowns **[inherited]**) | **Godown is a line-level scalar** (`InventoryVoucherLineViewModel` single `SelectedGodown`; `BatchAllocation` carries no godown). Splitting requires two lines. | **WRONG** |
| C-31 | **Stock Journal source == destination** | Balanced transfer, exempt for Manufacturing Journal / consuming Material In | Enforced **in the base unit** (`InventoryVoucherEntryViewModel.cs:461-469`, engine `:329-336`), with the documented exemptions | MATCH |
| C-32 | **Item-invoice accounts↔stock pairing** | Implicit in Tally | Σ item value == the stock-leg accounting amount, enforced (`VoucherValidator.cs:244-263`) | MATCH (stricter than Tally, correctly) |

### 2.5 Mode conditions

| # | Condition | TallyPrime | Ours | Verdict |
|---|---|---|---|---|
| C-33 | **Ctrl+H on Payment / Receipt / Contra** | Single ⟷ Double Entry. Single shows `Account` + `Particulars` and **no Dr/Cr labels**; polarity inverts by type (Receipt/Contra: Account = Dr; Payment: Account = Cr) (BOOK pp.29, 32 **[verified-A1]**; SG p.76 **[verified-A1]**) | **Single Entry does not exist** anywhere in `src/` | **ABSENT** |
| C-34 | **Ctrl+H on Purchase / Sales** | Item Invoice / Accounting Invoice / As Voucher (BOOK p.33 **[verified-A1]**; SG pp.78–82 **[verified-A1]**) | Item ✅ (Ctrl+I) · Accounting **Sales only** (Ctrl+H) · As Voucher ✅. **Purchase Accounting Invoice is dormant dead code** behind `CanBeAccountingInvoice` (`VoucherEntryViewModel.cs:80`, rationale `:70-79`) because TDS/RCM read `Lines`, which is empty in accounting mode | **PARTIAL** |
| C-35 | **Ctrl+H on Credit Note / Debit Note** | All three modes, same list (BOOK pp.54–55 **[verified-A1]** — the Credit Note section repeats the Item / Accounting / As Voucher definitions verbatim) | **Neither invoice mode** — `CanBeItemInvoice` admits only Purchase/Sales (`:67-68`) | **WRONG** |
| C-36 | **"As Voucher" still allows item selection** | BOOK p.33 definition #3 and pp.36–37, 42 **[verified-A1 for p.33]** | Our As-Voucher grid is pure Dr/Cr; no item picker | PARTIAL |
| C-37 | **Accounting Invoice mode and e-Way Bill** | e-Way Bill details **cannot** be entered in Accounting Invoice mode — they require stock items (GSTN **[inherited]**) | Accounting-invoice ledger picker excludes any ledger declaring a **Goods** supply (Rule 46(f) guard, `:2574-2582`) — a different but compatible restriction | MATCH-ish |

### 2.6 Voucher-screen F12 conditions (layer 4)

TallyPrime **abolished the single global "F12 > Voucher Entry" page** that Tally.ERP 9 had; F12 now
configures **the screen you are standing on**, so the same label exists separately per voucher screen.

| # | F12 option (per voucher screen) | Effect | Ours |
|---|---|---|---|
| C-38 | Skip Date field | Cursor skips Date (default Yes) | ABSENT |
| C-39 | Use Cr/Dr instead of To/By | Label swap on the plain grid | ABSENT (we always show Dr/Cr) |
| C-40 | Use common ledger account for item allocation | Shows/hides the single Purchase/Sales ledger field | ABSENT (always shown) |
| C-41 | **Use default Bill-wise details for Bill Allocation** | **Yes ⇒ auto-allocate and the Bill-wise screen does NOT appear** (WEB **[inherited]**) — the single most common "bill-wise not showing" support case | ABSENT |
| C-42 | Provides Cash/Trade Discount | Whole-invoice discount ledger | ABSENT |
| C-43 | Allow separate Buyer & Consignee name | Adds the Consignee block | ABSENT |
| C-44 | **Warn on negative Stock Balance** | Yes ⇒ **warns, does not block** (WEB **[inherited]**, see §8 U-1) | ABSENT — we **hard-block, unconditionally** |
| C-45 | Select Cost Centre/Class | Shows the Cost Centre Class field; picking a class **allocates in the background and the Cost Allocation screen never appears** | ABSENT (no cost-centre classes) |
| C-46 | Skip Supplier's / Buyer's / Party / Dispatch details screen | Suppresses those sub-screens | N/A — those screens do not exist |
| C-47 | Allow cash accounts in journal vouchers | Off by default ⇒ cash/bank **not selectable** in a Journal | **ABSENT.** Our ledger picker is unfiltered for every type: `Ledgers = company.Ledgers` (`VoucherEntryViewModel.cs:630`). A Contra can post to Sales; a Journal can post to Cash. |
| C-48 | Modify Tax Rate details of GST · Warn when Voucher No. exceeds 16 characters | GST-conditional F12 rows | ABSENT |

**Our whole F12 surface on a voucher screen is the voucher-numbering config** (`MainWindowViewModel.cs:6403-6428`).

---

## 3. Per-voucher field-by-field walkthroughs

Convention: **[skip]** cursor does not stop · **[auto]** system-filled · **[sub]** opens a sub-screen ·
**[cond]** conditional (§2) · **⛔** we do not have it · **⚠️** we have it differently.

### 3.1 Contra — F4
Internal movement of funds only (cash↔bank, bank↔bank); no business effect (BOOK p.25 **[verified-A1 TOC]**).

*Single Entry:* Voucher No. [auto/skip] → Date [skip, F2] → **`Account`** = ledger **credited** → **`Particulars`**
= ledger **debited** → `Amount` → [sub] Bank Allocations [cond C-23] → `Narration` → Ctrl+A.
Polarity note, corpus-verbatim in substance: *"In Single Entry Mode Dr means Account & Cr means Particulars"*
(BOOK p.27/p.29 **[verified-A1]**).
*Double Entry:* Dr/Cr lines with visible labels.
**Ours:** ⛔ no Single Entry. Dr/Cr grid only. ⚠️ ledger picker unfiltered (C-47) — nothing stops a Contra
touching a P&L ledger.

### 3.2 Receipt — F6
Voucher No. → Date [F2] → `Account` = ledger **debited** (cash/bank receiving) → `Particulars` = credited →
`Amount` → [sub] **Bill-wise Details** [cond C-21] → [sub] Bank Allocations [cond] → `Narration` → Ctrl+A.
Multi-line: *"multiple accounts are credited and one account is debited"* (SG p.77 **[inherited]**).
**Ours:** Dr/Cr grid; bill-wise + bank fire correctly here (this is the one family where our sub-screen model
is faithful). Advance-receipt GST panel `CanBeAdvanceReceipt` (`:1610`) is an addition beyond the corpus walkthrough.

### 3.3 Payment — F5
**Polarity inverts** and the corpus flags it: *"In Single Entry Mode **Cr means Account & Dr means Particulars**"*
(BOOK p.32 **[verified-A1]**).
Voucher No. → Date [F2] → `Account` = credited (cash/bank paying) → `Particulars` = debited → `Amount` →
[sub] Bill-wise (typically **Agst Ref**) → [sub] Bank Allocations → [sub] Cost Centre Allocation [cond C-22]
→ `Narration` → Ctrl+A. `Alt+S` (Set Status: On Hold / Processed / Reconciled) post-save **[inherited]**.
**Ours:** Dr/Cr grid; all three sub-screens present. TDS advisory panel additionally gated (`:819-858`).
⛔ `Alt+S`.

### 3.4 Purchase — F9

**Item Invoice** (SG p.79 **[verified-A1]**, BOOK p.34 **[verified-A1]**):
Voucher No. [auto/skip] → Date [F2] → **`Supplier Invoice No.`** + its Date → `Party's A/c Name` (or Cash) →
[sub] Supplier's Details [cond] ⛔ → [sub] Order Details / Tracking Numbers [cond] ⛔ → `Purchase Ledger`
[cond C-40] → per item: `Name of Item` → **[sub] Item Allocations** (`Location`/Godown, **Batch** [cond C-20]
⚠️, `Quantity` (+Billed [cond C-07] ✅), `Rate`, `Amount` [auto]) → additional ledgers (GST [auto], freight)
→ **[sub] Bill-wise Details — `New Ref` + the supplier invoice no.** ⛔ → `Narration` → Ctrl+A.
`Alt+A` Tax Analysis **[inherited]**.

**Accounting Invoice** (SG p.80 **[verified-A1]**): Date → `Supplier Invoice No.` → `Party's A/c Name` →
`Particulars` (purchase/service/**fixed-asset** ledger — no item field) → `Amount` → [sub] Bill-wise →
`Narration` → Ctrl+A. **Ours: ⛔ the whole mode** (dormant, `:80`).

**As Voucher** (BOOK pp.36–37 **[inherited]**): Date → `Supplier Invoice No.` → `Cr` supplier → `Dr` Purchase
ledger → `Name of Item` → `Location` → `Quantity` → `Rate` → Narration → Ctrl+A.
**Ours:** ⚠️ no item selection in As-Voucher (C-36).

### 3.5 Sales — F8
Same shape, with two naming differences that must not be conflated:
**Sales uses `Reference No.`; Purchase uses `Supplier Invoice No.`** (BOOK p.39 vs p.34 **[verified-A1]**).
We get this right: `ShowReferenceCapture` swaps the caption per base type (`VoucherEntryViewModel.cs:546-552`).

Item Invoice: Ref No. → `Party's A/c Name` → [sub] Dispatch Details ⛔ → [sub] Party/Buyer's Details
(+`Consignee` only under F12 C-43) ⛔ → [sub] Order/Tracking ⛔ → `Sales Ledger` → items (+Disc% [cond C-08]
⚠️) → GST ledgers [auto] ✅ → [sub] Bill-wise ⛔ → `Provide GST/e-Way Bill details: Yes/No` → Narration → Ctrl+A.
Accounting Invoice: services and **sale of fixed assets** (SG p.82 **[verified-A1]**). ✅ present (Sales only).
e-Way Bill details **cannot** be entered in Accounting Invoice mode **[inherited]**.

### 3.6 Journal — F7
Voucher No. → Date [F2] → `Dr` line (labels `By`/`To` unless F12 C-39) → `Cr` line, **amount [auto] to the
balancing figure**, editable → further lines with running Dr/Cr and a `Diff` → [sub] Bill-wise / Cost Centre
[cond] → Narration → Ctrl+A.
**No party field, no inventory, no invoice modes.** Cash/bank ledgers **not selectable by default** (F12 C-47).
**Ours:** ✅ grid and sub-screens. ⛔ auto-balancing-figure fill. ⛔ the cash-account restriction (C-47).

### 3.7 Credit Note — Alt+F6 / Debit Note — Alt+F5
TallyPrime: **all three modes** (BOOK pp.54–55, 60 **[verified-A1 for 54–55]**). Also used for interest paid
to a party, expenses paid by parties on our behalf, price differences and post-invoice discounts (BOOK p.54).
`Ctrl+I` "More Details" captures the Original Invoice No. & Date **[inherited]**.
**Ours:** ⛔ **no invoice modes at all** (C-35). We do have a richer §34 GST layer than the corpus describes —
`CanBeSection34Note` (`:1325-1326`), original-invoice reference, reason, the §34(2) 30-Nov cut-off with an
explicit override, consolidated fields — but it lives on the plain grid only.

### 3.8 Memorandum (inactive by default)
Suspense entries; **does not affect books**. Route `F10 > Show Inactive > Memorandum`. Fields identical to
Journal. **Conversion:** open it from `Exception Reports > Memorandum Register`, press the target voucher's
F-key **from the alteration screen** — it converts, keeping the entry — then Ctrl+A (BOOK pp.45–47 **[inherited]**).
**Ours:** `IsProvisionalType` badge, Optional/Post-Dated suppressed, `optional:false` forced (`:532-533`, `:2151`).
⛔ the conversion gesture; ⛔ the Show-Inactive gate.

### 3.9 Reversing Journal (inactive by default)
Auto-reverses; **does not directly affect ledgers or reports** — visible only through a **Scenario**.
Unique field: **`Applicable Up to`** (BOOK p.48 **[inherited]**).
**Ours:** ✅ `IsReversing` → the field, validated to parse and to be ≥ the voucher date (`:525`, `:2122-2137`).
Scenario masters exist (`ScenarioMasterViewModel.cs`).

### 3.10 Stock / order vouchers
Purchase Order (Ctrl+F9) · Sales Order (Ctrl+F8) · Receipt Note (Alt+F9) · Delivery Note (Alt+F8) ·
Rejection In (Ctrl+F6) · Rejection Out (Ctrl+F5) · Stock Journal (Alt+F7) · Physical Stock (Ctrl+F7).
Rejection In/Out fields per BOOK pp.51–53 **[verified-A1]**: `Ledger Account` (the party) → `Name of Item` →
`Quantity` → `Rate` → `Narration` → Ctrl+A.
**Ours:** a separate `InventoryVoucher` aggregate with no Dr/Cr balancing (`InventoryVoucherEntryViewModel.cs`),
per-type line shapes (`:311-317`), real batch allocation (`:205-208`). The **big miss is the link back**:
selecting an order in a later invoice to **import items, godown, qty, rate and amount** does not exist.

---

## 4. Sub-screens and their reconciliation rules

### 4.1 Bill-wise Details

**When it opens (TallyPrime).** After the **amount** on a Payment/Receipt/Cr-Dr Note party line; after the
**invoice total** on a Sales/Purchase invoice (SG p.91 **[verified-A1]**). Suppressed entirely when voucher
F12 "Use default Bill-wise details for Bill Allocation" = Yes (C-41).

**The four Types of Ref** (SG p.90 **[verified-A1]**, field behaviour SG pp.91–94 **[verified-A1]**):

| Type | Name field | Due Date / Credit Days | Amount | Used on |
|---|---|---|---|---|
| **New Ref** | **auto-filled from the Supplier Invoice No. / voucher no.**, editable | **auto-filled from the ledger's Default Credit Period** | auto = invoice total | Sales, Purchase |
| **Agst Ref** | **selected from the List of Pending Bills** | inherited from that bill | auto = amount entered; splittable across bills | Payment, Receipt, Purchase/Sales Return |
| **Advance** | a user-chosen tracking string | *"No need to enter any details"* | auto | Payment, Receipt |
| **On Account** | **field is SKIPPED** | **field is SKIPPED** | auto | when the bill is unknown |

> **Advance is not applicable to Sales.** WEB (`Manage Outstanding Receivables`) restricts it to
> Payment/Receipt; the corpus BOOK p.110 lists all four generically inside a sales walkthrough. **Decision
> taken (corpus + web reconciled): gate `Advance` off invoice-type vouchers.** **[inherited]**

**Ours** (`BillAllocationRowViewModel.cs`, `MainWindow.axaml:2182-2241`):

| Behaviour | TallyPrime | Ours |
|---|---|---|
| Fires in item/accounting invoice mode | Yes | **No** (C-21) |
| Agst Ref name | picked from pending bills | **free TextBox** — a typo silently creates an orphan reference |
| New Ref name auto-fill | from Supplier Invoice No. | seeded from the line, not from the captured reference no. |
| Due date auto-fill from credit period | shown in the field | derived at posting when blank; **not shown** |
| Advance / On Account field suppression | skipped | **all four columns always editable**; only `NameRequired` differs (`:45`) |
| Split must equal the line | Yes | Yes ✅ (`VoucherValidator.cs:295-299`) — and **mandatory**: zero complete rows fails `BillSplitOk` |
| Non-reconciling residue | *(secondary sources only — the screen re-prompts until it foots; **UNVERIFIED**, §8 U-2)* | hard block, no "park it On Account" offer |

A **Ctrl+B Settle-Bill** path from Outstandings *does* validate references against genuinely open bills and
caps each knock at the bill's pending amount (`BillSettlementService.cs:29-53`) — the in-voucher panel is the
weak one, not the engine.

**Verified side-effect to preserve:** switching bill-wise on for a ledger with history makes **all earlier
transactions reflect as On Account bills**; the ledger balance stays correct, only bill-level tracking is
absent **[inherited]**.

### 4.2 Cost Allocation

**Opens** immediately after the Amount on a cost-applicable ledger line (SG p.101 **[verified-A1]**).
Cost centres are usable **only in accounting vouchers**; allocatable on a Delivery Note but **not** a
Receipt Note **[inherited]**.

**The parallel-set rule (C-27) is the single most important line in this document.** Categories are
independent allocation *axes*, not a partition. Cost **centres** are hierarchical (sub-centres allowed);
cost **categories are flat** — *"it is not possible to create a Sub Cost Category under a Cost Category"*
**[inherited]**. Cost Category masters require **at least one** of Allocate Revenue Items / Allocate
Non-Revenue Items to be Yes (SG p.99 **[verified-A1]**).

**Cost Centre Classes** (WEB; absent from the corpus **[inherited]**): created at `F11 > Ctrl+I (More
Details) > Cost Centre Class`, each naming categories + centres + a **percentage** per centre. Surfaced by
voucher F12 "Select Cost Centre/Class"; selecting a class **allocates in the background and the Cost
Allocation screen is never displayed**; "Not Applicable" falls back to manual. Distinct from Voucher Class.
**Ours: ABSENT.**

Whether the *manual* allocation grid also accepts a percentage that back-computes the amount is
**UNVERIFIED** (§8 U-3). Percentages are definitively used in classes.

### 4.3 Stock Item Allocations (godown × batch)

TallyPrime resolves one item line into a set of **(godown, batch)** rows: `Mfg Dt.` · `Batch/Lot No.` ·
`Expiry Date` · `Quantity` · `Rate per` · `Amount`, plus Godown when multiple godowns are on
(BOOK pp.130–132 **[verified-A1]**; TB2 pp.79–80 **[inherited]**). Batch balances are therefore **per
godown**, not global — though **no source states the combined grid explicitly (UNVERIFIED, §8 U-4)**; it is
the only model consistent with per-godown batch reporting.

**Ours:** two different implementations for the same concept.
* Inventory screen (GRN/Delivery/Stock Journal/Physical): a real sub-screen with batch picker + "New Number",
  Mfg/Expiry, multi-row split, Σ-qty check, and a **non-blocking** expiry warning
  (`BatchAllocationViewModel.cs`). Godown is fixed at the line.
* Purchase/Sales item invoice: **a free-text batch label** (`InventoryVoucherLineViewModel.cs:327-328`).
* No godown split on either (C-30).

### 4.4 Bank Allocations
Fires on a bank ledger; carries no amount of its own, so there is **no split-sum check** — correct on both
sides (`VoucherValidator.cs:338-343`). Field-set parity not compared here (§8 U-5).

### 4.5 Party Details / Dispatch Details / Order & Tracking pull-in
**Entirely absent on our side.** In TallyPrime these are where address/GSTIN/place-of-supply/consignee are
captured, and where selecting an open order or pending note **auto-imports the item lines**. The absence is
both a data gap (e-invoice/e-way payloads have no dispatch block to draw on) and a workflow gap (orders
cannot be converted; every invoice is re-keyed).

---

## 5. Entry modes — which types support which

| Voucher type | TallyPrime modes | Ours | Gap |
|---|---|---|---|
| Contra F4 | Single / Double | As Voucher only | Single Entry |
| Receipt F6 | Single / Double | As Voucher only | Single Entry |
| Payment F5 | Single / Double | As Voucher only | Single Entry |
| Purchase F9 | **Item · Accounting · As Voucher** | Item · As Voucher | **Accounting (dormant)** |
| Sales F8 | **Item · Accounting · As Voucher** | Item · Accounting · As Voucher | As-Voucher item selection |
| Credit Note Alt+F6 | **Item · Accounting · As Voucher** | As Voucher only | **both invoice modes** |
| Debit Note Alt+F5 | **Item · Accounting · As Voucher** | As Voucher only | **both invoice modes** |
| Journal F7 | none | none | — |
| Memorandum / Reversing Journal | none | none | — |
| Orders / notes / Stock Journal / Physical | none documented | none | — |

Our mode gates are a clean total partition — `IsAsVoucherMode` is defined as the *complement* of the two
invoice modes (`VoucherEntryViewModel.cs:118`), so forcing `Mode` directly cannot produce a screen with no
grid. That is good defensive design and should survive any rework.

**Key binding:** TallyPrime = **one** `Ctrl+H` picker. Ours = `Ctrl+I` item-invoice toggle
(`MainWindow.axaml.cs:481`) + `Ctrl+H` accounting-invoice toggle (`:493`), cycling
As Voucher → Item → Accounting → As Voucher (`VoucherEntryViewModel.cs:2492-2501`), degrading to a two-way
flip on Purchase.

---

## 6. Validation — what blocks versus what warns

### 6.1 TallyPrime

| Blocks | Warns (accepts anyway) | Silent |
|---|---|---|
| Unbalanced voucher | **Negative stock** — F12 "Warn on negative Stock Balance"; *"the invoice or delivery note will still be accepted … an alerting feature, not a hard block"* (WEB **[inherited]**, §8 U-1) | Prefilled date |
| **Cost break-up ≠ ledger amount, per category** — *"Cost Break-up Total does not match for ledger (under Category …)"* **[inherited]** | Credit-days exceeded, when "Check for credit days during voucher entry" is on (SG p.91 **[verified-A1]**) | Auto-computed GST |
| Bill allocation ≠ line amount *(**UNVERIFIED** — §8 U-2)* | Voucher No. > 16 characters (GST F12) | |
| Unknown master reference | Expired batch selection *(**UNVERIFIED** — §8 U-6: help documents earliest-expiry *guidance*, never a block or a warning)* | |

### 6.2 Ours

**Plain grid — live gate `CanAccept`** (`VoucherEntryViewModel.cs:794-798`): balanced ∧ Σ Dr > 0 ∧ ≥2 complete
lines ∧ no half-filled row ∧ every bill split OK ∧ every cost split OK.

**`Accept()` hard refusals, in order** (`:1880-1987`): half-filled row · unreadable instrument date ·
unreadable bill due date · bill split ≠ line · cost split ≠ line · half-filled forex pair · §34 essentials
(original-invoice reference, reason, the §34(2) 30-Nov cut-off unless overridden, `:1521-1559`).
Then inside `PostAndSave` (`:2023`): advance-engine refusals · TDS compute failure · RCM compute failure ·
fewer than two lines after derived legs · Reversing "Applicable Upto" unparseable or earlier than the
voucher date · unparseable reference date · engine exceptions relayed · store save failure.
**Whole-window rollback:** every company mutation pushes a compensating undo and any non-success exit
unwinds them (`:1965-1986`).

**Item invoice** (`:3246-3253`, `:3377`): party · value ledger · ≥1 complete line · no half-filled row ·
rate > 0 unless `AllowZeroValuedTransactions` · total > 0 · complete additional-cost rows · a resolvable GST
rate on every taxable item. Then `LedgerService.Post` runs `VoucherValidator.EnsureValid` **and** the
no-negative-stock guard atomically, rolling the whole voucher back on a stock violation.

**Accounting invoice** (`:2863-2868`, `:2915-2974`): Sales-only · party · ≥1 complete line · no half-filled
row · no unresolved taxable ledger (SAC/rate) · total > 0.

**Inventory screen** (`InventoryVoucherEntryViewModel.cs:461-469`, `:506-553`): ≥1 complete line · no
half-filled row; Stock Journal additionally needs a destination side and source == destination **in the base
unit**. Engine `InventoryPostingService.Post` then enforces content-matches-type, referential integrity
(including unit-reduces-to-base), the balance rule, `PreventDuplicate`, and the negative-stock guard with
rollback.

**Our only non-blocking warning in the whole entry surface** is the batch expiry flag
(`BatchAllocationViewModel`, expired = red, near-expiry = amber, "issuing anyway is allowed"). Everything
else is a hard block or nothing.

**Negative stock is an UNCONDITIONAL hard block on every outward path**
(`InventoryPostingService.cs:348-405`). There is no company flag, no item flag, no voucher-type flag that
overrides it — I checked `VoucherValidator.cs` and `Company.cs` and no such flag is read anywhere. The guard
also samples **pre-count** on a Physical-Stock date (`:391-394`) so an intra-day over-draw cannot hide behind
a same-day count.

**One UX inconsistency, not a data defect:** `Ctrl+A` bypasses `CanAccept` —
`MainWindowViewModel.cs:5585/5588` call `Accept()` unconditionally, and `CanAccept` only greys the buttons.
`Accept()` re-validates, so nothing unsafe posts; the user just gets a different failure surface depending
on how they saved.

---

## 7. Ranked gaps, worst first

Ranked by **how badly a real user is blocked**, not by implementation cost.

### G-1 — Bill-wise Details never fires in invoice mode ⇒ receivables silently empty · **CRITICAL**
The normal way to record a sale is an Item Invoice. Our item-invoice and accounting-invoice Accept paths
build the party `EntryLine` with **no bill allocations** (`VoucherEntryViewModel.cs:3514-3518`; allocations
exist only at `:2091-2097`). `Outstandings.cs:137` only counts lines that *have* allocations. **Therefore a
company that invoices normally has an empty Receivables report, empty ageing, no overdue tracking, and
nothing for Ctrl+B to settle — with no error and no warning anywhere.** The corpus is explicit that the
sub-screen belongs there: SG p.79 step 7, p.80 step 6, p.81 step 6, p.82 step 5 **[verified-A1]**.
*Blast radius:* Outstandings, ageing, Bills Receivable/Payable, credit control, the whole AR/AP story.

### G-2 — Cost allocation enforces a partition; TallyPrime uses parallel sets · **CRITICAL**
`VoucherValidator.cs:326-330` sums **every** allocation across **all** categories and demands the total equal
the line. The corpus's own worked example — ₹5,000 travelling expense allocated ₹5,000 to Branch→Kolkata
**and** ₹5,000 to Department→Marketing (SG pp.101–102 **[verified-A1]**) — **is rejected by our engine.**
Multi-category cost accounting is therefore impossible: a user can allocate along exactly one axis, or split
one amount misleadingly across axes. Either way the Category Summary is wrong.
*Blast radius:* every cost report; and the fix is a **breaking change to the persisted validation contract**,
so it needs a migration story (see D-1).

### G-3 — Credit Note and Debit Note have no invoice modes · **HIGH**
`CanBeItemInvoice` admits only Purchase/Sales (`:67-68`). A sales return of stock cannot be entered as an
item invoice, so it cannot carry item lines, cannot move stock through the accounting voucher, and cannot
print as an item-wise credit note. BOOK pp.54–55 **[verified-A1]** gives Credit Note all three modes.

### G-4 — Voucher-type identity is discarded; inactive types open anyway · **HIGH**
`FirstOrDefault(t => t.BaseType == baseType && t.IsActive) ?? FirstOrDefault(t => t.BaseType == baseType)`
(`MainWindowViewModel.cs:2756-2757`, `:2793`, `:3311`, `:3340`). Two consequences: a company with a second
Sales type (export series, branch series, a different numbering series) **cannot reach it**; and the
`IsActive` flag is decorative — the fallback opens an inactive type silently, so the F10 > Show Inactive >
Activate gesture the corpus documents for ten voucher types has no meaning here.

### G-5 — Batch allocation is a free-text label on the invoice screens · **HIGH**
BOOK pp.130–132 **[verified-A1]** walks batch entry through **F9 then F8** with Mfg Dt./Expiry/New Number.
We give those screens `InventoryVoucherLineViewModel.cs:327-328` — a string. So on the only two vouchers
where a pharma/FMCG user actually buys and sells batched goods, there is no batch picker, no expiry capture,
no split, no balance check, and no expiry warning. The good sub-screen exists — it is wired to the wrong
screens.

### G-6 — No Single Entry mode · **HIGH**
Every corpus walkthrough of Payment/Receipt/Contra teaches Single Entry first (BOOK pp.29, 32; SG p.76
**[verified-A1]**). It is the mode most users spend most of their day in, and the polarity inversion between
Receipt/Contra and Payment is a documented teaching point. We have no `Account`/`Particulars` layout at all.

### G-7 — Purchase Accounting Invoice mode is dormant · **HIGH**
Deliberate and well-documented (`:70-79`) — shipping it silently dropped the §194J TDS carve-out. But the
consequence stands: **service purchases and fixed-asset purchases have no correct entry mode** (SG p.80
**[verified-A1]** names exactly those two use cases). The fix is bounded and known: wire TDS/RCM detection to
the Particulars lines, then flip `CanBeAccountingInvoice`.

### G-8 — Negative stock hard-blocks where TallyPrime warns · **HIGH (but see U-1)**
Unconditional block (`InventoryPostingService.cs:348-405`). WEB says TallyPrime warns and accepts. **The
corpus is silent — I grepped all ten PDFs for "negative stock" / "allow negative" / "negative balance" and
got zero hits [verified-A1].** This is the long-running open problem recorded in project memory; it is
listed here as a gap because the reference product's behaviour is a warning, not because the block is
wrong per se. See D-3.

### G-9 — Discount column has the wrong trigger and the wrong scope · **MEDIUM**
Gated on `EnableMultiplePriceLevels ∧ Sales ∧ item invoice` (`:185-186`). TallyPrime gates it on its own F11
flag, offers it on Purchase too, and accepts **an amount as well as a percentage** with a "Discount format"
sub-field. A purchase with a trade discount cannot be entered as billed.

### G-10 — No godown split within a line · **MEDIUM**
Godown is a line scalar. TallyPrime splits one line across godowns exactly as it splits cost centres
**[inherited]**. Workaround (two lines) distorts the printed invoice.

### G-11 — No order / tracking-number pull-in · **MEDIUM**
Selecting an open PO/SO/note to auto-import items, godown, qty, rate and amount is absent, so order
processing terminates at the order: every invoice is re-keyed, and order-fulfilment tracking is manual.

### G-12 — Ledger pickers are unfiltered per voucher type · **MEDIUM**
`Ledgers = company.Ledgers` (`:630`). A Contra can post to a Sales ledger; a Journal can post to Cash with
no F12 gate (C-47). Nothing is *unbalanced*, but the books can be silently mis-shaped.

### G-13 — No party-details / dispatch sub-screens · **MEDIUM**
No place to capture consignee, dispatch-through, LR/RR no., motor vehicle no., or a per-voucher address
override — which also starves the e-way-bill and e-invoice payloads.

### G-14 — Voucher-screen F12 layer does not exist · **MEDIUM (structural)**
Fourteen of the F12 conditions in §2.6 are absent; F12 opens the numbering config only. Every condition we
*do* implement is hard-coded rather than configurable, which is why so many of the gaps above are "the right
behaviour behind the wrong trigger".

### G-15 — Bill-wise ref-type field behaviour is uniform · **MEDIUM**
Agst Ref should pick from pending bills; Advance should skip Due Date; On Account should skip Name **and**
Due Date; New Ref should auto-fill Name from the captured invoice no. and Due Date from the credit period.
We show four always-editable free-text columns (`MainWindow.axaml:2214-2225`). The free-typed Agst Ref name
is the dangerous one — a typo creates an unmatched settlement that no validator catches.

### G-16 — No cost-centre classes, no general voucher classes · **LOW-MEDIUM**
Only a POS voucher class exists (`PosConfig`). Class-based auto-allocation (which *suppresses* the Cost
Allocation screen) is absent.

### G-17 — Missing small gestures · **LOW**
`Accept? Yes/No` confirmation · Journal balancing-figure auto-fill · "Automatic (Manual Override)" numbering
· Memorandum → real-voucher conversion by F-key · `Alt+S` payment status · per-ledger narration · Alt+A Tax
Analysis parity · credit-days warning (C-25).

---

## 8. UNCERTAINTIES

Nothing here should be built as if it were settled. Each entry says what would resolve it.

**U-1 — Negative stock: does TallyPrime warn or block?**
Corpus: **silent**. I grepped all ten PDFs for "negative stock", "allow negative", "negative balance" —
**zero hits [verified-A1]**. The warn-not-block claim rests on WEB/secondary sources only **[inherited]**.
*Resolve by:* observing real TallyPrime with F12 "Warn on negative Stock Balance" both ways. This is the
crux of the project's long-standing negative-stock problem and should not be re-attempted on a web claim.

**U-2 — What TallyPrime does when a bill-wise split does not reconcile.**
No official page states the enforcement rule. The commonly-reported behaviour (secondary only) is that the
screen keeps presenting another `Type of Ref` row until the allocation foots, so the residue must be parked
as a further New Ref or as **On Account**. *Resolve by:* observation. Until then our hard block is a design
decision, not a sourced rule — and it should be labelled as such in code.

**U-3 — Does the manual Cost Allocation grid accept a percentage that back-computes the amount?**
Not stated in corpus or help. Percentages are definitively used in Cost Centre **Classes**. *Resolve by:*
observation.

**U-4 — The combined (godown × batch) allocation grid.**
Corpus shows batch columns and the godown column separately; help treats godown splitting and batch
splitting as independent layers. **No source states the combined grid.** The (godown, batch) row model is the
only one consistent with per-godown batch reporting, but it is inferred. *Resolve by:* observation.

**U-5 — Bank Allocation field set and Transaction Type list.**
Our field set was not compared against TallyPrime's in this pass (Bank Date, Favouring Name / Received From,
the full Transaction Type enumeration). *Resolve by:* a targeted field-level comparison.

**U-6 — Selling an expired batch: block, warn, or silent?**
Help documents FIFO/earliest-expiry *selection guidance* only; it states no block and no warning. Our
non-blocking amber/red warning is a reasonable design choice but is **not** sourced. *Resolve by:* observation.

**U-7 — `Ledger.Type of Ledger` (Discount / Invoice Rounding) on our side.**
TallyPrime has it, revealed by a ledger-screen F12; I did not find an equivalent on `Ledger` but did not
exhaustively survey the ledger master. *Resolve by:* a 10-minute read of `LedgerMasterViewModel.cs`.

**U-8 — Does typing into `Amount` back-compute `Rate`?**
Practitioners rely on it; **no corpus or help statement exists**. Not asserted. *Resolve by:* observation.

**U-9 — Alternate / compound units at voucher entry.**
Compound units are corpus-verified. **Alternate Units** (item-screen F12, per-voucher conversion-factor
override, "rate must be per the unit used") come from WEB pages, some of them Tally.ERP 9-era. *Resolve by:*
confirming the exact TallyPrime field labels before building.

**U-10 — `Enable Job Costing` default.**
Listed as an F11 option via the ERP9→Prime mapping table; its default is **UNVERIFIED**.

**U-11 — `Integrate Accounts with Inventory` default.**
Sources conflict. Treated as Yes above; not safe to build a migration on.

**U-12 — Corpus defect worth recording.** BOOK p.129 **[verified-A1]**, in the section titled "Activation of
Batch-wise details", instructs the reader to activate **"Enable Goods & Services Tax (GST)"**. That is
plainly a copy-paste error in the source book; every other reference (including BOOK p.130's own F12 note)
says Enable Batches. Do not let this propagate into a requirement.

---

## Appendix — condition IDs by our-side anchor

| Anchor | Conditions |
|---|---|
| `VoucherEntryViewModel.cs:67-68,80,90,118,122` | C-33…C-37 |
| `VoucherEntryViewModel.cs:185-186` | C-08, C-09 |
| `VoucherEntryViewModel.cs:430-431,3424,2676-2688` | C-07 |
| `VoucherEntryViewModel.cs:630` | C-47 |
| `VoucherEntryViewModel.cs:3514-3518` vs `:2091-2097` | **C-21 / G-1** |
| `VoucherValidator.cs:307-330` | **C-27 / G-2** |
| `VoucherValidator.cs:289-299` | C-28 |
| `VoucherLineViewModel.cs:167-187,275-299,403-418,461-495` | C-21…C-24 |
| `ClassificationRules.cs:57-58` | C-22 |
| `InventoryVoucherLineViewModel.cs:327-328` | **C-20 / G-5** |
| `BatchAllocationViewModel.cs` | C-29, U-6 |
| `InventoryPostingService.cs:348-405` | C-44 / G-8 / U-1 |
| `MainWindowViewModel.cs:2756-2757` | **C-16, C-17 / G-4** |
| `MainWindowViewModel.cs:6403-6428` | C-38…C-48 / G-14 |
| `MainWindow.axaml.cs:481,493` | C-33…C-35 |
| `MainWindow.axaml:2182-2241` | G-15 |
| `Outstandings.cs:137` | G-1 blast radius |
