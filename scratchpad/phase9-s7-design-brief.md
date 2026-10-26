# Phase 9 slice S7 — Rule-88A ITC set-off + electronic ledgers + GST payment (PMT-06/DRC-03) + ITC-reversal engine — Implementation Brief

> **Slice:** P9-S7 (schema **v43 → v44**). The **first slice on the inbound/reconciliation side that POSTS to the
> ledger.** S6 imported GSTR-2A/2B, reconciled it, mirrored IMS, flagged §17(5), and **surfaced reversal
> candidates** — but posted nothing (advisory, ER-6/ER-14). **S7 is the SOLE POSTER (RQ-27):** it consumes
> those candidates, computes the deterministic Rule-88A set-off, deposits/pays GST cash via PMT-06/DRC-03, and
> posts the Rule 42/43/37/37A + §17(5) ITC reversals to GSTR-3B Table 4(B) — all through the double-entry
> engine so `Σ Dr == Σ Cr` and paisa never leak. Because S7 mutates the ledger, this brief is deliberately
> rigorous about **posting correctness, paisa conservation, idempotency / no-double-post, and ER-13
> (byte-identical when advanced-GST is off).**
>
> Design-only; grounded in the on-disk code (schema head confirmed `Schema.CurrentVersion = 43`,
> `src/Apex.Persistence.Sqlite/Schema.cs:98`). Governed by CLAUDE.md R1–R14; built via `/software`; the tax law is
> **A14-CONFIRMED (R7) — see §11**: Rule 88A / §49A order, Rule 42/43/37/37A formulas, PMT-06/DRC-03, the electronic-
> ledger (PMT-05/02/01) mechanics, the GSTR-3B Table-4 routing, and the **§50(3) = 18% p.a.** correction are all
> settled and are the authoritative spec below (the earlier ⚠ points now resolve to §11's rulings; two watch-outs —
> ECRS reclaim-balance + GST-2.0 cess-credit lapse — carried in §11.7); A10 adversarial pass (posting-correctness the
> top lens) before the PR gate; A12 owns git (R4).
>
> **Key RQs:** **RQ-20** (electronic ledgers), **RQ-21** (Rule 88A set-off), **RQ-22** (GST payment PMT-06/DRC-03),
> **RQ-27** (ITC-reversal engine — sole poster); supporting **RQ-26** (§17(5), landed in S6), **RQ-23** (Io).
> **DPs:** **DP-16** (residual-IGST split default = CGST-first, editable Table 6.1), **DP-17** (Rule 86B 1%-cash
> cap flag), **DP-30** (full Rule 42/43 + 37 + 37A reversal engine), **DP-31** (§17(5) per-purchase flag — the
> master flag shipped in S6; S7 posts its reversal), **DP-34** (§50 interest flag-only).
> **ERs:** **ER-7** (Rule-88A order; CGST↔SGST cross-utilisation **schema-impossible**; §49(5)(c)/(d) proviso
> non-overridable), **ER-14** (reversals hit Table 4(B) + reduce the credit ledger via the set-off engine /
> DRC-03; **the reconciler never auto-posts** — S7 is sole poster), **ER-2** (cess ring-fence: cess credit
> discharges only cess), **ER-3** (RCM output liability is **cash-only**, never credit-settled), **ER-9** (reports
> are pure projections over posted lines), **ER-10** (decimal-rupees in-engine / integer-paisa at the boundary;
> compute-once-then-split; no paisa leak), **ER-13** (advanced-GST-off ⇒ byte-identical to a v37 Phase-8 company).
> Requirements: `docs/phase9-gst-advanced-requirements.md` — S7 slice plan **lines 1022–1040**; RQ-20/21/22
> **829–841**; RQ-27 **779–784**; ER-7 **860–861**; ER-14 **873–875**; §2.8 set-off order **421–441**; §2.11
> reversals **478–501**; §2.14 PMT-06/DRC-03 **528–539**; DP-16 **1253–1256**; DP-17 **1257–1259**; DP-30
> **1300–1303**; golden fixtures **(F) 1118–1122** + **(H) 1130–1133**.

---

## 0. Grounding — what already exists (verified on disk)

S7 is a **posting** slice, so almost everything it needs to WRITE already has a proven builder, and everything it
needs to READ already has a pure projection. Nine seams are reused verbatim:

| Seam | File(s):line | What S7 reuses |
|---|---|---|
| **The only posting entry-point** | `LedgerService.Post(Voucher)` — `src/Apex.Ledger/Services/LedgerService.cs:30–42` | Every S7 posting (set-off Journal, PMT-06 deposit/payment, DRC-03, reversal, reclaim) is built as a `Voucher` and posted **only** through `Post`, which runs `VoucherValidator.EnsureValid` (balance invariant) then `Company.AddVoucherInternal`. **No S7 code writes an `entry_lines`/tax row directly.** |
| **Balance invariant** | `VoucherValidator.EnsureValid` — `src/Apex.Ledger/Services/VoucherValidator.cs:24–86` (Σ Dr == Σ Cr at `:74–75`; `IsBalanced` `:17`) | Guarantees no S7 voucher can corrupt the ledger — an unbalanced set-off/reversal throws `UnbalancedVoucherException` at post time. This is the #1-risk backstop. |
| **Dual-leg GST posting builder** | `RcmService.BuildReverseCharge` — `src/Apex.Ledger/Services/RcmService.cs:175–273` (local `Pair(head,amount,headBp)` `:205–225`) | The reference shape for building a **self-balancing per-head GST posting** carrying `GstLineTax` on each leg (total-then-split paisa exactness via `GstService.ComputeLineTax`; cess ring-fenced separately). S7's set-off `Dr Output {head} / Cr Input {head}` and reversal `Dr <cost> / Cr Input {head}` follow this shape. |
| **Statutory-payment + challan precedent** | `TdsDepositService` — `src/Apex.Ledger/Services/TdsDepositService.cs` (`EnsureStatPaymentType` `:35–46`, `BuildStatPayment` `:54–71`, `RecordChallan` `:77–86`, `OutstandingPayable` `:91–97`); `TdsChallan` — `src/Apex.Ledger/Domain/TdsChallan.cs:17–65`; `ChallanVoucherLink` — `…/ChallanVoucherLink.cs:9` | The direct template for the **PMT-06 challan** (`GstChallan` ← `TdsChallan`), the **stat-payment voucher** (`Dr payable / Cr bank`, posted via `Post`, reuse the `is_stat_payment` `VoucherType` flag), the **challan↔voucher link**, and the **`OutstandingPayable`-as-projection** read that a set-off engine mirrors to get "net cash payable per head". |
| **Find-or-create tax ledger** | `GstService.EnsureTaxLedger` — `src/Apex.Ledger/Services/GstService.cs:150–167`; `EnsureRcmOutputLedger` `:127–148`; `EnsureAdvanceTaxSuspenseLedger` (Current-Asset suspense) `:258–277`; `RoundOffLedgerName` `:61`; `TaxLedgerName` `:44–58` | S7's lazy `EnsureElectronicCashLedger()` (a Current-Asset ledger, modelled on the advance-tax suspense) and any lazy reversal-cost ledgers reuse this idempotent find-or-create-and-classify pattern. |
| **Output/ITC per head (set-off inputs)** | `Gstr3b.Build` — `src/Apex.Ledger/Reports/Gstr3b.cs:87–118`; `ReadSide` `:208–245`; support `GstReportSupport.PostedGstVouchers` `:38–44`, `InvoiceTaxableValue` `:213–226`, `PostedCessTotal` `:236–243`, `DirectionOf` `:24–29` | The set-off engine's inputs = `Gstr3b.OutwardCgst/Sgst/Igst` (+ `RcmOutward*`, cash-only) as **liability per head**, and `Gstr3b.ItcCgst/Sgst/Igst` (+ cess) as **credit available per head** — pure sums of posted `EntryLine.Amount` bucketed by `GstLineTax.TaxHead`, never recomputed (ER-9). |
| **Reversal-candidate surface (S6 hand-off)** | `ItcGateView` — `src/Apex.Ledger/Reports/ItcGateView.cs` (`ReversalCandidates` field `:35`; `ItcReversalCandidate` `:296–297`; `ItcReversalReason` `:273–286`; `ItcTriple` `:263–270`); `Gstr2bReconciliationReport.ReversalCandidates` `:33` (⇒ `InBooksOnly`) | S7's reversal engine **consumes** these candidates (reasons `Section17_5Blocked` / `Ineligible` / `Section16_2aaNotInPortal` / `ImsAcceptedCreditNote`), turning each into a **proposed** reversal the user confirms before S7 posts. |
| **Schema + store + Io + import-mirror** | `Schema.cs` (`MigrateV42ToV43` `:3190–3278`; `CreateV1` S6 tables `:462–535`; the v26→v27 challan precedent `MigrateV26ToV27` `:2505–2526`, `tds_challans` `:802–812`); `SqliteCompanyStore.cs` (v42-step `:1032–1050`, readers `:2253–2363`, save delete `:3919–3924`, insert `:4942–5048`); Io `CanonicalModel/Mapper/Xml.cs`, `ImportPlan.cs` (§15 `:1017–1072`), `ApplyJournal.cs`; downgrade helpers `InventoryVoucherRoundTripTests.DowngradeToV9:439–451`, `ItemInvoiceRoundTripTests.DowngradeToV11:320–328`; `SchemaMigrationEquivalenceTests:35–87` | The migration/persist/round-trip/all-or-nothing pattern, copied additively for v44 (§6, §7). |
| **Line-tax detail seam** | `GstLineTax` — `src/Apex.Ledger/Domain/GstLineTax.cs:13–56` (ctor `:39`); enums `GstTaxHead {Central,State,Integrated,Cess}` `GstEnums.cs:80–93`, `GstTaxDirection {Output,Input}` `:99–106`; `EntryLine` GST/TDS/TCS parallel details `EntryLine.cs:83/96/110` | S7 adds one nullable discriminant to `GstLineTax` (`GstAdjustmentKind? Adjustment`, default `null` = forward), mirroring how S2 added `RcmScheme` — lets the Table-6.1 / Table-4(B) projections classify posted set-off / reversal lines **without** polluting the existing outward/ITC sums. |

**Four facts shape the whole design and set S7 apart from S6 (which posted nothing):**

1. **The double-entry ledger is the single source of truth for every "electronic ledger" balance.** The four
   `Input {head}` Duties-&-Taxes ledgers **are** the electronic Credit Ledger's four pools; the four `Output {head}`
   ledgers **are** the electronic Liability Ledger. A set-off is a balanced Journal that moves value between them;
   a reversal reduces an `Input {head}` balance. This means the electronic ledgers are **projections, not new
   stored-balance tables** (§1) — which is what makes ER-13 automatic (adding no balance state ⇒ a company that
   never sets off is byte-identical) and eliminates the drift risk of a parallel balance table.

2. **Set-off / payment / reversal vouchers use the `Journal` / `Payment` base type, which `DirectionOf`
   (`GstReportSupport.cs:24–29`) maps to `null` direction** — so they are **automatically excluded** from
   `PostedGstVouchers` and therefore from the Table 3.1 outward and Table 4(A) ITC sums. This is the built-in guard
   that S7's postings **cannot corrupt** the existing GSTR-3B figures. Table 6.1 (payment) and Table 4(B)
   (reversal) are **new, separate projections** that read exactly those adjustment vouchers (by their `VoucherType`
   flag / `GstAdjustmentKind` tag), never the Sales/Purchase vouchers.

3. **No candidate ever auto-posts.** S6 already enforced advisory-only structurally (ER-14). S7 turns candidates
   into **proposed** reversals/set-offs that a user action confirms (amounts editable within legal bounds) before
   `LedgerService.Post` runs. This is the central defence against the top risk (silent ledger corruption /
   double-post). Formula-driven amounts (Rule 42/43, Rule-88A allocation) are **computed** automatically but still
   **posted only on an explicit confirmed action**, each guarded by an idempotency key (§5.3).

4. **The reversal reduces the credit ledger; the RCM/cash liability never uses credit (ER-3).** The engine must
   keep three value streams strictly separate: (a) forward ITC in the four Input pools (usable for set-off, cess
   ring-fenced ER-2), (b) RCM output liability + interest/penalty/fee (cash-only, never credit-settled), and
   (c) reversals (reduce Input pools, hit Table 4(B), optionally paid in cash via DRC-03).

---

## 1. Electronic ledgers (RQ-20) — PROJECTIONS, not stored-balance tables

### 1.1 The three ledgers and how each is derived

| Electronic ledger | Portal construct | Apex model (recommended) |
|---|---|---|
| **Credit Ledger** — 4 independent pools **IGST / CGST / SGST-UTGST / Cess** (cess ring-fenced) | ITC available to set off output tax | **Projection** of the four `Input {head}` Duties-&-Taxes ledger closing balances (debit balances), **reduced** by set-off utilisation and by posted ITC reversals. No new table. |
| **Cash Ledger** — deposits by **major head** (IGST/CGST/SGST/Cess) × **minor head** (Tax / Interest / Penalty / Fee / Other) | Money deposited via challan, drawable only within its own (major,minor) cell | **Projection** of the `Electronic Cash Ledger` Current-Asset ledger balance, **split by minor head** from the `gst_challans` deposit rows minus the payment/DRC-03 utilisations. One thin deposit table (`gst_challans`), no running-balance table. |
| **Liability Ledger** — output tax payable by head | Output tax + RCM + interest to be discharged | **Projection** of the four `Output {head}` ledger closing balances (credit balances) + the RCM output ledgers (cash-only), **reduced** by set-off and cash payment. No new table. |

**Decision — RECOMMEND projections over persisted running-balance tables**, and treat any `electronic_credit_ledger` /
`electronic_cash_ledger` **table** as an explicitly rejected design. This is a **deliberate refinement of the plan's
named v44 table list** (`docs/…requirements.md:1033` names `electronic_credit_ledger`, `electronic_cash_ledger`) —
**record the deviation in `memory.md` with the reason (R6)** and surface it at the PR gate. Rationale:

- **ER-9** — reports are pure projections over posted lines; a stored balance would *recompute*/*duplicate* state.
- **ER-13** — projections add **zero** persisted balance state ⇒ a company that never sets off/pays/reverses is
  **byte-identical** to a v43 company (the Input/Output ledgers are exactly what they are today).
- **Single source of truth / no drift** — the #1 risk is ledger corruption. A parallel running-balance table is a
  second source of truth that *will* drift from the double-entry ledger under any partial failure. Projecting from
  the posted vouchers is drift-proof by construction.
- **Faithfulness is preserved** — the *displayed* electronic-ledger statement (a portal-style running statement
  with opening/credit/debit/closing per head) is a **report** (`ElectronicLedgersView`, §1.3), materialised on
  demand from the posted vouchers + `gst_challans`, exactly as the GST portal's statement is a view over the
  filed/paid data.

> **Only if A14 or the gate insists on a persisted portal-style statement** for audit reproducibility: add a thin,
> **append-only, derived** `gst_cash_ledger_entries(date, major_head, minor_head, credit_paisa, debit_paisa, ref)`
> that is **rebuilt from postings** (never an independent writer) — but the default recommendation is to omit it.

### 1.2 `RQ-20`'s hard invariant — "no CGST↔SGST transfer path may exist in the schema"

Enforced in **two** places (structural, not "the code happens not to"):

1. **Engine** — the Rule-88A algorithm (§2) has **no branch** that debits `Input CGST` against `Output SGST` or
   vice-versa. Cross-head is unreachable, not merely unused.
2. **Schema** — the `gst_setoff_lines` allocation table (§6.1) carries a **`CHECK`** forbidding
   `(credit_head='C' AND liability_head='S')` and `(credit_head='S' AND liability_head='C')`, and forbidding any
   cess↔non-cess pairing (ER-2). A cross-head allocation is **impossible to persist**.

### 1.3 `ElectronicLedgersView` (new report, pure projection)

`src/Apex.Ledger/Reports/ElectronicLedgersView.cs` — a pure record (like `Gstr3b`) built over `[from, to]`:
- **Credit ledger:** per head, opening + additions (posted forward ITC) − utilisation (set-off) − reversals =
  closing. Cess pool ring-fenced.
- **Cash ledger:** per (major, minor) head, opening + deposits (`gst_challans`) − utilisation (payments/DRC-03) =
  closing.
- **Liability ledger:** per head, output tax + RCM (cash-only) − set-off − cash payment = closing (should foot to 0
  for a discharged period).
- Paisa-exact; all-zero when S7 is unused (ER-13). Posts nothing.

---

## 2. Rule-88A set-off engine (RQ-21; ER-7) — PURE compute

### 2.1 Shape

`GstSetOffService` (`src/Apex.Ledger/Services`) exposes a **pure, deterministic, side-effect-free** allocator plus a
thin poster that wraps it in a Journal voucher:

```csharp
// PURE — no ledger mutation. Deterministic, paisa-exact (integer paisa in/out).
public static SetOffAllocation Allocate(SetOffDemand demand, SetOffOptions options);

public sealed record SetOffDemand(                 // per-head, integer paisa
    long LiabCgst, long LiabSgst, long LiabIgst, long LiabCess,   // Output {head} (forward) liability
    long LiabRcmCash,                                              // RCM output — CASH ONLY (ER-3), never credit
    long CreditCgst, long CreditSgst, long CreditIgst, long CreditCess); // Input {head} pools (credit ledger)

public sealed record SetOffOptions(
    ResidualIgstSplit ResidualSplit = ResidualIgstSplit.CgstFirst,  // DP-16 default; editable Table 6.1 override
    Rule86BConfig? Rule86B = null);                                  // DP-17 — 1% min-cash cap, applied last

public sealed record SetOffAllocation(
    IReadOnlyList<SetOffLine> Lines,               // (creditHead|Cash → liabilityHead, paisa) — the Table 6.1 rows
    long CashCgst, long CashSgst, long CashIgst, long CashCess,     // residual payable in cash, per head
    long ClosingCgst, long ClosingSgst, long ClosingIgst, long ClosingCess); // carried-forward credit, per head
```

### 2.2 The exact utilisation order (§2.8, verifier-confirmed as unchanged by GST 2.0; requirements `:421–441`)

Operate on integer paisa, in this fixed sequence (deterministic):

- **Step A — IGST credit first, and fully exhausted (§49A):**
  1. `IGST credit → IGST liability` (mandatory, min of the two).
  2. Residual IGST credit → **CGST and/or SGST liability** in **any order / any proportion** (Rule 88A relaxed the
     old rigid order). **Default = `CgstFirst`** (DP-16: pay CGST liability before SGST). **A14-CONFIRMED (§11.1):**
     the portal HARD-ENFORCES IGST-first-and-exhausted, but the **intra-CGST/SGST split of the residual IGST credit is
     DISCRETIONARY / editable** — so the engine computes a compliant default **and MUST allow override** via the
     editable Table 6.1 (both golden splits reachable — §2.5).
  3. **IGST credit must be fully exhausted before any CGST or SGST credit is touched** (hard ordering — Step A
     completes before Steps B/C run — **A14-CONFIRMED, portal-enforced, §11.1**).
- **Step B — CGST credit (own-head priority):** `CGST credit → CGST liability`, then residual `CGST credit → IGST
  liability`. **CGST credit NEVER pays SGST** (head-purity wall — **A14-CONFIRMED, §11.1**).
- **Step C — SGST/UTGST credit (own-head priority):** `SGST credit → SGST liability`, then residual `SGST credit →
  IGST liability`. **SGST/UTGST credit NEVER pays CGST** (head-purity wall — **A14-CONFIRMED, §11.1**). **Proviso
  §49(5)(c)/(d) — non-overridable:** SGST credit may pay IGST **only after** CGST credit for IGST is exhausted ⇒
  enforce by **running Step B before Step C** (CGST-before-SGST for residual IGST).
- **Step D — Cess credit → Cess liability only** (ring-fenced, ER-2; **A14-CONFIRMED** as the proviso to §11 of the
  Compensation Cess Act, §11.1). Cess credit never pays CGST/SGST/IGST and vice-versa. **⚠ OPEN law question
  (A14, §11.7):** with Compensation Cess sunset (S1's dated windows), the future utilisability of *accumulated* cess
  ITC is legally **unsettled** — cess-only-against-cess holds **today**; do **not** build a lapse/write-off path yet.
- **Step E — residual liability → electronic cash ledger.** Whatever liability remains per head is cash-payable.
  **RCM output liability, interest, penalty, late fee = cash-only** (ER-3; `LiabRcmCash` never enters Steps A–D).
- **Step F — Rule 86B (99% cap, DP-17):** if configured-and-triggered (monthly taxable supply, excl. exempt/zero-
  rated, > ₹50 L and no listed exemption), force **≥ 1% of the output tax** through cash **after** set-off — i.e.
  claw back up to that floor from the credit-settled amount into `Cash*`. A configurable company/period flag;
  off by default ⇒ no effect ⇒ ER-13. **A14 did not return a new Rule 86B ruling this pass (§11.8)** — retain this
  config-flag design (> ₹50 L monthly-taxable trigger, applied after set-off, off by default); the exact
  trigger/exemption list stays as-designed pending any later A14 note.

**Cross-head prohibition (ER-7):** CGST credit ↔ SGST liability and SGST credit ↔ CGST liability have **no step** —
structurally impossible, and schema-CHECK-forbidden (§1.2, §6.1).

### 2.3 Editable Table 6.1 (portal-like override)

The default allocation is computed by `Allocate`. The UI (a later UI pass; not in this brief's engine scope) exposes
the **Table 6.1 grid** where the user may edit the residual-IGST split and the credit-vs-cash choices. Any override is
**re-validated** by `Allocate`-in-verify-mode against the non-overridable constraints:
- IGST credit fully exhausted before CGST/SGST credit;
- no CGST↔SGST cross-utilisation;
- cess only vs cess;
- the §49(5)(c)/(d) CGST-before-SGST-for-IGST proviso;
- (if on) the Rule 86B floor.
An illegal override is **rejected** (fail-fast) before any posting — never silently "fixed".

### 2.4 Posting the set-off (the poster)

`GstSetOffService.PostSetOff(company, ledgerService, period, allocation, VoucherType statAdjType)`:
- Builds **one balanced Journal** voucher (base type `Journal`; `VoucherType.IsGstStatAdjustment = true`, §6.4).
- One `EntryLine` pair per allocation line: `Dr Output {liabHead}` (reduce liability) / `Cr Input {creditHead}`
  (use credit), each carrying a `GstLineTax` tagged `Adjustment = SetOff` so Table 6.1 can project it. Cess set-off
  is a `Dr Output Cess / Cr Input Cess` pair (ring-fenced).
- **The residual-cash portion is NOT part of the set-off Journal** — it is discharged by the payment voucher (§3),
  keeping "credit utilisation" and "cash payment" as separate, individually-auditable vouchers.
- Posted via `LedgerService.Post` ⇒ `VoucherValidator` guarantees `Σ Dr == Σ Cr`.
- The structured allocation persists to `gst_setoff_lines` (§6.1) for the Table 6.1 audit + the report.
- **Idempotent per period** (§5.3): a period with an existing posted set-off is not re-posted; a re-run is a
  user-confirmed replace (delete the prior set-off voucher + rows, re-post) — never an additive second voucher.

### 2.5 Golden invariant (fixture (F), Circular 98/17/2019; requirements `:1118–1122`)

Liability IGST 1,000 / CGST 300 / SGST 300; credit IGST 1,300 / CGST 200 / SGST 200. Total credit (1,700) exceeds
total liability (1,600) by ₹100, and — because IGST credit is fungible across all three heads — the whole liability is
dischargeable **without any cash**, leaving a ₹100 credit surplus carried forward. The requirements doc asserts the
test properties (`:1118–1122`): IGST credit 1,300 → IGST 1,000, residual 300 → CGST **and/or** SGST (any
order/proportion) ⇒ **cash payable = ₹0 in both valid splits**, "Option 1 leaves ₹100 CGST credit, Option 2 leaves
₹100 SGST credit." The engine encodes **both splits as reachable** via the Table 6.1 override; the test asserts:
(a) **cash = ₹0** in each split, (b) the surplus credit lands in the expected pool per the doc's two options,
(c) **no CGST↔SGST cross-utilisation** in any allocation line, and (d) the cash ledger is untouched.
> **A14-CONFIRMED (§11.1):** A14 confirmed the **set-off ORDER** (IGST-first-and-exhausted; head-purity walls; cess
> ring-fence) but did **NOT** bless a specific Circular-98 closing-balance table. ⇒ **do NOT freeze memorized numbers.**
> The fixture asserts **LEGAL PROPERTIES** — IGST-first-exhausted, head-purity walls, cess ring-fence, minimal residual
> cash (₹0 here), no CGST↔SGST cross-utilisation — and lets the **engine compute its OWN deterministic worked example**
> (the ₹100 surplus landing per whichever split the Table 6.1 override selects). Property-based, not number-frozen.

---

## 3. GST payment posting (RQ-22) — PMT-06 (challan) + DRC-03 (voluntary/off-return)

### 3.1 PMT-06 — deposit into the electronic cash ledger + discharge residual liability

**Model (mirrors `TdsDepositService` + `TdsChallan`):**
- **`GstChallan`** domain type (`src/Apex.Ledger/Domain/GstChallan.cs`, modelled on `TdsChallan.cs:17–65`): `Id`,
  `Cpin` (generated on creation), `Cin`, `Brn` (on bank payment — both nullable until paid), `DepositDate`,
  `MajorHead` (`GstTaxHead`), `MinorHead` (`GstMinorHead` enum: Tax / Interest / Penalty / Fee / Other),
  `Amount` (paisa-exact, > 0), `VoucherId` (the deposit voucher). Fail-fast ctor (non-blank, amount > 0, paisa-exact).
- **Deposit posting** (`GstDepositService.PostPmt06`): reuse the `is_stat_payment` `VoucherType` flag
  (`EnsureStatPaymentType`-style, `TdsDepositService.cs:35–46`). Two-leg **Payment** voucher:
  `Dr Electronic Cash Ledger` (Current-Asset, lazily created by `EnsureElectronicCashLedger()` ← the advance-tax-
  suspense precedent `GstService.cs:258–277`) / `Cr Bank`. Record a `GstChallan` (CPIN→CIN/BRN, major+minor head),
  link challan↔voucher (mirror `Company.LinkChallanToVoucher` `:570–574`).
- **Discharge of residual liability from the cash ledger:** a second Payment voucher `Dr Output {head}` /
  `Cr Electronic Cash Ledger` per residual head (drawing down the deposited cash within its own minor head),
  tagged `GstLineTax.Adjustment = CashPayment`.
  > **Tally-Ctrl+F Autofill simplification (fallback):** collapse the two steps into a single stat-payment voucher
  > `Dr Output {head} / Cr Bank` + the `GstChallan` record (no separate cash-ledger leg). This is the existing
  > `TdsDepositService.BuildStatPayment` shape (`:54–71`). **Recommend the two-step deposit-then-utilise model for
  > fidelity** (it makes the cash-ledger balance real and minor-head-split), but the single-step is a legitimate
  > lower-fidelity option. **A14-CONFIRMED (§11.2/§11.3):** the cash ledger (PMT-05) is a **2-D matrix** MAJOR head
  > {IGST/CGST/SGST-UTGST/Cess} × MINOR head {Tax/Interest/Penalty/Fee/Other}; **CPIN** is 14-digit with **15-day
  > validity**, generated on challan creation, and the **cash ledger is credited ONLY on CIN** (bank credit) — so the
  > **two-step deposit-then-utilise model is the faithful one** (it materialises the CIN-gated, minor-head-split cash
  > balance; PMT-06 is also the QRMP M1/M2 challan). Minor heads other than Tax are cash-only (credit can't settle
  > interest/penalty/fee — §11.2).

### 3.2 DRC-03 — voluntary payment / off-return ITC reversal (requirements §2.14 `:528–539`)

- **`GstDrc03`** domain type + `gst_drc03` table (§6.3): `Id`, `Drc03Ref`, `Cause` (rule/section text —
  Rule 37/37A/§17(5) / 2B-IMS mismatch / voluntary), `Period`, per-head + per-minor-head amounts (paisa),
  `VoucherId` (the payment voucher), `Drc03aDemandRef` (nullable — DRC-03A demand link, 2024), `CreatedAt`.
- **DRC-03 is the landing instrument for reversals that cannot be netted in GSTR-3B** (rejected credit notes, Rule
  37/37A, 2B/IMS mismatches). Posting: `Dr <reversal-cause ledger>` / `Cr Electronic Cash Ledger` (or `Cr Bank`) for
  a cash-paid reversal, plus the `GstDrc03` record; for a reversal that merely reduces unutilised credit, the
  posting is `Dr <reversal-cause> / Cr Input {head}` (see §4.2) and the DRC-03 records the disclosure.
  **A14-CONFIRMED (§11.3):** DRC-03 is a **Rule 142(2)/(3)** voluntary/self-ascertained payment **payable from cash
  and/or credit — subject to the TAX-ONLY credit rule** (§11.2: credit settles only the tax component; interest /
  penalty / fee / late-fee are cash-only). The credit-reduction-vs-cash choice follows whether the ITC was already
  utilised. **DRC-03A** (Nov-2024, **Notn 12/2024-CT**) links a DRC-03 payment to a **confirmed demand** — captured in
  `drc03a_demand_ref` (§6.3). Circular 172/04/2022 governs DRC-03 for reversals.
- **§50 interest = flag-only (DP-34):** interest on delayed cash / wrongly-availed-and-utilised ITC is captured as a
  flag/field on the challan/DRC-03 record, **not auto-computed** in Phase 9. **A14-CORRECTION (§11.6): the §50(3) rate
  on wrongly-availed-AND-utilised ITC is 18% p.a. — NOT 24%** (Notn 09/2022-CT + Rule 88B + Circular 192/04/2023); if a
  rate is ever surfaced it MUST be 18%.

---

## 4. ITC-reversal engine (RQ-27; §2.11 `:478–501`; DP-30/DP-31) — the SOLE POSTER

`ItcReversalService` (`src/Apex.Ledger/Services`) is the **only** code in the codebase that posts an ITC reversal
(ER-14). It (a) **consumes** the S6 candidates, (b) **computes** the Rule 42/43 formula reversals, and (c) **posts**
each as a balanced stat-adjustment Journal reducing the credit ledger and feeding GSTR-3B Table 4(B).

### 4.1 Consuming the S6 candidate surface — which auto-compute, which are user-confirmed, what each posts

The candidates arrive from `ItcGateView.ReversalCandidates` (`ItcGateView.cs:35`, reasons at `:273–286`) and
`Gstr2bReconciliationReport.ReversalCandidates` (`:33` ⇒ `InBooksOnly`). **None auto-posts** (§0 fact 3). Mapping:

| S6 candidate reason | Rule / table | S7 treatment | Posting |
|---|---|---|---|
| `Section17_5Blocked` (`ItcReversalReason.Section17_5Blocked`) | §17(5) → Table **4(B)(1)** | **User-confirmed** reversal of any ITC that WAS availed on a §17(5)-blocked purchase (ideally never availed; if posted, reverse). | `Dr "§17(5) Blocked Credit (cost)" / Cr Input {head}`, tagged `Adjustment = ReversalSection17_5`. |
| `Ineligible` (Table 4(D)) | disclosure only | **User-confirmed / mostly informational** — 4(D) discloses ineligible ITC not taken; post a reversal only if it was availed. | Same shape, tagged `ReversalIneligible`; often no posting (disclosure). |
| `Section16_2aaNotInPortal` | §16(2)(aa) period-eligibility **hold** | **NOT a permanent reversal** — the ITC is *deferred* until it appears in a later 2B, not reversed. Surface as a hold; only escalates to a Rule 37A reversal if the supplier permanently fails. **Do not auto-post.** | Typically **no posting** (deferral); escalates to Rule 37A (below) at the 30-Nov cut-off. |
| `ImsAcceptedCreditNote` | Oct-2025 CN → Table 4(B) | **The clearest postable candidate** — accepting a supplier credit note reverses the recipient's ITC. Amount = the IMS **declared** reversal (`DeclaredReversalPaisa` / `NoReversalDeclared`) if present, else the full forward tax of the note (`ItcGateView.cs:155–170`). **User-confirmed** (confirm the declared amount). | `Dr "ITC Reversal — Credit Note" / Cr Input {head}`, tagged `ReversalCreditNote`, feeding Table 4(B). |

> **Design principle:** S7 presents every candidate as a **proposed** reversal (identity + suggested amount from
> the candidate's `SuggestedReversal`); the user confirms/edits within legal bounds; then S7 posts. This is the
> central mitigation of the top risk. `Section16_2aaNotInPortal` deliberately does **not** post (it is a deferral,
> not a reversal) — a subtle correctness point A10 must check.

### 4.2 The four statutory reversal rules — trigger, amount, posting, idempotency, reclaim

For each, the **posting shape** is a balanced **stat-adjustment Journal** (`IsGstStatAdjustment`, base `Journal`):
`Dr <reversal-cost / suspense ledger> / Cr Input {head}` per head — **reducing the electronic credit ledger**
(the `Input {head}` balance) and carrying a `GstLineTax` tagged with the rule so the **Table 4(B)** projection picks
it up. Off-return reversals additionally get a `GstDrc03` record (§3.2). Every posted reversal writes an
`itc_reversals` row (§6.2) keyed by `(rule, period, source_voucher_id|source_line_id)` for idempotency.

- **Rule 42 — inputs & input services common credit** (requirements `:486–488`). **Trigger:** common credit serves
  both taxable and exempt / non-business supply. **Amount (A14-CONFIRMED §11.4):** **C1 = T − (T1 + T2 + T3)**;
  **C2 = C1 − T4**; monthly **D1 = (E ÷ F) × C2** (E = exempt turnover, F = total turnover) plus **D2 = 5% × C2**
  (deemed non-business); **reversal = D1 + D2**. **Annual true-up recomputed on the full-year E÷F by the
  September-following return / GSTR-9** (interest on any shortfall). **Posting:** per head
  `Dr "ITC Reversal — Rule 42" / Cr Input {head}`, **Table 4(B)(1)** (non-reclaimable — A14-CONFIRMED §11.5).
  **Idempotency:** one reversal row per (Rule42, period); a re-compute for the same period **replaces**
  (delete + re-post), never stacks; the annual true-up is a distinct `(Rule42, FY-trueup)` row posting the delta.
- **Rule 43 — capital-goods common credit** (requirements `:489–490`). **Trigger:** common capital-goods credit.
  **Amount (A14-CONFIRMED §11.4):** **Tm = Tc ÷ 60** (monthly tranche of the common capital-goods credit Tc);
  each month reverse **Te = (E ÷ F) × Tr** (the exempt-turnover share) **over 60 months**. **Posting:** same shape,
  per head, **Table 4(B)(1)**. **Idempotency:** one row per (Rule43, capital-good, period); a 60-month schedule keyed
  to the asset so each month posts once.
- **Rule 37 — 180-day non-payment** (requirements `:491–492`; golden fixture **(H)** `:1130–1133`). **Trigger
  (A14-CONFIRMED §11.4):** recipient has not paid the supplier within **180 days** of the invoice ⇒ **proportionate
  reversal + §50 interest**. **Amount:** the full ITC availed on that invoice (fixture H: ₹18,000). **Posting:**
  `Dr "ITC Reversal — Rule 37" / Cr Input {head}`, **Table 4(B)(2)** (reclaimable — A14-CONFIRMED §11.5), landed on
  **DRC-03**; §50 interest flag-only (DP-34; **18% p.a. if surfaced — §11.6**). **Reclaim:** on later payment the ITC
  is **re-availed — NO time bar** (A14-CONFIRMED): a linked `Dr Input {head} / Cr "ITC Reversal — Rule 37"` posting,
  `itc_reversals.reclaim_of_id` → the reversal row, tagged `Adjustment = Reclaim`, routed to **Table 4(D)(1)** (§11.5).
  **Idempotency:** one reversal per invoice; one reclaim per reversal — and the reclaim is **capped at the tracked
  reversal balance** (ECRS, §11.7 / §5.3), never over-reclaimed.
- **Rule 37A — supplier non-payment** (requirements `:493–494`). **Trigger (A14-CONFIRMED §11.4 — note the TWO
  distinct dates):** the invoice is in 2B but the supplier has **NOT filed GSTR-3B by 30-Sep following the FY** ⇒ the
  recipient **reverses ITC by 30-Nov** ⇒ **reclaim when the supplier subsequently files.** (30-Sep = supplier cut-off;
  30-Nov = recipient reversal deadline — do not conflate.) **Consumes** the S6 `Section16_2aaNotInPortal` /
  InBooksOnly / Rule-37A candidate. **Amount:** ITC availed. **Posting:** same shape, **Table 4(B)(2)** (reclaimable —
  §11.5). **Reclaim:** when the supplier files, re-avail (linked reclaim row → **Table 4(D)(1)**, capped at the tracked
  reversal balance — ECRS §11.7).

### 4.3 GSTR-3B Table 4(B) — additive projection (retire "display-only" without breaking ER-13)

`Gstr3b.Build` (`Gstr3b.cs:87–118`) gains a **Table 4(B) reversal projection** (and Table 6.1 set-off), mirroring how
S2 added the RCM buckets as **init-only fields defaulting to `Money.Zero`** (`Gstr3b.cs:34–56`). New init-only fields
`ItcReversedCgst/Sgst/Igst/Cess` (Table 4(B)) + the Table-6.1 utilisation figures, computed **only** from the
adjustment vouchers (by `GstAdjustmentKind`), default zero when none exist. **Bucket routing (A14-CONFIRMED §11.5,
GSTN advisory 01-Sep-2022):** Rules 38/42/43 + §17(5) ⇒ **4(B)(1)** (non-reclaimable); Rule 37/37A ⇒ **4(B)(2)**
(reclaimable); a reclaim ⇒ **4(D)(1)** (and pulled into net ITC via **4(A)(5)**). The `GstAdjustmentKind` tag maps each
posted line to its bucket. **Net eligible ITC = Table 4(A) − Table 4(B).** The existing `NetCgst/Sgst/Igst` display-only computation (`:59–65`) stays; when posted set-off exists the
report additionally shows the **posted** Table 6.1 (this is what "retire the display-only net" means — the display-
only arithmetic remains for a period with no set-off, so **ER-13 holds byte-for-byte**). The set-off/reversal
vouchers are `Journal`/`Payment` base ⇒ `DirectionOf` = null ⇒ **excluded from the 3.1/4(A) sums** (§0 fact 2) —
so these additions **cannot** move the existing figures.

---

## 5. POSTING-CORRECTNESS invariant (the S7 analogue of S6's advisory-only §5) — enforced structurally

S6's headline property was "never post". S7's is "**post exactly once, always balanced, never leaking paisa, never
touching the ledger except through the guarded entry-point**". Four structural guarantees:

### 5.1 The single posting entry-point
Every S7 posting is a `Voucher` handed to `LedgerService.Post` (`LedgerService.cs:30–42`). **No S7 service writes an
`entry_lines` row, a tax row, or a ledger balance directly.** `VoucherValidator.EnsureValid` (`:24–86`) runs on every
post — the `Σ Dr == Σ Cr` throw (`:74–75`) makes an unbalanced set-off/reversal a **hard failure at post time**, not a
silent corruption. A reflection/compile test asserts no S7 service exposes a balance-mutating method.

### 5.2 Paisa conservation (ER-10)
- Set-off allocation and reversal per-head splits use the existing **largest-remainder, compute-once-then-split**
  engine (`AdditionalCostApportionment.Allocate` / `Money.RoundToPaisa`, already used by the ITC-gate split
  `ItcGateView.SplitHeadTax`) — integer paisa in, integer paisa out, `Σ parts == whole`.
- Every set-off line's `Dr` == `Cr`; every deposit `Dr == Cr`; every reversal `Dr == Cr`. The cash residual per head
  equals `liability − credit-utilised` to the paisa (no float).
- A **paisa-conservation test** (§8) asserts: after a full set-off + payment + reversal cycle, the electronic-ledger
  projection foots exactly (Σ closing credit + Σ cash paid + Σ reversed == Σ opening credit + Σ liability), and every
  voucher balances.

### 5.3 Idempotency / no-double-post (the #1 risk)
- Each posted reversal writes an `itc_reversals` row with a **unique key** `(company_id, rule, period,
  source_voucher_id | source_line_id)`; the engine **checks for an existing row before posting** → skip (or post a
  signed **delta**, for the Rule 42/43 annual true-up), never a duplicate. A **UNIQUE index** on that key makes a
  double-post a DB error, not silent drift.
- Each period's set-off writes `gst_setoff_lines` keyed to the period + set-off voucher; a re-run is a **confirmed
  replace** (delete prior rows + cancel/replace the prior set-off voucher), never a second additive voucher.
- Rule 37/37A **reclaim** is keyed by `reclaim_of_id` → its reversal row, so re-availment posts **once**.
- **ECRS reclaim-balance guard (A14-CONFIRMED §11.7):** the portal HARD-VALIDATES that a **Table 4(D)(1) reclaim cannot
  exceed the tracked reversal balance** (Electronic Credit Reversal & Re-claimed Statement). ⇒ the engine maintains a
  **running per-head reversal balance** (the `itc_reversals` rows for a head, netted by their `reclaim_of_id`
  reclaims) and **rejects (fail-fast) any reclaim that would overdraw it** — so S7 never advises an un-fileable reclaim.
- **Nothing auto-posts on report open** — set-off/payment/reversal are explicit user-confirmed actions (§0 fact 3).

### 5.4 No pollution of existing figures / no cross-head / cess ring-fence
- Adjustment vouchers (`Journal`/`Payment` base) are excluded from `PostedGstVouchers` by `DirectionOf` (§0 fact 2)
  ⇒ Table 3.1 outward and Table 4(A) ITC are **provably unchanged** by S7 postings (before/after test).
- `gst_setoff_lines` CHECK forbids CGST↔SGST and cess↔non-cess (§1.2, §6.1); the engine has no such branch.
- The RCM output liability (`LiabRcmCash`) never enters Steps A–D (ER-3) — a test asserts an RCM-only period pays
  entirely in cash with zero credit utilisation.

---

## 6. Schema v44 (mirror `MigrateV42ToV43` EXACTLY)

Bump `Schema.CurrentVersion` **43 → 44** (`Schema.cs:98`). Add **every** table/column/index to **BOTH** `CreateV1`
**AND** a new `MigrateV43ToV44` const (byte-identical DDL — `SchemaMigrationEquivalenceTests:35–87` enforces it). Wire
an `if (version == 43)` step in `SqliteCompanyStore.EnsureSchema` immediately after the `== 42` block
(`SqliteCompanyStore.cs:1050`), in one `BeginTransaction`/`Commit` that runs the migration then bumps `schema_version`
to 44. All new tables carry `company_id` FK + `ix_*_company`; empty when S7 is unused ⇒ **ER-13 byte-identical**.

### 6.1 `gst_setoff_lines` (Table 6.1 allocation — audit + report)
```
id TEXT PK, company_id TEXT FK→companies,
voucher_id TEXT NOT NULL REFERENCES vouchers(id),   -- the posted set-off Journal
period TEXT NOT NULL,                                -- "yyyy-MM"
credit_head INTEGER NOT NULL,                        -- GstTaxHead of the credit pool used, OR the Cash sentinel
liability_head INTEGER NOT NULL,                     -- GstTaxHead of the liability discharged
is_cash INTEGER NOT NULL DEFAULT 0,                  -- 1 ⇒ paid from cash ledger (credit_head ignored)
amount_paisa INTEGER NOT NULL,
CHECK (NOT (credit_head = 0 AND liability_head = 1)),   -- no CGST(0)→SGST(1)
CHECK (NOT (credit_head = 1 AND liability_head = 0)),   -- no SGST(1)→CGST(0)
CHECK ((credit_head = 3) = (liability_head = 3))        -- Cess(3) only vs Cess (ER-2)
+ CREATE INDEX ix_gst_setoff_lines_company, ix_gst_setoff_lines_voucher
```
*(Head ordinals per `GstTaxHead`: Central=0, State=1, Integrated=2, Cess=3 — `GstEnums.cs:80–93`.)*

### 6.2 `itc_reversals` (Rule 37/37A/42/43 + §17(5) reversal record — the poster's audit row)
```
id TEXT PK, company_id TEXT FK,
rule INTEGER NOT NULL,                               -- ItcReversalRule (Rule37/37A/42/43/Section17_5/Ineligible)
period TEXT NOT NULL,                                -- "yyyy-MM" (or FY for the annual true-up)
cgst_paisa/sgst_paisa/igst_paisa/cess_paisa INTEGER NOT NULL DEFAULT 0,
d1_basis_paisa INTEGER NULL, d2_basis_paisa INTEGER NULL,   -- Rule 42/43 apportionment basis (audit)
source_voucher_id TEXT NULL REFERENCES vouchers(id),        -- the purchase whose ITC is reversed (Rule 37/42/43/§17(5))
source_line_id TEXT NULL REFERENCES gstr2b_lines(id),       -- the 2B line (IMS-accepted CN / Rule 37A candidate)
reversal_voucher_id TEXT NOT NULL REFERENCES vouchers(id),  -- the posted stat-adjustment Journal
reclaim_of_id TEXT NULL REFERENCES itc_reversals(id),       -- set on a re-availment row → its reversal (Rule 37/37A)
drc03_id TEXT NULL REFERENCES gst_drc03(id),                -- off-return landing instrument
table4b_bucket INTEGER NOT NULL DEFAULT 0,                  -- 4B(1)/4B(2)/4D
created_at TEXT NOT NULL
+ UNIQUE (company_id, rule, period, source_voucher_id, source_line_id)   -- idempotency key (§5.3)
+ CREATE INDEX ix_itc_reversals_company
```

### 6.3 `gst_challans` (PMT-06) + `gst_drc03` (mirror `tds_challans`, `Schema.cs:802–812` / `MigrateV26ToV27:2505–2526`)
```
gst_challans:
  id TEXT PK, company_id TEXT FK, cpin TEXT NULL, cin TEXT NULL, brn TEXT NULL,
  deposit_date TEXT NOT NULL, major_head INTEGER NOT NULL, minor_head INTEGER NOT NULL,   -- GstTaxHead × GstMinorHead
  amount_paisa INTEGER NOT NULL, voucher_id TEXT NOT NULL REFERENCES vouchers(id),
  interest_flag INTEGER NOT NULL DEFAULT 0   -- §50 flag-only (DP-34)
  + CREATE INDEX ix_gst_challans_company

gst_drc03:
  id TEXT PK, company_id TEXT FK, drc03_ref TEXT NULL, cause TEXT NOT NULL, period TEXT NOT NULL,
  cgst_paisa/sgst_paisa/igst_paisa/cess_paisa INTEGER NOT NULL DEFAULT 0,
  interest_paisa INTEGER NOT NULL DEFAULT 0,   -- flag/field-only (DP-34), not auto-computed
  drc03a_demand_ref TEXT NULL, voucher_id TEXT NULL REFERENCES vouchers(id), created_at TEXT NOT NULL
  + CREATE INDEX ix_gst_drc03_company
```
*(A `gst_challan_voucher_links` M:N table is unnecessary — a GST challan maps to exactly one deposit voucher, so a
direct `voucher_id` FK suffices; noted because TDS used a link table for its M:N case.)*

### 6.4 Column ALTERs (default 0/NULL ⇒ byte-identical, ER-13)
```
ALTER TABLE voucher_types ADD COLUMN is_gst_stat_adjustment INTEGER NOT NULL DEFAULT 0;  -- Alt+J set-off/reversal Journal
ALTER TABLE entry_lines   ADD COLUMN gst_adjustment_kind    INTEGER NULL DEFAULT NULL;    -- GstAdjustmentKind (null = forward)
```
*(PMT-06 deposit/payment reuses the existing `voucher_types.is_stat_payment` flag from v26→v27 — no new flag. DRC-03
payments are ordinary `Payment` vouchers identified by their `gst_drc03` record.)*

### 6.5 Domain enums (new, `GstEnums.cs`)
- `ResidualIgstSplit { CgstFirst = 0, SgstFirst, Proportionate }` (DP-16; default `CgstFirst`).
- `GstMinorHead { Tax = 0, Interest, Penalty, Fee, Other }` (cash-ledger minor heads).
- `GstAdjustmentKind { SetOff = 0, CashPayment, ReversalRule37, ReversalRule37A, ReversalRule42, ReversalRule43,
  ReversalSection17_5, ReversalIneligible, ReversalCreditNote, Reclaim }` (the `GstLineTax.Adjustment` discriminant).
- `ItcReversalRule { Rule37 = 0, Rule37A, Rule42, Rule43, Section17_5, Ineligible }` (the `itc_reversals.rule` value).

### 6.6 Store load/save + downgrade helpers
- **Store:** add `ReadGstSetoffLines` / `ReadItcReversals` / `ReadGstChallans` / `ReadGstDrc03` (mirror the S6 readers
  `SqliteCompanyStore.cs:2253–2363`), wired into `LoadCompany` (after the S6 load block `:1396–1403`); add
  delete-then-insert pairs in `SaveCompany` (mirror the S6 DELETE order `:3919–3924` + `InsertGstr2bRecords`
  `:4942–5048`, child-first FK order: drc03/challans/reversals/setoff-lines). Read the new
  `entry_lines.gst_adjustment_kind` in the entry-line loader and the `voucher_types.is_gst_stat_adjustment` flag in
  the voucher-type loader; write them in the savers.
- **Downgrade helpers (REQUIRED — R9 exit-gate item):** add `DROP INDEX/TABLE IF EXISTS` for the four new tables to
  **`DowngradeToV11`** (`ItemInvoiceRoundTripTests.cs:320–328`, alongside the S6 drops) **and `DowngradeToV9`**
  (`InventoryVoucherRoundTripTests.cs:439–451`). The `entry_lines`/`voucher_types` rebuild-idiom strip lists gain the
  two new columns so the table-rewrite drops them (SQLite pre-3.35 lacks `DROP COLUMN`).

---

## 7. Io canonical model — lossless round-trip + all-or-nothing import (RQ-23; ER-13)

Set-off / payment / reversal records are the audit trail of posted vouchers and **must** survive export/import in
JSON **and** XML. Fold in **in-slice** (never deferred — the recurring Io-bypass A10 class). Mirror the S6 fold-in
(`CanonicalModel.cs:319–401`, `CanonicalMapper.cs:236–279`, `CanonicalXml.cs:122–160`, `ImportPlan.cs:1017–1072`,
`ApplyJournal.cs:48–50/122–124/216–218`).

- **`CanonicalModel.cs`:** append `GstSetoffLineDto`, `ItcReversalDto`, `GstChallanDto`, `GstDrc03Dto` collections to
  `CanonicalPayload`; add the `GstAdjustmentKind` (enum-name string, default `null`/absent) to the entry-line tax DTO
  and `IsGstStatAdjustment` (bool, default false) to the voucher-type DTO. Money = paisa `long`; dates = ISO;
  enums = names.
- **`CanonicalMapper.cs`:** `Map*` (domain → DTO), **deterministically ordered** (setoff-lines by
  (period, voucher, credit_head, liability_head); reversals by (rule, period, id); challans by (deposit_date, id))
  so serialization is byte-stable (mirror `MapGstr2bSnapshot` ordering `:238–256`).
- **`CanonicalXml.cs`:** the XML twin (build + read + parse registration).
- **`ImportPlan.cs`:** stage all four collections in a **new numbered §16 block** after the S6 §15 block (`:1017`),
  recorded in the **`journal`** (mirror `ApplyJournal` record/rollback `:122–124/216–218`) so a rollback prunes them
  ⇒ **all-or-nothing** (`Applied = false` on any invariant failure). **All-or-nothing, NOT orphan-skip:** unlike the
  S6 2B lines (external portal data that import unconditionally), an S7 record is the audit of a **posted voucher
  that is always co-exported** — a dangling `reversal_voucher_id` / `source_voucher_id` / `voucher_id` signals a
  corrupt batch and **rejects the whole import** (a dropped reversal record would desync the credit-ledger audit from
  the posted voucher). Intra-batch cross-refs (`reclaim_of_id`, `drc03_id`) resolve via the re-mint map or reject.
  Re-map all voucher FKs through the import's voucher-id map (`MapVoucher`).
- **`CompanyImportService`:** the all-or-nothing mirror runs through `ImportPlan`; the `Rehydrate` invariant checks
  (balanced amounts, valid rule/head/bucket, UNIQUE idempotency key) gate a malformed batch so an Io import cannot
  introduce a reversal the engine would never have posted.

---

## 8. TDD test list (failing-first; ground in the four test projects)

Projects: `Apex.Ledger.Tests` (engine), `Apex.Ledger.Io.Tests` (round-trip), `Apex.Persistence.Sqlite.Tests`
(schema/migration), `Apex.Desktop.Tests` (UI — a later pass). Each test written **red first**, proven to fail pre-fix.

**Rule-88A set-off (RQ-21; ER-7)**
1. **Fixture (F) both splits** (`:1118–1122`): IGST out 1000 / CGST 300 / SGST 300, credit IGST 1300 / CGST 200 /
   SGST 200 → `CgstFirst` and the `SgstFirst`/override both give **cash = ₹0**; the ₹100 surplus lands in CGST credit
   (Option 1) or SGST credit (Option 2). Both reachable. **Assert LEGAL PROPERTIES, not memorized numbers**
   (A14-CONFIRMED §11.1 the order but NOT a Circular-98 closing-balance table): IGST-first-exhausted, head-purity
   walls, cess ring-fence, minimal residual cash, no CGST↔SGST cross-utilisation — the **engine computes its own
   deterministic worked example**, the test checks the invariants hold.
2. **IGST-first-exhausted:** IGST credit is fully consumed before any CGST/SGST credit is touched (a case where
   using CGST credit early would strand IGST credit is disallowed).
3. **CGST↔SGST cross-utilisation impossible:** no allocation line pairs CGST credit with SGST liability or vice-versa
   (engine output + `gst_setoff_lines` CHECK both reject).
4. **§49(5)(c)/(d) proviso:** SGST credit pays IGST only after CGST credit for IGST is exhausted.
5. **Cess ring-fence (ER-2):** cess credit discharges only cess liability; a cess surplus never pays CGST/SGST/IGST.
6. **RCM/interest cash-only (ER-3):** an RCM-only liability period pays entirely in cash, zero credit utilisation.
7. **Rule 86B (DP-17):** with the flag on + trigger met, ≥ 1% of output tax is forced to cash after set-off; with the
   flag off, no effect (ER-13).
8. **Editable Table 6.1 override validation:** a legal override re-allocates; an illegal override (IGST-not-exhausted
   / cross-head / cess-cross) is **rejected** (fail-fast), no posting.
9. **Determinism + paisa exactness:** the same demand yields the identical allocation across runs; Σ allocated + Σ
   cash == Σ liability to the paisa; Σ credit used + Σ closing == Σ opening credit.

**GST payment (RQ-22)**
10. **PMT-06 deposit + discharge:** a challan deposit posts `Dr Electronic Cash Ledger / Cr Bank`, records CPIN
    (CIN/BRN on payment) + (major,minor) head; the residual-cash discharge draws down the cash ledger per head; both
    vouchers balance; the challan links to the voucher.
11. **DRC-03 voluntary/off-return:** a DRC-03 payment posts the reversal-cause voucher + a `gst_drc03` record with the
    cause + per-head/minor amounts; §50 interest is flag-only (not computed).
12. **Cash-ledger minor-head isolation + credit-tax-only (A14-CONFIRMED §11.2):** cash deposited under `Tax` cannot
    discharge an `Interest`/`Penalty` head; **and** the credit ledger (PMT-02) cannot settle interest / penalty / fee /
    late-fee (tax-only, §49(4)/Rule 86(2)) — the poster **rejects** both violations (fail-fast).

**ITC-reversal engine (RQ-27; §17(5); DP-30/DP-31) — the SOLE poster**
13. **Fixture (H) Rule 37** (`:1130–1133`): ITC ₹18,000 on a >180-day-unpaid purchase → reversal ₹18,000 to Table
    **4(B)(2)** (reclaimable, §11.5), reduces the `Input {head}` (credit ledger), lands on DRC-03; on later payment
    ₹18,000 **re-availed** to **4(D)(1)** (reclaim linked). **Assert the S6 reconciler only *surfaced* the candidate;
    `ItcReversalService` posted it — ER-14.** **ECRS cap (§11.7):** a reclaim > the tracked reversal balance is
    **rejected** (fail-fast).
14. **Rule 37A:** supplier-non-payment candidate (from S6 `Section16_2aaNotInPortal`/InBooksOnly) → **30-Nov** reversal
    to Table **4(B)(2)** (note the **30-Sep** supplier cut-off is distinct); reclaim on supplier filing → **4(D)(1)**,
    capped at the tracked reversal balance.
15. **Rule 42:** monthly D1 + D2 reversal on common credit; annual true-up posts the delta (not a stacked second full
    reversal).
16. **Rule 43:** capital-goods 60-month tranche; each month posts once (schedule idempotent).
17. **§17(5)-blocked (`Section17_5Blocked`):** a blocked-motor-vehicle purchase's availed ITC reverses to Table
    4(B)(1); an `Eligible` purchase is untouched.
18. **`Section16_2aaNotInPortal` is a deferral, NOT a posted reversal:** confirming the ITC-gate hold posts nothing
    (until it escalates to Rule 37A) — a targeted no-post assertion.
19. **IMS-accepted credit note:** an accepted CN with a partial `DeclaredReversalPaisa` reverses that amount; a
    `NoReversalDeclared` CN reverses nothing; a full-tax CN reverses the full forward tax.

**Posting-correctness invariant (§5 — STRUCTURAL, the top-risk lens)**
20. **Balance:** every S7 voucher (set-off, deposit, payment, reversal, reclaim) satisfies `Σ Dr == Σ Cr`
    (`VoucherValidator`); an intentionally unbalanced allocation throws at post time.
21. **No pollution of existing figures:** Table 3.1 outward + Table 4(A) ITC are **byte-identical** before and after
    a full set-off + payment + reversal cycle (adjustment vouchers are `Journal`/`Payment` ⇒ excluded by
    `DirectionOf`).
22. **Idempotency / no-double-post:** re-running the set-off / reversal for a period does **not** create a second
    voucher; the UNIQUE `itc_reversals` key rejects a duplicate; a Rule-37 reclaim posts once.
23. **Paisa conservation end-to-end:** the electronic-ledger projection foots exactly after a mixed cycle.

**Schema / migration (v43→v44)**
24. `SchemaMigrationEquivalenceTests` green — `CreateV1` == the v43→v44 migrate-chain (byte-identical DDL).
25. Legacy-DDL upgrade: a v43 DB upgrades to v44, existing rows untouched, new tables empty, new columns 0/NULL.
26. `DowngradeToV11` / `DowngradeToV9` drop the four new tables + strip the two new columns; round-trip green.
27. The `gst_setoff_lines` **CHECK** rejects a CGST↔SGST and a cess↔non-cess row at the DB layer.

**Io round-trip (RQ-23; ER-13)**
28. JSON + XML round-trip **paisa + count exact** for setoff-lines, reversals, challans, drc03, plus the
    `GstAdjustmentKind` line tag + the `IsGstStatAdjustment` voucher-type flag.
29. `ImportPlan` **all-or-nothing:** a batch with one reversal whose `reversal_voucher_id` is dangling rejects the
    **whole** batch (`Applied = false`).

**ER-13 off + regression fixtures**
30. **ER-13:** a company that never sets off / pays / reverses serialises **byte-identical** to a v43 company (new
    tables empty, new columns 0/NULL, `Gstr3b` Table 6.1/4(B) fields zero, `Net*` unchanged).
31. **Robert** (accounts-only) + **Bright** (trading) + the Phase-4 base-GST GSTR-1/3B fixtures + the Phase-9 S1–S6
    fixtures stay green / byte-identical; the Phase-9 **(F)** set-off and **(H)** Rule-37 reversal fixtures reconcile
    to their postings.

---

## 9. Risks / subtleties (ranked)

1. **A posting slice corrupting the ledger / breaking paisa balance / double-posting a reversal (HIGHEST).** The
   whole slice mutates the ledger. Mitigated **structurally**: post only through `LedgerService.Post` (balance
   throw, §5.1); idempotency UNIQUE key on `itc_reversals` + period-keyed set-off (§5.3); **nothing auto-posts** —
   every reversal/set-off is a user-confirmed action (§0 fact 3); paisa-conservation + before/after byte-identity
   tests (§8: 20–23). A silent double-post or an unbalanced reversal is the failure mode that must be impossible.
2. **CGST↔SGST cross-utilisation leaking into set-off (ER-7).** Enforced twice — engine has no such branch +
   `gst_setoff_lines` CHECK (§1.2, §6.1) — with tests 3 + 27. A leak here is a statutory violation, not just a bug.
3. **IGST credit not fully exhausted first / wrong residual-IGST default (Rule 88A).** Deterministic Step-A-before-BC
   ordering + fixture (F) both splits (tests 1–2). **A14-CONFIRMED (§11.1):** IGST-first-and-fully-exhausted is
   portal-enforced; the residual CGST/SGST split is **discretionary/editable** — so the `CgstFirst` default is a
   compliant choice **plus** a mandatory override (DP-16), not a single hardcoded answer. Risk retired.
4. **Reversal double-count OR the reconciler auto-reversing (S6 advisory vs S7 sole poster, ER-14).** S6 posts
   nothing (its structural test stays green); S7 posts once, idempotently; `Section16_2aaNotInPortal` deliberately
   does not post (test 18); Rule-37 reclaim posts once (test 22). A drift here silently mis-states the credit ledger.
5. **Corrupting the existing GSTR-3B outward/ITC figures.** Guarded by `DirectionOf` excluding `Journal`/`Payment`
   adjustment vouchers (§0 fact 2, §5.4) + the additive-init-only Table 6.1/4(B) fields (§4.3) — test 21.
6. **Cess ring-fence in set-off + reversal (ER-2).** Cess credit only vs cess; cess reversal in its own head — engine
   + CHECK + tests 5.
7. **Electronic-ledger-as-projection vs the plan's named tables (R6 deviation).** The recommended projection design
   deviates from the plan's `electronic_credit_ledger`/`electronic_cash_ledger` table names — **must be recorded in
   `memory.md` and surfaced at the gate** (§1.1).
8. **Rule 86B interaction with set-off order (DP-17).** Applied *after* set-off, clawing credit back to cash — a
   config flag, off by default (test 7). Order-sensitivity is subtle.
9. **PMT-06 challan lifecycle + minor-head cash isolation.** CPIN (14-digit, 15-day) → cash credited only on CIN;
   cash can't cross minor heads (test 12); PMT-09 out of scope. **A14-CONFIRMED (§11.2/§11.3).** Added invariant:
   credit (PMT-02) settles **tax only** — interest/penalty/fee are cash-only (§49(4)/Rule 86(2)); the poster rejects
   a credit-settled interest/penalty/fee.
10. **Migration equivalence + downgrade for 4 tables + 2 ALTER columns + a CHECK constraint.** Equivalence test +
    legacy-DDL + both downgrade helpers (tests 24–27).

---

## 10. Build recommendation — SPLIT into S7a + S7b under ONE PR gate (mirrors S6a/S6b)

The slice is large (electronic-ledger projections + Rule-88A engine + PMT-06/DRC-03 posting + the four-rule reversal
engine + 4 tables + 2 ALTERs + Io). Recommend **two coherent conventional commits under the single PR-7 gate**:

- **S7a — set-off + payment spine (lands the WHOLE schema v44).** `ResidualIgstSplit`/`GstMinorHead`/
  `GstAdjustmentKind`/`ItcReversalRule` enums + `GstLineTax.Adjustment` + `is_gst_stat_adjustment` flag; the
  `ElectronicLedgersView` projection (RQ-20); `GstSetOffService` (pure Rule-88A `Allocate` + `PostSetOff`, RQ-21,
  ER-7); `GstDepositService` (PMT-06 + `GstChallan`) + `GstDrc03` (RQ-22); **all four v44 tables** (`gst_setoff_lines`,
  `itc_reversals`, `gst_challans`, `gst_drc03` — `itc_reversals` lands here even though its engine arrives in S7b:
  one version bump, tables can be empty, matching the "S6a lands schema" precedent) + `MigrateV43ToV44` + downgrade +
  Io fold-in; tests 1–12, 20–31.
- **S7b — ITC-reversal engine (the sole poster).** `ItcReversalService` consuming the S6 candidates + Rule
  42/43/37/37A + §17(5) posting to Table 4(B) + the reclaim path (RQ-27, DP-30/31, ER-14); `Gstr3b` Table 4(B)
  additive projection; tests 13–19 (+ tighten 22).

Both land behind **PR-7** with the full A10 3-lens pass (**posting-correctness the top lens** — balance, paisa
conservation, idempotency, no-pollution, cross-head impossibility), A12 commits, CI green across 3 OS. Advantage: the
set-off/payment spine (the math-heavy, ledger-mutating core) is reviewable in isolation before the reversal engine
piles on; the whole schema settles in S7a so S7b is logic-only.

---

## 11. Law points — **A14-CONFIRMED** (authoritative spec, folded in)

**A14 has returned its verdict (R7). Every ruling below is CONFIRMED and is now the authoritative S7 law spec — it is
NO LONGER provisional. Where a value is ⚠-flagged elsewhere in this brief, it resolves to the confirmed ruling here.**
These are the rules the engine and poster are built to; the earlier "verify in parallel" framing is retired.

### 11.1 Rule 88A / §49A set-off order — **A14-CONFIRMED**
- **IGST credit is used FIRST** — to IGST liability, then to CGST **&** SGST/UTGST liability in **any order / any
  proportion**. **IGST credit MUST be fully exhausted before any CGST or SGST credit is used.**
- **CGST credit → CGST liability, then IGST liability — NEVER SGST.**
- **SGST/UTGST credit → SGST/UTGST liability, then IGST liability — NEVER CGST.**
- **Cess credit ONLY against Cess liability** (proviso to §11, Compensation Cess Act) — the ER-2 ring-fence is
  **law-confirmed**, not merely a design choice.
- The portal **HARD-ENFORCES** the IGST-first-and-fully-exhausted rule; but the **intra-CGST/SGST split of the
  residual IGST credit is DISCRETIONARY / editable** on the portal. ⇒ the engine **computes a compliant default**
  (DP-16, `ResidualIgstSplit.CgstFirst`) **but MUST allow override** via the editable Table 6.1 — both the default and
  a user split are legal. Steps A→B→C→D of §2.2 encode exactly this.

### 11.2 Electronic ledgers (§49 + Rules 85/86/87) — **A14-CONFIRMED, HARD INVARIANTS**
- **Cash ledger = PMT-05** — a **2-D matrix**: MAJOR head {IGST, CGST, SGST/UTGST, Cess} × MINOR head
  {Tax, Interest, Penalty, Fee, Other}. Cross-head cash movement is possible **only via PMT-09** (out of scope — §11.8).
- **Credit ledger = PMT-02** — payable for **TAX ONLY**; **never** interest / penalty / fee / late-fee (those are
  **cash-only**; **§49(4) / Rule 86(2)**).
- **Liability ledger = PMT-01** — Part-I (return liabilities) / Part-II (other liabilities).
- The **TAX-ONLY credit rule** and the **minor-head cash isolation** are **HARD INVARIANTS the poster MUST reject
  violations of** (fail-fast, never silent-fix). Tests: 12 + a new "credit cannot settle interest/penalty/fee" guard.

### 11.3 PMT-06 (challan) / DRC-03 (voluntary) — **A14-CONFIRMED**
- **PMT-06** = the challan that **deposits into the cash ledger**. **CPIN** (14-digit, **15-day validity**) is
  generated on creation; the **cash ledger is credited ONLY on CIN** (the bank-credit reference). PMT-06 is also the
  **QRMP M1 / M2** monthly challan.
- **DRC-03** = voluntary / self-ascertained payment (**Rule 142(2)/(3)**), payable **from cash and/or credit subject
  to the tax-only rule** (§11.2 — credit can settle only the tax component).
- **DRC-03A** (Nov-2024, **Notn 12/2024-CT**) **links a DRC-03 payment to a confirmed demand** — this is exactly the
  `drc03a_demand_ref` field (§6.2 / §6.3).

### 11.4 Reversal-rule formulas — **A14-CONFIRMED**
- **Rule 42** (inputs & input services): **C1 = T − (T1 + T2 + T3)**; **C2 = C1 − T4**; **D1 = (E ÷ F) × C2**;
  **D2 = 5% × C2**; monthly **reversal = D1 + D2**. **Annual true-up** is recomputed on the **full-year E ÷ F** by the
  **September-following return / GSTR-9** (interest on any shortfall).
- **Rule 43** (capital goods): **Tm = Tc ÷ 60**; **Te = (E ÷ F) × Tr**; reverse **Te each month over 60 months**.
- **Rule 37** (180-day non-payment): proportionate reversal **+ §50 interest**; **reclaim on later payment — NO time
  bar**.
- **Rule 37A** (supplier non-filing): invoice appears in 2B but the supplier has **NOT filed GSTR-3B by 30-Sep
  following the FY** ⇒ recipient **reverses by 30-Nov** ⇒ **reclaim when the supplier subsequently files.**
  **NOTE the TWO distinct dates** — **30-Sep** (supplier cut-off) is not **30-Nov** (recipient reversal deadline).

### 11.5 GSTR-3B Table-4 routing — **A14-CONFIRMED** (GSTN advisory 01-Sep-2022)
- **Non-reclaimable** reversals — Rules **38 / 42 / 43** + **§17(5)** → **Table 4(B)(1)**.
- **Reclaimable** reversals — Rule **37 / 37A** → **Table 4(B)(2)**.
- **Reclaim** of an earlier reversal → **Table 4(D)(1)** (and pulled back into net ITC via **Table 4(A)(5)**).
- ⇒ `itc_reversals.table4b_bucket`: Rule37/37A ⇒ **4(B)(2)**; Rule42/43/§17(5) (and Ineligible-if-availed) ⇒
  **4(B)(1)**; reclaim rows (via `reclaim_of_id`) ⇒ **4(D)(1)**. The `GstAdjustmentKind` line-tag drives the Table-4
  projection to exactly these buckets (§4.3).

### 11.6 CORRECTION — §50(3) interest rate = **18% p.a.** (NOT the outdated 24%)
**Record prominently:** interest on **wrongly-availed-AND-utilised ITC** is **18% p.a.** — **NOT** the **24%** some
stale sources quote — per **Notification 09/2022-CT + Rule 88B + Circular 192/04/2023**. S7 keeps **§50 interest
flag-only** (DP-34, not auto-computed), **but if a rate is ever surfaced anywhere it MUST be 18%.** Any 24% constant is
a defect.

### 11.7 Watch-outs A14 surfaced (the build MUST honor these)
- **ECRS — Electronic Credit Reversal & Re-claimed Statement:** the portal **HARD-VALIDATES that a Table 4(D)(1)
  reclaim cannot exceed the tracked reversal balance.** ⇒ the reversal engine (S7b) **MUST maintain a running per-head
  reversal balance** (the `itc_reversals` rows netted by their `reclaim_of_id` reclaims) so it **never advises an
  un-fileable reclaim.** A reclaim that would overdraw the tracked balance is **rejected** (fail-fast) — a new test
  asserts a reclaim > outstanding reversal balance is refused. See §5.3.
- **GST-2.0 cess-credit lapse — OPEN law question:** with Compensation Cess sunset (S1's three dated windows), the
  **future utilisability of accumulated cess ITC is legally UNSETTLED.** Flag this as an **OPEN law question** on the
  cess set-off path. **Cess-only-against-cess holds TODAY** (§11.1 / ER-2) — do **NOT** build a lapse / write-off path
  yet; carry the open question forward for a later slice / A14 re-check.

### 11.8 Still-open / accepted-as-designed (A14 did not overturn)
- **Rule 86B (1% cash cap, DP-17):** A14 did not return a new ruling this pass — retain the config-flag design
  (> ₹50 L monthly-taxable trigger, applied after set-off, **off by default ⇒ ER-13**); the exact trigger/exemption
  list stays as-designed pending any later A14 note.
- **§50 interest flag-only (DP-34):** confirmed acceptable for Phase 9 — with the **18% rate** (§11.6) if ever surfaced.
- **PMT-09 cross-head cash transfer:** out of scope for S7 (§11.2) — a deposit is drawable only within its own
  (major, minor) cell.

Adjacent carry-forward (non-blocking): the **§206C TCS base may need to include Compensation Cess** when
`BaseIncludesGst=true` (a CBDT question flagged in memory from S1) — tangential to S7 but in the same tax-law pass.

---

### Appendix — file manifest (create ● / edit ○)

- ● `src/Apex.Ledger/Services/{GstSetOffService,GstDepositService,ItcReversalService}.cs`
  (pure `Allocate` + posters; consume the S6 `ItcGateView.ReversalCandidates`).
- ● `src/Apex.Ledger/Reports/ElectronicLedgersView.cs` (credit/cash/liability projection, RQ-20).
- ○ `src/Apex.Ledger/Reports/Gstr3b.cs` (additive init-only **Table 6.1** utilisation + **Table 4(B)** reversal
  fields, default zero — mirror the S2 RCM-bucket pattern at `:34–56`; existing `Net*` unchanged, ER-13).
- ● `src/Apex.Ledger/Domain/{GstChallan,GstDrc03,GstSetoffLine,ItcReversal}.cs` (value-objects-with-identity +
  `Rehydrate`, mirror `TdsChallan.cs`); new enums in `GstEnums.cs` (`ResidualIgstSplit`, `GstMinorHead`,
  `GstAdjustmentKind`, `ItcReversalRule`) ○; `GstLineTax.cs` (add nullable `Adjustment`) ○.
- ○ `src/Apex.Ledger/Domain/Company.cs` (four new collections + Add/Remove/Find, mirror `:39–41/438–448/671–699`);
  `VoucherType.cs` (add `IsGstStatAdjustment`, mirror `IsStatPayment` `:137–140`).
- ○ `src/Apex.Ledger/Services/GstService.cs` (lazy `EnsureElectronicCashLedger()` + reversal-cost ledgers, mirror
  `EnsureAdvanceTaxSuspenseLedger` `:258–277`); reuse `LedgerService.Post`, `VoucherValidator`.
- ○ `src/Apex.Ledger.Io/{CanonicalModel,CanonicalMapper,CanonicalXml,ImportPlan,ApplyJournal,CompanyImportService}.cs`
  (four DTOs + the line/voucher-type tags; ImportPlan §16 all-or-nothing; journal rollback).
- ○ `src/Apex.Persistence.Sqlite/{Schema.cs (v44 + MigrateV43ToV44 + CreateV1 mirror + CHECKs),
  SqliteCompanyStore.cs (== 43 step, Read*/Insert* + entry_lines/voucher_types column read/write)}`.
- ○ Tests: `Apex.Ledger.Tests` (set-off/payment/reversal engines), `Apex.Ledger.Io.Tests` (round-trip + all-or-
  nothing), `Apex.Persistence.Sqlite.Tests` (`InventoryVoucherRoundTripTests.DowngradeToV9`,
  `ItemInvoiceRoundTripTests.DowngradeToV11`, `SchemaMigrationEquivalenceTests`, a new `GstSetOffSchemaTests`),
  `Apex.Desktop.Tests` (later UI pass).
