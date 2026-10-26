# Phase 9 slice S6 — GSTR-2A/2B import + reconciliation + IMS + §17(5) blocked-credit flag — Implementation Brief

> **Slice:** P9-S6 (schema **v42 → v43**). The **first INBOUND GST data path**: import a portal-downloaded
> GSTR-2B (static ITC gatekeeper) / GSTR-2A (dynamic) JSON into staging, reconcile it against the booked
> purchase register into buckets, mirror the portal's IMS Accept/Reject/Pending offline, and land the §17(5)
> per-purchase ITC-eligibility flag. **ADVISORY ONLY — never posts ITC or a reversal (ER-14/ER-6).**
> Design-only; grounded in the actual on-disk code (schema head confirmed `CurrentVersion = 42`,
> `Schema.cs:94`). Governed by CLAUDE.md R1–R14; built via `/software`; A14 re-verifies law per R7; A10
> adversarial pass before the PR gate; A12 owns git (R4).
>
> **Key RQs:** RQ-12 (import), RQ-13 (reconciliation), RQ-14 (IMS), RQ-15 (ITC-gate advisory), RQ-26 (§17(5)).
> **DPs:** DP-13 (import both; 2B = ITC basis; configurable tolerance), DP-14 (full offline IMS mirror),
> DP-15 (advisory-only), DP-31 (per-purchase ITC-eligibility flag). **ERs:** ER-6, ER-13, **ER-14 (structural
> advisory-only)**, ER-9, ER-10, ER-11, ER-16.

---

## 0. Grounding — what already exists (verified on disk)

The whole slice extends machinery already shipped in S1–S5, and reuses four proven seams verbatim:

| Seam | File(s) | What S6 reuses |
|---|---|---|
| **Connector transport** | `src/Apex.Ledger.Io/{IGstPortalConnector,OfflineJsonConnector,GspConnector}.cs` | Add ONE inbound `FetchStatement` operation; `OfflineJson` parses a portal-downloaded JSON file; `CustomerNicDirect`/`Gsp` throw `NotSupportedException` (2A/2B stay offline-only, RQ-30/DP-25). |
| **Value-object-with-identity record** | `src/Apex.Ledger/Domain/{EInvoiceRecord,EWayBillRecord}.cs` | Mutable aggregate + fresh ctor + static `Rehydrate(...)` (fail-fast invariant → all-or-nothing import) + `internal` mutators. Exactly the shape for the snapshot / line / IMS / recon records. |
| **Company collection** | `src/Apex.Ledger/Domain/Company.cs` (`_eInvoiceRecords` … `AddEInvoiceRecord`/`Find…`) | `List<T>` backing + `IReadOnlyList<T>` + `Add/Remove/Find` used by store & import rehydration. |
| **Schema + store + Io + import-mirror** | `Schema.cs` (`MigrateV41ToV42`), `SqliteCompanyStore.cs` (`if (version == 41)`), `CanonicalModel/Mapper/Xml.cs`, `ImportPlan.cs` (§14 staging w/ journal + orphan-skip) | The migration/persist/round-trip/all-or-nothing pattern, copied additively for v43. |

Two facts shape the whole design and set S6 apart from S4/S5:

1. **2B/2A rows are EXTERNAL data, not the taxpayer's own postings.** Unlike e-invoice/e-way records (which
   REQUIRE a `source_voucher_id` and are skipped as orphans on import if the voucher is absent), a 2B line
   stands alone — it exists precisely to be reconciled *against* the books, and the "In-2B-only" bucket is the
   case where no booked purchase matches. So on Io import the lines import **unconditionally**; only the
   *recon* row's `matched_voucher_id` is an **optional** reference resolved via `MapVoucherOpt` (null → the
   pin is dropped, the line falls back to unmatched). This is the single biggest structural difference from S4/S5.

2. **The books side already has a clean projection.** `GstReportSupport.PostedGstVouchers(company, from, to,
   GstTaxDirection.Input)` (`GstReportSupport.cs:38`) yields the posted inward (Purchase/DebitNote) vouchers
   carrying Input GST — the purchase register to reconcile against. Each voucher's supplier GSTIN comes from
   the party ledger's `PartyGstDetails.Gstin` (`PartyGstDetails.cs:19`); the per-head tax split from the posted
   `GstLineTax` lines (mirroring `Gstr3b.ReadSide`, `Gstr3b.cs:208`). **RCM-tagged input lines
   (`GstLineTax.IsReverseCharge`) are EXCLUDED** from the books register — inward RCM bypasses 2B/IMS and goes
   straight to 3B (§2.7), exactly as `Gstr3b.ReadSide` already skips them (`Gstr3b.cs:226`).

---

## 1. Import + staging (RQ-12; DP-13)

### 1.1 Connector extension (reuse the inbound seam)

Add ONE inbound member to `IGstPortalConnector` (the pure transport contract — carries bytes, never tax logic):

```csharp
// IGstPortalConnector.cs — the first INBOUND operation (Phase 9 slice 6; RQ-12; RQ-30)
/// <summary>Imports a portal statement (GSTR-2B/2A). OFFLINE: deterministically parses the supplied
/// portal-downloaded JSON bytes into a neutral snapshot. LIVE (CustomerNicDirect)/GSP: NotSupported —
/// returns/2A/2B stay offline-only in Phase 9 (RQ-30/DP-25). The transport never reconciles or posts.</summary>
Gstr2bImportResult FetchStatement(Gstr2bFetchRequest request);

public sealed record Gstr2bFetchRequest(byte[] Json, GstStatementType StatementType, string RecipientGstin);
public sealed record Gstr2bImportResult(bool Parsed, Gstr2bStatementDto? Statement, string? ErrorCode, string? ErrorMessage);
```

- **`OfflineJsonConnector.FetchStatement`** delegates to a pure, static `Gstr2bJsonParser.Parse(bytes, type,
  recipientGstin)` (new, in `Apex.Ledger.Io`) and wraps the result. Needs **zero credentials** (ER-16 baseline).
- **`GspConnector.FetchStatement`** → `throw new NotSupportedException(NotBuilt)` (same message const as the
  existing members). A `CustomerNicDirectConnector` (if/when instantiated) also throws for `FetchStatement`
  (live 2B fetch is a future-GSP capability; only e-Invoice/e-Way have a live NIC path).

### 1.2 Deterministic parser (`Gstr2bJsonParser`, new in `Apex.Ledger.Io`)

A **pure, deterministic** parser: portal JSON → `Gstr2bStatementDto` (neutral, mapper-facing DTO). Mirrors how
`CanonicalJson`/`ImportPlan` already parse canonical JSON. Requirements:

- Handles the GSTN 2B JSON sections **B2B, B2BA (amendments), CDNR (credit/debit notes), CDNRA, IMPG (import
  of goods — from BoE), IMPGSEZ, ISD** — mapping each to a `Gstr2bLineDto` with a `DocType` discriminant.
- Parses the **Cess** column on every section (FY2025-26 tobacco 2Bs still carry cess — §2.7). Money at the
  boundary = **integer paisa** (decimal rupees in the JSON × 100, `MoneyCodec.ToPaisa`).
- Captures the portal's **"ITC Available" (Y/N) bifurcation** per line (`itcavl` / `rsn` fields) → `ItcAvailable`
  + `ItcUnavailableReason` (the statutory ITC gate lives in the *portal's* 2B, §16(2)(aa)).
- Captures each line's **supplier-flagged `reverse_charge`** (`rev` = Y).
- Reads the **generation date** (`gendt`) and **return period** (`rtnprd`) into the snapshot header, plus the
  portal **summary ITC totals** (for a reconcile-to-source integrity check).
- **Fully deterministic** (no clock, no culture) — the same bytes always yield byte-identical DTOs (invariant
  culture, ordinal string handling), so the parse test is a fixed golden. A malformed/foreign-GSTIN file
  **fails fast** (parsed=false + error) rather than importing a partial statement.

### 1.3 Staging aggregate (immutable snapshot owns lines)

Two collaborating domain types, both `Apex.Ledger/Domain`, following the `EInvoiceRecord` shape:

- **`Gstr2bSnapshot`** — the **immutable dated statement** (ER-6). Fresh ctor + static `Rehydrate`. Owns a
  `IReadOnlyList<Gstr2bLine>`. Carries: `StatementType` (Gstr2b/Gstr2a), `ReturnPeriod` (string, e.g.
  `"2025-10"`), `RecipientGstin`, `GeneratedOn` (portal `gendt`), `SourceFileHash` (SHA-256 of the imported
  bytes — dedupe + immutability audit), `ImportedAt`, and the portal summary ITC totals (paisa). **No mutator
  edits a line** — a re-import for the same period creates a *new* snapshot (versioned by `ImportedAt` +
  hash), never mutating the old one (ER-6 "immutable dated snapshot").
- **`Gstr2bLine`** — one imported inward-supply record (child; a plain value-object). Carries: `SupplierGstin`,
  `SupplierTradeName`, `DocType` (`Gstr2bDocType`), `DocNumber` + `DocNumberNorm` (normalised — see §2.3),
  `DocDate`, `PosStateCode`, `TaxableValuePaisa`, `IgstPaisa`/`CgstPaisa`/`SgstPaisa`/`CessPaisa`,
  `ItcAvailable` (bool), `ItcUnavailableReason`, `ReverseCharge` (supplier-flagged).

**2A vs 2B share ONE table pair** discriminated by `StatementType` (matches the plan's schema list —
`gstr2b_snapshots`/`gstr2b_lines`, no separate 2a tables). **2B is the ITC-eligibility basis** (DP-13); 2A is
the supplementary dynamic view. The reconciler and the ITC-gate read the 2B snapshot; a 2A snapshot is
importable and reconcilable but is never the statutory gate.

---

## 2. Reconciliation engine — PURE (RQ-13; DP-13; DP-15)

### 2.1 Shape

A **pure, deterministic, side-effect-free** engine `Gstr2bReconciler` (`Apex.Ledger/Services`) — no ledger
mutation, no posting (ER-14 structural, see §5). Signature:

```csharp
public static Gstr2bReconciliationReport Reconcile(
    Company company, Gstr2bSnapshot snapshot, DateOnly from, DateOnly to, ReconTolerance tolerance);
```

It builds the **books-side purchase register** from `GstReportSupport.PostedGstVouchers(company, from, to,
Input)` — **excluding** RCM-tagged lines (`GstLineTax.IsReverseCharge`, mirroring `Gstr3b.ReadSide:226`) and
**excluding composition** (no ITC) — deriving per-voucher: supplier GSTIN (party `PartyGstDetails.Gstin`),
supplier-invoice ref (§2.3), taxable value (`GstReportSupport.InvoiceTaxableValue`), per-head tax + cess
(`PostedCessTotal`). It matches each 2B line against that register into buckets.

### 2.2 Buckets (paisa-exact, deterministic)

| Bucket | Meaning | Source |
|---|---|---|
| **Matched** | 2B line ↔ a booked purchase, keys equal + value/tax within tolerance | both |
| **PartialMismatch** | keys match (GSTIN + normalised doc-no + date window) but value/tax differ **beyond** tolerance | both (variance recorded) |
| **InPortalOnly** (In-2B-only) | 2B line with no booked purchase — portal has it, books don't | portal only |
| **InBooksOnly** | booked purchase with no 2B line — supplier hasn't filed ⇒ **ITC ineligible** per §16(2)(aa) | books only |

- **`Matched`** requires: same normalised supplier GSTIN **and** normalised doc-no, doc-date within the
  tolerance date-window, and taxable + total-tax variance ≤ tolerance (paisa). Cess variance folded into the
  tax variance.
- **`PartialMismatch`** = identity keys match but a value/tax field exceeds tolerance. The report carries
  `TaxableVariancePaisa` + `TaxVariancePaisa` (portal − books, signed) for drill-down.
- **`InBooksOnly`** rows are **derived** (a books voucher matched by no 2B line) — they have **no 2B line**,
  so they are surfaced in the report but are NOT persisted against a `gstr2b_lines` row (see §6.4).

Deterministic ordering: match candidates are considered in a fixed order (by supplier GSTIN, then normalised
doc-no, then doc-date, then voucher id) so the same inputs always yield the same one-to-one pairing —
**one 2B line matches at most one voucher and vice-versa** (a greedy stable pass; a consumed voucher can't
re-match). This determinism is a hard test target (risk #2).

### 2.3 Match keys + normalisation (the subtle part — risk #2)

- **Supplier GSTIN:** `PartyGstDetails.Gstin`, upper-cased/trimmed. A B2C/unregistered purchase (no GSTIN)
  can never match a 2B line (2B is B2B/CDN/import only) → those purchases don't enter the register.
- **Supplier invoice number (books side):** Tally records the supplier's bill reference in **bill-wise
  details**. The reconciler reads the supplier doc-no from the purchase voucher's **`BillAllocation.Name`**
  (`BillAllocation.cs:23`) on the party/creditor line (a `NewRef`/`AgstRef` allocation — the canonical home
  for the supplier's invoice reference), falling back to the voucher `Narration`/`Number` only when bill-wise
  details are not maintained. **Normalisation** (`DocNumberNorm`): trim, upper-case, strip leading zeros and
  non-alphanumerics (portal doc-no is case-insensitive and formatting-noisy). When no supplier ref is
  recoverable, the line can still match on **GSTIN + date-window + value/tax within tolerance** but is
  demoted to **PartialMismatch** (never a clean Matched) so the user verifies it.
  > **Surfaced gap (A14/A10 to confirm at kickoff):** the `Voucher` header has **no dedicated
  > `SupplierInvoiceNo`/`SupplierInvoiceDate` field** (verified — `Voucher.cs` has only `Number`, `Date`,
  > `Narration`, `PartyId`). Bill-wise ref is the pragmatic books-side doc-no. If fidelity demands a
  > first-class supplier-invoice reference on purchases, that is a **voucher-table ALTER** — recommend
  > deferring it (bill-wise ref suffices for reconciliation) and recording the choice in `memory.md`.
- **Date window:** `|portalDocDate − booksVoucherDate| ≤ tolerance.DateWindowDays` (default a small window,
  e.g. 0–1 day — the supplier's invoice date should equal the booked date; a window absorbs entry lag).
- **Value/tax tolerance:** `ReconTolerance` (see §2.4).

### 2.4 Configurable tolerance (DP-13)

`ReconTolerance` — a small value object (paisa + days), configured company-wide on `GstConfig` (mirrors how
`EWayThreshold` sits there) so it round-trips and defaults byte-identically:

```csharp
// GstConfig.cs additions (Phase 9 slice 6; RQ-13/DP-13; default = strict so an off company is byte-identical)
public Money ReconValueTolerance { get; set; } = Money.Zero;   // paisa slack on taxable/tax match
public int   ReconDateWindowDays { get; set; } = 0;            // ± days on doc-date match
```

Default `Zero`/`0` ⇒ exact-match reconciliation ⇒ a company that never touches 2B is byte-identical (ER-13).
The tolerance is a **matching** parameter only; it never changes a posted figure.

### 2.5 Output — advisory report

`Gstr2bReconciliationReport` is a pure record (like `Gstr3b`): per-bucket line collections + count/value
subtotals + the `matched_voucher` drill-down link + per-line variance. **It posts nothing.** The persisted
`gstr2b_recon` rows (§6.4) are an audit snapshot the "Reconcile" UI action writes by running this engine; the
engine remains the source of truth and re-running is idempotent (overwrites the recon rows).

---

## 3. IMS offline mirror (RQ-14; DP-14)

### 3.1 Per-line IMS action (mutable, keyed to the immutable 2B line)

Because the 2B snapshot is immutable (ER-6) but IMS is the user's evolving decision, IMS state is a **separate
mutable record** keyed to the 2B line id — never a mutation of the line. Domain type `ImsAction`
(`Apex.Ledger/Domain`), mutable value-object-with-identity + `Rehydrate`:

- `LineId` (→ `Gstr2bLine`), `Status` (`ImsStatus`), `Remarks`, `DeclaredReversalPaisa` (nullable),
  `NoReversalDeclared` (bool), `ActedOn` (nullable date).
- **`ImsStatus`** enum: `NoAction = 0` (⇒ **deemed-accepted** at 2B generation), `Accepted = 1`,
  `Rejected = 2`, `Pending = 3`. Stored as the ordinal.

### 3.2 Deemed-accept + Oct-2025 CN handling

- **Deemed-accept:** any line with **no `ImsAction`** (or `NoAction`) is treated as **Accepted** at 2B
  generation — a *derived* view (`EffectiveStatus`), never a stored flip (mirrors how `EWayStatus.Expired`
  is derived, not stored). So a fresh import needs **zero** IMS rows and still reads "all deemed accepted"
  (ER-13: empty when unused).
- **Oct-2025 credit-note change:** on **Accept** of a CDN line the recipient may declare a **partial / no ITC
  reversal** (`DeclaredReversalPaisa` / `NoReversalDeclared`, remarks mandatory when partial). A CN line may
  be kept **Pending** one period then deemed-accepted. The mirror stores the declaration; it **does not post**
  the reversal — that's S7 (ER-14).

### 3.3 Bypass section (non-actionable)

Records that **bypass IMS** — inward RCM (supplier-flagged `ReverseCharge`), and §16(4)/POS-ineligible lines —
are surfaced in a **separate, read-only "not IMS-actionable" section** of the report (not offered
Accept/Reject). The UI states clearly, on every IMS surface, that **the real Accept/Reject action happens on
the GST portal** — the app is an **offline mirror** (DP-14/DP-25). `ImsService` (`Apex.Ledger/Services`) is a
thin mutator (`SetAction`, `ClearAction`) that validates (e.g. a declared reversal only on an Accepted CN) and
never touches the ledger.

---

## 4. §17(5) blocked credits + ITC-eligibility flag (RQ-26; DP-31)

### 4.1 Per-purchase eligibility flag (on the shared GST block)

The §17(5) determination is a **per-purchase master flag**, distinct from the portal's 2B "ITC-available"
flag. It lives on the **shared** `StockItemGstDetails` block (used by BOTH stock items and sales/purchase
ledgers — `StockItemGstDetails.cs`, already carrying RCM + cess + valuation fields), so a purchase's ITC
eligibility is driven by its item/expense ledger:

```csharp
// StockItemGstDetails.cs additions (Phase 9 slice 6; RQ-26/DP-31). Default Eligible/None ⇒ byte-identical (ER-13).
public ItcEligibility ItcEligibility { get; set; } = ItcEligibility.Eligible;
public BlockedCreditCategory BlockedCreditCategory { get; set; } = BlockedCreditCategory.None;
```

- **`ItcEligibility`** enum: `Eligible = 0`, `BlockedSection17_5 = 1`, `Ineligible = 2`.
- **`BlockedCreditCategory`** enum (the §17(5) sub-clause, advisory grouping): `None = 0`, `MotorVehicles`,
  `FoodBeveragesHealth`, `WorksContractConstruction`, `MembershipClubs`, `PersonalConsumption`,
  `GiftsFreeSamples`, `LostStolenWrittenOff`, `Other`.
- `EnsureValid` (fail-fast, mirroring the existing cess checks): a `BlockedCreditCategory != None` requires
  `ItcEligibility == BlockedSection17_5`; `Eligible` requires `BlockedCreditCategory == None` (no contradiction).

### 4.2 §16(2)(aa) gate + GSTR-3B Table 4 advisory view

A new advisory report `ItcGateView` (`Apex.Ledger/Reports`, a pure projection like `Gstr3b`) surfaces, per
period, three ITC figures side-by-side (RQ-15):

- **ITC in books** — eligible Input GST on posted purchases (from the books register, §17(5)-blocked
  **excluded** from the eligible pool → mapped to a **Table 4(B)(1)/4(D)** advisory line).
- **ITC in 2B** — the `ItcAvailable` portal figure from the matched snapshot (**§16(2)(aa): ITC only if in
  2B**).
- **ITC claimed in 3B** — the existing `Gstr3b` ITC (display-only).

It **flags the differences** (books-but-not-2B ⇒ §16(2)(aa) ineligible this period; §17(5)-blocked ⇒ Table
4(B)(1)) — **purely advisory** (RQ-15/DP-15). It **surfaces reversal candidates** (Rule-37A supplier-non-payment,
IMS-rejected CN, §17(5)-blocked) for **S7 to post** — it never posts a reversal itself (ER-14, §5).

---

## 5. ADVISORY-ONLY invariant (ER-14 / ER-6) — enforced STRUCTURALLY

The single highest-risk property of the first inbound path: **imported data must never auto-mutate the ledger.**
Make it structural, not merely "the code happens not to post":

1. **No posting surface exists in this slice's engines.** `Gstr2bReconciler`, `ImsService`, `ItcGateView`,
   `Gstr2bImportService` take a `Company` (or read it) and return **reports/records** — none takes a
   `LedgerService`, a posting journal, or emits an `EntryLine`. There is deliberately **no method that creates
   a voucher or a `GstLineTax`** anywhere in the S6 surface (mirrors how `EInvoiceRecord` has no IRN-minting
   method — the *structural absence* is the guarantee, ER-5 style).
2. **Staging tables are physically separate from the ledger tables.** `gstr2b_*` / `ims_status` reference
   `companies`/`vouchers`/`gstr2b_lines` but **no ledger, entry_line, or tax posting is written** by any S6
   path. The recon's `matched_voucher_id` is a **read-only pointer** to an existing voucher, never a mutation.
3. **The reconciler/IMS/§17(5) views only SURFACE candidates.** The report exposes a
   `ReversalCandidates` collection (rejected-CN, Rule-37A, §17(5)-blocked) that carries the *identity* of the
   affected purchase + the *suggested* reversal amount — consumed by the **S7** set-off/stat-adjustment engine,
   which is the **sole poster** (RQ-27). S6 writes no `itc_reversals`/`gst_drc03` row (those tables land in S7).
4. **A structural regression test** (see §8, ER-14 test) posts nothing after a full import+reconcile+IMS cycle:
   ledger balances, `entry_lines`, and every `Gstr3b`/tax figure are **byte-identical** before and after — the
   inbound cycle leaves the books untouched.

This is the same posture as ER-6 ("2B is immutable; reconciliation is advisory — never auto-posts").

---

## 6. Schema v43 (mirror the `MigrateV41ToV42` pattern EXACTLY)

Bump `Schema.CurrentVersion` **42 → 43** (`Schema.cs:94`). Add **every** table/column to **BOTH** `CreateV1`
**AND** a new `MigrateV42ToV43` (byte-identical DDL — `SchemaMigrationEquivalenceTests` enforces it). Wire an
`if (version == 42)` step in `SqliteCompanyStore.EnsureSchema` (after the `== 41` block, `SqliteCompanyStore.cs:1008`)
in a txn that bumps `schema_version` to 43. All new tables carry `company_id` FK + `ix_*_company`; empty when
S6 is unused ⇒ **ER-13 byte-identical**.

### 6.1 `gstr2b_snapshots` (immutable dated statement)

```
id TEXT PK, company_id TEXT FK→companies,
statement_type INTEGER NOT NULL DEFAULT 0,      -- GstStatementType (Gstr2b=0, Gstr2a=1)
return_period TEXT NOT NULL,                     -- "yyyy-MM"
recipient_gstin TEXT NOT NULL,
generated_on TEXT NULL,                          -- ISO yyyy-MM-dd (portal gendt)
source_file_hash TEXT NOT NULL,                  -- SHA-256 of imported JSON (dedupe + immutability audit)
imported_at TEXT NOT NULL,                       -- ISO round-trip (o)
summary_itc_igst_paisa/cgst/sgst/cess_paisa INTEGER NOT NULL DEFAULT 0
+ CREATE INDEX ix_gstr2b_snapshots_company
```

### 6.2 `gstr2b_lines` (imported inward-supply record; child)

```
id TEXT PK, company_id TEXT FK, snapshot_id TEXT NOT NULL REFERENCES gstr2b_snapshots(id),
supplier_gstin TEXT NOT NULL, supplier_trade_name TEXT NULL,
doc_type INTEGER NOT NULL,                       -- Gstr2bDocType ordinal
doc_number TEXT NOT NULL, doc_number_norm TEXT NOT NULL,   -- normalised match key
doc_date TEXT NOT NULL,                          -- ISO yyyy-MM-dd
pos_state_code TEXT NULL,
taxable_value_paisa INTEGER NOT NULL DEFAULT 0,
igst_paisa/cgst_paisa/sgst_paisa/cess_paisa INTEGER NOT NULL DEFAULT 0,
itc_available INTEGER NOT NULL DEFAULT 1,        -- portal 2B ITC-Available Y/N bifurcation
itc_unavailable_reason TEXT NULL,
reverse_charge INTEGER NOT NULL DEFAULT 0        -- supplier-flagged RCM (bypasses IMS)
+ CREATE INDEX ix_gstr2b_lines_company, ix_gstr2b_lines_snapshot
```

### 6.3 `ims_status` (per-line IMS action; mutable)

```
id TEXT PK, company_id TEXT FK, line_id TEXT NOT NULL REFERENCES gstr2b_lines(id),
action INTEGER NOT NULL DEFAULT 0,              -- ImsStatus (NoAction=0 ⇒ deemed-accept)
remarks TEXT NULL,
declared_reversal_paisa INTEGER NULL,           -- Oct-2025 partial CN reversal
no_reversal_declared INTEGER NOT NULL DEFAULT 0,
acted_on TEXT NULL
+ CREATE INDEX ix_ims_status_company, ix_ims_status_line
```

### 6.4 `gstr2b_recon` (per-2B-line reconciliation result; audit + manual pin)

```
id TEXT PK, company_id TEXT FK, line_id TEXT NOT NULL REFERENCES gstr2b_lines(id),
bucket INTEGER NOT NULL,                         -- ReconBucket (Matched=0, PartialMismatch=1, InPortalOnly=2)
matched_voucher_id TEXT NULL REFERENCES vouchers(id),
taxable_variance_paisa INTEGER NOT NULL DEFAULT 0,
tax_variance_paisa INTEGER NOT NULL DEFAULT 0,
match_pinned INTEGER NOT NULL DEFAULT 0,         -- user manually pinned this match (overrides auto-matcher)
reconciled_at TEXT NULL
+ CREATE INDEX ix_gstr2b_recon_company, ix_gstr2b_recon_line
```

> **`InBooksOnly` is NOT persisted here** — it has no 2B line to key on (FK `line_id` NOT NULL). It is a
> report-time derivation (booked purchases matched by no 2B line). The recon table holds only portal-side
> results. Keeps FK integrity clean and the recon table a faithful child of `gstr2b_lines`.

### 6.5 `ledgers` + `stock_items` — §17(5) column

```
ALTER TABLE stock_items ADD COLUMN itc_eligibility INTEGER NOT NULL DEFAULT 0;   -- ItcEligibility
ALTER TABLE stock_items ADD COLUMN blocked_credit_category INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ledgers     ADD COLUMN itc_eligibility INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ledgers     ADD COLUMN blocked_credit_category INTEGER NOT NULL DEFAULT 0;
```

Default `0/0` ⇒ Eligible/None ⇒ byte-identical when off (ER-13). These populate the shared
`StockItemGstDetails` block on load (mirror how the existing cess/RCM columns on those tables already do).

### 6.6 Store load/save + downgrade helpers

- **Store:** add `ReadGstr2bSnapshots` / `ReadGstr2bLines` / `ReadImsStatus` / `ReadGstr2bRecon` (mirror
  `ReadEInvoiceRecords`/`ReadEWayBillRecords`, `SqliteCompanyStore.cs:2056/2088`), wiring into `LoadCompany`;
  add delete-then-insert in `SaveCompany` (mirror the `DELETE FROM einvoice_records …` + `INSERT INTO
  einvoice_records …` pair at `SqliteCompanyStore.cs:3746/4680`). Read the new §17(5) columns in the
  stock_items/ledgers loaders and write them in the savers.
- **Downgrade helpers (REQUIRED — the R9 exit-gate item):** add `DROP INDEX/TABLE IF EXISTS` for the four
  new tables to **`DowngradeToV11`** (`ItemInvoiceRoundTripTests.cs:218`, alongside the existing
  `DROP … eway_bills` at :313) **and `DowngradeToV9`** (`InventoryVoucherRoundTripTests.cs:252`, alongside
  :436). The stock_items/ledgers rebuild in those helpers (`DowngradeStripV13`-style) strips the two new
  §17(5) columns automatically once they are on the strip list.

---

## 7. Io canonical model — lossless round-trip + all-or-nothing import (RQ-23; ER-13; ER-16)

The taxpayer's reconciliation state (imported snapshots + IMS decisions + §17(5) flags) is worth preserving,
so it round-trips in JSON **and** XML. Fold in **in-slice** (never deferred — the recurring Io-bypass A10 class).

- **`CanonicalModel.cs`:** add `Gstr2bSnapshotDto` (owning `IReadOnlyList<Gstr2bLineDto>`), `ImsActionDto`,
  `Gstr2bReconResultDto` collections on the payload; add `ItcEligibility`/`BlockedCreditCategory` (enum-name
  strings) to the existing item/ledger GST DTO. Money = paisa `long`; dates = ISO strings; enums = names
  (mirror `EInvoiceRecordDto`/`EWayBillRecordDto`, `CanonicalModel.cs:332/353`).
  > **ER-16:** the snapshots are the customer's own portal downloads (not a secret) — export them. **No NIC
  > credential ever appears** (unchanged — credentials flow only through `INicCredentialStore`).
- **`CanonicalMapper.cs`:** `Map*` (domain → DTO), deterministically ordered (e.g. snapshots by
  `(ReturnPeriod, ImportedAt, Id)`, lines by `Id`, ims/recon by `LineId`) so serialization is byte-stable
  (mirror `MapEInvoiceRecord`, `CanonicalMapper.cs:231`).
- **`CanonicalXml.cs`:** the XML twin.
- **`ImportPlan.cs`:** stage all four collections in a **new §-block** after §14 (the RCM/e-invoice/e-way
  staging, `ImportPlan.cs:943`), recorded in the **`journal`** so a rollback prunes them ⇒ **all-or-nothing**.
  Rehydrate each via its static `Rehydrate` (fail-fast invariant validation in pre-flight → a malformed record
  rejects the whole batch, `Applied = false`). **CRITICAL divergence from S4/S5:** snapshots + lines import
  **unconditionally** (they are external data, not orphans of a source voucher). Only the **recon**
  `matched_voucher_id` uses `MapVoucherOpt` (`ImportPlan.cs:949`) — an unresolved match (voucher not imported)
  → the pin is dropped and the line reads unmatched, never a hard failure.
- **`CompanyImportService`:** the all-or-nothing mirror is exercised through `ImportPlan`; add the new
  collections so an Io import cannot bypass the engine guards (there are none to bypass here — the records are
  pure data — but the invariant checks in `Rehydrate` still gate a malformed batch).

---

## 8. TDD test list (failing-first; ground in the four test projects)

Projects: `Apex.Ledger.Tests` (engine), `Apex.Ledger.Io.Tests` (round-trip), `Apex.Persistence.Sqlite.Tests`
(schema/migration), `Apex.Desktop.Tests` (UI). Each test written **red first**, proven to fail pre-fix.

**Import + parse (RQ-12)**
1. `Gstr2bJsonParser.Parse` is deterministic — a fixed 2B JSON fixture → byte-identical DTOs across runs
   (invariant culture, ordinal), incl. **B2B/CDNR/IMPG/ISD sections**.
2. Parser reads the **Cess** column on a tobacco line (FY2025-26 2B) into `CessPaisa` (paisa-exact).
3. Parser captures the **ITC-Available Y/N** bifurcation + `reverse_charge` flag per line.
4. `OfflineJsonConnector.FetchStatement` parses with **zero credentials**; `GspConnector.FetchStatement`
   throws `NotSupportedException`.
5. A malformed / wrong-recipient-GSTIN file **fails fast** (parsed=false), imports nothing.

**Reconciliation (RQ-13; DP-13)**
6. Golden fixture yields all four buckets: **Matched** (within tolerance), **PartialMismatch** (value beyond
   tolerance, variance recorded), **InPortalOnly** (2B row, no purchase), **InBooksOnly** (purchase, no 2B row).
7. **Tolerance configurable:** the same near-match is `Matched` at tolerance ₹1, `PartialMismatch` at ₹0.
8. **Determinism / 1-to-1:** two candidate vouchers for one 2B line → the stable pass matches exactly one;
   re-running yields the identical pairing.
9. **RCM inward excluded** from the books register (a reverse-charge purchase never appears as InBooksOnly).
10. **Composition** company: no ITC register (reconciler yields no books side).
11. Match-key normalisation: `"INV-001"` (books bill-ref) ↔ `"inv001"` (portal) matches after normalisation.

**§16(2)(aa) gate + §17(5) (RQ-15, RQ-26; DP-31)**
12. **§16(2)(aa):** a purchase not in 2B is flagged **ITC-ineligible this period** (InBooksOnly ⇒ gate view
    excludes it from claimable ITC).
13. **§17(5) per category:** a `BlockedSection17_5` motor-vehicle purchase → excluded from the eligible ITC
    pool → surfaced on the **Table 4(B)(1)/4(D)** advisory line; an `Eligible` purchase is unaffected.
14. `StockItemGstDetails.EnsureValid` rejects a `None` category with `BlockedSection17_5` eligibility (and
    vice-versa) — fail-fast.

**IMS (RQ-14; DP-14)**
15. IMS `SetAction` records Accept/Reject/Pending + `ActedOn`; `ClearAction` reverts to NoAction.
16. **Deemed-accept:** a line with no `ImsAction` reads `EffectiveStatus = Accepted` (derived, not stored).
17. **Oct-2025 CN:** Accept of a CDN line with a **partial declared reversal** stores the paisa + mandatory
    remarks; `no_reversal_declared` path stores the no-reversal declaration. **No reversal is posted.**
18. Bypass records (RCM / §16(4)) land in the non-actionable section; `SetAction` on one is rejected.

**Advisory-only (ER-14 — STRUCTURAL)**
19. After a full **import → reconcile → IMS accept/reject → §17(5) flag** cycle, **ledger balances,
    `entry_lines`, and every `Gstr3b` figure are byte-identical** to before the cycle (nothing posted). Assert
    `itc_reversals`/`gst_drc03` do not exist / are untouched (they are S7).
20. The reconciler exposes reversal **candidates** but the report has **no** posting method (compile-time /
    reflection assertion that no S6 type takes a `LedgerService` or emits `EntryLine`).

**Schema / migration (v42→v43)**
21. `SchemaMigrationEquivalenceTests` green — `CreateV1` == the v42→v43 migrate-chain (byte-identical DDL).
22. Legacy-DDL upgrade test (mirror `TdsSchemaTests`/the e-way schema test): a v42 DB upgrades to v43, existing
    rows untouched, new tables empty, new §17(5) columns default 0.
23. `DowngradeToV11` / `DowngradeToV9` drop all four new tables + strip the §17(5) columns; round-trip green.

**Io round-trip (RQ-23; ER-13)**
24. JSON + XML round-trip **paisa + count exact** for snapshots, lines, IMS actions, recon results, §17(5)
    flags.
25. `ImportPlan` all-or-nothing: a batch with one malformed line (e.g. a Matched recon with a dangling
    `matched_voucher_id` that fails invariant) rejects the **whole** batch (`Applied = false`).
26. Snapshots/lines import **even when no source voucher exists** (external data — not skipped as orphans);
    the recon pin resolves via `MapVoucherOpt` (dropped if the voucher is absent).

**ER-13 off + fixtures**
27. **ER-13:** a company that never imports 2B nor sets a §17(5) flag serialises **byte-identical** to a v42
    Phase-8/9-S5 company (new tables empty, new columns 0).
28. **Robert** (accounts-only) + **Bright** (trading) + the Phase-4 base-GST GSTR-1/3B fixtures stay green /
    byte-identical; the Phase-9 GST-advanced fixture's example **(E)** (2B reconciliation, advisory) reconciles.

---

## 9. Risks / subtleties (ranked)

1. **Inbound data auto-mutating the ledger (HIGHEST — ER-14).** The first inbound path must never post ITC or
   a reversal. Enforced **structurally** (§5): no posting surface in any S6 engine; staging tables physically
   separate; the structural before/after byte-identity test (test 19). A reconciler that posts would corrupt
   the ledger silently.
2. **The reconciliation match key.** GSTIN + normalised doc-no + date-window + value/tolerance. The books-side
   supplier doc-no comes from **bill-wise ref** (`BillAllocation.Name`), not a first-class field (surfaced gap,
   §2.3) — normalisation and the demote-to-Partial-without-a-ref rule must be exact and deterministic (tests
   6–8, 11). A non-deterministic or many-to-many match is a silent correctness bug.
3. **2B is the statutory ITC basis, not 2A (§16(2)(aa)).** The ITC-gate must read the **2B** snapshot; a 2A
   import is supplementary only. Conflating them would overstate claimable ITC.
4. **Determinism on a possibly-large 2B.** Thousands of lines; the parse and the greedy stable match must be
   O(n log n), invariant-culture, ordinal, allocation-lean — and reproducible for the golden test.
5. **Byte-identical when off (ER-13).** Four new tables empty + two new columns default 0; legacy-DDL +
   equivalence test per the exit gate; `DowngradeToV9/V11` drop them.
6. **RCM / §16(4) bypass.** RCM-tagged inward supplies and POS/time-bar-ineligible lines must be **excluded**
   from the reconcilable register (they bypass 2B/IMS, §2.7) — reuse `GstLineTax.IsReverseCharge`, exactly as
   `Gstr3b.ReadSide` already does (test 9, 18).
7. **§17(5) is per-purchase, config-driven.** The flag is a master property on the item/expense ledger's GST
   block, not derivable from the 2B — with a fail-fast `EnsureValid` (test 14).
8. **Snapshot immutability (ER-6).** A re-import creates a new versioned snapshot; no mutator edits a line.
   IMS/recon are separate mutable records keyed to the immutable line id.

---

## 10. Build recommendation — SPLIT into two commits (one PR gate)

The slice is large (parser + connector + reconciler + IMS + §17(5) + 4 tables + Io). Recommend **two coherent
conventional commits under the single PR-6 gate** (mirrors the S1/S8 "spine then facets" split):

- **S6a — inbound spine (import + staging + reconciliation).** Connector `FetchStatement` +
  `Gstr2bJsonParser` + `Gstr2bSnapshot`/`Gstr2bLine` + the two staging tables (`gstr2b_snapshots`,
  `gstr2b_lines`) + `gstr2b_recon` + `Gstr2bReconciler` (pure buckets + tolerance) + Io fold-in + the
  migration/downgrade + tests 1–11, 21–28. Lands the whole **schema v43** in one migration (add all four tables
  + the §17(5) columns now, even though IMS/§17(5) engines arrive in S6b — one version bump, tables can be
  empty; matches the "CDN/advance table lands in S2a, engine in S2b" precedent).
- **S6b — IMS mirror + §17(5) advisory.** `ImsAction`/`ImsService` + `ims_status` wiring + the
  `ItcEligibility`/`BlockedCreditCategory` flags + `ItcGateView` (books-vs-2B-vs-3B, Table 4(B) advisory) +
  the reversal-candidate surface for S7 + tests 12–20.

Both land behind **PR-6** with the full A10 3-lens pass (advisory-only ER-14 the top lens), A12 commits, CI
green across 3 OS. Advantage: the reconciliation spine (the risky, math-heavy core) is reviewable in isolation
before the IMS/§17(5) UI-facing facets pile on.

---

### Appendix — file manifest (create ● / edit ○)

- ● `src/Apex.Ledger/Domain/{Gstr2bSnapshot,Gstr2bLine,ImsAction}.cs`; new enums in
  `GstEnums.cs` (`GstStatementType`, `Gstr2bDocType`, `ImsStatus`, `ReconBucket`, `ItcEligibility`,
  `BlockedCreditCategory`) ○; `ReconTolerance` value object ●.
- ○ `src/Apex.Ledger/Domain/{StockItemGstDetails,GstConfig,Company}.cs` (§17(5) flags; tolerance config;
  the four new collections + Add/Remove/Find).
- ● `src/Apex.Ledger/Services/{Gstr2bReconciler,ImsService}.cs`; ● `src/Apex.Ledger/Reports/{Gstr2bReconciliationReport,ItcGateView}.cs`.
- ○ `src/Apex.Ledger.Io/{IGstPortalConnector,OfflineJsonConnector,GspConnector}.cs` (inbound `FetchStatement`);
  ● `src/Apex.Ledger.Io/Gstr2bJsonParser.cs`; ○ `CanonicalModel.cs`, `CanonicalMapper.cs`, `CanonicalXml.cs`,
  `ImportPlan.cs`, `CompanyImportService.cs`.
- ○ `src/Apex.Persistence.Sqlite/{Schema.cs (v43 + MigrateV42ToV43), SqliteCompanyStore.cs (== 42 step,
  Read*/Save* + stock/ledger column read/write)}`.
- ○ Tests: `Apex.Ledger.Tests`, `Apex.Ledger.Io.Tests`, `Apex.Persistence.Sqlite.Tests`
  (`ItemInvoiceRoundTripTests.DowngradeToV11`, `InventoryVoucherRoundTripTests.DowngradeToV9`,
  `SchemaMigrationEquivalenceTests`, a new `Gstr2bSchemaTests`), `Apex.Desktop.Tests`.
