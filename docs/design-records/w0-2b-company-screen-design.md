> **HISTORICAL DESIGN RECORD — A SNAPSHOT, NOT A LIVE DOCUMENT.**
> Captured during the 2026-08-16/17 run and preserved here because the session scratchpad it was written in does
> not survive the session. It records what was true at the moment it was written; the tree has moved since.
>
> **CITATION POLICY.** Every `file.ext:NN` pointer in the original has been rewritten to `file.ext line NN`, so
> the repository's citation invariant (`DocumentCodeAgreementTests`) does not read them as live pointers. That
> is deliberate: these line numbers were accurate when captured and are NOT maintained. Re-derive before relying
> on any of them. The live, maintained pointers are in `plan.md` and `memory.md`, which are re-anchored on edit.
# W0-2b — Company Create/Alter screen: DESIGN

**Slice:** W0-2b (census S2 / T1-6; the WRITE half of T0-8, the CGST Rule 46(a) blank supplier address).
**Baseline:** worktree `…\.claude\worktrees\recursing-swirles-3138c6`, branch `claude/apex-wrong-figures-bc45f4`,
**HEAD `3a4fcdb`**. `docs/wf1-owed-review-findings.md` is modified in the working tree (a main-loop correction) and
was left alone.
**Method:** read-only. No file inside the repo was created or modified; no build and no test run was executed.
Every `file:line` below was opened at `3a4fcdb` during this pass.
**Governing ruling:** R12, 2026-08-15 — **INHERIT** (`plan.md`, W0-2b block, RULING 3).
**Prior art that must not be re-derived:** `docs/w0-2-company-screen-grounding.md` (the R7 corpus pass).

---

## 1. GROUND TRUTH — every inherited claim, re-verified at `3a4fcdb`

Verdict up front: **the brief's five traps all hold. Two claims in the grounding doc have drifted, and — the
find that changes the design — one mechanism the ruling appears to require is a silent data-loss trap in the
store as it stands today.** Details below; the trap is §1.9 and it drives §3.

### 1.1 ✅ HOLDS — `Company.EnsureValid()` has exactly one call site in `src/`, and it is the canonical import

Measured, not quoted. `grep -rn "EnsureValid()" src/ --include=*.cs` minus declarations and doc comments returns
**twelve** invocations across all of `src/`:

| Call site | Receiver |
|---|---|
| `src/Apex.Desktop/ViewModels/LedgerMasterViewModel.cs line 975` | `PartyMailingDetails` |
| `src/Apex.Desktop/ViewModels/StockItemMasterViewModel.cs line 611` | `StockItemGstDetails` |
| `src/Apex.Ledger/Domain/GstConfig.cs line 288` | `MasterGstDetails` (`DefaultGst?.`) |
| `src/Apex.Ledger/Services/GstService.cs line 76` | `GstConfig` |
| `src/Apex.Ledger/Services/TdsTcsService.cs line 47`, `:71` | `TdsConfig`, `TcsConfig` |
| `src/Apex.Ledger.Io/ImportPlan.cs line 188`, `:608`, `:665`, `:730`, `:1655` | `GstConfig` / `SalesPurchaseGst` / `MasterGstDetails` |
| **`src/Apex.Ledger.Io/ImportPlan.cs line 1203`** | **`Company` — the only one** |

`ImportPlan.cs line 1203` is `t.EnsureValid();` inside `ApplyCompanyHeader(Company t, ApplyJournal journal)`
(`ImportPlan.cs line 1190`), four lines after `t.State = c.State;` / `t.Pin = c.Pin;` (`:1198-1199`). The
declaration is `Company.EnsureValid()` at `src/Apex.Ledger/Domain/Company.cs line 97`; its body is the single rule
`if (!IndianPinCode.IsValidOrBlank(Pin)) throw new ArgumentException(...)` (`:99-100`).

**Nothing calls it on save.** `SqliteCompanyStore.Save(Company)` (`src/Apex.Persistence.Sqlite/SqliteCompanyStore.cs line 1790`)
does not, and neither does `InsertCompany` (`:4628`), which binds `c.Pin` verbatim at `:4694`
(`cmd.Parameters.AddWithValue("$pin", (object?)c.Pin ?? DBNull.Value)`). **This slice is what breaks it** — it is
the first UI that assigns `Company.Pin`. See §5.

### 1.2 ✅ HOLDS — `MasterGstDetails.EnsureValid` is reachable on 1 of 5 write paths

`src/Apex.Ledger/Domain/MasterGstDetails.cs line 62-71` states it in its own doc comment ("exactly **three** call
sites in `src/`"), and `docs/wf1-owed-review-findings.md` lens 2 finding 4 records the measurement: *"reachable
on exactly one of five write paths — the canonical import"*, and it is listed again under **"What this review
did NOT close"**. The same file's `:71` names *"the `Company.EnsureValid` limit recorded for W0-2a"* as the
sibling defect. **W0-2b must not add a sixth unguarded path** — see §5.

### 1.3 ✅ HOLDS — the create screen captures exactly one field, and there is no Alter screen

- `src/Apex.Desktop/Views/MainWindow.axaml line 228-244` is the whole create form: one `TextBox` bound to
  `NewCompanyName` (`:236`), a **static** hint `"Financial year begins 1-Apr; base currency ₹ INR."` (`:238-239`),
  and a `Button` `"Create (Ctrl+A)"` → `OnCreateCompanyClick` (`:240-242`). The `Border` is gated on
  `ConverterParameter=CreateCompany` (`:230`).
- `src/Apex.Desktop/ViewModels/MainWindowViewModel.cs line 815-823` `ShowCreateCompany()` — sets
  `CurrentScreen = Screen.CreateCompany`, `ScreenTitle = "Company Creation"`, clears `NewCompanyName`, sets the
  hint `"Enter the company name, then press Enter (Ctrl+A) to create."`, `LeaveCascade()`, `Menu.Clear()`,
  `BuildButtonBar()`.
- `src/Apex.Desktop/ViewModels/MainWindowViewModel.cs line 827-839` `CreateCompany()` — trims, refuses blank
  (`"A company name is required."`), `CompanyFactory.CreateSeeded(name)` (`:836`), `_storage.Save(company)`
  (`:837`), `OpenCompany(company)` (`:838`). **No second field is read.**
- The entry point is a menu row, not a cascade: `MainWindowViewModel.cs line 808`
  `Menu.Add(new MenuItemViewModel("Create Company", ShowCreateCompany, "F3"))`, built inside the Company-Select
  screen builder (`:795` sets `ScreenTitle = "Company Info — Select Company"`).
- **`grep -rn "AlterCompany" src/ --include=*.cs --include=*.axaml` returns ZERO hits.** The `Screen` enum
  (`MainWindowViewModel.cs line 17`, members from `:19`) carries `CompanySelect`, `CreateCompany`, `BackupCompany`,
  `RestoreCompany`, `GstConfig`, … and **no `AlterCompany`**.

### 1.4 ✅ HOLDS — `Company.State` has no assignment site in `src/Apex.Desktop`, and no consistency guard exists

The only writers of `Company.State` anywhere are the canonical import (`ImportPlan.cs line 1198`) and the journal
snapshot/rollback pair. The store round-trips it — write `SqliteCompanyStore.cs line 4693`
(`AddWithValue("$state", (object?)c.State ?? DBNull.Value)`), read `:1323` (`State = r.IsDBNull(5) ? null : r.GetString(5)`).
No `Apex.Desktop` view model assigns it. No consistency guard between `Company.State` and
`GstConfig.HomeStateCode` exists anywhere.

### 1.5 ✅ HOLDS — the 11 profile fields exist in the domain and in the schema; no migration for them

`src/Apex.Ledger/Domain/Company.cs` — `:59` `Id`, `:62` `Name`, `:65` `MailingName`, `:67` `Address`,
`:76` `Country = "India"`, `:85` `State`, `:89` `Pin`, `:104` `FinancialYearStart`, `:107` `BooksBeginFrom`,
`:109` `BaseCurrencySymbol = "₹"`, `:110` `BaseCurrencyName = "INR"`, `:111` `DecimalPlaces = 2`,
`:112` `DecimalUnitName = "Paisa"`.

> ⚠️ **Line drift against the grounding doc §7.1** (which was written at `fa651ae` and re-anchored at
> `85f82dd`): it gives `Country :68`, `State :69`, `Pin :70`, `FinancialYearStart :73`, `BooksBeginFrom :76`,
> `BaseCurrencySymbol :78`, `BaseCurrencyName :79`, `DecimalPlaces :80`, `DecimalUnitName :81`, `Gst :89`.
> **At `3a4fcdb` those are `:76`, `:85`, `:89`, `:104`, `:107`, `:109`, `:110`, `:111`, `:112`, `:120`.**
> The **claim** stands exactly — eleven profile fields excluding `Name` — only the numbers moved (W0-2a added
> the `Country`/`State`/`Pin` doc comments and `EnsureValid`). Do not copy §7.1's numbers into anything new.

`src/Apex.Persistence.Sqlite/Schema.cs line 179-194` — the `companies` DDL opens at `:179` and carries `name`,
`mailing_name`, `address`, `country`, `state`, `pin`, `financial_year_start`, `books_begin_from`,
`base_currency_symbol`, `base_currency_name`, `decimal_places`, `decimal_unit_name`, `primary_cost_category`,
`main_location`. **All present in the v1 create DDL.** `gst_home_state` is at `:201`.

### 1.6 ✅ HOLDS — Save is a delete-and-reinsert full rewrite

`SqliteCompanyStore.cs line 4602` `ExecTx(tx, "DELETE FROM companies WHERE id = $cid;", ("$cid", cid));` then
`InsertCompany(...)` (`:4628`), whose `INSERT INTO companies` (`:4634`) lists **86 columns** and binds the
eleven profile ones at `:4688-4700`. An Alter screen therefore needs **no new UPDATE statement**: mutate the
domain object, call `_storage.Save(company)`.

> ⚠️ **Line drift against grounding §7.6**, which gives `:4489` DELETE / `:4492` InsertCompany / `:4497` INSERT /
> `:4498-4501` columns, and `:1275` / `:1288-1300` for the read path. **At `3a4fcdb`: `:4602`, `:4628`, `:4634`,
> `:4635-4659`, and the read is `FROM companies WHERE id = $id` at `:1306`, mapped at `:1318-1341`.** Claim
> stands; numbers moved (WF-1 landed six more `companies` columns).

### 1.7 ✅ HOLDS — the print path never reads `Company.State`

`src/Apex.Desktop/Services/VoucherPrintProjector.cs line 721-727` `SellerBlock`:
`Name = Ascii(CompanyDisplayName(company))` (`:723`), `AddressLines = SplitAddress(SupplierPostalAddressText(company))`
(`:724`), `Gstin = Ascii(company.Gst?.Gstin ?? "")` (`:725`), `StateText = StateText(company.Gst?.HomeStateCode)`
(`:726`). Called at `:399` (item pass) and `:520` (service pass).
`SupplierPostalAddressText` (`:742-745`) returns `null` unless `company.Address` is non-blank, otherwise defers
to `PostalAddressText` (`:822-831`), which emits Address → Country → `"PIN: " + pin`, each skipped when blank.
`StateText(string? code)` (`:846-850`) resolves through `IndianState.FromCode` and returns `""` for an
unrecognised code. `SplitAddress` (`:855-866`) splits on **newlines only** — `"Pune, Maharashtra 411001"` is
ONE line, deliberately.

### 1.8 🔴 DRIFTED CLAIMS FOUND — two shipped code comments still call the R12 gate OPEN

Both were true when written and are false at `3a4fcdb`. **W0-2b must correct them in the same commit**, because
they are the two comments a future reader of this exact area will hit first:

1. **`src/Apex.Ledger/Domain/Company.cs line 79-84`** — the `State` doc comment ends: *"Its relationship to
   `GstConfig.HomeStateCode` is an **open R12 user gate** — see `plan.md` W0-2b."* **It is RESOLVED (INHERIT,
   2026-08-15).** The same comment's *"No print path reads this"* is still correct.
2. **`src/Apex.Desktop/Services/VoucherPrintProjector.cs line 698-707`** — `SellerBlock`'s doc comment: *"The
   capture question — expose both / suppress the postal one / wire one to the other — **remains an open R12 user
   gate** (`plan.md`, W0-2b)"*. Also resolved. The rest of that paragraph — that this method is independent of
   the answer, and that `A_company_whose_postal_State_disagrees_with_its_GST_State_prints_the_GST_one` pins it —
   remains exactly right under INHERIT and must be **kept**, only the "open gate" clause replaced.

No `.md`-side citation drift was found: the grounding doc's own `plan.md` pointers were re-anchored on
2026-08-16 and the W0-2b block is at `plan.md line 1830-1940` at `3a4fcdb` (row heading at `:1830`, RULING 3 at
`:1910`), consistent with what the doc claims.

### 1.9 🔴🔴 NEW, MEASURED, AND IT CHANGES THE DESIGN — the store *drops* a GST home State on a GST-off company

This is the single most important finding of this pass, and nothing in the grounding doc or `plan.md` records
it. The obvious reading of RULING 3 — *"the GST home State DEFAULTS FROM the postal State **at creation**"* —
would have `CreateCompany()` build a `GstConfig` and stamp `HomeStateCode` on it. **That value cannot survive a
reload on a GST-off company.**

- **Write** (`SqliteCompanyStore.cs line 4704-4709`): `$gsten` is `gst is { Enabled: true } ? 1 : 0`, but
  `$gsthome` is `(object?)gst?.HomeStateCode ?? DBNull.Value` — **the home state is written whenever a
  `GstConfig` object exists, regardless of `Enabled`.**
- **Read** (`SqliteCompanyStore.cs line 1348-1350`): `if (r.GetInt64(16) != 0) { company.Gst = new GstConfig { … } }`
  — the entire config, `HomeStateCode` included, is reconstructed **only when `gst_enabled = 1`**. With GST off,
  `company.Gst` comes back **null** and the persisted `gst_home_state` is unreachable.

So a creation-time stamp writes a value that the very next `Load` throws away — and worse, it lingers in the
column where a later `Save` (which re-derives `$gsthome` from the now-null `Gst`) will overwrite it with NULL.
This is structurally **the identical defect** as `docs/wf1-owed-review-findings.md` lens 1 finding 1: *"the two
source-order columns are `NOT NULL` on `companies` but live on `GstConfig`, which the store builds only when GST
is enabled; the delete-and-reinsert then fabricates … over the migration's `UPDATE`."* Same root cause, same
table, one column across.

**Consequence for the design:** the inheritance must NOT be a creation-time stamp onto `GstConfig`. It must be a
**seed-on-demand at the moment the GST screen is opened / GST is enabled** — which is, independently, *exactly
what the corpus describes*: TallyPrime's GST Details State *"by default **shows** the State name as selected in
the Company Creation screen"* (Book PDF p.177, grounding §4.1). The code constraint and the corpus converge on
the same mechanic. §3 builds it that way.

### 1.10 ✅ HOLDS — the schema numbers, re-read rather than quoted

`src/Apex.Persistence.Sqlite/Schema.cs line 159` `public const int CurrentVersion = 51;` (doc comment at `:119`
agrees). `plan.md line 1495` carries the binding allocation *"Schema (v50 → v53) — binding allocation … WF-1 = v51,
WF-2 = v52, WF-3 = v53"*, and `plan.md line 1841-1846` records the 2026-08-16 correction: **nothing is reserved
beyond v53**; whoever needs v54 takes it **and amends the allocation line in the same commit**; expected — not
binding — outcome `W0-2b = v54`. §2 decides whether W0-2b needs a number at all.

### 1.11 ✅ HOLDS — the bridge from a postal State name to a GST state code already exists

`src/Apex.Ledger/Domain/IndianState.cs line 107-109` `FromName(string? name)` — case-insensitive
(`StringComparison.OrdinalIgnoreCase`) lookup over `All` (`:52-89`: codes 01–38 plus 97 "Other Territory";
**neither 96 nor 99**, deliberately — `:35-49`). `FromCode` at `:103-104`, `IsValidCode` at `:100`. So the
INHERIT mapping `Company.State` → `GstConfig.HomeStateCode` is a one-liner **provided** the postal State is
constrained to that same list — which the corpus independently requires (grounding §2 field 6: *"State (from a
list)"*).

### 1.12 ✅ HOLDS — the GST screen already has a second, competing seed for the same field

`src/Apex.Desktop/ViewModels/GstConfigViewModel.cs line 607-616` `OnGstinChanged` auto-fills `HomeState` from the
GSTIN's leading two digits. `Load` seeds it from the stored config (`:562-567`), and Apply writes it back at
`:1606` (`config.HomeStateCode = HomeState.Code;`) after refusing to enable when `HomeState is null`
(`:1586-1591`) or the GSTIN is absent/invalid (`:1574-1585`). **The design must state the precedence between the
GSTIN-derived fill and the new postal-State seed** — §3 does.

### 1.13 ✅ HOLDS — `CompanyFactory` already accepts the dates the UI never passes

`src/Apex.Ledger/Services/CompanyFactory.cs line 17-20` `CreateSeeded(string name, DateOnly? financialYearStart = null,
DateOnly? booksBeginFrom = null)`; `:22` `var fyStart = financialYearStart ?? new DateOnly(DateTime.Today.Year, 4, 1);`
`:23` `var books = booksBeginFrom ?? fyStart;`. **The T1-6 defect is confirmed and is a real wrong-figure bug:**
the default is 1-Apr of the **current calendar year**, so a company created in January–March is stamped an
FY-start three months in the **future** relative to the live Indian FY.

### 1.14 ⚠️ A claim in the brief that needs narrowing

The brief says *"W0-2a already shipped the PRINT half (the supplier postal block renders once populated)"*. True,
**with the guard**: `SupplierPostalAddressText` (`:742-745`) keys the whole block off a non-blank `Address`. So
a company where the user types **State and PIN but no Address** prints **nothing at all** — not even the PIN.
That is correct ER-13 behaviour and must not be "fixed" here, but it is a behaviour the new screen makes newly
reachable, and §6 tests it.

---

## 2. THE SCHEMA QUESTION — decision: **W0-2b ships ZERO schema change and takes NO version number**

`plan.md`'s W0-2b row instructs: *"**check first whether W0-2b needs a migration at all** — the row's own
premise is that the 11 profile fields already exist in the schema."* Checked. It does not.

### 2.1 The 11 profile fields: no migration, confirmed by opening the DDL

§1.5 and §1.6 above. All fourteen `companies` columns behind the eleven profile fields are in the **v1 create
DDL** (`Schema.cs line 179-194`), and Save is delete-and-reinsert (`SqliteCompanyStore.cs line 4602` → `:4628`), so an
Alter path is `company.X = …; _storage.Save(company);` and nothing else. Read path `:1306`, mapped `:1318-1341`.

### 2.2 The INHERIT ruling needs no column either

Both sides already exist and both already persist: `companies.state` (`Schema.cs line 184`, round-tripped at
`SqliteCompanyStore.cs line 4693` / `:1323`) and `companies.gst_home_state` (`Schema.cs line 201`, `:4706` / `:1350`).
RULING 3 is explicitly **additive — "BOTH COLUMNS ARE KEPT. NO DESTRUCTIVE MIGRATION"** (`plan.md line 1924-1925`).
The consistency guard is a **warning rendered at edit time**, not a persisted acknowledgement, so it stores
nothing. Deliberately: a persisted "user has acknowledged the divergence" flag would be a new column, a new
migration, a new downgrade inverse and a new canonical attribute, to record a fact the two existing columns
already imply by simply differing.

### 2.3 The five Tally contact fields — **OUT of this slice**, with reasons

Telephone No · E-Mail · Mobile No · Fax No · Website. **Verified absent at `3a4fcdb`:**
`grep -rni "telephone|website|\bfax\b|mobile" src/Apex.Ledger/Domain/Company.cs src/Apex.Persistence.Sqlite/Schema.cs`
returns **zero hits**. There is no domain member and no column. Four independent reasons to leave them out, in
descending weight:

**(a) 🔴 The corpus cannot tell us what order to put them in — so shipping them means inventing.**
This is the decisive reason, and it is an R7 reason rather than an effort one. Grounding §2.1 and §9 item 9
record that the two PRIMARY sources give **different** contact-block orders (Study Guide p.59 prose:
Telephone → E-Mail → Mobile → Fax → Website; Book p.13: Telephone → Mobile → Fax → E-Mail → Website) — **and the
Study Guide's own worked example at p.268 sides with the Book against the Study Guide's own prose.** One source
contradicting itself is not a tie to be broken by preference. A screen is an ordered artefact; shipping five
fields would force an order that the corpus does not support, into a slice whose whole justification is corpus
fidelity. The grounding doc's §9 list exists precisely to stop that.

**(b) They are FIDELITY, not COMPLIANCE.** CGST Rule 46(a), quoted first-party from CBIC in grounding §5.4, is
*"name, address and Goods and Services Tax Identification Number of the supplier"*. Grounding §5.5 states the
scoping fact outright: *"Pin Code, Telephone, Mobile, Fax, E-Mail and Website are Tally-fidelity fields, not
compliance fields. A design that trims scope should trim there."* This slice's reason for existing is T0-8 — a
**statutory breach on every printed invoice**. The five contact fields do not touch it.

**(c) The cost is nine surfaces, not one column list.** Adding them is: 5 domain members on `Company`; 5
`companies` columns; a `MigrateV53ToV54` (or whatever the number is by then) plus a matching
`SchemaDowngrade.V54ToV53` inverse; `CanonicalMapper.MapCompany` (`src/Apex.Ledger.Io/CanonicalMapper.cs line 59-80`,
which maps the profile fields one-for-one); `CanonicalXml.BuildRoot`'s `<company>` element
(`src/Apex.Ledger.Io/CanonicalXml.cs line 52-68`) **and** its reader; the JSON reader/writer pair; `ImportPlan`'s
`ApplyCompanyHeader` (`:1190-1210`); and the round-trip/equivalence tests for all of it. **And the downgrade leg
is currently known-broken:** `docs/wf1-owed-review-findings.md` lens 1 finding 2 measured that
`SchemaDowngrade.V51ToV50` *"is not the true inverse it is documented as"* — a populated round trip loses
"two indexes and 81 column contracts", `foreign_key_check` throws, and **"the store cannot save the result"**;
the same file's closing section records that this is **not fixed**. Adding a new downgrade leg on top of a leg
that is recorded as broken is work this slice should not be quietly absorbing.

**(d) Taking no version number leaves the v54 allocation untouched.** `plan.md line 1495-1512` ends the binding
allocation at **v53** and makes v54 a first-come claim that **"MUST amend this line in the same commit"**, with
`plan.md line 1506` naming the two claimants: W0-2b and WF-8's fallback closure flag. By shipping schema-clean,
W0-2b **does not consume v54 and does not have to amend the allocation line** — removing a coordination hazard
between two slices rather than exercising it. (Note that the allocation's *expected* outcome, "W0-2b = v54",
is explicitly "binding on nobody"; this design declines it, which the line permits.)

### 2.4 Where the five fields SHOULD go, so this is a deferral and not a drop

Recommendation for `plan.md`: a **W0-2c** row — "Company contact block (Telephone, E-Mail, Mobile, Fax,
Website)" — carrying **two** explicit preconditions: **(i)** an R7/A14 pass that either settles the field order
from the corpus or records it as UNVERIFIED-and-chosen with the choice argued (grounding §9 item 9 is the open
entry it must close or extend), and **(ii)** a schema version claimed from the allocation line at that time,
amending it in the same commit. Until then the screen simply does not show them, which is a visible absence
rather than a wrong order.

### 2.5 What this decision costs, stated honestly

The Alter screen will not be able to edit a company's phone number or e-mail — the very three things the Book
gives as Alter's *purpose* (grounding §3, Book p.15: companies *"will alter or edit their information when they
have changed company **address** or **contact number** or **email**"*). **One of those three — address — is the
statutory one and ships here; the other two do not.** That is a real fidelity gap for a screen whose corpus
rationale names them, and it is recorded in §7 and §8 rather than glossed.

---

## 3. THE INHERIT MECHANICS — concretely, with named call sites

RULING 3 in four clauses (`plan.md line 1912-1925`): postal `Company.State` is the **source of truth**; the GST home
State **defaults from it at creation** and **stays editable**; a guard **warns** on divergence; **both columns
kept**. Below is how each clause becomes code, and where clause 2 has to be read carefully to avoid §1.9.

### 3.1 Where the postal State is captured

**A new shared view model, `CompanyProfileViewModel`** (`src/Apex.Desktop/ViewModels/CompanyProfileViewModel.cs`),
hosting the profile fields and used by **both** Create and Alter. One view model, two modes — because Create and
Alter differ in exactly two ways per the corpus (grounding §3: Alter adds only `Alt+D`; Create adds only
Directory and the Group-Company action), and duplicating eleven bound fields across two view models is how the
two screens start disagreeing.

**The State control is a picker, not a free-text box.** Corpus: grounding §2 field 6 — *"State (from a list)"*.
Item source: `IndianState.All` (`src/Apex.Ledger/Domain/IndianState.cs line 52-89`).

**The stored form is the State NAME, not the code.** Verified against every fixture that sets it:
`tests/Apex.Ledger.Io.Tests/CanonicalFixture.cs line 30` sets `company.State = "Maharashtra"`;
`tests/Apex.Ledger.Tests/CompanyImportRoundTripTests.cs line 495` the same;
`tests/Apex.Desktop.Tests/VoucherInvoicePrintViewModelTests.cs line 355` sets `c.State = "Kerala"`. Meanwhile
`GstConfig.HomeStateCode` is the 2-digit code — `"27"`, asserted at
`tests/Apex.Ledger.Io.Tests/CanonicalRoundTripTests.cs line 265`. Changing the postal storage form to a code would
rewrite the meaning of a column that canonical-imported books already populate; it is not on the table.

**🔴 ER-13 requirement — the picker must tolerate a value it does not recognise.** Canonical import assigns
`t.State = c.State` verbatim (`ImportPlan.cs line 1198`) with no list check, so `companies.state` can legitimately
hold a trailing-space value, an abbreviation, or a name from a source that predates the list. **When the loaded
`Company.State` is non-blank and `IndianState.FromName` returns null, the picker gains one extra, transient
entry carrying the stored text verbatim and preselected.** Accepting the screen without touching the control
writes the identical string back. **Under no circumstance may opening Alter and pressing accept blank or
"correct" a stored State** — that is a silent data change on a field the canonical round-trip asserts
(`CanonicalRoundTripTests.cs line 259`).

**No type-to-filter.** This picker is an ordinary `ComboBox`, like the ~199 others. KB-3 (prefix type-to-filter)
is **not built** — `plan.md line 672-676` records that S5 shipped type-to-**JUMP** and that *"No filtering
infrastructure exists anywhere in `src/`"*, and `plan.md line 712-715` puts KB-3 behind a measurement spike. **Do not
invent it here.** The picker inherits whatever the shared widget already does, and nothing more.

### 3.2 How the GST home State seeds from it — NOT at creation; at the GST screen

> 🔴 **This is the clause that has to be read against the store.** Taking *"defaults from it at creation"*
> literally — building a `GstConfig` in `CreateCompany()` and stamping `HomeStateCode` — **writes a value the
> next `Load` discards** (§1.9: the write at `SqliteCompanyStore.cs line 4706` is unconditional on `Enabled`; the
> read at `:1348` is gated on `gst_enabled = 1`). The seed would vanish on reload and then be overwritten with
> NULL by the following save. That is `docs/wf1-owed-review-findings.md` lens 1 finding 1 rebuilt one column
> across.

**The seed is a DISPLAY default at the moment the GST screen is populated** — which is precisely what the corpus
says: the GST Details State *"by default **shows** the State name as selected in the Company Creation screen"*
(`664311548-Tally-Prime-Book.pdf` PDF p.177, grounding §4.1). "Shows", not "stores".

**Exact site: `src/Apex.Desktop/ViewModels/GstConfigViewModel.cs`, immediately after `:563`.** Today `Load` runs
`Gstin = cfg?.Gstin ?? string.Empty;` (`:562`) — which fires `OnGstinChanged` (`:607-616`) and may set
`HomeState` from the GSTIN's leading two digits — then `:563`
`HomeState = HomeStates.FirstOrDefault(o => o.Code == cfg?.HomeStateCode);`, which **overwrites it, with null
when no config is stored**. The seed is one guarded statement appended after that line, in spirit:

    // INHERIT (the R12 ruling): with no GST State stored yet, default the picker to the company's postal
    // State, exactly as the corpus describes. A DISPLAY default only — nothing is persisted until the user
    // applies this screen, because a HomeStateCode written onto a GST-off company is dropped on reload (the
    // store reconstructs GstConfig only when gst_enabled = 1).
    HomeState ??= HomeStates.FirstOrDefault(o => o.Code == IndianState.FromName(_company.State)?.Code);

**Precedence ladder, highest first — every rung but the last is an explicit user act:**

| Rank | Source | Why it wins |
|---|---|---|
| 1 | A **stored** `GstConfig.HomeStateCode` | It is the company's recorded GST registration. |
| 2 | The **GSTIN the user just typed** (`OnGstinChanged`, `:607-616`) | The leading two digits **are** the registration State; a postal default must never override it. |
| 3 | The **postal `Company.State`** (new) | The corpus default — applies only when 1 and 2 are absent. |
| 4 | `null` → the screen refuses to enable (`:1586-1591`) | Unchanged. |

**What this buys structurally: W0-2b creates NO new writer of `gst_home_state`.** The single writer stays
`GstConfigViewModel.cs line 1606` `config.HomeStateCode = HomeState.Code;`, reached only through the existing
enable/apply path that already validates the GSTIN (`:1574-1585`) and refuses a null State (`:1586-1591`). The
§1.9 trap is avoided by construction rather than worked around.

### 3.3 What happens when a user later edits either

| Action | Effect on the other | Rationale |
|---|---|---|
| Postal State edited on **Alter**, GST **off** | none — there is no `GstConfig` to diverge from | The next GST enable seeds from the new value (3.2, rung 3). |
| Postal State edited on **Alter**, GST **on** | **`HomeStateCode` is NOT rewritten.** The guard warns. | Rewriting it silently would flip intra- vs inter-state on every subsequent invoice. `GstConfigViewModel.cs line 1596-1600` already says so in its own words: `HomeStateCode` *"decides intra- vs inter-state supply, i.e. CGST+SGST versus IGST on every invoice for the rest of the session"*. A postal field must not move a tax head. |
| Home State edited on the **GST screen**, away from the postal State | postal `Company.State` untouched | RULING 3 clause 2: the GST State *"stays EDITABLE for the rare genuine divergence — a registration in a State other than the postal one."* |
| Postal State **cleared** on Alter while GST on | `HomeStateCode` untouched; the guard falls silent (nothing to compare) | Clearing an advisory input must not destroy a statutory one. |

### 3.4 The consistency guard — what it says and where it appears

**Shape:** a computed, read-only advisory string on the view model, rendered as a normal text line whenever it is
non-empty. **A warning, never a refusal** — RULING 3 clause 3: *"A warning, not a refusal: divergence is legal,
silence about it is not."* It never gates Accept, never throws, and never mutates.

**Where — two places, because either screen can create the divergence:**

1. **`CompanyProfileViewModel`** (Alter and Create) — visible while the divergence exists, so it is present the
   moment an Alter screen opens on an already-divergent book, not only after an edit.
2. **`GstConfigViewModel`** — the same computation, so moving the Home State away from the postal State warns
   symmetrically. This half is what makes the guard honest: warning on only one screen would announce the
   divergence when it is created from the advisory side and stay silent when it is created from the statutory
   side.

**Computation** — one shared helper, so the two screens cannot drift (a small `CompanyStateConsistency` static
in `src/Apex.Desktop/Services`, beside the rule it serves):

    postal  = IndianState.FromName(company.State)            // null when unset OR unrecognised
    gstCode = company.Gst is { Enabled: true } ? company.Gst.HomeStateCode : null
    warn    = gstCode is not null && postal is not null && postal.Code != gstCode

**Three deliberate silences, each with a reason:**

- **GST off** → silent. There is no second State.
- **Postal State blank** → silent. Nothing was claimed.
- **Postal State non-blank but unrecognised by `IndianState.FromName`** → **its own, different message**, not
  the divergence one. Comparing an unresolvable name against a code and declaring "they differ" would report a
  divergence that may not exist. This case is reachable today from canonical import (§3.1).

**Wording.** Must satisfy the no-build-jargon rule in `tests/Apex.Desktop.Tests/XamlLayoutInvariantTests.cs`
(no "Phase N", no "slice N", no "W0-N") and the standing brand rule that the word "Tally" never appears in a
shipped user-facing string:

- Divergence — `Postal State 'Kerala' differs from the GST registration State 'Maharashtra (27)'. Printed invoices and tax calculation use the GST State.`
- Unrecognised postal State with GST on — `Postal State 'WB' is not a recognised State/UT, so it cannot be checked against the GST registration State 'West Bengal (19)'.`

Both name the actual values. A guard that says only "these differ" makes the user open two screens to find out
which is which.

### 3.5 An existing company where both are set and disagree

**Nothing is migrated, mutated or reconciled.** On opening Alter the guard renders immediately and the aggregate
is untouched; accepting without editing re-saves identical values (Save is a full rewrite of the same fields —
§1.6 — so the row comes back byte-identical). This is required by ER-13 and it is testable today, because **the
fixture already exists**: `tests/Apex.Desktop.Tests/VoucherInvoicePrintViewModelTests.cs line 355` builds exactly this
company — `c.State = "Kerala"` against a GST home State of 27 — and its own comment reads *"postal State (code
32) — deliberately NOT the GST home State (27)"*. The print-side assertion
`A_company_whose_postal_State_disagrees_with_its_GST_State_prints_the_GST_one` must stay green **unchanged**;
§6 adds the capture-side sibling rather than editing it.

### 3.6 The change list — every call site this slice touches

| # | File | Change |
|---|---|---|
| 1 | `src/Apex.Desktop/ViewModels/CompanyProfileViewModel.cs` | **NEW.** The bound fields, Create/Alter mode, validation, the guard string, Accept. |
| 2 | `src/Apex.Desktop/ViewModels/MainWindowViewModel.cs line 17` (`Screen` enum, members from `:19`) | Add `AlterCompany`. |
| 3 | `MainWindowViewModel.cs line 815-823` `ShowCreateCompany` | Build and expose a `CompanyProfileViewModel` in Create mode instead of clearing one string. |
| 4 | `MainWindowViewModel.cs line 827-839` `CreateCompany` | Read the view model; pass `financialYearStart` / `booksBeginFrom` into the **existing** `CompanyFactory.CreateSeeded` overload (`CompanyFactory.cs line 17-20`); assign the postal fields; `EnsureValid()` (§5); `_storage.Save`; `OpenCompany`. |
| 5 | `MainWindowViewModel.cs` — new `ShowAlterCompany()` | A page column via the established pattern `OpenPageColumn(new GatewayColumn(title, page), Screen.AlterCompany, title, () => AlterCompany = page)` — the shape `ShowGstConfig` uses at `:3641-3649`. |
| 6 | `MainWindowViewModel.cs line 912-978` `BuildRootColumn` | One new section header + one item (§4.4). |
| 7 | `src/Apex.Desktop/Views/MainWindow.axaml line 228-244` | Replace the one-field create form; add the Alter page column. |
| 8 | `src/Apex.Desktop/ViewModels/GstConfigViewModel.cs`, after `:563` | The seed (§3.2). |
| 9 | `GstConfigViewModel.cs` (guard surface) | The shared divergence advisory (§3.4). |
| 10 | `src/Apex.Ledger/Domain/Company.cs line 79-84` | Correct the stale "open R12 user gate" doc comment (§1.8). |
| 11 | `src/Apex.Desktop/Services/VoucherPrintProjector.cs line 698-707` | The same correction; **keep** the rest of that paragraph verbatim (§1.8). |

**No change to:** `Schema.cs`, `SchemaDowngrade.cs`, `SqliteCompanyStore.cs`, `CanonicalMapper.cs`,
`CanonicalXml.cs`, `ImportPlan.cs`, `InvoicePdf.cs`, or the body of `VoucherPrintProjector.SellerBlock`.

---

## 4. THE SCREEN

### 4.1 Fields, in corpus screen order, with the corpus's own labels

Order and section headings are the Study Guide's (grounding §2, p.57-60, headings literal), corroborated by the
Book p.13. **Shipped in this slice = the 11 that already have a domain member and a column** (§1.5). Everything
else is listed so the omissions are visible rather than silent.

| # | Corpus section | Corpus label | Our member | In this slice? |
|---|---|---|---|---|
| 1 | *(ungrouped)* | Directory | — | **NO — architectural.** One `.db` per company, path chosen by `CompanyStorage.PathForName` (`src/Apex.Desktop/Services/CompanyStorage.cs line 63-64`). Grounding §7.4 calls this "a deliberate architectural difference, not a gap". |
| 2 | *(ungrouped)* | **Name** | `Name` | **YES on Create. DISPLAY-ONLY on Alter** — see 4.3. |
| 3 | Primary Mailing Details | **Mailing Name** | `MailingName` | **YES.** The Rule 46(a) supplier *name* (grounding §5.5; Book p.13: *"Type Company's Short name here for Show in Invoice/Bill"*). |
| 4 | Primary Mailing Details | **Address** | `Address` | **YES.** Multi-line free text. The Rule 46(a) supplier *address*. |
| 5 | Primary Mailing Details | **State** | `State` | **YES.** Picker over `IndianState.All` (§3.1). |
| 6 | Primary Mailing Details | **Country** | `Country` | **YES.** Book's label; the Study Guide calls the same field "Statutory Compliance for". |
| 7 | Primary Mailing Details | **Pin Code** | `Pin` | **YES.** Validated (§5). |
| 8-12 | Contact Details | Telephone No · E-Mail · Mobile No · Fax no · Website | — | **NO — §2.3.** |
| 13 | Books and Financial Year Details | **Financial year begins from** | `FinancialYearStart` | **YES on Create** (fixes T1-6, §1.13). Alter: see 4.3. |
| 14 | Books and Financial Year Details | **Books beginning from** | `BooksBeginFrom` | **YES on Create.** Alter: see 4.3. |
| 15-16 | Security Control | Vault Password · User Access Control | — | **NO.** No domain member; security is in the excluded phase. |
| 17 | Base Currency Information | **Base Currency Symbol** | `BaseCurrencySymbol` | **YES.** |
| 18 | Base Currency Information | **Formal Name** | `BaseCurrencyName` | **YES.** |
| 19-21 | Base Currency Information | Suffix Symbol to Amount · Add space between amount and symbol · Show amount in Millions | — | **NO.** No domain member; and grounding §9 item 6 records that their *defaults are UNVERIFIED* — the Book shows the author's walkthrough values, not documented defaults. Shipping them means inventing defaults. |
| 22 | Base Currency Information | **Number of decimal Places** | `DecimalPlaces` | **YES.** |
| 23 | Base Currency Information | **Word representing amount after decimal** | `DecimalUnitName` | **YES.** |
| 24 | Base Currency Information | No of decimal places for amount in words | — | **NO.** No domain member. |

**Sections rendered:** *Primary Mailing Details*, *Books and Financial Year Details*, *Base Currency
Information* — the corpus's own headings, with the empty ones (Contact Details, Security Control) simply not
drawn. Nesting under headings is also the standing UI convention for this project.

#### 4.1.1 🔴 The State/Country order conflict — RESOLVED, and the reasoning recorded

Grounding §2.1 item 2 records that the two primaries invert this pair: **Book p.13** gives Address → **State** →
**Country** → Pin Code; **Study Guide p.58-59 prose** gives Address → **Country** → **State** → Pin Code.
**Chosen: the Book's order (State before Country)**, for a reason that is evidence and not preference — the
**Study Guide's own worked example at p.268 sides with the Book against the Study Guide's own prose**
(grounding §5.1 reproduces it: `Address:` / `State: West Bengal` / `Country: India` / `Pincode: 700039`). Two
concrete renderings agree; one piece of prose dissents. This is the *identical* self-contradiction pattern the
grounding records for the contact block (§2.1 item 1), and it resolves the same way.
**Consequence for labels:** we take the Book's label **"Country"** rather than the Study Guide's *"Statutory
Compliance for"*, so the order and the label come from the same source instead of being mixed.
**This does not touch the PRINT order.** Grounding §9 item 11 records that we *print* Address → Country → PIN →
State, and that changing it would move the shipped recipient block too. **Capture order and print order are now
deliberately different, and §8 records that as an open item rather than pretending it is resolved.**

### 4.2 Create-only

- **Directory** — not applicable (row 1 above).
- **`Alt+R` → Group Company** (grounding §3, SG p.267 / Book p.14, whose creation screen adds *Member
  Companies*, SG p.268) — **OUT.** No domain support for a group company exists; this is a feature, not a field.

### 4.3 Alter-only, and the two fields Alter must NOT edit

**`Alt+D` Delete (corpus: Book p.15, `Alt+K > Alter > Alt+D > Enter twice`) — OUT of this slice.**
`CompanyStorage.Delete(CompanyEntry)` exists (`CompanyStorage.cs line 100-108`) and is best-effort ("a locked file is
left in place"), but the Alter screen operates on the **currently open** company, whose `.db` the app is holding;
deleting it needs a shut-down-then-delete sequence and a destructive-confirmation design of its own. Deferring is
also consistent with how the project already treats destructive actions — the Restore panel *"refuses it unless
the archive has been examined AND the confirmation is ticked (NFR-8)"* (`MainWindow.axaml.cs line 233-236`).
Recorded, not dropped.

> ### 🔴🔴 4.3.1 `Name` MUST be display-only on Alter — a book-eater otherwise
>
> **The `.db` file path is derived from the company NAME.** `CompanyStorage.Save(Company)`
> (`src/Apex.Desktop/Services/CompanyStorage.cs line 70-75`) computes `var path = PathForName(company.Name);` and
> `PathForName` (`:63-64`) is `Path.Combine(CompaniesDirectory, SanitiseFileName(companyName) + ".db")`.
> `ListCompanies` (`:48-59`) then enumerates `*.db` and **takes the display name from the FILENAME**
> (`Path.GetFileNameWithoutExtension`).
>
> **So renaming a company on an Alter screen and saving would write a brand-new `.db` at the new name and leave
> the old file untouched** — two entries in Company Select, the same `Company.Id` in both, and every subsequent
> save landing on only one of them. Nothing in the store would report an error.
>
> **Decision: `Name` renders as read-only text on Alter.** Rename is a storage operation (move the file, refuse
> a collision against `Exists` at `:67`, handle the open handle) and belongs in its own slice.
> **This costs nothing statutory:** Rule 46(a)'s supplier *name* maps to **Mailing Name**, not `Name`
> (grounding §5.5), and Mailing Name **is** editable here. Tally itself says Mailing Name is the name that
> prints on the invoice.

> ### ⚠️ 4.3.2 `Financial year begins from` and `Books beginning from` on Alter — editable, with a warning
>
> Grounding §9 item 2 is explicit: **no corpus source states that any company field becomes read-only after
> creation**, and grounding §3 says the corpus documents **no** Alter-only field and **no** locking. So making
> them read-only would be inventing a restriction, and making them silently editable on a book with posted
> vouchers is a wrong-figures hazard (every period report is keyed off these dates).
> **Chosen middle: editable, with an advisory shown whenever the company already has vouchers** — the same
> warn-don't-refuse shape as the State guard, and for the same reason. Wording, e.g.
> `This book already has vouchers. Changing these dates changes which period every report covers.`
> **The alternative — locking them — must not be chosen silently**, because the corpus does not support it;
> §8 records the question rather than closing it.

### 4.4 Where it hangs off navigation

**Create — unchanged.** It stays the `Create Company` row on the Company-Select menu (`MainWindowViewModel.cs line 808`,
hint `"F3"`), because Alter needs an open company and Create must work with none.

**Alter — a new cascade page column under a new `Company` section.** In `BuildRootColumn`
(`MainWindowViewModel.cs line 912-978`), a `MenuItemViewModel.Header("Company")` **placed first, above `Masters`**,
carrying one item: `new MenuItemViewModel("Alter Company", () => { }, "", isSubItem: true, kind: MenuItemKind.Page)`.
Opened through the established page-column pattern (the shape `ShowGstConfig` uses, `:3641-3649`):
`OpenPageColumn(new GatewayColumn("Company Alteration", page), Screen.AlterCompany, "Company Alteration", () => AlterCompany = page)`.
This satisfies the Miller-column convention (prior panes persist; Esc pops) and the standing rule that every
screen nests under a parent section rather than sitting in a flat dump.

> 🔴 **The corpus route cannot be reproduced, and this is a recorded deviation, not an oversight.** The corpus
> route is `Gateway of Tally > Alt+K (Company menu) > Alter` (grounding §3/§6, Book p.15, SG p.61/p.267).
> **`Alt+K` is already bound in this app** — to the Saved Views list (RQ-8), `MainWindow.axaml.cs line 651-653`,
> scoped to `vm.IsReportContext`. Re-using the chord on a different context is exactly the first-match-wins
> arbitration hazard that Phase 10.6's **KB-4** exists to clean up, and this slice must not open that.
> **Therefore: no accelerator hint.** The item is reached by arrow + Enter, like `Chart of Accounts`, which
> also ships with an empty hint (`:919`). **Inventing a different chord would be worse** — it would be an
> unsourced keystroke presented as fidelity. Extended into §8.

### 4.5 Accept and Escape

**Accept — both corpus forms work, using shipped machinery.** Grounding §6 records that the corpus gives **both**
`Ctrl+A` (Book p.14; SG p.268 step 5) and Enter-then-Enter (SG p.60) and *"does not reconcile them"*. The app
already implements exactly that two-form shape as **WI-11**:

- **`Ctrl+A` → saves outright.** Existing arm, `MainWindow.axaml.cs line 211-256`, which falls through to
  `vm.ActivateSelected()` (`:254`). `ActivateSelected` (`MainWindowViewModel.cs line 5828`) **already has**
  `case Screen.CreateCompany: CreateCompany(); return;` (`:5835-5837`); add
  `case Screen.AlterCompany: AlterCompany?.Accept(); return;` beside it.
- **`Enter` → `Accept Company? (Y/N)` → `Y`.** Add `Screen.CreateCompany` and `Screen.AlterCompany` to
  `IsMasterAcceptScreen` (`MainWindowViewModel.cs line 4881-4890`) and `"Company"` to `MasterAcceptNoun`
  (`:4968-4995`). `ConfirmMasterAccept` (`:4914-4924`) then routes through **the same `ActivateSelected`**, so
  the prompt can never drift from the shortcut — the property that method's own comment exists to protect.
  ⚠️ **This CHANGES today's behaviour on the Create screen** (Enter currently creates immediately, via the
  `case Key.Enter when !IsPickerOpen(e)` arm at `MainWindow.axaml.cs line 900-903`). Named in §7. If a headless test
  turns out to depend on Enter-creates-immediately and the team prefers not to move it, the fallback is to add
  **only** `Screen.AlterCompany` — recorded here so the choice is explicit rather than discovered.
- **A picker being open must not raise the prompt.** Already handled: both Enter arms carry `!IsPickerOpen(e)`
  (`:893`, `:900`), which the comment at `:891-899` calls the D1 fix. The new State/Country pickers get that for
  free — **do not add a new Enter arm.**

**Escape — `Back()`, i.e. pop the column; two presses when a picker is open.** Existing arm,
`MainWindow.axaml.cs line 931-934`. With a dropdown open the first Escape closes the dropdown (the `ComboBox` does it
once the arm yields) and the second pops — the settled contract, stated at `:925-930`.
🔴 **This is OUR convention, not fidelity.** Grounding §9 item 5: **Escape behaviour on Company Creation is
UNVERIFIED** — the Book's p.435 table does not list `Esc` in the company region, and the only source that does is
the **rejected** Short-Key PDF (grounding §1.1). Consistency with the other ~157 form columns is the entire
justification, and it must be written down as such.

**F11-after-save is OUT.** Grounding §2.3 (SG p.60) records that TallyPrime opens the Company Features screen
after a save. Our `CreateCompany` ends in `OpenCompany(company)` (`:838`) → the Gateway. Changing that would
alter the landing screen for 153 test-suite bootstraps (§7). Recorded as a fidelity gap, deferred.

---

## 5. THE ENSUREVALID WIRING

`plan.md line 1795-1800` states the obligation in W0-2a's own row: *"`Company.EnsureValid()` has exactly ONE call
site in `src/`, and it is the canonical import. Nothing calls it on save … **The day W0-2b's screen ships, that
stops being true**, so W0-2b must call `Company.EnsureValid()` on its save path (or the store must), and it must
ship the test that proves a bad PIN typed into the screen is refused."* Re-verified at `3a4fcdb` in §1.1.

### 5.1 Where it goes — **`CompanyStorage.Save`**, and the reason is measured

**`src/Apex.Desktop/Services/CompanyStorage.cs line 70-75`**, as the first statement of `Save(Company company)`,
before `PathForName` is computed:

    public void Save(Company company)
    {
        // Every UI write path in this application funnels through here (measured: 98 `_storage.Save(...)` call
        // sites across src/Apex.Desktop, and this class is the ONLY constructor of SqliteCompanyStore in the
        // Desktop layer). The engine's own guard therefore runs on all of them, including screens not yet
        // written — the canonical importer has had this since W0-2a; the UI had nothing.
        company.EnsureValid();
        var path = PathForName(company.Name);
        …
    }

**Why this seam and not the view model.** Measured at `3a4fcdb`:

- `grep -rn "_storage\.Save(\|storage\.Save(" src/Apex.Desktop --include=*.cs` → **98 call sites**.
- `grep -rn "new SqliteCompanyStore" src/Apex.Desktop --include=*.cs` → **9 hits, ALL of them inside
  `CompanyStorage.cs`** (`:73`, `:80`, `:120`, `:127`, `:134`, `:141`, `:154`, `:161`, `:168`). No view model
  constructs a store.
- Every view model holds `CompanyStorage _storage` (e.g. `AccountGroupMasterViewModel.cs line 40`), not the SQLite
  type.

So `CompanyStorage.Save` is **the** Desktop write choke point. One call there covers all 98 paths and every
future screen. Putting the guard in `CompanyProfileViewModel.Accept()` instead would cover exactly one screen and
reproduce, one level up, the very defect the review recorded for `MasterGstDetails.EnsureValid` —
*"reachable on exactly one of five write paths"* (`docs/wf1-owed-review-findings.md`, lens 2 finding 4, still
listed under **"What this review did NOT close"**). **W0-2b must not add the sixth.**

### 5.2 Why NOT `SqliteCompanyStore.Save`

It is one layer deeper and would also cover the engine and the test fixtures — a wider blast radius than this
slice has evidence for, on a class whose comments record that *"`SqliteCompanyStore` contains no catch blocks of
its own"* (`src/Apex.Desktop/ViewModels/SaveFailure.cs line 31-33`). If a later slice wants the floor there too, that
is a deliberate engine change with its own regression run. **Named as the deliberate stopping point** rather than
left as an unexplained choice.

### 5.3 The friendly message in front of it — the established two-layer pattern

`EnsureValid` throws `ArgumentException` (`Company.cs line 99-100`), and `SaveFailure.IsReportable`
(`src/Apex.Desktop/ViewModels/SaveFailure.cs line 44-51`) already lists `ArgumentException`, so a throw from inside
`Save` is **reported, not a crash**, on every screen that wraps its save in the shared predicate. That is the
backstop, not the user experience.

The screen pre-validates and shows a message, exactly as the stock-item master does — its comment at
`src/Apex.Desktop/ViewModels/StockItemMasterViewModel.cs line 488` names the pattern ("friendly message before the
engine's EnsureValid backstop") and `:611` is the backstop call itself, commented `// backstop; already
pre-validated above`. So:

1. **`CompanyProfileViewModel.Accept()`** checks `IndianPinCode.IsValidOrBlank(Pin)` and, on failure, sets a
   message such as `PIN code must be 6 digits.` and **returns without saving** — no exception, no aggregate
   mutation, the operator's typing intact.
2. **`CompanyStorage.Save` → `company.EnsureValid()`** is the floor that catches any path that skipped step 1.

**Do not delete step 1 in favour of step 2 alone.** An `ArgumentException` surfaced through a save-failure
message reads as an internal error and, worse, arrives *after* the aggregate has already been mutated.

### 5.4 A trap in the ordering — mutate-then-save, and rollback

Every master screen in this app mutates the shared `Company` aggregate **and then** persists it, which is why
`GstConfigViewModel.TrySave` (`:1917-1931`) takes a `restore` action and why its comment block at `:1953-1958`
exists at all. `CompanyProfileViewModel.Accept()` must follow the same shape: **capture the previous field
values, assign, save, and restore them on any failure.** Otherwise a save that fails for an operational reason
(SQLITE_BUSY from a second instance, a read-only file) leaves the in-memory company holding values the book on
disk does not have — the "wrong-figures divergence" that comment names, on the postal block instead of the GST
one. Reuse `SaveFailure.IsReportable`; do not write a fourth private copy of the list.

### 5.5 How the wiring is PROVED to fire — three levels, mutation at the top

**(1) A behavioural test that goes red today** — the RED-PROOF, §6.1.

**(2) A mutation proof, run and recorded in the commit body.** Delete `company.EnsureValid();` from
`CompanyStorage.Save`, restore the file byte-identical afterwards, and record the exact count and names of the
tests that went red. This is the standing method in this project: `plan.md line 1808-1821` records W0-2a's three
mutations and their measured red counts (`1 red`, `3 red`, `1 red`) with test names, and records that **all
three had been measured DEAD before the fix**. A guard nobody mutated is a guard nobody has proved.
**Required outcome: at least one named test red, and it must be the PIN test, not an incidental string
assertion** — lens 2 finding 1 of the owed review is precisely the case where *"the only red was a hardcoded
string literal in a third test"*.

**(3) A reach assertion, so the choke point itself is pinned.** A test that fails if any Desktop code
constructs a `SqliteCompanyStore` outside `CompanyStorage` — i.e. that the choke point is still a choke point.
Cheap (a source scan over `src/Apex.Desktop`, the same shape `XamlLayoutInvariantTests` already uses to scan
`.axaml`), and it is what stops the guard being silently bypassed by a future screen that opens its own store.
**Without (3), (1) and (2) only prove the guard works on the path that exists today.**

---

## 6. TESTS

Standing rule for this slice, taken from `docs/wf1-owed-review-findings.md` lens 2: **a test that cannot fail is
not coverage.** Lens 2 finding 1 is the case where *"performing exactly the simplification its doc comment
forbids left both named guardian tests green"*, and finding 5 is a test that *"asserts the implementation against
itself: its only failure mode is disagreement, never the rule."* Every test below therefore names **the mutation
that must redden it**, and those mutations are to be **run and recorded in the commit body**, the way
`plan.md line 1808-1821` records W0-2a's three.

### 6.1 THE RED-PROOF

**RP-1 (behavioural — the real one).**
`A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block`, in
`tests/Apex.Desktop.Tests/`.
**Asserts:** drive the *only* company-creation path with a full postal block typed in; print a sales invoice;
assert `invoice.Seller.Name` is the Mailing Name, `invoice.Seller.AddressLines` contains the typed address lines,
and `invoice.Seller.Gstin` is the GSTIN — the three particulars of CGST Rule 46(a) (*"name, address and Goods and
Services Tax Identification Number of the supplier"*, grounding §5.4, first-party CBIC text). Then assert the
same strings are present in the rendered PDF bytes, not merely in the projection.
**Why it fails on the current tree:** it does not compile. `CompanyProfileViewModel` does not exist and
`MainWindowViewModel` exposes no postal property — §1.3/§1.4 measured that `Company.State`, `Address` and
`MailingName` have **no assignment site in `src/Apex.Desktop`** and that the create form is one `TextBox`
(`MainWindow.axaml line 236`). **When the defect is "there is no way to type it", the honest red is "there is nothing
to call".** That is stated plainly here rather than dressed up as an assertion failure.

**RP-2 (structural — observably red on the UNMODIFIED tree, no new production code needed).**
`The_company_postal_block_has_at_least_one_assignment_site_in_the_desktop_layer`.
**Asserts:** a source scan over `src/Apex.Desktop/**/*.cs` finds at least one assignment to each of
`.Address =`, `.State =`, `.Pin =`, `.MailingName =` **on a `Company`** (the existing `MailingName` hits at
`LedgerMasterViewModel.cs line 582, 803, 965, 988, 1152` are the **party** mailing block and must be excluded — the
grounding doc §7.2 records that exact confusion).
**Why it fails today:** the count is **zero** for `Address`, `State` and `Pin`. This is the census claim T0-8
rests on, expressed as a test. It compiles and runs red against `3a4fcdb` unchanged, which is what makes the
red-proof demonstrable before a single line of production code exists.
It is deliberately paired with RP-1 and **is not a substitute for it** — a structural test proves reachability,
never behaviour.

### 6.2 The inherit seeding

**T-1 `A_new_company_with_a_postal_State_seeds_the_GST_Home_State_when_GST_is_first_enabled`.**
Create through the screen with `State = "Kerala"`; open the GST screen; assert the Home State picker is
preselected to Kerala (`"32"`) **before the user touches anything**.
*Mutation that must redden it:* delete the seeding statement after `GstConfigViewModel.cs line 563`.

**T-2 `A_stored_GST_Home_State_is_never_overwritten_by_the_postal_State`.** A company with `HomeStateCode = "27"`
and postal `State = "Kerala"`; open the GST screen; assert the picker shows Maharashtra (27).
*Mutation:* change `??=` to `=` in the seed. This is the test that stops the seed becoming a silent overwrite of
a statutory value — the wrong-tax-head class.

**T-3 `A_typed_GSTIN_still_wins_over_the_postal_State_seed`.** Postal `State = "Kerala"`; no stored config; type a
Maharashtra GSTIN into the GST screen; assert the Home State moves to 27, not back to 32.
*Mutation:* place the seed **after** `OnGstinChanged`'s assignment instead of before it (i.e. let the postal
default clobber the GSTIN-derived code). Pins precedence rung 2 over rung 3 (§3.2).

**T-4 🔴 `A_GST_home_State_is_never_written_onto_a_GST_off_company`.** The §1.9 regression guard. Create through
the screen with a postal State; **save; reload from disk**; assert `company.Gst` is `null` and that no attempt was
made to persist a home state.
*Mutation:* implement the seed as a creation-time stamp (`company.Gst = new GstConfig { HomeStateCode = … }` in
`CreateCompany()`); the reload assertion must go red. **This is the single most valuable test in the slice**,
because the naive reading of the ruling produces exactly that bug and the store swallows it silently
(`SqliteCompanyStore.cs line 4706` writes it, `:1348` drops it).

### 6.3 The divergence guard

**T-5 `A_postal_State_that_disagrees_with_the_GST_State_raises_a_warning_and_still_saves`.** Load the divergent
fixture; assert the advisory text is non-empty **and names both values**; accept; assert the save succeeded and
**both columns still hold their original values**.
*Mutation:* make the guard a refusal (return before save). Must redden — RULING 3 clause 3 is *"A warning, not a
refusal."*

**T-6 `Matching_States_raise_no_warning` / `A_GST_off_company_raises_no_warning` / `A_blank_postal_State_raises_no_warning`.**
The three silences of §3.4.
*Mutation:* drop the `gstCode is not null` term — the GST-off case must redden. A guard that fires on every
GST-off company would be noise on every book on disk today.

**T-7 `An_unrecognised_postal_State_gets_its_own_message_not_the_divergence_one`.** Postal `State = "WB"`, GST on
with `"19"`; assert the advisory is the *unrecognised* wording, not the *differs* wording.
*Mutation:* fold the unrecognised case into the divergence branch. Must redden.

**T-8 `The_warning_is_symmetric_across_both_screens`.** The same divergent company produces the same advisory from
`CompanyProfileViewModel` and from `GstConfigViewModel`.
⚠️ **Not "the two agree with each other" — that is lens 2 finding 5's failure mode** (*"asserts the
implementation against itself"*). Assert each against the **expected literal string**, so relaxing the rule in
both places still reddens both.

### 6.4 PIN and postal validation on save

**T-9 `A_bad_PIN_typed_into_the_screen_is_refused_with_a_message_and_nothing_is_saved`.** Type `"70003"`
(five digits); accept; assert a friendly message, **no exception**, and that reloading from disk shows the PIN
unchanged.
*Mutation:* delete the view-model pre-check (§5.3 step 1). Must redden — and must redden with a *message*
assertion, not a crash.

**T-10 `The_engine_guard_still_refuses_a_bad_PIN_when_the_screen_check_is_bypassed`.** Set `company.Pin = "abcdef"`
directly on the aggregate, then call `CompanyStorage.Save`; assert `ArgumentException` naming the **company**
(`Company.cs line 99-100` words it *"Company PIN code '…' is not a valid 6-digit Indian PIN code."*, deliberately
distinct from the party message — `Company.cs line 93-96`).
*Mutation:* **delete `company.EnsureValid();` from `CompanyStorage.Save`.** This is the §5.5(2) mutation and its
red count goes in the commit body.

**T-11 `Every_desktop_save_path_goes_through_the_one_guarded_store_opener`.** The §5.5(3) reach assertion — no
`new SqliteCompanyStore` outside `src/Apex.Desktop/Services/CompanyStorage.cs`.
*Mutation:* add a `new SqliteCompanyStore(...)` in any view model. Must redden.

**Odd-value PIN fixtures — chosen against the actual rule** (`src/Apex.Ledger/Domain/IndianPinCode.cs line 16-29`:
exactly six ASCII digits, first digit 1–9, blank allowed):

| Value | Expected | Why this one |
|---|---|---|
| `"700039"` | accept | The corpus's own worked value (SG p.268, grounding §5.1) — the fixture is sourced, not invented. |
| `"070039"` | **reject** | Leading zero. The rule rejects it explicitly; a naive "six digits" check accepts it. |
| `"70003"` | reject | Five digits. |
| `"7000399"` | reject | Seven digits. |
| `"abcdef"` | reject | The exact value `IndianPinCode`'s own doc comment names as the reason it exists. |
| `" 700039 "` | accept | The rule `Trim()`s. Pins that the trim is real. |
| `"७००० ३९"` (Devanagari digits) | **reject** | `char.IsAsciiDigit`, not `char.IsDigit`. Nothing in the repository walks this today; without it the ASCII half of the rule is unprovable. |
| `null` / `""` | accept | Unset is not invalid. |

### 6.5 End-to-end: Rule 46(a) through the screen

**T-12 `A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block`** — RP-1, promoted to the
permanent suite. **Odd-value fixtures, deliberately awkward and each for a reason:**

- **Mailing Name ≠ Name.** `Name = "Bright Traders Private Limited"`, `MailingName = "Bright Traders"`. Asserts
  the printed supplier name is the **Mailing Name** (grounding §5.5 maps 46(a)'s supplier *name* to that field,
  and `CompanyDisplayName` at `VoucherPrintProjector.cs line 676-677` implements the fallback). A fixture where the
  two are equal cannot tell the two apart — that is exactly the enum-zero blindness of lens 2 findings 2 and 3.
- **A three-line address with a blank middle line**, taken from the corpus's own example (grounding §5.1):
  `"13A, Picnic Garden Road\n\n3rd Lane\nKolkata"`. Asserts `SplitAddress`
  (`VoucherPrintProjector.cs line 855-866`) drops the empty entry and that **three** lines print, not four.
- **An address line containing a comma** — `"13A, Picnic Garden Road"`. Pins the newline-only split that
  `SplitAddress`'s own comment insists on (*"'Pune, Maharashtra 411001' is one address line, not two"*,
  `:850-852`).
- **A non-ASCII character in the address** (e.g. `"Bengalūru"`). The printer runs everything through
  `ReportPrintProjector.Ascii`; this pins that the screen does not bypass it.
- **Postal State `"Kerala"` with a Maharashtra GSTIN and home State 27** — the divergence, so the E2E case and
  the guard case are the same company. Asserts the printed State is **Maharashtra (27)**, re-confirming
  `A_company_whose_postal_State_disagrees_with_its_GST_State_prints_the_GST_one` from the capture side.

**T-13 `A_company_created_through_the_screen_with_no_address_still_prints_nothing`.** ER-13 through the new
screen: create, type **nothing** in the postal block, print, assert `Seller.AddressLines` is empty and the PDF
contains neither `"PIN:"` nor `"India"`. This is the same assertion
`A_freshly_created_company_prints_no_supplier_address_lines_at_all`
(`tests/Apex.Desktop.Tests/VoucherInvoicePrintViewModelTests.cs line 472-499`) makes today, restated against the new
screen — see §7.2 for why the existing one must stay green **unchanged**.

### 6.6 Round-trip and persistence

**T-14 `A_postal_block_typed_into_the_screen_survives_save_reload_and_a_canonical_round_trip`.** Type the block,
save, reload from the `.db`, assert every field; then export canonical XML and re-import, assert again. The
canonical side already has `CanonicalCompanyPostalRoundTripTests` (`tests/Apex.Ledger.Io.Tests/`, which sets
`c.State = PostalState` at `:37`); this is the **UI-origin** half of it, which does not exist.

**T-15 🔴 `Opening_Alter_on_a_company_whose_stored_State_is_not_in_the_list_and_accepting_changes_nothing`.**
Store `State = "West Bengal "` (trailing space) — a value canonical import can produce (`ImportPlan.cs line 1198`
assigns verbatim, no list check) — open Alter, accept without touching the control, reload, assert the string is
**byte-identical**, trailing space included.
*Mutation:* make the picker fall back to `null`/first-item when the stored value is unrecognised. Must redden.
This is the §3.1 ER-13 requirement and the only test standing between an Alter screen and silent data loss on
canonical-imported books.

### 6.7 Documentation gates

**T-16.** Any `file.cs:NN` this slice writes into a `.md` must resolve — `DocumentCodeAgreementTests` scans every
`*.md` in the repo. **And `LoadBearingCitationContentTests`** (`tests/Apex.Ledger.Tests/LoadBearingCitationContentTests.cs`)
asserts CONTENT at named anchors, located by an adjacent context phrase. Two of its existing anchors sit on
sentences this slice will edit — `"GST one** — \`src/Apex.Desktop/Services/VoucherPrintProjector.cs line 726\` is"`
(`:47-50`) and `"**▶ The load-bearing guard — \`SupplierPostalAddressText\`"` (`:65-68`), both in `plan.md`.
**Editing the W0-2b block without preserving those exact context phrases turns the citation test red.**

**T-17.** Add **at least one new anchor** for this slice, following the lesson recorded in that file's own doc
comment (`:31-35`): *"A tool built because a check has a blind spot must be pointed at the code nobody has read
yet."* The natural anchor is the `CompanyStorage.Save` guard — document line context phrase → `CompanyStorage.cs`
→ required token `company.EnsureValid()`.

**T-18.** `XamlLayoutInvariantTests` bites on the new XAML: fixed columns must fit the page column
(`:407-408`); star columns must stay readable and **`StarvedStarAllowList` must not gain an entry** (`:509-510`);
header and row column definitions must match (`:604-605`); no `TextTrimming`/`TextWrapping` inside a horizontal
`StackPanel` (`:314-315`); and **no internal build jargon in any user-facing string** (`:1275-1276`) — so no
"Phase", no "slice", no "W0-2b" in a label, a hint, the guard text or the accept prompt. The brand rule adds:
the word "Tally" must never appear in a shipped string.

---

## 7. RISK

### 7.1 🔴 The single largest risk: 97 test files bootstrap through `CreateCompany()`

**Measured at `3a4fcdb`:**

| Probe | Count |
|---|---|
| Test files calling `.CreateCompany()` | **97** |
| `CreateCompany()` call sites in `tests/` | **153** |
| `NewCompanyName` references in `tests/` | **147** |

The near-universal fixture bootstrap is `vm.NewCompanyName = name; vm.CreateCompany();` (e.g.
`tests/Apex.Desktop.Tests/AccountGroupMasterViewModelTests.cs line 41-42`,
`ActualBilledVoucherEntryViewModelTests.cs line 60-61`, `BillOfSupplyRoutingTests.cs line 90-91`).

**Binding constraint: `NewCompanyName` must survive as a settable property on `MainWindowViewModel`, and
parameterless `CreateCompany()` must keep producing an identical company when only the name is set.** Implement
`NewCompanyName` as a pass-through to the profile view model's Name rather than deleting it. Refactoring these
147 assignments is not in scope and would bury the actual change in noise.
**Corollary for the FY defaults:** `CreateCompany()` must keep passing *nothing* for
`financialYearStart`/`booksBeginFrom` when the operator did not type them, so
`CompanyFactory.CreateSeeded`'s existing `?? new DateOnly(DateTime.Today.Year, 4, 1)` (`CompanyFactory.cs line 22`)
still governs. **Fixing the January–March future-FY bug (§1.13) by changing that default is a separate change
with its own regression surface — it would move the FY of every one of those 153 fixtures.** Recommendation:
**do not touch `CompanyFactory.cs line 22` in this slice**; the screen makes the date *typeable*, which is what T1-6
asks for. Record the default's defect as a follow-up.

### 7.2 What currently passes that would start failing

| Test | Why it moves | Correct handling |
|---|---|---|
| `GatewayHierarchyTests.Gateway_exposes_the_sections_with_their_items_nested` (`tests/Apex.Desktop.Tests/GatewayHierarchyTests.cs line 47-67`) | It asserts the **exact** header list and order: `Assert.Equal(new[] { "Masters", "Statutory", "Transactions", "Reports", "Data" }, HeaderLabels(vm));` (`:54`). Adding a `Company` section (§4.4) makes it red. | **Update deliberately** to `{ "Company", "Masters", "Statutory", "Transactions", "Reports", "Data" }` and add `Assert.Contains("Alter Company", items)`. The test is doing its job. |
| `GatewayHierarchyTests.Section_headers_are_not_selectable_and_selection_starts_on_the_first_item` (`:70-79`) | Asserts `vm.Menu[0].IsHeader` and `Assert.NotEqual(0, vm.SelectedIndex)`. Still true with a new first header, but the **selected index shifts**. | Verify, do not assume. Any sibling asserting a literal index must be re-derived, not renumbered by hand. |
| `A_freshly_created_company_prints_no_supplier_address_lines_at_all` (`tests/Apex.Desktop.Tests/VoucherInvoicePrintViewModelTests.cs line 472-499`) | It drives the **real** `CreateCompany()` path and asserts `Address` blank, `Country == "India"`, `Pin == null`. Its own doc comment says *"if `CreateCompany()` ever starts capturing an address, the probe assertions fail here first."* | **🔴 It must stay GREEN, UNCHANGED.** That is an acceptance criterion, not an accommodation: the create screen must default the postal block to blank and must not write `Country` to anything other than `"India"`. If this test needs editing, the design has drifted into changing every existing book's printed output. |
| `A_company_with_no_address_still_prints_exactly_as_before` (`:448`) and `The_Rule_46a_name_and_GSTIN_pair_is_delivered_but_the_address_half_is_not` (`:319`) | The latter's **name asserts a defect that this slice closes** for newly-captured companies. | The behaviour it pins — a company with no captured address prints no address — is still correct and must stay green. **Its name and doc comment need rewording**, because "the address half is not delivered" stops being a statement about the product and becomes a statement about that fixture. Reword; do not delete. |
| `KeyboardArbitrationTests` / `MenuHotKeyAndAcceptTests` / `QuickJumpModifierGuardTests` | They assert `"Accept Ledger? (Y/N)"` and `IsMasterAcceptScreen` on **ledger/voucher** screens only. | Unaffected by adding two screens to the list — **verify, do not assume.** `KeyboardArbitrationTests.cs line 1133` asserts `IsMasterAcceptScreen` is **false**, but on `Screen.VoucherEntry` (`:1132`), which is untouched. |
| Everything that saves a company through `CompanyStorage.Save` | §5.1 adds `company.EnsureValid()` there. Any fixture holding an invalid `Company.Pin` would now throw. | **Measured risk: low.** No UI writes `Company.Pin` today (§1.1), so no Desktop fixture can hold one. **But run the full Desktop suite before and after adding that single line and report both counts** — this is exactly the kind of "obviously safe" line that lens 1 finding 1 was. |

**Not pinned by anything, which is itself a finding:** `grep -rn "Screen.CreateCompany\|ShowCreateCompany" tests/`
returns **zero hits**. The create screen's navigation and keyboard behaviour has **no test coverage at all**
today — only its side effect (a company object) is exercised, 153 times. So adding `Screen.CreateCompany` to
`IsMasterAcceptScreen` (§4.5) breaks no test, **and that is not reassurance** — it means the behaviour change
would ship unobserved. The slice must add the coverage it is changing.

### 7.3 ER-13 — a company that never touches these fields must be byte-identical

**The claim to be proved, not asserted.** Three independent legs:

1. **Printed output.** Guaranteed by construction: the slice does not touch `SellerBlock`
   (`VoucherPrintProjector.cs line 721-727`) or its `SupplierPostalAddressText` guard (`:742-745`), which keys the
   whole block off a non-blank `Address`. A company created and never edited has a blank `Address`, so nothing
   prints — the state `A_freshly_created_company_prints_no_supplier_address_lines_at_all` already pins.
2. **The stored row.** No schema change (§2), and `InsertCompany` binds the same columns from the same members.
   A created-and-never-edited company writes the identical 86-column row.
3. **Canonical export.** `CanonicalMapper.MapCompany` (`CanonicalMapper.cs line 59-80`) is untouched, so the exported
   document for an unedited company is byte-identical.

**The one place ER-13 could genuinely break, and the test that must catch it:** if `CompanyProfileViewModel`
initialises `Country` from an empty control instead of from `company.Country`, Accept would write `""` (or
`null` into a `TEXT NOT NULL` column) over the `"India"` default. **Pinned by T-13 and by leg 1's existing
test.** The Create screen must show `Country` **pre-filled with the domain default**, and Accept must never
write a blank over a non-blank `Country`.

### 7.4 Deliberately left out, each with its reason

| Left out | Reason |
|---|---|
| **The 5 contact fields** (Telephone, E-Mail, Mobile, Fax, Website) | §2.3 — the corpus cannot settle their order (one primary contradicts itself), they are fidelity not compliance, and they cost a migration + a downgrade leg on a downgrade path already recorded as broken. Deferred to a proposed **W0-2c** with two named preconditions (§2.4). |
| **Any schema change / a v54 claim** | §2 — nothing needs one, and declining v54 leaves `plan.md`'s allocation line untouched (`plan.md line 1495-1512`). |
| **`Alt+D` company delete** | §4.3 — destructive, operates on the open company's own `.db`, needs a confirmation design of its own. |
| **Company RENAME** | §4.3.1 — the `.db` filename **is** the company name (`CompanyStorage.cs line 63-64, 70-75, 48-59`). Renaming without a file move produces two books. `Name` is display-only on Alter; the statutory name (Mailing Name) is editable. |
| **`Alt+R` Group Company** | §4.2 — no domain support; a feature, not a field. |
| **F11-opens-after-save** | §4.5 — corpus-attested (grounding §2.3) but changes the landing screen for 153 fixtures. |
| **The corpus `Alt+K` company-menu route** | §4.4 — `Alt+K` is bound to Saved Views (`MainWindow.axaml.cs line 651-653`). Rebinding is KB-4 arbitration work; inventing a different chord would be unsourced fidelity. |
| **KB-3 prefix type-to-filter on the new pickers** | §3.1 — not built anywhere in `src/`, three design rounds failed, and `plan.md line 712-715` puts it behind a measurement spike. |
| **Matching the corpus's PRINT order** (Address → State → Country → Pin Code) | Grounding §9 item 11 — it would move the shipped WI-4 **recipient** block too, a second statutory-document change. **Note this slice now makes capture order and print order differ deliberately** (§4.1.1); §8 records it. |
| **Changing `CompanyFactory`'s FY default** | §7.1 — moves the FY of 153 fixtures. The screen makes the date typeable, which is what T1-6 asks. |
| **Wiring `EnsureValid` into `SqliteCompanyStore.Save`** | §5.2 — a wider blast radius than this slice has evidence for. |

### 7.5 Residual risks that survive this design

- **The GST-off → GST-on transition.** The seed only fires when the GST screen is opened. A user who types a
  postal State, never opens the GST screen, and later imports a canonical document with a GST config gets no
  seeding. **Correct** — the imported config is authoritative — but it means "the GST State always derives from
  the postal one" is *not* an invariant and must not be written down as one anywhere.
- **The guard is advisory only.** By ruling. A user can ship invoices with a genuinely wrong home State and see
  only a warning line. That is the user's decision (RULING 3 clause 3), and it should be restated in
  `memory.md` so a future session does not "fix" it into a refusal.
- **`MasterGstDetails.EnsureValid` remains 1-of-5.** This slice does not close it and must not be read as
  closing it (`docs/wf1-owed-review-findings.md`, "What this review did NOT close"). It adds **no** new write
  path to that block, which is the most it can honestly claim.
- **`SchemaDowngrade.V51ToV50` remains broken.** Untouched here; named only so that "no schema change" is not
  mistaken for "the schema is healthy".

### 7.6 🔴 A SECOND unguarded invariant this screen makes reachable — found during this pass, recorded nowhere

`Company`'s constructor enforces two rules (`src/Apex.Ledger/Domain/Company.cs line 557-569`):

    if (string.IsNullOrWhiteSpace(name))     throw new ArgumentException("Company name is required.", …);   // :559-560
    if (booksBeginFrom < financialYearStart) throw new ArgumentException("BooksBeginFrom must be ≥ FinancialYearStart.", …);   // :561-562

**Neither is enforced on the setters.** `FinancialYearStart` (`:104`) and `BooksBeginFrom` (`:107`) are plain
`{ get; set; }`, and **`Company.EnsureValid()` (`:97-101`) checks only the PIN.** So assigning the two dates as
properties can persist a company that `Company`'s own constructor would refuse — the same shape as the recorded
defect *"the app can already produce a database its own importer rejects"*.

**And the one guarded path does not cover it either.** In `ApplyCompanyHeader`, `t.EnsureValid()` is at
`ImportPlan.cs line 1203` and the two date assignments are at **`:1204-1205`** — **the guard runs BEFORE the dates are
written.** Extending `EnsureValid` to cover the invariant would therefore still not protect the import path
without also moving that call.

**Why it matters to W0-2b:** §4.3.2 makes both dates editable on Alter, which is the first UI that assigns them
as properties. **Mitigation in this slice, deliberately scoped:**
1. `CompanyProfileViewModel.Accept()` pre-validates `BooksBeginFrom >= FinancialYearStart` and refuses with a
   message (the §5.3 pattern), so the screen cannot produce the state; and
2. a test asserts the refusal, with a mutation (delete the check) that must redden it.

**Deliberately NOT done here:** moving the rule into `Company.EnsureValid()` and relocating the `ImportPlan`
call. That changes the canonical import's behaviour on documents that are accepted today — a compatibility
decision with its own regression run, and it belongs to whoever owns the importer. **Recorded as a
carry-forward, not fixed silently, and not left undiscovered.**

---

## 8. WHAT THE CORPUS COULD NOT SETTLE

This **extends** `docs/w0-2-company-screen-grounding.md` §9 (items 1–11). Items 1–9 and 11 remain **open** and
this design fills none of them; item 10 is closed. Numbering continues from 11 so the two lists concatenate
without collision.

**Notes on the existing entries, because this design LEANS on three of them:**

- **Item 9 (the contact-block field order) is now load-bearing.** It is the primary reason the five contact
  fields are out of scope (§2.3a). If a later session closes item 9, the W0-2c precondition is met; if it
  cannot, the fields stay out. **Do not ship them by picking an order.**
- **Item 2 (which fields become non-editable after creation) is why §4.3.2 chose "editable with a warning".**
  Locking the two date fields would be inventing a restriction the corpus does not state; silently allowing the
  edit on a book with vouchers would be a wrong-figures hazard. The middle course is a project decision — **and
  the warning itself is unattested; see item 14 below.**
- **Item 6 (undocumented Base-Currency defaults)** is why the three currency-formatting toggles are out of scope
  even though they are cheap: shipping them means inventing their defaults.

### New entries — 12 to 20

**12. Whether TallyPrime warns when the postal State and the GST State disagree, and what it says.**
The corpus attests the **default** — the GST Details State *"by default shows the State name as selected in the
Company Creation screen"* (Book PDF p.177) — and it attests a standing exhortation on the same page, *"In
company creation time State must be selected right."* **It says nothing about what happens when the two
diverge.** The consistency guard of §3.4, its two-screen placement and its wording are **entirely a project
decision made under RULING 3**, not fidelity. Nothing in this design should be cited as evidence that TallyPrime
warns.

**13. Whether TallyPrime's Company Creation State list is the same list as its GST state-code list.**
The corpus says only *"State (from a list)"* (grounding §2 field 6) and never enumerates the Company Creation
list. §3.1 binds the postal picker to `IndianState.All` — the GST state-code master, codes 01–38 plus 97, and
**deliberately neither 96 nor 99** (`src/Apex.Ledger/Domain/IndianState.cs line 35-49`). **Whether TallyPrime's
postal list is that same set, a superset (a foreign postal address), or free text with a lookup is UNVERIFIED.**
The consequence is concrete: under this design a company **cannot record a non-Indian postal State**, and that
restriction is ours.

**14. Whether TallyPrime warns before changing "Financial year begins from" / "Books beginning from" on a book
that already has vouchers.** Grounding item 2 records that no source says the fields lock. **No source says
anything about a warning either.** §4.3.2's advisory is a project decision.

**15. Whether TallyPrime's Company Alter screen permits changing the company NAME, and what it does to the data
folder if it does.** The Book states Alter's purpose as editing *"company address or contact number or email and
other any information"* (p.15) — it neither includes nor excludes the Name. §4.3.1 makes `Name` display-only for
a **storage** reason of ours (the `.db` filename is the name), not a fidelity one. **If a later session finds
that TallyPrime does support rename, this restriction is a gap to close, not a decision to defend.**

**16. The accept confirmation's shape on the Company screen.** The corpus gives **both** `Ctrl+A` (Book p.14; SG
p.268) and **Enter-then-Enter** (SG p.60) and, per grounding §6, *"does not reconcile them"*. This app's shipped
WI-11 convention is `Ctrl+A` **or** Enter → **`Accept …? (Y/N)`** → `Y`. **The second key is `Y`, not `Enter`,
and that divergence is ours across all 24 existing master screens** (`MainWindowViewModel.cs line 4881-4890`). §4.5
extends it to the company screens for consistency. **UNVERIFIED whether TallyPrime's company screen has a
Y/N prompt at all.**

**17. The route to Company Alter when `Alt+K` is unavailable.** The corpus route is `Alt+K` → Company menu →
Alter (Book p.15; SG p.61/p.267). `Alt+K` is bound here to Saved Views (`MainWindow.axaml.cs line 651-653`). §4.4
places Alter in the Miller cascade under a new **Company** section **with no accelerator**. **The corpus offers
no alternative route to copy** — the placement is ours, and no chord should be invented to make it look
attested.

**18. Whether TallyPrime's printed supplier block follows its capture order.** Grounding item 11 already records
that **the corpus contains no supplier-block print specimen**, so the capture orders (Address → State → Country →
Pin Code) are indicative only. **This design adds a new fact to that entry:** §4.1.1 puts **State before
Country** on the capture screen (following the Book and the Study Guide's worked example), while W0-2a prints
**Country before State**. **Capture order and print order now differ deliberately, and the corpus cannot say
whether that is right.** Recorded so a later reader does not treat the mismatch as an accident. Aligning them
still means moving the State into the shared address builder and therefore changing the shipped **recipient**
block too — the reason item 11 deferred it in the first place.

**19. What "Mailing Name" does on ALTER when it was never separately set.** The corpus says it auto-fills from
Name **at creation** and is editable (grounding §2 field 3). Our constructor does the same —
`MailingName = name` (`Company.cs line 566`). **It is silent on whether TallyPrime re-syncs Mailing Name when the
Name changes on Alter.** Moot in this slice (Name is display-only on Alter, §4.3.1) — **it stops being moot the
day rename ships**, which is why it is recorded now.

**20. Whether TallyPrime persists a company-level State independently of the GST one at all.** RULING 3 keeps
both columns and makes the postal one the source of truth. **The corpus shows one State field on Company
Creation and one on GST Details, and describes the second as defaulting from the first — it never says whether
the second is STORED separately or merely displayed.** Our schema stores both (`companies.state` and
`companies.gst_home_state`, `Schema.cs line 184` and `:201`) and both are persisted and round-tripped. **The ruling
settles what WE do; it does not settle what TallyPrime does**, and this design must not be read as evidence of
the latter.

---

## 9. WHAT THIS DESIGN DELIBERATELY DOES NOT DECIDE

Left to the R12 gate / the implementer, stated so they are not decided by accident:

1. **Whether `Screen.CreateCompany` joins `IsMasterAcceptScreen`** alongside `AlterCompany` (§4.5). It changes
   an untested behaviour (§7.2). Recommendation: **yes, both**, with new coverage; fallback: Alter only.
2. **Whether the two date fields are editable on Alter at all** (§4.3.2). Recommendation: editable + advisory,
   because locking them is unattested (grounding item 2). A user or reviewer may prefer read-only-with-vouchers.
3. **The exact section placement of the `Company` gateway header** — first, or after `Masters` (§4.4).
   Recommendation: first. It changes `GatewayHierarchyTests.cs line 54` either way.
4. **Whether W0-2c (the contact block) is created as a plan row now or when item 9 is closed** (§2.4).

---

## 10. SUMMARY OF FINDINGS THAT CHANGE SOMETHING

| # | Finding | Where |
|---|---|---|
| F1 | **The store drops a GST home State written onto a GST-off company** — the naive reading of RULING 3 produces a silently-lost value. Same root cause as the owed review's lens 1 finding 1. | §1.9, §3.2, test T-4 |
| F2 | **The `.db` filename IS the company name**, so a rename through Alter would fork the book into two files with no error. | §4.3.1, §7.4 |
| F3 | **`CompanyStorage.Save` is the single Desktop write choke point** — 98 call sites, and the only constructor of `SqliteCompanyStore` in the Desktop layer. One `EnsureValid()` there covers every UI path, present and future. | §5.1 |
| F4 | **A second unguarded invariant**: `BooksBeginFrom >= FinancialYearStart` is constructor-only, `EnsureValid` does not check it, and the import calls the guard at `:1203` **before** writing the dates at `:1204-1205`. | §7.6 |
| F5 | **97 test files / 153 call sites bootstrap through `CreateCompany()`**, so `NewCompanyName` and the parameterless signature are a binding compatibility constraint. | §7.1 |
| F6 | **Two shipped code comments still describe the R12 gate as OPEN** (`Company.cs line 79-84`, `VoucherPrintProjector.cs line 698-707`). | §1.8, change list rows 10-11 |
| F7 | **The State/Country capture order is resolvable from the corpus after all** — the Study Guide's worked example sides with the Book against the Study Guide's prose, the same self-contradiction pattern as the contact block. | §4.1.1 |
| F8 | **The slice needs no schema change and should decline v54**, leaving `plan.md`'s allocation line untouched. | §2 |
| F9 | **The create screen has zero test coverage of its navigation/keyboard behaviour** — 153 fixtures exercise only its side effect. | §7.2 |
| F10 | **Line drift in the grounding doc §7.1 and §7.6** (`Company.cs` and `SqliteCompanyStore.cs` numbers). Claims stand; numbers moved. | §1.5, §1.6 |

