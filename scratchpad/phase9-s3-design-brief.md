# Phase 9 · Slice S3 — Composition scheme + Bill of Supply + CMP-08 / GSTR-4 (schema v39→v40)

**Design-only brief for the implementing engine agent.** Grounded in the actual code on
`claude/apex-phase-9-start-51d103` (worktree `wonderful-hellman-59520a`). Governs RQ-4, RQ-10, RQ-16; ER-4,
ER-8, ER-9, ER-10, ER-11, ER-13; DP-9. PR-3 gate per requirements §5 "P9-S3". **Do NOT run git. This is a
build spec, not an implementation.**

> **Design north star:** the composition path is a **branch that fires only when the company's GST
> `RegistrationType == Composition`**. A **Regular** (or Unregistered/Consumer) company — every existing
> Phase-4/8 and P9-S1/S2 fixture — must resolve, post, persist and serialise **byte-identically to a v39
> company** (ER-13). The way we guarantee that: (a) never alter an existing column/table/DTO field/ordering;
> (b) the two new `companies` columns default NULL, the two new `GstConfigDto` members default null; (c) every
> new engine branch is a pure `if (company is composition)` guard that a Regular company never enters. S3 adds
> **no new masters or vouchers of its own** — CMP-08 / GSTR-4 are **pure projections** (like `Gstr1.Build`)
> computed on demand, so S3's persistence footprint is just **two config columns**.

---

## 0. Files you will touch (all paths relative to the worktree root)

| Layer | File | What |
|---|---|---|
| Domain enums | `src/Apex.Ledger/Domain/GstEnums.cs` | +`CompositionSubType` (Manufacturer / Trader / Restaurant / ServiceProvider) |
| Domain config | `src/Apex.Ledger/Domain/GstConfig.cs` | +`CompositionSubType?`, +`CompositionOptInDate`; extend `EnsureValid` |
| Domain helper | `src/Apex.Ledger/Domain/CompositionThreshold.cs` (**new**) | static per-state / per-sub-type threshold + rate resolver (mirror `IndianState` static reference) |
| Engine — enable/gate | `src/Apex.Ledger/Services/GstService.cs` | `EnableGst` branch (gate the 6 GST ledgers off for composition); `ComputeInvoiceTax` composition short-circuit (no output tax, no ITC) |
| Engine — turnover tax | `src/Apex.Ledger/Services/CompositionTaxService.cs` (**new**) | tax-on-turnover per sub-type + base, split C+S paisa-exact; CMP-08 / GSTR-4 / GSTR-9A projections |
| Engine — RCM no-ITC | `src/Apex.Ledger/Services/RcmService.cs` | composition guard: inward RCM liability still posts (cash-only, → CMP-08) but the tax is **costed, not availed as ITC** (ER-4 "no ITC") |
| Reports — projections | `src/Apex.Ledger/Reports/Cmp08.cs` (**new**), `src/Apex.Ledger/Reports/Gstr4.cs` (**new**) | CMP-08 quarterly + GSTR-4 / GSTR-9A annual records (mirror `Gstr1`/`Gstr3b`) |
| Reports — support | `src/Apex.Ledger/Reports/GstReportSupport.cs` | +`OutwardSupplyValue` / turnover-by-taxability helper (reads stock/sales value, **not** tax lines) |
| Reports — suppress | `src/Apex.Ledger/Reports/Gstr1.cs`, `src/Apex.Ledger/Reports/Gstr3b.cs` | early-return an empty return for a composition company (they file CMP-08/GSTR-4, not 1/3B) |
| Print | `src/Apex.Ledger.Io/InvoicePrintData.cs` (+ `InvoicePdf.cs`) | derived `IsBillOfSupply` header + the §10 non-collection declaration line |
| Io return writer | `src/Apex.Ledger.Io/GstReturnJson.cs` (**new**) | deterministic CMP-08 / GSTR-4 offline JSON (mirror `FvuWriter`/`CanonicalJson` determinism) |
| Schema | `src/Apex.Persistence.Sqlite/Schema.cs` | `CurrentVersion`→40; ALTER `companies` in `CreateV1` **and** new `MigrateV39ToV40`; **fix the stale `CurrentVersion = 37` doc comment at :73** |
| Store | `src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs` | `EnsureSchema` step `version==39`; read/write the two new `companies` columns |
| Io model | `src/Apex.Ledger.Io/CanonicalModel.cs` | extend `GstConfigDto` (+2 members) |
| Io mapper | `src/Apex.Ledger.Io/CanonicalMapper.cs` | extend `MapGstConfig` |
| Io XML | `src/Apex.Ledger.Io/CanonicalXml.cs` | extend `BuildGstConfig` / `ReadGstConfig` |
| Io import | `src/Apex.Ledger.Io/ImportPlan.cs` | extend `BuildGstConfig`; `CompanyImportService.EnsureValid` mirror rides on `GstConfig.EnsureValid` |
| Tests | `tests/Apex.Ledger.Tests/*`, `tests/Apex.Persistence.Sqlite.Tests/CompositionSchemaTests.cs` (**new**) + `DowngradeToV11` helper, `tests/Apex.Ledger.Io.Tests/*` | see §7 |

`CanonicalJson.cs` needs **no change** — it serialises the DTO records via `System.Text.Json` with
`DefaultIgnoreCondition.Never`; the two new members flow automatically and, being null when the company is not
composition, keep byte-identity within the build (see §6).

---

## 1. Composition configuration (RQ-4, DP-9, ER-13)

### 1.1 What already exists (do NOT rebuild)
- `GstRegistrationType.Composition` (GstEnums.cs:14) is a defined enum value, already persisted on the company
  as `companies.gst_reg_type` (Schema.cs:129) and round-tripped through the store
  (SqliteCompanyStore.cs:1032 read / :3742 write) and Io (`GstConfigDto.RegistrationType`,
  CanonicalModel.cs:1007). **The reg-type plumbing is done** — S3 only adds the composition **sub-type**, the
  **opt-in date**, and the **behaviour** that reg-type=Composition drives.
- `PartyGstDetails.RegistrationType` already carries `Composition` for a party too (PartyGstDetails.cs:16) —
  no change needed there; a composition **supplier** to your Regular company is just a registered party.

### 1.2 New enum `CompositionSubType` (GstEnums.cs)
Add beside `GstRegistrationType`, storing the enum ordinal (Manufacturer=0, Trader=1, Restaurant=2,
ServiceProvider=3). The sub-type drives **both** the rate **and** the turnover base (§3).

```csharp
/// <summary>
/// The composition-dealer sub-type (Phase 9 slice 3; RQ-4; §10 + Rule 7). Drives the tax-on-turnover rate AND the
/// turnover base: Manufacturer/Restaurant tax the TOTAL turnover in state (incl. exempt); Trader/ServiceProvider tax
/// only TAXABLE supplies. Meaningful only when the company's <see cref="GstRegistrationType"/> is Composition.
/// </summary>
public enum CompositionSubType
{
    /// <summary>§10(1) manufacturer of non-excluded goods — 1% (0.5% C + 0.5% S) on TOTAL turnover in State/UT.</summary>
    Manufacturer,
    /// <summary>§10(1) trader / other supplier of goods — 1% (0.5% + 0.5%) on TAXABLE supplies only.</summary>
    Trader,
    /// <summary>Sch-II 6(b) restaurant (non-alcohol) — 5% (2.5% + 2.5%) on TOTAL turnover in State/UT.</summary>
    Restaurant,
    /// <summary>§10(2A) service provider / mixed supplier — 6% (3% + 3%) on TAXABLE supplies; ₹50 L threshold.</summary>
    ServiceProvider,
}
```

### 1.3 `GstConfig` additions
Add two members + adders (mirroring the `RegistrationType`/`ApplicableFrom` scalars, GstConfig.cs:33/36):

```csharp
/// <summary>The composition sub-type (Phase 9 slice 3; RQ-4). Non-null only when <see cref="RegistrationType"/> is
/// Composition; drives the tax-on-turnover rate + base. Null for a Regular company (byte-identical, ER-13).</summary>
public CompositionSubType? CompositionSubType { get; set; }

/// <summary>The date the dealer opted into composition (CMP-02); advisory (period-scoping), null when unset.</summary>
public DateOnly? CompositionOptInDate { get; set; }
```

`EnsureValid()` (GstConfig.cs:86) — extend, **only** on the composition branch (a Regular config is
byte-identical): a Composition dealer **is a registered person**, so it still requires a valid GSTIN (reuse the
existing Regular check by widening it), **and** a `CompositionSubType` must be set:

```csharp
// existing: if (RegistrationType == Regular && Gstin is null) throw …;
if (RegistrationType is GstRegistrationType.Regular or GstRegistrationType.Composition && Gstin is null)
    throw new ArgumentException($"A {RegistrationType} GST registration requires a GSTIN.");
if (RegistrationType == GstRegistrationType.Composition && CompositionSubType is null)
    throw new ArgumentException("A Composition registration requires a composition sub-type.");
```

### 1.4 Threshold + rate resolver `CompositionThreshold` (new static, mirror `IndianState`)
DP-9 calls for "the 8 special-category states at ₹75 L as a per-state master." These are **fixed law** (not
user data), so model them as **static reference data** (like `IndianState.All`) — **no DB table** (keeps ER-13
trivially clean: nothing to serialise, nothing to migrate). Keep it a pure function of (home-state code,
sub-type). The threshold is **advisory** (an eligibility warning, like Rule 138E), never a hard posting gate.

```csharp
public static class CompositionThreshold
{
    // 8 special-category states at ₹75 L (verifier-confirmed): Uttarakhand 05, Sikkim 11, Arunachal 12,
    // Nagaland 13, Manipur 14, Mizoram 15, Tripura 16, Meghalaya 17. (Assam 18, HP 02, J&K 01 are at ₹1.5 cr.)
    private static readonly HashSet<string> SpecialCategory = new(StringComparer.Ordinal)
        { "05", "11", "12", "13", "14", "15", "16", "17" };

    /// <summary>The preceding-FY aggregate-turnover eligibility threshold (rupees) for a sub-type + home state.</summary>
    public static Money Threshold(CompositionSubType subType, string? homeStateCode) => subType switch
    {
        CompositionSubType.ServiceProvider => new Money(5_000_000m),                    // §10(2A) ₹50 L (all states)
        _ when homeStateCode is { } s && SpecialCategory.Contains(s) => new Money(7_500_000m), // ₹75 L special
        _ => new Money(15_000_000m),                                                    // ₹1.5 cr general
    };

    /// <summary>The composition rate (integrated basis points) for a sub-type: Mfr/Trader 100 (1%), Restaurant 500
    /// (5%), ServiceProvider 600 (6%). Split half/half into CGST+SGST by the compute-total-then-split rule (§3).</summary>
    public static int RateBasisPoints(CompositionSubType subType) => subType switch
    {
        CompositionSubType.Manufacturer or CompositionSubType.Trader => 100,
        CompositionSubType.Restaurant => 500,
        CompositionSubType.ServiceProvider => 600,
        _ => throw new ArgumentOutOfRangeException(nameof(subType)),
    };

    /// <summary>True iff the sub-type taxes TOTAL turnover in state (Manufacturer/Restaurant); false ⇒ TAXABLE only
    /// (Trader/ServiceProvider). This is the §2.4 "base by dealer type" rule.</summary>
    public static bool TaxesTotalTurnover(CompositionSubType subType) =>
        subType is CompositionSubType.Manufacturer or CompositionSubType.Restaurant;
}
```

> **A14 to re-verify (R7):** the 8-state list, the ₹1.5 cr / ₹75 L / ₹50 L figures, and the 1/5/6 % rates
> against CBIC §10/Rule 7 at build. All are seeded/derived constants here — a council change is a one-line
> edit, never a schema change.

> **DEVIATION to record (R6):** §3.4 / §5 of the requirements name a `composition_config` **table** and "a
> bill-of-supply document flag." This brief consolidates the composition config into **two `companies`
> columns** (1:1 with the company, mirroring the existing `gst_reg_type`/`tds_*`/`pf_*` company-level config)
> — **no separate table** — and models the **Bill of Supply as a derived property** (§2.3), **no stored
> flag**. Both are byte-identity- and drift-safer than the named alternatives and stay faithful to the
> established codebase pattern; record the rationale in `memory.md`.

---

## 2. Bill of Supply + no-tax posting (RQ-10, ER-4, ER-8)

A composition dealer **cannot collect GST** (issues a Bill of Supply, not a tax invoice) and **cannot claim
ITC**. The engine enforces both by branching on the company reg-type — a Regular company never enters any
branch, so it is byte-identical.

### 2.1 `ComputeInvoiceTax` composition short-circuit (the load-bearing guard)
`ComputeInvoiceTax` (GstService.cs:536) is the single forward-GST computation for **both** outward (Output)
and inward (Input) supplies. For a composition company **both** must yield **no tax**: no output collected on a
sale, no ITC on a purchase. Add a guard at the very top (before any accumulation):

```csharp
public InvoiceTax ComputeInvoiceTax(
    IReadOnlyList<TaxableLine> lines, bool interState, GstTaxDirection direction, bool applyInvoiceRoundOff = false)
{
    ArgumentNullException.ThrowIfNull(lines);

    // Phase 9 slice 3 (RQ-10; ER-4): a Composition dealer issues a Bill of Supply — it neither collects output GST
    // (outward) nor avails ITC (inward). Suppress ALL forward CGST/SGST/IGST/Cess: no tax lines, no round-off, zero
    // totals. The supply value flows to the party leg untaxed (the caller assembles party Dr = supply value). A
    // Regular/Unregistered company never enters this branch ⇒ byte-identical (ER-13). Inward RCM is NOT computed here
    // (it flows through RcmService, §2.4) so the composition dealer's cash-only RCM liability is unaffected.
    if (_company.Gst?.RegistrationType == GstRegistrationType.Composition)
        return new InvoiceTax { TaxLines = [], LineBreakdown = [] };   // all Money totals default to zero

    …existing body unchanged…
}
```

- `InvoiceTax`'s `TotalCgst/Sgst/Igst/Cess`, `RoundOffAmount` default to `Money.Zero`; `TotalTax` computes to
  zero. The caller (`VoucherEntryViewModel`) already handles a zero-tax invoice (an exempt sale today), so no
  UI change is forced — the assembled voucher is party Dr = supply value, sales Cr = supply value, balanced,
  no tax leg.
- **Round-off:** deliberately suppressed for a Bill of Supply (a composition sale is the plain supply value).
  If a later UI wants rupee-rounding on a BoS, it is a pure display concern, not a tax posting.

### 2.2 `EnableGst` — gate the 6 GST tax ledgers off for composition (RQ-4)
`EnableGst` (GstService.cs:72) unconditionally creates the six Output/Input CGST/SGST/IGST ledgers (:89-91). A
composition dealer never posts to them (no collection, no ITC), so creating them pollutes the ledger set and
contradicts RQ-4 "gate the 6 GST tax ledgers off for a composition dealer." Branch:

```csharp
// Regular/Unregistered: the 6 Output/Input tax ledgers as today (byte-identical).
// Composition (Phase 9 slice 3; RQ-4): create NONE — a composition dealer collects no output tax and claims no ITC,
// so it needs no Output/Input GST ledgers. (Inward-RCM ledgers are still created LAZILY by RcmService when an RCM
// supply posts, §2.4; the Round-Off ledger below is harmless and kept.)
if (config.RegistrationType != GstRegistrationType.Composition)
    foreach (var direction in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
        foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
            EnsureTaxLedger(dutiesAndTaxes.Id, head, direction);

EnsureRoundOffLedger();  // unchanged
```

- Keep `SeedAdvancedGst` (GstService.cs:193) callable for a composition company (it seeds rate-history / cess /
  RCM categories — the RCM categories are needed for the composition dealer's inward RCM, §2.4). Cess ledgers
  stay lazy.
- **CMP-02 switch-over** (a Regular company with 6 existing ledgers that later opts into composition, +
  ITC-03 stock-ITC reversal) is **explicitly out of S3 scope** (task note) → carry-forward. S3 handles a
  company that is Composition at `EnableGst` time.

### 2.3 Bill of Supply header + declaration (print layer)
The Bill of Supply is a **derived** property, not stored state (mirrors `IsOutwardReverseChargeSupply`):

- Add `GstReportSupport.IsBillOfSupply(Company company, Voucher voucher)` ⇒ true iff
  `company.Gst?.RegistrationType == Composition` **and** the voucher's base type is `Sales` (an outward supply).
- `InvoicePrintData` (InvoicePrintData.cs) gains a derived `bool IsBillOfSupply` + a `CompositionDeclaration`
  string: **"Composition taxable person, not eligible to collect tax on supplies"** (§10 / Rule 5(f)). When
  true, `InvoicePdf` prints the document title as **"Bill of Supply"** (not "Tax Invoice") and suppresses the
  CGST/SGST/IGST tax columns (there are none). De-brand rule (ER-11): the declaration text must never say
  "Tally".
- **UI depth (screen wiring, print polish) MAY be a follow-up pass** like S1's deferred UI — the derived
  property + declaration string are the S3 deliverable; the printed-column layout tuning can follow. Record
  the split in `memory.md`.

### 2.4 Inward RCM stays at normal rate but **blocks ITC** (ER-4, the S2↔S3 interaction — a real subtlety)
ER-4 / §2.4: "**RCM inward still at normal rates**" **and** "**no ITC**." S2's `RcmService` posts a **dual
leg** — an Output RCM liability (cash-only) **and** an Input RCM ITC. For a **composition** dealer the ITC leg
is **wrong**: composition blocks all ITC, so the RCM tax is a **cost**, not a creditable input. Required S3
change in `RcmService`:

- When `_company.Gst?.RegistrationType == Composition`, still post the **Output RCM liability** leg (the
  composition dealer genuinely owes and pays this RCM tax — it lands in **CMP-08**, §4.1), but route the
  balancing debit to the **expense/purchase cost** (the RCM tax becomes part of the inward cost) **instead of**
  an Input RCM ITC line. Concretely: skip the `Input {head}` ITC `EntryLine`; add the same amount to the
  source expense/purchase ledger leg (or a dedicated **"RCM Tax (Non-creditable)"** expense ledger, lazily
  created like `EnsureRoundOffLedger`). The `GstLineTax` on the surviving liability leg keeps its RCM tag +
  scheme so CMP-08 reads it; **no ITC-tagged line exists**, so nothing feeds an ITC bucket.
- A Regular company's `RcmService` path is unchanged (byte-identical). Guard purely on reg-type.
- **If `RcmService` proves too entangled to branch cleanly in S3**, the minimum acceptable fallback is: the
  composition dealer's RCM ITC line must **not** surface as availed credit anywhere a composition return reads
  (CMP-08 must show the RCM tax as **payable**, never as offsetting credit). Prefer the clean cost-routing
  above; document whichever is chosen.

---

## 3. Tax-on-turnover engine `CompositionTaxService` (RQ-10, RQ-16, ER-9, ER-10)

A new pure service (framework/DB/clock-free), modelled on `Gstr1.Build` (a projection over posted vouchers) +
`GstService.ComputeLineTax` (paisa-exact split). **Reads posted supply value; never recomputes forward tax**
(there is none — composition posts no tax lines, so turnover is read from the **stock/sales value**, ER-9).

### 3.1 Turnover base per sub-type
Add `GstReportSupport.OutwardSupplyValue(Company, Voucher)` + a taxability classifier — the turnover source
that does **not** depend on tax lines (a composition voucher has none):

- **Supply value of an outward voucher** = its inventory value when it has stock lines
  (`voucher.InventoryLinesValue`, already used by `Gstr3b.ExemptOutwardValue`), else the sum of its
  sales/income-ledger legs (as-voucher accounting sale). Reads posted amounts only.
- **Taxable vs exempt split:** a stock line is a **taxable supply** iff its item's/sales-ledger's
  `StockItemGstDetails.IsTaxable` (Taxable taxability) — i.e. it *would* be taxed for a Regular dealer; an
  Exempt/Nil/Non-GST line is **exempt turnover**. (This reuses the same taxability the rate resolver reads,
  GstEnums `GstTaxability`.)

`CompositionTaxService` then computes, over `[from, to]` (a quarter for CMP-08):

```
totalTurnover   = Σ OutwardSupplyValue over posted Sales vouchers in the window (taxable + exempt + nil + non-GST)
taxableTurnover = Σ the taxable-classified portion only
base = CompositionThreshold.TaxesTotalTurnover(subType) ? totalTurnover : taxableTurnover
```

- **Cancelled / post-dated-after-`to`** vouchers excluded via `LedgerBalances.CountsAsOf` (the same filter the
  reports use). Only `VoucherBaseType.Sales` count as outward composition turnover (a §34 credit note against a
  composition sale, if ever modelled, would net down — out of S3 scope, note it).

### 3.2 The flat composition tax (compute-total-then-split, paisa-exact — ER-10)
```csharp
var rateBp = CompositionThreshold.RateBasisPoints(subType);          // 100 / 500 / 600
var total  = GstService.TaxAmount(base, rateBp);                     // round_paisa(base × rate) — the whole comp. tax
var cgst   = new Money(total.Amount / 2m).RoundToPaisa();            // half, rounded
var sgst   = new Money(total.Amount - cgst.Amount);                  // remainder ⇒ CGST + SGST == total (parity, L-4)
```

Reuse `GstService.TaxAmount` (GstService.cs:423) and the exact split idiom from `ComputeLineTax`
(GstService.cs:456-457) so `CGST + SGST == total` to the paisa (on an odd total SGST carries the +1 paisa).
**Never** compute each half at `rateBp/2` independently (the ±0.01 drift this project forbids). Composition
tax is **CGST + SGST only** — a composition dealer's outward supply is always **intra-state** (they cannot make
inter-state outward supplies under §10; if the fixtures ever need it, it is still C+S on the home-state base).

### 3.3 Service shape
```csharp
public sealed class CompositionTaxService(Company company)
{
    public CompositionTax ComputeForPeriod(DateOnly from, DateOnly to);   // one quarter (CMP-08) or a full FY (GSTR-4)
}
public sealed record CompositionTax(
    DateOnly From, DateOnly To, CompositionSubType SubType,
    Money TotalTurnover, Money TaxableTurnover, Money TurnoverBase,
    int RateBasisPoints, Money Cgst, Money Sgst,
    Money RcmInwardCgst, Money RcmInwardSgst, Money RcmInwardIgst, Money RcmInwardCess)   // inward RCM the dealer also pays
{
    public Money CompositionTax => new(Cgst.Amount + Sgst.Amount);
    public Money TotalPayable => new(CompositionTax.Amount + RcmInwardCgst.Amount + RcmInwardSgst.Amount
                                     + RcmInwardIgst.Amount);   // cess ring-fenced separately (ER-2)
}
```

- The **RCM inward** figures are read from `GstReportSupport.RcmLines` (the S2 projection, filtering
  `IsOutputLiability`) — a composition dealer pays inward-RCM tax in **cash** exactly like a Regular dealer,
  and CMP-08 Table 3 requires it (§4.1). Keep cess ring-fenced (ER-2): a separate field, never folded into
  the GST payable total.

---

## 4. Returns — CMP-08, GSTR-4, GSTR-9A + offline JSON; suppress GSTR-1/3B (RQ-16, ER-9)

### 4.1 CMP-08 (quarterly self-assessed statement) — `Cmp08` record (mirror `Gstr3b`)
A pure projection over `CompositionTaxService.ComputeForPeriod(quarterFrom, quarterTo)`, laid out to the
CMP-08 form:

- **Table 3 (i)** Outward supplies (incl. exempt) + inward RCM value → the tax on turnover (§3.2).
- **Table 3 (ii)** Inward supplies attracting reverse charge → the RCM inward tax (§3.3, cash).
- **Table 3 (iii)** Tax payable = (i)+(ii), by head (CGST/SGST/IGST, + ring-fenced Cess).
- **Table 3 (iv)** Interest — **flag/zero** in S3 (§50 late-payment interest is DP-34 flag-only, out of scope
  — model the field, leave it `Money.Zero`, note the carry-forward).

`Cmp08.Build(Company, quarter)` returns the record; a non-composition company yields a **not-applicable /
empty** CMP-08 (guard: `company.Gst?.RegistrationType != Composition ⇒ empty`).

### 4.2 GSTR-4 (annual) — `Gstr4` record
- **Table 5** — summary of the four quarters' CMP-08 (outward tax-on-turnover + inward-RCM, rate-wise).
- **Table 4A/4B/4C/4D** — inward supplies (from registered / unregistered / RCM / import) — a **light**
  projection over the composition dealer's posted Purchase vouchers in the FY (value + tax paid to supplier,
  **not** ITC — composition claims none). 4C/RCM ties to §3.3.
- **Table 6** — tax rate-wise liability (the composition rate + the RCM rates).
- `Gstr4.Build(Company, fyFrom, fyTo)`; empty/not-applicable for a non-composition company.

### 4.3 GSTR-9A (annual composition return) — **light** projection
A thin roll-up over the FY's CMP-08 + GSTR-4 (turnover, tax paid, late fee) per DP-18's "light projections"
posture. One record `Gstr9a.Build(Company, fyFrom, fyTo)`; mark clearly as light. **CMP-02 opt-in / CMP-04
withdrawal + ITC-03 / ITC-01 transitions are noted as carry-forward** (task scope) — not built in S3.

### 4.4 Offline JSON — `GstReturnJson` (new deterministic writer)
There is **no existing GSTR-1/3B offline-JSON writer** in `Apex.Ledger.Io` (the base returns are on-screen
projections only). So CMP-08 / GSTR-4 offline JSON is a **new** writer — follow the determinism of `FvuWriter`
(deterministic string building) and `CanonicalJson` (`System.Text.Json`, culture-invariant, stable member
order). Recommended: shape a small DTO per return and serialise with the same `JsonSerializerOptions` posture
as `CanonicalJson` (invariant, `WriteIndented` for readability, fixed property order). Money → **integer
paisa** at the boundary (`MoneyCodec.ToPaisa`, ER-10). Emit the government offline-tool envelope
(`gstin`, `fp`/`ret_period`, the summary sections).

> **A14 to verify (R7):** the exact CMP-08 and GSTR-4 offline-tool JSON schema (field names / nesting /
> `ret_period` format) against the current GSTN offline utility before finalising the writer. The projection
> records are correct regardless; only the JSON envelope is schema-sensitive.

### 4.5 Suppress / redirect GSTR-1 / GSTR-3B for a composition company
A composition dealer files **CMP-08 / GSTR-4**, **not** GSTR-1 / GSTR-3B. Guard both `Build`s:

- `Gstr1.Build` (Gstr1.cs:150) and `Gstr3b.Build` (Gstr3b.cs:87): at the top, if
  `company.Gst?.RegistrationType == Composition`, **early-return an empty return** (empty sections / all-zero
  totals over `[from, to]`). This keeps a composition company from emitting an outward/summary return and
  routes the Desktop to the CMP-08/GSTR-4 screens instead. A Regular company never enters the guard ⇒
  byte-identical (ER-13).
- **Where the inward RCM goes:** for a composition company the RCM inward is **not** dropped — it is reported
  in **CMP-08 Table 3(ii)** (§4.1), not GSTR-3B 3.1(d). The Gstr3b guard therefore does not lose data.

---

## 5. Schema v40 (ER-13; migration parity)

`src/Apex.Persistence.Sqlite/Schema.cs`:
1. `CurrentVersion = 39` → **`40`** (Schema.cs:86). **Also fix the stale doc comment at Schema.cs:73**
   (`<see cref="CurrentVersion"/> = 37`) → **40** (opportunistic hygiene, per the task).
2. **`CreateV1`:** add the two columns **inline** to the `companies` `CREATE TABLE` block (after
   `gst_periodicity`, Schema.cs:131, keeping the GST config columns together):
   ```sql
   composition_sub_type     INTEGER     NULL,   -- CompositionSubType enum ordinal; NULL unless reg_type=Composition
   composition_opt_in_date  TEXT        NULL,   -- ISO yyyy-MM-dd (CMP-02 opt-in), or NULL
   ```
3. **New `MigrateV39ToV40` constant** (mirror `MigrateV38ToV39`, Schema.cs:2787) — pure additive DDL, the two
   ALTERs **only** (no new table):
   ```sql
   ALTER TABLE companies ADD COLUMN composition_sub_type    INTEGER NULL;
   ALTER TABLE companies ADD COLUMN composition_opt_in_date TEXT    NULL;
   ```
   The column definitions in `MigrateV39ToV40` must be **byte-identical** to those in `CreateV1`
   (`SchemaMigrationEquivalenceTests` compares name/type/notnull/default/pk). Both default **NULL** ⇒ a v39
   company reloads byte-identical (ER-13).

`src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs`:
- **`EnsureSchema` stepper:** after the `version == 38` block (SqliteCompanyStore.cs:936-949), add a
  `version == 39` block that runs `Schema.MigrateV39ToV40`, bumps `schema_version` to 40, sets `version = 40`
  (copy the shape of the :936 block exactly). The final guard then passes.
- **Read:** append `composition_sub_type, composition_opt_in_date` to the **END** of the companies `SELECT`
  (SqliteCompanyStore.cs:973-984, after `bonus_prorate`) and read them at the **new trailing ordinals** — the
  reader is **positional** (`r.GetInt64(n)`), so **never insert mid-list**. Populate them into the
  `GstConfig` inside the existing `if (r.GetInt64(16) != 0)` GST-enabled block (:1025-1036):
  ```csharp
  CompositionSubType = r.IsDBNull(N)   ? (CompositionSubType?)null : (CompositionSubType)(int)r.GetInt64(N),
  CompositionOptInDate = r.IsDBNull(N+1) ? (DateOnly?)null : ParseDate(r.GetString(N+1)),
  ```
- **Write:** extend the `INSERT INTO companies (…)` column list + `VALUES` (SqliteCompanyStore.cs:3690-3719)
  with the two columns + `$compsub`/`$compdate`, and bind next to `$gstreg` (:3742):
  ```csharp
  cmd.Parameters.AddWithValue("$compsub",  gst?.CompositionSubType is { } st ? (int)st : (object)DBNull.Value);
  cmd.Parameters.AddWithValue("$compdate", gst?.CompositionOptInDate is { } d ? FormatDate(d) : (object)DBNull.Value);
  ```
  (Bind `DBNull` when the company is non-composition ⇒ existing companies byte-identical.)

**Downgrade helper (test-only, ER-13 legacy DDL):** the `DowngradeToV11` helper (in
`tests/Apex.Persistence.Sqlite.Tests/ItemInvoiceRoundTripTests.cs`, and its sibling in
`InventoryVoucherRoundTripTests.cs`) already rewrites `companies` to its early shape; **confirm** the rewrite
drops the two new columns (a column-list rewrite of `companies` naturally excludes them — no per-column DROP
needed, mirroring how the v38/v39 company columns were handled). No new table to `DROP`.

---

## 6. Io canonical model (RQ-23, ER-13)

S3's Io footprint is **just the two config members** (CMP-08 / GSTR-4 are recomputed projections, not persisted
data — nothing extra to round-trip).

`CanonicalModel.cs` — `GstConfigDto` (CanonicalModel.cs:1002): append two members **at the END** (after
`RcmCategories`, keeping existing order):
```csharp
// Phase 9 slice 3 (RQ-4): composition sub-type + opt-in date. Null when not a composition dealer ⇒ byte-identical (ER-13).
public string? CompositionSubType { get; init; }   // CompositionSubType name, or null
public string? CompositionOptInDate { get; init; } // ISO yyyy-MM-dd, or null
```

`CanonicalMapper.cs` — `MapGstConfig` (CanonicalMapper.cs:587): add
```csharp
CompositionSubType = g.CompositionSubType?.ToString(),
CompositionOptInDate = Iso(g.CompositionOptInDate),
```

`CanonicalXml.cs` — `BuildGstConfig` (CanonicalXml.cs:439): add `Opt("compositionSubType", g.CompositionSubType)`
and `Opt("compositionOptInDate", g.CompositionOptInDate)` (null-omitting, matching the existing `Opt` style);
`ReadGstConfig` (CanonicalXml.cs:1332): read them back with `Str(e, "compositionSubType")` /
`Str(e, "compositionOptInDate")`.

`ImportPlan.cs` — `BuildGstConfig` (ImportPlan.cs:1075): set on the constructed `GstConfig`:
```csharp
CompositionSubType = g.CompositionSubType is { } st ? ParseEnum<CompositionSubType>(st) : null,
CompositionOptInDate = CompanyImportService.ParseDateOpt(g.CompositionOptInDate),
```

`CanonicalJson.cs` — **no change** (`DefaultIgnoreCondition.Never` emits both members; null for a Regular
company ⇒ byte-identical within the build).

**CompanyImportService all-or-nothing:** it already delegates config construction to `BuildGstConfig` and
posts through the engine. Because `GstConfig.EnsureValid` (§1.3) now rejects a composition config with a
missing sub-type / GSTIN, a malformed imported composition config throws in **pre-flight** ⇒ `Applied = false`,
target company byte-unchanged (RQ-23). No separate mirror code is needed — the guard rides on `EnsureValid`
(the recurring "mirror into CompanyImportService" defect class is covered because the composition **posting**
suppression lives in `ComputeInvoiceTax`/`EnableGst`, which the import path also goes through).

---

## 7. TDD test list (write failing-first)

**Engine (`tests/Apex.Ledger.Tests`):**
1. **Bill of Supply — no output tax.** A Composition company sells a taxable item; assert
   `ComputeInvoiceTax(lines, …, Output)` returns **no tax lines**, zero `TotalTax`/`TotalCess`, and the
   assembled sale posts party Dr = supply value, sales Cr = supply value, **balanced, no CGST/SGST/IGST leg**.
   The same company's Regular twin still posts tax (proves the guard is reg-type-scoped).
2. **ITC blocked.** A Composition company records an ordinary Purchase of a taxable item;
   `ComputeInvoiceTax(lines, …, Input)` returns **no Input tax line** — no ITC ledger movement (the tax is
   embedded in the purchase cost).
3. **`EnableGst` gates the 6 ledgers off.** `EnableGst` on a Composition config creates **no** Output/Input
   CGST/SGST/IGST ledgers (only Round Off); a Regular config still creates the six (byte-identical).
4. **Tax-on-turnover per sub-type + base (paisa-exact).** With a fixed set of outward supplies (mix of taxable
   + exempt):
   - Manufacturer 1% on **total** turnover; Restaurant 5% on **total**; Trader 1% on **taxable-only**;
     ServiceProvider 6% on **taxable-only** — assert `TurnoverBase` picks the right base and
     `CompositionTax` = round(base × rate) split CGST+SGST with `CGST + SGST == total` (odd-total → SGST +1
     paisa).
   - A golden paisa case, e.g. Trader base ₹1,00,001 @ 1% ⇒ total 1000.01 → CGST 500.00, SGST 500.01.
5. **Inward RCM still normal-rate but blocks ITC (ER-4).** A Composition company with one inward RCM supply:
   the **Output RCM liability** posts (cash-only, appears in CMP-08 Table 3(ii)); **no Input ITC** line is
   created (the RCM tax is costed). A Regular company's RCM dual-leg is unchanged.
6. **CMP-08 reconciles.** `Cmp08.Build(company, quarter)` — outward tax-on-turnover + inward-RCM tax; total
   payable = tax-on-turnover + RCM inward; a non-composition company yields an empty/not-applicable CMP-08.
7. **GSTR-4 / GSTR-9A projections.** Four quarters' CMP-08 roll up into GSTR-4 Table 5; inward-supply tables
   populated from Purchase vouchers (value, no ITC); GSTR-9A light roll-up; empty for a non-composition company.
8. **GSTR-1 / GSTR-3B suppressed for composition.** `Gstr1.Build` / `Gstr3b.Build` on a Composition company
   return an **empty** return; the same posted supplies on a Regular company still produce full GSTR-1/3B
   (proves the guard is reg-type-scoped, byte-identical).
9. **Threshold / special-state logic.** `CompositionThreshold.Threshold`: general state ₹1.5 cr; a special
   state (05/11/12/13/14/15/16/17) ₹75 L; ServiceProvider ₹50 L regardless of state. (Advisory — assert the
   resolver, not a hard posting block.)

**Persistence (`tests/Apex.Persistence.Sqlite.Tests`):**
10. **Migration parity + round-trip.** `SchemaMigrationEquivalenceTests` passes at v40 (CreateV1 ==
    v1→v40 chain). New **`CompositionSchemaTests`** (mirror `GstRateHistoryCessSchemaTests`): (a) a fresh DB
    stamps to `CurrentVersion` and has both new columns; (b) a legacy **v39** DB auto-migrates to v40
    preserving rows, new columns NULL (ER-13), re-open idempotent; (c) save/load round-trips a Composition
    company — sub-type + opt-in date exact.

**Io (`tests/Apex.Ledger.Io.Tests`):**
11. **JSON + XML lossless round-trip.** A Composition company (sub-type + opt-in date) round-trips through
    `CanonicalJson` and `CanonicalXml`: export→parse→export **byte-stable**, reconstructed `Company` equal.
12. **ER-13 byte-identical when off.** A **Regular** company (and Robert / Bright / a plain Phase-4 GST
    company) exports JSON+XML with `compositionSubType`/`compositionOptInDate` **null/omitted**; assert the
    bytes match a captured pre-S3 golden (the new members are inert) and that a Regular company and a v39
    company serialise identically.
13. **CompanyImportService all-or-nothing.** Importing a valid Composition canonical model rebuilds the config
    and issues a Bill of Supply (no tax) through the engine; a malformed composition config (sub-type missing,
    or Composition without a GSTIN) rejects the whole batch (`Applied = false`, target byte-unchanged).

**Regression:** 14. **Robert + Bright green** (accounts-only / trading, no GST → untouched). All Phase-4 base
GST + P9-S1 (cess/rate-history) + P9-S2 (RCM/CDN/advances) fixtures **green and byte-identical** (a Regular
company enters no composition branch). **De-brand scan clean** (ER-11: the "Bill of Supply" declaration + any
new label/JSON say "Apex", never "Tally").

---

## 8. Risks / subtleties for the implementer

1. **Keep a Regular company byte-identical — the whole slice is a reg-type guard.** Every branch
   (`ComputeInvoiceTax` short-circuit, `EnableGst` ledger gate, `Gstr1/Gstr3b` suppression, RCM no-ITC) must
   fire **only** when `company.Gst?.RegistrationType == Composition`. A Regular/Unregistered company must hit
   the identical code paths it does at v39. The two new `companies` columns + two `GstConfigDto` members
   default NULL/null. *(Top risk — ER-13 is a hard exit-gate item.)*
2. **Turnover base correctness per sub-type.** Manufacturer/Restaurant tax **total** turnover in state (incl.
   exempt); Trader/ServiceProvider tax **taxable-only**. Getting the base wrong silently mis-states the
   composition tax. Turnover is read from **stock/sales value** (a composition voucher has **no tax lines**, so
   `InvoiceTaxableValue` — which reads tax lines — returns 0 and must **not** be used for turnover). Classify
   taxable-vs-exempt off the item/ledger `GstTaxability`.
3. **Don't break Phase-4 GSTR-1/3B for Regular dealers.** The suppression guard sits at the very top of
   `Gstr1.Build`/`Gstr3b.Build` and returns an empty return **only** for composition; the existing body is
   untouched. Re-run the Phase-4 GSTR golden tests on a Regular company and assert unchanged output.
4. **RCM-still-applies × no-ITC (the S2↔S3 interaction, §2.4).** ER-4 requires the composition dealer's inward
   RCM to still be charged at the **normal rate** and paid in **cash**, but **without** an ITC credit — the RCM
   tax is a **cost**. The naïve reuse of S2's dual-leg would wrongly grant ITC. Branch `RcmService` on
   reg-type: keep the Output liability leg (→ CMP-08), route the debit to expense/cost, and post **no** Input
   ITC line. Test 5 locks this.
5. **Paisa on the C+S split.** The composition tax is split **once** (compute total = round(base × rate), then
   CGST = round(total/2), SGST = total − CGST) — never two independent half-rate roundings (±0.01 drift, the
   recurring A10 finding). Reuse `GstService.TaxAmount` + the `ComputeLineTax` idiom.
6. **CMP-08 must include inward RCM + keep cess ring-fenced (ER-2).** CMP-08 Table 3(ii) is the composition
   dealer's cash RCM liability; fold the GST heads into `TotalPayable` but keep **Cess** a separate field
   (never mingled with the GST payable), consistent with S1's ring-fence.
7. **Offline-JSON schema is A14-gated (R7).** There is no precedent GSTR JSON writer to copy the envelope from;
   build `GstReturnJson` deterministically (invariant culture, integer-paisa, fixed member order) but have A14
   confirm the CMP-08 / GSTR-4 offline-tool field names before it is considered done.
8. **Positional store readers.** The companies `SELECT`/`INSERT` are ordinal-positional — **append** the two
   columns at the **end** of both lists and read at the new trailing ordinals; inserting mid-list silently
   corrupts every subsequent field (payroll/bonus columns sit at the current tail).
9. **Deviations to record in `memory.md` (R6).** (a) No `composition_config` table — consolidated into two
   `companies` columns (1:1, mirrors `gst_reg_type`). (b) Bill of Supply is a **derived** property, not a
   stored document flag. (c) CMP-02/CMP-04 + ITC-03/ITC-01 switch-over, §50 interest, and deep Desktop UI /
   print-column polish are **carry-forward** (S7 / follow-up UI pass), consistent with S1's deferred-UI
   posture.

---

## 9. Definition of Done (PR-3 gate, requirements §5)

Composition dealer issues a **Bill of Supply** with **no output tax + no ITC** · tax-on-turnover base correct
per sub-type (Manufacturer/Restaurant **total** vs Trader/ServiceProvider **taxable**), split C+S paisa-exact ·
**CMP-08 (quarterly) + GSTR-4 (annual)** projections + offline JSON reconcile · **RCM inward still at normal
rate but ITC-blocked** (ER-4) · **GSTR-1/3B suppressed** for a composition company · threshold/special-state
resolver correct · legacy-DDL **v39→v40** upgrade preserves rows · **ER-13 byte-identical when the company is
Regular** · migration parity (`CreateV1 == MigrateV39ToV40` chain) · Io JSON+XML + `CompanyImportService`
round-trip lossless & all-or-nothing · **Robert + Bright green**, all P9-S1/S2 fixtures green · **Schema.cs:73
stale doc comment fixed** · de-brand scan clean. Then A10 adversarial pass (3 lenses) → I re-run the Release
gate → A12 commits.
