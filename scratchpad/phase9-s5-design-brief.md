# Phase 9 — Slice S5 Design Brief: e-Way Bill (Rule 138, EWB-01/02 offline JSON + validity engine) — v41 → v42

> **Status:** DESIGN-ONLY (buildable implementation brief for the gated per-slice loop). No code written here.
> **Schema head is v41** (S1–S4a on disk, verified: `Schema.CurrentVersion == 41`, `MigrateV40ToV41` present,
> the S4a connectivity seam + e-invoice precedent all exist). S5 bumps **v41 → v42** in one additive migration.
> Governed by CLAUDE.md R1–R14; grounded in `docs/phase9-gst-advanced-requirements.md` **§2.6, §1.5, §3, §5
> "P9-S5", RQ-6, RQ-19, RQ-30, ER-13, ER-16, DP-12**. Every law figure carries an **A14 re-verify (R7)** gate.

**One-line thesis:** e-Way Bill is the *outbound-artefact sibling* of the S4a e-Invoice. It **reuses** — never
forks — the `IGstPortalConnector` seam, mirrors `EInvoiceRecord`/`EInvoiceService`/`EInvoiceJson` shape-for-shape,
and adds one thing e-Invoice did not have: a **validity/consignment-value engine**. The EWB number, like the IRN,
is **never computed locally** — it only ever arrives inbound from the portal (the S4a ER-5 discipline, applied to
EWB).

---

## 0. Grounding — the exact on-disk artefacts S5 mirrors / reuses

| Concern | On-disk file (verified) | S5 action |
|---|---|---|
| Connector seam (REUSE) | `src/Apex.Ledger.Io/IGstPortalConnector.cs` (`SubmitForIrn`/`CancelIrn` + `Inv01Request`/`IrnSubmissionResult`/`IrnCancelRequest`/`IrnCancelResult`) | **Extend** with e-Way ops + records — same interface, same 3 impls |
| Offline default | `src/Apex.Ledger.Io/OfflineJsonConnector.cs` (zero-cred, returns Pending) | Add e-Way members: stage request, return non-accepted |
| GSP stub | `src/Apex.Ledger.Io/GspConnector.cs` (every member throws `NotSupportedException`) | Add e-Way members: throw `NotBuilt` |
| Live path | `src/Apex.Desktop/Services/CustomerNicDirectConnector.cs` (fail-fast `EnsureConfigured`) | Add e-Way members: `EnsureConfigured()` then deferred throw |
| Secret store | `src/Apex.Ledger/Domain/INicCredentialStore.cs` + `NicApiCredentials` (redacted `ToString`) | **REUSE unchanged** — no new secret surface |
| Connector-mode enum | `GstConnectorMode` in `GstEnums.cs` (`OfflineJson=0`/`CustomerNicDirect=1`/`Gsp=2`) | REUSE — one company-level mode covers both e-Inv + e-Way |
| Artefact record (mirror) | `src/Apex.Ledger/Domain/EInvoiceRecord.cs` (transition-methods-only, `Rehydrate`, no ctor path to IRN) | `EWayBillRecord` twin |
| Engine (mirror) | `src/Apex.Ledger/Services/EInvoiceService.cs` (`CoverageOf`/`PrepareRecord`/`RecordIrpResponse`/`Cancel`) | `EWayBillService` twin |
| JSON writer (mirror) | `src/Apex.Ledger.Io/EInvoiceJson.cs` (deterministic, integer-paisa, `BuildInv01`/`BuildBatch`, fixed DTO order) | `EWayBillJson` twin |
| Value primitives (REUSE) | `src/Apex.Ledger/Reports/GstReportSupport.cs` (`InvoiceTaxableValue`, `OutwardSupplyValue`, `IntegratedRateOf`, `IsOutwardReverseChargeSupply`) | REUSE for consignment value |
| Cess (date-aware, REUSE) | `src/Apex.Ledger/Services/GstService.cs` (`ResolveCess` already drops non-tobacco cess on/after 22-Sep) | REUSE by **reading posted cess lines** (ER-9) |
| Schema (mirror) | `src/Apex.Persistence.Sqlite/Schema.cs` (`MigrateV40ToV41` = einvoice_records + companies cols) | `MigrateV41ToV42` twin |
| Persistence (mirror) | `SqliteCompanyStore.cs` (`ReadEInvoiceRecords`/`InsertEInvoiceRecords`, `EInvoiceRecord.Rehydrate`) | eway read/insert twin |
| Io canonical (mirror) | `CanonicalModel.cs` `EInvoiceRecordDto` + config fields; `CanonicalMapper.cs`; `CanonicalXml.cs` | `EWayBillRecordDto` twin |
| Import mirror (mirror) | `CompanyImportService` all-or-nothing rebuild | eway rebuild twin |
| Schema test (mirror) | `tests/…/EInvoiceSchemaTests.cs` (fresh-stamp / legacy-upgrade / round-trip) | `EWaySchemaTests` twin |
| Connector+JSON+Io test (mirror) | `tests/…/EInvoiceConnectorJsonTests.cs` | `EWayConnectorJsonTests` twin |
| Downgrade helpers (extend) | `InventoryVoucherRoundTripTests.cs` + `ItemInvoiceRoundTripTests.cs` `DowngradeToV11` | add `DROP … eway_*` |

**Design invariant carried from S4a → S5:** the pure engine **never branches on `ConnectorMode`** (mode-agnostic,
RQ-30). It builds the request and records whatever the connector hands back. The offline default therefore cannot
be "leaked into" by the live/GSP seams, and an offline taxpayer behaves exactly as the baseline.

---

## 1. e-Way config + consignment-value engine

### 1.1 F11 e-Way config (`GstConfig` additions, all default off/0 ⇒ ER-13 byte-identical)

Add to `GstConfig` (mirroring the S4a e-invoice block; **no secret field here** — creds flow only through
`INicCredentialStore`, ER-16):

```
public bool EWayBillEnabled { get; set; }                                    // F11 master gate
public DateOnly? EWayApplicableFrom { get; set; }                            // pre-date ⇒ Not-Applicable (mirror EInvoiceApplicableFrom)
public Money EWayThreshold { get; set; } = new Money(50_000m);               // Rule 138 default ₹50,000 (STRICT >)
public EWayConsignmentBasis ConsignmentBasis { get; set; } = EWayConsignmentBasis.Rule138Default;
public bool EWayIntraStateApplicable { get; set; } = true;                   // some states exempt intra-state entirely
// per-state / per-txn-type override table (collection, mirrors RateHistory/CessRates/RcmCategories pattern):
private readonly List<EWayStateThreshold> _eWayStateThresholds = new();
public IReadOnlyList<EWayStateThreshold> EWayStateThresholds => _eWayStateThresholds;
public void AddEWayStateThreshold(EWayStateThreshold t) => _eWayStateThresholds.Add(t ?? throw …);
```

- **`EWayThreshold` default ₹50,000** — note this is the **Rule-138 consignment threshold** (₹50,000 = 5,000,000
  paisa), **NOT** to be confused with the **₹5 cr AATO** that gates the *live NIC path* (that qualification lives
  at the composition root, §3.4, not as a turnover column).
- **`ConsignmentBasis`** enum (new, `GstEnums.cs`):
  `Rule138Default` (§15 taxable value + taxes + cess, **exempt excluded** — the statutory default),
  `TaxablePlusExempt` (add exempt-line value), `InvoiceValue` (whole invoice incl. exempt).
  The threshold check **always** includes taxes + cess; the basis only decides whether **exempt-supply value** is
  added in (§2.6: "value = §15 value incl. CGST+SGST+IGST+cess but EXCL. exempt-supply portion").
- **`EnsureValid` additions** (mirror the e-invoice guard): if `EWayBillEnabled` ⇒ require a GSTIN + an
  `EWayApplicableFrom`; a state-threshold row must carry a valid 2-digit state code and a non-negative paisa
  threshold.

### 1.2 New master types

```
// GstEnums.cs
public enum EWayConsignmentBasis { Rule138Default = 0, TaxablePlusExempt = 1, InvoiceValue = 2 }
public enum EWayTransactionType  { Regular = 0, JobWork = 1, Handicraft = 2, ... }   // drives mandatory-irrespective-of-value
public enum EWayTransportMode    { Road = 1, Rail = 2, Air = 3, Ship = 4 }           // NIC codes (Ship/multimodal ⇒ ODC 20 km rule)
public enum EWayStatus           { NotApplicable = 0, Pending = 1, Generated = 2, Cancelled = 3, Failed = 4, Expired = 5 }
                                   // mirrors EInvoiceStatus + an Expired terminal state the validity engine derives

// Domain/EWayStateThreshold.cs — mirrors GstCessRate/RcmCategory dated-master shape
public sealed record EWayStateThreshold(Guid Id, string StateCode, EWayTransactionType TxnType, Money Threshold);
```

Seed the known intra-state overrides (per §2.6, **A14 to re-verify each**): Delhi/Maharashtra/Bihar **₹1 L**,
Rajasthan **₹1 L / ₹2 L intra-city variants**, WB **₹50,000**. Seeding is opt-in (like `SeedAdvancedGst`) so an
un-provisioned company keeps the flat ₹50,000 default and stays byte-identical.

### 1.3 Consignment-value engine (`EWayBillService.ConsignmentValue`, pure, ER-9 — read POSTED lines, never recompute)

The value engine is a **pure projection over the posted voucher** — it does **not** re-run `ComputeInvoiceTax` or
`ResolveCess`. Everything it needs is already on the posted `GstLineTax` lines (this is why date-aware cess is
free, §1.4). Reuse the exact primitives `EInvoiceJson` already uses:

```
ConsignmentValue(company, voucher) =
    assessable            // §15 taxable value of TAXABLE supplies
  + cgst + sgst + igst    // posted forward tax (non-cess, non-RCM)
  + cess                  // posted ring-fenced cess (date-aware by construction)
  + exemptPortion?        // added ONLY when ConsignmentBasis != Rule138Default
```

- **`assessable`** = `GstReportSupport.InvoiceTaxableValue(voucher)` — already **excludes** `GstTaxHead.Cess`
  lines *and* `IsReverseCharge` lines, and (because exempt lines carry no tax line) already **excludes the
  exempt-supply portion**. This is precisely the Rule-138 "§15 value excl. exempt" component.
- **`cgst/sgst/igst`** = sum of posted non-cess, non-RCM tax-line `Amount`s (reuse the `ReadRateGroups` fold in
  `EInvoiceJson` — CGST/SGST/IGST per integrated-rate group, skipping `IsReverseCharge` and `GstTaxHead.Cess`).
- **`cess`** = sum of `GstTaxHead.Cess`, `IsReverseCharge:false` line `Amount`s (reuse
  `EInvoiceJson.ReadCessTotalPaisa` logic — lift it to a shared `GstReportSupport.PostedCessTotal(voucher)` so both
  writers share one implementation rather than duplicating it).
- **`exemptPortion`** (only for `TaxablePlusExempt`/`InvoiceValue`) = `OutwardSupplyValue(company, voucher, baseType).Total
  − .Taxable` (the exempt/nil value already split out by the composition engine's helper). `InvoiceValue` = the
  whole `.Total` + taxes + cess.
- All arithmetic in **integer paisa** at the boundary (`MoneyCodec.ToPaisa`), decimal rupees in-engine (ER-10).

### 1.4 Threshold applicability (`EWayBillService.CoverageOf`, mirror e-Invoice `CoverageOf`)

Returns an `EWayCoverage { Required, NotRequired, MandatoryIrrespectiveOfValue, NotApplicable }`:

1. `NotApplicable` when e-Way off / voucher pre-dates `EWayApplicableFrom` / voucher is not a goods-movement
   document (only `Sales` + goods `Purchase`/`CreditNote`/`DebitNote`/`Delivery/Receipt-note` movements carry an
   EWB; a pure-service invoice with no inventory movement does not).
2. `MandatoryIrrespectiveOfValue` when the voucher's `EWayTransactionType` is **inter-state
   principal↔job-worker** or **inter-state handicraft** — an EWB is required *regardless* of consignment value
   (§2.6). This short-circuits before the threshold test.
3. Else compute `ConsignmentValue`, resolve the **effective threshold** = the per-state/per-txn-type override for
   the place-of-supply state if one exists, else `EWayThreshold`. For an **intra-state** movement in a state that
   sets `EWayIntraStateApplicable = false`, return `NotRequired`.
4. **STRICT `>`**: `Required` iff `consignmentValue > effectiveThreshold`. **₹50,000.00 exactly ⇒ NotRequired**
   (§2.6 verifier: "> ₹50,000; ₹50,000.00 itself is NOT covered"). This boundary gets its own golden test.
5. **Rule 138E** (blocked generation on 2-period non-filing) **cannot be enforced offline** ⇒ surface a
   **warning-only** advisory (`bool IsRule138EWarning(...)` returning a flag + message the UI shows), never a hard
   block — mirrors `EInvoiceService.IsReportingAgeExceeded`.

**Date-aware cess is automatic (the subtlety worth stating).** Because S1's `ResolveCess` already returns `null`
(no cess line posted) for a non-tobacco item on/after 22-Sep-2025, a car sold 25-Sep has **no** cess line, so
`PostedCessTotal` reads ₹0 and the consignment value carries no cess — with **zero** date logic in S5. A car sold
20-Sep carries the posted cess line and the value includes it. S5 must **not** re-derive cess by date; reading the
posted lines is the whole mechanism (ER-9). (Belt-and-braces test: the 20-Sep vs 25-Sep golden pair.)

---

## 2. `EWayBillRecord` (Part A/B) + validity engine

### 2.1 `EWayBillRecord` — transition-methods-only artefact (twin of `EInvoiceRecord`, ER-5 discipline)

A mutable value-object-with-identity whose state advances `Pending → Generated → Cancelled/Failed`, plus an
`Expired` state the validity engine derives. **North star (the EWB number is the IRN's twin):** there is **no ctor
path and no method that derives `EwbNumber` or `ValidUpto`** — they arrive **only** inbound via
`RecordPortalResponse(...)` (what the connector handed back). Structural absence of any number-generation surface
is the guarantee (mirror `EInvoiceRecord`'s ER-5 comment).

```
public sealed class EWayBillRecord
{
    public Guid Id { get; }
    public Guid SourceVoucherId { get; }
    public string DocumentNumberUpper { get; }          // uppercased base doc no (mirror DocumentNumberOf)
    public EWayStatus Status { get; private set; }       // Pending initial

    // Part A (from the source voucher; assembled locally):
    public string? SupplyType { get; }                   // Outward/Inward
    public string? SubSupplyType { get; }                // Supply/Job Work/Export/...
    public string? DocType { get; }                      // INV/CRN/DBN/BIL/...
    public long ConsignmentValuePaisa { get; }           // computed §1.3, stored for audit
    // (Part-A item/HSN detail is emitted by the JSON writer from posted lines; not duplicated as columns)

    // Part B (transport — user-entered before generation):
    public string? TransporterId { get; private set; }   // TRANSIN / transporter GSTIN (15-char)
    public EWayTransportMode? Mode { get; private set; }
    public string? VehicleNumber { get; private set; }
    public int DistanceKm { get; private set; }          // approx distance driving the validity engine
    public string? TransportDocNo { get; private set; }
    public string? ShipFromStateCode { get; private set; }
    public string? ShipToStateCode { get; private set; }
    public bool IsOverDimensionalCargo { get; private set; }  // ODC / multimodal-ship ⇒ 20 km rule

    // Forward-compat (added NOW, gated to eff. 01-Aug-2026 — §2.6, DP-12):
    public string? ShipToGstin { get; private set; }     // mandatory from 01-Aug-2026
    public bool ClosureRequested { get; private set; }   // voluntary EWB "closure"
    public DateOnly? ClosedOn { get; private set; }

    // FROM the portal (never local, ER-5 twin):
    public string? EwbNumber { get; private set; }       // 12-digit EWB no — inbound only
    public DateTimeOffset? GeneratedAt { get; private set; }
    public DateTimeOffset? ValidUpto { get; private set; }
    public DateOnly? CancelledOn { get; private set; }
    public string? CancelReasonCode { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    // ctor: fresh Pending record; NO parameter that sets EwbNumber/ValidUpto (ER-5 twin).
    // static Rehydrate(...): copies portal-issued values verbatim; validates "Generated ⇒ EwbNumber present"
    //   and "Generated ⇒ ValidUpto present" (fail-fast on malformed import ⇒ all-or-nothing, RQ-23).
    // internal SetPartB(...), internal RecordPortalResponse(ewbNo, generatedAt, validUpto),
    // internal MarkCancelled(on, reason), internal MarkFailed(code, msg), internal MarkExtended(newValidUpto).
}
```

Company aggregate additions mirror the e-invoice set exactly: `_eWayBillRecords`, `EWayBillRecords`,
`AddEWayBillRecord`, `RemoveEWayBillRecord`, `FindEWayBillRecord(id)`, `FindEWayBillRecordForVoucher(voucherId)`.
(Unlike e-invoice, an EWB doc-no **can** legitimately be re-generated after cancellation, so there is **no**
`HasEWayDocumentNumber` reuse-block — the 24-h cancel simply frees the movement to be re-billed.)

### 2.2 Validity calculator (`EWayValidity`, pure, clock-free)

A pure static helper (caller supplies `generatedAt` — the engine is clock-free like `EInvoiceService.Cancel`'s
`today`). This is the one piece with **no** e-Invoice precedent, so it is fully specified here:

```
static DateTimeOffset ValidUpto(DateTimeOffset generatedAt, int distanceKm, bool odc)
{
    var per = odc ? 20 : 200;                                  // ODC/multimodal-ship = 1 day / 20 km; else 1 day / 200 km
    var days = Math.Max(1, (int)Math.Ceiling(distanceKm / (double)per));   // "or part thereof" ⇒ ceil, min 1
    // "One day" expires at MIDNIGHT of the day FOLLOWING generation (§2.6 verifier — CBIC active 4-row table):
    // day 1 window ends 00:00 of gen-date + 1; each further day adds a full midnight-to-midnight window.
    var firstMidnight = generatedAt.Date.AddDays(1);           // midnight following the generation day
    return new DateTimeOffset(firstMidnight.AddDays(days - 1), generatedAt.Offset);
}
```

- **Worked example (test anchor):** generated 2025-04-10 14:00, 250 km normal cargo ⇒ ceil(250/200)=2 days ⇒
  valid upto **midnight 2025-04-12 (00:00 on the 12th)**. Same trip ODC ⇒ ceil(250/20)=13 days.
- **Extension window:** `CanExtend(now, validUpto)` ⇒ `now ∈ [validUpto − 8h, validUpto + 8h]` (§2.6: 8 h before /
  after expiry). Extension re-runs `ValidUpto` for the remaining distance and **caps total life at 360 days** from
  first generation (`GeneratedAt + 360 days`, eff. 01-Jan-2025). An extend beyond the cap is refused (fail-fast).
- **Base-doc age block:** `EWayBillService.PrepareRecord` refuses a voucher whose date is **> 180 days** before
  the generation date (base-doc age limit, §2.6) — a clear domain error, not a silent skip.
- **`IsExpired(now, validUpto)`** ⇒ `now > validUpto`; the UI/report derives the `Expired` badge (the record's
  stored `Status` stays `Generated`; expiry is a *derived* view over `ValidUpto`, like a post-dated voucher — no
  clock write into the aggregate).

### 2.3 Part-B relaxation, cancel, accept/reject

- **Part-B required except intra-state ≤ 50 km** (consignor↔transporter or transporter↔consignee): `PrepareRecord`
  allows a `Generated`/submittable record with **no** Part-B iff `ShipFrom == ShipTo` (intra-state) **and**
  `DistanceKm ≤ 50`; otherwise Part-B (`Mode` + `VehicleNumber`/`TransportDocNo`) is mandatory before submission.
- **Cancellation 24 h:** `Cancel(record, today, reason)` — twin of `EInvoiceService.Cancel`; window
  `today ≤ GeneratedAt.Date + 1 day`; full-document only.
- **Accept/reject 72 h:** the *other-party* accept/reject is a **counter-party action** the offline clone cannot
  perform on the portal ⇒ model as an **advisory field** (`bool OtherPartyActionPending` + a 72-h deemed-accept
  note), never an enforced state transition. Surface it in the UI/report; do not auto-transition.

### 2.4 EWB-02 consolidated

`EWayBillService.PrepareConsolidated(vouchers, mode, vehicle, fromState)` groups multiple **already-generated**
EWB records travelling in one conveyance into a consolidated request; the **CEWB number** likewise arrives inbound
only. A consolidated record is a light header referencing the child `EwbNumber`s (no re-computation of value —
ER-9). Emitted by `EWayBillJson.BuildEwb02` (§4).

### 2.5 Ship-To GSTIN + closure — added now, gated to 01-Aug-2026

`const DateOnly EWayShipToMandatoryFrom = new(2026, 8, 1);` The `ShipToGstin`/`ClosureRequested`/`ClosedOn`
fields + columns exist from v42, but the **requirement** (`PrepareRecord` demands a Ship-To GSTIN) and the
**closure** operation only activate for `voucher.Date >= EWayShipToMandatoryFrom`. Before that date they are
optional/inert (byte-identical, ER-13, since default null/false).

---

## 3. Connectivity — REUSE the S4a `IGstPortalConnector` seam (do NOT fork)

### 3.1 Extend the interface (same file, additive)

```
// IGstPortalConnector.cs — ADD (e-Invoice members unchanged):
EwbSubmissionResult SubmitEway(Ewb01Request request);
EwbCancelResult     CancelEway(EwbCancelRequest request);
EwbExtendResult     ExtendEway(EwbExtendRequest request);
EwbSubmissionResult SubmitConsolidatedEway(Ewb02Request request);

public sealed record Ewb01Request(byte[] Json, string DocumentNumberUpper, Guid VoucherId);
public sealed record Ewb02Request(byte[] Json, IReadOnlyList<string> ChildEwbNumbers);
public sealed record EwbSubmissionResult(bool Accepted, string? EwbNumber, DateTimeOffset? GeneratedAt,
    DateTimeOffset? ValidUpto, string? ErrorCode, string? ErrorMessage);   // EwbNumber inbound only (ER-5 twin)
public sealed record EwbCancelRequest(string EwbNumber, string CancelReasonCode, string? Remarks);
public sealed record EwbCancelResult(bool Cancelled, string? ErrorCode, string? ErrorMessage);
public sealed record EwbExtendRequest(string EwbNumber, int RemainingDistanceKm, string Reason, string? Remarks);
public sealed record EwbExtendResult(bool Extended, DateTimeOffset? NewValidUpto, string? ErrorCode, string? ErrorMessage);
```

**ER-5 twin — the seam has deliberately NO `ComputeEwbNumber`/`SignEwb` member.** The 12-digit EWB number and the
validity only ever ride inbound on `EwbSubmissionResult`.

### 3.2 Wire the three existing impls (each gets the new members; no new class)

- **`OfflineJsonConnector`** (default, zero-cred): `SubmitEway`/`SubmitConsolidatedEway` return
  `Accepted:false, EwbNumber:null, …` (the caller keeps the request bytes for file export and later imports the
  portal write-back). `CancelEway`/`ExtendEway` stage the request. **Constructable with no arguments** — touches
  no secret store (ER-16 baseline).
- **`GspConnector`** (stub): every new member `throw new NotSupportedException(NotBuilt)`.
- **`CustomerNicDirectConnector`** (Desktop, live-but-deferred): every new member `EnsureConfigured()` then the
  deferred throw. Reuses the **same** `INicCredentialStore` + injected `HttpClient`; the live NIC e-Way call is a
  later build-time detail. Creds materialise only at the point of a call, never stored in the aggregate.

### 3.3 Write-back / import (offline default flow)

Mirror `EInvoiceService.RecordIrpResponse`: `EWayBillService.RecordPortalResponse(record, ewbNumber, generatedAt,
validUpto)` flips `Pending → Generated`, stores the number/validity **verbatim**. The offline path stays Pending
until the user imports the portal's write-back (JSON) — so the EWB number, like the IRN, arrives *inbound* even
offline (ER-5). `RecordFailure(record, code, msg)` for a portal rejection.

### 3.4 Live-path qualification (AATO > ₹5 cr) is a composition-root guard, not a turnover column

The **e-Way live NIC path is available only to AATO > ₹5 cr self-provisioners** (DP-12). This is enforced at the
Desktop composition root when resolving `ConnectorMode → IGstPortalConnector` (a config/eligibility check), **not**
by storing a turnover figure in the engine. Offline JSON stays the **default and fallback** for everyone else. The
pure core never references `CustomerNicDirectConnector` (it lives in the Desktop layer, reached only through the
interface) — an offline taxpayer behaves as if the live path did not exist.

---

## 4. `EWayBillJson` — deterministic, integer-paisa, de-branded (twin of `EInvoiceJson`)

A pure static writer following `EInvoiceJson` byte-for-byte in discipline: `System.Text.Json` only,
culture-invariant, **fixed property order** via `[JsonPropertyName]` DTOs, `WriteIndented`,
`UnsafeRelaxedJsonEscaping`, **UTF-8 no BOM**, **no clock / no RNG**, money as **integer paisa** at the boundary,
**de-branded** (ER-11 — a `DoesNotContain("Tally")` assertion). Values read off the **posted** lines (ER-9), never
recomputed.

```
public static class EWayBillJson
{
    private const string SchemaVersion = "1.03";   // NIC EWB-01 (A14 to confirm keys, R7 — mirror EInvoiceJson's schemaStatus flag)
    public static byte[] BuildEwb01(Company company, Voucher voucher, EWayBillRecord record);   // Part A + Part B
    public static byte[] BuildEwb02(Company company, IReadOnlyList<EWayBillRecord> children);   // consolidated
    // Optional: an EwbDtls sub-object builder reused by S4's INV-01 when "send e-Way with e-Invoice" is set
    //   (§2.6 bundling). Keep the Part-A assembly in a shared private helper so bundling is a later 1-line hook,
    //   not a rewrite. Do NOT modify EInvoiceJson in S5 unless the bundling toggle is pulled into scope.
}
```

- **EWB-01 DTO** (fixed order, mirror `Inv01Dto`): `Version`, `supplyType`, `subSupplyType`, `docType`,
  `docNo` (uppercased), `docDate`, `fromGstin`/`fromStateCode`, `toGstin`/`toStateCode`/`shipToGstin`,
  `itemList` (per posted line: HSN, qty-millis, unit, `taxable_amt_paisa`, `cgst/sgst/igst/cess_amt_paisa`,
  `gst_rate`), `totInvValue_paisa` (= consignment value §1.3), `transDistance`, `transporterId`, `transMode`,
  `vehicleNo`, `transDocNo`. Item apportionment reuses the exact `EInvoiceJson.BuildItems` value-share fold (last
  line absorbs the rounding remainder so Σ line tax == posted group tax to the paisa).
- **EWB-02 DTO**: `Version`, `fromStateCode`, `vehicleNo`, `transMode`, `cewbDate`, `ewbList` (the child
  `EwbNumber`s). No monetary recomputation — a consolidation is a header over generated children.
- **`transDistance`** and Part-B fields come from the `EWayBillRecord` (user-entered), not the posted ledger lines
  — the JSON is a join of the posted-line value view + the record's transport view.

---

## 5. Schema v42 (additive; BOTH `CreateV1` AND `MigrateV41ToV42`; `DowngradeToV11` DROP)

Mirror `MigrateV40ToV41` precisely. **Reuse the S4a `gst_connector_mode` + four `nic_*_enc` columns — no new
secret columns** (task-confirmed; one connector mode covers both e-Inv and e-Way).

```
-- Schema.cs: CurrentVersion 41 → 42; add MigrateV41ToV42 = the DDL below; ALSO append every line into CreateV1.

CREATE TABLE eway_bills (
    id                      TEXT    NOT NULL PRIMARY KEY,
    company_id              TEXT    NOT NULL REFERENCES companies(id),
    source_voucher_id       TEXT    NOT NULL REFERENCES vouchers(id),
    document_number_upper   TEXT    NOT NULL,
    status                  INTEGER NOT NULL DEFAULT 1,
    supply_type             TEXT        NULL,
    sub_supply_type         TEXT        NULL,
    doc_type                TEXT        NULL,
    consignment_value_paisa INTEGER NOT NULL DEFAULT 0,
    transporter_id          TEXT        NULL,
    trans_mode              INTEGER     NULL,
    vehicle_number          TEXT        NULL,
    distance_km             INTEGER NOT NULL DEFAULT 0,
    transport_doc_no        TEXT        NULL,
    ship_from_state_code    TEXT        NULL,
    ship_to_state_code      TEXT        NULL,
    is_odc                  INTEGER NOT NULL DEFAULT 0,
    ship_to_gstin           TEXT        NULL,   -- gated to 01-Aug-2026 (col exists now)
    closure_requested       INTEGER NOT NULL DEFAULT 0,
    closed_on               TEXT        NULL,
    ewb_number              TEXT        NULL,   -- FROM portal, never local
    generated_at            TEXT        NULL,
    valid_upto              TEXT        NULL,
    cancelled_on            TEXT        NULL,
    cancel_reason_code      TEXT        NULL,
    error_code              TEXT        NULL,
    error_message           TEXT        NULL
);
CREATE INDEX ix_eway_bills_company ON eway_bills(company_id);

-- per-state / per-txn-type override table (empty when off ⇒ ER-13):
CREATE TABLE eway_state_thresholds (
    id               TEXT    NOT NULL PRIMARY KEY,
    company_id       TEXT    NOT NULL REFERENCES companies(id),
    state_code       TEXT    NOT NULL,
    txn_type         INTEGER NOT NULL DEFAULT 0,
    threshold_paisa  INTEGER NOT NULL
);
CREATE INDEX ix_eway_state_thresholds_company ON eway_state_thresholds(company_id);

-- companies: e-Way config (non-secret; default off/0/₹50,000 ⇒ ER-13)
ALTER TABLE companies ADD COLUMN eway_bill_enabled          INTEGER NOT NULL DEFAULT 0;
ALTER TABLE companies ADD COLUMN eway_applicable_from       TEXT        NULL;
ALTER TABLE companies ADD COLUMN eway_threshold_paisa       INTEGER NOT NULL DEFAULT 5000000;  -- ₹50,000
ALTER TABLE companies ADD COLUMN eway_consignment_basis     INTEGER NOT NULL DEFAULT 0;        -- Rule138Default
ALTER TABLE companies ADD COLUMN eway_intrastate_applicable INTEGER NOT NULL DEFAULT 1;
-- (gst_connector_mode + nic_*_enc already exist from v41 — REUSED, not re-added)
```

- **`SqliteCompanyStore.EnsureSchema`**: add the `if (version == 41) { exec MigrateV41ToV42; version = 42; }` step
  (mirror the v40→v41 step at `SqliteCompanyStore.cs:980`). Read/insert `eway_bills` + `eway_state_thresholds`
  (twins of `ReadEInvoiceRecords`/`InsertEInvoiceRecords`); rehydrate via `EWayBillRecord.Rehydrate`. Add the eway
  config columns to the companies SELECT/UPSERT lists (mirror `SqliteCompanyStore.cs:1034` / `:3816`). Add
  `DELETE FROM eway_bills` + `DELETE FROM eway_state_thresholds` to the company-clear block (mirror `:3640`).
- **`SchemaMigrationEquivalenceTests`** enforces `CreateV1 == migrate-chain` — the DDL **must** be identical in
  both, so append to `CreateV1` in the same edit.
- **`DowngradeToV11`** helpers in `ItemInvoiceRoundTripTests.cs` **and** `InventoryVoucherRoundTripTests.cs`: add
  `DROP INDEX IF EXISTS ix_eway_bills_company; DROP TABLE IF EXISTS eway_bills; DROP INDEX IF EXISTS
  ix_eway_state_thresholds_company; DROP TABLE IF EXISTS eway_state_thresholds;` (the companies ALTERs are dropped
  by the existing full-companies-rebuild the downgrade already performs).

---

## 6. Io canonical (JSON + XML lossless; ER-13 off; secrets excluded; import all-or-nothing)

Mirror the S4a e-invoice fold-in exactly:

- **`CanonicalModel.cs`**: add `EWayBillRecordDto` (twin of `EInvoiceRecordDto` — all Part-A/B/validity/status
  fields; `GeneratedAt`/`ValidUpto` as ISO strings; **no** credential field) and
  `EWayStateThresholdDto`; add `IReadOnlyList<EWayBillRecordDto> EWayBillRecords => []` +
  `EWayStateThresholds => []` to the payload; add the eway config fields to the company DTO
  (`EWayBillEnabled`/`EWayApplicableFrom`/`EWayThresholdPaisa=5_000_000`/`ConsignmentBasis="Rule138Default"`/
  `EWayIntraStateApplicable=true`). Defaults chosen so an e-Way-**off** company serialises byte-identically (ER-13).
- **`CanonicalMapper.cs`**: map both directions (domain ⇄ DTO). The **portal-issued EWB number + validity ARE
  exported** (they are portal receipts, not secrets — same call as IRN/QR). The **only secret** (NIC creds) stays
  absent — it flows solely through `INicCredentialStore` (ER-16), and the mapper has no reference to it.
- **`CanonicalXml.cs`**: matching elements; JSON and XML must carry the same payload (the round-trip test asserts
  `CanonicalJson.Export(jm) == CanonicalJson.Export(xm)`).
- **`CompanyImportService`**: all-or-nothing rebuild — pre-flight validates every DTO (a `Generated` eway record
  with no `EwbNumber` or no `ValidUpto` **rejects the whole batch**, `Applied=false`, target untouched), then
  rebuilds via `EWayBillRecord.Rehydrate`. This is the recurring "Io-import bypassing engine guards" defect class —
  mirror `Import_rebuilds_a_generated_record_and_rejects_a_malformed_one_whole_batch`.

---

## 7. TDD test list (failing-first, in the gated loop order)

**A. Consignment-value engine** (`EWayValueTests` — new)
1. Pure taxable intra sale: consignment = taxable + CGST + SGST (paisa-exact, hand-worked).
2. Inter sale: consignment = taxable + IGST.
3. **> ₹50,000 STRICT boundary**: ₹50,000.00 consignment ⇒ `NotRequired`; ₹50,000.01 ⇒ `Required`.
4. **Per-state override**: same consignment under a Delhi ₹1 L override ⇒ `NotRequired`; WB ₹50k ⇒ `Required`.
5. **Cess date-aware**: identical car voucher 20-Sep (cess line posted) ⇒ value includes cess; 25-Sep (no cess
   line) ⇒ value excludes cess — proving the posted-line read, **no S5 date logic**.
6. **Exempt excluded**: mixed taxable+exempt invoice ⇒ `Rule138Default` value excludes the exempt line;
   `TaxablePlusExempt` includes it.
7. **RCM excluded** from forward consignment value (RCM lines never inflate it).
8. **Job-work / handicraft mandatory**: inter-state job-work below ₹50k ⇒ `MandatoryIrrespectiveOfValue`.
9. Intra-state in a `EWayIntraStateApplicable=false` state ⇒ `NotRequired`.

**B. Validity engine** (`EWayValidityTests` — new)
10. 250 km normal ⇒ 2 days ⇒ valid upto midnight of gen-date+2; 250 km ODC ⇒ 13 days.
11. 1 km ⇒ min 1 day; 200 km ⇒ 1 day; 201 km ⇒ 2 days (ceil / "part thereof").
12. **Midnight semantics**: gen 14:00 ⇒ day-1 window ends 00:00 next day (not gen+24h).
13. Extension window: `CanExtend` true only within ±8 h of expiry; extend re-computes for remaining distance.
14. **360-day cap**: an extension pushing past gen+360d is refused.
15. **180-day base-doc block**: `PrepareRecord` on a voucher dated > 180 d before gen ⇒ throws.
16. `IsExpired` derived over `ValidUpto` (no clock write into the aggregate).

**C. Part-B / lifecycle**
17. **Part-B ≤ 50 km intra-state relaxation**: intra + ≤50 km ⇒ submittable with no Part-B; > 50 km or inter ⇒
    Part-B mandatory (throws without it).
18. 24-h cancel window (twin of e-invoice cancel test); accept/reject 72-h advisory field set, no auto-transition.
19. **EWB-02 consolidated** over ≥2 generated children references their numbers; no value recomputation.
20. **Ship-To GSTIN gating**: voucher dated 2026-08-01 ⇒ Ship-To GSTIN required; earlier ⇒ optional/inert.

**D. Connector reuse + EWB-number-never-local** (`EWayConnectorJsonTests` — new, twin of `EInvoiceConnectorJsonTests`)
21. `OfflineJsonConnector.SubmitEway` works with **zero credentials**, returns `Accepted:false, EwbNumber:null`.
22. `GspConnector` every e-Way member throws `NotSupportedException`.
23. **No local EWB number**: the record has no ctor path / method producing `EwbNumber`; it is null until
    `RecordPortalResponse`; a `Generated` `Rehydrate` with null `EwbNumber` throws (ER-5 twin).
24. Engine is **mode-agnostic** (never branches on `ConnectorMode`).

**E. JSON writer**
25. `BuildEwb01` deterministic (byte-identical on repeat), integer-paisa, uppercased doc-no, **de-branded** (no
    "Tally"), accepts 40 % slab, post-22-Sep cess = 0.
26. `BuildEwb02` deterministic, references child EWB numbers.

**F. Schema + Io + regressions** (`EWaySchemaTests`, extend `EWayConnectorJsonTests`)
27. Fresh DB stamps to ≥ v42 with `eway_bills` + `eway_state_thresholds` + companies eway cols.
28. **Legacy v41 → v42** auto-migrates preserving every row; new tables empty; new cols default 0/₹50k/NULL;
    re-open idempotent (twin of `Legacy_v40_database_auto_migrates_to_v41`).
29. `SchemaMigrationEquivalenceTests` green (CreateV1 == migrate chain).
30. Save/load round-trips a Generated eway record + config (paisa/enum/date/validity exact).
31. **Io lossless** JSON+XML round-trip byte-stable; JSON payload == XML payload.
32. **ER-13 off**: an e-Way-off company is byte-identical; `EWayBillRecords` empty; Robert de-branded, no eway
    records.
33. **CompanyImportService** rebuilds a Generated eway record; a malformed one (Generated, no EwbNumber/ValidUpto)
    rejects the whole batch, target untouched.
34. **Robert + Bright + all prior fixtures green**; the Phase-4 base-GST GSTR-1/3B stays byte-identical; the S1–S4
    fixtures (cess, RCM, composition, e-invoice) stay green.

---

## 8. Risks / subtleties

1. **Date-aware cess must NOT be re-derived by date in S5.** The correct mechanism is reading the **posted** cess
   lines (`GstTaxHead.Cess`, `IsReverseCharge:false`) — S1's `ResolveCess` already dropped non-tobacco cess on/after
   22-Sep, so the value is date-aware for free (ER-9). A reviewer will flag any S5 code that consults the voucher
   date to decide cess — that is the S1 engine's job, already done at posting time. Lift the cess-total read into a
   shared `GstReportSupport.PostedCessTotal` so `EInvoiceJson` and `EWayBillJson` share one implementation (avoids
   a drift bug where the two disagree).
2. **Validity midnight/extension math is the only novel arithmetic** and the easiest to get wrong: "one day" is
   **not** gen + 24 h — it ends at **midnight of the day following generation**, then each additional day is a full
   midnight-to-midnight window (`ceil(dist/per)`, min 1). ODC flips 200→20 km. Extension is ±8 h around expiry with
   a hard 360-day cap. Every branch gets a hand-worked golden test (§7 B). Keep it a **pure clock-free** function
   (caller supplies `generatedAt`/`now`), like `EInvoiceService.Cancel(today)` — no `DateTime.Now` in the engine.
3. **EWB number never computed locally (ER-5 twin).** The single biggest correctness/fidelity risk is a well-meaning
   "generate a 12-digit EWB number" helper. There must be **no** such surface — the number only arrives inbound on
   `EwbSubmissionResult`/`Rehydrate`, exactly as the IRN does. The structural absence is the guarantee (assert it
   in test #23).
4. **REUSE — do NOT fork — the S4a connector seam.** Extend `IGstPortalConnector` + the three existing impls +
   the existing `INicCredentialStore`; do **not** create a parallel `IEWayConnector` or a second credential store.
   The `GstConnectorMode` enum and the `nic_*_enc` columns are shared. A parallel seam would violate RQ-30 and
   double the ER-16 attack surface.
5. **Per-state override correctness.** Strict `>` at the *effective* threshold (state override if present, else
   ₹50,000). The override resolves on the **place-of-supply** state (reuse `GstReportSupport.PlaceOfSupply`), and
   only for **intra-state** movements — an inter-state consignment always uses the ₹50,000 default (the state
   overrides are intra-state-only). Seed the overrides opt-in so an un-provisioned company stays byte-identical.
6. **Byte-identical when off (ER-13).** Every new column defaults 0/₹50,000/NULL; every new table empty; the eway
   config DTO defaults chosen so an e-Way-off company (and Robert/Bright) serialise unchanged. This is the hard
   gate — the S4a `E_invoicing_off_company_is_byte_identical` test is the exact template.
7. **Consignment threshold ≠ live-path AATO.** ₹50,000 is the Rule-138 consignment threshold; ₹5 cr is the AATO
   that *qualifies a taxpayer for the live NIC path* (DP-12) — enforced at the composition root, not a stored
   turnover. Do not conflate the two constants.
8. **Accept/reject + Rule 138E are inherently portal-side.** Model them as **advisory** (a flag + note), never an
   enforced offline state transition — mirror how the 2A/2B recon and `IsReportingAgeExceeded` stay advisory.

---

## 9. Recommendation — one build vs a split

**Recommend a single slice with two internal commits** (A12 small-commit discipline, R10) — do **not** split into
two schema versions (the plan fixes S5 at one v41→v42 bump). Sequence inside the loop:

- **Commit S5a — schema + record + connector-seam extension + Io (the persistence spine).** `EWayBillRecord`,
  the `GstConfig`/enum/master additions, `MigrateV41ToV42` (+ CreateV1 + EnsureSchema + read/insert +
  DowngradeToV11), the `IGstPortalConnector` e-Way extension across all three impls, and the Io canonical +
  CompanyImportService fold-in. Gate: schema tests (#27–29), Io round-trip (#31), ER-13 off (#32), import
  all-or-nothing (#33), connector reuse (#21–24), Robert/Bright green. This lands the v42 schema **complete at one
  version** and de-risks the migration before any compute lands.
- **Commit S5b — the value + validity + JSON engine.** `EWayBillService` (`ConsignmentValue`/`CoverageOf`/
  `PrepareRecord`/`RecordPortalResponse`/`Cancel`/`PrepareConsolidated`), `EWayValidity`, `EWayBillJson`
  (EWB-01/02), shared `PostedCessTotal`. Gate: value tests (#1–9), validity (#10–16), lifecycle (#17–20), JSON
  (#25–26), full regression (#34).

Rationale: the validity/consignment engine is the genuinely new work and the highest review-value target (A10's 3
lenses); isolating it behind an already-green schema+Io spine means a review or a session limit that hits S5b
cannot corrupt the migration, and the "what got built by which agent" verification (the S7 workflow-reliability
lesson) is cleaner. A single PR gate (PR-5) closes the slice per the plan.

---

### Appendix — file manifest (new vs. edited)

**New:** `Domain/EWayBillRecord.cs`, `Domain/EWayStateThreshold.cs`, `Services/EWayBillService.cs`,
`Services/EWayValidity.cs`, `Apex.Ledger.Io/EWayBillJson.cs`; tests `EWayValueTests.cs`, `EWayValidityTests.cs`,
`EWayConnectorJsonTests.cs`, `EWaySchemaTests.cs`.
**Edited:** `Domain/GstConfig.cs`, `Domain/GstEnums.cs`, `Domain/Company.cs`, `Reports/GstReportSupport.cs`
(shared `PostedCessTotal`), `Apex.Ledger.Io/IGstPortalConnector.cs`, `OfflineJsonConnector.cs`, `GspConnector.cs`,
`Apex.Desktop/Services/CustomerNicDirectConnector.cs`, `Apex.Persistence.Sqlite/Schema.cs`,
`SqliteCompanyStore.cs`, `Apex.Ledger.Io/CanonicalModel.cs`, `CanonicalMapper.cs`, `CanonicalXml.cs`,
`CompanyImportService.cs`; tests `ItemInvoiceRoundTripTests.cs` + `InventoryVoucherRoundTripTests.cs`
(`DowngradeToV11`).
