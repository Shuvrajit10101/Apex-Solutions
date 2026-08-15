# Apex Solutions — Feature Census and Prioritised Gap Register

**Scope:** every Tally 7.2 feature, plus the Tally.ERP 9 additions, judged against TallyPrime as the fidelity target.
**Baseline:** worktree `…\.claude\worktrees\recursing-swirles-3138c6`, HEAD `468a96e`, schema v50. Read-only; nothing built, run, or edited.
**Date:** 2026-08-10.

Markers used below: **[V]** = re-verified by me against source at this HEAD during this census. Unmarked rows are relayed from the three mapping agents with their `file:line` evidence intact. **GUESS** where I am inferring.

---

## 1. THE DENOMINATOR

### 1.1 The counting rule (argue with this first)

A **capability** is one thing a user would name when asking "can it do X" — the granularity of a Tally menu row or an F11 toggle, not a field and not a code file. Rules applied:

1. **Voucher types count individually** (18 for 7.2). They are the atoms of the product.
2. **Report families count as one** (`Account Books`, `Statements of Accounts`, `Inventory Books`, `Exception Reports`). This is the largest deliberate compression in the count and it flatters us: `Account Books` scores as one PARTIAL row while hiding six missing registers. Expanding families to individual reports would push the denominator past 200.
3. **A capability is counted once**, in the earliest product that shipped it. ERP 9 rows the source census marks "IN 7.2" are folded into the 7.2 baseline.
4. **Excluded from the denominator entirely** (not gaps, not progress): pure licensing (Silver/Gold, multi-site, rental), edition/subscription features (Tally.NET, Remote Access, Control Centre, Support Centre, TRiB, SMS, Auditors' Edition, Tally.Server 9, Data Synchronisation), the 7.2 data-format migration tool, the 7.2 character-grid UI (superseded by our fidelity target), international statutory packs, TDL, and multilingual. 13 rows.
5. **Held out of the net figure pending a user decision:** obsolete-by-law statutory (9 rows, §3) and excluded-by-decision (7 rows, §4).

### 1.2 The number

| # | Area | In scope | Present | Partial | Absent | Cannot tell |
|---|---|---:|---:|---:|---:|---:|
| 1 | Company creation & configuration (F11/F12) | 4 | 0 | 3 | 1 | 0 |
| 2 | Accounting masters | 10 | 0 | 7 | 3 | 0 |
| 3 | Inventory masters | 12 | 0 | 10 | 0 | 2 |
| 4 | Voucher types (7.2's classic 18) | 18 | 18 | 0 | 0 | 0 |
| 5 | Voucher behaviours & edit verbs | 7 | 4 | 1 | 1 | 1 |
| 6 | Statutory, current law (GST, TDS/TCS, salary IT) | 10 | 3 | 6 | 0 | 1 |
| 7 | Payroll | 5 | 2 | 2 | 0 | 1 |
| 8 | Banking | 9 | 2 | 1 | 5 | 1 |
| 9 | Inventory / manufacturing / job work (post-7.2) | 5 | 2 | 0 | 1 | 2 |
| 10 | Accounting features (post-7.2) | 2 | 0 | 0 | 2 | 0 |
| 11 | Reports | 12 | 6 | 6 | 0 | 0 |
| 12 | Printing | 5 | 0 | 2 | 3 | 0 |
| 13 | Data management (import/export/backup/e-mail) | 5 | 1 | 3 | 1 | 0 |
| 14 | TallyPrime-only capabilities | 11 | 4 | 3 | 4 | 0 |
| | **TOTAL** | **115** | **42** | **44** | **21** | **8** |

**A full clone requires ~115 named capabilities. We have 42 complete, 44 partial, 21 absent, 8 undetermined.**

Reconciles top-down: 90 (7.2 baseline) + 28 (ERP 9 additions in scope) + 11 (TallyPrime-only) = 129, less 8 obsolete-by-law, less 5 excluded-by-decision folded into the baseline, less 1 (ODBC, out of scope) = 115.

### 1.3 The honest "cannot tell" bucket — and it is not 8

The 8 in the table are capabilities whose **existence** nobody has checked. That is the small number. The real one:

**Existence was measured. Fidelity was not.** All three mapping agents measured *does the code exist and can a user reach it*. Almost nothing was measured against *does it behave the way Tally behaves*. Capabilities with any sourced behavioural verification at all:

1. Chart of accounts — 28 predefined groups (OFFICIAL help.tallysolutions.com, verification report A1)
2. Double-entry posting — Robert and Bright fixtures reproduce to the paisa
3. Voucher shortcut keys (OFFICIAL keyboard-shortcuts page)
4. PO/SO/GRN/DN stock-vs-accounts effect rules (corpus BOOK p.67)
5. EPS/EPF split (OFFICIAL epfindia.gov.in)
6. Rule-88A ITC set-off with the §49(5)(c)/(d) proviso
7. GSTR-1 amendment section-to-table map (A14-confirmed in-file)
8. Cost category/centre worked example (corpus SG pp.101-102)

**8 of 115 capabilities have had their behaviour compared to a source. 107 have not.** Every "PRESENT" in the table above means *present and reachable*, not *correct*. A previous sweep on this project reported CANNOT TELL 256 and the 256 was the honest part; the equivalent honest number here is **107**.

Two further caveats on the denominator itself:

- **Granularity dominates.** Compressing four report families into four rows hides ~14 missing reports. Counting them out gives a denominator near 200 and a worse present-ratio. The 115 is the *most favourable defensible* count.
- **The 7.2 baseline is partly unsourced.** The source census marks many 7.2 rows UNVERIFIED — presence asserted from era-ambiguous course syllabi and blogs, because no official 7.2 documentation is reachable and the cracked install is off limits by standing instruction. Roughly 20 of the 90 baseline rows rest on SECONDARY sourcing.

---

## 2. THE GAP REGISTER

Ranked by what a business suffers. Wrong money first, then invalid documents, then impossible tasks, then permanence, then missing capability, then cosmetics.

### TIER 0 — WRONG MONEY AND LEGALLY INVALID DOCUMENTS

| ID | Gap | Evidence | Harm |
|---|---|---|---|
| **T0-1** | **§194Q TDS deducted on the whole transaction value, not the excess over ₹50 lakh.** Once `ThresholdCrossed` returns true, TDS = `assessableValue.Amount * rateBp / 10_000m` on the full amount. | **[V]** `src/Apex.Ledger/Services/TdsService.cs:71-75`. WF-2 is planned in Phase 10.10 but has **not landed** at `468a96e`. | Over-deducts ₹5,000 on the first qualifying transaction and compounds. Deductor liable to the deductee. Register IV-2. |
| **T0-2** | **Closing stock valued at SELLING price.** `LastSaleCost` returns `FlatValue(closingQty, LastSaleRate(...))`. | **[V]** `src/Apex.Ledger/Services/StockValuationService.cs:85`. | Overstates closing stock → overstates gross profit → overstates taxable income. Balance Sheet and P&L both wrong. Register IV-6. |
| **T0-3** | **`StandardCost` is offered as a valuation method whose input field does not exist**, and silently falls back to `LastPurchaseRate`. | Dropdown at `StockItemMasterViewModel.cs:333`; zero `StandardCost` hits in `MainWindow.axaml`; fallback at `StockValuationService.cs:86-87`. Reachable only via JSON/XML import. | Silent wrong valuation with no warning to the operator. |
| **T0-4** | **GST rate hierarchy inverted; 3 of 5 resolution levels do not exist.** | Register IV-1. WF-1 planned, not confirmed landed at this HEAD. | Wrong tax rate on invoices → wrong GSTR-1/3B → wrong liability. |
| **T0-5** | **4% Health & Education Cess applied to live payroll deductions on a rate the code itself says it could not verify.** | `src/Apex.Ledger/Services/SalaryIncomeTax.cs:50-54` — the comment states the rate must be verified before the FY 2026-27 tables are relied on. | Real money deducted from real salaries on an unsourced statutory figure. **Standing user decision, highest priority.** |
| **T0-6** | **Shipped TDS rates and thresholds cited to commercial blogs** (cleartax, disytax). | `src/Apex.Ledger/Seed/SeedTdsTcsRates.cs:7-8`. | R7 violation on figures the product applies to money. |
| **T0-7** | **A composition dealer's every printed document is an illegal tax invoice.** The app *knows* the answer on screen — `IsBillOfSupply` and the s10/Rule-5(f) declaration render in the UI — but neither reaches the PDF, and `InvoicePdf` hard-codes the title. | **[V]** `GstReportSupport.cs:110-123`, `VoucherDetailViewModel.cs:36-43`, `MainWindow.axaml:1990` — and **zero** `BillOfSupply` hits in `Apex.Ledger.Io` or `VoucherPrintProjector.cs`. | Non-compliant document issued to customers. **~1 day to fix; the data is already computed.** |
| **T0-8** | **Every printed invoice carries a blank seller address block.** `SellerBlock` reads `company.MailingName` and `company.Address`; both are public settable fields, persisted and read — with **zero assignment sites anywhere in `src/Apex.Desktop`**. | **[V]** `VoucherPrintProjector.cs:734-739`; `Company.cs:65-70`; grep for assignments to those members across all of `src/Apex.Desktop` returns nothing. Only `ApplyJournal.cs:343-353` and `ImportPlan.cs:1180-1190` write them. | CGST Rule 46 requires the supplier's address on a tax invoice. **Unfixable from inside the UI** — the field cannot be typed anywhere. |
| **T0-9** | **IRN and signed QR are never printed on an e-invoiced supply** — and structurally cannot be. `PdfWriter` exposes only `Text` and `Line`; there is no image primitive. | `PdfWriter.cs:30-70`; zero `Irn`/`QrCode` hits in `InvoicePdf.cs`/`InvoicePrintData.cs`/`VoucherPrintProjector.cs`. | A printed e-invoiced supply is non-compliant. Blocked behind a print-engine rewrite. |
| **T0-10** | **Credit and Debit Notes move no stock.** `ItemInvoiceStock.Counts()` returns true only for Purchase and Sales. | `src/Apex.Ledger/Services/ItemInvoiceStock.cs:53`. plan.md 10.9 NEXT-1, decision D3 approved behind an oracle. | Every goods return leaves inventory permanently overstated. |
| **T0-11** | **Purchase item-invoices, Credit Notes and Debit Notes never print in invoice format** — they silently fall back to a Dr/Cr voucher print. | `VoucherPrintProjector.IsTaxInvoice` requires `BaseType == Sales` (`:48`). Contradicts `docs/phase5-reports-io-requirements.md:217` RQ-11. | Supplier and return documents are unusable as documents. |

### TIER 1 — ROUTINE TASKS IMPOSSIBLE, OR DAMAGE PERMANENT

| ID | Gap | Evidence | Harm |
|---|---|---|---|
| **T1-1** | **No voucher alteration, deletion, cancellation, duplication or insertion.** `VoucherDetailViewModel` is display-only. Alt+D is unbound. Alt+X abandons an *in-progress* entry; it does not cancel a posted voucher. | `VoucherDetailViewModel.cs:31-43`; `MainWindow.axaml.cs:875` (a bare-letter menu jump, not delete); `:309-314`. Phase 10.11 PLANNED, not built. | **This is the master defect.** Every error in Tier 0 is permanent once posted. A real book cannot be kept. |
| **T1-2** | **No master Delete anywhere in the UI, and no Alter for 24 of 27 master kinds.** The engine already has 16 delete services with **zero** Desktop callers; 8 more delete services do not exist at all. | **[V]** `ForAlter` exists in exactly 3 master VMs (Ledger, Group, Stock Item) plus the dispatcher; **[V]** zero Desktop callers for any master-delete service. One `Delete` button exists in 16,988 lines of `MainWindow.axaml` and it deletes a Saved View. | A typo in a master is permanent. Tally has Alter on at least 13 master kinds (corpus BOOK, 19 distinct `GOT > Alter > …` step lines). |
| **T1-3** | **No Voucher Type master.** No ViewModel, no `Screen` enum member, no Create-menu row. Consequently: no custom voucher types, no numbering-method selection, no way to activate an inactive type. | No `VoucherTypeMasterViewModel` among 120 ViewModel files; zero `"Voucher Type"` hits in the label dispatch. Corpus BOOK pp.17-18 has all four verbs. | Blocks a whole configuration layer, and directly causes T1-4. |
| **T1-4** | **Payroll cannot post.** The Payroll voucher type ships `IsActive = false` and `PayrollService.EnablePayroll` never flips it — the only writer of that property in the entire tree is `JobWorkService.cs:51`. `VoucherTypeResolver.ResolveForEntry` returns null with a message telling the operator to activate a type there is no UI to activate. | `SeedVoucherTypes.cs:67`; `PayrollService.cs:36-40`; `VoucherTypeResolver.cs:58`. Also excluded from the Day-Book Alt+A picker (`MainWindowViewModel.cs:3007`) and the Scenario picker. | An entire declared-complete phase (Phase 8) has an unreachable posting path. |
| **T1-5** | **Voucher numbering Manual and None are unreachable.** `MethodDisplay` is a read-only string with no setter; the Voucher No. on the entry screen is a `<Run>` inside a `TextBlock`, not a TextBox. | `VoucherNumberingConfigViewModel.cs:115`; `MainWindow.axaml:2056, 3544, 3879, 4104`; seed hard-codes Automatic for all 23 types. Confirms IV-13. | Cannot match a pre-printed book, cannot continue an existing numbering series. |
| **T1-6** | **Company creation captures one field: Name.** FY is hard-coded to 1-Apr of the current year; currency hard-coded to INR. No Alter Company, no Delete Company. | `MainWindow.axaml:228-244` (the entire form); `MainWindowViewModel.cs:815-838`. | **Cannot create a company for a prior financial year** — so no historical book can be entered. Root cause of T0-8. |
| **T1-7** | **Restore is unreachable on a fresh install.** The engine supports restoring a company this machine never had; the screen is gated on an open company. | `MainWindowViewModel.cs:2826-2842`, `:6776`; engine capability at `CompanyBackup.cs:268-270`. | The exact disaster-recovery case backup exists for is the one case it cannot serve. **~half a day.** |
| **T1-8** | **No Tracking Numbers.** Receipt Note↔Purchase and Delivery Note↔Sales cannot be linked. | Zero `TrackingNumber` hits in `src/`. | Order fulfilment cannot be tracked correctly — a named prerequisite for WF-8. |
| **T1-9** | **71 of 77 report surfaces are dead ends.** 6 of 45 `ReportKind` values drill; 0 of 32 dedicated report Screens drill. | `ReportsViewModel.cs:1093-1120`; `MainWindowViewModel.cs:2083-2100`. **This corrects IV-19, which says "~50" and counts only the separate Screens — it understates its own defect by ~40%.** | Drill-down is the single most-used gesture in Tally. It is absent from 92% of our report surface. |
| **T1-10** | **32 of 77 report surfaces cannot be printed at all; 22 of those have no export either.** | `IsPrintablePage = IsReportContext \|\| VoucherDetail`, and `IsReportContext` requires `Reports != null`, which is null on all 32. **[V]** `MainWindowViewModel.cs:2214, 2227`. | A user cannot get Outstandings, BRS, Cost, Budget Variance, GSTR-4/9/9C, ITC or challan-recon out of the app in any form. |
| **T1-11** | **No GSTR-1 or GSTR-3B JSON — the two returns that actually get uploaded.** And the five return writers that *do* exist are dead code. | **[V]** `GstReturnJson` has zero production callers: the only references in `src/` are two doc comments (`Gstr1.cs:93`, `EInvoiceJson.cs:13`). No GSTR-1/3B emitter exists anywhere. | GST filing is impossible from the app. Contradicts plan.md:85 and :428. |
| **T1-12** | **GSTR-1 is missing 7 form tables** (5 B2CL, 6A exports, 6B SEZ, 6C deemed, 7 B2CS keyed on Place of Supply, 8 nil/exempt four-way split, 13 documents issued); **GSTR-3B is missing 5** (the 3.1 four-way split, 3.1.1, 3.2, 5, 5.1). `Gstr1B2CRow` has no Place-of-Supply member at all. | `Gstr1.cs:42, 171-180`; `Gstr3b.cs:22-31`. Our own A14-confirmed comment at `Gstr1Amendments.cs:70` proves tables 5 and 6A exist on the form — the *amendment* rows are modelled while the originals are not. | Returns are structurally incomplete, not merely unverified. |
| **T1-13** | **Export/SEZ/deemed-export: 3 of 5 supply-category enum values are dead.** `ResolveSupplyCategory` never mints SEZ or DeemedExport; Export hard-maps to EXPWP so there is no LUT/without-payment path. None feeds GSTR-1/3B. | `GstEnums.cs:341-346`; `EInvoiceService.cs:86-106`; `EInvoiceJson.cs:156-161` — all three files say so in their own doc comments. | Zero-rated supplies cannot be handled correctly. |
| **T1-14** | **No cheque printing, deposit slip, banking payment advice, cheque register, multi-account printing, delivery challan, reminder letter, or confirmation of accounts.** Cheque printing is *configurable* and cannot be *performed*. | `Ledger.EnableChequePrinting` / `ChequePrintingBankName` exist, persist and round-trip through Canonical XML/JSON, with zero UI and zero consumers in any print path (`Ledger.cs:54-66`; `SqliteCompanyStore.cs:4978-4979`). | Seven standard Tally documents have no code at all. Textbook dead-field trap. |
| **T1-15** | **No F11 Accounting Features group at all.** Bill-wise, interest, cost centres, multi-currency, budgets, credit limits, cheque printing, multi-address have no per-company switch. No `Integrate Accounts with Inventory`. No Accounts-only vs Accounts-with-Inventory maintain mode. | `GstConfigViewModel.cs` — 21 booleans, all statutory plus six inventory. Zero hits for `IntegrateAccountsWithInventory`, `MaintainMode`. | Every one of those capabilities is always-on with no way to turn it off. `Integrate Accounts with Inventory` in particular changes how the Balance Sheet sources closing stock — it belongs in wrong-figures territory, not a checkbox. |
| **T1-16** | **No global F12 configuration tree.** Four context panels only; everywhere else F12 sets a stub string. | `MainWindowViewModel.cs:6628-6653` — the fall-through is literally `Message = "F12 Configure — display options (Phase 1 defaults)."` | Blocks the entire display/entry configuration layer. Entangled with gap-decision D10. |

### TIER 2 — CAPABILITY GAPS WITH NO WRONG FIGURE

| ID | Gap | Evidence |
|---|---|---|
| T2-1 | **Missing report families:** Group Summary; Sales / Purchase / Journal / Credit Note / Debit Note Registers; Statistics; Stock Query; Movement Analysis; Bills Pending; Stock Ageing; Optional / Post-Dated / Cancelled Voucher registers; Cash Flow Projection. | Per-name greps return 0 files each. The three `JournalRegister` hits are all `ReversingJournalRegister`. Note D20 records the CN/DN Register as the corpus-prescribed review path. |
| T2-2 | **Statutory long tail:** ~15 further TDS sections (193, 194, 194B/BB, 194D/DA, 194G, 194IA/IB/IC, 194K, 194LA, 194M, 194N, 194O, 194R, 194S, 195); §206C(1G); Form 27Q; §197 certificates; §234E and §201(1A); Form 16B/26QB; ISD; multi-GSTIN; PF Forms 3A/5/6A/10/12A; ESI Forms 3/5/6; NPS pay head; Labour Welfare Fund; Form 12BA; LUT / shipping bill; Bill of Entry; GST refunds (RFD-01/11). | Per-term greps in the statutory mapping brief. This is tonnage, not difficulty — and it is most of the tonnage. |
| T2-3 | **Master-layer gaps:** Credit Limits; Multi Address (company and ledger); per-item Alternate Units; cost-centre Budgets; Group behavioural flags (sub-ledger, nett debit/credit, used-for-calculation, allocation method — Tally field names **UNVERIFIED** against this corpus); GST Classification master; Show Inactive; multi-master create (Multi Ledger / Multi Group / Multi Stock Item); ledger Alias in the UI; `Inventory values are affected?`. | Zero grep hits for each identifier. |
| T2-4 | **Print engine floor.** `PdfWriter` has only `Text` and `Line`, standard-14 Helvetica, WinAnsi/Latin1. No image or raster primitive, no font embedding. | `PdfWriter.cs:30-70, 83-84, 97, 128-135`. **Consequence: no logo, no QR, no barcode, no non-Latin script, no JPEG export — ever — without replacing the writer.** Collides with the no-NuGet DP-2 constraint. |
| T2-5 | **No physical printing.** Zero hits for `PrintDialog`, `PrinterSettings`, `System.Drawing.Printing`, `winspool`. "Print" means render-a-PDF-and-save-a-file. | Disclosed at plan.md:378, but the P key, the `Screen.PrintPreview` name and the button label all read as printing. |
| T2-6 | **Export/import format gaps:** report export is Csv/Xlsx/Pdf only (Tally offers 7); no HTML, JPEG, XML, JSON or ASCII for a report; no Tally-XML or SDF reader so no third-party Tally data can be ingested; no Excel import; no live SMTP send. | `ExportConfig.cs:5-10`; `ImportDataViewModel.cs:14-23`. HTML and SMTP disclosed as deferred at plan.md:388-391. |
| T2-7 | **Missing TallyPrime UX:** Go To (Alt+G) and Switch To (Ctrl+G); graphical dashboard / any chart at all; More Details; the standard report button bar (Change View, Basis of Values, Monthly Summary, Value Range, Scale Factor, Vertical Balance, number-of-decimals, Alt+U Unhide). | Zero `Key.G` occurrences in `src/Apex.Desktop`; zero `BarChart`/`PieChart`/`graphical`/`MoreDetails` hits; per-term greps return 0 for the button bar. |
| T2-8 | **Keyboard: KB-3 prefix type-to-filter is not built.** Three design rounds NOT-READY; no filtering code in `src/` at all. S5 shipped type-to-*jump*. | plan.md:551-557. Lowest business harm in the register, and correctly last. |

### TIER 3 — REGISTER AND PLAN FALSEHOODS

These harm the project rather than the business, but they are the reason nobody knew the size of the gap. Each needs correcting in place.

> ### 🔴 † 2026-08-15 — TWO FIGURES ATTRIBUTED TO THIS SECTION ARE NOT IN IT
> `plan.md:359` and `:365` state that **"the census found 34 false claims"** and **"nine internal
> contradictions"**, and point at *"Census §2 Tier 3"*. **Neither figure exists in this document.** Measured
> 2026-08-15: `grep -nE "\b34\b" docs/full-clone-census.md` → **zero hits**; `grep -i contradict` → **two**
> incidental hits inside T0-11 and T1-11, neither a count; **the table below has fifteen data rows, not 34 and
> not 9.** The *phase list* `plan.md` gives alongside those numbers **is** real and verifiable in this table
> (Phase 1, 2, 5, 9, 10.9) — **it is only the counts that are unsourced.** ⇒ **Do not repeat either number.**
> `plan.md` is the file that needs correcting, not this one; the exact replacement text is held for whoever owns
> that file. Also recorded at `memory.md:2164` and `memory.md:2185`.
>
> **† Three rows below have been acted on since this census was written (2026-08-10):**
> the **`docs/invented-vs-cloned.md` IV-19 "~50"** row and the **negative-stock "STOPPED AND BANKED"** row were
> both carried into the registers' 2026-08-15 re-verification and are corrected there. The **"24 predefined
> voucher types"** row is untouched — `SeedVoucherTypes.cs:71` still reads `public const int Count = 23`,
> re-measured 2026-08-15, and `plan.md` said 24 in nine places when this was written.
>
> **‡ 2026-08-15 (W0-15 review):** `plan.md`'s live, present-tense counts have since been corrected to 23, and the
> sites that remain at 24 are deliberate — one quotation made in order to retire it, and **CLOSED phases' historical
> records of the count as it stood when they shipped** (the Attendance seed row was deleted on 2026-08-03 by
> `7bfc2c6`, after Phase 10.7 had already shipped against it). Rewriting those would make the record false. Each is
> now exempted per-site, with its occurrence count, in
> `tests/Apex.Ledger.Tests/DocumentCodeAgreementTests.cs`, which fails on any NEW one.

| Claim | Reality |
|---|---|
| "24 predefined voucher types" — plan.md:40, 243, 260, 307, 607, 969, 1076; catalog:187, 519; `VoucherType.cs:6` | **[V]** `SeedVoucherTypes.cs:71` is `public const int Count = 23`. The Attendance row was deliberately removed (decision D24-B). The corpus says 24 for TallyPrime (BOOK p.17) — so this is a **real fidelity gap the docs are hiding**, not a typo. |
| `VoucherType.cs:6` "24 are seeded; custom types may be added" | Both halves false. 23 seeded, and there is no UI anywhere to add a custom type. |
| plan.md:334 Phase 1 (COMPLETE): "multi-create", "delete guards", "Alt+D/Alt+X" | Three falsehoods in one line. Multi-create: 0 hits. No ledger/group delete exists, so there are no delete guards. Alt+D unbound; Alt+X abandons an in-progress entry. |
| plan.md:54, 347 Phase 2 (COMPLETE): "cheque printing" | No renderer exists. Two persisted fields only. |
| `docs/phase5-reports-io-requirements.md:209` RQ-9: "SHALL render ANY REPORT" | **[V]** False by 42% — 32 of 77 surfaces are unprintable. |
| plan.md:378-388: "export PDF/XLSX/CSV/JSON/XML" | Conflates two unrelated surfaces. Report export is `{Csv, Xlsx, Pdf}`; JSON/XML belong to whole-company canonical backup. |
| plan.md:377: "graphical dashboard, More Details" delivered | Zero chart code, zero `MoreDetails` hits. Not previously in any register. |
| plan.md:376: "Account/Inventory Books" families | Ships Cash/Bank/Ledger only. Six registers and five inventory reports absent. |
| plan.md:423-428 Phase 9: "BoE / LUT / shipping bill / SEZ / deemed exports", "GSTR-9A", "per-tax-ledger rounding", "multi-GSTIN", "GSTR JSON" | None exist. GSTR-9A has an engine projection and no Screen, no ViewModel, no menu row. No path posts a Round-Off leg at all (`VoucherPrintProjector.cs:347-350`). |
| plan.md:1076 Phase 10.9: "every one of the 24 voucher types reachable by menu AND shortcut" | 23 types, and Payroll is unreachable on the two IsActive-filtered surfaces. |
| `docs/tally-version-and-voucher-gap-audit.md` §4.1: "masters present and wired to UI" | True for **Create only**. 24 of 27 have no Alter; none of the 27 has Delete or Display. **The single most misleading line in the existing registers.** |
| `docs/invented-vs-cloned.md` IV-19: "~50 reports are dead ends" | 71 of 77. Understates itself by ~40%. |
| plan.md 10.8: negative stock "STOPPED AND BANKED" | **A false claim of absence — the rarer and more dangerous kind.** `Company.WarnOnNegativeStock` shipped, persists and is honoured, with zero UI toggle. Behaviour changed and the register says nothing shipped. |
| gap-audit §4.6: "CN/DN have no menu row", "Ctrl+F7 unbound" | Both STALE/FIXED. Menu rows at `MainWindowViewModel.cs:1002-1003`; Ctrl+F7 bound at **†** `MainWindow.axaml.cs:765` *(was cited `:681`; corrected 2026-08-15 — `:681` is now the bare-E Export arm, and the Ctrl+F7 arm with its grounding comment is `:758-765`)*. |
| `PopulatedCompanyFixture` described as "51 vouchers of every type" | 51 is right; "every type" is not — 8 of 23 base types, zero inventory/order/job-work/POS/payroll vouchers. |

---

## 3. OBSOLETE BY LAW — **USER DECISION REQUIRED, NOT DECIDED HERE**

These 7.2 features exist only to serve pre-GST Indian tax law. Cloning them faithfully would build dead law into a 2026 product. **I recommend, but this section must not be actioned without the user's explicit call.**

| Feature | Status in law | Recommendation |
|---|---|---|
| State VAT — enable, dealer type, TIN, registration date | Subsumed by GST from 2017-07-01 | **Do not build.** |
| VAT/Tax Classifications (`Input VAT @ 4%`, `Output VAT @ 4%`) | Dead; and the UI itself was replaced by "Nature of Transaction" in ERP 9 Rel 5.0, so 7.2's screens differ even from the corpus | **Do not build.** |
| The 2005 four-slab VAT rate structure (1% / 4% / 12.5% / exempt, ~550 categories) | Dead | **Do not build** — this would hard-code 2005 tax law. |
| VAT Composition scheme | Dead | **Do not build.** |
| VAT Reports (Computation + state return forms) | Dead | **Do not build.** |
| Central Sales Tax — 2% interstate, C/F/H declaration forms | Dead | **Do not build.** |
| Service Tax + Form ST3 | Subsumed by GST 2017 | **Do not build.** |
| Excise (7.2's F12 invoice-format route; Excise for Dealers RG23D/Form 2; Excise for Manufacturers) | Central excise on most goods ended with GST | **Do not build.** |
| Fringe Benefit Tax | Abolished by Finance Act 2009; **not in 7.2 anyway** | **Do not build** — listed only so nobody adds it "for completeness". |

**Count: 9 capabilities.** Held out of the 115.

Three things the user should weigh before deciding:

1. **Real TallyPrime still ships these** as downloadable "Extension for Tax" modules (verification report A25, OFFICIAL tallysolutions.com). So "exactly cloned" arguably includes them. My recommendation is still no — they encode repealed rate tables.
2. **TDS and TCS are different and must not be swept in with the above.** The *mechanism* is current law. Only 7.2's *sections, rates, thresholds and return forms* are twenty years stale. **Clone the mechanism, never the numbers.**
3. **A partial option exists:** model VAT/CST/Service Tax as *historical read-only* — enough to display a migrated pre-2017 book, not enough to post new ones. Cheaper than a full clone, and honest about the law.

---

## 4. EXCLUDED BY DECISION — NOT GAPS, MUST NOT BE COUNTED AS SUCH

| Item | Basis |
|---|---|
| TallyVault (company-data encryption) | plan.md:432-440, standing user decision |
| Security Control: users, roles, security levels, password policy | plan.md Phase 10, user decision. R13 confirmed in code — 29 `password` hits are all explicit "no password is stored" notices or the NIC API credential record |
| Tally Audit / Edit Log / audit trail on masters and vouchers | plan.md Phase 10, user decision. **Note for the user, not as a gap:** their own Tally 7.2 has Tally Audit and this build has no equivalent |
| Alter / Delete / Cancel shipping with **no audit trail** | Explicit R12 decision recorded at plan.md Phase 10.11 |
| Split Company Data by financial year | plan.md Phase 10, user decision |
| Repair / Rewrite / Verify company data | plan.md Phase 10, user decision |
| Group Company consolidation | plan.md Phase 10, user decision |
| Hardening / packaging / v1.0.0 release | plan.md Phase 11, user decision |
| Legacy indirect tax stack | plan.md §1.3 out of scope — **and** obsolete-by-law per §3. Excluded twice over |

**Count: 7 capabilities held out of the 115** (Phase 11 is process, not a capability; the legacy stack is counted in §3, not here).

Separately excluded from the denominator as out-of-scope-by-architecture, not by user decision — surfaced here so the user can overrule: Tally.NET, Remote Access, Control Centre, Support Centre, TRiB, SMS, Auditors' Edition, Tally.Server 9, multi-site/rental licensing, TDL, multilingual, international statutory packs. **One deserves a second look: Data Synchronisation's IP mode is self-hosted and needs no Tally.NET server** — if branch-to-HO sync ever matters, that one is buildable.

---

## 5. SEQUENCING PROPOSAL

### The three named blockers, confirmed

1. **No voucher alteration or deletion makes every other defect permanent** (T1-1). Confirmed. **This is the true root of the tree** — every Tier 0 wrong figure is unrecoverable until it lands.
2. **No Order No / Tracking No blocks correct order fulfilment** (T1-8). Confirmed — zero `TrackingNumber` hits.
3. **No master-screen F12 blocks a whole configuration layer** (T1-16). Confirmed, and it is entangled with the *missing F11 Accounting group* (T1-15) — they are one configuration layer, not two.

### Prerequisite graph

```
S0  PopulatedCompanyFixture extension (posts 15 more base types)
      -> prerequisite for HONESTLY REGRESSION-TESTING everything below.
         Currently 8 of 23 base types; no print/export test uses it at all.

S1  Voucher lifecycle (Phase 10.11: alter / delete / cancel / duplicate / insert)
      -> unblocks: recovery from every Tier 0 defect
      -> depends on: S0

S2  Company creation + Alter Company (11 profile fields that ALREADY exist
    on the domain, in the schema, and in the printer)
      -> unblocks: T0-8 blank seller address, prior-FY books, Restore reachability

S3  Voucher Type master + Show Inactive + Voucher Class
      -> unblocks: T1-4 Payroll posting, T1-5 Manual/None numbering,
                   per-type print settings, custom types

S4  Shared report base carrying drill + print + export by construction
      -> unblocks: T1-9 (71 surfaces), T1-10 (32 surfaces), and MUST precede
                   T2-1 so the ~14 new reports are born drillable

S5  PdfWriter image/XObject + font embedding (or a no-NuGet PDF rewrite)
      -> unblocks: T0-9 IRN/QR, company logo, JPEG export, multilingual print,
                   cheque printing layout
      -> HARD DEPENDENCY, 3-6 weeks before any dependent feature starts

S6  F11 Accounting + Inventory Features groups, then global F12 tree
      -> Integrate-Accounts-with-Inventory needs its OWN slice with an oracle
         harness — it changes how the Balance Sheet sources closing stock
```

### Recommended order

**Wave 0 — do these first, they are cheap and they stop active harm.** All are UI over finished plumbing.

| Item | Size | Why first |
|---|---|---|
| T0-7 Bill of Supply routing + `DocumentTitle` field | ~1 day | The screen already computes the answer. Stops issuing illegal documents today. |
| S2 Company Create/Alter screen (11 existing fields) | days | Fixes T0-8 on every future invoice; unblocks prior-FY books. |
| T1-7 Restore reachable from Company Select | ~½ day | Difference between a backup feature and a disaster-recovery feature. |
| T1-11 Wire the 5 orphaned `GstReturnJson` writers to their screens | ~2-3 days | GSTN key schema still needs A14/R7 confirmation first. |
| Negative-stock warn toggle + e-Way config editor | days each | Shipped behaviour with no control surface. |
| Tier 3 doc corrections (23-vs-24, Phase 1/2/5/9/10.9 claims, IV-19's number) | ~1 day | Nothing downstream can be planned honestly until the registers stop lying. |
| **S0 fixture extension** | S | **Highest-leverage single item in the report.** Nothing below is honestly testable without it. |

**Wave 1 — the correctness wave.** T0-1 §194Q excess carve, T0-2/T0-3 valuation (with an oracle harness — see the negative-stock note: three attempts, three unbounded Balance-Sheet errors that each passed the full suite), T0-4 GST hierarchy, T0-10 CN/DN stock parity. Then **S1 voucher lifecycle**, so these fixes are recoverable in existing books.

**Wave 2 — the structural wave.** S3 Voucher Type master. S4 shared report base (drill + print + export in one refactor — they are the same refactor; do not do them separately). S6 F11/F12 configuration layer, with Integrate-Accounts-with-Inventory carved out under its own oracle.

**Wave 3 — breadth.** T2-1 report families (each is a projection over data we already post; the ReportKind+menu+grid pattern is proven 45 times). T1-12/T1-13 GST return completeness. T1-8 tracking numbers and order fulfilment.

**Wave 4 — S5 print engine**, then everything gated behind it: T0-9, cheque printing, multi-account printing, the five missing print documents.

**Wave 5 — T2-2 statutory long tail.** Nothing here is architecturally hard. It is tonnage, and it is most of the remaining tonnage.

---

## 6. WHAT IS STILL UNMEASURED AFTER THIS CENSUS

**The floor is still a floor.** State this plainly to whoever reads next.

1. **Behaviour is unmeasured for 107 of 115 capabilities.** This census measured *existence and reachability*. Only 8 capabilities have any sourced behavioural comparison to Tally (§1.3). A "PRESENT" row means the code is there and a user can reach it — nothing more. Every one of the 42 present rows could still compute the wrong number, and two of them demonstrably do.

2. **Report content and column sets are unmeasured across all 77 surfaces.** Nobody has compared a single Apex report's columns, groupings, totals or ordering against the same report in Tally. The 45 `ReportKind` values and 32 dedicated Screens were counted, not read.

3. **Print layout fidelity is unmeasured.** The renderers were inventoried; not one printed document has been laid against its Tally counterpart. What *is* now known is worse than "unmeasured": the engine is structurally capped (T2-4).

4. **GST return content correctness is unmeasured.** Missing *tables* were counted. Whether the rows that exist carry the right values under the right conditions has never been checked against a filed return.

5. **The 7.2 baseline itself is ~20/90 SECONDARY-sourced.** Course syllabi and blogs, because no official 7.2 documentation is reachable and the install is off limits. Several rows are honestly UNVERIFIED: `INV-VALUATION`, `INV-ACTUAL-BILLED`, `INV-ADDL-COST`, `MSTR-VOUCHER-TYPE`, `MSTR-VOUCHER-CLASS`, `VB-VOUCHER-NUMBERING`, `PRN-CHEQUE`, `PRN-STATIONERY` (marked GUESS at source), `DATA-REWRITE` (GUESS), `DATA-SPLIT`, `RPT-RATIO`, `RPT-CASHFLOW`, `RPT-STOCK-SUMMARY`, `RPT-EXCEPTIONS`, `RPT-COLUMNAR`, Job Costing, Item Cost Tracking.

6. **Several Tally field names in the absent list are UNVERIFIED against this corpus** even though their absence from our code is CONFIRMED: the Group behavioural flags (sub-ledger / nett debit-credit / used-for-calculation / allocation method), Godown "Allow storage of materials", credit limits. R7 grounding must precede any design work on these.

7. **The 8 CANNOT-TELL rows in the table** were never greppted by any agent: Actual-vs-Billed, Additional Cost of Purchase, Transfer Journal, Kerala Flood Cess, payroll job-rates/cost-centre allocation, unified Banking menu, Job Costing, Item Cost Tracking.

8. **Bank statement import arithmetic was not re-verified** — the file exists and the screen is wired (MEDIUM confidence only).

9. **No print or export test uses `PopulatedCompanyFixture`.** Every renderer is locked against thin bespoke fixtures. That is precisely the condition that made the previous sweep undecidable, and it is unchanged.

10. **`docs/invented-vs-cloned.md` §7's unmeasured list is now only partly closed.** Closed by this census: printing and print layouts (structurally, not fidelity-wise), report layouts (existence only), company creation and F11/F12, backup/restore, import/export, POS, banking, security. **Still genuinely unmeasured: GST return *content*, payroll *entry-surface* fidelity, budgets, scenarios, forex, manufacturing, job work, multi-currency.**

---

**Bottom line for the user.** A perfect clone needs roughly 115 named capabilities. We have 42 whole, 44 partial, 21 missing — but only 8 have ever been checked against a source for correctness, so the fidelity denominator is still 107 wide open. The most urgent items are not the missing ones: they are eleven confirmed wrong-money-or-invalid-document defects that a business would suffer today, sitting on top of a book that cannot be corrected because no voucher can be altered or deleted.