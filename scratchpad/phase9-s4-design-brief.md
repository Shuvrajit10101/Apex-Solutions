# Phase 9 · Slice S4 — e-Invoice (IRN/QR, INV-01 offline JSON) + the pluggable connectivity adapter + B2C dynamic QR (schema v40→v41)

**Design-only brief for the implementing engine agent.** Grounded in the actual code as it exists on
`claude/apex-phase-9-start-51d103` (worktree `wonderful-hellman-59520a`, schema head **v40** — S1/S2/S3
already on disk; memory.md is stale). Governs **RQ-5, RQ-18, RQ-28, RQ-30**; **ER-5, ER-9, ER-10, ER-11,
ER-13, ER-15, ER-16**. PR-4 gate per requirements §5 "P9-S4". **Do NOT run git. This is a build spec, not an
implementation.**

> **Design north star.** Two hard invariants dominate every decision in this slice:
> 1. **The IRN and the IRP-signed QR are NEVER computed or signed locally (ER-5).** The clone builds the
>    INV-01 *request* payload; the IRP (offline-imported or via the customer's own NIC creds) returns the IRN
>    / Ack / signed QR; the clone only *stores what it is handed*. There must be **no method anywhere that
>    SHA-256-hashes an IRN or signs a QR** — its structural absence is the guarantee.
> 2. **The app never holds a GSP/vendor credential nor the customer's portal password/DSC (ER-16); a NIC
>    secret never touches the plain canonical JSON/XML export.** The offline default works with **zero
>    credentials**; the optional live path uses ONLY the customer-configured NIC API creds, stored
>    **protected-at-rest** and structurally excluded from serialization.
>
> And the standing project rule: an **e-invoicing-off** company (no e-invoice config, no records, offline
> connector, no B2C-QR) must post, persist and serialise **byte-identically to a v40 company (ER-13)**.

---

## 0. Files you will touch (paths relative to worktree root)

| Layer | File | What |
|---|---|---|
| Domain enums | `src/Apex.Ledger/Domain/GstEnums.cs` | +`EInvoiceStatus`, +`EInvoiceExemptionClass` (`[Flags]`), +`GstConnectorMode`, +`EInvoiceSupplyCategory` |
| Domain records | `src/Apex.Ledger/Domain/EInvoiceRecord.cs` (**new**) | per-voucher IRP artefact (mirror `RcmDocument.cs`) |
| Domain config | `src/Apex.Ledger/Domain/GstConfig.cs` | +e-invoice config, +B2C-QR config, +connector mode; **NO secret fields here** |
| Domain config (secret seam) | `src/Apex.Ledger/Domain/INicCredentialStore.cs` (**new**) | abstraction over the protected-at-rest NIC creds — the ONLY channel a secret flows through |
| Domain aggregate | `src/Apex.Ledger/Domain/Company.cs` | `_eInvoiceRecords` list + `EInvoiceRecords`/`AddEInvoiceRecord`/`FindEInvoiceRecordForVoucher` (mirror `_rcmDocuments`, Company.cs:413/592) |
| Engine | `src/Apex.Ledger/Services/EInvoiceService.cs` (**new**) | applicability, INV-01 request assembly, `RecordIrpResponse`, 24-h `Cancel`, doc-no uppercasing |
| Engine | `src/Apex.Ledger/Services/B2cQrService.cs` (**new**) | pure UPI-QR projection; **zero** reference to `EInvoiceService`/`IGstPortalConnector` (ER-15) |
| Report feed | `src/Apex.Ledger/Reports/Gstr1.cs` | annotate B2B rows with the IRN of an IRN-tagged doc; a covered-vs-tagged reconciliation view |
| Connector seam | `src/Apex.Ledger.Io/IGstPortalConnector.cs` (**new**) | transport interface + request/response records |
| Connector (default) | `src/Apex.Ledger.Io/OfflineJsonConnector.cs` (**new**) | the built, zero-credential default |
| Connector (stubs) | `src/Apex.Ledger.Io/GspConnector.cs` (**new**) | seam present, throws — NOT built |
| INV-01 writer | `src/Apex.Ledger.Io/EInvoiceJson.cs` (**new**) | deterministic INV-01 emitter + `<2 MB` auto-split (sibling of `GstReturnJson.cs`/`FvuWriter.cs`) |
| Schema | `src/Apex.Persistence.Sqlite/Schema.cs` | `CurrentVersion`→41; `einvoice_records` in `CreateV1` **and** new `MigrateV40ToV41`; ALTER `companies` |
| Store | `src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs` | `EnsureSchema` step `version==40`; read/write `einvoice_records` + new company columns |
| Live path (infra, seam) | `src/Apex.Desktop/Services/CustomerNicDirectConnector.cs` (**new, seam**) + a DPAPI `INicCredentialStore` impl | live NIC transport — **wired but deferred** (the "build-time detail"); the pure core never references it |
| Io model | `src/Apex.Ledger.Io/CanonicalModel.cs` | +`EInvoiceRecordDto`; extend `GstConfigDto` (non-secret e-inv/B2C-QR/mode) |
| Io mapper | `src/Apex.Ledger.Io/CanonicalMapper.cs` | map records + config; **never map a secret** |
| Io XML | `src/Apex.Ledger.Io/CanonicalXml.cs` | `<eInvoiceRecords>` + config attrs |
| Io import | `src/Apex.Ledger.Io/ImportPlan.cs` | rebuild records + config (CompanyImportService delegates, unchanged) |
| Tests | `tests/Apex.Ledger.Tests/*`, `tests/Apex.Ledger.Io.Tests/*`, `tests/Apex.Persistence.Sqlite.Tests/EInvoiceSchemaTests.cs` (**new**), downgrade helper | see §6 |

`CanonicalJson.cs` needs **no code change** — it serialises the DTO records via `System.Text.Json`
(`DefaultIgnoreCondition.Never`); new members flow automatically and, defaulting off/empty/null, keep
byte-identity within the build (see §5).

---

## 1. The pluggable connectivity adapter (RQ-30, ER-16) — build the SEAM this slice

e-Invoice is the **first feature to use the transport seam**, so S4 defines it. Keep the seam **out of the
default path**: an offline taxpayer must behave exactly as if the seam did not exist.

### 1.1 Where each piece lives (respects the pure-core layering)
`Apex.Ledger` is framework/DB/clock/RNG-free; `Apex.Ledger.Io` is deterministic pure serialization. A live
HTTP call + an OS keystore **cannot** live in either. Split accordingly:

- **`Apex.Ledger.Io/IGstPortalConnector.cs`** — the interface + request/response records + the built
  **`OfflineJsonConnector`** (it only materialises/parses INV-01 bytes — pure, no I/O beyond byte buffers).
- **`Apex.Ledger.Io/GspConnector.cs`** — the **stubbed** seam: implements the interface, every method throws
  `NotSupportedException("GSP integration is not built in Phase 9 — configure OfflineJson or CustomerNicDirect.")`.
- **`Apex.Ledger/Domain/GstConnectorMode.cs`** (enum) — persisted selector on the company config.
- **`Apex.Desktop/Services/CustomerNicDirectConnector.cs`** — the **optional live** implementation (HTTP to
  the NIC IRP). Built as a **seam wired but deferred**: it depends on `INicCredentialStore` (below) + an
  injected `HttpClient`/`HttpMessageHandler`; until the DPAPI store + endpoints are provisioned it fails fast
  with a clear "live e-invoicing not configured" message. The **pure core never references this type** — it is
  reached only through `IGstPortalConnector` resolved at the Desktop composition root.

### 1.2 The interface (transport only — never business rules)
```csharp
namespace Apex.Ledger.Io;

/// Transport abstraction for GST-portal interchange (RQ-30). Pure contract; carries bytes + IRP artefacts,
/// never tax logic. e-Invoice uses SubmitForIrn/CancelIrn now; e-Way (S5) adds SubmitEwayBill/CancelEwayBill;
/// returns/2A/2B/IMS stay offline-only in Phase 9 (a future GSP impl fills the rest).
public interface IGstPortalConnector
{
    GstConnectorMode Mode { get; }

    /// Offline: writes the INV-01 request bytes for the user to upload, returns Pending (no IRN yet).
    /// Live (CustomerNicDirect): posts to the NIC IRP with the customer's own creds, returns the signed result.
    IrnSubmissionResult SubmitForIrn(Inv01Request request);

    /// Offline: stages a cancel request file. Live: calls the IRP cancel API. 24-h/full-doc rule is enforced
    /// by EInvoiceService BEFORE this is called (transport never re-decides policy).
    IrnCancelResult CancelIrn(IrnCancelRequest request);
}

public enum GstConnectorMode { OfflineJson = 0, CustomerNicDirect = 1, Gsp = 2 }   // (also in Domain; see §2.1)

public sealed record Inv01Request(byte[] Json, string DocumentNumberUpper, Guid VoucherId);
public sealed record IrnSubmissionResult(          // what the IRP hands back — stored verbatim (ER-5)
    bool Accepted, string? Irn, string? AckNo, DateOnly? AckDate, string? SignedQr, byte[]? SignedJson,
    string? ErrorCode, string? ErrorMessage);
public sealed record IrnCancelRequest(string Irn, string CancelReasonCode, string? Remarks);
public sealed record IrnCancelResult(bool Cancelled, string? ErrorCode, string? ErrorMessage);
```
- `OfflineJsonConnector.SubmitForIrn` returns `Accepted:false, Irn:null …` with the request bytes retained by
  the caller for file export (status stays **Pending** until the signed response is imported). The default
  connector **needs no credentials and touches no secret store** — that is the ER-16 baseline.
- The interface deliberately has **no** `ComputeIrn`/`SignQr` member. IRN/QR only ever arrive *inbound* on
  `IrnSubmissionResult` (ER-5 by construction).

### 1.3 Selecting the connector (config-driven)
`GstConfig.ConnectorMode` (default `OfflineJson`) selects the implementation at the Desktop composition root:
`OfflineJson → OfflineJsonConnector`, `CustomerNicDirect → CustomerNicDirectConnector` (Desktop),
`Gsp → GspConnector` (throws). The pure engine (`EInvoiceService`) is handed an `IGstPortalConnector` and is
**mode-agnostic** — it builds the request, hands it to the connector, and records whatever comes back. This is
why the offline default cannot be "leaked into" by the live/GSP seams: the engine never branches on mode.

### 1.4 Enforcing ER-16 structurally (not by convention)
- **The NIC secrets are never a `string` property on `GstConfig`** (which the CanonicalMapper serialises).
  They live only behind **`INicCredentialStore`** (Domain interface; DPAPI/OS-keystore impl in the infra
  layer), keyed by GSTIN:
  ```csharp
  public interface INicCredentialStore {                    // Domain seam; no secret ever crosses into Io
      bool HasCredentials(string gstin);
      NicApiCredentials Get(string gstin);                  // returns plaintext ONLY at the point of an HTTP call
      void Store(string gstin, NicApiCredentials creds);    // writes protected-at-rest (DPAPI/keystore)
      void Clear(string gstin);
  }
  public sealed record NicApiCredentials(string ClientId, string ClientSecret, string ApiUsername, string ApiPassword);
  ```
- **SQLite** stores the creds as **DPAPI-wrapped ciphertext BLOBs** (`nic_*_enc`, §4) — never plaintext,
  never a GSP/vendor cred, never the portal password/DSC.
- **The canonical JSON/XML export OMITS every credential column** (§5). The only connector-related thing that
  round-trips is the **mode** and the non-secret e-invoice/B2C config. A `CustomerNicDirectConnector` obtains
  creds only from `INicCredentialStore` at call time; they never enter the `Company` aggregate or a DTO.
- A test (§6.10) asserts the exported bytes contain **no** credential material and that `GstConfig` exposes no
  secret-bearing property.

---

## 2. e-Invoice engine + records (RQ-5, RQ-18; ER-5)

### 2.1 F11 e-Invoicing config (on `GstConfig`)
Add (all default off/null ⇒ ER-13):
```csharp
// --- e-Invoicing (RQ-5) ---
public bool EInvoicingEnabled { get; set; }
public DateOnly? EInvoiceApplicableFrom { get; set; }
public Money EInvoiceAatoThreshold { get; set; } = new Money(50_000_000m); // ₹5 cr, CONFIGURABLE (DP-11) — NOT ₹2cr
public bool EInvoiceApplicabilityOverride { get; set; }                    // sticky manual override
public EInvoiceExemptionClass ExemptionClasses { get; set; } = EInvoiceExemptionClass.None; // typed [Flags]
public bool ReportingAgeLimitApplies { get; set; }   // true only when AATO >= ₹10 cr — INDEPENDENT of applicability
// --- connectivity (RQ-30) ---
public GstConnectorMode ConnectorMode { get; set; } = GstConnectorMode.OfflineJson;
// --- B2C dynamic QR (RQ-28) — see §3 ---
public bool B2cDynamicQrEnabled { get; set; }
public Money B2cQrAatoThreshold { get; set; } = new Money(5_000_000_000m); // ₹500 cr, configurable (DP-28)
public string? B2cQrUpiId { get; set; }
public string? B2cQrPayeeName { get; set; }
```
`EInvoiceExemptionClass` `[Flags]`: `None=0, SezUnit=1, InsurerBankNbfc=2, Gta=4, PassengerTransport=8,
MultiplexCinema=16, GovtOrLocalAuthority=32, Oidar=64`. (SEZ **unit** is exempt; SEZ **developer** is NOT —
document this in the enum XML doc so it is not conflated.) `EnsureValid`: when `EInvoicingEnabled`, require a
GSTIN and `EInvoiceApplicableFrom`; when `B2cDynamicQrEnabled`, require `B2cQrUpiId` + `B2cQrPayeeName`.

### 2.2 Applicability — which documents are covered
`EInvoiceService.CoverageOf(Company, Voucher) → EInvoiceCoverage { Covered, Excluded, Exempt, NotApplicable }`:
- **Not applicable** if `!EInvoicingEnabled`, or `voucher.Date < EInvoiceApplicableFrom`, or the company is
  **Composition** (issues a Bill of Supply — never e-invoiced), or (AATO below threshold AND not overridden).
- **Exempt** if any `ExemptionClasses` flag matches the supplier's business class (regardless of turnover).
- **Covered** documents (RQ-18, §2.5): **B2B** (party has GSTIN, registered), **exports** (LUT / with-payment),
  **SEZ** supplies, **deemed exports**, **RCM supplier-liable**, **B2B credit/debit notes** (join
  `CreditDebitNoteLinks`). Supply category comes from a new `EInvoiceSupplyCategory` enum
  (`Regular/Export/SezWithPayment/SezWithoutPayment/DeemedExport/RcmSupplierLiable`) resolved from the party
  GST block + voucher.
- **Excluded** (never covered, §2.5): **B2C**, **Bill of Supply / composition**, **ISD**, **imports**.
  (B2C is where the B2C-dynamic-QR path picks up instead — §3.)
- The **30-day reporting-age** guard fires only when `ReportingAgeLimitApplies` (AATO ≥ ₹10 cr) and is
  computed against the document date **independently** of the ₹5 cr applicability — a warning surfaced by the
  service, not a hard block on the covered set.

### 2.3 The per-voucher record `EInvoiceRecord` (mirror `RcmDocument.cs`)
```csharp
public sealed class EInvoiceRecord {                 // value object with identity; framework/DB/clock-free
    public Guid Id { get; }
    public Guid SourceVoucherId { get; }
    public string DocumentNumberUpper { get; }       // the UPPERCASED doc-no used to build the IRN request
    public EInvoiceStatus Status { get; private set; } // NotApplicable/Pending/Generated/Cancelled/Failed
    public string? Irn { get; private set; }          // 64-char, FROM the IRP — never computed here
    public string? AckNo { get; private set; }
    public DateOnly? AckDate { get; private set; }
    public string? SignedQr { get; private set; }     // the IRP-signed QR string, stored verbatim
    public byte[]? SignedJson { get; private set; }   // the IRP-signed INV-01 response, stored verbatim
    public DateOnly? CancelledOn { get; private set; }
    public string? CancelReasonCode { get; private set; }
    // Transition methods only — no ctor path that sets Irn without an IRP response:
    internal void MarkPending();
    internal void RecordIrpResponse(string irn, string ackNo, DateOnly ackDate, string signedQr, byte[] signedJson);
    internal void MarkCancelled(DateOnly on, string reasonCode);
    internal void MarkFailed(string errorCode, string errorMessage);
}
public enum EInvoiceStatus { NotApplicable = 0, Pending = 1, Generated = 2, Cancelled = 3, Failed = 4 }
```
`Company`: `_eInvoiceRecords` list + `EInvoiceRecords`/`AddEInvoiceRecord`/`FindEInvoiceRecordForVoucher(Guid)`
(mirror Company.cs:413/592). One record per covered voucher (a doc-no, once used, is **never reused** even
after cancel — enforced in §2.5).

### 2.4 The flow (no local IRN/QR computation anywhere)
```
EInvoiceService.PrepareRequest(voucher):
   assert CoverageOf(...) == Covered  (else refuse — Excluded/B2C never enters here, ER-15)
   docNoUpper = UpperInvariant(DocumentNumberOf(voucher))     // uppercase BEFORE building the request
   json = EInvoiceJson.BuildInv01(company, voucher)           // deterministic request payload (§2.6)
   record = new EInvoiceRecord(..., docNoUpper); record.MarkPending(); company.AddEInvoiceRecord(record)
   return new Inv01Request(json, docNoUpper, voucher.Id)

// caller hands the request to the selected IGstPortalConnector; on the signed response:
EInvoiceService.RecordIrpResponse(record, IrnSubmissionResult r):
   if r.Accepted: record.RecordIrpResponse(r.Irn!, r.AckNo!, r.AckDate!.Value, r.SignedQr!, r.SignedJson!) → Generated
   else:          record.MarkFailed(r.ErrorCode!, r.ErrorMessage!)
```
- `DocumentNumberOf(voucher)` = the voucher's printed document number (voucher-type series prefix + `Number`,
  reuse `VoucherPrintProjector`'s existing formatting) — **uppercased** (IRP case-insensitive since
  01-Jun-2025). Store the uppercased value on the record so the request and the artefact agree.
- **There is no `ComputeIrn`.** The 64-char IRN can only appear via `RecordIrpResponse`. §6.4 asserts this.

### 2.5 24-h cancellation, no reuse, no amend-on-IRP
`EInvoiceService.Cancel(record, DateOnly today, string reasonCode)`:
- Require `record.Status == Generated` and `record.AckDate is {}` and `today <= AckDate + 1 day` (24-h window;
  since the engine is clock-free, `today` is passed in — mirror the RCM 30-day pattern). **Full document only**
  — no partial-line cancel exists.
- `record.MarkCancelled(today, reasonCode)`; the cancelled **doc-no is not reusable** — a new voucher must
  carry a fresh document number, and `PrepareRequest` refuses to build a second record for a doc-no already
  present on any (even cancelled) record (`FindEInvoiceRecordForVoucher` + a doc-no guard).
- **No amend-on-IRP**: there is no "modify IRN" method. Corrections route through **GSTR-1 amendment tables**
  (RQ-29, delivered in S8) — S4 only guarantees the *absence* of an amend path and that a cancelled/edited
  doc is reportable via the amendment feed later.

### 2.6 The INV-01 JSON writer + `<2 MB` auto-split (`EInvoiceJson.cs`)
Sibling of `GstReturnJson.cs` (S3) and `FvuWriter.cs` — the same determinism discipline: `System.Text.Json`
only, invariant culture, fixed property order, **no clock / no RNG**, money as **integer paisa** at the
boundary (`MoneyCodec.ToPaisa`, ER-10), UTF-8 no BOM, de-branded (ER-11). Pin the schema:
`private const string SchemaVersion = "1.1";` (NIC INV-01 v1.1) with a `schemaStatus` flag exactly like
`GstReturnJson.SchemaStatusFlag` (A14 to confirm the exact NIC key names at build — R7).

- **Object shape** (INV-01 sections): `Version`, `TranDtls` (SupTyp = B2B/EXPWP/EXPWOP/SEZWP/SEZWOP/DEXP,
  RegRev = Y for RCM), `DocDtls` (Typ = INV/CRN/DBN, **No = the uppercased doc-no**, Dt), `SellerDtls`,
  `BuyerDtls`, `ItemList[]` (SlNo, HsnCd, Qty, Unit, UnitPrice, TotAmt, AssAmt, **GstRt accepting 40**,
  IgstAmt/CgstAmt/SgstAmt, **CesRt/CesAmt/CesNonAdvlAmt trending to 0** post-22-Sep), `ValDtls`
  (AssVal/CgstVal/SgstVal/IgstVal/CesVal/TotInvVal), optional `EwbDtls` (bundled in S5).
- **Values are read off the posted tax lines** (`GstLineTax`), never recomputed (ER-9) — reuse
  `GstReportSupport.InvoiceTaxableValue` + the per-head reads already in `Gstr1.ReadInvoiceHeads`/
  `ReadInvoiceRateGroups`. Cess is read from the ring-fenced `GstTaxHead.Cess` line (S1).
- **Auto-split:** `EInvoiceJson.BuildBatch(company, vouchers) → IReadOnlyList<byte[]>` packs multiple covered
  vouchers' INV-01 objects into files, starting a **new part when the next object would push the part over the
  ~2 MB NIC bulk limit** (use a conservative byte budget, e.g. 2 000 000). Partitioning is **deterministic**:
  order vouchers by (document date, voucher number, id), greedily fill; each part is independently byte-stable.
  A single covered voucher ⇒ one part (so the golden-fixture test in §6.3 is trivial to pin).

### 2.7 GSTR-1 auto-population from IRN-tagged docs (RQ-18 feed)
`Gstr1.cs` already projects B2B rows from posted outward vouchers. Add a **thin, additive** join:
- Annotate each `Gstr1B2BRow` with `string? Irn` (default null) = the IRN of a **Generated**
  `EInvoiceRecord` for that voucher (join `FindEInvoiceRecordForVoucher`). A row with no record stays null
  (byte-identical to today for an off company, ER-13).
- Add a reconciliation projection (`Gstr1.EInvoiceReconciliation`): counts of **covered** B2B/export/SEZ/RCM/CDN
  docs vs docs with a **Generated** IRN vs **Pending/Failed** — the "GSTR-1 reconciles to IRN-tagged docs"
  gate. Advisory; recomputes each build (no persistence).
- The "Source/IRN tag cleared on edit" nuance: because GSTR-1 is a pure projection, an edited voucher simply
  no longer matches a stale record; keep S4 minimal — a Generated record whose voucher changed is surfaced as
  a **mismatch** in the reconciliation view (not auto-deleted). Do NOT add an edit-hook in S4.

---

## 3. B2C Dynamic QR (RQ-28, ER-15, Notn 14/2020-CT) — a self-generated, IRN-free path

**Entirely separate from §2.** A B2C invoice is *excluded* from e-invoicing (§2.2), so it never gets an
`EInvoiceRecord` and never touches `IGstPortalConnector`. The B2C QR is **self-generated** — a UPI/payment
QR the supplier builds — with **no IRN**.

### 3.1 Model — config columns + a pure projection (no table)
Per §3.2 of the requirements the B2C QR is "GstConfig + posted-doc projection" — so **no new table**; the
config lives in the columns added to `companies` in §2.1 (`b2c_dynamic_qr_enabled`, `b2c_qr_aato_threshold_paisa`,
`b2c_qr_upi_id`, `b2c_qr_payee_name`), and the payload is computed on demand.

`B2cQrService` (pure; **no** `using` of `EInvoiceService`/`IGstPortalConnector`):
```csharp
public sealed record B2cQrPayload(string UpiUri, Money Amount);   // carries NO Irn field — by construction (ER-15)

public B2cQrPayload? BuildFor(Voucher voucher):
   if !B2cDynamicQrEnabled                                    → null
   if AATO <= B2cQrAatoThreshold (and no override)            → null   // gated > ₹500 cr
   if voucher is NOT a B2C outward sale (party has GSTIN, or exempt class) → null
   amount = GstReportSupport invoice grand total (taxable + tax + cess)
   upi = $"upi://pay?pa={UpiId}&pn={UriEscape(Payee)}&am={amount:0.00}&cu=INR&tn={UriEscape(docNo)}"
   return new B2cQrPayload(upi, amount)
```
- Rendered on the **B2C invoice print only** (`VoucherPrintProjector`), never exported to the IRP.
- **ER-15 is structural**: `B2cQrPayload` has no IRN field; `B2cQrService` has no path to the connector or
  the e-invoice record set; e-invoice applicability *excludes* B2C. The two paths cannot be conflated because
  neither references the other's types. §6.8 asserts a B2C sale produces a QR + **no** `EInvoiceRecord`.

---

## 4. Schema v41 (ER-13; migration parity)

`src/Apex.Persistence.Sqlite/Schema.cs`:
1. `CurrentVersion = 40` → **`41`** (line 88).
2. **`CreateV1`:** add the `einvoice_records` `CREATE TABLE` + index; add the ALTER-equivalent columns inline
   in the `companies` `CREATE TABLE` block (after the composition columns, line 137).
3. **New `MigrateV40ToV41`** (mirror `MigrateV39ToV40`, line 2877) — pure additive DDL:
   ```sql
   CREATE TABLE einvoice_records (
       id                    TEXT    NOT NULL PRIMARY KEY,
       company_id            TEXT    NOT NULL REFERENCES companies(id),
       source_voucher_id     TEXT    NOT NULL REFERENCES vouchers(id),
       document_number_upper TEXT    NOT NULL,
       status                INTEGER NOT NULL DEFAULT 1,   -- EInvoiceStatus ordinal (Pending=1)
       irn                   TEXT        NULL,             -- 64-char, FROM the IRP; NULL until Generated
       ack_no                TEXT        NULL,
       ack_date              TEXT        NULL,             -- ISO yyyy-MM-dd
       signed_qr             TEXT        NULL,             -- IRP-signed QR, stored verbatim
       signed_json           BLOB        NULL,             -- IRP-signed INV-01 response, stored verbatim
       cancelled_on          TEXT        NULL,
       cancel_reason_code    TEXT        NULL
   );
   CREATE INDEX ix_einvoice_records_company ON einvoice_records(company_id);

   -- companies: e-invoice config (non-secret)
   ALTER TABLE companies ADD COLUMN einvoicing_enabled            INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE companies ADD COLUMN einvoice_applicable_from      TEXT        NULL;
   ALTER TABLE companies ADD COLUMN einvoice_aato_threshold_paisa INTEGER NOT NULL DEFAULT 500000000; -- ₹5cr
   ALTER TABLE companies ADD COLUMN einvoice_applicability_override INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE companies ADD COLUMN einvoice_exemption_classes    INTEGER NOT NULL DEFAULT 0; -- [Flags] ordinal
   ALTER TABLE companies ADD COLUMN einvoice_reporting_age_applies INTEGER NOT NULL DEFAULT 0;
   -- companies: connector mode (non-secret)
   ALTER TABLE companies ADD COLUMN gst_connector_mode            INTEGER NOT NULL DEFAULT 0; -- OfflineJson=0
   -- companies: B2C dynamic QR config (non-secret)
   ALTER TABLE companies ADD COLUMN b2c_dynamic_qr_enabled        INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE companies ADD COLUMN b2c_qr_aato_threshold_paisa   INTEGER NOT NULL DEFAULT 500000000000; -- ₹500cr
   ALTER TABLE companies ADD COLUMN b2c_qr_upi_id                 TEXT        NULL;
   ALTER TABLE companies ADD COLUMN b2c_qr_payee_name             TEXT        NULL;
   -- companies: NIC live-path creds — PROTECTED-AT-REST ciphertext BLOBs, NEVER plaintext, NEVER exported (ER-16)
   ALTER TABLE companies ADD COLUMN nic_client_id_enc            BLOB        NULL;
   ALTER TABLE companies ADD COLUMN nic_client_secret_enc        BLOB        NULL;
   ALTER TABLE companies ADD COLUMN nic_api_username_enc         BLOB        NULL;
   ALTER TABLE companies ADD COLUMN nic_api_password_enc         BLOB        NULL;
   ```
   Column definitions in `MigrateV40ToV41` must be **byte-identical** to those in `CreateV1` (the equivalence
   test compares name/type/notnull/default/pk). Note the `einvoice_aato_threshold_paisa` default (`500000000`
   = ₹5 cr) and the two ₹500-cr B2C defaults — pin them exactly in **both** places.
4. **The `nic_*_enc` columns are the ONLY secret-bearing storage** and are written **exclusively** by the
   infra `INicCredentialStore` DPAPI impl — the pure store's INSERT/SELECT of `companies` leaves them out (or
   passes them straight through as opaque bytes it never decrypts). They are **never** read into `GstConfig`.

`src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs`:
- **`EnsureSchema` stepper:** add a `version == 40` block running `Schema.MigrateV40ToV41`, bump
  `schema_version` to 41 (copy the shape of the `version == 39` block exactly).
- **Read:** add `ReadEInvoiceRecords(Guid)` (mirror `ReadRcmDocuments`, SqliteCompanyStore.cs:1945) and
  `company.AddEInvoiceRecord(...)` where RCM docs are attached (~line 1275). Extend the `companies` SELECT/
  hydration to populate the new **non-secret** e-invoice/B2C/mode config onto `GstConfig` (positional
  ordinals — **append** the new columns at the END of the `companies` SELECT and read at trailing ordinals;
  never insert mid-list). Do **not** read the `nic_*_enc` blobs into `GstConfig`.
- **Write:** add `InsertEInvoiceRecords(tx, c)` next to `InsertRcmRecords` (SqliteCompanyStore.cs:4358; FK
  vouchers ⇒ runs after `InsertVouchers`); extend the `companies` INSERT column list with the new config
  columns. `ack_date`/`cancelled_on` ⇒ ISO `yyyy-MM-dd` TEXT (`FormatDate`); `signed_json` ⇒ BLOB.

**Downgrade helper (test-only, ER-13 legacy DDL):** in the `DowngradeToV11` helper
(`ItemInvoiceRoundTripTests.cs`), add `DROP TABLE IF EXISTS einvoice_records;` (the new `companies` columns
ride the existing table-rewrite downgrade idiom — no per-column drop needed).

---

## 5. Io canonical model (RQ-23, ER-13, ER-16)

`CanonicalModel.cs`:
- New record beside `RcmDocumentDto` (line 310):
  ```csharp
  public sealed record EInvoiceRecordDto {
      public required Guid Id { get; init; }
      public required Guid SourceVoucherId { get; init; }
      public required string DocumentNumberUpper { get; init; }
      public required string Status { get; init; }            // EInvoiceStatus name
      public string? Irn { get; init; }
      public string? AckNo { get; init; }
      public string? AckDate { get; init; }                   // ISO or null
      public string? SignedQr { get; init; }
      public string? SignedJsonBase64 { get; init; }          // signed INV-01 response, base64 (an IRP artefact, NOT a secret)
      public string? CancelledOn { get; init; }
      public string? CancelReasonCode { get; init; }
  }
  ```
  Add `IReadOnlyList<EInvoiceRecordDto> EInvoiceRecords { get; init; } = [];` to the company DTO (beside
  `RcmDocuments`, line 299).
- `GstConfigDto`: add the **non-secret** e-invoice/B2C/mode members (`EInvoicingEnabled`,
  `EInvoiceApplicableFrom`, `EInvoiceAatoThresholdPaisa`, `EInvoiceApplicabilityOverride`,
  `EInvoiceExemptionClasses` (name), `EInvoiceReportingAgeApplies`, `ConnectorMode` (name),
  `B2cDynamicQrEnabled`, `B2cQrAatoThresholdPaisa`, `B2cQrUpiId`, `B2cQrPayeeName`), all defaulting off/empty.
- **DO NOT add any `Nic*` / credential member to any DTO.** The IRP-signed artefacts (IRN/QR/SignedJson) *are*
  exported — they are portal-issued receipts, not secrets. The **NIC API creds are the only secret** and they
  never appear in the canonical model — a deliberate omission (§1.4, ER-16). Note this in the `GstConfigDto`
  XML doc so a future maintainer does not "helpfully" add them.

`CanonicalMapper.cs`: map `EInvoiceRecords` (deterministic order: `.OrderBy(SourceVoucherId).ThenBy(Id)`) and
the new config; **the mapper has no access to `INicCredentialStore`** — it cannot serialise a secret even by
mistake.

`CanonicalXml.cs`: after `<rcmDocuments>`, emit `<eInvoiceRecords>` (`<record .../>` children); read back in
`ReadCompany`. Emit the config attributes on the GST-config element. `Opt(...)` for nullable attrs.

`CanonicalJson.cs`: **no change** (`DefaultIgnoreCondition.Never` ⇒ new members emit as
empty/false/null/`"OfflineJson"`/`"None"` for an off company ⇒ ER-13 byte-identical within the build).

`ImportPlan.cs`: in `BuildGstConfig`, set the new config members; add a `BuildEInvoiceRecords` loop
(`company.AddEInvoiceRecord(new EInvoiceRecord(Guid.NewGuid(), …))`, fresh ids, `ParseEnum<EInvoiceStatus>`,
`DateOnly.Parse`, `Convert.FromBase64String` for `SignedJson`). **CompanyImportService is unchanged** — it
delegates; a malformed record (e.g. `Status=Generated` with no IRN) makes `EnsureValid`/the record's transition
throw in pre-flight ⇒ `Applied = false`, target untouched (RQ-23, all-or-nothing). Importing a covered voucher
whose config says e-invoicing-on rebuilds its record without the pure core ever needing a live connector.

---

## 6. TDD test list (write failing-first)

**Connector seam (`tests/Apex.Ledger.Io.Tests`):**
1. **Offline is the default** — a fresh/enabled company resolves `ConnectorMode == OfflineJson`;
   `OfflineJsonConnector.SubmitForIrn` returns `Accepted:false, Irn:null` and needs **no credentials**
   (constructable with none).
2. **CustomerNicDirect seam present** — the mode + the config wire up; the connector type implements
   `IGstPortalConnector`; with no configured creds it fails fast with a clear "live e-invoicing not configured"
   (never a silent success, never a plaintext cred read).
3. **GspConnector stubbed** — every `IGstPortalConnector` member throws `NotSupportedException` (seam present,
   NOT built).

**INV-01 writer (`tests/Apex.Ledger.Io.Tests`):**
4. **Deterministic + paisa + fixture (C)** — the ₹50,000 @ 18% B2B sale (CGST ₹4,500 + SGST ₹4,500, inv value
   ₹59,000) serialises to INV-01; two calls are **byte-identical**; money is integer paisa; `DocDtls.No` is the
   **uppercased** doc-no; a 40%-slab line serialises `GstRt:40`; a post-22-Sep cess line serialises `CesAmt:0`.
5. **Auto-split `<2 MB`** — a batch that would exceed 2 MB splits into ≥2 deterministic parts, each
   `< 2 000 000` bytes and independently byte-stable; a single covered voucher ⇒ exactly one part.

**e-Invoice engine (`tests/Apex.Ledger.Tests`):**
6. **IRN/QR stored-from-IRP, never computed** — `RecordIrpResponse(irn, ack, qr, json)` stores them verbatim
   and flips `Generated`; assert **no** public/internal method computes a 64-char IRN or signs a QR (reflection
   scan: no `Sha256`/`ComputeIrn`/`SignQr` surface).
7. **24-h cancel + no reuse** — cancel at `AckDate + 1 day` succeeds → `Cancelled`; at `+2 days` throws;
   `PrepareRequest` refuses a second record for a doc-no already used by any (even cancelled) record; no
   amend-on-IRP method exists.
8. **Covered-doc scope** — B2B/export/SEZ/deemed-export/RCM/B2B-CDN ⇒ `Covered`; **B2C / composition
   Bill-of-Supply / ISD / import ⇒ Excluded**; an exempt class (SEZ-unit/GTA/…) ⇒ `Exempt`; the 30-day age
   guard fires only when `ReportingAgeLimitApplies` (₹10 cr), independent of the ₹5 cr threshold.
9. **GSTR-1 auto-populate from IRN-tagged** — a covered B2B invoice with a Generated record surfaces its IRN
   on the B2B row and counts as "tagged" in the reconciliation view; an off company's GSTR-1 is byte-identical
   (no IRN column populated).

**B2C QR (`tests/Apex.Ledger.Tests`):**
10. **Gated `> ₹500 cr` + never-in-IRN-flow (ER-15)** — B2C QR builds only when enabled + AATO > ₹500 cr on a
    B2C sale; a B2C sale produces a `B2cQrPayload` with **no IRN field** and creates **no** `EInvoiceRecord`;
    a B2B sale produces no B2C QR; `B2cQrService` has no reference to the connector/e-invoice types.

**Security (ER-16):**
11. **No secret in the canonical export; creds protected-at-rest** — a company with a configured (mock) NIC
    cred exports JSON+XML that contain **none** of the cred bytes; `GstConfig` exposes no secret-bearing
    property; the DPAPI `INicCredentialStore` round-trips a cred as ciphertext (the on-disk blob ≠ plaintext);
    `GspConnector` never accepts/holds a vendor credential.

**Persistence (`tests/Apex.Persistence.Sqlite.Tests`):**
12. **Migration parity + upgrade** — `SchemaMigrationEquivalenceTests` passes at v41 (CreateV1 == v1→v41 chain).
    New **`EInvoiceSchemaTests`** (mirror `GstRateHistoryCessSchemaTests`): (a) fresh DB has `einvoice_records`
    + every new `companies` column; (b) a legacy **v40** DB auto-migrates to v41 preserving rows, new columns
    default 0/NULL, new table empty (ER-13), re-open idempotent; (c) save/load round-trips a company with a
    Generated e-invoice record + e-invoice/B2C/mode config (paisa/enum/date exact); (d) the `nic_*_enc` blobs
    round-trip as opaque bytes and never surface on `GstConfig`.

**Io + regression:**
13. **Io lossless round-trip** — a company with e-invoice records + config round-trips through `CanonicalJson`
    and `CanonicalXml` byte-stable; the reconstructed `Company` equals the original.
14. **ER-13 byte-identical-off** — an e-invoicing-off company (Robert, Bright, a plain Phase-4 GST company)
    exports with `eInvoiceRecords` empty, config off, `connectorMode:"OfflineJson"`; assert bytes match a
    captured pre-S4 golden and the Phase-4 GSTR-1/GSTR-3B `TabularExport` goldens are **unchanged**.
15. **CompanyImportService all-or-nothing** — importing a covered voucher + Generated record rebuilds them;
    a malformed record (Generated with no IRN) rejects the whole batch (`Applied = false`, target unchanged).
16. **Robert + Bright green**; **de-brand scan clean** (ER-11 — no "Tally" in new code/JSON/QR/labels).

---

## 7. Risks / subtleties for the implementer

1. **IRN/QR must be stored-from-IRP, NEVER computed locally (ER-5).** Do not add a SHA-256 IRN helper "for
   offline testing" — the 64-char IRN must only ever arrive on `IrnSubmissionResult`. The reflection test (§6.6)
   is the guard. *(Top risk — it is the whole point of the feature.)*
2. **No NIC secret in the plain canonical export (ER-16).** Credentials live ONLY behind `INicCredentialStore`
   as DPAPI ciphertext in `nic_*_enc`; they are never a `GstConfig` string, never a DTO member, never in
   JSON/XML. The IRP-*signed* artefacts (IRN/QR/SignedJson) ARE exported — they are receipts, not secrets;
   don't confuse the two.
3. **Byte-identical when e-invoicing off (ER-13).** New `companies` columns default 0/NULL/₹5cr/₹500cr;
   `einvoice_records` empty; new DTO members default off/`"OfflineJson"`/`"None"`. Never reorder an existing
   DTO field, column, or the mapper's list ordering — an off company must serialise identically to a v40
   company. Pin the two AATO defaults identically in `CreateV1` AND `MigrateV40ToV41` (the equivalence test
   compares defaults byte-for-byte).
4. **Auto-split `<2 MB` must be deterministic.** Partition by a fixed order (doc date, number, id) with a
   conservative byte budget; each part independently byte-stable. A non-deterministic pack (dictionary order,
   clock, or a budget that flips on re-run) breaks the golden-fixture test and the byte-stability gate.
5. **The connector seam must not leak into the offline default.** `EInvoiceService` is mode-agnostic: it builds
   the request and records the response; it never branches on `ConnectorMode`. `OfflineJsonConnector` needs no
   creds and no store. An offline taxpayer must behave as if `CustomerNicDirect`/`Gsp` did not exist.
6. **B2C dynamic QR must never enter the IRN flow (ER-15).** B2C is *excluded* from e-invoice applicability, so
   a B2C sale gets no `EInvoiceRecord`; `B2cQrService` has no reference to the connector/e-invoice types and
   its payload carries no IRN field. Keep the two services in separate files with no cross-`using`.
7. **Covered-doc scope must exclude the composition Bill of Supply and B2C.** `CoverageOf` short-circuits on
   Composition (Bill of Supply) and B2C **before** the covered-set check — mirror the `Gstr1.Build`
   composition early-return (Gstr1.cs:156) so a composition dealer never mints an e-invoice.
8. **Uppercase the doc-no BEFORE building the request**, and persist the uppercased value on the record, so the
   request payload, the stored artefact, and any later cancel all reference the identical doc-no (IRP is
   case-insensitive from 01-Jun-2025, but the request must be normalised deterministically).
9. **Positional store readers.** `SqliteCompanyStore` reads `companies` by column ordinal — **append** the new
   config columns to the END of the SELECT and read at trailing ordinals; inserting mid-list silently corrupts
   every subsequent field. Do **not** read `nic_*_enc` into `GstConfig`.
10. **30-day reporting age is independent of the ₹5 cr applicability.** Model it as its own `ReportingAgeLimitApplies`
    flag (AATO ≥ ₹10 cr), never derived from the applicability threshold — a ₹6 cr taxpayer is covered but has
    no 30-day age limit.

---

## 8. One build, or a split? — **Recommend a split (S4a + S4b), single schema bump in S4a**

Recommend **S4a then S4b**, mirroring the S2a/S2b precedent (v39 schema complete in one bump; the CDN engine
followed in S2b):

- **S4a — connector seam + e-invoice core + all v41 schema.** `IGstPortalConnector` + `OfflineJsonConnector` +
  `GspConnector` stub + the `INicCredentialStore` seam; `EInvoiceService` (applicability, INV-01 request,
  `RecordIrpResponse`, 24-h `Cancel`, uppercase doc-no); `EInvoiceJson` writer + auto-split; `EInvoiceRecord` +
  the `companies` config; **the entire v41 migration** (e-invoice + B2C-QR + connector + `nic_*_enc` columns,
  since there is one migration per version); GSTR-1 IRN feed; Io round-trip. `CustomerNicDirectConnector` ships
  as the **wired-but-deferred** live seam (the DPAPI/HTTP internals are the acknowledged "build-time detail").
- **S4b — B2C dynamic QR.** `B2cQrService` + the print rendering, using config columns already added in S4a.
  Small, isolated, no schema.

**Why split:** (a) it keeps each A12 commit coherent (R10); (b) the ER-15 risk — conflating the IRP QR with the
B2C QR — is best mitigated by building the B2C path as its own commit that provably has no connector reference;
(c) the connector + e-invoice core is the bulk and the risk, and deserves its own focused A10 adversarial pass
before the smaller B2C-QR pass. Both land under the **single v41 schema** (S4a), so there is no extra migration.

---

## 9. Definition of Done (PR-4 gate, requirements §5)

Offline connector is the default + works with zero credentials · CustomerNicDirect seam present +
GspConnector stubbed · **the app holds no GSP/vendor credential and no portal password/DSC; NIC creds
protected-at-rest and absent from the canonical export (ER-16)** · INV-01 JSON validates against the pinned
NIC schema version (golden fixture), deterministic, integer-paisa, auto-split `<2 MB` · **IRN/QR stored
as-returned, never locally computed (ER-5)** · 24-h full-document cancel + doc-no not reusable + no
amend-on-IRP · covered-doc scope correct (B2B/export/SEZ/RCM/CDN; excludes B2C/BoS/ISD/import) · **B2C dynamic
QR self-generated, carries no IRN, never enters the IRP/INV-01 path (ER-15)** · GSTR-1 reconciles to
IRN-tagged docs · JSON byte-stable + de-branded · legacy-DDL v40→v41 upgrade preserves rows · migration parity
(CreateV1 == MigrateV40ToV41 chain) · **ER-13 byte-identical when e-invoicing off** · Io JSON+XML +
CompanyImportService round-trip lossless & all-or-nothing · **Robert + Bright green** · Phase-4 GSTR fixtures
byte-identical · de-brand scan clean. Then A10 adversarial pass (3 lenses) → A12 commits.
