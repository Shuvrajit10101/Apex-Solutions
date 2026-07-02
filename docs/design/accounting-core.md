# Design — Accounting Core (`Apex.Ledger`)

> **Phase 1 design spec** for the framework-agnostic double-entry ledger engine.
> Scope: the domain entities, invariants, seed data, and report projections that make up
> `src/Apex.Ledger` — a pure C# class library with **no UI and no DB dependencies**
> (persistence is via repository interfaces the shell supplies).
>
> **Grounding:** `plan.md` §4 (Domain/Data Model — 28 groups, 2 ledgers, 24 voucher types),
> `docs/tally-feature-catalog.md` §1/§3/§4/§16/§22, `docs/adr/0001-tech-stack.md` (C#/.NET 10 +
> Avalonia + SQLite), and the regression fixtures `tests/Apex.Ledger.Tests/Fixtures/robert.json`
> and `bright.json`. This design is the load-bearing decision of the whole build (plan.md §1.1,
> §3.2): everything else — inventory, GST, payroll, reports — is a projection or extension over
> this engine.
>
> **Status:** Draft for Phase 1. Existing Phase-0 stubs (`Money`, `DrCr`) are adopted as-is.

---

## 1. Design goals & non-goals

**Goals**

- A **framework-agnostic** domain library: no `System.Windows`, no Avalonia, no EF Core, no
  `Microsoft.Data.Sqlite` references. Persistence is behind `I…Repository` ports so SQLite (or a
  test in-memory store) can be swapped without touching accounting logic (plan.md §3.2 3-tier).
- **Paisa-exact** arithmetic (NFR-3): all money is `System.Decimal`, **never `double`**. Adopts
  the existing `Money` readonly struct (`src/Apex.Ledger/Money.cs`) and `DrCr` enum
  (`src/Apex.Ledger/DrCr.cs`).
- **Fail-fast** posting: an unbalanced or malformed voucher is rejected at `Post` time, never
  persisted (defensive-programming boundary; catalog §1).
- **Reports as pure functions** over the posted voucher set + masters — deterministic, side-effect
  free, unit-testable in isolation. Robert & Bright reproduce their expected totals **to the paisa**.

**Non-goals (deferred to later phases, but the model leaves room)**

- Bill-wise refs, cost-centre allocation, inventory allocation, GST/TDS/TCS breakup on a line
  (Phases 2–4/7). §4.7 defines the extension seams; Phase 1 leaves the collections empty.
- Multi-currency, budgets, scenarios (Phase 2). Base currency is fixed ₹/INR in Phase 1.
- Stock-in-Hand closing-balance *derivation* from inventory (Phase 3). In Phase 1 a Stock-in-Hand
  ledger is a plain ledger with an entered/opening figure (as the Bright fixture models it).

---

## 2. Assembly layout

```
src/Apex.Ledger/                       (class library, netX.0, no UI/DB deps)
├─ Money.cs                 (exists)   value type — decimal rupees, paisa ToString
├─ DrCr.cs                  (exists)   Debit / Credit enum (Tally By/To)
├─ Domain/
│  ├─ GroupNature.cs                   enum { Asset, Liability, Income, Expense }
│  ├─ Company.cs                       tenant/dataset boundary + masters + vouchers
│  ├─ Group.cs                         classification node (nature, parent, flags)
│  ├─ Ledger.cs                        transactional account (under group, opening)
│  ├─ VoucherType.cs                   name, base type, shortcut, numbering
│  ├─ VoucherBaseType.cs              enum of the base kinds (Contra/Payment/…)
│  ├─ NumberingMethod.cs               enum { Automatic, Manual, None }
│  ├─ Voucher.cs                       header + lines + Cancelled/Optional/PostDated
│  └─ EntryLine.cs                     ledger, amount, Dr/Cr (+ future allocations)
├─ Seed/
│  ├─ SeedGroups.cs                    the 28 predefined groups (nature+parent)
│  ├─ SeedLedgers.cs                   Cash, Profit & Loss A/c
│  └─ SeedVoucherTypes.cs             the 24 predefined voucher types
├─ Services/
│  ├─ CompanyFactory.cs                CreateSeeded(name, …)
│  └─ LedgerService.cs                 Post(voucher), Cancel, Delete, numbering
├─ Reports/
│  ├─ TrialBalance.cs                  pure projection
│  ├─ BalanceSheet.cs                  pure projection
│  ├─ ProfitAndLoss.cs                 pure projection
│  ├─ DayBook.cs                       pure projection
│  ├─ LedgerBook.cs                    pure projection (one ledger, running balance)
│  └─ ClassificationRules.cs           P&L vs Balance-Sheet group classification
└─ Persistence/
   ├─ ICompanyRepository.cs            port (implemented in the SQLite adapter project)
   ├─ IVoucherRepository.cs            port
   └─ IMasterRepository.cs            port (groups, ledgers, voucher types)
```

The SQLite/EF adapters live in a **separate** project (`Apex.Ledger.Sqlite` or the shell's data
layer), not here. This file specifies only the domain + service + report contracts.

---

## 3. Value types & identity (already established)

- **`Money`** (exists): `readonly struct` wrapping a `decimal Amount`; `+ - unary-`, comparisons,
  `Zero`, `FromRupees`, and a 2-dp invariant `ToString` ("0.00" = paisa). All engine money uses
  this — never `double`, never raw `decimal` fields on entities.
- **`DrCr`** (exists): `enum { Debit = 0, Credit = 1 }`. Tally shows **By = Debit**, **To =
  Credit**; the F12 "Use Cr/Dr instead of To/By" toggle is a *presentation* concern for the UI,
  not the engine.
- **Identity / rename semantics (plan.md §4 head; verification §A11):** every master carries a
  **stable surrogate id** (`GroupId`, `LedgerId`, `VoucherTypeId`, `CompanyId`), a `Guid`. **The
  name is NOT the key.** Alter renames in place and applies retroactively to all historical
  vouchers, because vouchers reference the id, not the name. Fixtures reference masters by name for
  human readability; the loader resolves name → id on import.

---

## 4. Domain entities

### 4.1 `GroupNature` (enum)

The four accounting natures required by the task. Drives sign convention and where a balance lands
in the statements.

```
public enum GroupNature { Asset, Liability, Income, Expense }
```

> Fixture note: `robert.json`/`bright.json` spell nature as "Assets"/"Liabilities"/"Income"/
> "Expenses". The loader maps those strings → `Asset`/`Liability`/`Income`/`Expense`. The engine's
> canonical spelling is singular.

### 4.2 `Company`

The tenant/dataset boundary; owns all masters and vouchers (catalog §2; plan.md §4.1). One SQLite
`.db` file per company is the persistence boundary, but the domain object is DB-agnostic.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | stable surrogate key |
| `Name` | `string` | required, non-empty |
| `MailingName` | `string` | defaults to `Name`, editable |
| `Address` | `string?` | |
| `Country` | `string` | default "India" |
| `State` | `string?` | |
| `Pin` | `string?` | |
| `FinancialYearStart` | `DateOnly` | default 1-Apr of the working year |
| `BooksBeginFrom` | `DateOnly` | ≥ `FinancialYearStart`; mid-year start allowed |
| `BaseCurrencySymbol` | `string` | default "₹" |
| `BaseCurrencyName` | `string` | default "INR" |
| `DecimalPlaces` | `int` | default 2 |
| `DecimalUnitName` | `string` | default "Paisa" |
| `Groups` | `IReadOnlyList<Group>` | seeded on create |
| `Ledgers` | `IReadOnlyList<Ledger>` | seeded on create |
| `VoucherTypes` | `IReadOnlyList<VoucherType>` | seeded on create |
| `Vouchers` | `IReadOnlyList<Voucher>` | posted set |

*Also seeded but out of Phase-1's reporting scope:* Primary Cost Category, Main Location (see §5.4).

### 4.3 `Group`

Classification node with a nature and a parent; the chart-of-accounts backbone (catalog §3;
plan.md §4.1).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | stable key |
| `Name` | `string` | unique within a company |
| `Nature` | `GroupNature` | Asset / Liability / Income / Expense |
| `ParentId` | `Guid?` | null ⇒ primary group |
| `IsPrimary` | `bool` | `ParentId is null`; one of the 15 primary heads |
| `IsPredefined` | `bool` | true for the 28 seeds — **cannot be deleted** (§6) |
| `Alias` | `string?` | optional short name |

**Invariant:** a child group inherits the *statement placement* of its primary ancestor (nature may
be stored redundantly but must equal the root's nature for a predefined subtree). See §7 classification.

### 4.4 `Ledger`

The transactional account — the thing a voucher line actually posts to (catalog §3; plan.md §4.1).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | stable key |
| `Name` | `string` | unique within a company |
| `GroupId` | `Guid` | the group this ledger is *Under*; required |
| `OpeningBalance` | `Money` | magnitude only, always ≥ 0 |
| `OpeningIsDebit` | `bool` | `true` = opening Dr, `false` = opening Cr |
| `Alias` | `string?` | optional |
| `IsPredefined` | `bool` | true for Cash and Profit & Loss A/c |

Design choice: opening balance is stored as **(magnitude `Money`, `OpeningIsDebit` bool)** rather
than a signed amount, mirroring the fixtures' `{openingBalance, openingSide}` shape and Tally's
"Opening Balance … Dr/Cr" prompt. A zero opening has `OpeningIsDebit` set to the account's natural
side but contributes nothing.

*Feature-gated blocks* (bill-by-bill, credit limit, interest, bank details, "inventory values
affected?", cost-centres-applicable, GST/TDS/TCS sub-screens, PAN/MSME) are **out of Phase-1
scope** — modelled as later optional value objects hung off `Ledger`, absent for now.

### 4.5 `VoucherType`

Defines a class of vouchers with its base behaviour, default shortcut, and numbering (catalog §4;
plan.md §4.1).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | stable key |
| `Name` | `string` | e.g. "Payment", "Sales" |
| `BaseType` | `VoucherBaseType` | the built-in kind it derives from |
| `DefaultShortcut` | `string?` | e.g. "F5", "Alt+F6"; null for the inactive/other types |
| `Numbering` | `NumberingMethod` | Automatic / Manual / None |
| `Abbreviation` | `string?` | e.g. "Pymt", "Sale" |
| `IsActive` | `bool` | Payroll/Job-Work types inactive until their F11 feature is on |
| `IsPredefined` | `bool` | true for the 24 seeds |

```
public enum VoucherBaseType {
    Contra, Payment, Receipt, Journal, Sales, Purchase, CreditNote, DebitNote,
    StockJournal, PhysicalStock, SalesOrder, PurchaseOrder, DeliveryNote, ReceiptNote,
    RejectionOut, RejectionIn, Memorandum, ReversingJournal, JobWorkInOrder, MaterialIn,
    JobWorkOutOrder, MaterialOut, Attendance, Payroll
}

public enum NumberingMethod { Automatic, Manual, None }
```

### 4.6 `Voucher`

A single balanced transaction: header + entry lines (catalog §4; plan.md §4.1).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | stable key |
| `TypeId` | `Guid` | the `VoucherType` |
| `Number` | `int` | sequence within its type (see numbering §8.3) |
| `Date` | `DateOnly` | within/after `BooksBeginFrom` |
| `Narration` | `string?` | free text |
| `PartyId` | `Guid?` | optional party ledger (invoice types) |
| `Lines` | `IReadOnlyList<EntryLine>` | **≥ 2**, balanced |
| `Cancelled` | `bool` | Alt+X — number retained in sequence, greyed in Day Book |
| `Optional` | `bool` | Ctrl+L — excluded from live balances until regularised |
| `PostDated` | `bool` | Ctrl+T — excluded from balances until its date is reached |

**Cancel vs Delete (verification §A14):** *Cancel* (`Alt+X`) sets `Cancelled = true` and keeps the
number in sequence (a cancelled voucher shows greyed in the Day Book with zero effect). *Delete*
(`Alt+D`) removes the voucher entirely and may leave a gap in numbering. These are two different
operations on `LedgerService` (§8.2).

### 4.7 `EntryLine`

One posting: a ledger, an amount, and a side (catalog §4; plan.md §4.1).

| Field | Type | Notes |
|---|---|---|
| `LedgerId` | `Guid` | the account posted to |
| `Amount` | `Money` | magnitude, always > 0 (a zero line is invalid) |
| `Side` | `DrCr` | Debit or Credit |

**Extension seam (later phases, empty in Phase 1):** optional sub-allocation collections —
`BillRefs`, `InventoryAllocations`, `CostAllocations`, `TaxBreakups`, `BankAllocations`. Modelled as
empty read-only lists now so the shape is stable; each populates in its owning phase (2/3/4/7).

---

## 5. Seed data (applied on every `Company.CreateSeeded`)

The seed is itself a **fixture-backed unit test** (plan.md §4.4): a fresh company must contain
*exactly* these — 28 groups, 2 ledgers, 24 voucher types, Primary Cost Category, Main Location,
₹/INR 2-dp "Paisa", FY 1-Apr→31-Mar. A 28-group count assertion guards against the historical
28-vs-29 drift (verification §A6/A7 — "Bank OCC A/c" is an **alias** of Bank OD A/c, not a 29th
group; P&L A/c is a **ledger**, not a 29th group per §A8).

### 5.1 The 28 predefined groups (Name — Nature — Parent)

**15 Primary** (9 Balance-Sheet + 6 P&L). `Parent = —` means primary (`ParentId is null`).

| # | Group | Nature | Parent | Statement |
|---|---|---|---|---|
| 1 | Capital Account | Liability | — | Balance Sheet |
| 2 | Loans (Liability) | Liability | — | Balance Sheet |
| 3 | Current Liabilities | Liability | — | Balance Sheet |
| 4 | Fixed Assets | Asset | — | Balance Sheet |
| 5 | Investments | Asset | — | Balance Sheet |
| 6 | Current Assets | Asset | — | Balance Sheet |
| 7 | Branch / Divisions | Asset | — | Balance Sheet |
| 8 | Misc. Expenses (Asset) | Asset | — | Balance Sheet |
| 9 | Suspense A/c | Liability | — | Balance Sheet |
| 10 | Sales Accounts | Income | — | Profit & Loss |
| 11 | Purchase Accounts | Expense | — | Profit & Loss |
| 12 | Direct Incomes | Income | — | Profit & Loss |
| 13 | Indirect Incomes | Income | — | Profit & Loss |
| 14 | Direct Expenses | Expense | — | Profit & Loss |
| 15 | Indirect Expenses | Expense | — | Profit & Loss |

**13 Sub-groups** (canonical parent). Nature inherits from the primary ancestor.

| # | Group | Nature | Parent |
|---|---|---|---|
| 16 | Reserves & Surplus | Liability | Capital Account |
| 17 | Bank OD A/c *(alias: Bank OCC A/c)* | Liability | Loans (Liability) |
| 18 | Secured Loans | Liability | Loans (Liability) |
| 19 | Unsecured Loans | Liability | Loans (Liability) |
| 20 | Duties & Taxes | Liability | Current Liabilities |
| 21 | Provisions | Liability | Current Liabilities |
| 22 | Sundry Creditors | Liability | Current Liabilities |
| 23 | Bank Accounts | Asset | Current Assets |
| 24 | Cash-in-Hand | Asset | Current Assets |
| 25 | Deposits (Asset) | Asset | Current Assets |
| 26 | Loans & Advances (Asset) | Asset | Current Assets |
| 27 | Stock-in-Hand | Asset | Current Assets |
| 28 | Sundry Debtors | Asset | Current Assets |

> **Misc. Expenses (Asset)** carries `Nature = Asset` despite the word "Expenses" — it is the
> deferred-revenue-expenditure head shown on the Balance Sheet asset side, not a P&L expense
> (verification §A6/A7). The classification rule (§7) keys off group ancestry, not the name.

### 5.2 The 2 default ledgers

| Ledger | Under (Group) | Opening | `IsPredefined` |
|---|---|---|---|
| Cash | Cash-in-Hand | 0, Debit | true |
| Profit & Loss A/c | *(primary reserved head)* | 0, Credit | true |

**Profit & Loss A/c is a reserved ledger/head, not a 29th group** (verification §A8). It appears on
the Balance Sheet liabilities/capital side and receives the period net profit (see §7.3). In Phase
1 it is modelled as a predefined ledger whose "group" is the reserved P&L head; its Balance-Sheet
line is computed (opening P&L brought forward + current-period net profit), never entered directly.

### 5.3 The 24 predefined voucher types (Name — Base type — Shortcut — Numbering)

The **16 accounting/inventory core types** (catalog §4 table):

| # | Name | Base type | Shortcut | Numbering |
|---|---|---|---|---|
| 1 | Contra | Contra | F4 | Automatic |
| 2 | Payment | Payment | F5 | Automatic |
| 3 | Receipt | Receipt | F6 | Automatic |
| 4 | Journal | Journal | F7 | Automatic |
| 5 | Sales | Sales | F8 | Automatic |
| 6 | Purchase | Purchase | F9 | Automatic |
| 7 | Credit Note | CreditNote | Alt+F6 | Automatic |
| 8 | Debit Note | DebitNote | Alt+F5 | Automatic |
| 9 | Stock Journal | StockJournal | Alt+F7 | Automatic |
| 10 | Physical Stock | PhysicalStock | F10 (Physical Stock) | Automatic |
| 11 | Sales Order | SalesOrder | Ctrl+F8 | Automatic |
| 12 | Purchase Order | PurchaseOrder | Ctrl+F9 | Automatic |
| 13 | Delivery Note | DeliveryNote | Alt+F8 | Automatic |
| 14 | Receipt Note | ReceiptNote | Alt+F9 | Automatic |
| 15 | Rejection Out | RejectionOut | Ctrl+F5 | Automatic |
| 16 | Rejection In | RejectionIn | Ctrl+F6 | Automatic |

The **8 additional predefined types** (`F10 Other Vouchers → Show Inactive`; catalog §4). Payroll &
Job-Work types are inactive until their F11 feature is enabled (verification §A15):

| # | Name | Base type | Shortcut | Numbering | `IsActive` default |
|---|---|---|---|---|---|
| 17 | Memorandum | Memorandum | — (F10) | Automatic | true |
| 18 | Reversing Journal | ReversingJournal | — (F10) | Automatic | true |
| 19 | Job Work In Order | JobWorkInOrder | — | Automatic | false (Job-Work F11) |
| 20 | Material In | MaterialIn | — | Automatic | false (Job-Work F11) |
| 21 | Job Work Out Order | JobWorkOutOrder | — | Automatic | false (Job-Work F11) |
| 22 | Material Out | MaterialOut | — | Automatic | false (Job-Work F11) |
| 23 | Attendance | Attendance | — (Ctrl+F4 area) | Automatic | false (Payroll F11) |
| 24 | Payroll | Payroll | Ctrl+F4 | Automatic | false (Payroll F11) |

Total = 16 + 8 = **24**. The seed asserts exactly 24 types and the eight core accounting shortcuts
(F4–F9, Alt+F5/F6) resolve to the right base types.

### 5.4 Other seeds

- **Primary Cost Category** — default cost category "Primary Cost Category" (catalog §6/§22).
  Modelled but unused by Phase-1 reports; present so the seed count is faithful.
- **Main Location** — default godown "Main Location" (catalog §9/§22). Same status.
- **Base currency** ₹ / INR, 2 decimals, "Paisa"; **FY** 1-Apr → 31-Mar.

---

## 6. Invariants (enforced by the engine)

1. **Balanced voucher (the golden invariant).** For every posted (non-cancelled) voucher,
   **Σ Debit amounts = Σ Credit amounts**, computed in `Money` (`decimal`). A voucher failing this
   is rejected by `LedgerService.Post` and never persisted. (catalog §1; plan.md §4.1; NFR-3.)
2. **≥ 2 lines.** A voucher has at least two entry lines (a single-sided posting is impossible in
   double entry).
3. **Positive line amounts.** Every `EntryLine.Amount > 0`; the side (`Dr/Cr`) carries the
   direction. No zero-value or negative lines (zero-valued inventory lines are a Phase-6 feature
   under a voucher-type flag, out of Phase-1 scope).
4. **Money is `System.Decimal`, never `double`.** All amounts flow through `Money`. Binary floating
   point is banned in the engine (NFR-3; ADR-0001). Rounding, where needed later (tax), is
   half-up to 2 dp on `decimal`.
5. **Ledger must belong to a known group; voucher lines must reference known ledgers.** Referential
   integrity is checked at post time (fail-fast).
6. **Predefined masters cannot be deleted.** The 28 predefined groups and the 2 predefined ledgers
   have `IsPredefined = true`; `Delete` on any of them throws. (catalog §3; plan.md §4.1.)
7. **Delete guards (catalog §3).** A group with sub-groups or contained ledgers, or a ledger with
   transactions, cannot be deleted. The closing balance of a Stock-in-Hand ledger cannot be altered
   directly (Phase-3 concern; noted here for completeness).
8. **Opening balances self-balance.** Across all ledgers, Σ(opening Dr) = Σ(opening Cr). Both
   fixtures satisfy this; the seed's two zero-opening ledgers trivially preserve it. The engine
   surfaces any opening imbalance to a *Difference in Opening Balances* line (Tally behaviour) but
   Phase-1 fixtures are pre-balanced.
9. **Date within books.** `Voucher.Date >= Company.BooksBeginFrom`. Post-dated vouchers are allowed
   (flagged) but excluded from as-of balances until their date is reached.
10. **Trial Balance always balances.** A structural consequence of (1) + (8): Σ closing Dr =
    Σ closing Cr for the whole ledger set at any as-of date. Asserted by the fixtures
    (Robert: 137000 = 137000; Bright: 273000 = 273000).

---

## 7. Report projections & their math

All reports are **pure functions** over `(masters, postedVouchers, asOfDate)`. "Posted" excludes
`Cancelled`, `Optional`, and (for a given as-of) `PostDated`-and-not-yet-due vouchers. The core
primitive is the **ledger closing balance**.

### 7.1 Ledger closing balance (the shared primitive)

For a ledger `L` as of date `D`:

```
signed(L)  = (OpeningIsDebit ? +Opening : −Opening)
           + Σ over posted lines on L with line.Date ≤ D of
               (line.Side == Debit ? +Amount : −Amount)

closing(L) = signed(L) >= 0  ⇒  (Debit,  |signed|)
             signed(L) <  0  ⇒  (Credit, |signed|)
```

Convention: **Debit is positive, Credit is negative** internally; the (side, magnitude) pair is the
external shape (matches the fixtures' `{side, amount}`). *Worked check (Robert, Cash):* opening
+70000 Dr, then −25000 −6000 −4000 +12000 −1500 −5000 −8000 −10000 = +22500 ⇒ **22500 Debit** ✓
(fixture `ledgerClosing.Cash`).

### 7.2 Trial Balance

For every ledger, its `closing(L)` placed in the Dr or Cr column; totals summed.

- **Projection:** `IReadOnlyList<TrialBalanceRow>` where each row = `(LedgerName, GroupName,
  DebitAmount, CreditAmount)` (exactly one column non-zero).
- **Totals:** `TotalDebit = Σ debit rows`, `TotalCredit = Σ credit rows`.
- **Invariant:** `TotalDebit == TotalCredit` (§6.10). *Robert:* 137000 = 137000 ✓. *Bright:*
  273000 = 273000 ✓.
- Grouped/condensed presentation (roll ledgers up to their group) is a display option; the raw
  projection is per-ledger.

### 7.3 Profit & Loss

Classification (catalog §1/§16; plan.md §4): a ledger is a **P&L ledger** iff its group's primary
ancestor is one of **Sales Accounts, Purchase Accounts, Direct Incomes, Indirect Incomes, Direct
Expenses, Indirect Expenses** (the 6 P&L primaries). Everything else is Balance Sheet.

- **Income side** = P&L ledgers whose ancestor nature is `Income` (Sales/Direct/Indirect Incomes),
  taken at their credit magnitude.
- **Expense side** = P&L ledgers whose ancestor nature is `Expense` (Purchase/Direct/Indirect
  Expenses), taken at their debit magnitude.
- **Net profit** = `TotalIncome − TotalExpenses` (positive ⇒ profit, credit to Capital/P&L side;
  negative ⇒ loss).
- **Projection:** `ProfitAndLoss { Income[], TotalIncome, Expenses[], TotalExpenses, NetProfit }`.
- *Robert:* income 37000 (Freight Income), expenses 32000 ⇒ **net profit 5000** ✓
  (`profitAndLoss.netProfit`).

**Trading account / periodic inventory (Bright):** where opening/closing stock and Purchases are
present, gross profit = `Sales + ClosingStock − OpeningStock − Purchases − DirectExpenses`, then
net profit = `GrossProfit + OtherIncome − IndirectExpenses`. *Bright:* 73000 + 15000 − 25000 −
40000 − (6000+2000) = **15000 gross profit**; 15000 − (7000+3000+6000) = **−1000 net (loss)** ✓
(`tradingAndProfitAndLoss`). Phase 1 treats Opening/Closing Stock and the closing-stock adjustment
as ordinary ledgers exactly as the fixture posts them; the *inventory-derived* closing-stock value
is finalised by the Phase-3 engine. The P&L projection therefore takes a
`ClosingStockMode ∈ {AsPostedLedger, InventoryDerived}` parameter, defaulting to `AsPostedLedger`
in Phase 1.

### 7.4 Balance Sheet

Every **non-P&L** ledger's closing balance, grouped by its primary ancestor, split Liabilities vs
Assets by nature.

- **Liabilities/Capital side** = ledgers under primary groups of nature `Liability` (Capital
  Account, Loans, Current Liabilities, Suspense, …) at their credit magnitude, **plus the period
  net profit** flowing from the P&L into the Capital / P&L A/c line (§7.3). A loss reduces that side.
- **Assets side** = ledgers under primary groups of nature `Asset` (Fixed Assets, Current Assets,
  Investments, Misc. Expenses (Asset), Branch/Divisions) at their debit magnitude.
- **Invariant:** `TotalLiabilities == TotalAssets` — guaranteed because
  `Assets − Liabilities(excl. profit) = NetProfit` by the balanced-voucher + self-balancing-opening
  invariants.
- **Projection:** `BalanceSheet { Liabilities[], TotalLiabilities, Assets[], TotalAssets,
  NetProfitInCapital }`.
- *Robert:* liabilities = Capital 100000 + Net Profit 5000 = 105000; assets = Truck 40000 + Cash
  22500 + SBI 32500 + Debtors 10000 = 105000 ✓. *Bright:* liabilities 184000 = assets 184000, with
  net profit −1000 folded into capital and Machinery shown net of depreciation (54000) ✓.

### 7.5 Day Book

All vouchers within a date range in **chronological order** (then by number within a date), each
with its type, number, party, narration, and Dr/Cr totals; cancelled vouchers shown greyed with
zero effect (catalog §16).

- **Projection:** `IReadOnlyList<DayBookRow>` = `(Date, VoucherTypeName, Number, PartyOrParticulars,
  Amount, IsCancelled)`, ordered by `(Date, Number)`.

### 7.6 Ledger book (one ledger)

Opening balance, then every posting to that ledger in date order, with a **running balance** after
each (catalog §16 Account Books).

- **Projection:** `LedgerBook { LedgerName, OpeningSide, OpeningAmount, Rows[], ClosingSide,
  ClosingAmount }` where each row = `(Date, VoucherTypeName, Number, CounterParticulars, Debit,
  Credit, RunningSide, RunningAmount)`.
- **Running balance** uses the §7.1 signed accumulation, emitting `(side, magnitude)` after each
  line. The final row's running balance equals `closing(L)`.
- **Cash Book / Bank Book** are the ledger book specialised to a Cash-in-Hand / Bank-Accounts
  ledger (same projection, filtered master).

### 7.7 Classification rules module

`ClassificationRules` centralises the P&L-vs-Balance-Sheet decision so all reports agree:

```
static bool IsProfitAndLossGroup(Group g, IMasterLookup masters);   // ancestor ∈ 6 P&L primaries
static bool IsBalanceSheetGroup(Group g, IMasterLookup masters);    // = !IsProfitAndLoss
static GroupNature PrimaryNatureOf(Group g, IMasterLookup masters); // walks to the root
```

Walks `ParentId` to the primary ancestor; the six P&L primaries are matched by identity (seeded
ids), not by name, so a rename can't break classification.

---

## 8. Service contracts (proposed C# signatures)

Namespace `Apex.Ledger` (matching the existing stubs). Signatures are illustrative; exact shapes
firm up under TDD in Phase 1.

### 8.1 `CompanyFactory`

```csharp
public static class CompanyFactory
{
    /// Creates a fully seeded company: 28 groups, 2 ledgers, 24 voucher types,
    /// Primary Cost Category, Main Location, ₹/INR 2-dp, FY 1-Apr→31-Mar.
    public static Company CreateSeeded(
        string name,
        DateOnly? financialYearStart = null,   // default 1-Apr of current working year
        DateOnly? booksBeginFrom     = null);   // default = financialYearStart

    /// The canonical seed sets, exposed for the seed-verification unit test.
    public static IReadOnlyList<Group>       SeedGroups(Company c);
    public static IReadOnlyList<Ledger>      SeedLedgers(Company c);
    public static IReadOnlyList<VoucherType> SeedVoucherTypes(Company c);
}
```

### 8.2 `LedgerService`

```csharp
public sealed class LedgerService
{
    public LedgerService(Company company /*, or repository ports in the persisted variant */);

    /// Validates invariants (§6) then appends the voucher to the posted set.
    /// Throws UnbalancedVoucherException / InvalidVoucherException on failure — never persists a bad voucher.
    public Voucher Post(Voucher voucher);

    /// Alt+X — mark cancelled; keeps the number in sequence, zero effect on balances.
    public void Cancel(Guid voucherId);

    /// Alt+D — remove entirely; may gap the numbering sequence.
    public void Delete(Guid voucherId);

    /// Next automatic number for a voucher type (§8.3).
    public int NextNumber(Guid voucherTypeId);
}
```

Validation helper (used by `Post`, also directly unit-testable):

```csharp
public static class VoucherValidator
{
    public static bool IsBalanced(Voucher v);          // Σ Dr == Σ Cr in Money/decimal
    public static (Money debit, Money credit) Totals(Voucher v);
    public static void EnsureValid(Voucher v, Company c); // throws on any §6 violation
}
```

### 8.3 Numbering

`NextNumber` returns `max(existing numbers of that type) + 1` for `Automatic`; `Manual` accepts a
caller-supplied number (uniqueness checked); `None` leaves `Number = 0`. Cancel keeps the number;
Delete can leave a gap (verification §A14). Numbering is **per voucher type**, per company.

### 8.4 Reports (pure functions)

```csharp
public static class Reports
{
    public static TrialBalance  TrialBalance (Company c, DateOnly asOf);
    public static ProfitAndLoss ProfitAndLoss(Company c, DateOnly from, DateOnly to,
                                              ClosingStockMode mode = ClosingStockMode.AsPostedLedger);
    public static BalanceSheet  BalanceSheet (Company c, DateOnly asOf);
    public static IReadOnlyList<DayBookRow> DayBook(Company c, DateOnly from, DateOnly to);
    public static LedgerBook LedgerBook(Company c, Guid ledgerId, DateOnly from, DateOnly to);
}

public enum ClosingStockMode { AsPostedLedger, InventoryDerived }
```

Each returns an immutable record; none mutates the company. This makes them trivially testable
against the fixtures' `expected` blocks.

---

## 9. Fixtures as the regression contract

Robert and Bright (plan.md §6.3; R8) are the standing engine baseline. The Phase-1 test suite:

1. **Load** each fixture (masters + vouchers), resolving names → ids.
2. **Seed check** — a fresh `CreateSeeded` company contains exactly the 28 groups / 2 ledgers / 24
   voucher types (count + identity assertions; guards the 28-vs-29 drift).
3. **Post** all vouchers via `LedgerService.Post`; assert none is rejected and each balances.
4. **Assert to the paisa** against each fixture's `expected`:
   - `ledgerClosing` — every ledger's `(side, amount)`.
   - `trialBalance` — totals equal and `balanced`.
   - `profitAndLoss` / `tradingAndProfitAndLoss` — income, expense, gross/net profit.
   - `balanceSheet` — both sides and `balanced`, net profit folded into capital, Machinery net of
     depreciation (Bright).
5. **Any red = stop** (the recorded R9 / verification-before-completion lesson) — a green gate must
   not hide a real bug.

Robert exercises the accounts-only path (Contra/Payment/Receipt/Sales/Purchase + net profit into
Balance Sheet). Bright adds year-end depreciation (Journal) and periodic closing stock, previewing
the Phase-3 inventory integration while staying accounts-only in Phase 1.

---

## 10. Open items handed to later phases

- **Stock-in-Hand derivation** (Phase 3): closing stock becomes inventory-derived; `ProfitAndLoss`
  switches to `ClosingStockMode.InventoryDerived`.
- **Line sub-allocations** (Phases 2/3/4/7): bill refs, cost centres, inventory, GST/TDS/TCS.
- **Feature-gated ledger blocks & F11/F12 config layer** (Phase 1 UI onward): bill-by-bill, credit
  limit, bank details, statutory sub-screens.
- **Difference in Opening Balances** surfacing (edge case): Phase-1 fixtures are pre-balanced;
  general opening-imbalance handling can wait until a company-create UI exists.
- **Optional / Post-dated exclusion semantics** are modelled now (flags on `Voucher`) but only
  fully exercised once Phase-2 banking (post-dated Ctrl+T) lands.

---

*This design is framework-agnostic by construction: no entity, service, or report here references a
UI toolkit or a database. Persistence enters only through the `Persistence/` ports, implemented in a
separate adapter. The one load-bearing thing — the double-entry posting engine and its report
projections — is isolated and exhaustively fixture-tested (plan.md §1.1, §3.3).*
