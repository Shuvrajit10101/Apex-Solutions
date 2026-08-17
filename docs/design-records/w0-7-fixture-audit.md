> **HISTORICAL DESIGN RECORD — A SNAPSHOT, NOT A LIVE DOCUMENT.**
> Captured during the 2026-08-16/17 run and preserved here because the session scratchpad it was written in does
> not survive the session. It records what was true at the moment it was written; the tree has moved since.
>
> **CITATION POLICY.** Every `file.ext:NN` pointer in the original has been rewritten to `file.ext line NN`, so
> the repository's citation invariant (`DocumentCodeAgreementTests`) does not read them as live pointers. That
> is deliberate: these line numbers were accurate when captured and are NOT maintained. Re-derive before relying
> on any of them. The live, maintained pointers are in `plan.md` and `memory.md`, which are re-anchored on edit.
# W0-7 — extend `PopulatedCompanyFixture` to every voucher family

**Design agent, read-only pass. Repo read: `…\.claude\worktrees\recursing-swirles-3138c6`, branch
`claude/apex-wrong-figures-bc45f4`, HEAD `3a4fcdb`.**

---

## §0 — HEADLINE: THE SLICE IS ALREADY BUILT AND COMMITTED. THIS IS NOT A DESIGN, IT IS AN AUDIT.

The briefing for this slice was written on a stale premise. **W0-7 shipped.** Measured, not relayed:

```
$ git log --oneline -- tests/Apex.Desktop.Tests/Fixtures/PopulatedCompanyFixture.cs
1de940e test(fixture): W0-7 — extend the populated fixture to every voucher family
3774695 fix(ui): one row-track fixes three critical layout defects (StockSummary, PriceList, item-invoice)
```

`1de940e` is an **ancestor of HEAD** and the file is **NOT in `git status --porcelain`** — i.e. it is
committed, clean, and untouched by the concurrent W0-2b working-tree edit. The 21 dirty paths in the
working tree (17 ` M`, 4 `??`) are all W0-2b/WF-1 files; **none** of them is the fixture, the coverage
test, or any file this slice would own.

Files that exist today and did not exist when the census was written:

| Path | State |
|---|---|
| `tests\Apex.Desktop.Tests\Fixtures\PopulatedCompanyFixture.cs` | 1404 lines, committed, clean |
| `tests\Apex.Desktop.Tests\Fixtures\PopulatedFixtureCoverageTests.cs` | committed, clean — the coverage assertion §8 asked for |

**Therefore the deliverable of this pass is inverted**: instead of designing an extension, I have
measured the shipped extension against the nine questions the briefing posed, and I record below
(a) what the shipped slice actually did, (b) where the census text is now *stale and must be corrected*,
and (c) the **residual gaps that are genuinely still open** — which is the only part that is real work.

> ⚠️ **For the main loop:** do not re-run W0-7 as a build slice. Re-running it would re-derive a fixture
> that already exists and would collide with `1de940e`. The actionable output of this pass is §9
> (residual gaps) and §7 (the census/§1.3 correction), not a rebuild.

---
## §1 — GROUND TRUTH: WHAT THE FIXTURE POSTS TODAY (measured from source, not relayed)

### 1.1 The denominator

| Thing | Value | Where measured |
|---|---|---|
| `VoucherBaseType` enum members | **24** | `src\Apex.Ledger\Domain\VoucherBaseType.cs line 9-32` |
| `SeedVoucherTypes.Count` | **23** | `src\Apex.Ledger\Seed\SeedVoucherTypes.cs line 71` (`public const int Count = 23;`) |
| The 24th, unseeded | **`Attendance`** | `SeedVoucherTypes.cs line 59-66` — the row is deliberately absent (decision D24 option B); the **enum member stays**, because `voucher_types.base_type` persists as the enum **ordinal**, so deleting it would renumber `Payroll` 23→22 and every stored Payroll type would load as Attendance |
| Fixture voucher types after `BuildRegular()` | **24** | 23 seeded + the POS Sales variant added at `PopulatedCompanyFixture.cs line 1205-1220` |

**⚠️ ONE STALE COMMENT FOUND, and it is exactly the W0-6 defect class.**
`src\Apex.Ledger\Services\CompanyFactory.cs` carries the comment **`// 24 voucher types.`** immediately
above `foreach (var t in SeedVoucherTypes.Build())`, which builds **23**. `SeedVoucherTypes`' own doc
says *"Count guard: exactly 23 (was 24 — the dead Attendance row is gone)"*. Missed by W0-6's count
sweep. Trivial, one line, and it belongs to **W0-6's open remainder — not to this slice.**

> 🔴 **CITE THIS ONE BY TEXT, NOT BY LINE — and here is the proof of why.** I read that comment at
> `CompanyFactory.cs line 41` early in this pass and at **`:55`** an hour later. The file was **clean** in
> `git status` at the start of the session and is **` M` now**: the concurrent W0-2b agent modified it
> *while I was reading*. `SeedVoucherTypeSet()` moved `:61` → `:75` over the same interval. This is the
> project's own recorded rule (*"a version number carried forward from prose written before the previous
> slice landed"*) demonstrating itself in real time. **Every line number in this document is a snapshot
> at the moment it was read; the `src/` ones especially may already have moved. The quoted TEXT is the
> durable reference.**

### 1.2 The numerator — every base kind the fixture now posts

`BuildRegular()` (`PopulatedCompanyFixture.cs line 71-119`) calls five posting passes. Enumerated by reading
each one:

| Pass | `file:line` | Base kinds posted | n |
|---|---|---|---|
| `PostVouchers` | `:782-910` | Sales, Purchase, Payment, Receipt, Journal, Contra, DebitNote, CreditNote | 8 |
| `PostStockAndOrderVouchers` | `:956-1133` | ReceiptNote, DeliveryNote, RejectionIn, RejectionOut, StockJournal, PhysicalStock, PurchaseOrder, SalesOrder, JobWorkOutOrder, JobWorkInOrder, MaterialOut, MaterialIn | 12 |
| `PostProvisionalVouchers` | `:1145-1182` | Memorandum, ReversingJournal (+ an **Optional-flagged Journal**, `optional: true` at `:1180` — the third exclusion mechanism, which is a *flag*, not a base kind) | 2 |
| `PostPosBill` | `:1196-1268` | Sales again, on a **second, POS-flagged** `VoucherType` (`useForPos: true`, `:1206`) | 0 new |
| `RunPayroll` | `:1287-1352` | Payroll, via `PayrollVoucherService`, plus `AttendanceEntry` rows | 1 |
| | | **TOTAL DISTINCT** | **23** |

**23 of 23 seeded base kinds are posted.** Attendance is the only enum member with no voucher.

**The fixture's handling of it is right; the product's is a fidelity divergence. Both halves must be
said, and §7.2 says the second with the corpus behind it.** *Right in the fixture:* nothing in **our**
product posts a `Voucher` of that kind — the screen writes `AttendanceEntry` rows through
`PayrollAttendanceService`, and the fixture does exactly that (`:1305-1307`), while
`MainWindowViewModel.CanAddFromDayBook` still excludes Attendance from the Alt+A picker. So the fixture
models the **shipped reachability** rather than an invented one, which is the correct choice for a test
instrument. *Divergent in the product:* **TallyPrime does ship Attendance as a voucher**, entered at
`GOT > Voucher > F10 > Attendance` (corpus Book PDF p.29 and p.374 — see §7.2). Nothing in this pass
asks to change either; the point is that **"we post no Attendance voucher" is a fact about us, not a
fidelity result**, and it has been filed as the latter in `SeedVoucherTypes`' own comment.

### 1.3 🔴 THE CENSUS'S TWO CLAIMS, CHECKED — one is now FALSE, one is STILL TRUE

| Census claim | Where | Verdict |
|---|---|---|
| *"8 of 23 base types, zero inventory/order/job-work/POS/payroll vouchers"* | `docs\full-clone-census.md line 188`, and again in the prerequisite graph at `:249-251` (*"posts 15 more base types … Currently 8 of 23"*) | **FALSE at HEAD.** It is **23 of 23** since `1de940e` (2026-08-10). Both census sites are stale. |
| *"No print or export test uses `PopulatedCompanyFixture`. Every renderer is locked against thin bespoke fixtures … it is unchanged."* | `docs\full-clone-census.md line 325` (finding 9) | **STILL TRUE at HEAD, verified exhaustively.** See §6. |

**And a claim in my own briefing is also wrong, so I state it rather than inherit it:** the briefing said
*"one of them (W0-2b) added a print test"* using the fixture. W0-2b's print test is
`A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block`
(`tests\Apex.Desktop.Tests\VoucherInvoicePrintViewModelTests.cs line 370`) — and that file contains **no
reference to `PopulatedCompanyFixture` at all** (grep: only its own class name at `:27` and a
cross-reference comment at `:327`). It builds a bespoke company. So W0-2b did **not** close finding 9.

### 1.4 The complete consumer list — 8 test classes + 1 shared guard

`grep -r PopulatedCompanyFixture` → **44 occurrences in 12 files**, of which 2 are docs (`plan.md` ×5,
`docs\full-clone-census.md` ×3) and 10 are code:

| Consumer (`tests\Apex.Desktop.Tests\…`) | How it enters |
|---|---|
| `CaAuditLayoutLockTests.cs line 64,71` | `storage.Save(BuildRegular())` → real company-select |
| `KeyboardArbitrationTests.cs line 94,99,670,693` | same |
| `ItemInvoiceStockItemColumnTests.cs line 70,77` | same |
| `InventoryReportScrollReachabilityTests.cs line 96,103` | same |
| `SharedGridVariantBudgetLockTests.cs line 238,244,327` | SQLite path **and** a direct `new ReportsViewModel(BuildRegular(), kind)` |
| `StatutoryColumnBudgetLockTests.cs line 320,326,476,485` | same |
| `Fixtures\ReportEmptyStateShapeTests.cs line 43,66` | direct, plus it borrows `FyStart` for its *empty* control company |
| `Fixtures\PopulatedFixtureCoverageTests.cs` (12 hits) | the slice's own lock |
| `Fixtures\ReportContentGuard.cs` (2 hits) | shared row-floor helper |
| `Fixtures\PopulatedCompanyFixture.cs` | the fixture |

Every SQLite-path consumer resolves the company **by name**
(`vm.Menu.First(m => m.Label == RegularCompanyName)`), never by index or id — which is why the
`Guid.NewGuid()` identifiers in §4 are harmless to them.

---

## §2 — THE SHAPE OF THE EXTENSION, AS BUILT (briefing item 2)

The briefing asked what a representative voucher of each missing family needs, and *"where adding a
family means adding a whole master graph."* The shipped slice answers this; it is recorded here because
it is the reusable part — the next fixture extension will face the same table.

| Family | Posting service used | Master graph it needed | Present pre-W0-7? |
|---|---|---|---|
| ReceiptNote, DeliveryNote, RejectionIn, RejectionOut | `InventoryPostingService.Post(InventoryVoucher …)` | items + godowns + batch labels + a party ledger | **yes** — no new masters |
| StockJournal | `InventoryVoucher.StockJournal(…)` | two godowns; source and destination **must balance in the base unit** | yes |
| PhysicalStock | `InventoryVoucher.PhysicalStock(…)` | item + godown; the counted qty **SETS** book qty (DP-3) | yes |
| PurchaseOrder, SalesOrder | `InventoryVoucher.Order(…)` | items + godowns + party; affects **neither** stock nor accounts | yes |
| JobWorkInOrder, JobWorkOutOrder | `InventoryVoucher.JobWork(…)` + `JobWorkOrder` payload | a **third-party godown** (`thirdParty: true`), a finished good, component lines tracked `PendingToIssue` / `PendingToReceive` | **godown added** at `:383` ("Bharat Zinc Electroplating Job Work Premises (3rd Party)") |
| MaterialOut | `JobWorkService.BuildMaterialOutTransfer(…)` | balanced transfer + order link | yes |
| MaterialIn | `JobWorkService.BuildConsumingMaterialIn(…)` | **balance-EXEMPT transform**; requires the type stamped `AllowConsumption` | **gate had to be fixed — see 2.1** |
| Memorandum, ReversingJournal | `LedgerService.Post(…)` | ordinary ledgers; `applicableUpto` for the reversing one (`:1167`) | yes |
| Optional (a **flag**, not a base kind) | `LedgerService.Post(…, optional: true)` | none | yes |
| **POS** | `LedgerService.Post(…, posTenders:)` + `PosTenderService` | **a whole graph**: a second POS-flagged `VoucherType` carrying `PosConfig`, plus **four tender ledgers under the groups `PosTenderService` requires** — Gift→Sundry Debtors, Card/Cheque→Bank, Cash→Cash-in-Hand (`:1200-1219`) | **ALL NEW** |
| **Payroll** | `PayrollVoucherService.Post(…)` | **the largest graph**: attendance types of the right *kind*, payroll units, pay heads created **through `PayHeadService`** (attendance linkage + computation formulae), employee-scoped salary structures, and **attendance recorded first** | **partly new — and three pre-existing master sets were UNPOSTABLE** |

### 2.1 The two places where "add a family" meant "fix a master graph that was decorative"

These are the load-bearing findings of the slice, and they generalise:

1. **The attendance-type *kinds* were all wrong** (`:682-704`). All four were seeded `AttendancePaid` —
   including *"Absent — Loss of Pay"* and *"Overtime Hours Worked"*, which by definition are not paid
   attendance. Consequence: an On-Attendance head cannot pro-rate against a loss-of-pay type that claims
   to be paid, and **no On-Production head could exist at all**, because `PayHeadService` requires a
   `Production`-kind type and the company had none.
2. **The pay heads were unpostable** (`:708-713`). They were built with `new PayHead(...)` straight onto
   the company, **bypassing `PayHeadService`'s validation**, and every one of them violated it — the
   On-Attendance head linked no attendance type; three As-Computed-Value heads carried no formula at all.
   *Any attempt to actually run payroll on this fixture threw.* They now go through the real service.
3. **Job Order Processing was a raw flag** (`:91-97`). Assigning `c.EnableJobOrderProcessing = true`
   turns the **menu rows** on while leaving the four Job-Work voucher types `IsActive = false` and
   unstamped. The fixture now drives `new JobWorkService(c).SetEnabled(true)` — the same path the real
   F11 toggle drives — which also stamps `UseForJobWork` / `AllowConsumption`.

**The generalisable lesson, and it is the one worth carrying into Phase 10.11:** a master built by
constructor instead of by its service is *decorative*. It satisfies the type system, renders in a list,
and cannot be transacted against. Three separate master sets in this fixture were in that state, and a
fixture that only ever *listed* them never noticed.

### 2.2 Stock coherence is load-bearing — it is the constraint a naive extension breaks

`PostStockAndOrderVouchers`' own doc (`:950-954`) records the trap: an outward line on a **batched** item
that omits its batch label opens a **separate `(item, godown, "")` key** that has never held anything, so
it goes negative by the full quantity **while the item's total on-hand still looks healthy**. Every
outward line therefore names the same batch the stock arrived under, and inward vouchers are dated before
outward ones so no key is ever short. This is asserted, not merely commented — see §8, test F.

---

## §3 — ODD-VALUE FIXTURES (briefing item 3) — SATISFIED, AND LOCKED

The standing rule (a ±₹0.50 defect survived its whole life under round-number assertions) is honoured.
The figures actually shipped, read from source:

| Family | Odd figures | `file:line` |
|---|---|---|
| ReceiptNote | 465.250 kg @ ₹69.35; 212.500 kg @ ₹248.65 | `:982-985` |
| DeliveryNote | 47 Nos @ ₹2.63; 63 Nos @ ₹17.44 | `:995-996` |
| RejectionIn / RejectionOut | 6 @ ₹2.63; 18.750 kg @ ₹248.65 | `:1004`, `:1011` |
| StockJournal | 42.750 Ltr @ ₹178.00, both legs | `:1018-1019` |
| PhysicalStock | counted **1068.375** Ltr | `:1026` |
| PurchaseOrder | 1,250.750 kg @ ₹69.35; 96.500 @ ₹181.27 | `:1034-1035` |
| SalesOrder | 145 @ ₹431.63; 320 @ ₹25.47 | `:1045-1046` |
| JobWorkOut / JobWorkIn | 318.625 @ ₹61.27; 214.375 @ ₹68.53; FG rates ₹47.86 / ₹21.35 | `:1062-1087` |
| MaterialOut / MaterialIn | 318.625 issued; **306.500 consumed** (deliberately ≠ issued) | `:1103-1123` |
| Memorandum | ₹12,473.85 | `:1149` |
| ReversingJournal | ₹18,942.37 | `:1159` |
| Optional Journal | ₹9,617.42 | `:1172` |
| POS | 34 @ **₹247.99**; tax from the real `GstService`; cash tender is a **derived residual** | `:1224-1235` |
| Payroll | 8 distinct basics ₹31,486.50 … ₹21,368.90; advances ₹1,943.25 / ₹2,617.40 / ₹1,284.75 / ₹3,071.60; conveyance ₹1,687.50; incentive ₹118.25/hr | `:1325-1331`, `:1339-1340` |
| Attendance | fractional overtime hours 14.25 / 6.50 / 18.75 / 11.25 / 4.75 / 9.50 / 21.25 | `:1296-1297` |

**The discipline is deliberately applied BY ROLE, which is the right refinement and is worth stating:**
Kg/Ltr items carry fractional *quantities*; **Nos** items carry whole quantities with fractional *rates*,
*"because no real book ships 60.125 bolts"* (`:945-948`). A blanket make-everything-fractional rule would
have made the fixture unrealistic, and therefore a worse instrument.

**Two totals are DERIVED, never literal** — the POS bill total comes from
`GstService.ComputeInvoiceTax` and the cash tender from `PosTenderService.CashResidual(…)`
(`:1228-1235`) — so no hand-typed grand total can drift away from the posted one.

### 3.1 🔴 The subtlest thing in the slice — two odd-value assertions that CANNOT FAIL

`PopulatedFixtureCoverageTests` §D (`:318-357`) records two near-misses, and they are the most
transferable lesson in the whole pass:

- **POS.** `HasOddPaisa(bill)` *looks* like the right check and is worthless — **18% of any whole-rupee
  taxable value lands on paisa**, so the posted CGST/SGST lines satisfy it whatever the counter rate is.
  Measured: rounding the rate to ₹248 left that form of the assertion **green**. The test therefore pins
  the **hand-typed counter rate** and the **cash residual** instead.
- **Payroll.** The posted amounts are largely derived (HRA 40%, EPF 12%, ESI 0.75%) and *"carry paisa
  almost whatever the inputs are"*. The test therefore pins the **hand-seeded salary-structure amounts**,
  plus a **distinctness** assertion: eight members must produce eight **distinct** net-payable figures,
  or the payslip and pay sheet are eight identical rows.

**Rule to carry forward: assert on what a tidy-up would actually round — the hand-typed input — never on
a derived figure that is odd by arithmetic necessity.**

---

## §4 — DETERMINISM (briefing item 4) — CLEAN HERE; THE NAMED DEFECT IS ELSEWHERE AND STILL LIVE

Measured across `tests\Apex.Desktop.Tests\Fixtures\*.cs`:

```
grep -n "DateTime\.\(Now\|Today\|UtcNow\)\|DateOnly\.FromDateTime\|GetHashCode\|new Random\|Random\.Shared" Fixtures/*.cs
  -> no matches (exit 1)
```

- **No `DateTime.Now` / `.Today` / `.UtcNow`.** Every date derives from the two constants
  `FyStart = 2025-04-01` and `FyEnd = 2026-03-31` (`:52-53`) via `AddDays`. The payroll period is the
  literal `2025-08-01 … 2025-08-31` (`:1290-1291`).
- **No `GetHashCode`, no `Random`.** Every identifier-shaped string is a formatted counter —
  `SIF/EMP/2025/{n:D4}`, `HEAT-{2025_000 + (n*137)}/LOT-{n:D3}-BHOSARI`, `1005{n:D8}`.
- **GSTINs are minted, not hashed.** `Mint()` (`:1376-1383`) computes a real Luhn-mod-36 check character
  through `Gstin.ComputeCheckDigit`, so every party passes `Gstin.Validate` and the fixture exercises the
  real validation path instead of side-stepping it.
- **`Guid.NewGuid()` appears 66 times**, exclusively as entity identity. This is *bounded*
  nondeterminism and it is safe **because no consumer keys on an id**: every SQLite-path consumer resolves
  the company by `m.Label == RegularCompanyName` (§1.4), and the fixture's own positional indexing
  (`inv.Items[6]`, `c.PayHeads[0]`) reads **in-memory insertion order**, which is a plain `List.Add`.
  **Residual risk, small but real:** if a load path ever returns a collection in id order, positional
  indexing after a round trip becomes flaky. `TypeFor` (`:936-938`) exists precisely to remove one such
  dependency for voucher types; **nothing does the equivalent for `Items` or `PayHeads`.** Logged as R-3.

### 4.1 🔴 THE `Math.Abs(pin.GetHashCode())` DEFECT THE BRIEFING NAMED IS **NOT** IN THIS FIXTURE — IT IS LIVE IN THE W0-2b WORKING TREE

```
tests\Apex.Desktop.Tests\CompanyProfileScreenTests.cs line 329
    var vm = CreateThroughScreen("Engine Floor Co " + Math.Abs(pin.GetHashCode()));
```

That file is **untracked** (`?? tests/Apex.Desktop.Tests/CompanyProfileScreenTests.cs`) — it is the
concurrently-running W0-2b agent's in-flight work, **not** a W0-7 artefact. Both hazards are real:
`string.GetHashCode()` is randomised per process on .NET Core (so the company name differs every run), and
`Math.Abs(int.MinValue)` throws `OverflowException`. **I have not touched it** (read-only mandate, and it
is another agent's mid-edit file). **Route it to the W0-2b fix pass, not to this slice.**

It is also the only `GetHashCode` under `tests/`; the four under `src/` are legitimate `Equals` overrides
(`ExpiryPeriod.cs line 108`, `Money.cs line 50`, `SavedReportView.cs line 100`) plus one doc comment.

---

## §5 — BLAST RADIUS (briefing item 5) — MEASURED, NOT ESTIMATED

The briefing rightly called this *"the main cost of the slice"*. It can now be reported as fact rather
than forecast, from `git show --stat 1de940e`:

```
tests/Apex.Desktop.Tests/CaAuditLayoutLockTests.cs                  |  41 +-
tests/Apex.Desktop.Tests/Fixtures/PopulatedCompanyFixture.cs        | 641 ++++++++-
tests/Apex.Desktop.Tests/Fixtures/PopulatedFixtureCoverageTests.cs  | 550 +++++ (new)
tests/Apex.Desktop.Tests/Fixtures/ReportContentGuard.cs             | 140 +++ (new)
tests/Apex.Desktop.Tests/Fixtures/ReportEmptyStateShapeTests.cs     |  69 +++ (new)
tests/Apex.Desktop.Tests/InventoryReportScrollReachabilityTests.cs  |  32 +-
tests/Apex.Desktop.Tests/KeyboardArbitrationTests.cs                |  23 +-
7 files changed, 1447 insertions(+), 49 deletions(-)
```

**Of the six pre-existing consumers, exactly THREE needed changes.** Verified that the other three
(`ItemInvoiceStockItemColumnTests`, `SharedGridVariantBudgetLockTests`, `StatutoryColumnBudgetLockTests`)
**did already exist at `1de940e`** (`git cat-file -e 1de940e:…`) and were **untouched** — so their
absence from the diff is genuine independence, not "added later".

### 5.1 What actually moved — and it is the opposite of what a blast-radius forecast expects

The predicted cost was *"every test that counts rows, sums totals, or asserts a report's contents"*. The
observed cost was almost entirely the reverse: **the assertions that moved were the ones that were
VACUOUS, and extending the fixture is what exposed them.**

| Consumer | What changed | Direction |
|---|---|---|
| `CaAuditLayoutLockTests` | `Rows.Count > 1` → `>= 8` (Batch-wise); `Existing.Count > 0` → `>= 8` (employees) | **floors RAISED** — `> 1` was satisfied by one batch row plus a total |
| `KeyboardArbitrationTests` | stale doc-comment counts ("38 ledgers, 28 stock items, 51 vouchers") struck; the file **asserts no absolute count** — every arm captures its own `before` at runtime and asserts a **delta** | **doc only**; the tests were already delta-based and got *stronger* on a bigger book |
| `InventoryReportScrollReachabilityTests` | the empty-state detector was replaced (prose `"No "` prefix → structural row counting via the new shared `ReportContentGuard`) | **defect fixed**, not assertion moved |

**No total was re-baselined, and no row count was lowered anywhere.** That is a direct consequence of a
design choice worth naming: the catalogue-depth SKUs are **masters only, with no opening stock and no
movements** (`:425-430`), and the provisional families **never reach the Trial Balance** (`:1141-1143`),
so neither can perturb a figure a consuming test measures. **Adding a family to a shared fixture is
cheap exactly to the extent that the family is inert to the aggregates other tests read** — that is the
transferable rule.

### 5.2 The three real hazards the slice hit, recorded because Phase 10.11 will hit them too

1. **A guard that could not guard.** `IsEmptyState` matched only placeholder prose beginning `"No "`,
   while `BuildReorderStatus`'s default branch renders *"All items are above their reorder levels."*
   Measured: with the reorder levels removed, the case **passed over a report containing that one
   sentence**. Detection is now structural — a row that is neither `IsHeader` nor `IsTotal` — and shared
   between the coverage lock and the scroll locks via `ReportContentGuard`, *so the two cannot drift*.
2. **`Rows.Count > 0` cannot tell populated from empty**, because **8 of the 11 inventory reports always
   emit at least one row**. Measured: it protected **12 of 44 cases while all 44 passed**.
3. **A report's as-of date is computed from `Company.Vouchers` only.** `ReportsViewModel.ComputeAsOf`
   never looks at `Company.InventoryVouchers` (`:1254-1262`), so a book whose newest activity is a stock
   movement opens every report **earlier than its own newest movement**. The fixture works around it by
   dating the POS bill (day 178) after the last inventory voucher (day 171). **The underlying defect is
   UNFIXED and is a live carry-forward** — see §9, R-1.

---

## §6 — 🔴 THE PRINT/EXPORT GAP (briefing item 6) — STILL OPEN. THIS IS THE REAL RESIDUAL WORK.

### 6.1 Verified: census finding 9 is unchanged at HEAD

`grep -rn PopulatedCompanyFixture tests/ src/` returns **10 code files** (§1.4). **Not one of them is a
print or an export test.** The print/export surfaces and their current fixtures:

| Surface | Test | Fixture it uses |
|---|---|---|
| `InvoicePdf.Render` | `Apex.Ledger.Io.Tests\InvoicePdfTests.cs` | bespoke |
| `ReportPdf.Render` | `Apex.Ledger.Io.Tests\ReportPdfTests.cs` | bespoke |
| `VoucherPdf`, `PosReceiptPdf`, `PayslipPdf`, `CertificatePdf` | `Apex.Ledger.Io.Tests\*` | bespoke |
| `CsvWriter` / `XlsxWriter` / `TabularExport` | `Apex.Ledger.Io.Tests\TabularExportTests.cs` | bespoke |
| `VoucherPrintProjector.ProjectInvoice` | `Apex.Desktop.Tests\VoucherInvoicePrintViewModelTests.cs` | **bespoke** (confirmed — no fixture reference) |
| `ExportViewModel` | `Apex.Desktop.Tests\ExportViewModelTests.cs` | **`vm.LoadRobertDemo()`** — the thin Robert demo (`:39`) |
| `PrintPreviewViewModel` | `Apex.Desktop.Tests\PrintPreviewViewModelTests.cs` | bespoke report |

**Why it has stayed open is structural, and the structure is only half a problem.** The fixture lives in
`Apex.Desktop.Tests`; the renderers are tested in `Apex.Ledger.Io.Tests`, a **different assembly that
cannot reference it** (`Apex.Ledger.Io.Tests.csproj` references only `Apex.Ledger.Io` and `Apex.Ledger`).
**But the reverse direction is already open:**

```
src\Apex.Desktop\Apex.Desktop.csproj  ->  ProjectReference Apex.Ledger.Io
```

so `Apex.Desktop.Tests` has `Apex.Ledger.Io` transitively — and **already uses it today**
(`BillOfSupplyPosAndPostingGuardTests.cs`, `BillOfSupplyRoutingTests.cs`). **A print/export test against
the populated fixture therefore needs NO new project reference and NO fixture move.**

### 6.2 The smallest change that closes finding 9 — designed

Two new tests in **one new file**, `tests\Apex.Desktop.Tests\Fixtures\PopulatedFixtureRenderReachTests.cs`,
using the entry pattern the six existing consumers already use verbatim:

```
storage.Save(PopulatedCompanyFixture.BuildRegular());
vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
```

**Test P — one PRINT surface.** Open a voucher of a family that only exists because of W0-7 and project
it through the shipped path. The strongest single choice is the **POS bill**, because it is the fixture's
only multi-tender document, its own renderer (`PosReceiptPdf`) had a shipped §10(4) defect, and its
grand total is derived rather than literal. Assert **the paisa**, not the byte count:
`ProjectInvoice(company, posBill).GrandTotal` equals the posted party/tender debit total. That is
precisely the invariant W0-10 was built to hold (*"₹47,296.73 printed against ₹55,810.14 posted"*), and
today **nothing checks it on a realistic book.**

**Test X — one EXPORT surface.** `ExportViewModelTests` already has every seam needed —
`new ExportViewModel(shell.Reports!, folder, now, writeBytes:)` (`:81`) injects both the clock and the
byte sink, so the test touches no disk and has no timestamp flake. Swap `vm.LoadRobertDemo()` for the
fixture, open a report **that only has rows because of W0-7** — `ReportKind.MaterialInRegister` or
`OrderRegister` — export CSV, and assert a **known odd figure reaches the bytes**: e.g. `1068.375`
(the Physical Stock count) or `465.250` / `248.65` (the Receipt Note). A round-number assertion here
would prove nothing; an odd figure proves the number survived projection, formatting and encoding.

**Scope discipline:** do **not** migrate the existing `Apex.Ledger.Io.Tests` renderers onto the fixture.
They are unit tests of the writers and are correctly bespoke. Finding 9's real complaint is that **no
renderer is ever driven by a realistic book at all** — two tests remove that, and the census row can then
be honestly rewritten from *"unchanged"* to *"partially closed: one print and one export surface"*.

### 6.3 Cost

~1 new file, ~120 lines, **no production change, no schema change, no new project reference**.
The `_writeBytes` seam and the by-name company activation mean no new flake surface.

---

## §7 — RULING 5: THE FIDELITY ROW (briefing item 7) — DRAFTED, AND THE CORPUS *DOES* SETTLE IT

The briefing anticipated *"the corpus does not describe our fixture"*. That is true of the fixture, and
irrelevant: **the voucher families themselves are squarely corpus-described, and the corpus turns out to
contradict us on one of them.** Read fresh with `pdftotext -layout` (R7; the rejected Short-Key PDF was
not used).

### 7.1 What the corpus establishes

**Source: `tally\664311548-Tally-Prime-Book.pdf`, PDF p.29 (printed "Page 25"), under the heading
*"How many Types of Pre-defined Voucher in Tally Prime?"*** — an explicit numbered table:

> *"There are 24 Pre-defined vouchers in Tally Prime"* — 1 Contra · 2 Receipt · 3 Payment · 4 Purchase ·
> 5 Sales · 6 Journal · 7 Memorandum · 8 Reversing Journal · 9 Rejections in · 10 Rejections out ·
> 11 Credit Note · 12 Debit Note · 13 Purchase order · 14 Receipt Note · 15 Sale Order ·
> 16 Delivery Note · 17 Stock Journal · 18 Physical Stock · 19 Job Work in order · 20 Material in ·
> 21 Job Work out order · 22 Material out · **23 Attendance** · 24 Payroll

**Our `VoucherBaseType` enum is a member-for-member match with this table — all 24, same families.** That
is a genuine, sourced fidelity result and it has never been recorded.

### 7.2 🔴 And the corpus contradicts our decision D24 — which must be stated, not glossed

`SeedVoucherTypes` justifies dropping the Attendance seed row with *"nothing in the product ever posted a
Voucher of base kind Attendance … it needs no voucher type at all"*. **That is true of OUR product and
false of TallyPrime's**, and the corpus is explicit:

> **Book PDF p.374 (printed p.370), *"Part 1: Attendance Voucher"*:** *"Attendance Voucher — It is used
> to record Employee's attendance dates based of Attendance/Production types (Present or absent or
> Overtime by days or Hours)."* — entered at **`GOT > Voucher > F10 > Attendance`**, capturing Date,
> Employee Name, Attendance/Production Type and Value, with both a **manual** and an **autofill** entry
> mode (PDF pp.374-375).

So TallyPrime records attendance **as a voucher, reached through the voucher menu**. We record the same
four fields as non-voucher `AttendanceEntry` rows through a dedicated screen, and exclude the kind from
the Alt+A picker (`MainWindowViewModel.CanAddFromDayBook`). **The DATA is faithful; the ENTRY ROUTE and
the voucher-ness are a divergence** — and D24's stated rationale is a statement about our
implementation being offered as though it were a statement about Tally.

**This does not reopen D24.** Keeping the enum member is separately and correctly justified (the ordinal
is persisted; deleting it would load every stored Payroll type as Attendance). The point is only that the
divergence must be **recorded as a divergence**, not filed as parity.

### 7.3 The row to add to `docs\full-clone-census.md` §1.3 — proposed text

> 10. **Voucher families — the 24 pre-defined kinds, and what each affects (added with W0-7).**
>     **SOURCED AND MATCHING.** The corpus Book (PDF p.29 / printed p.25) answers *"How many Types of
>     Pre-defined Voucher in Tally Prime?"* with an explicit numbered table of **24**, and our
>     `VoucherBaseType` enum is a **member-for-member match** — Contra, Receipt, Payment, Purchase,
>     Sales, Journal, Memorandum, Reversing Journal, Rejections In/Out, Credit/Debit Note, Purchase
>     Order, Receipt Note, Sales Order, Delivery Note, Stock Journal, Physical Stock, Job Work In/Out
>     Order, Material In/Out, Attendance, Payroll. The **stock-vs-accounts effect rules** for
>     PO/SO/GRN/DN are separately sourced at row 4. `PopulatedCompanyFixture` now posts a specimen of
>     **23 of the 23 kinds the product seeds**, asserted as data by `PopulatedFixtureCoverageTests`
>     across a real SQLite round trip.
>     **TWO DIVERGENCES, RECORDED RATHER THAN GLOSSED.** (a) **Attendance is a VOUCHER in TallyPrime**
>     — Book PDF p.374, *"Attendance Voucher … GOT > Voucher > F10 > Attendance"*, with manual and
>     autofill modes — whereas we seed no Attendance voucher type and write non-voucher
>     `AttendanceEntry` rows from a dedicated screen. The captured fields (employee, attendance/production
>     type, value, date) match; the entry route does not. Decision **D24 option B** chose this, and its
>     stated reason (*"nothing in the product ever posted a Voucher of that kind"*) describes **our**
>     product, not Tally's. (b) **Payroll ships `IsActive = false` and nothing in the product can
>     activate it** (gap **T1-4**), so 23 of 24 families are reachable and one is not.
>     **NOT SOURCED, and deliberately so:** nothing about the fixture's own *contents* — the party names,
>     the odd-valued quantities and the 8-employee roster are ours, chosen for layout stress and
>     round-number defect exposure, and the corpus neither describes nor could settle them.

**Note the shape:** it follows row 9's precedent — what IS sourced, what is OURS, and where each
divergence is already logged — which is what the R12 ruling at `docs\full-clone-census.md line 82` asks for.

---

## §8 — TESTS (briefing item 8) — THE LOCK EXISTS AND IS STRONG; TWO HOLES REMAIN

`tests\Apex.Desktop.Tests\Fixtures\PopulatedFixtureCoverageTests.cs` (550 lines) is the answer to *"what
test proves the fixture actually posts what it claims"*. Ten assertions:

| # | Test | What it stops |
|---|---|---|
| A | `Regular_fixture_posts_a_voucher_of_every_seeded_base_kind` | a family silently dropping out |
| B | `Regular_fixture_carries_a_pos_bill_and_recorded_attendance` | the two families a base-kind sweep is **blind to** |
| C | `Regular_fixture_posts_a_payroll_voucher_carrying_per_employee_detail` | a **half-run** — floor pinned at the whole roster, not a token `>= 4` |
| C2 | `Every_seeded_voucher_type_is_active_except_the_deliberately_inactive_payroll` | types seeded but **not reachable** |
| C3 | `Only_the_pos_bill_is_posted_on_a_specialised_voucher_type` | a bare `First(t => t.BaseType == x)` picking up the POS till |
| D | `Every_added_family_carries_at_least_one_odd_valued_figure` | a round-number "tidy-up" |
| E | `The_whole_book_survives_a_real_sqlite_round_trip` | coverage that exists only in memory |
| F | `The_book_never_drives_a_stock_key_negative` | coverage **bought with an incoherent book** |
| G | `Inventory_and_new_family_reports_render_real_rows_on_the_fixture` (Theory, **21** report kinds) | the point of the slice, as an assertion |
| H | `The_book_stays_large_enough_for_the_layout_locks_that_measure_it` | the book quietly shrinking |

**The briefing's second question — *"what stops the fixture being extended later without its coverage
assertion being updated?"* — is answered well.** Test A derives its **denominator from
`c.VoucherTypes`**, not from a hard-coded list, so **a newly seeded voucher type fails test A on the day
it is added** rather than quietly widening the blind spot. That is the right mechanism and it should be
copied wherever a coverage claim is made.

**Test F is the assertion that stops the whole thing being bought with nonsense**, and it deserves the
emphasis it gets: it is trivial to "post a Delivery Note" by despatching stock that was never received,
and since v50 the engine *accepts* it (negative stock is a detector, not a block). Without F, the
coverage would be real and the book would be a fiction.

### 8.1 Two holes that remain

- **H-1 — a new enum member with NO seed row is invisible to test A.** A's denominator is
  `c.VoucherTypes.Select(t => t.BaseType)`, i.e. the *seeded* kinds. Add a `VoucherBaseType` member and
  no seed row and **nothing fails** — which is precisely how `Attendance` reached its current state.
  The nearest guard, `SeedTests.cs line 157` (`Assert.Equal(23, SeedVoucherTypes.Count)`), locks the seed
  count but **never compares it to `Enum.GetValues<VoucherBaseType>().Length`**. A three-line test —
  *every enum member is either seeded or on a named, justified exclusion list* — would close it and
  would document the Attendance exclusion in code rather than in a comment.
- **H-2 — no print or export surface is in the lock at all.** Tests G covers 21 *report* kinds; there is
  no equivalent for the renderers. §6 is the fix.

---

## §9 — RISK, SCOPE, AND THE RESIDUAL WORK (briefing item 9)

### 9.1 What the shipped slice deliberately left out — and I agree with each

- **No income-tax pay head.** A head tagged `IncomeTaxComponent.TaxDeductedAtSource` would route the run
  through `SalaryIncomeTax`, whose **4% Health & Education Cess rate is the unverified figure at census
  T0-5 and a standing user decision.** The fixture's own comment (`:1281-1285`) states it plainly:
  *"A shared fixture must not bake an unresolved statutory figure into the numbers every other test
  reads."* **This is the single best judgement call in the slice.**
- **Payroll's voucher type stays `IsActive = false`.** The seed ships it inactive and **nothing in the
  product can activate it** (T1-4: no Voucher Type master; `PayrollService.EnablePayroll` does not flip
  the flag). The pay run is posted through `PayrollVoucherService`, which bypasses the resolver — so the
  voucher, the payslip and the payroll register are populated **while the Alt+A picker still correctly
  omits the type.** *(Briefing item 9's "verify before designing around it" — verified: `SeedVoucherTypes`
  ships `new("Payroll", …, false)`; `CanAddFromDayBook` excludes Attendance, and the resolver excludes
  inactive types. C2 pins Payroll as the **only** permitted inactive type, so the exception cannot spread.)*
- **Composition stays a second company.** `RegistrationType` is single-valued, so CMP-08 / GSTR-4 need
  `BuildComposition()`. Correct, and it is deliberately smaller.
- **Catalogue-depth SKUs are masters only** — no opening stock, no movements — so they stress list
  geometry without perturbing any aggregate.

### 9.2 🔴 RESIDUAL WORK — the actionable output of this pass

| id | What | Severity | Owner |
|---|---|---|---|
| **R-1** | **`ReportsViewModel.ComputeAsOf` scans `Company.Vouchers` only, never `Company.InventoryVouchers`.** A book whose newest activity is a stock movement (goods despatched today, invoice raised next week — the commonest sequence in an Indian trading book) opens **every report at an as-of date earlier than its own newest stock movement**, and that movement is invisible until the operator changes the date by hand. **Reproduced** during W0-7 (the Material In on day 171 fell outside the window and the register rendered EMPTY) and **worked around in the fixture, not fixed.** | **HIGH — a live product defect, not a test defect** | needs a plan row; belongs with the register/report work |
| **R-2** | **The print/export gap (census finding 9) is still open.** Design in §6: one new file, two tests, no production change, no new project reference. | MEDIUM — it is what turns the fixture from a posting fixture into a regression instrument | the natural W0-7 tail |
| **R-3** | Fixture indexes `inv.Items[n]` / `c.PayHeads[n]` positionally. Safe today (in-memory `List.Add` order), fragile if a load path ever reorders. `TypeFor` already solves this for voucher types; nothing does for items or pay heads. | LOW | opportunistic |
| **R-4** | **H-1** — a new `VoucherBaseType` member with no seed row is invisible to the coverage lock. ~3-line test. | LOW | opportunistic |
| **R-5** | `CompanyFactory`'s `// 24 voucher types.` comment vs `SeedVoucherTypes.Count == 23`. | TRIVIAL | **W0-6's open remainder** |
| **R-6** | **`Math.Abs(pin.GetHashCode())` at `CompanyProfileScreenTests.cs line 329`** — randomised per process, and `Math.Abs(int.MinValue)` throws. **Untracked file = the in-flight W0-2b slice.** | MEDIUM | **W0-2b fix pass — NOT this slice** |

### 9.3 🔴 DOCUMENT CORRECTIONS OWED (R5/R6) — the highest-value item in this pass

**`plan.md` and the census both still describe W0-7 as unstarted, seven days after it shipped.**

| File | Text | Correction |
|---|---|---|
| `plan.md` (Phase 10.12 row list) | *"**W0-7 (S0) `PopulatedCompanyFixture` extension** … It covers **8 of 23 base types and zero inventory, order, provisional, job-work, POS or payroll vouchers**, and **no print or export test uses it at all.**"* — **no DONE marker** | Mark **DONE at `1de940e` (2026-08-10)**; **23 of 23**; note the print/export half (R-2) is **the one part still open** |
| `plan.md` (§5 ruling-6 banner, and the Phase 10.11 header) | Both argue W0-7 must ship before Phase 10.11 *"while that fixture covers 8 of 23 base types"* | The **prerequisite is SATISFIED**. Phase 10.11 is **unblocked** — which is the operative fact for the main loop |
| `docs\full-clone-census.md line 188` | *"51 is right; 'every type' is not — 8 of 23 base types…"* | Now **23 of 23** since `1de940e` |
| `docs\full-clone-census.md line 249-251` (prerequisite graph, S0) | *"posts 15 more base types … Currently 8 of 23; no print/export test uses it at all"* | S0 **DONE**; the print/export clause **still stands** |
| `docs\full-clone-census.md line 325` (finding 9) | *"No print or export test uses `PopulatedCompanyFixture` … it is unchanged."* | **STILL TRUE — leave it standing** until R-2 lands |
| `docs\full-clone-census.md` §1.3 | no row for W0-7 | Add **row 10** (§7.3) |

⚠️ **Both `plan.md` and `docs\full-clone-census.md` are ` M` in the working tree right now** (the W0-2b
agent holds them). **These corrections must be sequenced after that slice commits, or they will be lost
in a conflict.**

---

## §10 — BOTTOM LINE FOR THE MAIN LOOP

1. **W0-7 is DONE** — `1de940e`, 2026-08-10, ancestor of HEAD, clean in the working tree. **23 of 23**
   seeded base kinds posted, locked by a 550-line coverage test across a real SQLite round trip.
   **Do not rebuild it.**
2. **The stated blocker on Phase 10.11 is therefore DISCHARGED.** The ruling of 2026-08-16 — *"W0-7
   ships first, then Phase 10.11"* — was made against a `plan.md` row that was already six days stale.
   **Phase 10.11 can start now.** Its regression surface is the 23 families the fixture posts, which is
   exactly what the ruling required.
3. **Three things are genuinely still open**, in priority order: **R-1** (the `ComputeAsOf` product
   defect — the only one that harms a user), **R-2** (the print/export gap, ~1 file), and the
   **document corrections in §9.3**.
4. **R-6 belongs to the concurrent W0-2b pass** and should be handed to it, not queued here.

*End of pass. Read-only throughout: no repo file was created, modified or deleted; no build or test run;
no git command beyond `log` / `status` / `show` / `diff` / `cat-file` / `merge-base`.*

