# TallyPrime fidelity defect register

**Author:** A1 (Business Analyst) · **Date:** 2026-08-04 · **Status:** for review
**Fidelity target:** TallyPrime. Tally 7.2 is a checklist only, never the spec.
**Inputs merged:** four independent fidelity sweeps — *wrong defaults*, *fields we demand that Tally fills*,
*block vs warn*, *masters* — deduplicated, re-verified against the current worktree, and ranked.

---

## ⚠️ RE-VERIFIED 2026-08-15 AGAINST HEAD `c56e5c3` — READ THIS BEFORE TRUSTING ANY ROW

🔴 **THE `D`-NUMBERS IN THIS FILE ARE LOCAL TO IT. Always cite them as "`tally-fidelity-defects.md` D-n".**
`docs/tally-gap-decisions.md` has a **different** D3/D7/D12/D13, and the eight "diverged rule" copies use a
**third** D1–D8. Confusion between the three has already cost this project real time.

All 19 rows were re-opened file-by-file on **2026-08-15**. Corrections are marked **†**, carried as a `†` line
under the affected row's severity line, and the original claim is always quoted beside the correction —
**nothing was silently rewritten.**

**THREE ROWS ARE NOW FIXED IN CODE. All three landed on 2026-08-06, two days after this register was written,
and none was folded back in:**

| Row | Fixed by | Proof at HEAD |
|---|---|---|
| **D1** Payment/Receipt/Contra open in Double Entry | `f277318` | `VoucherEntryViewModel.SeedOpeningMode()` at `:141-144` — `if (CanBeSingleEntry) Mode = VoucherEntryMode.SingleEntry;` — called from the constructor at `:1194`. The field initialiser (now `:100`) is explicitly documented at `:96-98` as **NOT** the opening mode. |
| **D4** Ledger master has no Opening Balance field | `c8b44cf` | `MainWindow.axaml:4356-4387` is a real amount `TextBox` (`OpeningBalanceText`) + Dr/Cr `ComboBox` (`OpeningSide`), placed under "Under" per corpus field order; `LedgerMasterViewModel.cs:1053-1054` writes `OpeningBalance`/`OpeningIsDebit` to the domain. |
| **D7** Negative stock is an unconditional hard block | `a12e651` (schema **v50**) | The throwing guard is **deleted**; `InventoryPostingService.cs:176` `DetectNegativeStock()` never throws, `:184-185` `NegativeStockWarnings()` is gated on `Company.cs:268` `WarnOnNegativeStock = true`. |

**TWO ROWS ARE HALF-FIXED and their unqualified wording is now misleading** — **D9** and **D18**, both because
the **G-4 resolver fallback they lean on no longer exists**. `MainWindowViewModel.cs:2756-2757` is now unrelated
code; commit `7bfc2c6` routed all five call sites through `VoucherTypeResolver.ResolveForEntry`, and
`src/Apex.Ledger/Services/VoucherTypeResolver.cs:58` reads
`if (type.BaseType != baseType || !type.IsActive) continue;`. **`IsActive` is no longer decorative — it is
load-bearing.** That makes **D18's fix instruction actively dangerous as written** (see its † note).

**THE REMAINING 14 ROWS AND U-A ARE STILL TRUE**, but most carry drifted citations. `VoucherEntryViewModel.cs`
has moved ~+55 lines in its first third and **~+215 lines past `:4200`**; `MainWindow.axaml` ~+39 lines around
the stock-item masters. Corrected in place below.

🔴 **§1's counts are NOT re-cut and are wrong as totals.** They still say 19 defects / 11 HIGH, which counted
D1, D4 and D7 as open. **16 rows are open at HEAD.** The per-row detail is authoritative; the summary arithmetic
is not. Re-cutting was declined deliberately — it would erase the record of what the register found on 2026-08-04.

🔴 **§6 is now itself stale in two places** — see the † note there.

---

## How to read this document

**Ranking rule.** Rows are ordered by **how often an operator meets the defect**, not by how large it looks.
A wrong default on every payment outranks a missing field on a rare voucher. Severity is recorded but does
**not** drive the order.

**Verification.** I re-read every `file:line` in this register against the current worktree and re-ran the
load-bearing corpus greps myself. Rows are marked:

> **† 2026-08-15 — "the current worktree" means 2026-08-04's, not today's.** That sentence is the one most likely
> to be believed on a skim, and it stopped being true within two days. Every `file:line` was re-read again on
> **2026-08-15 against HEAD `c56e5c3`**; where it had moved, the correction is the `†` line under the row.
> **A `[code-verified]` mark below therefore certifies 2026-08-04, and a `†` line certifies 2026-08-15.**

| Mark | Meaning |
|---|---|
| **[code-verified]** | I opened the file at that line this session and the quoted behaviour is what the code does |
| **[corpus-verified]** | I extracted the cited page/line with `pdftotext -layout` this session |
| **[web]** | the Tally-side claim rests on `help.tallysolutions.com` only — the licensed corpus is silent |
| **[inherited]** | carried from the upstream sweep, not independently re-read |

**Corpus tags** (git-ignored, never committed, at `…\Apex Solutions(end)\tally\`):
**BOOK** = `664311548-Tally-Prime-Book.pdf` · **SG** = `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` ·
**GSTN** = `703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf` · **TB2** = `719244897-Tally-Book.pdf`.
BOOK/SG page numbers are *printed* pages; GSTN references are extracted-text line numbers.

**Companion document.** `docs/voucher-entry-specification.md` holds the four-layer config model, the 48-row
condition matrix (C-01…C-48) and the per-voucher walkthroughs. This register does **not** repeat them —
where a row maps onto a spec condition or gap, the ID is given. See §5 for the spec rows *not* in this
register, and §6 for three places where the spec is now **stale**.

---

## 1. Executive summary

**19 distinct defects** survive deduplication of the four sweeps: **18 with a citation on both sides**, and
**1 where the TallyPrime behaviour is unproven** (§4, kept out of the ranked register on purpose).

| Severity | Count | |
|---|---|---|
| **HIGH** | 11 | wrong on a screen the operator uses daily, or blocks a documented workflow outright |
| **MEDIUM** | 6 | wrong or missing, with a workaround the operator must discover |
| **LOW** | 1 | divergence that makes the product feel unfamiliar without costing work |
| **UNVERIFIED** | 1 | our code is provably wrong *about itself*; whether Tally differs is unproven |

**The shape of the problem.** These are not nineteen unrelated bugs. Three root causes produce most of them:

1. **We ask the operator to type what Tally computes.** Seven rows (D2, D3, D5, D11, D14, D16, and the
   unverified U-A) are the same defect wearing different clothes: a field that TallyPrime fills from data the
   app already holds, left blank for the operator to key. The auto-fill machinery *exists* in this codebase —
   `SyncInvoiceBillWise` and `AutoFillFromOrder` both do it correctly, with a proper operator-dirty ownership
   rule — it is simply wired to one screen instead of all of them.
2. **We block where Tally warns.** Four rows (D7, D8, D14, D15) refuse a voucher TallyPrime accepts. Our
   entry surface has exactly **one** non-blocking warning in it (the batch-expiry flag); everything else is a
   hard stop or nothing. TallyPrime's model is post-entry correction — record the voucher, surface the gap in
   an exception report at return time.
3. **We have no layer-2 config and no voucher-type master.** D9 and D10 are the structural reason so many of
   the others read as *"the right behaviour behind the wrong trigger"*.

**The two rows to fix first**, on frequency alone, are D1 and D2. Together they mean every payment, receipt
and contra in the product opens in the wrong layout **and** then asks for the same figure twice — and D1
makes D2 worse, because it forces the Dr/Cr grid to be the screen the operator lands on.

**Read §3 before scheduling any of this.** The register is a sample, not a census.

---

## 2. THE REGISTER

Ranked by operator encounter frequency, most-met first. Area codes: **VE-D** voucher entry / defaults &
auto-fill · **VE-V** voucher entry / validation · **MST** masters · **CFG** structural configuration.

---

### D1 · ~~Payment, Receipt and Contra open in Double Entry~~ — **FIXED IN CODE**
**HIGH** · Area **VE-D** · spec C-33, G-6 · ✅ **CLOSED 2026-08-15**

> **† 2026-08-15 — THIS ROW READ THE OPPOSITE OF THE CODE. FIXED by `f277318` ("Payment, Receipt and Contra open
> in Single Entry, as Tally ships them", 2026-08-06 — two days after this register was written). Everything below
> describes the pre-fix state; do not schedule it.** Verified at HEAD `c56e5c3`:
> - The fix is exactly what the Fix cell prescribed: **`VoucherEntryViewModel.cs:141-144`**
>   `private void SeedOpeningMode() { if (CanBeSingleEntry) Mode = VoucherEntryMode.SingleEntry; }`, called at
>   **`:1194`**, *after* the two starter `AddLine` calls at `:1187-1188` so `OnModeChanged`'s polarity stamp has
>   lines to act on.
> - **`:90-91` "the default" with no Tally citation is GONE.** `:102-140` is now `SeedOpeningMode`'s doc block
>   carrying the GSTN evidence this row asked for (lines 330/334, 1634, 1965) — **and it records the one apparent
>   counter-example (GSTN line 330) rather than burying it**, plus the residual uncertainty about the ERP-9-era
>   F12 control that the Fix cell asked to be written down.
> - The field initialiser survives at **`:100`** but `:96-98` now states plainly: *"**This field initialiser is
>   NOT the opening mode.**"*
> **Citations corrected:** `:96` → **`:100`** · `:1176` `CanBeSingleEntry` → **`:1231-1232`** · `:1184`
> `IsSingleEntry` → **`:1239`** · polarity inversion `:1192-1197` → **`:1247-1252`** · `ShowPlainDrCrGrid` `:132`
> → **`:180`** · the Ctrl+H toggle `:3183-3185` → **`:3393`/`:3397-3400`**.

| | |
|---|---|
| **What the operator experiences** | The three highest-volume vouchers in the product open in the wrong layout **every single time**. They must press Ctrl+H before every payment, receipt and contra, and then make two Dr/Cr side decisions per voucher that TallyPrime never asks for — on a screen where getting the side backwards silently reverses a cash or bank entry. |
| **What we do** | `VoucherEntryViewModel.cs:96` — `[ObservableProperty] private VoucherEntryMode _mode = VoucherEntryMode.AsVoucher;` is the unconditional initial mode for all 23 types. Single Entry **exists and is correct**: `:1176` `CanBeSingleEntry` (Contra/Payment/Receipt), `:1184` `IsSingleEntry`, polarity inversion at `:1192-1197`. It is only ever reached by Ctrl+H (`:3183-3185`, an AsVoucher ⟷ SingleEntry toggle), so `ShowPlainDrCrGrid` (`:132`) renders the Dr/Cr grid on first open, always. The XML doc at `:90-91` calls AsVoucher "the default" with no Tally citation. **[code-verified]** |
| **What Tally does** | Ships Payment/Receipt/Contra in Single Entry — an `Account` field plus a `Particulars` list, no Dr/Cr labels. **Note the shape of the evidence:** the corpus never tells the reader to turn Single Entry *on*; it tells them three separate times to turn it **off** to reach the Dr/Cr screen, which is only meaningful if the shipped state is Single Entry. |
| **Citation** | GSTN extracted lines 330/334 ("Use single entry mode for payment/receipt/contra vouchers? NO"), 1634 (step 2, Receipt against reference), 1965 (Payment with cost centres) — **[corpus-verified]**, all four hits re-extracted this session. Layout corroboration: BOOK pp.26-27, 29, 31-32 ("In Single Entry Mode Dr & Cr not Show"); SG p.76. **[inherited]** |
| **Fix** | Seed the mode per type instead of a single literal: initialise `Mode` to `SingleEntry` when `CanBeSingleEntry`, `AsVoucher` otherwise. `IsSingleEntry` already guards a forced mode on a Journal and `ShowPlainDrCrGrid` already stops the two grids co-rendering, so nothing downstream changes. Replace the uncited "the default" wording at `:90-91` with the GSTN citation, and record the residual uncertainty: the corpus uses the ERP-9-era F12 label, so what carries over is the *shipped state*, not the control. |

---

### D2 · The balancing amount on the Dr/Cr grid is typed — TallyPrime captures it automatically
**HIGH** · Area **VE-D** · spec G-17 · *(merged: "wrong defaults" #2 + "fields" #4)*

> **† 2026-08-15 — verdict STANDS; nothing between `AddLine` and `Recalculate` seeds a balancing amount.**
> `VoucherLineViewModel.cs:560`/`:569` and `InventoryVoucherLineViewModel.cs:296-308` are unchanged and exact.
> **Corrected:** `AddLine` `:1161-1167` → **`:1216-1222`** · `Recalculate`/`IsBalanced` `:1374-1381` →
> **`:1562-1579`** · **`CanAccept` `:794-798` is stale** — it is now `[ObservableProperty] private bool _canAccept;`
> at **`:1096`**, assigned at **`:1579`** · the Single-Entry Account total write `:1289` → **`:1485-1487`** ·
> `SingleEntryAccountTotal` `:1233-1241` → **`:1288-1296`** · the dirty-flag precedent `:449-463` → **`:466-521`**
> (`SyncInvoiceBillWise`, stamps at `:492-512`).
> **Note the D1 dependency named in §1 has changed shape:** D1 is fixed, so the Dr/Cr grid is no longer the screen
> Payment/Receipt/Contra land on. This defect now bites Journal (F7) and the As-Voucher path, not "every payment".

| | |
|---|---|
| **What the operator experiences** | Every two-line Payment, Receipt, Contra and Journal is typed twice — the same figure into the Dr line and the Cr line. A mistyped second figure produces an unbalanced voucher the app then refuses, so the cost of the missing default is a typo *plus* a rejection. On a multi-line Journal the operator does the arithmetic in their head to close the difference **while the screen is already showing it** in `DifferenceText`. |
| **What we do** | `VoucherEntryViewModel.cs:1161-1167` — `AddLine` constructs a `VoucherLineViewModel` and stamps only the voucher date; `AmountText` starts empty and nothing ever fills it with the outstanding balance. `Recalculate` reports the shortfall as text and gates Accept on `IsBalanced` (`:1374-1381`); `CanAccept` (`:794-798`) refuses until the operator types it. The only two places any code writes `AmountText` are the forex base-value path (`VoucherLineViewModel.cs:560,569`) and the Single-Entry Account total (`VoucherEntryViewModel.cs:1289`). Single Entry is correct — `SingleEntryAccountTotal` derives the Account side as Σ of the particulars (`:1233-1241`) — so the gap is specifically **the grid D1 makes the default**, which also covers Journal (F7), where the balancing figure is the whole point of the second line. **[code-verified]** |
| **What Tally does** | On the Dr/Cr / As-Voucher screen the balancing leg's amount is captured the moment the second ledger is selected; the operator overrides it only when splitting. |
| **Citation** | SG p.78 step 4 (Purchase, Voucher Mode) "*…amount will be captured automatically*"; p.83 step 4 (Debit Note); p.84 step 4 (Credit Note); pp.101-102 steps 10-11 (Payment with cost allocation — the operator selects Cash/Bank and step 11 reads simply "Amount will be captured automatically"). All four re-extracted this session at SG text lines 2344, 2416, 2479, 2524, 2955 — **[corpus-verified]**. Also GSTN ~line 327. |
| **Fix** | When a line's ledger is picked and its `AmountText` is still untouched, stamp it with the current unsigned imbalance (Σ opposite side − Σ this side, when positive), marked screen-owned so a later recalculation restamps it and an operator keystroke freezes it. The dirty-flag discipline is already proven twice in this codebase — `VoucherEntryViewModel.cs:449-463` and `InventoryVoucherLineViewModel.cs:296-308` (`ApplyPriceAutoFill` / `_suppressDirty`). Never restamp a value the operator typed. |

---

### D3 · The plain-grid Bill-wise row seeds *nothing* — and its own comment asserts the opposite
**HIGH** · Area **VE-D** · spec C-21, G-15 · *(merged: "wrong defaults" #3 + "fields" #1 + "fields" #3)*

> **† 2026-08-15 — verdict STANDS, and the false comment is still there verbatim.** `VoucherLineViewModel.cs:161-166`,
> `SyncBillWise` `:167-187`, `AddBillAllocation(BillRefType.NewRef)` at `:180`, `AddBillAllocation` `:190-196`,
> `BillAllocationRowViewModel.cs:29-32` and `BillSplitOk` `:228-238` are **all unchanged and exact**, as are
> `MainWindow.axaml:2215-2224`, `:2539-2550` and `:3154`. **Commits `9608567` and `828fc9f` touched only the
> *invoice* path** — `VoucherLineViewModel.AddBillAllocation` still constructs a wholly blank row.
> **Corrected:** the invoice-only auto-stamp `VoucherEntryViewModel.cs:444-463` → **`:466-521`** ·
> `AutoBillDueDateText()` `:356-359` → **`:404-407`** (`:356` is now `private bool _invoiceBillDirty;`).

| | |
|---|---|
| **What the operator experiences** | On every bill-wise Payment, Receipt, Journal or As-Voucher invoice line, the Bill-wise panel opens **completely blank**. The operator retypes the line amount into the bill row (Accept stays greyed until the two match to the paisa), invents a reference name TallyPrime would have filled from the voucher number, and is shown an empty Due Date with no indication that leaving it blank is safe. This is the same complaint that started this review, in the panel that was **not** fixed. It serves both the Dr/Cr grid (`MainWindow.axaml:2215-2224`) and the Single-Entry grid (`:2539-2550`) — i.e. every Payment, Receipt and Contra against a party. |
| **What we do** | `VoucherLineViewModel.cs:161-166` states the seeded row defaults "*its amount + name to the line so the common single-bill case needs no typing*". **It does not.** `SyncBillWise` (`:167-187`) calls `AddBillAllocation(BillRefType.NewRef)` at `:180`; `AddBillAllocation` (`:190-196`) constructs `BillAllocationRowViewModel` whose `_name`, `_dueDateText` and `_amountText` are all `string.Empty` (`BillAllocationRowViewModel.cs:29-32`), and no code path stamps them. `BillSplitOk` (`:228-238`) then returns false while `complete.Count == 0`. Auto-stamping of name, amount and due date exists **only** on the invoice path (`VoucherEntryViewModel.cs:444-463`), gated on `ShowInvoiceOverlay`. The due-date derivation is likewise already written — `AutoBillDueDateText()` (`:356-359`) computes `Date.AddDays(party.DefaultCreditPeriodDays)` — but stamps only into `InvoiceBillAllocations`. **[code-verified]** |
| **What Tally does** | The Amount is filled in for you in **all four** Types of Ref, without exception. New Ref: Name auto-captured from the Supplier Invoice No. (editable), Due Date auto-reflected from the party's credit period, Amount from the Total Invoice Amount. Agst Ref: Due Date inherited, Amount "as per the amount entered earlier". Advance: Due Date "*No need to enter any details*". On Account: the Name **and** Due Date fields are skipped entirely. |
| **Citation** | SG §5.4–5.7 "Maintain Balances Bill by Bill", printed pp.91-94 — the four Type-of-Ref field specs. Re-extracted verbatim this session (SG text lines 2704-2762): "Amount will be captured automatically as per the Total Invoice Amount" / "…as per the amount entered earlier"; "Credit days and Date reflected automatically as per the given credit period specified for the party ledger"; "Select On Account to skip the Name field along with Due Date or Credit Days". **[corpus-verified]** Tabulated in `docs/voucher-entry-specification.md` §4.1. |
| **Fix** | One seeding path for both surfaces. Lift `VoucherEntryViewModel.cs:444-463` (amount from the running total, name from `AutoBillReferenceName()`, due date from `AutoBillDueDateText()`, all under the blank-or-still-ours ownership rule, with the `_invoiceBillDirty` flag so a deliberate split is never clobbered) into `VoucherLineViewModel.SyncBillWise`/`AddBillAllocation`, driven off `ParsedAmount` and the **line's own** ledger rather than `SelectedParty`. Additionally suppress Due Date for `Advance` and `OnAccount` — the invoice panel already disables Name for On Account (`MainWindow.axaml:3154`) but Due Date stays editable in all three panels. **Until it is done, correct the comments at `:161-166`** — they read as a specification of behaviour that does not exist, and they are what let this defect survive a review. |

---

### D4 · ~~Ledger master has no Opening Balance field~~ — **FIXED IN CODE**
**HIGH** · Area **MST** · ✅ **CLOSED 2026-08-15**

> **† 2026-08-15 — FIXED by `c8b44cf` ("the Ledger master captures an opening balance", 2026-08-06). Everything
> below describes the pre-fix state; do not schedule it.** Verified at HEAD:
> - **The UI exists.** `MainWindow.axaml:4356-4387` — an amount `TextBox` bound to `OpeningBalanceText` (`:4371`)
>   plus a Dr/Cr `ComboBox` bound to `OpeningSide` (`:4377-4386`), placed **directly under "Under"**, which is the
>   corpus field order.
> - **It reaches the domain.** `LedgerMasterViewModel.cs:1053-1054` `target.OpeningBalance = opening;` /
>   `target.OpeningIsDebit = OpeningIsDebit;`. The old `:968-970` rationale is inverted at **`:1111-1114`**:
>   *"OpeningBalance / OpeningIsDebit USED to be on this list … It owns them now."* The `Money.Zero` at `:575-580`
>   survives only as a detached placeholder at **`:656-663`**, immediately overwritten.
> - Supporting members: `_openingBalanceText` `:167` · `OpeningIsDebit` `:183-196` · `OpeningSides`/`OpeningSide`
>   `:204-216` · nature-derived side proposal `SetOpeningSideFromNature` `:601-606` · validation for unparseable,
>   negative and sub-paisa input `:864-894`. The read-only list column `MainWindow.axaml:4816` → **`:4852`**.
>
> **⚠️ TWO DELIBERATE DEPARTURES FROM THE FIX PRESCRIBED BELOW — both are live decisions, not oversights:**
> **(a) The write is unconditional, so Alter DOES restate the opening**, where the Fix below asked for it to be
> *read-only in Alter* to preserve the "an Alter must not restate a prior period" rule. **This was a considered
> reversal, not an oversight** — `LedgerMasterViewModel.cs:1043-1051` argues it at length (*"On ALTER this is a
> real restatement of the opening — which is the point. An accountant's first opening is very often wrong and the
> corpus's alteration screen IS the creation screen, pre-filled … It is safe precisely because `LoadFrom` pre-fills
> BOTH halves from the store, so an alter that touches only a name writes the same values back"*), and `:1111-1114`
> records that the two members were moved off the not-written list on purpose. **Recorded here only so the reversal
> is visible to whoever owns the prior-period rule; nothing is claimed to be wrong.**
> **(b) The bill-wise opening breakup sub-screen (SG p.91 step 6) is still absent** — no bill-allocation collection
> exists on `LedgerMasterViewModel`. That half of the row is **still open**.

| | |
|---|---|
| **What the operator experiences** | An accountant migrating a live business cannot enter a single opening balance — no bank balance, no debtor, no creditor, no capital. Every one has to be faked with a hand-written opening journal, which a TallyPrime user would never think to do, because in Tally the field is right there at the bottom of the ledger they are already creating. This is the most-typed field in a real Tally setup session and it does not exist. |
| **What we do** | The Ledger Creation screen captures Name, Under, Currency and the conditional party/GST/TDS/interest blocks — **no Opening Balance input and no Dr/Cr side chooser**. `Create()` hard-codes the opening to `Money.Zero` and derives the side from the group's nature (`LedgerMasterViewModel.cs:575-580`). `TryBuildInto` deliberately never writes `OpeningBalance`/`OpeningIsDebit` (`:968-970`, with the stated rationale that an Alter must not restate a prior period), so Alter cannot set it either. The only "Opening" on screen is a read-only column of the existing-ledgers list (`MainWindow.axaml:4816`). **[code-verified]** |
| **What Tally does** | Opening Balance is a first-class field and the **last** field before Accept: enter it with Dr/Cr per the nature. For a bill-wise party ledger the opening amount additionally opens a Bill-wise breakup window where it is split into references with due dates. |
| **Citation** | SG p.65 "Single Ledger Creation" step 4 **[corpus-verified upstream]**; SG p.91 step 6 (opening balance + bill-wise breakup). WEB `help.tallysolutions.com/ledgers-in-tallyprime/`. |
| **Fix** | Add an Opening Balance amount + Dr/Cr side pair as the last fields of the form, defaulting the side from the group nature exactly as `:575` already computes. Write it in `Create()` only; preserve the existing "Alter must not restate a prior period" rule by leaving it read-only in Alter (or behind an explicit confirm). The bill-wise opening breakup sub-screen (SG p.91 step 6) follows once bill-wise layer-1/2 gating exists. |

---

### D5 · "Agst Ref" bill name is a free TextBox in every panel — Tally makes you pick from the List of Pending Bills
**HIGH** · Area **VE-D** · spec G-15

> **† 2026-08-15 — verdict STANDS; every citation is exact, zero drift.** `MainWindow.axaml:2215-2217`,
> `:2539-2542`, `:3152-3155`, `BillAllocationRowViewModel` `_name` `:30` and `ToAllocation()` `:85-89`, and
> `Outstandings.OpenBillsFor` at `Outstandings.cs:124` are all unchanged. The asymmetry the row calls urgent is
> re-confirmed: the only `OpenBillsFor` consumers are `InterestCalculation.cs:293` and `BillSettlementService.cs:66`
> — **never the in-voucher panel**.

| | |
|---|---|
| **What the operator experiences** | Settling a supplier bill means remembering and retyping the reference string exactly. **One transposed character posts a settlement against a bill that does not exist**: the real bill stays open in Receivables/Payables forever and an orphan negative reference appears beside it, with no error at entry and nothing that ever reconciles it. The operator is doing lookup work the app can already do. |
| **What we do** | All three Bill-wise panels bind Name to a plain `TextBox` — plain Dr/Cr grid `MainWindow.axaml:2215-2217`, Single Entry `:2539-2542`, invoice panel `:3152-3155`. `BillAllocationRowViewModel` exposes only `[ObservableProperty] private string _name` (`:30`) with no candidate list, and `ToAllocation()` (`:85-89`) trims whatever was typed straight into the domain `BillAllocation`. **No validator anywhere checks an `AgstRef` name against a real open bill.** The app already owns the exact list Tally shows — `Outstandings.OpenBillsFor(company, ledger, asOf)` (`src/Apex.Ledger/Reports/Outstandings.cs:124`), the building block the Ctrl+B Outstandings screen binds to. **[code-verified]** |
| **What Tally does** | For Against Reference the field is a **selection**, not free text: "*Name: Select the appropriate Bill from the list of Pending Bills*", and the amount may then be broken up across several of those pending bills. The reference cannot be misspelled because it is never spelled. |
| **Citation** | SG §5.5 "Against Reference", printed p.92 — re-extracted this session (SG text lines 2723-2732), including "You can also break up the amount against different pending bills". **[corpus-verified]** |
| **Fix** | Replace the Name TextBox with a ComboBox whose `ItemsSource` is `Outstandings.OpenBillsFor(company, line.SelectedLedger, voucherDate)` whenever `RefType == AgstRef`, and stamp the picked bill's pending amount into `AmountText`. Keep the free TextBox for `NewRef`/`Advance`, where a new string is legitimately being authored. Note the asymmetry that makes this urgent: `BillSettlementService.BuildSettlementAllocations` **already** validates references against genuinely open bills and caps each knock at the pending amount — the in-voucher panel is the only path that skips the check. |

---

### D6 · Stock Item master is unusable on a fresh company: "Under" has no Primary and "Units" has no Not Applicable
**HIGH** · Area **MST**

> **† 2026-08-15 — verdict STANDS.** `StockItemMasterViewModel.cs:365` (`CanCreate`), `:410-414` and `:415-419`
> are unchanged and exact. **Corrected:** the `(none)` Category sentinel `:764` → **`:765`** · `RefreshPickers`
> is **`:755-787`** and still adds **no** sentinel to `Groups` (`:758-760`) or `Units` (`:772-774`) ·
> `CompanyFactory.cs:30-53` → the seed block is **`:25-52`**, and it still seeds no stock groups and no units.

| | |
|---|---|
| **What the operator experiences** | On a new company the operator opens Create > Stock Item, types the item name, and is blocked with "create a Stock Group first" — a screen TallyPrime lets them complete in four keystrokes. It also permanently forbids the **service / unmeasured item** (Units = Not Applicable) that Tally users create routinely for consultancy and freight lines. |
| **What we do** | `CanCreate => Groups.Count > 0 && Units.Count > 0` (`StockItemMasterViewModel.cs:365`) disables creation entirely until both exist. `SaveMaster` rejects a blank group with "Pick a stock group to place the item under (create a Stock Group first)" (`:410-414`) and a blank unit with "Pick a base unit for the item (create a Unit first)" (`:415-419`). `RefreshPickers` fills Groups from `_company.StockGroups` and Units from `_company.Units` with **no sentinel option** — the Category picker gets a `(none)` sentinel (`:764`) but Under and Base unit do not. `CompanyFactory` seeds no stock groups and no units (`src/Apex.Ledger/Services/CompanyFactory.cs:30-53` seeds groups, ledgers, voucher types, one cost category, base currency and Main Location only). **[code-verified]** |
| **What Tally does** | Creation is Name → Under → Category → Units → Statutory Details → Opening Balance, and "*Under: Select **Primary** or an existing Stock Group from the list*". For Units, "*By default, **Not Applicable** appears in this field*" — so a brand-new company can create a stock item immediately with no prior masters at all. |
| **Citation** | SG p.115 "Single Stock Item Creation" steps 1-6; BOOK p.23 (the Not-Applicable default). **[corpus-verified upstream]** |
| **Fix** | Add a "◦ Primary (top-level)" sentinel to the Under picker — mirroring `ParentStockGroupOption` / `ParentGodownOption`, which already do exactly this — and make `StockItem.StockGroupId` nullable-or-Primary. Add a "Not Applicable" sentinel to Units and allow a null `BaseUnitId`, with quantity columns suppressed for such an item. Then drop the `CanCreate` gate. |

---

### D7 · ~~Negative stock is an unconditional hard block across the whole company timeline~~ — **FIXED IN CODE**
**HIGH** · Area **VE-V** · spec C-44, G-8, **U-1** · ✅ **CLOSED 2026-08-15 for the block; the WARN half has no surface**

> **† 2026-08-15 — THIS ROW READ THE OPPOSITE OF THE CODE. FIXED by `a12e651` ("negative stock warns instead of
> blocking", schema **v50**, **2026-08-06 09:52:01 +0530 — 13 minutes 46 seconds BEFORE this register was
> written**). Everything below describes the pre-fix state.** Verified at HEAD `c56e5c3`:
>
> **‡ CHRONOLOGY CORRECTED 2026-08-15 (W0-16).** The line above previously read *"2026-08-06 — two days after this
> register was written"*. `git log` says the opposite, and the inversion is the whole point of the row: `18bf524`
> — the single commit that CREATED this register and `docs/invented-vs-cloned.md`, and the only commit adding
> either file across all refs — is **2026-08-06 10:05:47 +0530**, with `a12e651` (**09:52:01**) and `f277318`
> (**09:52:00**) already its ancestors. **The registers were written under fourteen minutes after the fixes, on a
> tree that already contained them, describing them as open** — and the false "two days after" was introduced by
> `7ae0894`, the W0-14 pass whose entire purpose was correcting these registers. A documentation-only pass, done
> carefully, inverted a fact one `git log` settles; that is exactly what a check catches and a reviewer
> demonstrably did not.
> - **The throw is gone.** `EnsureNoNegativeStockAnywhere` and the message *"Negative stock is not allowed."*
>   (`:400`) do not exist anywhere in `src/`. The guard became a detector: **`InventoryPostingService.cs:176`**
>   `public IReadOnlyList<NegativeStockShortfall> DetectNegativeStock() => DetectNegativeStockAnywhere();`
>   (`:394-442`, never throws, never mutates).
> - **The company flag exists.** `Company.cs:268` `public bool WarnOnNegativeStock { get; set; } = true;`,
>   consulted at exactly one place — **`InventoryPostingService.cs:184-185`** `NegativeStockWarnings()`.
> - **All four call sites are un-blocked.** `LedgerService.cs:55-60` now reads *"Negative stock is no longer
>   blocked anywhere, so the append simply stands"*; `Cancel` `:88-97`, `Delete` `:99-108`. The class doc at
>   `InventoryPostingService.cs:37-44` states *"⚠️ Negative stock is NOT blocked (plan.md NS-3; changed at v50)."*
> - **The "structurally incapable of ever showing a row" sub-claim is now FALSE.** `ReportsViewModel.cs:2656`,
>   `:2675` and `MainWindowViewModel.cs:1654` are unchanged and exact, but `NegativeStock.Build`
>   (`src/Apex.Ledger/Reports/NegativeStock.cs:44`) can now return rows because negative on-hand is postable —
>   asserted by `tests/Apex.Ledger.Tests/Inventory/NegativeStockPolicyTests.cs:106, 144, 176`.
>
> 🔴 **TWO HALVES REMAIN OPEN, and both are CODE defects, not documentation ones:**
> **(a) `NegativeStockWarnings()` has NO production caller** — its only reference outside its own definition is
> `NegativeStockPolicyTests.cs:331`. So "warn-only" currently warns nobody: there is no Accept-time advisory.
> **(b) There is no control surface** — `grep WarnOnNegativeStock src/Apex.Desktop` returns **zero hits**, so a
> company that wants the block back cannot ask for it. Tracked as W0-5 in `docs/NEXT_SESSION_KICKOFF.md`.
> **The Fix cell's caution below is still correct about the VALUATION half (NS-8), which is untouched.** Note the
> attempt count has risen: `docs/NEXT_SESSION_KICKOFF.md:113-146` records **eight** reverted attempts, not three,
> and a sourcing pass now argues the fix is to **delete** the invented repayment model rather than write a ninth
> — TallyPrime's Average Cost has no repayment model, and its own oracle `RunAverageDebtAware` implements the
> refuted one, so **30 of 36 AverageCost goldens must be re-derived from the formula, never edited to match code.**

| | |
|---|---|
| **What the operator experiences** | The single most common real-world data-entry sequence in Indian trading — deliver/invoice the goods today, book the supplier's purchase bill when it arrives next week — is **impossible**. The operator is stopped dead at Accept with "Negative stock is not allowed" and loses the keyed invoice. Worse: because the guard rescans the whole timeline on every post, one such situation can make the **entire company unpostable**, not just fail the one voucher. And our own Negative Stock exception report can never show a row, so the operator has a permanently dead menu item. |
| **What we do** | `EnsureNoNegativeStockAnywhere()` throws whenever any (item, godown, batch) key goes below zero at any date (`src/Apex.Ledger/Services/InventoryPostingService.cs:400`, guard `:348-405`). **No company flag, no item flag, no voucher-type flag, no F12 option relaxes it** — nothing in `Company.cs`, `VoucherType.cs` or `VoucherValidator.cs` reads such a switch. Invoked on every accounting post (`LedgerService.cs:60`), every inventory post (`:81`), every Cancel (`:101`) and every Delete (`:117`), rescanning the entire company across every affected key × date. Internal contradiction: we ship a Negative Stock exception report (`ReportsViewModel.cs:2656` `BuildNegativeStock`, menu at `MainWindowViewModel.cs:1654`) whose empty-state row reads "No negative stock as at this date." (`ReportsViewModel.cs:2675`) — structurally incapable of ever showing a row. **[code-verified]** |
| **What Tally does** | Exposes `Warn on negative Stock Balance` as a per-voucher-screen F12 option; when it fires it shows the shortfall and lets the operator continue — the voucher is still accepted, an alerting feature rather than a hard block. That TallyPrime *ships* a Negative Stock exception report is itself evidence that negative on-hand is a state it permits and then surfaces. |
| **Citation** | **[web] only.** `help.tallysolutions.com` Sales FAQ (`/tally-prime/sales-process/faq-sales/`) and the ERP-9-era `Configuring_Warning_Message_for_Negative_Stock_Balance.htm`. **The licensed corpus is completely silent: I re-ran the grep for "negative stock" / "allow negative" across all ten PDFs this session and got 0 hits in every one** — spec §8 **U-1** stands. **[corpus-verified: silence]** |
| **Fix** | ⚠️ **Do not build on the strength of this web citation alone.** Project memory (`negative-stock-valuation-open-problem.md`) records **three prior attempts, three NOT-READY reviews, three different unbounded Balance-Sheet errors that each passed the full suite**, all reverted. The recorded next step is an **oracle-harness-first** approach, and U-1 should be settled by observing real TallyPrime with the F12 option both ways *before* any build. The shape of the eventual fix: add the voucher-screen F12 `Warn on negative Stock Balance` (default Yes per C-44) plus a company-level allow flag, and have `EnsureNoNegativeStockAnywhere` **return** the offending keys rather than throw, so Accept can present them and let the operator proceed. |

---

### D8 · An unresolved GST rate / HSN-SAC hard-blocks Accept — TallyPrime records the voucher and lists it in GSTR-1 exceptions
**HIGH** · Area **VE-V**

> **† 2026-08-15 — verdict STANDS; the block is intact and part 2 of the Fix has NOT landed.** A repo-wide
> `grep -i "Uncertain|Incomplete/Mismatch"` across `src/` returns **zero hits** — there is no GSTR-1 exception
> surface, so the Fix's ordering constraint ("part 2 must land first") is still binding.
> **Corrected:** the item-invoice refusal `:4476-4481` → **`:4690-4695`** · the accounting-invoice mirror
> `:3868-3872` → **`:4082-4087`** · the `hasUnresolved` conjunct `:3736-3741` → **`:3950`** (declaration) and
> **`:3955`** (`&& !hasUnresolved`).

| | |
|---|---|
| **What the operator experiences** | Data entry stops for a **master-data** problem. Keying a stack of invoices, the operator hits an item whose HSN/tax rate was never set up and **cannot save the voucher at all** — abandon it, leave the voucher screen, alter the stock item or ledger master, come back, re-key everything. In TallyPrime the same invoice saves and the gap is cleaned up in bulk from the GSTR-1 exception report at return time, which is how practices actually work. This is the classic clone-feels-wrong divergence: the field is present, the rigidity is inverted. |
| **What we do** | Item invoice: `AcceptItemInvoice` refuses when any taxable item has no resolvable rate — "Item 'X' is taxable but no GST rate is set on the item, the Sales/Purchase ledger, or the company." (`VoucherEntryViewModel.cs:4476-4481`). Accounting invoice: the mirror refusal on the ledger (`:3868-3872`), plus `hasUnresolved` as a hard `CanAccept` conjunct (`:3736-3741`). Neither path offers an accept-as-is escape, and nothing records the voucher in an incomplete state for later repair. The comment at `:3736` states the intent as "no silent ₹0" — a defensible goal, implemented as an **entry-time block** rather than a **return-time exception**. **[code-verified]** |
| **What Tally does** | Accepts the voucher and routes it to `GSTR-1 > Uncertain Transactions > Number of Voucher with Incomplete/Mismatch in Information`, where item-master exceptions are resolved by updating HSN/SAC and tax rate, and ledger exceptions by correcting HSN/SAC, rates and taxability. The operator drills down and either repairs the master, presses **Alt+W** (Recompute), or presses **Alt+J** (Accept as is) to include the transaction regardless. |
| **Citation** | BOOK printed p.194 — the GSTR-1 walkthrough instructs "Press Enter on 'Uncertain Transaction' then Press Enter on 'Number of Voucher with Incomplete/Mismatch in Information'" then Alt+V. Re-extracted this session (BOOK text lines 6922-6923, repeated at 8927-8928). **[corpus-verified]** Plus **[web]** `help.tallysolutions.com` GSTR-1 incomplete/mismatch page for the Alt+J / Alt+W gestures. |
| **Fix** | Two parts, and **part 2 must land first** or this trades a block for a silent data hole. (1) Build the GSTR-1 Uncertain Transactions / Incomplete-Mismatch exception surface — a report finding posted vouchers whose taxable lines have no resolvable rate/HSN/SAC, with drill-down to the master and Recompute / Accept-as-is actions. (2) *Only then* downgrade the two Accept refusals (`:4476`, `:3868`) and the `hasUnresolved` conjunct (`:3736`) to a non-blocking on-screen advisory, posting at ₹0 tax and letting the report carry it. This is a `plan.md`-scale item, not an inline change — raise it at a phase gate under **R12**. |

---

### D9 · There is no Voucher Type master at all — `Create > Voucher Type` does not exist
**HIGH** · Area **MST/CFG** · spec C-16, C-17, G-4 · **† the G-4 half is FIXED; the master is still absent**

> **† 2026-08-15 — the MAIN verdict STANDS; the G-4 sub-claim is now FALSE.**
> **Still true:** `BuildCreateColumn()` (now `:1180-1257`, one row added) has **no "Voucher Type" entry**; no
> `VoucherTypeMasterViewModel` file exists; `VoucherNumberingConfigViewModel.cs:114-115` still holds the numbering
> Method **display-only**; and `VoucherType.cs` (now 281 lines) still carries every flag unreachable from the UI —
> `UseAsManufacturingJournal` `:62`, `TrackAdditionalCosts` `:77`, `AllowZeroValuedTransactions` `:89`,
> `UseForPos` `:100`, `PosConfig` `:105`, `IsStatPayment` `:140`, `IsRcmPaymentVoucher` `:153`,
> `IsGstStatAdjustment` `:167`.
> **🔴 NOW FALSE — the G-4 resolver fallback is GONE.** `MainWindowViewModel.cs:2756-2757` is Export/Import code
> at HEAD. Commit `7bfc2c6` replaced all five routes with `VoucherTypeResolver.ResolveForEntry` (call sites
> `:2892`, `:2948`, `:3510`, `:3538`, `:5083`), and `src/Apex.Ledger/Services/VoucherTypeResolver.cs:58` reads
> `if (type.BaseType != baseType || !type.IsActive) continue;` — **an inactive type is never returned, so
> `IsActive` is load-bearing, not decorative.** The Fix's closing instruction *"Remove the G-4 resolver fallback
> in the same change"* is therefore **already done**.
> **What survives of the sub-claim:** a **second** Sales series is still unreachable by a base-kind route, because
> `VoucherTypeResolver.cs:60` reads `if (type.IsPredefined) return type;   // the seeded series wins outright`.
> Cross-check: `docs/full-clone-census.md` T1-4 reaches the same conclusion from the opposite direction — Payroll
> ships `IsActive = false` and now genuinely **cannot post**, which is only possible because the fallback is gone.

| | |
|---|---|
| **What the operator experiences** | A Tally user cannot do the very first thing they do on a new company: split Sales into "Cash Sale" and "Credit Sale", give each an abbreviation and its own numbering series. They also cannot activate an inactive type, cannot create the POS invoice type the book walks through, and cannot create the Manufacturing Journal type the manufacturing chapter requires — **even though the engine supports all three**. |
| **What we do** | `BuildCreateColumn()` (`MainWindowViewModel.cs:1180-1256`) lists Ledger, Group, Cost Category/Centre, Stock Group/Category/Unit/Godown/Item, Reorder Levels, Batch, BOM, Price Level/List, Budget, Scenario, Currency, Nature of Payment/Goods and the payroll masters — **no "Voucher Type" entry**, and no `VoucherTypeMasterViewModel` exists. The only reachable voucher-type editing is the F12 numbering panel (`VoucherNumberingConfigViewModel.cs`), which edits Prevent-duplicate / Width / Prefill / date-keyed prefix+suffix for an existing type and holds the numbering Method **display-only** (`:114`). Meanwhile `src/Apex.Ledger/Domain/VoucherType.cs:16-206` carries Name, BaseType, Abbreviation, IsActive, UseAsManufacturingJournal, UseForPos, PosConfig, UseForJobWork, TrackAdditionalCosts, AllowZeroValuedTransactions, IsStatPayment, IsRcmPaymentVoucher, IsGstStatAdjustment — **every one unreachable from the UI**. Compounded by spec **G-4**: `MainWindowViewModel.cs:2756-2757` resolves `FirstOrDefault(BaseType == …)`, so a second Sales type is unreachable and `IsActive` is decorative. **[code-verified]** |
| **What Tally does** | `Gateway > Create > Voucher Type` is a core master. Minimal screen is Name + "Select type of voucher"; the walked examples add Method of voucher numbering, Print voucher after saving, Use for POS invoicing, Message to Print (1)/(2), Default title to print, Set/alter declaration, Use as a Manufacturing Journal. Alteration is `Gateway > Alter > Voucher Type`; Alt+D deletes. |
| **Citation** | BOOK pp.17-18 (`GOT > Create > Voucher Type`, with the Cash Sale / Credit Sale / Credit Purchase practice table); BOOK p.140 (Manufacturing Journal type); BOOK pp.153-154 (POS INVOICE type, full field list). **[corpus-verified upstream]** |
| **Fix** | Add a Voucher Type master (Create + Alter) in the corpus field order: Name, alias, Select type of voucher, Abbreviation, Activate this Voucher Type, Method of voucher numbering, Use effective dates, Make 'Optional' as default, Allow narration, Print voucher after saving, plus base-type-conditional flags (Use for POS invoicing → the POS message/title/declaration block for Sales; Use as Manufacturing Journal for Stock Journal; Track additional costs for Purchase). Fold `VoucherNumberingConfigViewModel` in as the numbering section rather than keeping it a separate F12-only screen. **Remove the G-4 resolver fallback in the same change**, or activation still means nothing. |

---

### D10 · No master-screen F12 — layer 2 of the four-layer gate does not exist
**MEDIUM (structural)** · Area **CFG** · spec §1.1 layer 2, G-14

> **† 2026-08-15 — verdict STANDS; citation drift only.** **Corrected:** `F12Configure()` `:6628-6652` →
> **`:6669-6694`** · the LedgerMaster arm `:6637-6641` → **`:6678-6682`** · the fall-through `:6652` → **`:6693`**
> (`Message = "F12 Configure — display options (Phase 1 defaults).";`) · `LedgerMasterViewModel.cs:243` (the one
> revealed field) → **`:298`**, gated by `ShowAppropriation` at **`:310`**.
> Corroborated independently by `docs/full-clone-census.md` T1-16, which cites the same fall-through.

| | |
|---|---|
| **What the operator experiences** | A Tally user's reflex when a field is missing is **F12 on the screen they are standing on**. Here that reflex produces a status-line message and nothing else, on every master but one. The reverse bites too: fields Tally keeps hidden until asked for (the whole Employee general/statutory/passport/contract block, the batch switches) are always on screen, so the master looks cluttered and unfamiliar. |
| **What we do** | `F12Configure()` (`MainWindowViewModel.cs:6628-6652`) handles three contexts only: the numbering config; the Ledger master's single `ShowConfiguration` boolean (`:6637-6641`, which reveals **exactly one field** — Method of Appropriation, `LedgerMasterViewModel.cs:243`); and the voucher-numbering config on a voucher screen. Every other master falls through to `Message = "F12 Configure — display options (Phase 1 defaults)."` (`:6652` **[code-verified]**). So on Stock Item, Stock Group, Godown, Unit, Group, Cost Category, Cost Centre, Employee and Employee Group, **F12 does nothing**, and every conditional field keys off layer 1 (a company flag) or layer 3 (the master's own value) alone. |
| **What Tally does** | Every master screen has its own F12 deciding which fields render on *that* master, independently of the F11 capability. The corpus states this trap by name at least four times. |
| **Citation** | SG p.91 step 3 (ledger: "*If the option is not appear, press F12 (Configure) and 'Maintain balance Bill-by-Bill' set to Yes*"); BOOK p.130 (stock item: "*If you don't see Manufacturing & Expiry Option … then Press F12 & Enable Batches*"); BOOK p.325 (employee group); BOOK p.326 (employee). **[corpus-verified upstream for the first two]** Also `docs/voucher-entry-specification.md` §1.1 layer 2 — "*we have no layer-2 concept at all*". |
| **Fix** | Give each master view model its own F12 configuration object (a small set of per-screen booleans persisted on the company) and route `F12Configure` to the **live master screen's** toggle rather than to a hard-coded LedgerMaster arm. Start with the four the corpus names by title: ledger "Maintain balance Bill-by-Bill", stock item "Maintain Stock Item in Batches", employee-group "show more", employee "show more details". |

---

### D11 · No Order No. / Tracking No. field anywhere — every Receipt Note, Delivery Note and invoice line is re-keyed by hand
**HIGH** · Area **VE-D** · spec G-11, §4.5

> **† 2026-08-15 — the re-keying defect STANDS; one sub-claim is now OVERSTATED.**
> **Still true:** `InventoryVoucherEntryViewModel`'s header observables (now **`:107-139`** — `_title` `:107`,
> `_date` `:108`, `_voucherNumber` `:109`, `_narration` `:110`, `_selectedParty` `:111`, `_isPostDated` `:124`,
> `_message` `:127`) contain **no order or tracking field of any kind**; repo-wide `OrderNo`/`TrackingNumber`
> outside Job Work is still nothing. Corroborated by `docs/full-clone-census.md` T1-8: *"Zero `TrackingNumber`
> hits in `src/`."*
> **🔴 Now overstated:** *"order-fulfilment tracking … cannot exist"*. Commit `1407978` added
> `src/Apex.Ledger/Reports/OrderFulfilment.cs` and widened the party picker to the four movement notes, so
> fulfilment tracking **does** exist — but it is derived by `(PartyId, StockItemId)` attribution rather than by an
> order link, and that commit ships **four known-wrong residuals (R1–R4), three of which UNDER-state shortfall**.
> So the capability is present and unreliable, which is a different (and more dangerous) claim than absent.
> **Corrected:** `MaterialMovementEntryViewModel.AutoFillFromOrder` `:222-260` → declaration at **`:238`**.
> `:188` and `ReportsViewModel.cs:1652` are unchanged and exact.

| | |
|---|---|
| **What the operator experiences** | An operator who has already entered a Purchase Order **retypes the entire order** — item, location, quantity, rate — into the GRN, and then a **third** time into the Purchase invoice. Beyond the keystrokes, nothing links the three documents, so order-fulfilment tracking (ordered-not-received, received-not-billed) cannot exist, and any divergence between the three re-keyings is invisible. |
| **What we do** | `InventoryVoucherEntryViewModel` has **no order or tracking field at all**: its header observables are Date, VoucherNumber, Narration, SelectedParty, IsPostDated, Message (`:103-137`). A repo-wide search for `OrderNo`/`TrackingNumber` outside the Job Work path returns nothing — the only hits are `JobWorkOrderEntryViewModel.cs`, `MaterialMovementEntryViewModel.cs:188` and `ReportsViewModel.cs:1652` **[code-verified]**. So on a Receipt Note the operator picks item, godown, quantity and rate — all four — for every line, even when the matching PO is already in the company. **The mechanism is proven in this codebase and wired only to Job Work:** `MaterialMovementEntryViewModel.AutoFillFromOrder` (`:222-260`) populates both grids from the picked order, item, godown, quantity and rate included. |
| **What Tally does** | Receipt Note: "*Order No — Select your Purchase Order Number from list … All the details from the purchase order is imported into GRN voucher*", with Location, Quantity and Rate each reading "Auto fill", and the explicit corollary "*If you don't have any Purchase Order then Manually Type all Details in GRN*". Delivery Note: "*Order No(s) — Select your Sale Order from list — After Selection All Details Automatic Fill*", where even Name of Item reads "Auto fill". The Purchase invoice pulls the same way from the goods-receipt side: "*Provide Receipt Note No (Select from the List of Tracking Numbers)*". |
| **Citation** | BOOK pp.70-71 (Receipt Note, Item Invoice mode) and pp.77-78 (Delivery Note, Item Invoice mode); SG p.118 step 3 (Purchase Bill or Invoice). **[corpus-verified upstream]** |
| **Fix** | Add an Order No. picker to the Receipt Note / Delivery Note header, sourced from open PO/SO inventory vouchers for the selected party, and an equivalent Tracking No. picker to the Purchase/Sales item invoice. On selection, populate the line grid exactly as `AutoFillFromOrder` already does — the row-stamping code, the clear-on-deselect behaviour and the "the user may still edit any line" contract are all written and tested there. |

---

### D12 · Batch sub-fields are independent switches instead of being nested under "Maintain in Batches"
**HIGH** · Area **MST**

> **† 2026-08-15 — verdict STANDS; `MainWindow.axaml` drifted ≈ +39 lines in this region.** The XAML comment the
> Fix asks to delete is still there verbatim. **Corrected:** the comment `:6424-6425` → **`:6462-6464`** ·
> `IsVisible="{Binding ShowBatchSwitches}"` `:6428` → **`:6467`** · "Maintain in Batches" `:6432` →
> **`:6471-6472`** · "Track date of Manufacturing" `:6434` → **`:6473-6474`** · "Use Expiry dates" `:6436` →
> **`:6475-6476`**. Still **no `IsVisible`/`IsEnabled` dependency between the three**.
> `StockItemMasterViewModel.cs:165` is unchanged and exact.

| | |
|---|---|
| **What the operator experiences** | The operator can save a stock item with **Use Expiry dates = Yes while Maintain in Batches = No** — a master TallyPrime cannot produce. Nothing then asks for an expiry date at entry (there is no batch sub-screen for a non-batch item), so the item **silently claims shelf-life tracking it does not have**. A pharmacy user who has always seen the two sub-fields appear only after answering Yes will read the always-on checkboxes as a different product. |
| **What we do** | All three checkboxes sit in one panel gated only on the company flag: `IsVisible="{Binding ShowBatchSwitches}"` (`MainWindow.axaml:6428`) wrapping "Maintain in Batches" (`:6432`), "Track date of Manufacturing" (`:6434`) and "Use Expiry dates" (`:6436`), with **no IsVisible/IsEnabled dependency between them**. The XAML comment states the intent explicitly — "*The three switches are independent (Use-Expiry may be on without Track-Mfg)*" (`:6424-6425`). `ShowBatchSwitches => _company.MaintainBatchwiseDetails` (`StockItemMasterViewModel.cs:165`) is a single company flag; there is no separate "Set expiry dates for batches" sub-flag and no item-screen F12 gate. **[code-verified]** |
| **What Tally does** | "*Track Date of manufacturing gets enabled only when Maintain in Batches is set to Yes.*" "*Use expiry dates gets enabled only after enabling Set expiry dates for batches under Maintain batch-wise details option in Inventory Features (F11).*" The item-screen gate is F12 → "Maintain Stock Item in Batches". The corpus walks the same order and adds the F12 note. |
| **Citation** | **[web]** `help.tallysolutions.com/manage-inventory-batch-wise-tally/` for both dependency sentences and the F12 option name; BOOK pp.129-130 for the field order and the F12 note **[corpus-verified upstream]**. ⚠️ Note the corpus defect recorded as spec **U-12**: BOOK p.129, in the section titled "Activation of Batch-wise details", tells the reader to activate "Enable Goods & Services Tax (GST)" — plainly a copy-paste error; p.130's own F12 note says Enable Batches. Do not let it propagate. |
| **Fix** | Bind the two sub-field checkboxes' `IsVisible` to `MaintainInBatches`, and add the F11 sub-flag "Set expiry dates for batches" (under Maintain batch-wise details) as the extra gate on Use Expiry dates. Also force `TrackManufacturingDate`/`UseExpiryDates` to false on save whenever `MaintainInBatches` is false, so an item flipped back cannot keep an orphaned flag. Delete the XAML comment at `:6424-6425` — like D3's, it documents the defect as if it were the design. |

---

### D13 · The voucher ledger picker is unfiltered for every voucher type — a Journal can post to Cash, a Contra to Sales
**MEDIUM** · Area **VE-V** · spec C-47, G-12

> **† 2026-08-15 — verdict STANDS, and this row's own drift-correction has itself drifted.** The row notes the
> spec said `:630` and corrects it to `:1076`. **It is now `:1124`** — `Ledgers = company.Ledgers;`, still the
> sole assignment, still one unfiltered collection handed unchanged to every `VoucherLineViewModel` (`:1218`),
> with no per-type restriction anywhere. **A register line that exists to fix a stale citation went stale in
> eleven days; that is the argument for the doc-vs-code CI check already on the kickoff queue.**

| | |
|---|---|
| **What the operator experiences** | Nothing is ever *unbalanced*, so nothing complains — but the books can be silently mis-shaped. A Contra (which by definition moves funds between cash and bank only) can touch a P&L ledger; a Journal can post to Cash where TallyPrime would not offer it. The operator gets no signal at entry and no signal afterwards. |
| **What we do** | `Ledgers = company.Ledgers` (`VoucherEntryViewModel.cs:1076` **[code-verified]** — note the line has drifted from the `:630` recorded in the spec). One unfiltered collection serves every voucher type; no per-type restriction exists anywhere. |
| **What Tally does** | Voucher-screen F12 "**Allow cash accounts in journal vouchers**" is **off by default**, so cash/bank ledgers are not selectable in a Journal until the operator asks for them. Contra is restricted to cash/bank movement by the type's own definition. |
| **Citation** | Spec C-47 (**[inherited]**, WEB-sourced); BOOK p.25 for the Contra definition — internal movement of funds only, no business effect **[corpus-verified upstream, via BOOK TOC]**. |
| **Fix** | Filter the picker per base type at `:1076`, and put the Journal cash restriction behind the F12 flag rather than hard-coding it — which requires the voucher-screen F12 layer (D10's voucher-side twin, spec G-14). Ship the filter as an advisory-with-override if the F12 layer is not yet available; do not make it a silent hard restriction the operator cannot lift, because TallyPrime's is liftable. |

---

### D14 · A free-goods item line requires a Rate to be typed — TallyPrime tells the operator to leave Rate and Amount blank
**HIGH** · Area **VE-V**

> **† 2026-08-15 — verdict STANDS; `VoucherEntryViewModel.cs` drifted ~+215 lines past `:4200`.**
> `InventoryVoucherLineViewModel.cs:333` is unchanged. **Corrected:** `everyLineRateOk` `:4218-4220` →
> **`:4432-4434`** (content identical — `allowZero` still only widens `r > 0` to `r >= 0` and **never admits
> `ParsedRate is null`**) · the `CanAccept` conjunct `:4227` → **`:4441`** · the `AcceptItemInvoice` refusal
> `:4398` → **`:4612-4615`**, error message unchanged.

| | |
|---|---|
| **What the operator experiences** | A buy-one-get-one or free-sample line **cannot be keyed the way the reference product teaches it**. The operator tabs past Rate as documented, Accept greys out with no visible reason on the line, and Ctrl+A produces an error naming a config flag **that does not fix it**. They must discover on their own that typing an explicit "0" is required — and typing 0 changes the printed invoice, which in TallyPrime shows a blank Rate/Amount cell for a free item, not "0.00". |
| **What we do** | A line with a blank Rate has `ParsedRate == null` (`InventoryVoucherLineViewModel.cs:333`). `RecalculateItemInvoice` computes `everyLineRateOk` as `l.ParsedRate is { } r && (r > 0m \|\| (allowZero && r >= 0m))` (`VoucherEntryViewModel.cs:4218-4220`) — **false for a null rate** — so `CanAccept` goes false (`:4227`). `AcceptItemInvoice` refuses the same line at `:4398` with "…needs a rate greater than zero (enable 'Allow zero-valued transactions' to enter a free-goods line at ₹0)." **Critically the `allowZero` relaxation only admits a rate of literal 0** — a *blank* rate is rejected even when the voucher type has `AllowZeroValuedTransactions = Yes`, so the error message points the operator at a setting that will not by itself unblock them. **[code-verified]** |
| **What Tally does** | For free items: set `Allow zero-valued transactions` to Yes on the voucher type, then "*enter the Quantity and keep the Rate and Amount fields blank*". The blank Rate/Amount is the **documented entry gesture**, not an incomplete row; the printed invoice then shows billed and free items with their respective quantities. |
| **Citation** | **[web]** `help.tallysolutions.com` — "How to Record Sales Receipts and Sales with Additional Charges, Discounts and Free Items"; and "Using Zero Valued Entries", which states zero-valued entries are allowed only in Sales and Purchase vouchers. **Corpus: silent on free-goods entry mechanics.** |
| **Fix** | Treat a blank Rate on a zero-valued-enabled type as an explicit ₹0 rather than missing data. In `InventoryVoucherLineViewModel`, expose the distinction (blank vs typed-zero) so the print projector can render an empty cell, and relax `everyLineRateOk` (`:4218-4220`) and the Accept guard (`:4398`) to accept `ParsedRate is null` when `_type.AllowZeroValuedTransactions` is on. Keep the current rejection when the flag is off, so an ordinary invoice still catches a fat-fingered blank rate. |

---

### D15 · A wholly zero-valued invoice enables Accept and is then rejected by the engine with an unrelated error
**HIGH** · Area **VE-V** · corrects spec C-19 (recorded MATCH — true only for a *mixed* invoice)

> **† 2026-08-15 — verdict STANDS; the one-character relaxation the Fix asks for has NOT landed.**
> `VoucherValidator.cs:89` still reads `if (line.Amount.Amount <= 0m)` with **no `AllowZeroValuedTransactions`
> conjunct**, so the ₹0 legs still die there after Accept has already lit up. **Corrected:**
> the `total > 0m || allowZero` gate `:4229` → **`:4443`** · the derived legs `:4521-4526` → **`:4735-4740`** ·
> `VoucherValidator.cs:84-85` → **`:89-90`** · the surfaced message `:4565` → **`:4781`** · the supporting
> citations in the Fix: `voucherType` fetched `:39` → **`:44`**, the Sales/Purchase restriction `:71-75` →
> **`:76-80`**, the per-line item guard `:241` → **`:267`**.

| | |
|---|---|
| **What the operator experiences** | The free-samples dispatch — the headline use case the flag exists for — cannot be recorded. And the failure mode is the one that erodes trust fastest: **the Accept button is enabled**, the operator presses it, and the app rejects the voucher with an error about entry-line amounts that names neither the feature nor anything on screen. The keyed lines are lost. |
| **What we do** | When every item line is free, `RecalculateItemInvoice` deliberately permits a ₹0 total — `&& (total > 0m \|\| allowZero)` (`VoucherEntryViewModel.cs:4229` **[code-verified]**) — so Accept lights up. `AcceptItemInvoice` then derives the two accounting legs from that total (`:4521-4526`, both ₹0) and posts. `VoucherValidator.EnsureValid` throws on the first line: `if (line.Amount.Amount <= 0m) throw new InvalidVoucherException("Every entry line amount must be > 0.")` (`VoucherValidator.cs:84-85` **[code-verified]**). `EntryLine` has no zero guard of its own, so the ₹0 legs construct happily and die only at the validator. Surfaced at `:4565` as "Cannot accept: Every entry line amount must be > 0." **The `AllowZeroValuedTransactions` relaxation was threaded through the per-line item guard (`VoucherValidator.cs:241`) and the UI gate, but never through the §6.3 line-amount invariant the derived legs hit.** |
| **What Tally does** | Supports an invoice consisting **solely** of zero-valued items — the stated use case being issue of free or returnable samples where no money is involved but inventory must still be updated. That is precisely a voucher whose accounting legs are ₹0 while stock moves. |
| **Citation** | **[web]** `help.tallysolutions.com` — "Using Zero Valued Entries"; "How to Record Purchase of Goods and Services in TallyPrime". Corpus grounds `AllowZeroValuedTransactions` as a Sales/Purchase type flag (spec C-19) but **no corpus page walks a wholly-₹0 invoice**. |
| **Fix** | Thread the flag through the §6.3 invariant: in `EnsureValid`, when the resolved `voucherType.AllowZeroValuedTransactions` is on (already fetched at `:39`, already restricted to Sales/Purchase at `:71-75`), relax the line guard at `:84` from `<= 0m` to `< 0m`. The balance invariant (Σ Dr == Σ Cr) and the item-invoice pairing invariant both hold trivially at ₹0, so nothing else changes. Add regression fixtures for a wholly-free Sales **and** a wholly-free Purchase — the existing zero-valued tests evidently cover only the mixed case. |

---

### D16 · On a Credit Note / Debit Note the Rate is typed — and there is no item-invoice mode to type it into
**MEDIUM** · Area **VE-D** · spec C-35, G-3

> **† 2026-08-15 — verdict STANDS; the "do not widen without proof" warning has since been strengthened.**
> `CanBeItemInvoice` `:67-68`, `InventoryVoucherLineViewModel:296-308` and `StockItem.cs:57` are unchanged and
> exact. **Corrected:** `RefreshPriceLevelDefaults` `:3081-3110` → declaration at **`:3295`** ·
> `ShowPriceLevelSelector` `:631-632` → **`:679-680`** · `CanBeSection34Note` `:1325-1326` → **`:2211-2212`** ·
> the standing warning `:70-79` → **`:70-83`**, now closing *"**Do not widen this predicate further without the
> equivalent proof.**"* **Related open row worth reading alongside this one:** `docs/full-clone-census.md` T0-10
> — Credit and Debit Notes move **no stock at all** (`ItemInvoiceStock.Counts()` at `ItemInvoiceStock.cs:53`
> returns true only for Purchase and Sales), so widening the mode without the stock parity would produce an
> item-wise note that still leaves inventory overstated.

| | |
|---|---|
| **What the operator experiences** | A sales return of stock cannot be entered as an item invoice at all. The operator enters the return on the plain Dr/Cr grid and types the gross amount, so the note cannot carry item lines, cannot move stock through the accounting voucher, and cannot print as an item-wise credit note. |
| **What we do** | `CanBeItemInvoice` admits only Purchase and Sales (`VoucherEntryViewModel.cs:67-68` **[code-verified]**), so a Credit or Debit Note has no item-invoice grid and therefore **no Rate field**. Even if the mode were opened, `InventoryVoucherLineViewModel.RateText` is only ever auto-filled from a Price Level (`:296-308`, driven by `RefreshPriceLevelDefaults`, `VoucherEntryViewModel.cs:3081-3110`), which `ShowPriceLevelSelector` restricts to Sales item invoices on a price-level-enabled company (`:631-632`). There is **no item-valuation fallback**: `StockItem.StandardCost` (`src/Apex.Ledger/Domain/StockItem.cs:57`) is read only by the StandardCost valuation method, never by voucher entry. |
| **What Tally does** | Credit Note and Debit Note get **all three** modes — Item Invoice, Accounting Invoice, As Voucher — with the definitions repeated verbatim from the Sales/Purchase sections. The corpus is precise about where the Rate is typed: Purchase item invoice (p.34) and Sales item invoice (p.39) both read "Rate — Type Price of one item"; the Credit/Debit Note item-invoice sections carry the corresponding auto-fill wording. |
| **Citation** | BOOK pp.54-55 (Credit Note repeats the Item / Accounting / As Voucher definitions), p.60 (Debit Note); BOOK p.34 vs p.39 for the Purchase/Sales Rate wording. **[corpus-verified upstream for pp.54-55]** |
| **Fix** | Widen `CanBeItemInvoice` (and `CanBeAccountingInvoice`) to Credit Note and Debit Note. **Precondition, learned the hard way:** the same widening on Purchase silently dropped the §194J TDS carve-out because `TdsPossible`/`DetectTdsShape` read the plain `Lines` collection, which is empty in invoice mode — see the standing warning at `VoucherEntryViewModel.cs:70-79`. That wiring is now done for Purchase and guarded by `PurchaseAccountingInvoiceTdsTests`; **do not widen this predicate further without the equivalent proof** for the note types, including the §34 GST layer (`CanBeSection34Note`, `:1325-1326`) which today lives on the plain grid only. |

---

### D17 · Group (accounting) master cannot create a top-level group — the Under picker has no "Primary"
**MEDIUM** · Area **MST**

> **† 2026-08-15 — verdict STANDS; all citations exact, zero drift.** `AccountGroupMasterViewModel.cs:210-219`
> still fills `ParentOptions` from `_company.Groups` (`:214-215`) with no sentinel, and `:179-183` still
> hard-rejects a null parent (the same refusal guards `Alter()` at `:113-117`); `DerivedNature` reads the parent
> at **`:159-161`**. The sibling sentinels are confirmed present at `StockGroupMasterViewModel.cs:119` and
> `GodownMasterViewModel.cs:120`, both `Display = "◦ Primary (top-level)"`.
> **The row's own caveat — "the Tally side needs one confirming page reference" — is now DISCHARGED.**
> `docs/invented-vs-cloned.md` **IV-12** supplies it: **[web]** `help.tallysolutions.com/tally-prime/masters-tally/groups-in-tallyprime/`
> ("Under: Select **Primary** or any other predefined groups"; "**Nature of Group**: Appears only if the group is
> created under **Primary**") and **[corpus]** SG p.67 extracted line 2085 ("One can also create groups under the
> Primary group category, if required"). **IV-12 is this row's twin and carries the fuller analysis** — including
> that the uncited comment at `GroupService.cs:13-17` is the root cause and that every sibling master already does
> the opposite. Schedule the two together.

| | |
|---|---|
| **What the operator experiences** | A user who wants a new top-level head of account — routine when mirroring an existing chart of accounts — cannot create one. Every group must hang off an existing group, so the operator either abandons the structure or parks it under a head where it does not belong. Same shape as D6, on a different master. |
| **What we do** | `RefreshParentOptions()` fills the picker straight from `_company.Groups` with **no sentinel** (`AccountGroupMasterViewModel.cs:210-219`), and `Create()` hard-rejects a null parent with "Pick an Under (parent) group — the nature is derived from it" (`:179-183`). **[code-verified]** Note the coupling that makes this non-trivial: `DerivedNature` is read *from the parent*, so a Primary group needs its nature chosen explicitly. Sibling masters in this same codebase already ship the sentinel (`ParentStockGroupOption`, `ParentGodownOption`). |
| **What Tally does** | "Under" offers **Primary** alongside the existing groups, and a Primary group's nature is stated on the master rather than inherited. |
| **Citation** | **[inherited]** — the upstream sweep's report was truncated before its citation line. **Treat the Tally side of this row as needing one confirming page reference** (the Group Creation walkthrough in SG/BOOK) before it is scheduled. Our side is code-verified and stands regardless. |
| **Fix** | Add a "◦ Primary (top-level)" sentinel to `ParentOptions`, make the parent nullable, and reveal an explicit Nature picker when Primary is chosen (since `DerivedNature` has no parent to derive from). Mirror the sentinel wording used by `ParentStockGroupOption` so the three masters read alike. |

---

### D18 · ~~Ten~~ **Eleven** predefined voucher types ship ACTIVE where TallyPrime ships them inactive
**LOW** · Area **MST** · spec C-16, G-4 · **† the "decorative" claim is now FALSE — and that makes the Fix DANGEROUS**

> **† 2026-08-15 — the seed defect STANDS; the COUNT is wrong and the FIX INSTRUCTION IS NOW UNSAFE.**
> **(1) The count.** The heading says **ten**; the body lists **eleven**, and eleven is what the seed carries.
> `SeedVoucherTypes.cs` is unchanged and exact: Credit Note `:30`, Debit Note `:31`, Physical Stock `:44`,
> Sales Order `:45`, Purchase Order `:46`, Delivery Note `:47`, Receipt Note `:48`, Rejection Out `:49`,
> Rejection In `:50`, Memorandum `:53`, Reversing Journal `:54` — every one still ends `, true)`. Only the four
> job-work rows (`:55-58`) and Payroll (`:67`) are seeded `false`. **Corrected to eleven.**
> **(2) 🔴 "`IsActive` is decorative" is FALSE at HEAD.** `MainWindowViewModel.cs:2756-2757` no longer exists;
> `VoucherTypeResolver.cs:58` skips inactive types, `:60` prefers the predefined one, and the five routes now
> surface `NoActiveTypeMessage` (`:2895`, `:2951`, `:3513`, `:3541`, `:5086`) instead of silently opening a
> deactivated type.
> **(3) 🔴 THEREFORE THE FIX BELOW IS DANGEROUS AS WRITTEN.** It says *"do not do this before the G-4 resolver
> fallback is removed (D9), or the types become unreachable rather than merely hidden."* **The fallback is
> already removed.** So seeding the eleven `IsActive: false` **today** would make them genuinely unreachable —
> and the activation route does not exist: repo-wide `grep "Show Inactive|ShowInactive"` across `src/` returns
> **zero hits**. **The correct sequence is now inverted: build `F10 > Show Inactive > Activate` FIRST, then flip
> the seed.** The precedent for the existing-companies half is unchanged
> (`VoucherTypeResolver.RepairSupersededSeedShortcuts`). **This is the clearest example in either register of a
> stale document turning a safe instruction into an unsafe one.**

| | |
|---|---|
| **What the operator experiences** | Opposite direction to every other row — **easier**, not harder. But a new company's Vouchers menu shows ten types TallyPrime hides, so the operator scans a longer list than the reference product, and the `F10 > Show Inactive > Activate` gesture the corpus teaches for exactly those ten types has nothing to act on. |
| **What we do** | `src/Apex.Ledger/Seed/SeedVoucherTypes.cs` seeds `IsActive: true` for Credit Note, Debit Note, Physical Stock, Sales Order, Purchase Order, Delivery Note, Receipt Note, Rejection Out, Rejection In, Memorandum and Reversing Journal (the `true` literal in each seed row, `:30-54`). Only the four job-work types and Payroll are seeded inactive. **[code-verified]** Combined with the G-4 resolver fallback (`MainWindowViewModel.cs:2756-2757`), `IsActive` is decorative for every one of them. |
| **What Tally does** | Ships these types inactive. The documented route is `Voucher screen > F10 (Other Vouchers) > Show Inactive > select the type > Activate this Voucher Type: Yes`, or the same flag from Chart of Accounts > Voucher Types. |
| **Citation** | BOOK — each step reads "GOT > Voucher > Press F10 (Other Voucher) > Click on Show Inactive > …": Memorandum, Reversing Journal, Rejection In, Rejection Out, Purchase Order, Receipt Note, Sales Order, Delivery Note. SG pp.83-84 for the two notes ("*Before entering Debit Note Voucher, first you have to activate the Voucher Type … supply 'Yes' to 'Activate this Voucher Type'*", and the identical sentence for Credit Note); SG p.73 for the general gesture. **[corpus-verified upstream]** |
| **Fix** | Seed the ten with `IsActive: false` and make the F10 menu offer a Show-Inactive branch that flips the flag. **Sequencing matters:** do not do this before the G-4 resolver fallback is removed (D9), or the types become *unreachable* rather than merely hidden. This is also a behaviour change for existing companies, so it needs the same repair-on-load treatment `VoucherTypeResolver.RepairSupersededSeedShortcuts` already gets — or an explicit decision to leave existing companies alone. |

---

## 3. Honest scale — this register is a sample, not a census

The request was a sweep of "such minute mistakes all over the app", and the number that matters for deciding
how to spend effort is the *real* one, not the comfortable one. Here it is, with its limits stated.

**What this register actually contains.** 19 distinct defects — 18 cited on both sides, 1 unverified —
merged from 22 raw findings across four sweeps (three merges: two sweeps found the balancing-figure defect
independently, and three separate findings turned out to be one blank-seeded Bill-wise row).

**Three of the four sweeps arrived truncated.** The *wrong defaults* sweep was cut mid-way through its
"checked and correct" list (item 7 of an unknown total); *fields we demand* was cut inside finding #6;
*block vs warn* was cut inside finding #5; *masters* was cut inside finding #6. I reconstructed the three
truncated findings from their titles and re-verified them against the code myself (D13, D16, D17) — but
**each of those sweeps may have had further findings I never received.** That is a floor on this count, not
a ceiling.

**Roughly 10 further distinct defects are already documented and are NOT re-listed here.**
`docs/voucher-entry-specification.md` ranks 17 gaps (G-1…G-17) and 48 conditions of which ~14 are marked
WRONG or ABSENT. Seven of its gaps overlap this register (G-3, G-4, G-8, G-11, G-12, G-15, G-17). The rest
are additional and still open — see §5.

**Eight named areas of the app were never swept at all** by any of the four lenses: reports (touched only
incidentally, via the dead Negative Stock report), voucher and invoice **printing**, the GST returns UI,
payroll masters and payroll entry, the TDS/TCS screens, the Day Book, import/export, and company creation /
F11 features. The keyboard and navigation surface is separately tracked and separately incomplete.

**So what is the real total?** I will not invent one, and any single number here would be a guess dressed as
a count. What can be said with evidence:

- **~29 defects are known today** (19 here + ~10 spec-only), all cited, all actionable.
- Each lens returned **5–6 defects from a partial pass over essentially one screen family** (voucher entry
  and master creation). There are **at least a dozen comparable screen families untouched**.
- The density is not random: three root causes (§1) generate most of what has been found, and those causes —
  auto-fill wired to one screen, block-instead-of-warn, and the missing layer-2/voucher-type config — are
  **app-wide**, not voucher-entry-specific. Wherever we have not looked, they are very likely still true.

On that evidence a complete sweep at the same density plausibly lands in the **low hundreds**, and I would
expect the count to grow fastest in printing and reports, which no lens has touched. **Treat "80" as a floor
rather than an estimate, and do not treat any number as final until the unswept areas in §7 have had a pass.**

**One structural encouragement, offered honestly:** the fix cost is not proportional to the count. D2, D3,
D11 and U-A are four rows and *one* piece of work — lift an auto-fill that already exists, with an ownership
rule that is already proven, to the screens that lack it. D9 and D10 unlock a further tier by themselves.

---

## 4. UNVERIFIED — our code is wrong about itself; whether Tally differs is unproven

Kept out of the ranked register on purpose. **A fabricated "Tally does X" is worse than an admitted gap.**

### U-A · Plain-grid Cost Allocation row seeds a blank amount — and the comment again asserts a pre-fill that does not exist
**Severity: our-side LOW–MEDIUM; Tally-side UNPROVEN** · Area **VE-D**

> **† 2026-08-15 — verdict STANDS, on both sides.** The false comment is verbatim at **`VoucherLineViewModel.cs:271-272`**
> (the block runs `:269-274`), `SyncCostApplicable` `:275-299` and `CostAllocationRowViewModel.cs:35` are unchanged
> and exact, and nothing stamps `AmountText` — the row is constructed with only a default Category at `:308-309`.
> **The safe half of "What to do meanwhile" — correcting the comment — has NOT been done.**
> **Corrected:** the row was already internally inconsistent here — it says `AddCostAllocation()` is called *"at
> `:302`"*, which lies **outside** the `:275-299` range it gives for the enclosing method. At HEAD the **call site
> is `:292`** and the **method declaration is `:302-313`**.

- **What we do (certain, [code-verified]).** `VoucherLineViewModel.cs:269-272` states the seeded cost row
  defaults "*its amount to the line, so the common single-centre case needs one centre pick*". It does not.
  `SyncCostApplicable` (`:275-299`) calls `AddCostAllocation()` at `:302`; that method pre-selects a sensible
  Category but constructs `CostAllocationRowViewModel` with `_amountText = string.Empty`
  (`CostAllocationRowViewModel.cs:35`), and nothing stamps it. The operator retypes the full line amount into
  the cost row on every cost-applicable expense line.
- **What Tally does — UNVERIFIED.** The corpus walkthrough reads "*Amount: Specify the amount e.g., Rs.
  5000*" (SG p.101 step 5), which **neither confirms nor rules out** a pre-filled field, and no help page
  states it. SG pp.101-102 steps 3-9 (the ₹5,000 Travelling Expenses example) is the only worked case and it
  is silent on the default.
- **What would settle it.** Observe a real TallyPrime Cost Allocation screen on a **single-centre**
  allocation and note whether Amount arrives filled.
- **What to do meanwhile.** Do **not** implement the pre-fill on the strength of an inference. Do **delete or
  correct the parenthetical claim at `:269-272`** — this is the third instance in this register (with D3 and
  D12) of an in-code comment documenting behaviour the code does not have, and in D3's case that comment is
  exactly what let the defect survive review. That correction is safe, free, and independent of the Tally
  question.
- **One live design constraint if it is ever built.** Per spec **C-27**, cost categories are **parallel
  sets**, not a partition — the corpus allocates ₹5,000 to Branch→Kolkata *and* ₹5,000 to
  Department→Marketing on a ₹5,000 line. Any pre-fill must therefore stamp the line amount **once per
  category**, not once per row. (Our engine currently rejects that corpus example outright — spec G-2, which
  is not in this register because it was found before these sweeps.)

---

## 5. Spec gaps NOT re-listed here (still open, still cited)

These are already ranked in `docs/voucher-entry-specification.md` §7 with citations. They are real and
additional to this register; they are omitted only to avoid duplicating a document that already holds them.

> **† 2026-08-15 — G-2 is FIXED and must come off this list; G-14 is unchanged.** `VoucherValidator.cs:338-339`
> now documents and enforces *"— **within each cost category independently** — their magnitudes must sum exactly
> to the line amount"*, and `:340-345` states the rule in the corpus's own terms: *"**Parallel sets, not a
> partition** … the corpus's worked example allocates one ₹5,000 travelling expense in full to Branch → Kolkata
> **and** in full to Department → Marketing **and** in full to Executive → Sales Executive 1 (TALLY PRIME STUDY
> GUIDE pp.101–102). Summing across categories and comparing that to the line — **which this validator used to
> do** — rejects the reference product's own example."* The per-axis check is at `:376-387`. Fixed by `aed9a50`,
> which also carries legacy-book rehydration. **The same correction applies to U-A's closing sentence**, which
> still says *"Our engine currently rejects that corpus example outright — spec G-2."* It no longer does.
> The other nine rows in this table were **not** re-verified in the 2026-08-15 pass — they live in
> `docs/voucher-entry-specification.md`, which is separately stale (see §6's `†` note).

| Spec ID | Gap | Severity |
|---|---|---|
| ~~**G-2**~~ | ~~Cost allocation enforces a partition~~ — **† FIXED `aed9a50`**; `VoucherValidator.cs:338-345`, `:376-387` | ~~CRITICAL~~ |
| **G-5** | Batch allocation is a free-text label on the Purchase/Sales invoice screens; the good sub-screen exists but is wired to the wrong screens | HIGH |
| **G-9** | Discount column has the wrong trigger (`EnableMultiplePriceLevels`) and the wrong scope (Sales only, % only) | MEDIUM |
| **G-10** | No godown split within one line — godown is a line-level scalar | MEDIUM |
| **G-13** | No party-details / dispatch sub-screens; starves the e-way-bill and e-invoice payloads | MEDIUM |
| **G-14** | The **voucher-screen** F12 layer does not exist (D10 is its master-screen twin) | MEDIUM (structural) |
| **G-16** | No cost-centre classes, no general voucher classes | LOW-MEDIUM |
| **G-17** | Remaining small gestures: `Accept? Yes/No`, "Automatic (Manual Override)" numbering, Memorandum→voucher conversion, `Alt+S` payment status, per-ledger narration, credit-days warning (C-25) | LOW |
| **C-01/02/03** | `Maintain Accounts` / `Maintain Inventory` / `Integrate Accounts with Inventory` company flags absent — C-02 is the single largest screen-shape switch in TallyPrime | ABSENT |
| **C-14** | `Enable multiple addresses` — no address picker (follows from G-13) | ABSENT |

---

## 6. ⚠️ The spec is stale in three places — verified this session

> ### † 2026-08-15 — THIS SECTION IS NOW STALE ABOUT ITSELF
> **(1) Item 1's closing sentence is WRONG.** It reads *"What remains is D1: it is not the default."* **D1 is
> fixed** — `SeedOpeningMode` at `VoucherEntryViewModel.cs:141-144`, called at `:1194`. Nothing remains of D1.
> Item 1's own citation drifted too: `:1176-1197` → **`:1231-1252`**.
> **(2) Item 2 stands**, but `CanBeAccountingInvoice` reads `Sales or Purchase` at **`:85-86`**, not `:80`.
> **(3) Item 3 stands, caveat included** — `InvoiceBillSplitOk` as a `CanAccept` conjunct `:4231` → **`:4445`**;
> the invoice bill-panel seeding `:444-463` → **`:466-521`** (stamps at `:492-512`). **The "I did not trace the
> posting path end-to-end, so confirm before closing" caveat is STILL UNDISCHARGED** — the 2026-08-15 pass did
> not trace it either.
> **(4) The closing note is stale about the very drift it exists to record:** the unfiltered ledger picker is now
> **`:1124`**, not `:1076`.
> **(5) 🔴 The final sentence — *"Every `file:line` in **this** register was re-read against the current worktree
> today"* — was true on 2026-08-04 and is the sentence most likely to be believed on a skim. It is no longer
> true.** Read it as *"…was re-read on 2026-08-04, and again on 2026-08-15; the 2026-08-15 corrections are the
> `†` lines."* Two files moved far enough to matter: `VoucherEntryViewModel.cs` by ~+55 lines in its first third
> and **~+215 lines past `:4200`**, and `MainWindow.axaml` by ~+39 lines around the stock-item masters.
> **⇒ This is the argument for the doc-vs-code CI check on the kickoff queue: no test in this repository reads a
> `.md` file, so nothing can fail when a register goes stale.**


`docs/voucher-entry-specification.md` is dated 2026-08-01 and the code has moved under it. Anyone reading it
alongside this register must know:

1. **G-6 is FIXED.** The spec says "*Single Entry does not exist anywhere in `src/`*". It now exists and is
   correct — `VoucherEntryViewModel.cs:1176-1197`, with the polarity inversion properly implemented and
   properly commented as the most dangerous value in the class. **What remains is D1: it is not the default.**
2. **G-7 is FIXED.** The spec says Purchase Accounting Invoice is dormant dead code behind
   `CanBeAccountingInvoice`. That predicate now reads `Sales or Purchase` (`:80`), the TDS/RCM wiring to the
   Particulars lines is done, and `PurchaseAccountingInvoiceTdsTests` guards the §194J carve-out.
3. **G-1 appears FIXED.** The spec's CRITICAL gap — bill-wise never fires in invoice mode, so Receivables is
   silently empty — no longer matches the code: `InvoiceBillSplitOk` is now a conjunct of `CanAccept` on the
   item-invoice path (`:4231`) and the invoice bill panel seeds correctly (`:444-463`). *I did not trace the
   posting path end-to-end, so treat this as "appears fixed, confirm before closing".*

**Line numbers throughout the spec have drifted** (e.g. the unfiltered ledger picker is `:1076`, not `:630`).
Every `file:line` in **this** register was re-read against the current worktree today.

---

## 7. Recommended next passes (not scheduled — for the phase gate, R12)

Ranked by expected defect density, given that the three root causes in §1 are app-wide:

1. **Printing / invoice formats** — never swept; D14 already shows a print-fidelity consequence (blank vs
   "0.00" in the Rate cell) discovered only as a side effect.
2. **Reports** — never swept; the one report a lens touched incidentally (Negative Stock) turned out to be
   structurally dead.
3. **The remaining ~14 master screens** — field order, F12 layer, sentinel options. D4, D6, D12 and D17 came
   from four masters; there are far more.
4. **Company creation / F11 features** — spec C-01, C-02, C-03 are ABSENT and C-02 is described as the
   largest screen-shape switch in the product.
5. **Payroll and TDS/TCS entry screens** — deep engine support, entry-surface fidelity unexamined.
6. **Day Book, import/export, GST returns UI.**

**Before any of it:** settle spec **U-1** (negative stock, D7) by observation, not by web citation. It is the
one row in this register with a documented history of three reverted attempts, and it is the row most likely
to be built on a wrong premise.

> ### † 2026-08-15 — §7 has been overtaken by the census; and its closing paragraph is now half wrong
> **The six passes below were largely run.** `docs/full-clone-census.md` (2026-08-10) swept the whole product to
> a denominator of **~115 capabilities: 42 complete, 44 partial, 21 absent, 8 undetermined**, and its §6 item 10
> records which surfaces it closed. Passes 1 (printing), 2 (reports), 4 (company creation / F11) and 6 (Day Book,
> import/export, GST returns UI) all now have measured rows — see census **T2-4** (the print engine has no image
> primitive, so IRN/QR/logo are structurally impossible without replacing `PdfWriter`), **T1-9/T1-10** (71 of 77
> report surfaces are dead ends; 32 cannot be printed at all), **T1-6** (company creation captured one field — **CLOSED 2026-08-17**; the census row is the current one) and
> **T1-11/T1-12** (no GSTR-1 or GSTR-3B JSON; 7 GSTR-1 and 5 GSTR-3B tables missing).
> **Passes 3 (the remaining master screens) and 5 (payroll / TDS-TCS entry surfaces) are the two that remain
> genuinely unswept** — census §6 lists payroll *entry-surface* fidelity among what is still unmeasured.
> **🔴 The closing paragraph is now half wrong.** *"Settle U-1 by observation"* is still right, but *"three
> reverted attempts"* is out of date — `docs/NEXT_SESSION_KICKOFF.md:113-146` records **eight**, and the
> **posting** half of D7 was subsequently built and shipped (`a12e651`) **without** U-1 being settled. What is
> still blocked on observation is the **valuation** half (NS-8), and the blocking measurements now have names:
> **T1/T2/T3/T4** in `docs/tallyprime-valuation-test-books.md`, of which **T4 is the question that stopped the
> work eight times and no document answers it.**
> **One number to carry forward instead of this section's own:** the census's headline is harsher than §3's
> "low hundreds" — **only 8 of 115 capabilities have ever had their behaviour compared to a source.**
