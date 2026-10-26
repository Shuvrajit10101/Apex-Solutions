# Phase 9 · Slice S1 — GST 2.0 dated rate framework + Compensation-Cess seam (schema v37→v38)

**Design-only brief for the implementing engine agent.** Grounded in the actual code as it exists on
`claude/apex-phase-9-start-51d103` (worktree `wonderful-hellman-59520a`). Governs RQ-1, RQ-2, RQ-9; ER-1,
ER-2, ER-8, ER-9, ER-10, ER-11, ER-13. PR-1 gate per requirements §5 "P9-S1". **Do NOT run git. This is a
build spec, not an implementation.**

> **Design north star:** every change is *additive and date-aware*. An advanced-GST-**off** company (no cess
> rows, no dated rate history) must resolve, post, persist and serialise **byte-identically to a v37 Phase-8
> company** (ER-13). The way we guarantee that: (a) never alter an existing column/table/DTO field/ordering;
> (b) new tables start empty, new columns default 0/NULL, new DTO members default `[]`/null; (c) the new
> date-aware resolution path is a *pure override that only fires when a matching dated row exists* — absent
> any history/cess row, `ResolveRate`/`ComputeInvoiceTax` behave exactly as Phase-4/8 do today.

---

## 0. Files you will touch (all paths absolute-relative to worktree root)

| Layer | File | What |
|---|---|---|
| Domain enums | `src/Apex.Ledger/Domain/GstEnums.cs` | +`GstRateClass`, `GstValuationBasis`, `CessValuationMode` |
| Domain masters | `src/Apex.Ledger/Domain/GstRateHistoryEntry.cs` (**new**), `src/Apex.Ledger/Domain/GstCessRate.cs` (**new**) | dated rate-history + cess masters (mirror `GstRateSlab.cs`) |
| Domain config | `src/Apex.Ledger/Domain/GstConfig.cs` | +`RateHistory`, +`CessRates` lists + adders |
| Domain item/ledger GST | `src/Apex.Ledger/Domain/StockItemGstDetails.cs` | +cess/RSP/valuation-basis fields (covers **both** item `.Gst` and ledger `.SalesPurchaseGst` — same type) |
| Seed | `src/Apex.Ledger/Seed/SeedGstRates.cs` | +`BuildDefaultRateHistory()`, +`BuildDefaultCessRates()` (dated, 3 windows) |
| Engine | `src/Apex.Ledger/Services/GstService.cs` | date-aware `ResolveRate`; cess in `ComputeInvoiceTax`/`AddHead`; lazy Cess-ledger creation |
| Schema | `src/Apex.Persistence.Sqlite/Schema.cs` | `CurrentVersion`→38; `gst_rate_history`+`gst_cess_rates` in `CreateV1` **and** new `MigrateV37ToV38`; ALTER `stock_items`/`ledgers` |
| Store | `src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs` | `EnsureSchema` step `version==37`; read/write of the two new tables + new item/ledger columns |
| Io model | `src/Apex.Ledger.Io/CanonicalModel.cs` | +`GstRateHistoryDto`, +`GstCessRateDto`; extend `GstConfigDto`, `StockItemGstDto` |
| Io mapper | `src/Apex.Ledger.Io/CanonicalMapper.cs` | extend `MapGstConfig`, `MapStockItemGst` |
| Io XML | `src/Apex.Ledger.Io/CanonicalXml.cs` | extend `BuildGstConfig`/`ReadGstConfig`, `BuildStockItemGst`/`ReadStockItemGst` |
| Io import | `src/Apex.Ledger.Io/ImportPlan.cs` | extend `BuildGstConfig`, `BuildStockItemGst` (CompanyImportService itself unchanged — it delegates) |
| Tests | `tests/Apex.Ledger.Tests/*`, `tests/Apex.Persistence.Sqlite.Tests/GstRateHistoryCessSchemaTests.cs` (**new**), `tests/Apex.Ledger.Io.Tests/*`, `tests/Apex.Persistence.Sqlite.Tests/ItemInvoiceRoundTripTests.cs` (Downgrade helper) | see §5 |

`CanonicalJson.cs` needs **no code change** — it serialises the DTO records via `System.Text.Json`
(`ExportOptions`, `DefaultIgnoreCondition.Never`); the new DTO members flow automatically and, being
`[]`/null when off, keep byte-identity within the build (see §4).

---

## 1. Dated rate-history master (RQ-1, ER-1)

### 1.1 Problem with today's model
Rates live in `gst_rate_slabs (id, company_id, rate_bp, label, is_predefined)` — a **flat, undated** pick-list
(`GstConfig.RateSlabs`, `GstRateSlab.cs`). `GstService.ResolveRate(item, salesPurchaseLedger)`
(GstService.cs:157) resolves *most-granular-wins* from the item's/ledger's scalar `RateBasisPoints` — there is
no notion of a voucher date, so the same car cannot be 28% on 20-Sep-2025 and 40% on 25-Sep-2025.

### 1.2 New table `gst_rate_history` (company-level, HSN-keyed, dated)
Add a **companion** table (do **not** repurpose or drop `gst_rate_slabs` — it stays as the UI pick-list, so an
off/legacy company's serialisation is untouched). One row = one dated rate applicability window.

```sql
CREATE TABLE gst_rate_history (
    id               TEXT    NOT NULL PRIMARY KEY,
    company_id       TEXT    NOT NULL REFERENCES companies(id),
    hsn_sac          TEXT        NULL,   -- specific HSN/SAC carve-out; NULL = generic slab (0/5/18/40, specials)
    rate_bp          INTEGER NOT NULL,   -- integrated rate in basis points (4000 = 40%)
    rate_class       INTEGER NOT NULL,   -- GstRateClass ordinal (Standard/Merit/Special/DeMerit/CarveOut/Legacy)
    effective_from   TEXT    NOT NULL,   -- ISO yyyy-MM-dd, inclusive
    effective_to     TEXT        NULL,   -- ISO yyyy-MM-dd, inclusive; NULL = open-ended
    valuation_basis  INTEGER NOT NULL DEFAULT 0,  -- GstValuationBasis ordinal (TransactionValue=0, RetailSalePrice=1)
    label            TEXT    NOT NULL,
    is_predefined    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_gst_rate_history_company ON gst_rate_history(company_id);
```

- **Keys/indexes:** PK `id`; isolation FK `company_id`; `ix_gst_rate_history_company` (mirrors every existing
  `ix_*_company`). No composite unique constraint (Tally allows overlapping user rows; resolution is
  deterministic by "most-specific + latest `effective_from`", §1.4).
- **Content (seeded, A14 to re-verify exact numbers/dates at build — R7):**
  - Generic (`hsn_sac` NULL): `0/500/1800/4000` bp, class `Standard/Merit/…/DeMerit`, `effective_from =
    2025-09-22`, `effective_to = NULL`. Retained specials `300/150/25` bp (3%/1.5%/0.25%), class `Special`,
    open-ended. **Legacy `1200/2800` bp, class `Legacy`, `effective_from = 2017-07-01`,
    `effective_to = 2025-09-21`** — kept *inactive-by-date*, never deleted (a pre-22-Sep voucher must
    reprint at the historic rate).
  - HSN carve-outs: `8703` (car) → two rows: `2800 bp` `[2017-07-01 … 2025-09-21]` and `4000 bp`
    `[2025-09-22 … open]`. Tobacco/pan-masala HSNs (`2402` cigarettes, `2403` chewing, `21069020` pan
    masala) → `2800 bp`, class `CarveOut`, `valuation_basis = RetailSalePrice`, open-ended (the "28%+cess"
    carve-out that did **not** move to 40%).

### 1.3 Domain: `GstRateHistoryEntry` (mirror `GstRateSlab.cs`) + `GstConfig`
```csharp
public sealed class GstRateHistoryEntry {          // immutable, framework/DB-agnostic
    public Guid Id { get; }
    public string? HsnSac { get; }                 // NULL = generic slab
    public int RateBasisPoints { get; }            // >= 0
    public GstRateClass RateClass { get; }
    public DateOnly EffectiveFrom { get; }
    public DateOnly? EffectiveTo { get; }           // null = open
    public GstValuationBasis ValuationBasis { get; }
    public string Label { get; }
    public bool IsPredefined { get; }
    public bool IsEffectiveOn(DateOnly d) =>
        d >= EffectiveFrom && (EffectiveTo is null || d <= EffectiveTo);   // both bounds INCLUSIVE
}
```
`GstConfig.cs`: add a private `List<GstRateHistoryEntry> _rateHistory`, public
`IReadOnlyList<GstRateHistoryEntry> RateHistory => _rateHistory`, `void AddRateHistory(GstRateHistoryEntry e)`
(mirrors `AddRateSlab`, line 42). No change to `EnsureValid` for S1 (dated rows are advisory data).

### 1.4 Rate resolution changes in `GstService`
Add a **date-aware overload** that *layers on top of* today's resolution so existing fixtures are untouched:

```csharp
// Existing signature stays and delegates (so no caller is forced to change if it has no date):
public RateResolution ResolveRate(StockItem? item, Ledger? spLedger)
    => ResolveRate(item, spLedger, voucherDate: null);

public RateResolution ResolveRate(StockItem? item, Ledger? spLedger, DateOnly? voucherDate) {
    // 1) EXACTLY today's logic first (item scalar -> ledger scalar -> unresolved sentinel).
    var baseRes = ResolveBase(item, spLedger);          // = current body of ResolveRate (GstService.cs:157-177)

    // 2) DATE OVERRIDE — only when a voucher date AND a matching HSN-dated history row both exist.
    //    Absent either, return baseRes unchanged => byte-identical to Phase-4/8.
    if (voucherDate is { } d && baseRes.IsTaxable && (item?.Gst?.HsnSac ?? spLedger?.SalesPurchaseGst?.HsnSac) is { } hsn) {
        var hit = _company.Gst?.RateHistory
            .Where(h => h.HsnSac == hsn && h.IsEffectiveOn(d))
            .OrderByDescending(h => h.EffectiveFrom).ThenByDescending(h => h.Id)   // most-recently-effective wins
            .FirstOrDefault();
        if (hit is not null)
            return RateResolution.Taxable(hit.RateBasisPoints) with { ValuationBasis = hit.ValuationBasis };
    }
    return baseRes;
}
```
- Extend the `RateResolution` record (GstService.cs:142) with a **`GstValuationBasis ValuationBasis`** member
  (default `TransactionValue`) so the caller learns the RSP flag. `Taxable(int)`/`NonTaxable(...)` factories
  default it to `TransactionValue` — keeps every existing construction call valid.
- **Preserve** `IsUnresolved` sentinel behaviour (line 180) and the `-1` bp sentinel.
- The compute-total-then-split (`ComputeLineTax`, `ComputeInvoiceTax`) and `RoundToPaisa` are **unchanged**;
  only the *rate that flows in* becomes date-aware.
- **Call-site threading (minimal):** `VoucherEntryViewModel.ComputeItemInvoiceGst()`
  (VoucherEntryViewModel.cs:1185-1211) and the additional-cost path (~1269) call
  `_gst.ResolveRate(l.SelectedItem, valueLedger)` — pass the voucher's `Date`. The VM already knows the
  voucher date; thread it as `ResolveRate(item, ledger, VoucherDate)`. `ImportPlan` posts *already-computed*
  tax lines, so it does not call `ResolveRate`.

### 1.5 Seeding
`SeedGstRates.cs` gains `BuildDefaultRateHistory()` returning the §1.2 dated rows (fresh ids each call, like
`BuildDefaults`, line 23). `GstService.EnableGst` (line 60): after the existing slab seed (line 67), seed the
history when empty: `if (config.RateHistory.Count == 0) foreach (var e in SeedGstRates.BuildDefaultRateHistory()) config.AddRateHistory(e);`.
Keep the existing `RateSlabs` seed **exactly as is** (the pick-list is unchanged → off-company byte-identity).

---

## 2. Compensation-Cess seam (RQ-2, RQ-9, ER-2)

### 2.1 New table `gst_cess_rates` (company-level, HSN-keyed, dated, 3 valuation modes)
```sql
CREATE TABLE gst_cess_rates (
    id                    TEXT    NOT NULL PRIMARY KEY,
    company_id            TEXT    NOT NULL REFERENCES companies(id),
    hsn_sac               TEXT        NULL,   -- goods this cess applies to; NULL = generic
    valuation_mode        INTEGER NOT NULL,   -- CessValuationMode ordinal (AdValorem=0, Specific=1, RetailSalePriceFactor=2)
    cess_rate_bp          INTEGER NOT NULL DEFAULT 0,  -- ad-valorem % in bp (0 unless AdValorem)
    cess_per_unit_paisa   INTEGER NOT NULL DEFAULT 0,  -- specific per-unit amount, paisa (0 unless Specific)
    cess_rsp_factor_millis INTEGER NOT NULL DEFAULT 0, -- RSP multiplier x1000 (e.g. 0.32R -> 320; 0 unless RspFactor)
    effective_from        TEXT    NOT NULL,   -- ISO yyyy-MM-dd, inclusive
    effective_to          TEXT        NULL,   -- ISO yyyy-MM-dd, inclusive; NULL = open
    label                 TEXT    NOT NULL,
    is_predefined         INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_gst_cess_rates_company ON gst_cess_rates(company_id);
```
- **Three dated FY2025-26 windows** (resolved by voucher date; A14 to re-verify — R7):
  - **(a) 2025-04-01 … 2025-09-21:** cess on de-merit goods (car `8703` ad-valorem, aerated `2202` 12%, coal
    `2701` **specific** ₹400/tonne, tobacco).
  - **(b) 2025-09-22 … 2026-01-31:** **only tobacco/pan-masala** retain cess (RSP-factor / specific);
    **no rows** for cars/aerated/coal ⇒ they resolve to zero cess automatically.
  - **(c) from 2026-02-01:** **no rows at all** ⇒ nil cess on everything incl. tobacco.
- Seeded by `SeedGstRates.BuildDefaultCessRates()`; `EnableGst` seeds it when `config.CessRates.Count == 0`.

### 2.2 Domain `GstCessRate` (mirror `GstRateSlab`) + `GstConfig.CessRates`
```csharp
public sealed class GstCessRate {
    public Guid Id { get; }
    public string? HsnSac { get; }
    public CessValuationMode ValuationMode { get; }
    public int CessRateBasisPoints { get; }      // ad-valorem
    public Money CessPerUnit { get; }            // specific per-unit (paisa-exact)
    public int CessRspFactorMillis { get; }      // RSP factor x1000
    public DateOnly EffectiveFrom { get; }
    public DateOnly? EffectiveTo { get; }
    public string Label { get; }
    public bool IsPredefined { get; }
    public bool IsEffectiveOn(DateOnly d) => d >= EffectiveFrom && (EffectiveTo is null || d <= EffectiveTo);
}
```
`GstConfig`: add `IReadOnlyList<GstCessRate> CessRates` + `AddCessRate(...)` (mirror `AddRateSlab`).

### 2.3 Per-item cess/RSP fields on `StockItemGstDetails`
This one value object is shared by the item (`StockItem.Gst`) **and** the sales/purchase ledger
(`Ledger.SalesPurchaseGst`), so extending it once covers both. Add:
```csharp
public bool CessApplicable { get; set; }                       // gate: item bears cess
public CessValuationMode? CessValuationMode { get; set; }      // null => inherit from gst_cess_rates by HSN+date
public int? CessRateBasisPoints { get; set; }                  // ad-valorem override
public Money? CessPerUnit { get; set; }                        // specific override
public int? CessRspFactorMillis { get; set; }                  // RSP-factor override
public Money? RetailSalePrice { get; set; }                    // declared RSP (per unit) — drives RSP-factor cess + RSP GST valuation
public GstValuationBasis ValuationBasis { get; set; } = GstValuationBasis.TransactionValue; // RQ-1 per-item RSP flag
```
Extend `EnsureValid()` (line 37): RSP-factor cess requires `RetailSalePrice`; ad-valorem requires a
non-negative `CessRateBasisPoints`; specific requires a non-negative `CessPerUnit`.

### 2.4 Cess computation in `GstService.ComputeInvoiceTax` / `AddHead`
- **Input:** extend `TaxableLine` (GstService.cs:231) to carry the resolved cess + the quantity (needed for
  specific/RSP): `readonly record struct TaxableLine(Money TaxableValue, int IntegratedBasisPoints,
  CessCharge? Cess = null)` where `CessCharge` = `(CessValuationMode Mode, int RateBp, Money PerUnit,
  int RspFactorMillis, Money Rsp, decimal Quantity)`. The optional default `= null` keeps every existing
  `new TaxableLine(value, bp)` construction valid.
- **Per-line cess amount** (compute once, `RoundToPaisa`, mirroring total-then-split):
  - `AdValorem` → `RoundToPaisa(TaxableValue × RateBp / 10000)`.
  - `Specific` → `RoundToPaisa(Quantity × PerUnit)`.
  - `RetailSalePriceFactor` → `RoundToPaisa(Quantity × Rsp × RspFactorMillis / 1000)`.
- **Posting:** aggregate cess per rate group like the CGST/SGST/IGST heads, then emit **one** cess entry line
  via a 4th `AddHead(GstTaxHead.Cess, cessAmount, headBp: RateBp/*0 for specific/RSP*/, groupCessBase)` inside
  the per-rate loop (after GstService.cs:327-344). Direction mirrors the party side: Output Cess on a sale,
  Input Cess on a purchase (`taxSide`, line 308). `GstTaxHead.Cess` already exists (GstEnums.cs:70);
  `TaxLedgerName(Cess, dir)` already yields `"Output Cess"`/`"Input Cess"` (GstService.cs:41).
- **Result:** add `Money TotalCess { get; init; }` to `InvoiceTax` (line 234) and include cess in `TotalTax`
  **only for display** — but keep cess **out of** the CGST/SGST/IGST totals (ring-fence, ER-2). Recommend a
  separate `TotalTax` (unchanged: C+S+I) and a distinct `TotalCess`; the round-off grand total (line 351)
  must **add cess** so the assembled voucher balances.

### 2.5 Output/Input Cess ledgers — **lazily**, never unconditionally (critical, ER-13)
`EnableGst` (line 77) currently loops `{Central, State, Integrated}` and creates 6 tax ledgers. **Do NOT add
`Cess` to that unconditional loop** — that would give *every* GST company two extra ledgers and break the
Phase-4 fixtures + off-company byte-identity. Instead:
- Add `private void EnsureCessLedgers(Guid dutiesAndTaxesGroupId)` that `EnsureTaxLedger(Cess, Output)` +
  `EnsureTaxLedger(Cess, Input)` (idempotent, line 92).
- Call it from `EnableGst` **only when `config.CessRates.Count > 0`**.
- Call it from `ComputeInvoiceTax` **only when a cess line is about to post** (look up the `Duties & Taxes`
  group via `_company.FindGroupByName`, as `EnableGst` does at line 73), so an imported cess voucher and any
  ad-hoc cess line always find their ledger. Because `EnsureTaxLedger` is idempotent, repeated calls are safe.

### 2.6 Ring-fence in S1
The electronic credit ledger (`electronic_credit_ledger`, the ring-fenced cess **pool**) does not exist until
**S7**. In S1 the ring-fence is **structural + compute-level**: cess posts to its **own** Output/Input Cess
ledger and **never** to CGST/SGST/IGST (and those never post to Cess). The plan's "cess pool column on the
S7 credit-ledger seam **or a placeholder**" ⇒ **omit for S1** (no credit-ledger table yet); note it as an
S7 carry-forward. The S1 invariant test (§5.3) asserts the compute-level separation.

### 2.7 40%-de-merit ≠ cess (ER-1 note)
Because cess is resolved **only** from `gst_cess_rates` by voucher date, a 40%-slab item after 22-Sep-2025 has
**no effective cess row** ⇒ zero cess automatically. Keep 40% as ordinary CGST+SGST/IGST. A shared "de-merit"
flag must **not** re-attach cess. §5.2 has the guard test.

---

## 3. Schema v38 (ER-13; migration parity)

`src/Apex.Persistence.Sqlite/Schema.cs`:
1. `CurrentVersion = 37` → **`38`** (line 86).
2. **`CreateV1`:** add the two `CREATE TABLE` + indexes (§1.2, §2.1); add the ALTER-equivalent columns
   **inline** to the `stock_items` (after line 676) and `ledgers` (after line 354) `CREATE TABLE` blocks.
3. **New `MigrateV37ToV38` constant** (mirror `MigrateV36ToV37`, line 2566) — pure additive DDL:
   ```sql
   -- new tables (empty when advanced-GST off)
   CREATE TABLE gst_rate_history ( … §1.2 … );
   CREATE INDEX ix_gst_rate_history_company ON gst_rate_history(company_id);
   CREATE TABLE gst_cess_rates ( … §2.1 … );
   CREATE INDEX ix_gst_cess_rates_company ON gst_cess_rates(company_id);
   -- item cess/RSP/valuation (DEFAULT 0/NULL => byte-identical when off)
   ALTER TABLE stock_items ADD COLUMN gst_valuation_basis     INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE stock_items ADD COLUMN cess_applicable         INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE stock_items ADD COLUMN cess_valuation_mode     INTEGER     NULL;
   ALTER TABLE stock_items ADD COLUMN cess_rate_bp            INTEGER     NULL;
   ALTER TABLE stock_items ADD COLUMN cess_per_unit_paisa     INTEGER     NULL;
   ALTER TABLE stock_items ADD COLUMN cess_rsp_factor_millis  INTEGER     NULL;
   ALTER TABLE stock_items ADD COLUMN rsp_paisa               INTEGER     NULL;
   -- ledger sales/purchase-GST mirror (same StockItemGstDetails hydration)
   ALTER TABLE ledgers ADD COLUMN sp_gst_valuation_basis     INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE ledgers ADD COLUMN sp_cess_applicable         INTEGER NOT NULL DEFAULT 0;
   ALTER TABLE ledgers ADD COLUMN sp_cess_valuation_mode     INTEGER     NULL;
   ALTER TABLE ledgers ADD COLUMN sp_cess_rate_bp            INTEGER     NULL;
   ALTER TABLE ledgers ADD COLUMN sp_cess_per_unit_paisa     INTEGER     NULL;
   ALTER TABLE ledgers ADD COLUMN sp_cess_rsp_factor_millis  INTEGER     NULL;
   ALTER TABLE ledgers ADD COLUMN sp_rsp_paisa               INTEGER     NULL;
   ```
   The column definitions in `MigrateV37ToV38` must be **byte-identical** to those in `CreateV1` (the
   equivalence test compares name/type/notnull/default/pk).
4. **`entry_lines` unchanged** — `gst_tax_head` already stores the `GstTaxHead` ordinal incl. `Cess` (=3), and
   `gst_rate_bp`/`gst_taxable_value_paisa` already carry the cess line's rate (0 for specific/RSP) and base
   (GstService/Schema.cs:440-442). No schema change needed for the cess entry line.

`src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs`:
- **`EnsureSchema` stepper:** after the `version == 36` block (lines 886-904), add a `version == 37` block that
  runs `Schema.MigrateV37ToV38`, bumps `schema_version` to 38, sets `version = 38` (copy the shape of lines
  886-904 exactly). The final guard (line 906) then passes.
- **Read:** add `ReadGstRateHistory(Guid)` and `ReadGstCessRates(Guid)` (mirror `ReadGstRateSlabs`, line
  1714) and attach to `GstConfig` where the slabs are attached (line 1069). Extend the item GST hydration
  (`ReadStockItemGst`, line 2575) and the ledger sales/purchase GST hydration (`ReadSalesPurchaseGst`, line
  1585) to populate the new cess/RSP/valuation fields. **These readers use positional ordinals** (e.g.
  `r.IsDBNull(13)`), so **append** the new columns at the **end** of the respective `SELECT` lists
  (stock item SELECT ~line 2352/4426; ledger SELECT ~line 1521/3706) and read them at the new trailing
  ordinals — never insert mid-list.
- **Write:** in the save path, `DELETE FROM gst_rate_history` / `DELETE FROM gst_cess_rates` alongside the
  `gst_rate_slabs` delete (line 3375); INSERT the history + cess rows next to the slab insert (lines
  3580-3596); extend the `stock_items` and `ledgers` INSERT column lists (~4426 / ~3706) to write the new
  columns. DateOnly ⇒ ISO `yyyy-MM-dd` TEXT (as `gst_applicable_from` already is).

**Downgrade helper (test-only, ER-13 legacy DDL):** in
`tests/Apex.Persistence.Sqlite.Tests/ItemInvoiceRoundTripTests.cs`, the `DowngradeToV11` helper drops later
tables (line 218+, e.g. `DROP TABLE IF EXISTS gst_rate_slabs;` at line 390). Add
`DROP TABLE IF EXISTS gst_rate_history;` and `DROP TABLE IF EXISTS gst_cess_rates;` to that helper. (The new
`stock_items`/`ledgers` columns ride on the existing table-rewrite downgrade idiom already used for those
tables — no per-column drop needed; confirm the helper rewrites those tables to their v12/pre-GST shape.)

---

## 4. Io canonical model (RQ-23, ER-13)

`CanonicalModel.cs`:
- New records beside `GstRateSlabDto` (line 958):
  ```csharp
  public sealed record GstRateHistoryDto {
      public required Guid Id { get; init; }
      public string? HsnSac { get; init; }
      public int RateBasisPoints { get; init; }
      public required string RateClass { get; init; }        // GstRateClass name
      public required string EffectiveFrom { get; init; }     // ISO
      public string? EffectiveTo { get; init; }               // ISO or null
      public required string ValuationBasis { get; init; }    // GstValuationBasis name
      public required string Label { get; init; }
      public bool IsPredefined { get; init; }
  }
  public sealed record GstCessRateDto {
      public required Guid Id { get; init; }
      public string? HsnSac { get; init; }
      public required string ValuationMode { get; init; }     // CessValuationMode name
      public int CessRateBasisPoints { get; init; }
      public long CessPerUnitPaisa { get; init; }
      public int CessRspFactorMillis { get; init; }
      public required string EffectiveFrom { get; init; }
      public string? EffectiveTo { get; init; }
      public required string Label { get; init; }
      public bool IsPredefined { get; init; }
  }
  ```
- `GstConfigDto` (line 947): add `IReadOnlyList<GstRateHistoryDto> RateHistory { get; init; } = [];` and
  `IReadOnlyList<GstCessRateDto> CessRates { get; init; } = [];` (default empty — off-company byte-identity).
- `StockItemGstDto` (line 973): add `bool CessApplicable`, `string? CessValuationMode`,
  `int? CessRateBasisPoints`, `long? CessPerUnitPaisa`, `int? CessRspFactorMillis`, `long? RspPaisa`,
  `string ValuationBasis` (`init` default `"TransactionValue"`). Money is **integer paisa** at the Io
  boundary (`MoneyCodec.ToPaisa`, ER-10) — mirror the existing `TaxableValuePaisa` treatment (GstLineTaxDto).

`CanonicalMapper.cs`:
- `MapGstConfig` (line 560): map `RateHistory` (order deterministically:
  `.OrderBy(EffectiveFrom).ThenBy(HsnSac).ThenBy(RateBasisPoints).ThenBy(Id)`) and `CessRates` (same idea).
- `MapStockItemGst` (line 577): map the new cess/RSP/valuation fields (`Money?` → `long?` paisa).

`CanonicalXml.cs`:
- `BuildGstConfig` (line 416): after `<rateSlabs>`, emit `<rateHistory>` (children `<entry .../>`) and
  `<cessRates>` (children `<cess .../>`). `ReadGstConfig` (line 1235): read them back (mirror the `rateSlab`
  read at line 1240). Use `Opt(...)` for nullable attrs (matches existing null-omission style).
- `BuildStockItemGst`/`ReadStockItemGst` (used at lines 386/1199/577/1320-ish): add the cess/RSP/valuation
  attributes.

`CanonicalJson.cs`: **no change**. `ExportOptions.DefaultIgnoreCondition = Never` means every member is
emitted, so the new `RateHistory = []`, `CessRates = []`, `CessApplicable = false`,
`ValuationBasis = "TransactionValue"`, cess scalars `null` all serialise **identically** for an off company
and for a plain v37 Phase-8 company under the v38 build ⇒ **ER-13 byte-identical when off holds within the
build** (the only way it could break is reordering/altering an *existing* field — do not).

`ImportPlan.cs`:
- `BuildGstConfig` (line 1033): after `AddRateSlab` (line 1045-1046), add
  `foreach (var h in g.RateHistory) config.AddRateHistory(new GstRateHistoryEntry(Guid.NewGuid(), …));`
  and `foreach (var c in g.CessRates) config.AddCessRate(new GstCessRate(Guid.NewGuid(), …));`
  (fresh ids, `ParseEnum<GstRateClass>`/`ParseEnum<GstValuationBasis>`/`ParseEnum<CessValuationMode>`,
  `DateOnly.Parse` for the ISO strings, `MoneyCodec.FromPaisa` for money).
- `BuildStockItemGst` (line 1320): set the cess/RSP/valuation fields.
- **`CompanyImportService` itself is unchanged** — it already posts every voucher through
  `LedgerService.Post`/`VoucherValidator` all-or-nothing (its type doc, lines 48-67). Because the cess ledgers
  are created **lazily** by `EnableGst`/`ComputeInvoiceTax` (§2.5), an imported cess-bearing voucher's cess
  lines find their ring-fenced ledger — the import cannot bypass the seam. A malformed cess row makes
  `BuildGstConfig`/`EnsureValid` throw in pre-flight ⇒ `Applied = false`, target untouched (RQ-23).

---

## 5. TDD test list (write failing-first)

**Engine (`tests/Apex.Ledger.Tests`):**
1. **Rate-resolution-by-date** — seed history for HSN `8703`; `ResolveRate(car, ledger, 2025-09-20)` → `2800`
   bp; `ResolveRate(car, ledger, 2025-09-25)` → `4000` bp. A legacy-only HSN pre-22-Sep resolves its legacy
   rate; an item with **no** history row resolves via its scalar `RateBasisPoints` **unchanged** (proves the
   override only fires on a match). `RateResolution.ValuationBasis` = `RetailSalePrice` for a tobacco HSN.
2. **Cess valuation modes** — ad-valorem (`V=100000 @ 12%` → `12000`); specific (`coal qty=10t @ ₹400/t` →
   `4000`); RSP-factor (`pan masala qty=5, RSP=100, factor 0.32` → `5 × 100 × 0.32 = 160`), each `RoundToPaisa`
   once at the group level. **Date pair:** car cess on `2025-09-20` > 0; on `2025-09-25` == 0. **De-merit
   guard:** a 40%-slab item on `2025-09-25` has **zero** cess.
3. **Cess ring-fence invariant** — a cess-bearing invoice posts a `Cess` line to `Output Cess`/`Input Cess`
   only; assert **no** cess amount lands on any CGST/SGST/IGST ledger and `TotalCess` is separate from
   `TotalTax` (C+S+I).
4. **Cess-bearing voucher balances to the paisa** — assemble a full sales voucher (party Dr = taxable + CGST +
   SGST + Cess; stock/sales Cr; tax Cr) and post through `LedgerService.Post`/`VoucherValidator`: Σ Dr = Σ Cr,
   and Σ posted cess lines == computed `TotalCess`.
5. **Phase-4 base-GST fixtures byte-identical when advanced-GST off** — re-run the existing GSTR-1/GSTR-3B
   `TabularExport` golden tests on a plain GST company (no cess, no history) and assert **unchanged** output;
   add an assertion that such a company creates **no** Cess ledger (only the 6 CGST/SGST/IGST + Round Off).

**Persistence (`tests/Apex.Persistence.Sqlite.Tests`):**
6. **Migration parity** — `SchemaMigrationEquivalenceTests` (unchanged code) must pass at v38 (CreateV1 ==
   v1→v38 chain). New **`GstRateHistoryCessSchemaTests`** (mirror `GratuityBonusSchemaTests`): (a) fresh DB
   stamps to `CurrentVersion` and has `gst_rate_history`, `gst_cess_rates`, and every new item/ledger column;
   (b) a legacy **v37** DB (minimal DDL, one `companies` row) auto-migrates to v38 preserving the row, new
   columns default 0/NULL, new tables empty (ER-13), and re-opening is idempotent; (c) save/load round-trips a
   cess-bearing company — rate history, cess rates, item `RetailSalePrice`/cess fields paisa-exact.

**Io (`tests/Apex.Ledger.Io.Tests`):**
7. **JSON + XML lossless round-trip incl. new data** — a company with dated rate history + 3-window cess
   rates + an item with RSP/cess round-trips through `CanonicalJson` and `CanonicalXml`:
   export→parse→export **byte-stable**, and the reconstructed `Company` equals the original to the paisa.
8. **ER-13 byte-identical-when-off** — an advanced-GST-**off** company (Robert, Bright, and a plain Phase-4
   GST company) exports JSON+XML with `rateHistory`/`cessRates` empty, cess scalars null,
   `valuationBasis = "TransactionValue"`; assert the plain GST company's bytes match a captured pre-S1 golden
   (i.e. new members are inert) and that an off company and a Phase-8 company serialise identically.
9. **CompanyImportService all-or-nothing** — importing a cess-bearing canonical model rebuilds history + cess
   + item cess fields and posts the cess voucher through the engine (cess ledgers auto-created, ring-fence
   honoured); a malformed cess row (e.g. RSP-factor mode with no RSP) rejects the whole batch
   (`Applied = false`, target byte-unchanged).

**Regression:** 10. **Robert + Bright green** (accounts-only / trading, no GST → untouched). De-brand scan
clean (ER-11: no "Tally" in any new code/JSON/label).

---

## 6. Risks / subtleties for the implementer

1. **Do NOT create Cess ledgers unconditionally.** Adding `Cess` to the `EnableGst` head loop (GstService.cs:
   77-79) gives every GST company two extra ledgers → breaks Phase-4 GSTR fixtures **and** off-company
   byte-identity. Create Output/Input Cess **lazily** — only when `CessRates` is non-empty (in `EnableGst`) or
   a cess line is about to post (in `ComputeInvoiceTax`). `EnsureTaxLedger` is already idempotent. *(Top risk.)*
2. **Paisa conservation on cess.** Compute each cess amount **once** per rate group with `RoundToPaisa`
   (never per sub-unit for specific/RSP — `qty × per-unit` rounded once, else ±0.01 drift on odd tails, the
   recurring A10 finding). The assembled voucher's **party leg must include cess** or the voucher won't
   balance; the invoice round-off grand total (GstService.cs:351) must add cess too.
3. **Date-boundary inclusivity.** `IsEffectiveOn` uses both bounds **inclusive**. The regime flips on
   **22-Sep-2025**: legacy/cess rows end `effective_to = 2025-09-21`; new rows start
   `effective_from = 2025-09-22`. A voucher on exactly the boundary must resolve deterministically — the
   20-Sep-vs-25-Sep golden pair is one off-by-one away from failing.
4. **Don't break Phase-4/8 resolution.** The date override fires **only** when a voucher date **and** a
   matching HSN-dated history row both exist; otherwise `ResolveRate` returns the current
   item→ledger→unresolved result unchanged. Every existing fixture passes a date but has no history rows.
5. **Keep legacy rates resolvable.** Legacy `12%`/`28%` are **never deleted** — they live as dated
   `Legacy`-class rows with `effective_to = 2025-09-21`, so a pre-22-Sep voucher reprints at the historic
   rate. Deleting/expiring them wrong corrupts historical reprints.
6. **Io byte-identity under `IgnoreCondition.Never`.** Because JSON emits every member, the *only* safe
   additions are **new** optional members defaulting `[]`/null/`"TransactionValue"`. Never reorder or change
   an existing DTO field, table column order, or the mapper's list ordering — an off company must serialise
   identically to a Phase-8 company under the same build.
7. **Cess entry line reuses `entry_lines.gst_tax_head` (ordinal 3 = Cess).** No `entry_lines` schema change; a
   specific/RSP cess line legitimately carries `gst_rate_bp = 0` (`GstLineTax` allows `bp >= 0`,
   GstLineTax.cs:26) — reports read the **amount** off the line, never recompute (ER-9). Confirm the Phase-4
   GSTR-1/3B projections tolerate (or simply never see, when off) a `Cess`-head line.
8. **Positional store readers.** `SqliteCompanyStore` reads item/ledger GST by column **ordinal** — append new
   columns to the **end** of each `SELECT` and read at trailing ordinals; inserting mid-list silently
   corrupts every subsequent field.

---

## 7. Definition of Done (PR-1 gate, requirements §5)

Rate resolves by voucher date (pre/post-22-Sep pair) · three cess windows compute (20-Sep vs 25-Sep car =
cess vs no-cess golden pair) · cess ring-fenced to its own ledger · total-then-split conserves to the paisa ·
legacy-DDL v37→v38 upgrade preserves rows · **ER-13 byte-identical when advanced-GST off** · Io JSON+XML +
`CompanyImportService` round-trip lossless & all-or-nothing · **Robert + Bright green** · migration parity
(`CreateV1 == MigrateV37ToV38` chain) · de-brand scan clean. Then A10 adversarial pass → A12 commits.
