# Tally version & voucher gap audit — Apex Solutions

**Author:** A1 (Business Analyst) · **Date:** 2026-08-01 · **Branch:** `claude/confident-ellis-dedef5`
**Scope:** read-only audit. No source, test, plan or memory file was modified; no build or test run was executed.

---

## Citation key

Every factual claim below carries a tag. Claims with **no tag are marked `[UNCITED]` inline** — there are a small
number and they are also collected in §6.

| Tag | Meaning |
|---|---|
| `[CODE]` | Verified by me this session by reading the file at the stated path/line in this worktree. |
| `[CODE-H]` | Taken from the implementation-survey handoff; **file exists and I read its surrounding context, but I did not walk every stated line number**. Treat line numbers as approximate. |
| `[CORPUS-BOOK]` | `tally/664311548-Tally-Prime-Book.pdf` (Deepak Prasad, *Tally Prime Basic Accounting & Inventory*). Page numbers are the **author's** printed page numbers. Licensed corpus, git-ignored, never quoted at length. |
| `[CORPUS-SG]` | `tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf`. |
| `[CORPUS-SHORTKEY]` | `tally/659947760-Tally-Prime-Short-Key.pdf` — **see §6.1, this document is not machine-trustworthy**. |
| `[OFFICIAL]` | Tally Solutions' own help site, fetched and read this session. URL given. |
| `[PRIMARY-ERP9]` | Tally.ERP 9 Series A Release 1.0 official release-notes PDF, mirrored at `https://gyctc.wordpress.com/wp-content/uploads/2018/01/book-1.pdf`. Read via the version-research handoff, not re-fetched by me. |
| `[SECONDARY]` | Third-party web source, named at point of use. Tally's own pre-2009 product pages have been retired and archive.org was unreachable from this environment, so **all Tally 4.5–9 dating is secondary**. |

---

## 1. Executive summary — where we stand, in plain numbers

**The build.**

> **† Re-verified 2026-08-15 against `SeedVoucherTypes.cs`. Three findings below have been ACTED ON since
> 2026-08-01 and their original wording is corrected in place, with the original quoted beside each
> correction.** The seed is now locked to `docs/design/accounting-core.md` §5.3 by
> `tests/Apex.Ledger.Tests/DocumentCodeAgreementTests.cs`, so this class of drift goes red in CI from here on.
> **(a)** "24 of 24 … are seeded" → **23 of 24**; the Attendance seed row was deleted (decision **D24-B**).
> **(b)** "1 is dead data" → the dead row is **gone**, not merely unused. **(c)** The Physical Stock bullet
> below (`"1 of 24" advertises a wrong and dead shortcut`) is **SUPERSEDED**: the seed now carries
> **`"Ctrl+F7"`** (`src/Apex.Ledger/Seed/SeedVoucherTypes.cs:44`, decision **X1**) and
> `VoucherTypeResolver.RepairSupersededSeedShortcuts` repairs companies created before that change. The
> bullet is left standing as the record of why the fix was needed; **do not read it as current**.

- **23 of 24** TallyPrime predefined voucher types are seeded in the domain — *originally "24 of 24"*. The 24th, **Attendance**, is deliberately not seeded (decision **D24-B**): nothing in the product ever posted a voucher of that kind, so the row was dead master data. `[CODE: src/Apex.Ledger/Seed/SeedVoucherTypes.cs:21-68`, with a hard count guard at `:71` and `:99-100]`. TallyPrime's own number 24 and the enumeration are corroborated `[CORPUS-BOOK p.24: "There are 24 Pre-defined vouchers in Tally Prime", 3-column table]`; the 23-vs-24 difference is a **recorded fidelity gap**, not a miscount `[docs/full-clone-census.md Tier 3]`.
- **23 of 23 seeded types** have a real, reachable entry screen. The one dead row this audit found — `VoucherBaseType.Attendance`, which appeared **nowhere in the codebase except its enum member and its seed row** — has since had that **seed row deleted**; the `VoucherBaseType.Attendance` **enum member stays**, because `voucher_types.base_type` is persisted as the enum ordinal and removing it would renumber every later member `[CODE: src/Apex.Ledger/Seed/SeedVoucherTypes.cs:59-66]`. The Attendance *screen* exists but writes `AttendanceEntry` rows, not vouchers `[CODE-H: AttendanceVoucherEntryViewModel]`.
- **2 of 24** have a working entry screen but **no menu row anywhere in the application**: Credit Note and Debit Note. Verified: the strings "Credit Note"/"Debit Note" do not occur in `MainWindowViewModel.cs` at all, and `VoucherBaseType.CreditNote` occurs in the Desktop project only at `VoucherEntryViewModel.cs:1326/1338` (GST logic) and `MainWindow.axaml.cs:656` (the Alt+F6 key) `[CODE]`. They are keyboard-only, plus the Day-Book Alt+A picker `[CODE-H]`.
- **1 of 24** advertises a **wrong and dead** shortcut. Physical Stock is seeded with `"F10"` `[CODE: SeedVoucherTypes.cs:31]` and its menu row prints `"F10"` `[CODE: MainWindowViewModel.cs BuildInventoryVouchersColumn]`. TallyPrime's Physical Stock key is **Ctrl+F7**, and **F10** is "view list of all vouchers or masters" `[OFFICIAL: help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/]`. In Apex, **Ctrl+F7 is bound to nothing** (the Ctrl block at `MainWindow.axaml.cs:620-630` handles only F5/F6/F8/F9) and **F10 opens the Other Vouchers menu** `[CODE: MainWindow.axaml.cs:813-814 → MainWindowViewModel.ShowOtherVouchersMenu at :1925]`. Physical Stock therefore has **no keyboard route at all**, while the UI tells the user it has one.
- **Entry modes:** TallyPrime documents three invoice modes on **four** voucher types — Purchase, Sales, Credit Note, Debit Note `[CORPUS-BOOK pp.33/39/46 (Purchase), 38/42/44 (Sales), 55/57/58 (CN), 61/63/65 (DN)]`. Apex supports Item Invoice on **two** (Purchase, Sales) and Accounting Invoice on **one** (Sales) `[CODE: VoucherEntryViewModel.cs:67-68 CanBeItemInvoice, :80 CanBeAccountingInvoice]`. That is **3 of the 8 documented mode/type pairs missing**, plus Credit/Debit Note entirely lacking invoice modes.
- **Stock on returns does not move.** Item-invoice stock is folded into the inventory engine only for Purchase and Sales base kinds `[CODE: src/Apex.Ledger/Services/ItemInvoiceStock.cs Counts(): "type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales"]`. A goods-return credit note posts **no inventory movement**.
- **Tests:** the last gate recorded in `memory.md` is **3491 passing, schema v49**. I did **not** re-run it (read-only mandate), so this is a recorded figure, not a verified-today figure. The suite contains **2697** `[Fact]`/`[Theory]` attributes across **342** test files against **482** source files `[CODE: static counts, this session]`. (The 3491 vs 2697 difference is `[Theory]` case expansion — that inference is `[UNCITED]`.)

**The version mismatch — the headline.**

The project is a **TallyPrime** clone: the entire licensed corpus is TallyPrime, and `plan.md` and the feature
catalog are built from it. The user is comparing it against **Tally 7.2**, a 2005 product `[SECONDARY: izoe.in,
techguruplus, slideshare "Comparison between various version of tally"; corroborated indirectly by the official
Tally.ERP 9 Rel 1.0 notes, which ship a "Tally72Migration" tool — [PRIMARY-ERP9] pp.4-5]`. That is roughly
**twenty years and five product generations** of divergence, and it cuts **both ways**:

- **Apex has large modules that 7.2 has no counterpart for at all** — GST (ERP 9 Release 6.0, 2017 `[SECONDARY: tallyacademy.in; official release notes help.tallysolutions.com/docs/te9rel60/release_notes_6_0/release_6_0_2.htm]`), e-Invoice and e-Way Bill (TallyPrime 1.1 / 2.0 `[OFFICIAL: help.tallysolutions.com/tallyprime-features-release-wise/]`), IMS (TallyPrime 6.1, same source), GSTR-9/9C, TCS.
- **Tally 7.2 has an entire indirect-tax module Apex does not implement in any form**: state-wise **VAT**, plus **CST** and **Service Tax** — 7.2's headline features `[SECONDARY: izoe.in; techguruplus; slideshare comparison]`. Grep across `src/Apex.Ledger` for `VAT`, `CST`, `ServiceTax`, `Excise`, `FBT` returns **zero files** `[CODE]`.
- **Tally 7.2 does not have five of the 23 voucher types Apex seeds** — *originally "six of the 24", counted before the Attendance seed row was deleted (decision **D24-B**); Attendance is no longer among the types Apex seeds, so five remain.* Attendance and Payroll arrive with Tally 9 `[SECONDARY: Tally 9 Release Notes 1.0 preview, mirrored scribd.com/document/4760128; ncsmindia.com/wp-content/uploads/2012/04/TALLY-9.0-PDF.pdf §14-15]`; the four Job Work types arrive with Tally.ERP 9 Release 3.0-era Job Work `[SECONDARY: apnitally.com/2011/02/tallyerp-9-rel-30-is-available-for.html — see §6.4, this attribution is weak]`.

**Bottom line.** The voucher *inventory* is close to complete; the voucher *fidelity* is not. Nine specific
defects (§3, §5) are the difference between "24 types exist" and "24 types work the way Tally's do". Three of
them — no stock on credit notes, no accounting invoice on purchases, no Voucher Class — are **business-visible
and cost money**, not cosmetics.

---

## 2. The Tally version question, stated squarely

### 2.1 What the project targets

`CLAUDE.md` names the target as Tally Prime, `docs/tally-feature-catalog.md` is the requirements reference, and
**all ten corpus PDFs in `tally/` are TallyPrime documents** — `517196318-Tally-Prime-with-GST`,
`567608375-Case-Study-1-Tally-Prime-Exercise`, `654430402-Tally-Practical-Problems`,
`659947760-Tally-Prime-Short-Key`, `664311548-Tally-Prime-Book`, `680842180-Tally-With-GST-Notes`,
`696054070-TALLY-PRIME-STUDY-GUIDE`, `703679456-TALLY-PRIME-WITH-GST-Notes-PDF`,
`712654832-Fundamental-of-Accounting-and-Tally-Prime-Note1`, `719244897-Tally-Book` `[CODE: directory listing]`.
There is **no 7.2, no Tally 9, and no Tally.ERP 9 primary material in the corpus at all.** Every fidelity
judgement this project has made for two years has been made against TallyPrime.

### 2.2 What the user is running

Tally 7.2, released **2005**, is the "integrated enterprise system" release: **state-wise VAT**, plus **TDS** and
**Service Tax** `[SECONDARY: izoe.in/blog/from-tally-4-5-to-tally-prime-exploring-different-versions-benefits/;
techguruplus.com/tally-versions-list/; slideshare.net/slideshow/comparison-between-various-version-of-tally/5849600]`.
Its existence and its file format are corroborated by a primary Tally document: the official Tally.ERP 9 Series A
Release 1.0 notes ship a **"Tally72Migration"** tool and describe converting Tally 7.2 `.tcp` files
`[PRIMARY-ERP9 pp.4-5]`.

> **A note on scope.** The user's copy of 7.2 was explicitly declared out of bounds for this audit and was not
> opened, read, listed or launched. Nothing in this document is derived from it. Everything below is from the
> licensed corpus and public documentation.

### 2.3 What 7.2 lacks versus Tally 9

Tally 9 (2006, Release 1.0) added, over 7.2:

| Added in Tally 9 | Evidence | Strength |
|---|---|---|
| **Payroll** — pay heads, salary structures, attendance, gratuity, cost-centre-allocable payroll. Brings the **Attendance** and **Payroll** voucher types, which do not exist in 7.2. | Tally 9 Release Notes 1.0 preview ("Payroll Processing: Full payroll module added"); detail in ncsmindia.com/…/TALLY-9.0-PDF.pdf §14 | `[SECONDARY]` — Tally's own notes, but only the preview is publicly readable |
| **POS Invoicing** — multi-mode tender, cash drawer, POS registers | Tally 9 Release Notes 1.0 preview; ncsmindia PDF §15 | `[SECONDARY]` |
| **FBT (Fringe Benefit Tax)** | slideshare comparison deck; techguruplus. Corroborated: the official ERP 9 Rel 1.0 notes treat FBT as a **pre-existing** module receiving "Major Enhancements" `[PRIMARY-ERP9 p.16]` | `[SECONDARY]` with primary corroboration of pre-existence |
| **Excise for Dealers** (RG23D, Form 2) | slideshare; techguruplus. Corroborated: official ERP 9 Rel 1.0 notes list it under Minor Enhancements / Issues Resolved `[PRIMARY-ERP9 pp.22, 29]`, i.e. pre-existing | same |
| **Concurrent multilingual** — 13 languages, enter in one / print in another | Tally 9 Release Notes 1.0 preview (names Bahasa Melayu/Indonesia explicitly); ncsmindia PDF §16 | `[SECONDARY]` |
| **International statutory** — Malaysia Service Tax & Sales Tax, Indonesia VAT | Tally 9 Release Notes 1.0 preview | `[SECONDARY]`, strongest of the Tally-9 claims |
| **e-TDS return filing** on top of the existing TDS module | medium.com/@tally_97442; internshala blog | `[SECONDARY]`, weak |
| **Job Costing** | official ERP 9 Rel 1.0 notes list it under Minor Enhancements `[PRIMARY-ERP9 p.23]` and Issues Resolved `[p.33]` — how Tally documents a pre-existing feature | **Uncertain — see §6.4.** I could not establish whether it shipped in Tally 9 R1.0 or a later 9.x, and I found **no source proving its absence from 7.2**. |

**Do not credit these to Tally 9 — they were already in 7.2:** VAT (7.2's own headline), TDS, Service Tax, CST,
multi-currency and forex gain/loss, cost centres and cost categories, budgets, scenarios, optional and post-dated
vouchers, reversing journals, BoM manufacturing journals, multi-godown, batch/expiry, price lists and price
levels, reorder levels, ageing, TallyVault, and Tally Audit `[SECONDARY: slideshare comparison; ncsmindia PDF
company-creation chapter and its dedicated Tally Audit chapter]`. ODBC arrived at **6.3** `[SECONDARY:
techguruplus; slideshare]`.

**Voucher-type count in 7.2.** 7.2's predefined set is the classic **18**: Contra, Payment, Receipt, Journal,
Sales, Purchase, Credit Note, Debit Note, Memorandum, Reversing Journal, Sales Order, Purchase Order, Delivery
Note, Receipt Note, Rejections In, Rejections Out, Stock Journal, Physical Stock. **This list is reconstructed,
not sourced — see §6.3.** Tally 9 adds Attendance and Payroll (→20). The four Job Work types (Job Work In/Out
Order, Material In/Out) are Tally.ERP 9 (→24) — the count TallyPrime ships today `[CORPUS-BOOK p.24]`.

### 2.4 What a cloner working from 7.2 would faithfully reproduce that TallyPrime no longer does

This is the section that matters most for interpreting user feedback. If the user reports "this is wrong, Tally
does X", X may be a **7.2 behaviour that TallyPrime deliberately removed**. Nine known divergences:

1. **Two mode keys instead of one.** Tally.ERP 9 and earlier used **Ctrl+V** (As Voucher) and **Alt+I** (As
   Invoice) as separate keys. TallyPrime collapsed both into **Ctrl+H (Change Mode)**
   `[OFFICIAL: help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/ — "In Vouchers: to change mode – open vouchers in different modes"]`.
   Apex implements the TallyPrime form: Ctrl+H cycling `[CODE-H: VoucherEntryViewModel.ChangeMode()]` plus Ctrl+I
   as a direct Item-Invoice toggle `[CODE: MainWindow.axaml.cs:481-487]`. A 7.2 user pressing Ctrl+V or Alt+I
   will find nothing. **This is correct behaviour, not a bug.**
2. **Different voucher keys.** Several keys were re-cut in TallyPrime. Credit Note was **Ctrl+F8** in ERP 9, now
   **Alt+F6**. Debit Note was **Ctrl+F9**, now **Alt+F5**. Rejections Out was **Alt+F6**, now **Ctrl+F5**.
   Physical Stock was **Alt+F10**, now **Ctrl+F7** `[OFFICIAL, same page, for the TallyPrime column; the ERP 9
   column is [SECONDARY] via the catalogue handoff and the ERP 9 help table at
   help.tallysolutions.com/docs/te9rel54/Common_Files/Function_Key_Combination.htm]`. A 7.2 user's muscle memory
   will collide with correct TallyPrime bindings on at least four keys.
3. **Ctrl+H means two different things.** In a **voucher** it changes entry mode; in a **report** it changes
   *view* `[OFFICIAL, same page]`. In ERP 9 the report job was done by F7/F8/F9 and Alt+T. Apex consumes Ctrl+H
   only on a Purchase/Sales voucher screen `[CODE-H: MainWindowViewModel.cs:4841-4842 IsInvoiceableEntry gate]` —
   so **the report arm of Ctrl+H is not implemented at all**. That is a TallyPrime gap, not a 7.2 one, and it is
   listed in §4.
4. **No GST in 7.2 — none at all.** GST is ERP 9 Release 6.0, July 2017 `[SECONDARY: tallyacademy.in;
   OFFICIAL release notes help.tallysolutions.com/docs/te9rel60/release_notes_6_0/release_6_0_2.htm]`. Apex's
   entire GST surface (GSTR-1, GSTR-3B, GSTR-4, GSTR-9, GSTR-9C, CMP-08, QRMP, ITC set-off, e-invoice, e-Way,
   IMS, GSTR-2B recon — 20+ report modules `[CODE: src/Apex.Ledger/Reports/ listing]`) has **no 7.2 counterpart
   to be judged against**.
5. **VAT / CST / Service Tax, conversely, have no Apex counterpart.** Zero files in `src/Apex.Ledger` mention
   them `[CODE]`. If the user's 7.2 workflow is VAT-based, **none of it transfers.**
6. **Menu navigation.** 7.2 uses the 1990s menu tree (Gateway → Display → Account Books → …). TallyPrime replaced
   it with **Alt+G (Go To)**, **Chart of Accounts** and the top-bar Company menu `[CORPUS-SG p.229 uses "Alt+G >
   Create Master > Voucher Type"; CORPUS-BOOK p.24 uses "GOT > Charts of Accounts > Stock item > Alt+H
   (Multi-Master)"]`. Apex implements a **Miller-column cascade**, which is neither — a deliberate recorded
   design decision, not a fidelity claim `[UNCITED as to the decision record: I did not open the ADR]`.
7. **Manufacturing Journal is not a predefined type in either**, but the route differs. In TallyPrime it is a
   *user-created* type under the Stock Journal parent, reached from the voucher-type list `[CORPUS-SG; corpus
   TALLYBOOK p.10 per the catalogue handoff]`. Apex binds **Alt+F7 directly to Manufacturing Journal** whenever
   the BOM feature is on, which **removes Stock Journal's own keyboard route** `[CODE: MainWindow.axaml.cs:662-664]`.
   That is an Apex divergence from TallyPrime, not from 7.2.
8. **F10.** In TallyPrime F10 lists vouchers/masters `[OFFICIAL]`. Apex's F10 opens Other Vouchers `[CODE:
   MainWindow.axaml.cs:813-814]` — close enough in spirit. But the corpus shortcut sheet renders F10 as "List of
   Voucher Type" `[CORPUS-SHORTKEY]`, and Apex's *seed* still says F10 means Physical Stock `[CODE:
   SeedVoucherTypes.cs:31]`. Three different stories in one codebase.
9. **Payroll, POS and Job Work simply are not in 7.2.** A 7.2 user reviewing Apex's Payroll, POS Billing or Job
   Work screens has no baseline to compare against, in either direction.

**Practical recommendation:** before the next acceptance round, agree with the user *which* Tally is the
yardstick. If the answer is TallyPrime (as `CLAUDE.md` and `plan.md` say), 7.2 feedback should be triaged through
this list before being logged as defects. If the answer changes to 7.2, roughly half the shipped statutory work
(all of GST) is out of scope and a VAT module is in — a scope change of a size that needs an R12 gate.

---

## 3. The voucher table

**Columns.** *Tally* = does TallyPrime have this predefined voucher type. *Apex* = does Apex have a working entry
screen. *Fidelity* = FULL (behaves as documented) / PARTIAL (present, documented behaviour missing) / STUB
(surface exists, does not do the job) / MISSING. *Tally key* is the TallyPrime key `[OFFICIAL:
help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/, fetched and read this
session]`. *Apex key* is what is actually wired in `src/Apex.Desktop/Views/MainWindow.axaml.cs` `[CODE]`.

Seed row references throughout are `src/Apex.Ledger/Seed/SeedVoucherTypes.cs` `[CODE]`. The 24-type list and its
ordering are `[CORPUS-BOOK p.24]`.

### 3.1 Accounting vouchers

| # | Voucher | Tally | Apex | Fidelity | Tally key | Apex key | What specifically is missing |
|---|---|---|---|---|---|---|---|
| 1 | **Contra** | Yes | Yes | **PARTIAL** | F4 | F4 `[CODE: MainWindow.axaml.cs:804 → ButtonBar]` | Corpus documents **Ctrl+H Double Entry ⇄ Single Entry** on Contra `[CORPUS-BOOK pp.25, 26 — two distinct navigation steps]`. Apex's Ctrl+H is gated to Purchase/Sales only `[CODE-H: MainWindowViewModel.cs:4841 IsInvoiceableEntry]`, so **Single Entry mode does not exist on Contra**. |
| 2 | **Payment** | Yes | Yes | **PARTIAL** | F5 | F5 | Same: Single Entry mode absent `[CORPUS-BOOK p.30/31; CORPUS-SG p.75 "Ctrl+H (Change Mode) → Single Entry"]`. The statutory-payment autofill path exists separately (TDS Ctrl+F, TCS) `[CODE: MainWindow.axaml.cs:361-367; CODE-H: TdsStatPaymentViewModel/TcsStatPaymentViewModel]`. |
| 3 | **Receipt** | Yes | Yes | **PARTIAL** | F6 | F6 | Same: Single Entry mode absent `[CORPUS-BOOK pp.28, 29]`. |
| 4 | **Journal** | Yes | Yes | **FULL** | F7 | F7 | Nothing identified. Corpus reaches Journal with F7 and shows **no Ctrl+H step** `[CORPUS-BOOK p.44]`, so fixed double-entry is correct. |
| 5 | **Sales** | Yes | Yes | **FULL** | F8 | F8 | The only type with all three modes in Apex `[CODE: VoucherEntryViewModel.cs:67-68, :80]`. Matches `[CORPUS-BOOK pp.38, 42, 44]`. |
| 6 | **Purchase** | Yes | Yes | **PARTIAL** | F9 | F9 | **Accounting Invoice mode is absent.** Corpus documents it `[CORPUS-BOOK p.39 — "GOT > Voucher > Press F9 … > Ctrl+H (For Accounting Invoice)"; extraction line 1402]`. Apex gates it off: `CanBeAccountingInvoice => BaseType == Sales` `[CODE: VoucherEntryViewModel.cs:80]`. The reason is documented in the XML comment at `:71-79` — enabling it silently dropped the §194J TDS carve-out because `TdsPossible`/`DetectTdsShape` read the empty `Lines` collection. **Consequence: a service purchase (consultancy, rent, professional fees) cannot be entered as an invoice at all.** |
| 7 | **Credit Note** | Yes | Yes | **PARTIAL** | Alt+F6 | Alt+F6 `[CODE: MainWindow.axaml.cs:656]` | Three defects. (a) **No menu row exists anywhere** — verified by exhaustive grep `[CODE]`; keyboard-only. (b) **No invoice modes.** Corpus documents all three on CN `[CORPUS-BOOK pp.55, 57, 58; extraction lines 2055/2132/2181]`; `CanBeItemInvoice` excludes CN `[CODE: VoucherEntryViewModel.cs:67-68]`. (c) **No stock movement** — `ItemInvoiceStock.Counts()` returns false unless the base type is Purchase or Sales `[CODE: src/Apex.Ledger/Services/ItemInvoiceStock.cs]`, so **a sales return does not put goods back into stock.** GST CN/DN linkage does exist `[CODE: VoucherEntryViewModel.cs:1326, 1338; CODE-H: CreditDebitNoteService.cs]`. |
| 8 | **Debit Note** | Yes | Yes | **PARTIAL** | Alt+F5 | Alt+F5 `[CODE: MainWindow.axaml.cs:655]` | Identical three defects, mirrored `[CORPUS-BOOK pp.61, 63, 65; extraction lines 2270/2348/2402]`. A purchase return does not remove goods from stock. |

### 3.2 Non-accounting / memo vouchers

| # | Voucher | Tally | Apex | Fidelity | Tally key | Apex key | What specifically is missing |
|---|---|---|---|---|---|---|---|
| 9 | **Memorandum** | Yes | Yes | **FULL** | none (F10 route) | none; menu row under Other Vouchers `[CODE: BuildOtherVouchersColumn]` | Provisional gate implemented `[CODE-H: VoucherEntryViewModel.cs:533]`; convert-to-real-voucher implemented `[CODE-H: MainWindowViewModel.cs:4856]`, matching `[CORPUS-BOOK pp.45-46]`. Memorandum Register exists `[CODE: src/Apex.Ledger/Reports/MemorandumRegister.cs]`. |
| 10 | **Reversing Journal** | Yes | Yes | **FULL** | none (F10 route) | none; menu row under Other Vouchers `[CODE]` | "Applicable Upto" implemented `[CODE-H: VoucherEntryViewModel.cs:521-525; src/Apex.Ledger/Domain/Voucher.cs:97-101]`, matching `[CORPUS-BOOK pp.47-48]`. Register exists `[CODE: Reports/ReversingJournalRegister.cs]`. |

### 3.3 Inventory vouchers

| # | Voucher | Tally | Apex | Fidelity | Tally key | Apex key | What specifically is missing |
|---|---|---|---|---|---|---|---|
| 11 | **Receipt Note (GRN)** | Yes | Yes | **FULL** | Alt+F9 | Alt+F9 `[CODE: MainWindow.axaml.cs:657]` | Nothing identified. Register exists `[CODE: Reports/InventoryRegisters.cs, ReportKind.ReceiptNoteRegister]`. |
| 12 | **Delivery Note** | Yes | Yes | **FULL** | Alt+F8 | Alt+F8 `[CODE: :658]` | Nothing identified. Posting rule "affects stock only, not party balance" `[CORPUS-BOOK pp.76-77]` matches `AffectsAccounts` excluding DeliveryNote `[CODE: src/Apex.Ledger/Domain/VoucherEffects.cs]`. Statutory delivery-challan detail (triplicate marking ORIGINAL/DUPLICATE/TRIPLICATE, ≤16-char serial, Rule 55(4) e-Way threshold) `[CORPUS-BOOK p.76]` — **I did not verify whether Apex prints the triplicate markings; treat as unassessed.** |
| 13 | **Rejections In** | Yes | Yes | **FULL** | Ctrl+F6 | Ctrl+F6 `[CODE: MainWindow.axaml.cs:628]` | Nothing identified. Note corpus lists Rejections In with **no Rate field** (quantity-only) `[CORPUS-BOOK p.51]` while Rejections Out has one `[p.53]` — **I did not verify whether Apex mirrors that asymmetry; unassessed.** |
| 14 | **Rejections Out** | Yes | Yes | **FULL** | Ctrl+F5 | Ctrl+F5 `[CODE: :629]` | Nothing identified. |
| 15 | **Stock Journal** | Yes | Yes | **PARTIAL** | Alt+F7 | Alt+F7 — **but only when BOM is off** `[CODE: MainWindow.axaml.cs:662-664]` | When `Company.SetComponentsBom` is true, Alt+F7 opens **Manufacturing Journal instead**, leaving Stock Journal menu-only. In TallyPrime, Alt+F7 opens Stock Journal and Manufacturing Journal is selected from the voucher-type list `[CORPUS-BOOK; CORPUS-SG per the catalogue handoff]`. Source/Destination (Consumption/Production) sides and additional-cost handling are implemented `[CODE-H: InventoryVoucherEntryViewModel; src/Apex.Ledger/Services/AdditionalCostApportionment.cs]`, matching `[CORPUS-BOOK pp.79-81]`. |
| 16 | **Physical Stock** | Yes | Yes | **PARTIAL** | **Ctrl+F7** `[OFFICIAL]` | **NONE — Ctrl+F7 is unbound** `[CODE: MainWindow.axaml.cs:620-630, the Ctrl block handles only F5/F6/F8/F9]` | Two defects. (a) **Wrong key advertised, and it is dead**: seed says `"F10"` `[CODE: SeedVoucherTypes.cs:31]`, menu row prints `"F10"` `[CODE: BuildInventoryVouchersColumn]`, and F10 actually opens Other Vouchers `[CODE: MainWindow.axaml.cs:813-814]`. Screen is menu-only. (b) **The "Ignore physical stock difference" configuration does not exist** — grep for `IgnorePhysical`/"ignore physical" returns nothing `[CODE]`. Apex hardcodes the *non-ignoring* arm: subsequent transactions use the counted balance `[CODE: src/Apex.Ledger/Services/InventoryLedger.cs:164 applyBefore, :269 ordering, :273 IsPhysicalStock]`. Corpus documents this as a **toggle** `[CORPUS-BOOK p.82]`. |

### 3.4 Order vouchers

| # | Voucher | Tally | Apex | Fidelity | Tally key | Apex key | What specifically is missing |
|---|---|---|---|---|---|---|---|
| 17 | **Purchase Order** | Yes | Yes | **FULL** | Ctrl+F9 | Ctrl+F9 `[CODE: MainWindow.axaml.cs:626]`; also Ctrl+F9 on Reorder Status raises a pre-filled PO `[CODE: :625]` | Non-posting semantics correct: PO is excluded from both `AffectsStock` and `AffectsAccounts`, and listed in `IsOrderBaseType` `[CODE: src/Apex.Ledger/Domain/VoucherEffects.cs]`, matching the corpus's explicit "affects neither Stock nor Accounts" `[CORPUS-BOOK p.67]`. |
| 18 | **Sales Order** | Yes | Yes | **PARTIAL** | Ctrl+F8 | Ctrl+F8 `[CODE: :627]` | Non-posting semantics correct `[CODE: VoucherEffects.cs; CORPUS-BOOK p.73]`. Corpus documents two SO sub-screens — **Order Details** (Mode/Terms of Payment, Other Reference, Terms of Delivery) and **Dispatch Details** (Dispatch through, Destination, Courier/Agent) `[CORPUS-BOOK pp.73-74]`. **I did not verify whether Apex's `InventoryVoucherEntryViewModel` carries these; treat as unassessed, not as confirmed-present.** |

### 3.5 Job Work vouchers

All four are seeded `IsActive = false` and surface only when `Company.EnableJobOrderProcessing` is on
`[CODE: SeedVoucherTypes.cs:42-45; BuildOtherVouchersColumn gates on `Company is { EnableJobOrderProcessing: true }`]`.
Activation is via `JobWorkService.SetEnabled` `[CODE-H: src/Apex.Ledger/Services/JobWorkService.cs:45-58]`.

| # | Voucher | Tally | Apex | Fidelity | Tally key | Apex key | What specifically is missing |
|---|---|---|---|---|---|---|---|
| 19 | **Job Work In Order** | Yes | Yes | **FULL** | none (F10 route) | none; menu row | Order-only semantics correct `[CODE: VoucherEffects.IsOrderBaseType includes JobWorkInOrder]`. Corpus fields — Job Giver party, Process Instruction (Duration of Process, Nature of Processing), Tracking Components, Fill Components using BOM, per-component "Pending to Receive" `[CORPUS-BOOK pp.83-86]`. **Field-level parity not verified; unassessed.** |
| 20 | **Material In** | Yes | Yes | **FULL** | none (F10 route) | none; menu row | Moves stock, not accounts `[CODE: VoucherEffects.AffectsStock includes MaterialIn; AffectsAccounts excludes it]`. `UseForJobWork` flag exists `[CODE: src/Apex.Ledger/Domain/VoucherType.cs:115]`, matching the corpus's "Use for Job Work = Yes" requirement `[CORPUS-BOOK pp.87-89]`. |
| 21 | **Job Work Out Order** | Yes | Yes | **FULL** | none (F10 route) | none; menu row | Mirror of #19 `[CORPUS-BOOK pp.90-93]`. Same unassessed field-level caveat. |
| 22 | **Material Out** | Yes | Yes | **FULL** | none (F10 route) | none; menu row | Mirror of #20 `[CORPUS-BOOK pp.94-95]`. |

### 3.6 Payroll vouchers

| # | Voucher | Tally | Apex | Fidelity | Tally key | Apex key | What specifically is missing |
|---|---|---|---|---|---|---|---|
| 23 | **Attendance** | Yes | Screen yes, **voucher type dead** | **STUB** | none (F10 route) `[OFFICIAL: no direct key listed]` | none; menu row under Payroll when `PayrollEnabled` `[CODE: BuildVouchersColumn]` | **The seeded voucher type is never used.** `VoucherBaseType.Attendance` occurs in the entire repository only at its enum member and `SeedVoucherTypes.cs:46` — verified by exhaustive grep of `src/` and `tests/` `[CODE]`. The screen writes `AttendanceEntry` rows via `PayrollAttendanceService` `[CODE-H]`. So the row exists in the voucher-type master and does nothing. Corpus also documents **Ctrl+F Attendance Autofill** `[CORPUS-SG "Processing Attendance", ~p.213]` — **not verified present in Apex; unassessed.** |
| 24 | **Payroll** | Yes | Yes | **PARTIAL** | Ctrl+F4 | Ctrl+F4 `[CODE: MainWindow.axaml.cs:372, gated on PayrollEnabled]` | Posting works — `PayrollVoucherService` resolves the type **without** an `IsActive` filter `[CODE-H: src/Apex.Ledger/Services/PayrollVoucherService.cs:72]`, and the type correctly affects accounts `[CODE: VoucherEffects.AffectsAccounts includes Payroll]`. **But nothing ever flips Attendance/Payroll `IsActive` to true**: `PayrollService.EnablePayroll` only sets company flags `[CODE-H: src/Apex.Ledger/Services/PayrollService.cs:36-41]`. Two visible consequences: neither type appears in the Day-Book Alt+A "Add Voucher" picker (which filters `t.IsActive` `[CODE-H: MainWindowViewModel.cs:2840]`), nor in the Scenario master's include-list (`ScenarioMasterViewModel.cs:92` filters `t.IsActive` `[CODE: verified]`). **A payroll voucher cannot be included in a scenario.** Corpus documents **Payroll Autofill** as the transaction type `[CORPUS-SG ~p.214]` — unassessed. |

### 3.7 Non-predefined types Tally supports and Apex handles specially

| Type | Tally | Apex | Fidelity | What is missing |
|---|---|---|---|---|
| **POS Billing** (user-created, parent = Sales) | Yes `[CORPUS-SG pp.229-230, 236-238]` | Yes — auto-created `useForPos: true` Sales type on first use `[CODE-H: MainWindowViewModel.cs:3367-3395]`; menu row under Other Vouchers `[CODE]` | **PARTIAL** | Alt+I single/multi-tender toggle implemented `[CODE: MainWindow.axaml.cs:502-509]`; Alt+A tax analysis `[CODE: :512-518]`; tender types and POS config modelled `[CODE: src/Apex.Ledger/Domain/PosConfig.cs, PosTenderType.cs]`. **The type is auto-created, not user-created** — the user cannot define their own POS type with their own abbreviation/messages/declaration, because there is no Voucher Type master (see §4.1). |
| **Manufacturing Journal** (user-created, parent = Stock Journal) | Yes | Yes — auto-created, gated on `SetComponentsBom` `[CODE-H: MainWindowViewModel.cs:3260-3292]` | **PARTIAL** | Same "auto-created, not user-created" limitation. Plus it **steals Alt+F7 from Stock Journal** (§3.3 #15). |
| **TDS / TCS Stat Payment** | Yes (Payment + Ctrl+F autofill) | Yes — dedicated screens `[CODE-H: TdsStatPaymentViewModel, TcsStatPaymentViewModel]`; Ctrl+F bound for TDS `[CODE: MainWindow.axaml.cs:361-367]` | **PARTIAL** | **TCS has no open accelerator** — the menu row carries an empty shortcut string `[CODE: BuildVouchersColumn, "TCS Stat Payment" with `""`]`. The in-code comment states this is deliberate to avoid a Ctrl+F collision. In TallyPrime the Stat Payment button is Ctrl+F on the Payment voucher for **all** taxes `[SECONDARY: businessdocbox mirror of ERP 9 Rel 2.0 release notes, which introduced the "Stat Payment" button covering VAT/CST/TDS/TCS/Excise/FBT]`. |
| **Voucher Class** (on Sales/Purchase/Payment/etc.) | **Yes** — corpus uses it: "Alt+F5 > Select Class 'S.I' > Ctrl+H" `[CORPUS-BOOK, extraction line 4398]` | **No general implementation** | **MISSING** | Grep for `VoucherClass` in `src/` returns only the **POS tender-ledger pre-map** `[CODE: src/Apex.Ledger/Domain/PosConfig.cs:9, 39; VoucherType.cs:103]`. There is no general voucher-class feature — no default-ledger classes, no automatic percentage-of-value allocations, no class selection at voucher entry. |

### 3.8 Summary counts

| Fidelity | Count (of the 24) | Types |
|---|---|---|
| **FULL** | 13 | Journal, Sales, Memorandum, Reversing Journal, Receipt Note, Delivery Note, Rejections In, Rejections Out, Purchase Order, Job Work In Order, Job Work Out Order, Material In, Material Out |
| **PARTIAL** | 10 | Contra, Payment, Receipt, Purchase, Credit Note, Debit Note, Stock Journal, Physical Stock, Sales Order, Payroll |
| **STUB** | 1 | Attendance |
| **MISSING** | 0 | — |

> **Read "13 FULL" as an upper bound.** The classification is my own `[UNCITED as a judgement]`, and five of the
> thirteen — the four Job Work types and Delivery Note — are marked FULL on *posting-semantics* evidence only.
> Their field-level parity against `[CORPUS-BOOK pp.76, 83-95]` is **unassessed** (§6.8). Any of them could drop
> to PARTIAL on a closer look. Sales Order already did, for exactly that reason.

Separately, note what this table does **not** say. Zero MISSING is a statement about voucher *types*, not about
voucher *capability*: **Voucher Class**, which cuts across Sales, Purchase, Payment and more, is missing entirely
(§3.7), and it is not counted here because it is not one of the 24.

---

## 4. Feature gaps by area

### 4.1 Masters

| Gap | Evidence | Severity |
|---|---|---|
| **No Voucher Type master.** TallyPrime has full Create / Alter / Display / Delete on voucher types — "GOT > Create > Voucher Type", "GOT > Alter > Voucher type > … Alt+D" `[CORPUS-BOOK pp.17-18]`, plus Alt+G > Create Master > Voucher Type `[CORPUS-SG p.229]`. Apex has **no such screen**: there is no `VoucherTypeMasterViewModel` in the 118 files under `src/Apex.Desktop/ViewModels/` `[CODE: directory listing]`, and the only voucher-type editor is `VoucherNumberingConfigViewModel` (numbering affixes only) `[CODE]`. The only two non-seeded types in the system are auto-created by the app `[CODE-H: MainWindowViewModel.cs:3260-3292, :3367-3395]`. | The user cannot create "Cash Sales" vs "Credit Sales", cannot rename, cannot set an abbreviation, cannot deactivate an unused type, cannot delete. | **HIGH** |
| **No "Show Inactive" flow.** TallyPrime activates a dormant type in-flow: F10 > Show Inactive > select > Enter > Yes `[CORPUS-SG p.74; CORPUS-BOOK pp.45-94]`. Apex's F10 shows a fixed menu column `[CODE: BuildOtherVouchersColumn]` with no inactive list. | Combined with the Payroll `IsActive` defect (§3.6 #24), an inactive type can only be activated by code. | **MEDIUM** |
| **No Voucher Class.** See §3.7. | `[CODE: grep]` vs `[CORPUS-BOOK line 4398]` | **MEDIUM** |
| Masters that **are** present and wired to UI: Ledgers, Groups, Stock Items/Groups/Categories, Units, Godowns, Batches, BoM, Price Levels/Lists, Reorder, Currencies, Cost Centres/Categories, Budgets, Scenarios, Employees/Groups/Categories, Pay Heads, Salary Structures, Attendance Types, Payroll Units, Nature of Payment, Nature of Goods `[CODE: ViewModels directory listing — 30+ `*MasterViewModel.cs` files]`. | — | — |

### 4.2 Reports

45 report kinds in the generic reports screen `[CODE: ReportKind enum in src/Apex.Desktop/ViewModels/ReportsViewModel.cs]`,
plus ~30 dedicated report view-models, over 78 report modules in `src/Apex.Ledger/Reports/` `[CODE: directory listing]`.
Known absences, each verified by grep `[CODE]`:

| Missing report | Notes |
|---|---|
| **Group Summary** | Zero hits for "Group Summary" in `src/Apex.Ledger` or `src/Apex.Desktop/ViewModels`. A standard Tally Display report. |
| **Stock Query** (Alt+S) | Zero hits. Corpus lists Alt+S = Stock Query Report `[CORPUS-SHORTKEY — but see §6.1; this key/label pair may be misaligned]`. Apex binds Alt+S to a Reorder Levels toggle `[CODE-H: MainWindow.axaml.cs:298]`. |
| **Movement Analysis** | Zero hits. Tally's item/party/group movement analysis. |
| **Sales Register / Purchase Register / Journal Register** as voucher-type registers | Only `PosRegister`, `MemorandumRegister`, `ReversingJournalRegister` and the inventory registers exist `[CODE: Reports/ listing]`. Tally's Display > Account Books offers a register per voucher type. **Day Book exists** `[CODE: Reports/DayBook.cs]` and may partially cover this — **not assessed.** |
| **Ctrl+H "Change View" on reports** | Apex consumes Ctrl+H only on Purchase/Sales voucher screens `[CODE-H: MainWindowViewModel.cs:4841-4842]`. The report arm documented at `[OFFICIAL]` is unimplemented. |

Present and substantial: Balance Sheet, P&L, Trial Balance, Cash Flow, Funds Flow, Ratio Analysis, Day Book,
Ledger Book (incl. Cash Book / Bank Book) `[CODE: Reports/LedgerBook.cs]`, Outstandings, Stock Summary, Godown
Summary, Batch/Ageing, Reorder Status, Negative Stock, Negative Cash/Bank, Cost Reports, Budget Variance, Forex,
Interest, Bank Reconciliation, Comparative/columnar `[CODE: Reports/ directory]`.

### 4.3 Statutory

| Area | State | Evidence |
|---|---|---|
| **GST** | Extensive — GSTR-1 (+ amendments), GSTR-3B, GSTR-4, GSTR-9, GSTR-9C, CMP-08, QRMP, GSTR-2B reconciliation, ITC gate/reversal/set-off, electronic ledgers, e-invoice, e-Way Bill, IMS, RCM, advance receipts, DRC-03, B2C QR | `[CODE: src/Apex.Ledger/Reports/ and /Services/ listings]` |
| **TDS / TCS** | Extensive — Form 24Q/26Q/27A/27D/27EQ, Form 16/16A, challan reconciliation, coverage and exception reports, stat payment | `[CODE: same]` |
| **Payroll statutory** | PF ECR, ESI, Professional Tax, Gratuity provision, Bonus, §192 salary TDS | `[CODE: same]` |
| **VAT / CST / Service Tax / Excise / FBT** | **ABSENT — zero files** | `[CODE: grep across src/Apex.Ledger returns no match for any of the five]` |
| **The 4% cess question** | Recorded in project memory as an **open user decision**: an unverified 4% cess applied for TY2026-27 on the default path, described as a live payroll deduction with no retrievable statutory basis. | `[UNCITED by me — I did not open the cited source file this session. This is carried forward from the project memory index and must be verified before it is relied on.]` |

### 4.4 Payroll

| Gap | Evidence | Severity |
|---|---|---|
| **Attendance voucher type is dead data** (§3.6 #23) | `[CODE: exhaustive grep]` | LOW as a defect, MEDIUM as a signal — a seeded master row that nothing reads is exactly the kind of thing that makes a "24 of 24" claim misleading. |
| **Payroll/Attendance types never activated** — excluded from the Day-Book picker and from Scenarios (§3.6 #24) | `[CODE: ScenarioMasterViewModel.cs:92 verified; CODE-H: MainWindowViewModel.cs:2840]` | **MEDIUM** — payroll cannot participate in scenario/provisional reporting. |
| Attendance Autofill (Ctrl+F) and Payroll Autofill parity | `[CORPUS-SG ~pp.213-214]` — **unassessed** | Unknown |
| Payroll engine breadth | Pay heads, salary structures, attendance types, PF/ESI/PT/Gratuity/Bonus, §192 TDS, Payslip/PaySheet/Payroll Register/Attendance Register all present | `[CODE: Services/ and Reports/ listings]` |

### 4.5 Inventory

| Gap | Evidence | Severity |
|---|---|---|
| **Credit/Debit Note move no stock** (§3.1 #7/#8) | `[CODE: ItemInvoiceStock.Counts()]` | **HIGH** |
| **No "Ignore physical stock difference" config** (§3.3 #16) | `[CODE: grep]` vs `[CORPUS-BOOK p.82]` | **MEDIUM** |
| **Stock Journal loses its key when BOM is on** (§3.3 #15) | `[CODE: MainWindow.axaml.cs:662-664]` | **LOW** |
| **Negative stock: unfinished and explicitly stopped.** `plan.md` §Phase 10.8 records the work as **STOPPED, engine reverted to HEAD** by user decision 2026-07-29 `[CODE: plan.md:1126]`. Three attempts produced three different unbounded Balance-Sheet errors, each of which passed the full suite `[UNCITED by me — from the project memory index; the plan entry at :689 and :719-720 is the citable part]`. A `NegativeStock` **report** exists `[CODE: Reports/NegativeStock.cs]`; the *allow-negative valuation* feature does not. | | **HIGH** (open, known, and previously dangerous) |
| Present: multi-godown, batch/expiry, BoM, additional cost apportionment, price lists/levels, reorder, actual-vs-billed quantity, UQC, stock valuation methods | `[CODE: Domain/ and Services/ listings]` | — |

### 4.6 Keyboard

Architecture is sound: **all** key handling is in one Window-level tunnel handler `[CODE: src/Apex.Desktop/Views/MainWindow.axaml.cs:30 AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel)]`, first-match-wins, with only two auxiliary Enter-to-drill handlers `[CODE-H: :1447, :1470]`. Gaps:

| Gap | Evidence | Severity |
|---|---|---|
| **Ctrl+F7 (Physical Stock) unbound; F10 advertised instead and dead** (§3.3 #16) | `[CODE]` vs `[OFFICIAL]` | **MEDIUM** |
| **Credit Note / Debit Note reachable only by keyboard** — no menu row, so the accelerator is undiscoverable | `[CODE: grep]` | **MEDIUM** |
| **Prefix type-to-filter does not exist.** The settled keyboard contract requires dropdowns to filter by typed prefix with the typed text visible. What ships is type-to-**JUMP**: on a data-driven column the bare letter moves the highlight `[CODE-H: MainWindowViewModel.HandleMenuLetter :6337 → GatewayColumn.TypeAhead :430]`. `plan.md` records this as **KB-3, new work, three design rounds NOT-READY** `[CODE: plan.md:528-533]`. | | **HIGH** for daily data-entry speed |
| **Ctrl+H report "Change View" arm unimplemented** (§4.2) | `[CODE-H]` vs `[OFFICIAL]` | **LOW** |
| **TCS Stat Payment has no accelerator** (§3.7) | `[CODE]` | **LOW** |
| **15 `SelectedIndex`-bound ListBoxes deferred** from the keyboard-parity slice | `[CODE: plan.md:550]` | **MEDIUM** |

### 4.7 Configuration

| Gap | Evidence | Severity |
|---|---|---|
| **Voucher numbering: only 3 methods.** Apex has `Automatic`, `Manual`, `None` `[CODE: src/Apex.Ledger/Domain/NumberingMethod.cs]`. TallyPrime also offers **Automatic (Manual Override)** and **Multi-User Auto**. `[UNCITED — I did not find these two in the corpus this session and did not fetch an official page for them. Verify before acting.]` | | MEDIUM if confirmed |
| **FY restart / reset of numbering is explicitly deferred** by user decision, because it collides with e-invoice statutory numbering | `[CODE: plan.md:595]` | **MEDIUM** — most Indian businesses restart invoice numbers each FY |
| Present: prefix/suffix affixes with date-effective rows, width, prefill-with-zero, prevent-duplicate | `[CODE: src/Apex.Ledger/Domain/VoucherType.cs:178-206; VoucherNumberingConfigViewModel]` | — |
| F11 Company Features and F12 Configure exist as button-bar entries | `[CODE-H: MainWindowViewModel.BuildButtonBar :6546-6547]` | — |

### 4.8 Data

| Gap | Evidence | Severity |
|---|---|---|
| **TallyVault (company data encryption) — absent** | grep for "Vault" across `src/` returns nothing `[CODE]`. Planned in the **excluded** Phase 10 `[CODE: plan.md:420-424]`. | **HIGH** for a shipping product |
| **Security Control / users / roles / password policy — absent** | grep for `class User`, `SecurityControl`, `UserRole` returns nothing `[CODE]`. Phase 10 `[CODE: plan.md:420-424]`. | **HIGH** |
| **Edit Log / Tally Audit — absent** | grep for "Edit Log" returns nothing; the only "audit" hits in `Company.cs` are GST ITC-reversal audit rows, unrelated `[CODE: Company.cs:454-458, 732-736]`. `plan.md` C-7 records Edit Log and Tally Audit as **two separate deliverables**, both in Phase 10 `[CODE: plan.md:1093]`. TallyPrime's Edit Log is a 2.1 feature `[OFFICIAL: help.tallysolutions.com/tallyprime-features-release-wise/]`; **Tally Audit predates Tally 9** `[SECONDARY: ncsmindia PDF, dedicated Tally Audit chapter]`, so a 7.2 user **does** have it and Apex does not. | **HIGH** |
| **Backup / Restore — absent as such.** Import/Export data screens exist `[CODE: ImportDataViewModel.cs, ExportDataViewModel.cs]`, which is not the same thing. `plan.md` R-7 explicitly relies on "backup/restore (Phase 10)" as the data-loss mitigation `[CODE: plan.md:1041]` — **and Phase 10 is excluded** `[CODE: plan.md:14]`. | **HIGH** — the stated mitigation for the stated top data risk is not built |
| **Split company by FY — absent** | grep for `SplitCompany` returns nothing `[CODE]`. Phase 10 `[CODE: plan.md:420-424]`. | **MEDIUM** |
| **Group company consolidation — absent** | Phase 10 `[CODE: plan.md:420-424]`; not independently grepped `[UNCITED as to absence]` | **MEDIUM** |
| **ODBC — absent** | grep returns nothing `[CODE]`. Present in Tally since 6.3 `[SECONDARY: techguruplus; slideshare]`, therefore **present in the user's 7.2**. | **LOW** for most users |
| **Multilingual — absent** | grep for "Language" across the four source projects returns nothing `[CODE]`. Tally 9+ feature `[SECONDARY]`; not in 7.2 either. | **LOW** |
| **Remote access / Tally.NET / Control Centre — absent** | Not grepped individually `[UNCITED as to absence]`, but these are Tally.ERP 9 Rel 1.0 features `[PRIMARY-ERP9 p.1]` and out of `plan.md`'s scope entirely. Not in 7.2 either. | **N/A** |
| Present: SQLite persistence with versioned migrations to **v49** `[CODE: plan.md:2103 records v49; src/Apex.Persistence.Sqlite/Schema.cs]`, canonical import/export model `[CODE: src/Apex.Ledger.Io/CanonicalModel.cs]` | — | — |

---

## 5. Severity ranking — what actually matters to someone running a business on this

Ranked by *what breaks for a real user*, not by implementation cost.

### Tier 1 — will produce wrong books or lose data

1. **Credit / Debit Notes do not move stock.** A sales return credits the customer but leaves the goods off the
   books; a purchase return debits the supplier but leaves phantom goods on hand. Closing stock, Balance Sheet
   and gross profit all drift, silently, and the drift compounds every return.
   `[CODE: src/Apex.Ledger/Services/ItemInvoiceStock.cs Counts()]` vs `[CORPUS-BOOK pp.55-65]`
2. **No backup / restore.** `plan.md` names backup/restore as the mitigation for its own top-ranked data-loss
   risk R-7, and puts it in the excluded Phase 10 `[CODE: plan.md:1041 and :14]`. Every business running on this
   is one file corruption away from total loss.
3. **No TallyVault, no user accounts, no roles, no audit trail.** Anyone with the file has everything, and
   nothing records who changed what. Tally 7.2 has **both TallyVault and Tally Audit** `[SECONDARY: ncsmindia
   PDF]`, so this is a **regression against what the user has today**, not merely a missing modern feature.
   `[CODE: greps; plan.md:420-424, :1093]`
4. **Negative stock unresolved.** Stopped by user decision after three attempts each produced a different
   unbounded Balance-Sheet error that passed the full test suite `[CODE: plan.md:1126, :689, :719-720]`. Until
   this is settled, any company that ever goes stock-negative has untrustworthy valuation.

### Tier 2 — blocks ordinary daily work

5. **No Voucher Type master.** No "Cash Sales" vs "Credit Sales", no custom purchase types, no renaming, no
   deactivating, no deleting. Every real Tally deployment does this in week one.
   `[CODE: no such ViewModel exists]` vs `[CORPUS-BOOK pp.17-18]`
6. **Purchase has no Accounting Invoice mode.** Service purchases — rent, consultancy, professional fees,
   freight, audit fees — cannot be entered as invoices. The workaround is the raw Dr/Cr grid, which is slower and
   error-prone, and the reason for the gate (§194J TDS reads the wrong collection) means the naive fix is
   *actively dangerous*. `[CODE: VoucherEntryViewModel.cs:71-80]` vs `[CORPUS-BOOK p.39]`
7. **No prefix type-to-filter in dropdowns.** In a real ledger list of several hundred names, type-to-jump is not
   a substitute for filtering. This is the single biggest drag on data-entry speed and it is the settled,
   user-confirmed contract that is unbuilt. `[CODE: plan.md:528-533]`
8. **No FY restart of voucher numbering.** Most Indian businesses restart invoice numbers on 1 April. Deferred by
   user decision for a real reason (e-invoice numbering collision), but it remains a gap.
   `[CODE: plan.md:595]`
9. **Single Entry mode missing on Contra / Payment / Receipt.** The single-entry payment layout is how most
   operators enter cash and bank vouchers. `[CORPUS-BOOK pp.25-31; CORPUS-SG p.75]`

### Tier 3 — visible wrongness, low blast radius

10. **Physical Stock advertises a key that is both wrong and dead** (F10; should be Ctrl+F7, which is unbound).
    Users will press it, nothing will happen. `[CODE]` vs `[OFFICIAL]`
11. **Credit / Debit Note invisible in every menu.** Discoverable only if you already know Alt+F6 / Alt+F5.
    `[CODE: exhaustive grep]`
12. **Payroll and Attendance types are inactive forever** — excluded from the Day-Book add-voucher picker and
    from Scenarios. `[CODE: ScenarioMasterViewModel.cs:92]`
13. **No Voucher Classes.** `[CODE: grep]` vs `[CORPUS-BOOK line 4398]`
14. **Attendance voucher type is dead seed data.** Harmless in itself; corrosive as evidence, because it is
    exactly what a "24 of 24 complete" status line hides. `[CODE: exhaustive grep]`
15. **Missing reports:** Group Summary, Stock Query, Movement Analysis, per-voucher-type registers.
    `[CODE: greps]`
16. **Stock Journal loses Alt+F7 when BOM is enabled.** `[CODE: MainWindow.axaml.cs:662-664]`
17. **"Ignore physical stock difference" is not configurable.** `[CODE: grep]` vs `[CORPUS-BOOK p.82]`
18. **No ODBC, no split-by-FY, no group company.** `[CODE: greps; plan.md:420-424]`

### Tier 0 — not a defect, but the largest single risk

**The yardstick is unagreed.** The project builds TallyPrime; the user evaluates against Tally 7.2. Roughly half
of what Apex has shipped (all GST, e-invoice, e-Way, IMS, TCS) has **no 7.2 counterpart**, and 7.2's central
indirect-tax module (VAT/CST/Service Tax) has **no Apex counterpart at all** `[CODE: grep returns zero files]`.
Until this is settled explicitly, acceptance feedback cannot be triaged reliably in either direction, and every
defect list will be contaminated by version-mismatch noise. This is an R12 decision, not an engineering task.

---

## 6. Uncertainties and uncited claims

Kept here rather than buried. **Nothing in this section should be relied on without further verification.**

### 6.1 `tally/659947760-Tally-Prime-Short-Key.pdf` is not machine-trustworthy

Its two-column layout extracts with a **systematic label/key misalignment**. The extracted text pairs "F6 =
Contra", "F7 = Payment", "F8 = Stock Journal", "Alt+F8 = Sales" — all wrong. The official TallyPrime table gives
F4 = Contra, F5 = Payment, F8 = Sales, Alt+F7 = Stock Journal `[OFFICIAL:
help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/]`, and the Book agrees
`[CORPUS-BOOK p.25 "Press F4 … for Contra Voucher"]`. **I have not cited any key mapping from this PDF**, and
the two places I referenced it (§2.4 item 8, §4.2 Stock Query) are flagged. Any prior work in this project that
sourced shortcuts from it should be re-checked.

### 6.2 All Tally 4.5 – Tally 9 dating is secondary

Tally Solutions has retired its pre-2009 product pages from `tallysolutions.com`, and `archive.org` was
unreachable from this environment. The only **primary** Tally document available for the pre-Prime era is the
**Tally.ERP 9 Series A Release 1.0** release-notes PDF (12 March 2009, confirmed on its title page)
`[PRIMARY-ERP9]`, and even that was read via the research handoff rather than re-fetched by me. Tally 9's own
release notes are readable only as a preview. **Every date and feature attribution for 4.5, 5.x, 6.3, 7.2, 8.1
and 9 in §2 is therefore secondary.**

### 6.3 The Tally 7.2 predefined voucher list is reconstructed, not sourced

The "classic 18" list in §2.3 was reconstructed from the Tally.ERP 9 documentation set by working backwards from
what Tally 9 and ERP 9 added. **I found no citable official Tally 7.2 voucher-type list.** If this list matters
for a scope decision, it must be verified against a real 7.2 manual — not against the installed product, which is
out of bounds.

### 6.4 Job Costing: cannot place it

The official ERP 9 Rel 1.0 notes list Job Costing under *Minor Enhancements* `[PRIMARY-ERP9 p.23]` and *Issues
Resolved* `[p.33]`, which is how Tally documents a **pre-existing** feature. That tells us it predates March
2009. It does **not** tell us whether it shipped in Tally 9 Release 1.0, a later 9.x, or 7.2. **I found no source
proving its absence from 7.2.** Treat "Job Costing is a Tally 9 addition" as unproven.

### 6.5 TCS in 7.2 — contested

The research handoff flags TCS as contested between sources. I did not resolve it. It does not affect any Apex
conclusion (Apex implements TCS; 7.2's status only affects the §2.3 "already in 7.2" list).

### 6.6 Job Work is attributed to ERP 9 Release 3.0 on weak evidence

§2.3's claim that the four Job Work voucher types are Tally.ERP 9 rests on a single blog post
`[SECONDARY: apnitally.com/2011/02/tallyerp-9-rel-30-is-available-for.html]` plus the absence of Job Work from
Tally 9 feature lists. The **destination** is solid — TallyPrime ships 24 predefined types including the four Job
Work ones `[CORPUS-BOOK p.24]` — but the **release attribution** is weak.

### 6.7 Code claims I did not personally walk

These come from the implementation-survey handoff and are tagged `[CODE-H]` above. I confirmed each file exists
and read enough surrounding context to believe the claim, but I did not verify every stated line number:
`AttendanceVoucherEntryViewModel`, `PayrollVoucherService.cs:72`, `PayrollService.cs:36-41`,
`MainWindowViewModel.cs:2840` (Day-Book picker `IsActive` filter), `:3260-3292` (Manufacturing Journal
auto-create), `:3367-3395` (POS auto-create), `:4826-4842` (ChangeMode routing), `:4856` (memo convert),
`:6337` (HandleMenuLetter), `:6477-6548` (ButtonBar), `JobWorkService.cs:45-58`,
`VoucherEntryViewModel.cs:521-533` (Applicable Upto, provisional gate), and the two auxiliary key handlers at
`MainWindow.axaml.cs:1447/:1470`. **`MainWindowViewModel.cs` is ~6,500 lines and is doing far too much** — that
is an observation, not a finding, and is `[UNCITED]` as to whether it is a problem.

### 6.8 Explicitly unassessed (absence of a finding is not a finding of absence)

I did **not** check field-level parity for: Sales Order's Order Details / Dispatch Details sub-screens
`[CORPUS-BOOK pp.73-74]`; the Job Work order screens' Process Instruction / Tracking Components / BOM fill
`[CORPUS-BOOK pp.83-93]`; Rejections In's quantity-only (no Rate) asymmetry `[CORPUS-BOOK pp.51, 53]`; Delivery
Note triplicate print markings `[CORPUS-BOOK p.76]`; Attendance Autofill and Payroll Autofill `[CORPUS-SG
~pp.213-214]`; Credit/Debit Note "Reason for issuing Note", supplier's note number, and Ctrl+I Original Invoice
No/Date `[CORPUS-BOOK pp.54-66]`. Each of these is a plausible additional gap. **They are absent from §3 because
I did not look, not because they are fine.**

### 6.9 Test figure

**3491 tests green / schema v49** is the last gate recorded in `memory.md` `[CODE: memory.md:2103]`. I did not
re-run it — the audit mandate is read-only and forbids `dotnet build`/`dotnet test`. The static counts (2697
`[Fact]`/`[Theory]` attributes, 342 test files, 482 source files) **are** mine, taken this session `[CODE]`.

### 6.10 The 4% cess

Carried forward from the project memory index as an open user decision — an unverified 4% cess applied for
TY2026-27 on the default payroll path, with no retrievable statutory basis. **I did not open the cited source
file or verify the claim this session.** `[UNCITED]` Flagged here because it is a live money-affecting item and
would otherwise be invisible in a voucher-focused audit.

---

## Sources

Official / primary:
- [TallyPrime keyboard shortcuts — TallyHelp](https://help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/) (fetched and read this session; the authority for every "Tally key" column value in §3)
- [TallyPrime features, release-wise — TallyHelp](https://help.tallysolutions.com/tallyprime-features-release-wise/)
- [Tally.ERP 9 Release 6.0.2 release notes — TallyHelp](https://help.tallysolutions.com/docs/te9rel60/release_notes_6_0/release_6_0_2.htm)
- [Keyboard shortcuts in Tally.ERP 9 — TallyHelp](https://help.tallysolutions.com/docs/te9rel54/Common_Files/Function_Key_Combination.htm)
- Tally.ERP 9 Series A Release 1.0 release notes (PDF mirror): https://gyctc.wordpress.com/wp-content/uploads/2018/01/book-1.pdf

Licensed corpus (git-ignored, `…\Apex Solutions(end)\tally\`, never quoted at length):
- `664311548-Tally-Prime-Book.pdf` — author pages 17-18, 24-31, 33-46, 51-95
- `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` — pp. 73-78, 118, 213-214, 229-238
- `659947760-Tally-Prime-Short-Key.pdf` — **see §6.1**

Secondary (named at point of use in §2): techguruplus.com, slideshare.net, izoe.in, ncsmindia.com,
scribd.com (Tally 9 Release Notes 1.0 preview), apnitally.com, tallyacademy.in, businessdocbox.com,
handwiki.org, medium.com, internshala.com.
