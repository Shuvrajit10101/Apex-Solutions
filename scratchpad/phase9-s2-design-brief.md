# Phase 9 · Slice S2 — RCM (reverse charge) + self-invoice/payment voucher + §34 Credit/Debit Notes + GST-on-advances (schema v38→v39)

**Design-only brief for the implementing engine agent.** Grounded in the actual code as it exists on
`claude/apex-phase-9-start-51d103` (worktree `wonderful-hellman-59520a`), schema head **v38** after S1
(`2980b86`). Governs **RQ-3, RQ-7, RQ-8, RQ-11 (import-of-services part), RQ-24, RQ-25**; **ER-3
(RCM dual-leg, cash-only), ER-8, ER-9, ER-10, ER-11, ER-12 (§34 CDN link + direction + 30-Nov guard),
ER-13**. PR-2 gate per requirements §5 "P9-S2". **ER-16/RQ-30 connectivity is NOT in S2.** **Do NOT run git.
This is a build spec, not an implementation.**

> **Design north star (same as S1):** every change is *additive, date-aware, and byte-identical when off*.
> A company that never uses RCM / CDN-GST / advances (no rcm_categories, no RCM-tagged lines, no cdn-links,
> no advance-receipts) must resolve, post, persist and serialise **byte-identically to a v38 company**
> (ER-13). Guarantees: (a) never alter an existing column/table/DTO field/ordering; (b) new tables start
> empty, new columns default 0/NULL, new DTO members default `[]`/false/null; (c) the RCM/CDN/advance paths
> are *opt-in* — absent a category master + a per-master RCM flag (RCM), a cdn-link (CDN), or a service-advance
> receipt (advances), `ComputeInvoiceTax` and the GSTR projections behave exactly as Phase-4/8/S1 do today.
>
> **Reuse posture:** S2 does **not** fork the engine. RCM reuses `ComputeInvoiceTax`'s total-then-split +
> `RoundToPaisa` + the S1 dated `ResolveRate`/`ResolveCess`; §34 CDN reuses the existing
> `VoucherBaseType.CreditNote`/`DebitNote` + `ComputeInvoiceTax`; advances reuse the `Receipt`/`Payment` base
> types; the self-accounting mirrors the Phase-7 `TdsService` (a self-computed tax that lands in its **own
> payable** and is later discharged/claimed).

---

## 0. Files you will touch (paths relative to worktree root)

| Layer | File | What |
|---|---|---|
| Domain enums | `src/Apex.Ledger/Domain/GstEnums.cs` | +`RcmStream`, +`RcmItcScheme`, +`CdnType`, +`RcmDocumentKind` |
| Domain masters | `src/Apex.Ledger/Domain/RcmCategory.cs` (**new**) | dated notified-category master (mirror `NatureOfPayment.cs` + `GstRateHistoryEntry.cs`) |
| Domain records | `src/Apex.Ledger/Domain/RcmDocument.cs` (**new**), `src/Apex.Ledger/Domain/GstCreditDebitNoteLink.cs` (**new**), `src/Apex.Ledger/Domain/GstAdvanceReceipt.cs` (**new**) | voucher-linked value-objects-with-identity (mirror `TdsChallan.cs`) |
| Domain config | `src/Apex.Ledger/Domain/GstConfig.cs` | +`RcmCategories` list + `AddRcmCategory` (mirror `RateHistory`/`CessRates` from S1) |
| Domain item/ledger GST | `src/Apex.Ledger/Domain/StockItemGstDetails.cs` | +`ReverseChargeApplicable`, +`GtaForwardCharge`, +`RcmCategoryId` (shared by item `.Gst` **and** ledger `.SalesPurchaseGst`) |
| Domain party GST | `src/Apex.Ledger/Domain/PartyGstDetails.cs` | +`IsPromoter` (§9(4)), +`IsBodyCorporate` (recipient/supplier qualifier) |
| Domain line detail | `src/Apex.Ledger/Domain/GstLineTax.cs` | +`IsReverseCharge` (default false), +`RcmScheme` (`RcmItcScheme?`) — the report bucketing tag |
| Domain ledger class. | `src/Apex.Ledger/Domain/LedgerGstClassification.cs` | +`IsReverseCharge` (default false) — distinguishes the RCM Output liability ledgers |
| Domain voucher-type | `src/Apex.Ledger/Domain/VoucherType.cs` | +`IsRcmPaymentVoucher` flag (mirror `IsStatPayment`, line 137) |
| Domain aggregate | `src/Apex.Ledger/Domain/Company.cs` | +`RcmDocuments`/`CreditDebitNoteLinks`/`AdvanceReceipts` collections + `Add*`/`Find*` (mirror `_tdsChallans`, lines 32/395/518) |
| Seed | `src/Apex.Ledger/Seed/SeedRcmCategories.cs` (**new**) | `BuildDefaults()` — 13/2017, 10/2017, 4/2017, 7/2019 dated categories (mirror `SeedGstRates`/`SeedNaturesOfPayment`) |
| Engine | `src/Apex.Ledger/Services/GstService.cs` | `FindTaxLedger` disambiguation; RCM Output-ledger naming + lazy creation; `IsReverseCharge` threading through `AddHead` |
| Engine (new) | `src/Apex.Ledger/Services/RcmService.cs` (**new**) | applicability + dual-leg + time-of-supply + self-invoice/payment docs (mirror `TdsService`/`TdsDepositService`) |
| Engine (new) | `src/Apex.Ledger/Services/CreditDebitNoteService.cs` (**new**) | §34 CDN build + original-invoice link + §34(2) guard + 9B feed |
| Engine (new) | `src/Apex.Ledger/Services/AdvanceReceiptService.cs` (**new**) | advance receipt/refund + services-only advance tax + adjustment + 11A/11B |
| Reports | `src/Apex.Ledger/Reports/GstReportSupport.cs` | RCM-aware line projection (exclude RCM/CDN sign correctly, §1.6) |
| Reports | `src/Apex.Ledger/Reports/Gstr3b.cs` | 3.1(d) RCM outward + 4A(2)/4A(3) RCM ITC; sign-aware CDN |
| Reports | `src/Apex.Ledger/Reports/Gstr1.cs` | Table 9B (CDN) + Table 11A/11B (advances) + 4B (outward-RCM supply, light) |
| Schema | `src/Apex.Persistence.Sqlite/Schema.cs` | `CurrentVersion`→39; 4 new tables in `CreateV1` **and** new `MigrateV38ToV39`; ALTERs on `stock_items`/`ledgers`/`entry_lines`/`voucher_types` |
| Store | `src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs` | `EnsureSchema` step `version==38`; read/write of the 4 tables + new columns |
| Io model | `src/Apex.Ledger.Io/CanonicalModel.cs` | +`RcmCategoryDto`, +`RcmDocumentDto`, +`GstCdnLinkDto`, +`GstAdvanceReceiptDto`; extend `GstConfigDto`, `StockItemGstDto`, `PartyGstDto`, `GstLineTaxDto`, ledger-classification DTO |
| Io mapper/xml/plan | `src/Apex.Ledger.Io/{CanonicalMapper,CanonicalXml,ImportPlan}.cs` | map/serialise/rebuild the new DTOs |
| Io import guards | `src/Apex.Ledger.Io/CompanyImportService.cs` | mirror the RCM cash-only + §34(2) + services-only guards all-or-nothing |
| Tests | `tests/Apex.Ledger.Tests/*`, `tests/Apex.Persistence.Sqlite.Tests/RcmCdnAdvanceSchemaTests.cs` (**new**), `tests/Apex.Ledger.Io.Tests/*`, `tests/Apex.Persistence.Sqlite.Tests/ItemInvoiceRoundTripTests.cs` (Downgrade helper) | see §6 |

`CanonicalJson.cs` needs **no code change** (S1 note holds — `DefaultIgnoreCondition.Never` emits every DTO
member; new members default `[]`/false/null so an off company serialises identically).

---

## 1. RCM — reverse charge (RQ-3, RQ-7, RQ-8, RQ-11; ER-3)

### 1.1 The notified-category master `rcm_categories` (dated, seeded, on `GstConfig`)
A companion dated master hung off `GstConfig` **exactly like S1's `RateHistory`/`CessRates`** (so the wiring is
already proven: `_rcmCategories` list + `IReadOnlyList<RcmCategory> RcmCategories` + `AddRcmCategory`). Domain
`RcmCategory` mirrors `NatureOfPayment.cs` (a seeded dated master) fused with `GstRateHistoryEntry.cs`'s
`IsEffectiveOn`:

```csharp
public sealed class RcmCategory {              // immutable, framework/DB-agnostic
    public Guid Id { get; }
    public string Notification { get; }        // "13/2017-CT(R)" | "10/2017-IGST(R)" | "4/2017-CT(R)" | "7/2019-CT(R)"
    public RcmStream Stream { get; }           // Section9_3 (notified) | Section9_4 (unregistered→notified recipient)
    public string SupplyNature { get; }        // "GTA" | "Legal" | "Director" | "Sponsorship" | "Security" | "Renting-commercial" | "Cement" | ...
    public GstSupplyType SupplyType { get; }    // Goods (HSN) | Services (SAC) — reuse the existing enum
    public string? HsnSac { get; }             // set for goods categories (cement 2523); null for service categories
    public int RateBasisPoints { get; }        // integrated RCM rate (e.g. 1800 = 18%, GTA 500)
    public RcmParty SupplierQualifier { get; } // Any | Unregistered | NonBodyCorporate
    public RcmParty RecipientQualifier { get; }// Any | BodyCorporate | RegisteredPerson | Promoter
    public DateOnly EffectiveFrom { get; }
    public DateOnly? EffectiveTo { get; }       // null = open-ended
    public string Label { get; }
    public bool IsPredefined { get; }
    public bool IsEffectiveOn(DateOnly d) => d >= EffectiveFrom && (EffectiveTo is null || d <= EffectiveTo);
}
```
`RcmStream` and `RcmParty` are new enums (in `GstEnums.cs`). **Cement is date-effective**: rather than dating
the RCM *rate* on the category, prefer resolving the cement RCM rate through the **S1 dated rate history** by
HSN `2523` (28% ≤ 21-Sep-2025, 18% from 22-Sep-2025) via `GstService.ResolveRate(item, ledger, tosDate)` —
this is the whole point of the S1 seam, and keeps a single source of truth for HSN rates. Service categories
(no HSN) carry their rate on the category row and may use dated rows if a rate changes (e.g. GTA's GST-2.0
change: seed one row `EffectiveTo = 2025-09-21` and one `EffectiveFrom = 2025-09-22`). **A14 re-verifies every
notification number, rate and effective date at build (R7).**

**Seed** `SeedRcmCategories.BuildDefaults()` (fresh ids each call, mirror `SeedGstRates.BuildDefaultRateHistory`):
GTA (5% no-ITC / Annexure-V FC branch), legal/advocate, arbitral, sponsorship, director, security (non-body-
corporate supplier → body-corporate recipient), motor-vehicle renting to a body corporate (5%), renting of
commercial immovable property (18%, Notn 09/2024 from 10-Oct-2024), copyright (author/artist); §9(4) promoter
rows (Notn 7/2019): cement-from-unregistered (rate via HSN `2523` dated history), the <80%-shortfall row
(18%), capital-goods-from-unregistered. Seeded **only** via the advanced-GST opt-in — mirror S1: add a
`SeedAdvancedGst` block (GstService.cs:131) `if (config.RcmCategories.Count == 0) foreach (…) config.AddRcmCategory(…)`.
`EnableGst` stays byte-identical (ER-13).

### 1.2 RCM flags on the masters (RQ-3)
- **`StockItemGstDetails`** (the shared type covering `StockItem.Gst` **and** `Ledger.SalesPurchaseGst`, so
  one edit covers a stock item *and* a purchase/expense ledger — the same trick S1 used for cess): add
  `bool ReverseChargeApplicable`, `bool GtaForwardCharge` (Annexure-V forward-charge opt-out), `Guid? RcmCategoryId`
  (the notified category / nature-of-supply link; nullable, no FK — mirror `tds_nature_id`). All default
  false/null ⇒ byte-identical when off.
- **`PartyGstDetails`**: add `bool IsPromoter` (real-estate §9(4) promoter) and `bool IsBodyCorporate` (drives
  the recipient/supplier qualifier match). `RegistrationType` already distinguishes registered vs unregistered.
- **Applicability resolution** (pure, in `RcmService`): RCM fires when the expense/purchase ledger-or-item's
  `ReverseChargeApplicable` is set **and** a `RcmCategory` matches on stream + supplier/recipient qualifiers +
  effective-on(tosDate). `§9(4)` fires **only** when `RecipientQualifier == Promoter` **and** the company/party
  `IsPromoter` — so the default (no promoter profile) leaves §9(4) OFF (PR-2 gate: "§9(4) fires only for the
  promoter profile"). GTA with `GtaForwardCharge` on ⇒ **no** RCM (supplier charges forward at 18%).

### 1.3 The dual-leg posting (RQ-7; ER-3) — the core engine change
On an **RCM inward** supply the supplier value carries **no supplier-charged tax**; the recipient
self-accounts a **balanced pair** *added on top of* the ordinary purchase legs. Worked example — an inward
service ₹10,000, IGST @18% (inter-state / import-of-services):

```
Dr  Purchase/Expense            10,000     ] ordinary purchase legs (already balance on their own)
Cr  Party (supplier)            10,000     ]   — supplier charges ZERO tax
Cr  RCM Output IGST              1,800      ] the RCM self-accounting pair (balances on its own, additive)
Dr  Input IGST (ITC)            1,800      ]   — Σ Dr = Σ Cr overall (11,800 = 11,800)
```

- **Output liability leg → a DEDICATED ledger** `"RCM Output CGST"` / `"RCM Output SGST"` / `"RCM Output IGST"`
  (and `"RCM Output Cess"` if the RCM line bears cess, §1.7). These are **NOT** the normal Output ledgers:
  they sit under `Duties & Taxes`, carry `LedgerGstClassification(head, Output, isReverseCharge: true)`, and are
  the **cash-only** liability (§2(82)/§49(4)) — excluded from "output tax" and never settled from the credit
  ledger. Create them **lazily** (mirror S1's `EnsureCessLedgers`, GstService.cs:117): a
  `RcmService.EnsureRcmOutputLedgers(head)` called only when an RCM line is about to post. `TaxLedgerName`
  (GstService.cs:34) gains an `isReverseCharge` overload returning `"RCM Output {CGST|SGST|IGST|Cess}"`.
- **Input ITC leg → the NORMAL `Input {head}` ledger**, because RCM ITC is ordinary downstream credit. It is
  distinguished for the return **only by the line tag** (§1.4), not by a separate ledger — this avoids a second
  set of ledgers and the `FindTaxLedger` ambiguity below. (Blocked-credit / ineligible-RCM-ITC is S6.)
- **`FindTaxLedger` disambiguation (critical):** the existing `FindTaxLedger(head, direction)` (GstService.cs:88)
  matches on head+direction only; with RCM Output ledgers now also `(head, Output)`, it becomes ambiguous. Add
  `&& c.IsReverseCharge == false` to its predicate so it **always** returns the ordinary ledger, and add a
  distinct `FindRcmOutputLedger(head)` that filters `IsReverseCharge == true`. Miss this and a normal sale could
  post to the RCM ledger.
- **POS split:** the pair is computed with the **same `ComputeLineTax` total-then-split** — intra ⇒
  CGST+SGST (each half), inter/import-of-services ⇒ IGST — so the RCM output pair and the ITC pair foot to the
  paisa by construction (L-4). Place of supply is resolved as today (`IsInterState`, GstService.cs:171).
- **Cash-only invariant in S2:** the electronic credit/cash ledgers + Rule-88A set-off are **S7**. In S2 the
  invariant is **structural**: the RCM output liability lives in its own `RCM Output {head}` ledger, distinct
  from every normal Output ledger and from the credit side, and there is no mechanism in S2 to net it against
  ITC. The test asserts the liability lands in the RCM ledger (never the normal Output ledger) and is
  discharged only by a cash Payment (Dr `RCM Output {head}` / Cr Bank). Full "excluded from Rule-88A" enforcement
  is an S7 carry-forward (note it).

### 1.4 Line-level tag for the returns (`GstLineTax`)
Extend `GstLineTax.cs` (immutable) with two **optional, defaulted** members so every existing construction stays
valid and an off company is byte-identical:
```csharp
public bool IsReverseCharge { get; }         // default false
public RcmItcScheme? RcmScheme { get; }      // null | ImportOfServices (→4A(2)) | OtherRcm (→4A(3))
```
- The **RCM Output** line carries `IsReverseCharge = true` (scheme null — it is a liability, not ITC).
- The **RCM Input ITC** line carries `IsReverseCharge = true` + `RcmScheme = ImportOfServices | OtherRcm`.
- `RcmScheme` is set from the transaction: import-of-services ⇒ `ImportOfServices`; every other RCM ⇒ `OtherRcm`.
- The tag is what `Gstr3b`/`GstReportSupport` read to bucket 3.1(d) / 4A(2) / 4A(3) — **pure projection, never
  recompute** (ER-9), exactly like the S1 cess ring-fence reads the `Cess` head off the line.

`AddHead` (GstService.cs:504) gains an `isReverseCharge`/`scheme` pass-through so it stamps the `GstLineTax`.
Keep the existing `AddHead` signature working (defaulted params) so the S1 cess call sites are unchanged.

### 1.5 Self-invoice (Rule 47A) + payment voucher (Rule 52) (RQ-8) — decision
**Design decision (state explicitly; the requirements author's "flag/sub-type on existing base types" guidance
double-counts if taken literally for the self-invoice):**
- The **RCM inward transaction is one Purchase voucher** that books the §1.3 dual leg. That single voucher IS the
  accounting; there is **no separate self-invoice accounting voucher** (a second Sales voucher would double the
  tax). To avoid the double-count trap, the **self-invoice (Rule 47A) is a generated document record**, not an
  accounting voucher — a `RcmDocument { Kind = SelfInvoice }` in the new `rcm_documents` table (own consecutive
  number series per Rule 47A, its own date constrained to **≤ 30 days** of the receipt date, and a link to the
  source purchase voucher). It only fires when the supplier is **unregistered** (a registered §9(3) supplier
  already issues the invoice → **no** self-invoice; `RcmService` branches on the party GSTIN, RQ-8). This mirrors
  the S4 IRN-record pattern and the `TdsChallan` voucher-linked-record pattern (`Company._tdsChallans`,
  `LinkChallanToVoucher`).
- The **payment voucher (Rule 52)** is issued at **supplier payment** — a genuinely separate accounting event
  (Dr Party / Cr Bank), so it **is** an ordinary `Payment` voucher, optionally flagged
  `VoucherType.IsRcmPaymentVoucher` (mirror `IsStatPayment`, VoucherType.cs:137 — no new base type, so every
  exhaustive base-type switch and `DirectionOf` are untouched). Its Rule-52 document metadata may also be captured
  as a `RcmDocument { Kind = PaymentVoucher }` for the printed document + series.
- **Self-invoice numbering + 30-day window** live on the `RcmDocument` record (a per-company running number,
  assigned by `RcmService`). The 30-day check is a pure guard: `selfInvoiceDate <= receiptDate.AddDays(30)` else
  a warn/block (mirror the §34(2) guard, §2.4).

### 1.6 Time of supply (30/60-day) (RQ-7)
A pure static helper `RcmService.TimeOfSupply(GstSupplyType kind, DateOnly? invoiceDate, DateOnly? receiptDate,
DateOnly? paymentDate)` returning the crystallisation date = **earliest of** {payment date, receipt/invoice
date, the **31st day** from receipt for **goods** §12(3) / the **61st day** from invoice for **services**
§13(3)}. This date drives (a) which tax period the RCM liability falls in and (b) the S1 dated rate/cess
resolution (`ResolveRate(item, ledger, tosDate)`, `ResolveCess(…, tosDate, qty)`). **Shared verbatim** with
`AdvanceReceiptService` (§3.4) — put it on `RcmService` (static) and call it from both, or a small shared
`GstTimeOfSupply` helper. Pure, clock-free, deterministic.

### 1.7 Import of services vs goods (RQ-11) + cess-on-RCM interaction
- **Import of SERVICES = RCM (IGST always inter-state):** `RcmScheme = ImportOfServices` → GSTR-3B **4A(2)**.
  Model an `IsImportOfServices` marker on the transaction (a param to `RcmService.BuildReverseCharge`, or derived
  from a party flag). Force IGST.
- **Import of GOODS is NOT RCM:** the engine must **never** raise an RCM leg for import-of-goods (IGST is paid
  at customs on the Bill of Entry → GSTR-3B **4A(1)**, out of S2's dual-leg scope). Guard: `RcmService` refuses
  to build a dual leg when the supply is import-of-goods (a hard fail-fast, ER-5 style) — the test asserts no
  RCM line is produced.
- **Cess on an RCM line:** an RCM inward line may itself bear Compensation Cess (rare — e.g. a cess-bearing
  imported good under a promoter scenario). Reuse S1's `ResolveCess(item, ledger, tosDate, qty)`; the RCM cess
  liability posts to `"RCM Output Cess"` and the RCM cess ITC to `"Input Cess"`, both tagged `IsReverseCharge`.
  The **cess ring-fence (ER-2) still holds** — RCM cess never mingles with CGST/SGST/IGST, and the report
  excludes it from the RCM CGST/SGST/IGST buckets exactly as S1 excludes it from `InvoiceTaxableValue`
  (GstReportSupport.cs:107). Keep it in scope but note it is the least-exercised path.

### 1.8 Reports — DirectionOf & the RCM buckets (RQ-7)
RCM breaks the 1:1 base-type→direction map (a **Purchase** now yields an **Output** liability). **Do not change
`DirectionOf`'s signature** (many callers). Instead:
- Add `GstReportSupport.RcmLines(company, from, to)` — a pure projection enumerating every posted line with
  `GstLineTax.IsReverseCharge == true`, yielding `(voucher, head, amount, IsOutputLiability, RcmScheme)`. A line
  posting to an `IsReverseCharge` **classification** ledger is the output liability (→ **3.1(d)**); a line on a
  normal Input ledger tagged RCM is the ITC (→ **4A(2)** import / **4A(3)** other).
- **Critically, the normal projections must EXCLUDE RCM-tagged lines** so they are not double-counted:
  - `Gstr3b.ReadSide(Output)` must not pick up the RCM Output lines (they are on Purchase vouchers, so
    `DirectionOf` already excludes them — but assert it), and its 4A(5) "all other ITC" must **subtract/exclude**
    the RCM-tagged Input lines (they belong in 4A(2)/4A(3)). This mirrors the S1 cess exclusion pattern exactly
    (GstReportSupport.cs:99-113, Gstr1.cs:190).
  - Add `Gstr3b` fields: `RcmOutwardCgst/Sgst/Igst/Cess` (3.1(d)) and `RcmItcImportIgst` (4A(2)) +
    `RcmItcOtherCgst/Sgst/Igst/Cess` (4A(3)).
- **GSTR-1 4B** (the *outward*-RCM-supply case — the company makes a supply on which the **recipient** pays RCM,
  so the invoice carries zero tax but must be flagged) is light in S2: a Sales voucher whose sales/expense ledger
  is flagged `ReverseChargeApplicable` on the **outward** side posts no tax and lands in a `Gstr1` 4B bucket.
  Keep this minimal (a value bucket) unless A14 finds it needs the full row shape.

---

## 2. §34 Credit / Debit Notes (RQ-24; ER-12) — decision + build

### 2.1 Reuse decision (DP-27 resolution — state it)
The codebase **already** has `VoucherBaseType.CreditNote`/`DebitNote` and `GstReportSupport.DirectionOf` maps
CreditNote→Output, DebitNote→Input (GstReportSupport.cs:26-27). Phase-4 can post a plain credit/debit note
through `ComputeInvoiceTax`, **but** it lacks the three §34 essentials (confirm at kickoff — the requirement
§2.9 flags exactly this gap): the **original-invoice link**, the **§34(2) 30-Nov guard**, and the **Table 9B
feed**, and the **directional sign** on the returns. **Decision: REUSE the existing CreditNote/DebitNote base
types + `ComputeInvoiceTax` (no new base type); ADD the link record, the guard, the sign-aware projection, and
the 9B table.** This is the "first-class §34 GST document" formalised as a link over the existing return voucher
— *not* silently inherited.

### 2.2 The link record `gst_cdn_links`
Domain `GstCreditDebitNoteLink` (value-object-with-identity, mirror `TdsChallan.cs`), hung on `Company` as a
collection (`_cdnLinks` + `CreditDebitNoteLinks` + `AddCreditDebitNoteLink`/`Find…`):
```csharp
public sealed class GstCreditDebitNoteLink {
    public Guid Id { get; }
    public Guid CdnVoucherId { get; }               // the credit/debit-note voucher
    public CdnType CdnType { get; }                 // Credit | Debit
    public Guid? OriginalInvoiceVoucherId { get; }  // the linked original invoice (null ⇒ consolidated-party ref)
    public string? OriginalInvoiceNumber { get; }   // denormalised for 9B when the voucher link is unavailable
    public DateOnly? OriginalInvoiceDate { get; }   // drives the §34(2) FY basis
    public string ReasonCode { get; }               // "01 sales return" | "02 post-supply discount" | "03 deficiency" | ...
    public bool Is9BTarget { get; }                 // registered CDN (9B) vs unregistered
}
```
The **§34(2) cutoff is computed**, not stored: `deadline = 30-Nov of the FY *following* the original supply's
FY` (reuse `TdsService.FinancialYearOf`, TdsService.cs:125, or a local copy). A credit note dated after the
deadline that *reduces* liability is blocked/warned (§2.4).

### 2.3 Directional output-tax adjustment + sign on the returns (the trickiest correctness point)
A **credit note reduces** the supplier's output liability; a **debit note increases** it. In the ledger this
means the credit note posts Output GST on the **Debit** side (reducing the Output ledger's credit balance),
opposite a sale. `ComputeInvoiceTax(direction: Output)` posts tax on the **Credit** side (correct for a sale).
So `CreditDebitNoteService` must build the CDN voucher with the tax legs on the **reducing side** for a credit
note (and the increasing side for a debit note). Two coupled changes:
1. **Posting:** the CDN service assembles the voucher so the Output tax lines are Dr for a credit note / Cr for
   a debit note (the party and sales/stock legs mirror). Reuse `ComputeInvoiceTax` for the *amounts/split*, then
   place them on the CDN-appropriate side (or add a `ComputeInvoiceTax` overload that takes an explicit
   `taxSide`).
2. **Sign-aware projection:** `Gstr1`/`Gstr3b` currently sum `line.Amount.Amount` (always positive) by head and
   do **not** distinguish base type. Make the output accumulation **sign-aware**: a `CreditNote` voucher
   *subtracts* from output tax, a `DebitNote` on the input side *adds* to ITC (or the mirror), so 3.1(a)/§4 net
   correctly. Drive the sign off `type.BaseType` (already enumerated per direction). **Confirm at kickoff whether
   Phase-4 ever posted CDN GST** — if it did (a golden exists), preserve it; if not (likely), there is no golden
   to break and this is net-new.

### 2.4 §34(2) 30-Nov guard (ER-12)
Pure guard in `CreditDebitNoteService.BuildCreditNote(...)`: if `CdnType == Credit` and the note reduces
liability and `cdnDate > NovemberThirtyFollowing(originalInvoiceDate)` → throw (block) or surface a warning per
DP (recommend **block** with an override flag, matching the fail-fast house style; the requirement says
"warn/block"). **Debit notes are uncapped** (no issuance cut-off; their ITC is keyed to the DN's own §16(4) FY —
note as a downstream/S6-S7 concern, not enforced in S2). A CDN with **no original-invoice link and no
consolidated-party ref** is rejected (ER-12: "never a free-floating tax delta").

### 2.5 GSTR-1 Table 9B feed
Add `Gstr1` `Table9B` rows: one per CDN voucher — original-invoice ref (number+date), CDN type, place of supply,
taxable value, CGST/SGST/IGST (signed), reason. Amendments → **9C** are S8 (RQ-29); S2 delivers 9B only. Reads
the posted CDN tax off the lines (pure projection), joined to the `gst_cdn_links` record for the original ref.

---

## 3. GST on advances (RQ-25; §12/§13, Rule 50/51)

### 3.1 Receipt Voucher (Rule 50) + the tax-on-advance pair
On an **advance received for a SERVICE**, GST is due on receipt. The `AdvanceReceiptService` (mirror
`TdsService` shape) produces a self-balancing tax pair *added on top of* the ordinary receipt legs. Worked
example — advance ₹11,800 incl. 18% on a ₹10,000 service:
```
Dr  Bank                        11,800     ] ordinary receipt legs
Cr  Advance from customer       11,800     ]
Cr  Output IGST                 1,800      ] tax-on-advance pair (self-balancing, additive)  → GSTR-1 11A
Dr  Output Tax on Advances      1,800      ]   (a current-asset suspense, reversed on the invoice)
```
- The **Output GST** leg is a genuine output liability (the tax is payable now) → the **normal** `Output {head}`
  ledger, tagged (see §3.3) so 11A can find it. The **"Output Tax on Advances"** suspense ledger (auto-created
  lazily under `Current Assets`, mirror `EnsureRoundOffLedger`, GstService.cs:152) holds the paid-tax pending the
  invoice, so the receipt balances without inflating revenue.
- Rate/POS at receipt: use the resolved rate; where unknown at receipt, Rule-50 fallback = **18% / inter-state**
  (a documented default in `AdvanceReceiptService`).

### 3.2 Services-only branch (Notn 66/2017) (RQ-25)
**Goods advances are de-taxed** — no tax-on-advance pair. Branch on `GstSupplyType` (already on
`StockItemGstDetails`/the advance record): `Services` ⇒ tax the advance; `Goods` ⇒ receipt legs only, no tax
pair, no 11A row. The test asserts a goods advance posts zero advance tax and a service advance posts the pair.

### 3.3 Advance record `gst_advance_receipts` + adjustment (11A/11B)
Domain `GstAdvanceReceipt` (value-object-with-identity, mirror `TdsChallan`), `Company` collection:
```csharp
public sealed class GstAdvanceReceipt {
    public Guid Id { get; }
    public Guid ReceiptVoucherId { get; }
    public bool IsService { get; }                       // false ⇒ goods (de-taxed)
    public Money AdvanceAmount { get; }                  // net (ex-tax) advance for a service; gross for goods
    public int RateBasisPoints { get; }
    public bool InterState { get; }
    public string? PlaceOfSupplyStateCode { get; }
    public Money AdvanceTax { get; }                     // 0 for goods
    public Guid? AdjustedAgainstInvoiceVoucherId { get; }// set on adjustment (→ 11B)
    public Guid? RefundVoucherId { get; }                // set on Rule-51 refund
}
```
- **Adjustment against the later invoice:** when the tax invoice is raised, the advance is adjusted — the
  `Output Tax on Advances` suspense is reversed (Dr Output GST-on-advance reversed / Cr suspense) so the
  invoice's own output tax is not double-counted, and `AdjustedAgainstInvoiceVoucherId` is set → **GSTR-1 11B**.
- **11A = Σ advance tax received in the period; 11B = Σ advances adjusted in the period** — pure `Gstr1`
  projections over the advance records + their linked vouchers.
- Reuse an `IsReverseCharge`-style tag OR (simpler) read 11A/11B off the `gst_advance_receipts` records directly
  (they carry the tax + adjustment link), which avoids tagging the Output line specially. Recommend the
  record-driven projection (the records are the source of truth for 11A/11B).

### 3.4 Refund Voucher (Rule 51)
On non-supply, the advance + its tax are returned via a `Payment`/Refund base-type voucher (Dr Advance / Dr
Output Tax on Advances reversal / Cr Bank) — sets `RefundVoucherId`. Reuses the §13(3) time-of-supply/earliest-
event helper (§1.6) shared with `RcmService`.

---

## 4. Schema v39 (ER-13; migration parity)

`src/Apex.Persistence.Sqlite/Schema.cs`:
1. `CurrentVersion = 38` → **`39`** (line 86).
2. **`CreateV1`:** add the 4 new tables + indexes (below) and the ALTER-equivalent columns **inline** in the
   `stock_items`, `ledgers`, `entry_lines`, `voucher_types` `CREATE TABLE` blocks (matching column defs
   byte-for-byte with the migration — the equivalence test enforces it).
3. **New `MigrateV38ToV39`** (mirror `MigrateV37ToV38`, Schema.cs:2641) — pure additive DDL:

```sql
-- notified RCM category master (dated, seeded via SeedAdvancedGst; empty when RCM unused)
CREATE TABLE rcm_categories (
    id                   TEXT    NOT NULL PRIMARY KEY,
    company_id           TEXT    NOT NULL REFERENCES companies(id),
    notification         TEXT    NOT NULL,   -- "13/2017-CT(R)" etc.
    stream               INTEGER NOT NULL,   -- RcmStream ordinal (Section9_3=0, Section9_4=1)
    supply_nature        TEXT    NOT NULL,   -- "GTA" | "Legal" | "Cement" | ...
    supply_type          INTEGER NOT NULL,   -- GstSupplyType ordinal (Goods=0, Services=1)
    hsn_sac              TEXT        NULL,    -- goods categories (cement 2523); NULL for services
    rate_bp              INTEGER NOT NULL,
    supplier_qualifier   INTEGER NOT NULL,   -- RcmParty ordinal
    recipient_qualifier  INTEGER NOT NULL,   -- RcmParty ordinal
    effective_from       TEXT    NOT NULL,   -- ISO yyyy-MM-dd, inclusive
    effective_to         TEXT        NULL,   -- ISO yyyy-MM-dd, inclusive; NULL = open
    label                TEXT    NOT NULL,
    is_predefined        INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_rcm_categories_company ON rcm_categories(company_id);

-- RCM self-invoice (Rule 47A) + payment voucher (Rule 52) generated documents (voucher-linked, own series)
CREATE TABLE rcm_documents (
    id                 TEXT    NOT NULL PRIMARY KEY,
    company_id         TEXT    NOT NULL REFERENCES companies(id),
    doc_kind           INTEGER NOT NULL,   -- RcmDocumentKind ordinal (SelfInvoice=0, PaymentVoucher=1)
    source_voucher_id  TEXT    NOT NULL REFERENCES vouchers(id),
    series_number      INTEGER NOT NULL,   -- consecutive per-company self-invoice/payment series
    doc_date           TEXT    NOT NULL,   -- ISO yyyy-MM-dd (self-invoice ≤ receipt+30d)
    supplier_ledger_id TEXT        NULL REFERENCES ledgers(id)
);
CREATE INDEX ix_rcm_documents_company ON rcm_documents(company_id);

-- §34 credit/debit-note link to the original invoice (+ 9B target, reason)
CREATE TABLE gst_cdn_links (
    id                          TEXT    NOT NULL PRIMARY KEY,
    company_id                  TEXT    NOT NULL REFERENCES companies(id),
    cdn_voucher_id              TEXT    NOT NULL REFERENCES vouchers(id),
    cdn_type                    INTEGER NOT NULL,   -- CdnType ordinal (Credit=0, Debit=1)
    original_invoice_voucher_id TEXT        NULL REFERENCES vouchers(id),
    original_invoice_number     TEXT        NULL,
    original_invoice_date       TEXT        NULL,   -- ISO yyyy-MM-dd (§34(2) FY basis)
    reason_code                 TEXT    NOT NULL,
    is_9b_target                INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX ix_gst_cdn_links_company ON gst_cdn_links(company_id);

-- GST-on-advances (Rule 50/51) + 11A/11B adjustment
CREATE TABLE gst_advance_receipts (
    id                             TEXT    NOT NULL PRIMARY KEY,
    company_id                     TEXT    NOT NULL REFERENCES companies(id),
    receipt_voucher_id             TEXT    NOT NULL REFERENCES vouchers(id),
    is_service                     INTEGER NOT NULL DEFAULT 1,  -- 0 ⇒ goods (de-taxed)
    advance_amount_paisa           INTEGER NOT NULL,
    rate_bp                        INTEGER NOT NULL DEFAULT 0,
    inter_state                    INTEGER NOT NULL DEFAULT 0,
    pos_state_code                 TEXT        NULL,
    advance_tax_paisa              INTEGER NOT NULL DEFAULT 0,  -- 0 for goods
    adjusted_against_invoice_vid   TEXT        NULL REFERENCES vouchers(id),
    refund_voucher_id              TEXT        NULL REFERENCES vouchers(id)
);
CREATE INDEX ix_gst_advance_receipts_company ON gst_advance_receipts(company_id);

-- RCM flags on the shared item/S-P-ledger GST block (default off ⇒ byte-identical)
ALTER TABLE stock_items ADD COLUMN reverse_charge_applicable INTEGER NOT NULL DEFAULT 0;
ALTER TABLE stock_items ADD COLUMN gta_forward_charge        INTEGER NOT NULL DEFAULT 0;
ALTER TABLE stock_items ADD COLUMN rcm_category_id           TEXT        NULL;
ALTER TABLE ledgers ADD COLUMN sp_reverse_charge_applicable  INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ledgers ADD COLUMN sp_gta_forward_charge         INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ledgers ADD COLUMN sp_rcm_category_id            TEXT        NULL;
-- party RCM qualifiers
ALTER TABLE ledgers ADD COLUMN party_is_promoter             INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ledgers ADD COLUMN party_is_body_corporate       INTEGER NOT NULL DEFAULT 0;
-- RCM Output ledger classification discriminator (default 0 = ordinary tax ledger)
ALTER TABLE ledgers ADD COLUMN gst_class_reverse_charge      INTEGER NOT NULL DEFAULT 0;
-- entry-line RCM tag (default NULL/0 ⇒ non-RCM line byte-identical)
ALTER TABLE entry_lines ADD COLUMN gst_is_reverse_charge     INTEGER NOT NULL DEFAULT 0;
ALTER TABLE entry_lines ADD COLUMN gst_rcm_scheme            INTEGER     NULL;  -- RcmItcScheme ordinal or NULL
-- RCM payment-voucher flag (mirror is_stat_payment)
ALTER TABLE voucher_types ADD COLUMN is_rcm_payment_voucher  INTEGER NOT NULL DEFAULT 0;
```
   Every added column default 0/NULL and every new table empty ⇒ a v38 company reloads byte-identically (ER-13).

`src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs`:
- **`EnsureSchema` stepper:** after the `version == 37` block (lines 911-929), add a `version == 38` block that
  runs `Schema.MigrateV38ToV39`, bumps `schema_version` to 39, sets `version = 39` (copy lines 911-929 exactly).
  The final guard (line 931) then passes.
- **Read:** add `ReadRcmCategories(Guid)` (attach to `GstConfig` where the S1 rate-history is attached, lines
  1097-1101), `ReadRcmDocuments`/`ReadGstCdnLinks`/`ReadGstAdvanceReceipts` (attach to the `Company`
  collections). Extend the item GST hydration, the ledger S-P-GST hydration, the party-GST hydration, the ledger
  classification hydration, and the entry-line GST hydration to populate the new columns. **These readers are
  positional (ordinal) — append every new column at the END of its `SELECT` list and read at the new trailing
  ordinal; never insert mid-list** (the S1 brief §3 and the store's `r.GetInt64(43)`-style reads at lines
  1631-1635 confirm this is load-bearing).
- **Write:** `DELETE` the 4 new tables' rows alongside the existing GST deletes (SqliteCompanyStore.cs:3480-3483)
  **before** the company row (they FK `companies`/`vouchers`); INSERT them next to the S1 rate-history/cess
  inserts (lines 3706-3749); extend the `stock_items`/`ledgers`/`entry_lines`/`voucher_types` INSERT column
  lists with the new columns. DateOnly ⇒ ISO `yyyy-MM-dd` TEXT.

**Downgrade helper (test-only, ER-13 legacy DDL):** in
`tests/Apex.Persistence.Sqlite.Tests/ItemInvoiceRoundTripTests.cs` `DowngradeToV11` (line 218+; S1 added the
`gst_rate_history`/`gst_cess_rates` drops at lines 293/295), add
`DROP TABLE IF EXISTS rcm_categories; DROP TABLE IF EXISTS rcm_documents; DROP TABLE IF EXISTS gst_cdn_links;
DROP TABLE IF EXISTS gst_advance_receipts;`. The new `stock_items`/`ledgers`/`entry_lines`/`voucher_types`
columns ride on the existing table-rewrite downgrade idiom (confirm the helper rewrites those tables to their
pre-v39 shape — same as S1's item/ledger columns).

---

## 5. Io canonical model (RQ-23, ER-13)

`CanonicalModel.cs`:
- New DTOs beside `GstRateHistoryDto`/`GstCessRateDto` (lines 972/986): `RcmCategoryDto` (all category fields,
  enum names as strings, ISO dates), `RcmDocumentDto`, `GstCdnLinkDto`, `GstAdvanceReceiptDto` (money as integer
  paisa — `long`, ER-10, mirror `CessPerUnitPaisa`).
- `GstConfigDto` (line ~947): append `IReadOnlyList<RcmCategoryDto> RcmCategories { get; init; } = [];` (after
  `RateHistory`/`CessRates`, keeping existing field order — S1's comment at CanonicalModel.cs:957 shows the
  "append at END" rule).
- Top-level `CanonicalModel` (or the company DTO that carries `TdsChallans`): append
  `RcmDocuments`, `CreditDebitNoteLinks`, `AdvanceReceipts` lists (mirror however `TdsChallans` +
  `ChallanVoucherLinks` are carried).
- `StockItemGstDto`: add `bool ReverseChargeApplicable`, `bool GtaForwardCharge`, `Guid? RcmCategoryId`.
- `PartyGstDto`: add `bool IsPromoter`, `bool IsBodyCorporate`.
- `GstLineTaxDto`: add `bool IsReverseCharge` (default false), `string? RcmScheme` (enum name or null).
- The ledger-classification DTO: add `bool IsReverseCharge` (default false).

`CanonicalMapper.cs`: extend `MapGstConfig` (map `RcmCategories`, deterministic order
`.OrderBy(EffectiveFrom).ThenBy(Notification).ThenBy(SupplyNature).ThenBy(Id)`), `MapStockItemGst`,
`MapPartyGst`, `MapGstLineTax`, the classification mapper, and add mappers for the 3 new company-level record
lists (order by `Id` or the natural voucher/series order).

`CanonicalXml.cs`: emit/read `<rcmCategories>` under `<gstConfig>` (after `<cessRates>`), the new company-level
record sections, and the new item/party/line/classification attributes. Use `Opt(...)` for nullable attrs
(existing null-omission style).

`ImportPlan.cs`: extend `BuildGstConfig` (add `foreach (var c in g.RcmCategories) config.AddRcmCategory(new
RcmCategory(Guid.NewGuid(), …))`, fresh ids, `ParseEnum<>` for the enums, `DateOnly.Parse` for ISO), the
item/party/line/classification builders, and rebuild the 3 record lists into the `Company` collections (fresh
ids, references re-resolved **by name** like every other import ref — RcmCategoryId, original-invoice ref,
adjusted-against-invoice ref).

`CompanyImportService.cs`: it posts every voucher through `LedgerService.Post`/`VoucherValidator`
all-or-nothing, so the **dual-leg balance is enforced automatically** (a leaky RCM pair trips the balance
invariant). **But the *semantic* guards are not** — mirror them in pre-flight (all-or-nothing, mirror the
recurring Io-bypass A10 finding class): (a) an RCM output line must land in an `IsReverseCharge` classification
ledger, never a normal Output ledger (cash-only structural check); (b) a credit-note link violating §34(2) with
no override rejects the batch; (c) a goods advance carrying non-zero advance tax rejects the batch (services-only
invariant); (d) a CDN with neither original-invoice ref nor consolidated-party ref rejects the batch (ER-12).
Prefer rebuilding CDN/advance/RCM vouchers **through their services** in `ImportPlan` so the guards run once;
where the DTO carries pre-computed lines, replicate the guard checks in `CompanyImportService` pre-flight.

---

## 6. TDD test list (write failing-first)

**RCM (`tests/Apex.Ledger.Tests`):**
1. **Dual-leg balances to the paisa** — an inward service ₹10,000 @18% inter-state posts `Cr RCM Output IGST
   1,800` + `Dr Input IGST 1,800` on top of the balanced purchase legs; `LedgerService.Post`/`VoucherValidator`
   accepts it; Σ Dr = Σ Cr; the RCM output lands in `RCM Output IGST` (never `Output IGST`); the ITC lands in
   `Input IGST` tagged `IsReverseCharge`+`OtherRcm`.
2. **POS split** — an intra-state RCM inward @18% posts `RCM Output CGST 900` + `RCM Output SGST 900` +
   `Input CGST 900` + `Input SGST 900` (total-then-split; odd-total 1-paisa remainder to SGST).
3. **Cash-only (structural)** — the RCM output liability sits in its own `RCM Output {head}` ledger; discharge is
   a cash Payment (Dr `RCM Output {head}` / Cr Bank); assert it is never posted to the normal Output ledger and
   there is no set-off path in S2 (S7 carry-forward noted).
4. **§9(4) promoter-only** — an unregistered-supplier inward with no promoter profile ⇒ **no** RCM leg (blanket
   §9(4) OFF by default); the same inward with `IsPromoter` + a matching 7/2019 category ⇒ dual leg fires. Cement
   from an unregistered supplier fires **regardless of the 80% test** and resolves the **dated** rate (28% on
   20-Sep-2025 via HSN 2523 legacy row, 18% on 25-Sep-2025) through the S1 `ResolveRate`.
5. **Import-of-services vs goods** — import-of-services ⇒ dual leg, IGST, `RcmScheme = ImportOfServices` (→4A(2));
   import-of-goods ⇒ **no** RCM leg produced (fail-fast guard; 4A(1) is out of scope).
6. **Self-invoice / payment voucher** — an unregistered-supplier RCM auto-generates a `SelfInvoice` `RcmDocument`
   with its own series and `doc_date ≤ receipt + 30 days` (a 31-day date is rejected/warned); a **registered**
   §9(3) supplier ⇒ **no** self-invoice. A supplier payment books a `Payment` voucher flagged
   `IsRcmPaymentVoucher` + a `PaymentVoucher` `RcmDocument`.
7. **GSTR-3B RCM buckets** — 3.1(d) = Σ RCM output lines; 4A(2) = Σ import-of-services RCM ITC; 4A(3) = Σ other
   RCM ITC; assert 4A(5) "all other ITC" **excludes** the RCM-tagged Input lines (no double-count), and 3.1(a)
   outward is unaffected by the RCM purchase.
8. **Cess-on-RCM (interaction)** — a cess-bearing RCM inward posts `RCM Output Cess` + `Input Cess` (tagged),
   ring-fenced out of the RCM CGST/SGST/IGST buckets (ER-2), balances to the paisa.

**§34 CDN (`tests/Apex.Ledger.Tests`):**
9. **Credit note reduces output** — a credit note linked to an original invoice posts Output GST on the reducing
   side and GSTR-3B 3.1 / GSTR-1 net **down** by that tax; a debit note posts on the increasing side and nets
   **up**. Both link to the original invoice (a CDN with no link and no consolidated-party ref is rejected, ER-12).
10. **§34(2) 30-Nov guard** — a credit note reducing liability dated after 30-Nov of the FY following the original
    supply is blocked/warned; on/before the deadline it posts. A **debit note** after the deadline is accepted
    (no cut-off).
11. **Table 9B mapping** — the CDN appears in GSTR-1 Table 9B with the original-invoice ref, type, POS, signed
    taxable value + CGST/SGST/IGST; reads the posted CDN tax (never recomputed).
12. **Paisa-exact** — the CDN reuses `ComputeInvoiceTax` total-then-split; CGST+SGST == IGST == round(value×rate).

**Advances (`tests/Apex.Ledger.Tests`):**
13. **Service advance taxes on receipt** — a ₹10,000 service advance @18% posts the `Cr Output IGST 1,800` /
    `Dr Output Tax on Advances 1,800` pair; the receipt balances; a `gst_advance_receipts` record with the tax is
    created → GSTR-1 **11A**.
14. **Goods advance de-taxed** — a goods advance posts **zero** advance tax (Notn 66/2017) — receipt legs only,
    no 11A row.
15. **Advance → invoice adjustment** — raising the tax invoice reverses the `Output Tax on Advances` suspense and
    sets `AdjustedAgainstInvoiceVoucherId` → GSTR-1 **11B**; the invoice's own output tax is not double-counted.
16. **Refund voucher (Rule 51)** — non-supply refunds the advance + tax and sets `RefundVoucherId`.
17. **Shared time-of-supply** — `RcmService.TimeOfSupply` == the advance earliest-event helper for the same
    inputs (goods 30-day / services 60-day / earliest of receipt/payment/invoice).

**Persistence (`tests/Apex.Persistence.Sqlite.Tests`):**
18. **Migration parity** — `SchemaMigrationEquivalenceTests` (unchanged code) passes at v39 (`CreateV1` ==
    v1→v39 chain). New **`RcmCdnAdvanceSchemaTests`** (mirror `GstRateHistoryCessSchemaTests`): (a) fresh DB
    stamps to `CurrentVersion` with the 4 new tables + every new column; (b) a legacy **v38** DB auto-migrates to
    v39 preserving rows, new columns default 0/NULL, new tables empty (ER-13), re-open idempotent; (c) save/load
    round-trips an RCM + CDN + advance company paisa-exact (categories, docs, links, advance records, RCM-tagged
    lines, RCM Output ledger classification).

**Io (`tests/Apex.Ledger.Io.Tests`):**
19. **JSON + XML lossless round-trip** — an RCM + CDN + advance company round-trips through `CanonicalJson` and
    `CanonicalXml`: export→parse→export **byte-stable**, reconstructed `Company` equals the original to the paisa
    (incl. RCM tag on lines, category refs by name, cdn/advance links).
20. **ER-13 byte-identical-when-off** — a company with no RCM/CDN/advance data (Robert, Bright, a plain S1 GST
    company) serialises with empty `RcmCategories`/record lists, `IsReverseCharge = false`, `RcmScheme = null`;
    assert its bytes match a captured pre-S2 golden (new members inert) and an off company == a v38 company.
21. **CompanyImportService all-or-nothing** — importing an RCM/CDN/advance model rebuilds every master + posts the
    vouchers through the engine (dual-leg balances, cash-only structural check, §34(2) guard, services-only
    guard); a batch violating any guard (goods advance with tax, credit note past 30-Nov, RCM output on a normal
    ledger, unlinked CDN) rejects wholesale (`Applied = false`, target byte-unchanged).

**Regression:** 22. **Robert + Bright green**; **Phase-4/S1 GST fixtures byte-identical** (GSTR-1/3B golden +
cess golden unchanged when RCM/CDN/advance off). De-brand scan clean (ER-11 — no "Tally" in any new
code/JSON/label; "RCM"/"Bill of Supply"/section codes are law, not brand).

---

## 7. Risks / subtleties for the implementer

1. **Paisa conservation across the RCM dual leg (top risk).** The output-liability pair and the ITC pair are each
   computed **once** with `ComputeLineTax` total-then-split + `RoundToPaisa`, and the two legs are the **same
   amount** so they cancel — never independently re-round the ITC leg (±0.01 drift, the recurring A10 finding).
   The whole voucher's party leg is the **supplier value only** (no tax), and the RCM pair is self-balancing on
   top; if you accidentally add the RCM tax to the party leg the voucher won't balance.
2. **`FindTaxLedger` ambiguity.** RCM Output ledgers are `(head, Output)` just like the normal Output ledgers.
   `FindTaxLedger` **must** filter `IsReverseCharge == false` (else a normal sale posts to the RCM ledger and
   every GSTR reconciliation breaks). Add a distinct `FindRcmOutputLedger`. This is easy to miss.
3. **RCM lines double-counted in normal GSTR buckets.** The RCM Input ITC posts to the **normal** `Input {head}`
   ledger; GSTR-3B 4A(5) "all other ITC" **must exclude** the RCM-tagged lines (they belong to 4A(2)/4A(3)) —
   mirror the S1 cess exclusion (`GstReportSupport.InvoiceTaxableValue`/`Gstr1.ReadInvoiceRateGroups` skip the
   `Cess` head). Symmetrically assert the RCM Output line (on a Purchase) is not swept into 3.1(a).
4. **CDN sign direction (trickiest CDN point).** A credit note must post Output tax on the **reducing** (Debit)
   side and the returns must **subtract** it; a debit note the opposite. `ComputeInvoiceTax` posts Output tax as
   Credit (correct for a sale) — the CDN service must place the legs on the CDN-appropriate side and the report
   accumulation must be **sign-aware by base type**. Confirm at kickoff whether Phase-4 already posts CDN GST
   (a golden to preserve) or not (net-new) — §2.3.
5. **Don't break Phase-4/8/S1 posting.** RCM/CDN/advance are all opt-in: absent a category master + master flag
   (RCM), a cdn-link (CDN), or a service-advance record (advances), `ComputeInvoiceTax` and every GSTR projection
   behave exactly as today. Every existing sale/purchase/cess fixture must stay byte-identical; the RCM Output
   and "Output Tax on Advances" ledgers are created **lazily** (never in `EnableGst`) so an off company has the
   same ledger set as v38.
6. **Cash-only invariant is only structural in S2.** The credit/cash ledgers + Rule-88A are S7. S2 guarantees the
   RCM liability sits in its **own** ledger and is discharged by cash; the "never nettable against the credit
   ledger" enforcement lands in S7 — note it as a carry-forward, don't half-build a set-off.
7. **Date-boundary reuse.** RCM time-of-supply (30/60-day) and the advance earliest-event share one helper and
   feed the S1 dated `ResolveRate`/`ResolveCess` — the cement 28%→18% and cess-window golden pairs are one
   off-by-one from failing at the 22-Sep-2025 boundary (inclusive bounds, S1 §6.3).
8. **Cess-on-RCM interaction.** A cess-bearing RCM line adds `RCM Output Cess` + `Input Cess`; the S1 cess
   ring-fence (ER-2) still holds — cess never mingles with the RCM CGST/SGST/IGST buckets. Least-exercised path;
   test it once.
9. **§34 CDN reuse vs replace.** Reuse the existing `CreditNote`/`DebitNote` base types (do **not** add a base
   type — `DirectionOf` already maps them). The novelty is the link record + guard + 9B + sign, not the voucher
   kind.
10. **Positional store readers + append-at-end.** `SqliteCompanyStore` reads item/ledger/party/line GST by column
    **ordinal**; append every new column at the END of each `SELECT` and read at trailing ordinals — inserting
    mid-list silently corrupts every later field (S1 §6.8).
11. **Io byte-identity under `IgnoreCondition.Never`.** Only add **new** members defaulting `[]`/false/null;
    never reorder or alter an existing DTO field / column order / mapper list ordering (S1 §6.6).

---

## 8. Recommendation — split S2 into two sequential sub-builds

**Recommend SPLIT.** S2 is roughly 2× the size of S1: it adds 4 tables, a new seeded dated master, RCM
flags on three master types, a line-level tag + a ledger-classification discriminator, **three** new engine
services, dual-leg posting with a new ledger family, sign-aware CDN, an advance suspense-and-adjustment flow, and
three new GSTR tables — far more than one A10 review can hold in view at once, and three distinct correctness
regimes (self-accounting balance; CDN sign/guard; advance suspense) that each deserve their own regression lock.

**Proposed split (both land on the single `v38→v39` migration; small coherent commits per R10):**
- **S2a — RCM core.** `MigrateV38ToV39` lands **in full here** (all 4 tables + all ALTERs created empty, so the
  schema is complete at one version bump). Build: `rcm_categories` + seed, RCM flags on the masters,
  `GstLineTax`/`LedgerGstClassification` tags, `RcmService` (dual leg + time-of-supply + import-of-services guard
  + self-invoice/payment docs), `FindTaxLedger` disambiguation + lazy RCM Output ledgers, GSTR-3B 3.1(d)/4A
  buckets + the exclusion fix, Io + import mirror for the RCM surface. PR-2a gate = tests 1–8, 18–22 (RCM parts).
- **S2b — §34 CDN + GST-on-advances.** Against the already-present v39 schema (no new bump): `CreditDebitNoteService`
  (link + §34(2) guard + sign + 9B), `AdvanceReceiptService` (services-only advance tax + suspense + adjustment +
  11A/11B + refund), reusing the S2a time-of-supply helper. PR-2b gate = tests 9–17 + the CDN/advance halves of
  18–22.

**Rationale:** RCM is the large, high-risk, self-contained unit whose dual-leg + report bucketing is the phase's
hardest new invariant; CDN + advances are outward-document adjustments that *share only* the time-of-supply
helper and are otherwise independent, so they compose cleanly as a second commit without reopening the RCM
engine. Keeping one migration preserves the plan's `v38→v39` contract while letting each behavioural half get a
focused A10 pass and its own byte-stable golden. If the gate must remain a single PR-2, still build in this
order (RCM engine green before touching CDN/advances) to bound blast radius.

---

## 9. Definition of Done (PR-2 gate, requirements §5)

RCM dual-leg balances to the paisa · §9(4) fires **only** for the promoter profile (blanket default OFF) ·
import-of-goods excluded from 3.1(d) · cash-only discharge (structural) · self-invoice within 30 days ·
**CDN links to the original + adjusts output tax in the right direction + §34(2) 30-Nov guard fires (ER-12) + 9B
mapping** · **advance tax on services only (goods de-taxed) + Receipt Voucher + 11A/11B** · RCM-aware GSTR
projection (3.1(d)/4A) with no double-count · legacy-DDL v38→v39 upgrade preserves rows · **ER-13 byte-identical
when off** · Io JSON+XML + `CompanyImportService` round-trip lossless & all-or-nothing · migration parity
(`CreateV1 == MigrateV38ToV39` chain) · **Robert + Bright + Phase-4/S1 fixtures green** · de-brand scan clean.
Then A10 adversarial pass (3 lenses) → I re-run the Release gate → A12 commits.
