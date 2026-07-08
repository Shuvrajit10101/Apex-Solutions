# Phase 5 — Reports depth + printing / export / import / email Requirements

> **Authored by A13 (CA / requirements sign-off) + A14 (Tally-domain fidelity), per `plan.md` §5 Phase 5
> and the `/software` lifecycle (requirements → design → …).** This is the up-front requirements slice for
> **Phase 5 (Reports depth + output I/O)**. It fills SRS §4.5 and traces every requirement to the feature
> catalog (`docs/tally-feature-catalog.md`, "catalog §N") and its verification report
> (`docs/tally-feature-catalog-verification-report.md`, "VR item N"). Requirements follow the "good
> requirement" checklist: uniquely identified, atomic, testable, unambiguous, traceable.
>
> **Fidelity & IP discipline (R4/R7):** behaviour below is described in **our own words**, grounded in the
> catalog + the `tally/` corpus (read locally by A14, never reproduced verbatim). The shipped app and code
> — including every **printed, exported, and emailed artefact** — must **never** contain the word "Tally";
> our product is **Apex Solutions**. This is a hard, testable gate for a phase whose whole job is producing
> customer-facing output (ER-11).
>
> **Web-verification note (R7):** unlike Phase 4 (GST law), Phase 5 is **formatting/IO, not statutory law**,
> so it carries **no tax-law facts to web-verify**. The only external facts it leans on are stable, public
> **file-format** specifications (PDF, the OPC/SpreadsheetML `.xlsx` container, RFC 4180 CSV) — cited where
> a requirement depends on the byte format, but these are engineering standards, not drifting law.
>
> **Reading order for a resuming session:** `memory.md` → `plan.md` (Phase 5) → this file.

---

## 1. Purpose & scope

### 1.1 Purpose
Complete the **report surface** and add **output I/O** on top of the framework-agnostic `Apex.Ledger` core
and the existing report projections. After Phase 5 a user can, entirely **offline**:

- **Configure any report** — change its **period / date range**, toggle **detailed vs summary**, apply
  **sort and filter**, add **comparative / columnar** periods, and **save the configured view** for recall;
- **Drill down** consistently from any report figure to its underlying voucher(s), extending the pattern that
  already exists for inventory (`ReportsViewModel.DrillToMovementRequested`) to every report;
- **Print / preview** a **voucher, an invoice, or a report** through a headless-testable page/format model
  that renders to a **PDF document** (the offline definition of "print" — §DP-8);
- **Export** any report / invoice / master list to **PDF, XLSX, CSV, and JSON** with paisa-exact figures
  that match the on-screen numbers byte-for-byte;
- **Import** masters and vouchers from **the app's own JSON/CSV export format** back into the double-entry /
  inventory engine **transactionally and validated**, so a round-trip is lossless and bad data is rejected
  with a message instead of corrupting the ledger;
- **Email** an invoice or report by **composing a message with the exported attachment** (offline compose +
  handoff; actually *sending* over SMTP is a documented, gated deferral — §DP-6).

Everything here is a **read-side / IO extension**: reports stay **pure read-only projections over
already-posted vouchers** (no new mutable "report store" to drift), and import is the **only** write path —
it flows through the *same* posting/validation engine the UI uses, never a back door into SQLite.

Persistence adds SQLite **schema v14** over the current **v13** (core GST; `Schema.cs`
`CurrentVersion = 13`) to store **Saved Views** and an optional **SMTP profile** (§ER-1). No existing table
is rewritten.

### 1.2 In scope (Phase 5) — grounded in plan.md §5 Phase 5, catalog §16 (reports) + §17 (print/export/import/email)

**A. Report configuration & depth (catalog §16).**
- **Period / date-range selection** on every report (F2 = change the "as-of"/single date; Alt+F2 = change
  the reporting **period** range), replacing the current fixed "as-of = last voucher date" behaviour
  (`ReportsViewModel._asOf`). *(catalog §16 "period" — F2/Alt+F2)*
- **Detailed vs summary** explode/condense (Alt+F1) for the hierarchical reports (Balance Sheet, P&L, Trial
  Balance group tree, Stock Summary). *(catalog §16 "detailed/condensed")*
- **Sorting & filtering** (Alt+F12 value/range filter; sort by name/amount) on tabular reports. *(catalog §16)*
- **Comparative / columnar** reports — add a column (Alt+C) for another period / another company scenario;
  auto-column (Alt+N) across periods — for the balance reports and registers that the catalog columnarises.
  *(catalog §16 "comparative/columnar")*
- **Report families breadth** the catalog lists under §16 that we do **not** yet render: **Cash / Funds
  Flow**, **Ratio Analysis** (a small ratios dashboard), and the **Exception reports** (Negative Stock,
  Negative Cash/Bank, Memorandum register, Reversing-Journal register) — added as pure projections. *(catalog
  §16 "Cash/Funds Flow, Ratio Analysis, Exception reports")*
- **Save View** — bind a report's configuration tuple (kind + period + detail level + filters + columns) to a
  name and recall it. *(catalog §16 "Save View"; VR §(B) modern baseline)*
- **Universal drill-down (Enter)** — every report figure descends to its voucher(s), generalising the
  existing inventory drill-down event. *(catalog §16 "Enter"; plan.md §1.1 "drill-down everywhere")*
- **F12 report configuration** — the per-report options panel (e.g. show/hide zero balances, show
  percentages, closing-stock valuation basis on P&L). *(catalog §16 "F12 configure")*

**B. Printing (catalog §17).**
- A **print/preview path** for **(i) a voucher, (ii) a sales/purchase invoice, and (iii) any report**, via a
  **page/format model** (paper size A4/Letter, orientation, margins) that **renders to a PDF document** (the
  offline meaning of "print", §DP-8; an OS print-dialog hand-off is §DP-8's deferred variant).
- An **invoice print format** grounded in a real trading tax-invoice layout: **header** (company name /
  address / GSTIN), **party** (name / address / GSTIN + place of supply), **line items** (description /
  HSN-SAC / qty / rate / discount / taxable value), **GST breakup** (CGST / SGST or IGST, rate-wise), **total
  taxable + total tax + grand total**, **amount in words**, and a **declaration / signature** block.
- **F12 print configuration** (title override, include/exclude company logo, show/hide narration) and
  **multi-copy** marking (Original / Duplicate / Triplicate) for invoices. *(catalog §17 "print config,
  Original/Duplicate/Triplicate")*

**C. Export (catalog §17).**
- **Export of any report, invoice, or master list** to **PDF, XLSX, CSV, and JSON** (§DP-2/DP-3). Exports are
  **paisa-exact** and reproduce the on-screen, configured figures (ER-2/ER-4).
- Export **configuration**: target folder + filename (with an optional timestamp), reusing the report's
  currently-applied period / filter / detail level. *(catalog §17 "export config")*

**D. Import (catalog §17).**
- **Import of masters and vouchers** from the **app's own JSON export** (the canonical round-trip format) and
  from **CSV** for the flat master/voucher cases (§DP-4/DP-5), **into the double-entry / inventory engine**
  through the existing posting + validation path.
- **Validation + duplicate handling** (skip / merge-opening-balance / reject-on-conflict) and a
  **transactional, all-or-nothing** apply that **never partially corrupts** the ledger (ER-6/ER-7). *(catalog
  §17 "import modes: combine opening balance / ignore duplicates / modify")*

**E. Email (catalog §17).**
- **Compose an email** for an invoice or report: recipient(s), subject, body, and the **exported attachment**
  (PDF by default, §DP-6). The composed message + attachment is produced **offline** and handed to the OS
  mail client / saved as an `.eml`; **live SMTP sending is deferred** (§DP-6). *(catalog §17 "email invoice /
  report"; SMTP profile)*

**F. Modern baseline (VR §(B), scheduled by plan.md §5 Phase 5).**
- A **graphical dashboard** (headline tiles + Ratio Analysis) and **Go To** multi-tasking + **More Details**
  side-panel are named in plan.md's Phase-5 module list. **See §1.3** — these are flagged as the
  **thinnest-slice / candidate-deferral** items so Phase 5's core (report config + the four I/O verbs) is not
  starved. *(plan.md §5 Phase 5 "graphical dashboard, Go To multi-tasking, More Details")*

### 1.3 Explicitly deferred / boundary notes (so a gap is not mistaken for a defect)

Stated so the Phase-5 boundary is unambiguous:

- **XML import/export** — plan.md §5 Phase 5 literally names "round-trip **XML** export/import" and
  "PDF/Excel/**XML**/JSON/HTML". **We flag a plan-vs-charter divergence (see the scope-reconciliation box
  below) and recommend JSON as the canonical round-trip format instead of XML** (§DP-4). If the user prefers
  the literal plan text, an XML round-trip is added as a fifth export/import format — flagged, not silently
  dropped.
- **HTML export** — listed in plan.md's format list. **Recommend deferring HTML** to a later polish pass
  (§DP-3): PDF already covers "human-readable, non-editable", and HTML adds a styling surface with low
  incremental value for an offline desktop user. Flagged.
- **Live SMTP send** — actually transmitting the email requires an SMTP server + stored credentials + network
  (against NFR-1 offline-first). **Phase 5 composes + attaches + hands off / saves `.eml`; the live send is a
  documented deferral** (§DP-6). The **SMTP profile** master (server/port/TLS, no password in the repo — R13)
  MAY be captured now so a later phase only wires the transport.
- **OS-native print dialog** — Phase 5's "print" is **render-to-PDF + preview** (§DP-8); driving the platform
  print spooler (Avalonia has no uniform cross-platform print API) is the deferred variant. A user prints by
  opening the produced PDF in their OS viewer and printing from there — fully offline, cross-platform, and
  headless-testable.
- **Bank-statement CSV import already exists** (`BankStatementImportViewModel`, Phase 2) and is **out of this
  phase's import scope** — it is a specialised statement-matcher, not the general master/voucher importer this
  phase builds. Phase 5 does **not** re-touch it.
- **Report families NOT in the catalog's §16 core** (e.g. advanced statutory registers already owned by their
  own phases — GST returns are Phase 4/9, TDS/TCS Phase 7, Payroll Phase 8) are **out of Phase 5**; Phase 5
  only adds the **general** report-config + I/O machinery, which those phase-owned reports then inherit for
  free (ER-5).
- **Digital-signature / password-protected PDF, e-Invoice/e-Way JSON** — out of Phase 5 (e-Invoice/e-Way JSON
  is Phase 9; signed PDF is not in scope).

> **Scope reconciliation (plan.md vs charter) — the divergences to flag for go/no-go (R6/R12).**
> `plan.md` §5 Phase 5 names the export/import format set as **"PDF/Excel/XML/JSON/HTML"** and specifically
> **"round-trip XML export/import"**, whereas the A13 charter for this phase asks for **"PDF + Excel/XLSX +
> CSV/JSON"** export and **CSV (and/or the app's own export format)** import. **Resolution (recommended,
> §DP-2/DP-3/DP-4):** ship **PDF + XLSX + CSV + JSON** as the Phase-5 formats, make **JSON the canonical
> lossless round-trip** import/export format (it maps 1:1 to our domain records and needs no schema
> ceremony), add **CSV** for flat master/voucher import, and **defer XML and HTML** as optional later formats.
> Rationale: JSON round-trips our aggregate exactly with the least code; XML's interchange value is only
> relevant for interop with *other* Tally-format tools, which is out of scope for an original-branded offline
> app; HTML duplicates PDF's read-only role. Both divergences (XML→JSON canonical, HTML deferred) are
> **logged for the user's approval** — if the user wants literal plan fidelity, XML + HTML are added back,
> flagged not silently decided (mirrors the Phase-4 DP-9 handling).

---

## 2. Numbered functional requirements

> Each RQ is testable and cites its catalog/VR origin. "SHALL" = mandatory Phase-5 behaviour. Money is
> **integer paisa** throughout (ER-2); every exported/printed figure derives from the *same* projection the
> screen shows (ER-4), so on-screen and on-paper numbers can never disagree.

### 2.1 Report configuration & depth — catalog §16

- **RQ-1 — Period / date-range selection.** Every report SHALL accept an explicit **reporting period**
  (from-date … to-date) and a single **as-of date**; **F2** changes the as-of date and **Alt+F2** changes the
  period range. The report **engine already takes these parameters** (`*.Build(Company, from, to)` /
  `Build(Company, asOf)`); Phase 5's work is to **expose them in the UI** so the current fixed "as-of = last
  voucher date" default (`ReportsViewModel._asOf`) becomes **user-overridable per report** — reusing the same
  projection, not a new data path. *(catalog §16 "period"; existing `Build` signatures)*
- **RQ-2 — Detailed vs summary.** Hierarchical reports (Balance Sheet, P&L, Trial Balance, Stock Summary)
  SHALL support a **detailed ⇄ condensed** toggle (**Alt+F1**) that explodes/collapses group sub-totals down
  to ledger/item level, without changing the underlying figures. *(catalog §16 "detailed/condensed")*
- **RQ-3 — Sort & filter.** Tabular reports SHALL support **sorting** (by particulars name or by amount,
  asc/desc) and a **value/range filter** (**Alt+F12**, e.g. balances ≥ a threshold, or a name substring),
  applied as a pure post-projection transform so filtered totals stay internally consistent. *(catalog §16
  "sort/filter, Alt+F12")*
- **RQ-4 — Comparative / columnar.** The balance reports and registers the catalog columnarises SHALL support
  **adding a comparative column** (**Alt+C**) for another **period** (and, where meaningful, another
  **scenario** — reusing the existing `SupportsScenario` seam) and **auto-columns** (**Alt+N**) across a span
  of periods. Each column is an independent projection over its own period; columns SHALL align on the same
  particulars rows. *(catalog §16 "comparative/columnar, Alt+C/Alt+N")*
- **RQ-5 — New report families.** The system SHALL add, as pure read-only projections in `Apex.Ledger.Reports`:
  **(a) Cash Flow** and **(b) Funds Flow** (movement of cash / working capital across a period), **(c) Ratio
  Analysis** (a small set of standard ratios — e.g. working-capital, current ratio, gross/net-profit %, drawn
  from the existing BS/P&L projections), and the **Exception reports (d)** Negative Stock, **(e)** Negative
  Cash/Bank, **(f)** Memorandum register, **(g)** Reversing-Journal register. Each SHALL be drill-down-able
  (RQ-7). *(catalog §16 "Cash/Funds Flow, Ratio Analysis, Exception reports")*
- **RQ-6 — F12 report configuration.** Each report SHALL expose an **F12 configuration** panel of its relevant
  options (at minimum: show/hide zero-balance rows; show percentages; for P&L/BS the closing-stock valuation
  basis), applied as pure display transforms. *(catalog §16 "F12 configure")*
- **RQ-7 — Universal drill-down (Enter).** **Enter** on any report figure SHALL open the underlying
  voucher(s), generalising the existing `ReportsViewModel.DrillToMovementRequested` event. Some report rows
  **already carry a voucher identity** (`BankReconciliation`/`InventoryRegisters` rows expose `Guid
  VoucherId`; Stock-Summary rows carry `DrillStockItemId`); the **gap is the accounting reports** (Trial
  Balance / Balance Sheet / P&L / Day Book), whose rows SHALL be extended to carry the identity needed to
  locate their source voucher(s) so the shell opens the target in a new cascading column. A figure that
  aggregates many vouchers SHALL drill to the *list* of those vouchers. *(catalog §16 "Enter"; plan.md §1.1
  "any report figure Enters to its voucher"; existing `VoucherId`/`DrillStockItemId` row carriers)*
- **RQ-8 — Save View.** A configured report (kind + period + as-of + detail level + sort + filter + columns +
  F12 options) SHALL be **saveable under a name** and **recallable**, persisted per company (schema v14,
  ER-1). Recalling a saved view SHALL reproduce the identical configured report. *(catalog §16 "Save View";
  VR §(B))*

### 2.2 Printing — catalog §17

- **RQ-9 — Print/preview a report.** The system SHALL render **any report** to a **paginated printable
  document** (§DP-8: a PDF), honouring a **page model** — paper size (A4 / Letter), **orientation**
  (portrait / landscape), and **margins** — with automatic page breaks, a repeating header (company name +
  report title + period) and a page-number footer. A **preview** SHALL show the exact pages that would print.
  *(catalog §17 "report print, page setup")*
- **RQ-10 — Print/preview a voucher.** The system SHALL render **a single voucher** (accounting or item) to a
  printable document showing its type, number, date, party, entry lines (Dr/Cr), narration, and totals.
  *(catalog §17 "voucher print")*
- **RQ-11 — Print an invoice (tax-invoice format).** For a **sales / purchase item-invoice** the system SHALL
  render a **tax-invoice** document with: **(a)** a header block — company **name / address / GSTIN**;
  **(b)** the **party** block — name / address / **GSTIN** + place of supply; **(c)** an **items table** —
  serial, description, **HSN/SAC**, quantity + unit, rate, discount, **taxable value**; **(d)** a **GST
  breakup** — **CGST + SGST** (intra-state) or **IGST** (inter-state), rate-wise, from the Phase-4 tax
  computation; **(e)** totals — **total taxable, total tax, invoice grand total**; **(f)** the **amount in
  words**; and **(g)** a **declaration + signature** block. Every figure SHALL tie to the posted voucher to
  the paisa (ER-4). *(catalog §17 "invoice print format"; grounded in a real trading tax-invoice)*
- **RQ-12 — Print configuration (F12).** The invoice/report print SHALL offer an **F12 print configuration**:
  **title override**, **include/exclude company logo**, show/hide narration/declaration, and — for invoices —
  a **copy type** marking of **Original for Recipient / Duplicate for Transporter / Triplicate for Supplier**
  (printed as a copy caption). *(catalog §17 "print config, Original/Duplicate/Triplicate")*
- **RQ-13 — De-branded output.** No printed artefact (title, footer, watermark, metadata) SHALL contain the
  word "Tally"; the product reads **Apex Solutions** and the PDF's producer/creator metadata SHALL be an Apex
  string. *(ER-11; project rule)*

### 2.3 Export — catalog §17

- **RQ-14 — Export formats.** The system SHALL export a report / invoice / master list to **PDF, XLSX, CSV,
  and JSON** (§DP-2/DP-3). A **report** exports its currently-configured rows/columns; an **invoice** exports
  the tax-invoice layout (PDF) or its structured line data (XLSX/CSV/JSON); a **master list** (Chart of
  Accounts, ledgers, stock items, parties, voucher types) exports its records. *(catalog §17 "export
  formats/scope")*
- **RQ-15 — Export fidelity (paisa-exact, matches screen).** Every numeric cell in any export SHALL equal the
  on-screen figure **to the paisa** (rupees-and-paisa formatted for PDF/CSV human columns; the underlying
  integer-paisa value preserved exactly in JSON). Re-exporting the same configured report SHALL be
  **deterministic** (byte-stable given the same data + config). *(NFR-3; ER-2/ER-4)*
- **RQ-16 — Export configuration.** An export SHALL let the user choose the **target folder** and **filename**
  (offering a sensible default of `<report>-<company>-<period>.<ext>`, optionally timestamped), and SHALL
  export exactly the **configured** view (period, filters, detail level, columns from §2.1). *(catalog §17
  "export config")*
- **RQ-17 — XLSX is a valid OPC package.** The `.xlsx` export SHALL be a **valid Office Open XML (OPC/ZIP)
  spreadsheet** — a well-formed package (`[Content_Types].xml`, workbook + sheet parts, relationships) that
  opens without repair in a standard spreadsheet application; numbers SHALL be written as **numeric cells**
  (not text) so the recipient can compute on them. *File-format source:* ECMA-376 Office Open XML / OPC
  package structure (a stable public spec, not law). *(catalog §17 "Excel export"; ER-3)*
- **RQ-18 — CSV is RFC-4180 well-formed.** The CSV export SHALL be **RFC 4180**-conformant — comma-separated,
  CRLF-terminated rows, fields containing comma/quote/newline properly double-quote-escaped, a header row of
  column names, and a stable UTF-8 (with-or-without-BOM per §DP-3) encoding. *File-format source:* RFC 4180.
  *(catalog §17 "CSV export")*
- **RQ-19 — JSON is the canonical round-trip format.** The JSON export SHALL serialise the exact domain
  records (masters / vouchers / report rows) with **integer-paisa amounts preserved losslessly** (no float),
  in a **versioned envelope** (`formatVersion`, `schemaVersion`, `company`, payload) so that **importing an
  Apex JSON export reproduces the same data** (RQ-22, round-trip ER-4). *(catalog §17; §DP-4 canonical
  format)*

### 2.4 Import — catalog §17

- **RQ-20 — Import masters & vouchers.** The system SHALL import **masters** (groups, ledgers, stock items,
  parties, voucher types) and **vouchers** from **(a)** an **Apex JSON export** (the lossless round-trip
  path, RQ-19) and **(b)** **CSV** for flat master/voucher lists (§DP-5), applying them through the **same
  posting + master-create engine the UI uses** — never a direct SQLite write (ER-6). *(catalog §17 "import
  masters/vouchers")*
- **RQ-21 — Validation before apply (reject-with-message).** Import SHALL **validate every record before
  applying any**: masters checked for required fields + valid parent/group references; vouchers checked for
  **Σ Dr = Σ Cr**, existing/creatable ledger & item references, valid dates, and (for GST vouchers) the
  Phase-4 tax invariants. On **any** invalid record the import SHALL **reject with a clear per-record error
  message** identifying the row and the reason, and SHALL **apply nothing** (all-or-nothing, RQ-23). *(catalog
  §17 "import validation"; NFR-3 fail-fast; ER-7)*
- **RQ-22 — Lossless round-trip.** Exporting data to **JSON** (RQ-19) and re-importing it into a fresh company
  SHALL reproduce the **identical** masters and vouchers — same figures to the paisa, same references,
  same balance — proving no field is dropped or coerced. This is a **hard, tested gate** (ER-4). *(catalog
  §17 "round-trip"; plan.md Phase 5 exit gate "export/import round-trips losslessly")*
- **RQ-23 — Transactional apply (never corrupt the ledger).** The whole import SHALL run in **one atomic unit
  of work**: it either commits **every** validated record or, on failure, **rolls back to the pre-import
  state** leaving the company byte-for-byte unchanged. A crash mid-import SHALL **not** leave a half-imported,
  unbalanced ledger. *(catalog §17; NFR-8; ER-7)*
- **RQ-24 — Duplicate handling.** For a master/voucher that **already exists** (matched by stable id or
  name+number), the import SHALL offer a **conflict policy**: **skip** (ignore duplicate), **merge opening
  balance** (masters only — add the imported opening balance to the existing), or **reject-the-batch**
  (§DP-5). The chosen policy SHALL be applied consistently and reported in the import summary. *(catalog §17
  "combine opening balance / ignore duplicates / modify")*

### 2.5 Email — catalog §17

- **RQ-25 — Compose email with attachment.** From an open invoice or report the system SHALL **compose an
  email** — recipient(s), subject (defaulted from the document), body, and the **exported attachment** (PDF by
  default; the user MAY choose XLSX/CSV/JSON, §DP-6). *(catalog §17 "email invoice/report")*
- **RQ-26 — Offline compose + hand-off (send deferred).** The composed message + attachment SHALL be produced
  **entirely offline** and **handed off** — either opened in the OS default mail client (`mailto:` + the saved
  attachment) or written as a standard **`.eml`** file the user can send — with the **live SMTP transmission
  explicitly deferred** (§DP-6, NFR-1). No send attempt SHALL block or require the network in Phase 5.
  *(catalog §17; §DP-6; NFR-1)*
- **RQ-27 — SMTP profile capture (no secrets in repo).** The system MAY capture an **SMTP profile** (server,
  port, TLS flag, from-address) as a company/app setting (schema v14) so a later phase can wire the send;
  **no password or credential SHALL be committed to the repo** — a credential, if entered, lives only in the
  local settings store, never in source (R13). *(catalog §17 "SMTP config"; R13/NFR-7)*

### 2.6 Keyboard & navigation — catalog §16/§17, §21; make the display-only shortcut bar real

- **RQ-28 — Wire the P/E/M/O shortcut bar.** The gateway header today shows a **display-only** bar
  `P: Print   E: Export   M: E-Mail   O: Import` (`src/Apex.Desktop/Views/MainWindow.axaml`). Phase 5 SHALL
  make these **live**: **P / Ctrl+P → Print** (RQ-9/10/11), **E / Alt+E → Export** (RQ-14), **M / Ctrl+M →
  E-Mail** (RQ-25), **O / Alt+O → Import** (RQ-20), each acting on the **currently-focused** report / voucher
  / invoice, keyboard-only reachable (NFR-2). *(catalog §17 shortcuts; current `MainWindow.axaml` header)*
- **RQ-29 — Report-config shortcuts.** On an open report the cross-cutting actions SHALL be bound: **F2**
  (date), **Alt+F2** (period), **Alt+F1** (detail/summary), **Alt+F12** (filter), **Alt+C** (new column),
  **Alt+N** (auto-column), **F12** (report config), **Enter** (drill-down), plus a **Save View** action —
  reproduced on the right-hand button panel and reachable without a mouse. *(catalog §16 shortcut set; NFR-2)*
- **RQ-30 — Reports navigation hierarchy.** The new report families (Cash/Funds Flow, Ratio Analysis,
  Exception reports) SHALL nest under the existing **Reports** section in the cascading Miller-column
  hierarchy (e.g. Reports → Statements of Accounts / Exception Reports), never a flat dump. *(plan.md
  professional-hierarchy + cascading-nav rules)*

---

## 3. File-format references (engineering standards, not law) — cited

> **R7 note:** Phase 5 has **no statutory-law facts** to verify. The only external facts are stable, public
> **file-format** specifications, cited here so a format-dependent requirement is grounded. These do not
> drift like tax law; no per-phase re-verification is needed.

| # | Format fact | Source |
|---|---|---|
| F-1 | **PDF** is an open, self-contained document format (ISO 32000); a minimal conforming PDF needs only a header, a body of objects (catalog, pages, page content streams, fonts), an xref table, and a trailer — hand-writable without a library. | ISO 32000-1 / Adobe PDF Reference (public spec). |
| F-2 | **`.xlsx`** is an **Office Open XML** workbook packaged as an **OPC / ZIP** container: `[Content_Types].xml`, a `workbook.xml`, per-sheet `sheetN.xml`, and `.rels` relationship parts. A valid ZIP of those parts opens in any modern spreadsheet app. | ECMA-376 Office Open XML; OPC (ECMA-376 Part 2). |
| F-3 | **CSV** is defined by **RFC 4180**: CRLF line breaks, comma field separator, fields with comma/quote/CRLF wrapped in double-quotes, embedded quotes doubled, an optional header line. | IETF RFC 4180. |
| F-4 | **`.eml`** is the standard **RFC 5322 / MIME** message format (headers + MIME parts); an attachment is a base64 MIME part — a message can be composed to a file offline with no mail server. | IETF RFC 5322 / RFC 2045 (MIME). |

> These citations exist to justify the **hand-rolled** DP defaults (§DP-1/DP-2): PDF and `.xlsx` and `.eml`
> are all writable from their public specs with no third-party dependency, keeping the app self-contained
> (NFR-1). No web-verification of *changing* facts was required for this phase.

---

## 4. Engineering rules (ER)

- **ER-1 — Idempotent v13→v14 migration.** Phase 5 SHALL add SQLite **schema v14** via a `MigrateV13ToV14`
  block that runs inside one transaction bumping `schema_version` to 14, using only new `CREATE TABLE` /
  `CREATE INDEX` (a `saved_views` table keyed by company + name storing the serialised report config; an
  optional `smtp_profile` / app-settings table) — **never** `ALTER`-ing or rewriting existing rows. A fresh
  DB is stamped straight to v14 via the consolidated `CreateV1` DDL, following `MigrateV1ToV2 …
  MigrateV12ToV13` exactly. *(mirrors `src/Apex.Persistence.Sqlite/Schema.cs` `CurrentVersion = 13`)*
- **ER-2 — Paisa-exact everywhere in output.** Every amount in every printed / exported artefact SHALL derive
  from the **INTEGER-paisa** domain value (via `Money`), never a float. Human formats (PDF/CSV columns) format
  the paisa value to rupees-and-paisa deterministically; machine formats (JSON) carry the raw integer paisa
  losslessly. No export SHALL introduce a rounding the screen did not. *(NFR-3; `Money.cs`)*
- **ER-3 — Output writers are pure & headless-testable.** All print/export writers (PDF, XLSX, CSV, JSON) and
  the import reader SHALL live in a **framework-agnostic** layer (in `Apex.Ledger` or a new
  `Apex.Ledger.Io` project with **no Avalonia / no interactive dialog / no clock / no RNG** dependency), taking
  a report/voucher model + config **in** and producing a **`byte[]` / stream out**, so every format is
  **unit-tested by asserting on the produced bytes** (valid-ZIP, parseable-PDF, round-trip-equal-JSON) with no
  UI and no file-picker. The Avalonia layer only chooses a path and writes the stream to disk. *(NFR-6;
  testing.md headless; plan.md §3 framework-agnostic core)*
- **ER-4 — Screen ⇄ paper ⇄ file consistency; JSON round-trip.** Print, export, and screen SHALL all read the
  **same report projection** — there is **one** source of each figure, so they cannot disagree. The **JSON
  export → import** path SHALL be a **proven lossless round-trip** (RQ-22): `import(export(company)) ≡
  company` for masters + vouchers, asserted by a test comparing every field to the paisa. *(RQ-15/RQ-22;
  testing.md)*
- **ER-5 — Config + I/O are report-generic (owned reports inherit them).** The period/filter/detail/columnar
  configuration and the print/export machinery SHALL be **generic over a report model**, so reports owned by
  other phases (GST returns, and future TDS/TCS/Payroll registers) gain print/export/config **without
  per-report code**. Phase 5 builds the mechanism once. *(implementation.md reuse; plan.md phase-owned reports)*
- **ER-6 — Import is engine-routed, never a DB back-door.** Import SHALL create masters and post vouchers
  **through the existing domain services / validators** (the same path the keyboard UI uses) — it SHALL NOT
  `INSERT` directly into SQLite. This guarantees imported data obeys **every** invariant (balance, item-invoice
  pairing, GST, delete-guards) automatically. *(RQ-20; NFR-3; VoucherValidator reuse)*
- **ER-7 — Import is transactional & fail-safe.** Import SHALL be **all-or-nothing** within one unit of work
  (validate-all → apply-all → commit, else roll back), so a rejected or crashing import leaves the company
  **exactly** as before (RQ-21/23). No destructive or partial state SHALL be observable. *(NFR-8; catalog §17)*
- **ER-8 — Hand-rolled, dependency-free writers (self-contained ethos).** Per §DP-1/DP-2, the PDF and `.xlsx`
  writers SHALL be **hand-rolled to their public specs** (F-1/F-2) with **no third-party NuGet dependency**,
  keeping the app self-contained and offline (NFR-1) and matching the project's prior precedent of writing
  container formats by hand. If a DP is later overridden toward a library, it SHALL be a **single,
  well-scoped, offline-capable** dependency, reviewed and recorded. *(NFR-1; §DP-1/DP-2)*
- **ER-9 — Reports stay read-only projections.** Adding config/columns/new families SHALL NOT introduce any
  mutable "report" or "saved-figure" store; **Saved Views persist only the *configuration*** (not the numbers),
  which are always recomputed from posted vouchers, so a saved view can never show stale figures. *(RQ-8;
  ER-7 Phase-4 parallel)*
- **ER-10 — Deterministic, culture-invariant formatting.** All number/date formatting in exports SHALL be
  **culture-invariant** (fixed decimal separator, ISO-8601 dates, fixed `\r\n`) so exports are **byte-stable**
  across machines and locales and the round-trip/byte-assert tests are reproducible in CI. *(RQ-15/18; NFR-3)*
- **ER-11 — No "Tally" in any output.** No printed page, PDF metadata, exported cell/field name, email
  subject/body, `.eml` header, or import/export format identifier SHALL contain the word "Tally"; every
  customer-facing artefact reads **Apex Solutions**. This is a **tested** gate (a grep/assert over produced
  bytes). *(project rule; the strongest concern for an output-producing phase)*
- **ER-12 — Cross-platform, no interactive dialog in the engine.** The IO engine SHALL contain **no** file
  dialogs, message boxes, or OS-print calls (those live only in the thin Avalonia layer), so the whole
  print/export/import path runs on Windows / Linux / macOS and in headless CI identically. *(NFR-5; plan.md
  cross-platform)*
- **ER-13 — Zero regression to Phases 1–4.** Report **figures** SHALL be unchanged — Phase 5 only adds config,
  new families, and output — so **Robert, Bright, and the GST golden set stay green** and every existing
  report renders the same numbers it did before (§6). *(R8; NFR-3)*

---

## 5. Decision points (DP) — defaults recommended for A13/user approval

> Each DP states the ambiguity, our recommended default (grounded in the codebase + the offline-desktop
> ethos + the catalog), and a one-line rationale. **These require the user's approval before implementation**
> (R12).

- **DP-1 — PDF approach: hand-rolled vs NuGet.** *Ambiguity:* write a minimal PDF by hand or take a PDF
  library dependency. **Recommend: hand-roll a minimal PDF writer** (header + objects + xref + trailer, one
  embedded standard font, text + line primitives — F-1) with **no dependency**. *Rationale:* matches the
  self-contained/offline ethos (NFR-1) and the project's prior hand-rolled-container precedent; our documents
  are simple tabular/invoice layouts that need no rich PDF feature; a dependency-free writer is fully
  headless-testable (parse-back assertions) and cross-platform. *(Override path: a single offline-capable PDF
  NuGet if the hand-rolled writer proves too costly — recorded, ER-8.)*
- **DP-2 — XLSX approach: hand-rolled OPC vs library.** *Ambiguity:* build the `.xlsx` OPC/ZIP by hand or use
  a spreadsheet library. **Recommend: hand-roll the OPC package** (`[Content_Types].xml` + `workbook.xml` +
  `sheetN.xml` + `.rels`, zipped with the framework's built-in `System.IO.Compression.ZipArchive` — F-2),
  **no third-party dependency**. *Rationale:* the framework already ships ZIP; a minimal SpreadsheetML writer
  covers our numeric/text grids; keeps the app self-contained (NFR-1) and the output validated by a
  "opens-without-repair / cells-are-numeric" test. This matches the project's noted precedent that a
  hand-rolled `.xlsx` OPC package is viable. *(Override: a single offline library if numeric-format fidelity
  demands it.)*
- **DP-3 — Export format set (and CSV encoding).** *Ambiguity:* which formats ship, and CSV BOM. **Recommend:
  ship PDF + XLSX + CSV + JSON; DEFER XML and HTML** (§1.3 reconciliation); write CSV as **UTF-8 with a BOM**
  (so Excel opens Indian-rupee/₹ text correctly) while remaining RFC-4180 field-wise. *Rationale:* these four
  cover human-readable (PDF), spreadsheet (XLSX), universal-flat (CSV), and lossless-machine/round-trip
  (JSON); XML/HTML add cost without an offline-desktop payoff (flagged for the user against literal plan text).
- **DP-4 — Canonical round-trip format: JSON vs XML.** *Ambiguity:* plan.md says round-trip **XML**; the
  charter says JSON/CSV. **Recommend: JSON is the canonical lossless round-trip format** (versioned envelope,
  integer-paisa preserved — RQ-19); XML deferred. *Rationale:* JSON maps 1:1 to our domain aggregate with the
  least ceremony and the cleanest round-trip test; XML's only advantage (interop with other Tally-format
  tools) is out of scope for an original-branded offline app. *(Flagged for go/no-go; add XML if the user
  wants literal plan fidelity.)*
- **DP-5 — Import scope in Phase 5.** *Ambiguity:* which entities import, and via which formats. **Recommend:
  import (a) all core masters — groups, ledgers, stock items, parties, voucher types — and (b) accounting &
  item vouchers, via JSON (lossless) + CSV (flat); duplicate policy = skip / merge-opening-balance /
  reject-batch (RQ-24).** *Rationale:* this is the round-trip-provable set that exercises the engine end-to-end
  (ER-6); GST/inventory sub-allocations ride along inside the voucher JSON. Statutory-register import stays
  with its owning phase. Bank-statement CSV import is untouched (already exists).
- **DP-6 — Email: compose-now vs SMTP-send-now.** *Ambiguity:* compose + attach only, or wire live SMTP.
  **Recommend: compose + attach + hand-off (open OS mail client / write `.eml`) NOW; DEFER live SMTP send**;
  optionally **capture** an SMTP profile (server/port/TLS/from — no password in repo, R13) so a later phase
  wires transport. *Rationale:* live send needs a server + credentials + network, cutting against offline-first
  (NFR-1); offline compose delivers the user value (an attachable, correct PDF) now and keeps Phase 5
  self-contained and testable. *(Override: wire SMTP now if the user accepts the online dependency.)*
- **DP-7 — Saved-View persistence granularity.** *Ambiguity:* persist views per company or globally; store
  numbers or only config. **Recommend: persist per company (schema v14), storing ONLY the configuration
  tuple** (kind + period + detail + sort + filter + columns + F12 options), never the computed figures (ER-9).
  *Rationale:* figures are always recomputed from posted vouchers, so a saved view can never go stale; per-
  company scoping matches the one-`.db`-per-company tenant boundary.
- **DP-8 — "Print" = render-to-PDF vs OS print dialog.** *Ambiguity:* what "print" means on a cross-platform
  offline app. **Recommend: print = render-to-PDF + on-screen preview** (the user prints the PDF from their OS
  viewer); the OS-native print spooler is a deferred variant. *Rationale:* Avalonia has no uniform
  cross-platform print API; render-to-PDF is cross-platform, headless-testable, and reuses the export PDF
  writer (one code path, ER-4). *(Override: platform-specific print hand-off later if a user needs direct
  spooling.)*
- **DP-9 — Invoice number/amount-in-words + template source.** *Ambiguity:* where the invoice template + the
  amount-in-words come from. **Recommend: a single built-in tax-invoice template (RQ-11) with a
  paisa-accurate Indian-numbering amount-in-words (lakh/crore) generated in the pure IO layer.** *Rationale:*
  one faithful, de-branded template covers the trading-invoice case grounded in a real layout; Indian
  place-value words (e.g. "Rupees One Lakh Twenty Thousand …") are what an Indian tax invoice requires, and
  the converter is pure + unit-testable. *(Custom user templates / multiple formats are a later polish.)*
- **DP-10 — Comparative-column axis in Phase 5.** *Ambiguity:* comparatives across periods only, or also
  companies/scenarios. **Recommend: Phase 5 comparatives span PERIODS (and, where it already exists,
  SCENARIOS via the `SupportsScenario` seam); multi-company comparatives wait for group-company (Phase 10).**
  *Rationale:* period-over-period is the common columnar need and rests on the existing projection + scenario
  machinery; cross-company columns depend on the Phase-10 consolidation boundary. *(Flagged so a missing
  multi-company column is not read as a defect.)*

---

## 6. Bright / Robert / GST-golden impact (Phase 5 must NOT change any figure)

> Phase 5 adds **read-side config + output + import**; it changes **no** posting rule and **no** report
> figure. All existing fixtures SHALL stay green, and Phase 5 adds its own round-trip/output fixtures.

Phase 5 SHALL prove, to the paisa (NFR-3):

- **PR-1 — Robert stays green (accounts-only).** The 13-voucher **Robert** fixture SHALL reproduce its known
  Trial Balance / P&L / Balance Sheet totals **unchanged** under the new configurable report path (the default
  period = its books) — proving report-config refactoring did not move a figure. *(R8; ER-13)*
- **PR-2 — Bright stays green (inventory-integrated).** The **Bright** fixture (derived closing stock ₹15,000,
  BS Stock-in-Hand ₹15,000, gross profit ₹15,000, net profit −₹1,000) SHALL remain green, and its **Stock
  Summary / BS / P&L SHALL export** (PDF/XLSX/CSV/JSON) with figures **matching the screen to the paisa**.
  *(R8; RQ-15)*
- **PR-3 — GST golden set prints & exports.** The Phase-4 **GST golden** invoice(s) SHALL **print as a
  tax-invoice** (RQ-11) with the correct **CGST/SGST or IGST** breakup and grand total, and **export**, every
  figure tying to the posted voucher to the paisa. *(RQ-11/RQ-15)*
- **PR-4 — Lossless JSON round-trip (hard gate).** A fixture company (Robert and/or Bright) exported to
  **JSON** and re-imported into a **fresh** company SHALL reproduce **identical** masters + vouchers — same
  balances, references, and totals to the paisa — satisfying the plan.md Phase-5 exit gate "export/import
  round-trips losslessly". A **deliberately-corrupted** import (unbalanced voucher, missing ledger) SHALL be
  **rejected with a message and apply nothing** (RQ-21/23). *(plan.md Phase 5 exit gate; ER-4/ER-7)*
- **PR-5 — Output writers validated headlessly.** The produced **PDF parses**, the **`.xlsx` opens without
  repair / cells are numeric**, the **CSV is RFC-4180 well-formed**, and **no artefact contains "Tally"** —
  all asserted in xUnit over the produced bytes, no UI. *(ER-3/ER-11; testing.md)*

---

## 7. Sign-off checklist (A13 / CA ticks to close Phase 5)

> One line per RQ-group + the fixture/round-trip gate + the R9/R11 gate items. A13 signs only when **every**
> box is ticked with shown evidence (tests green displayed, app run, review passed, de-branded, committed).

- ☐ **1. Report configuration & depth (RQ-1..RQ-8)** — period/date range (F2/Alt+F2); detailed⇄summary
  (Alt+F1); sort & filter (Alt+F12); comparative/columnar (Alt+C/Alt+N); new families (Cash/Funds Flow, Ratio
  Analysis, Exception reports); F12 report config; universal drill-down (Enter); Save View — implemented,
  tested, catalog-faithful.
- ☐ **2. Printing (RQ-9..RQ-13)** — report / voucher / invoice render-to-PDF with page model; tax-invoice
  format (header/party+GSTIN/items/GST breakup/totals/words/signature); F12 print config + Original/Duplicate/
  Triplicate; de-branded output — implemented and tested.
- ☐ **3. Export (RQ-14..RQ-19)** — PDF/XLSX/CSV/JSON of reports/invoices/masters; paisa-exact matches screen;
  export config; valid OPC `.xlsx`; RFC-4180 CSV; canonical JSON envelope — implemented and byte-asserted.
- ☐ **4. Import (RQ-20..RQ-24)** — masters + vouchers from JSON/CSV via the engine; validate-before-apply with
  per-record messages; lossless round-trip; transactional all-or-nothing; duplicate policy (skip/merge-OB/
  reject) — implemented and tested.
- ☐ **5. Email (RQ-25..RQ-27)** — compose with attachment; offline compose + `.eml`/mail-client hand-off
  (send deferred, DP-6); SMTP profile capture with no secret in repo — implemented and tested.
- ☐ **6. Keyboard & navigation (RQ-28..RQ-30)** — P/E/M/O shortcut bar made live (Ctrl+P/Alt+E/Ctrl+M/Alt+O);
  report-config shortcuts bound; new families nested in the cascading Reports hierarchy — keyboard-only
  reachable.
- ☐ **7. Engineering rules (ER-1..ER-13)** — idempotent v13→v14 migration; paisa-exact output; pure
  headless-testable writers; screen⇄paper⇄file consistency + JSON round-trip; report-generic config/IO;
  engine-routed transactional import; read-only projections (config-only Saved Views); culture-invariant
  formatting; hand-rolled dependency-free writers; no "Tally" in output; cross-platform no-dialog engine; zero
  regression — all satisfied.
- ☐ **8. File-format grounding (§3 F-1..F-4).** PDF / OPC-`.xlsx` / RFC-4180-CSV / RFC-5322-`.eml` — each
  format-dependent requirement cited to its public spec; **no statutory web-verification was required** (this
  phase is formatting/IO, not law) — recorded.
- ☐ **9. Decision points (DP-1..DP-10)** — each default **approved by the user** (R12) before build, or the
  approved variant recorded in `memory.md`; **the plan.md-vs-charter divergences (XML→JSON canonical round-
  trip; HTML/XML deferred; SMTP send deferred; print=to-PDF) explicitly approved** (§1.3 box).
- ☐ **10. Fixture / round-trip gate (PR-1..PR-5)** — Robert & Bright stay green and export to the paisa; GST
  golden prints & exports; **JSON round-trip is lossless and a corrupted import is rejected** (hard gate);
  output writers validated headlessly. *(hard gate)*
- ☐ **11. Tests green — shown.** Full unit + integration suite green (displayed), including Robert, Bright, the
  GST golden set, and the new output/round-trip tests, per R9.
- ☐ **12. Review passed.** Code Reviewer + Verification/Completeness Critic adversarial pass, no open findings,
  fidelity-matrix gap list re-derived (R9/R10).
- ☐ **13. De-branded.** No occurrence of the word "Tally" in any shipped app UI, code, **or produced artefact
  (PDF/XLSX/CSV/JSON/email)**; product reads "Apex Solutions" throughout (project rule) — asserted over
  produced bytes.
- ☐ **14. App run.** The real app launched; a report configured (period/detail/column), an invoice printed to
  PDF, a report exported, and a JSON export re-imported — evidence recorded (R9/R11).
- ☐ **15. Committed by the GitHub Expert.** Small conventional commits tied to Phase-5 plan items, pushed by
  A12 (the **only** git actor, R4); `memory.md` updated (R5), including the DP/divergence decisions.

---

*Traceability: every RQ cites its catalog § (§16 report config/depth; §17 print/export/import/email) / VR
item; every format-dependent RQ cites its public file-format spec (§3 F-1..F-4); ERs cite the `Schema.cs`
(`CurrentVersion = 13` → v14), `Money.cs` integer-paisa, the existing `ReportsViewModel`
(`DrillToMovementRequested`, `SupportsScenario`, `_asOf`) and the display-only `MainWindow.axaml`
`P: Print E: Export M: E-Mail O: Import` bar, and the framework-agnostic-core + NFR conventions; the
fixture/round-trip gate maps to `tests/Apex.Ledger.Tests/Fixtures/{robert,bright}.json` and the GST golden
set. This doc fills SRS §4.5. Any build-time deviation — and the DP / plan-vs-charter decisions — are logged
in `memory.md` with their reason (R6).*
