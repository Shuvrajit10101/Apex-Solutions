# TallyPrime Feature Catalog — Verification & Completeness Report

Target catalog: `docs/tally-feature-catalog.md` (490 lines)
Compiled: 2026-07-02
Inputs: 4 web-verification topic sets + 2 completeness critiques (Accounting/Banking focus §2–§8; GST/TDS/TCS focus §12–§13).

This report de-duplicates and prioritizes those inputs. Section (A) lists confirmed/corrected facts to apply, with exact old→new text and a source URL where one was provided. Section (B) lists missing features split CORE / PHASE-2 / NICHE. Section (C) lists residual uncertainties needing a human/official check. Section (D) is the highest-value quick-apply edit list.

Items sourced only from model knowledge (a critique with no external URL) are marked **[model-knowledge]**; apply after a spot-check.

---

## (A) Confirmed corrections to apply — grouped by catalog section

### §3 Chart of Accounts — Groups & Ledgers

1. **28 predefined groups (not 29).** If the catalog says "29 predefined groups", replace with "28 predefined groups". The count is 15 Primary + 13 Sub-groups. The bogus 29 arises from treating the Bank OCC A/c alias as its own group.
   Source: https://help.tallysolutions.com/tally-prime/accounting/groups-in-tallyprime/

2. **Bank OCC A/c is an alias, not a separate group.** If both "Bank OD A/c" and "Bank OCC A/c" appear as two rows, collapse to a single row:
   old → two separate group rows for "Bank OD A/c" and "Bank OCC A/c"
   new → `Bank OD A/c (alias: Bank OCC A/c) — Sub-group under Loans (Liability)`
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

3. **Resolve the open ⚠verify flag on Bank OCC A/c** (in §3 and repeated unresolved in §23). Replace the flag with the resolved fact: current TallyPrime ships exactly 28 groups; canonical sub-group name is "Bank OD A/c"; "Bank OCC A/c" is an alternate label, not a 29th group.
   Source: same as #2.

4. **Bank OD A/c parent = Loans (Liability).** Ensure the parent shows "Loans (Liability)", not Current Liabilities or Bank Accounts.
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

5. **Bank Accounts and Cash-in-Hand are SUB-groups of Current Assets, not Primary.** If listed as Primary, change type to "Sub-group", parent "Current Assets". Full Current Assets sub-group set: Bank Accounts, Cash-in-Hand, Deposits (Asset), Loans & Advances (Asset), Stock-in-Hand, Sundry Debtors.
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

6. **The 15 Primary groups (exact set):** Capital Account; Loans (Liability); Current Assets; Current Liabilities; Fixed Assets; Investments; Misc. Expenses (ASSET); Suspense A/c; Branch/Divisions; Sales Accounts; Purchase Accounts; Direct Incomes; Indirect Incomes; Direct Expenses; Indirect Expenses. Anything else (Reserves & Surplus, Secured/Unsecured Loans, Duties & Taxes, Provisions, Sundry Debtors/Creditors, Deposits, Loans & Advances, Stock-in-Hand, Bank OD A/c) must be a Sub-group.
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

7. **The 13 Sub-groups and parents (1/3/6/3 split):** Reserves & Surplus (Capital Account); Bank OD A/c, Secured Loans, Unsecured Loans (Loans (Liability)); Bank Accounts, Cash-in-Hand, Deposits (Asset), Loans & Advances (Asset), Stock-in-Hand, Sundry Debtors (Current Assets); Duties & Taxes, Provisions, Sundry Creditors (Current Liabilities).
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

8. **Do NOT add "Profit & Loss A/c" as a 29th predefined group.** It exists as a reserved account/primary head but is excluded from the 28-group enumeration.
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

9. **Enrichment (optional, confirmed):** add aliases — Reserves & Surplus (alias: Retained Earnings); Misc. Expenses (ASSET) = Miscellaneous Expenses (Asset); Suspense A/c = Suspense Account. Confirm exact spellings "Misc. Expenses (ASSET)" and "Suspense A/c".
   Source: https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Intro_Groups.htm

10. **Stock-in-Hand closing balance is derived, not merely guarded** **[model-knowledge]**. Reframe the §3 "cannot alter closing balance of Stock-in-Hand" delete-guard as: the closing balance is a computed/derived value driven by inventory transactions when "Integrate Accounts & Inventory" is enabled — not a protected-but-editable field.

11. **Identity-preserving rename semantics** **[model-knowledge]**. Add a clone-note: Alter → renaming a Group/Ledger/Voucher Type mutates the master in place and applies retroactively to all historical vouchers (stable ID, name is not the key; not delete+recreate).

### §4 Accounting vouchers

12. **Voucher shortcut corrections (current TallyPrime).** Apply wherever stale Tally.ERP 9 values appear:
    - Debit Note: `Ctrl+F9` → `Alt+F5`
    - Credit Note: `Ctrl+F8` → `Alt+F6`
    - Sales Order: if `Alt+F5` → `Ctrl+F8`
    - Purchase Order: if `Alt+F4` → `Ctrl+F9`
    - Physical Stock: `Ctrl+F7` → `F10 (Other Vouchers) > Physical Stock` (no dedicated function key)
    - Memorandum: `Ctrl+F10` → `F10 (Other Vouchers) > Memorandum`
    - Reversing Journal: direct `F10` → `F10 (Other Vouchers) > Reversing Journal` (F10 opens the Other Vouchers list, not the voucher directly)
    - Material In / Material Out: any dedicated key → `F10 (Other Vouchers) > Material In / Material Out`
    - Job Work In Order / Job Work Out Order: any dedicated key → `F10 (Other Vouchers) > …`
    - Attendance: `Ctrl+F5` → `F10 (Other Vouchers) > Attendance` (and ensure `Ctrl+F5` maps only to Rejection Out)
    Confirmed-correct (no change): Contra=F4, Payment=F5, Receipt=F6, Journal=F7, Sales=F8, Purchase=F9, Receipt Note=Alt+F9, Delivery Note=Alt+F8, Rejection In=Ctrl+F6, Rejection Out=Ctrl+F5, Stock Journal=Alt+F7, Payroll=Ctrl+F4.
    Source: https://help.tallysolutions.com/keyboard-shortcuts-tally-prime/ ; https://help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/

13. **Single-entry is a mode, not the only model** **[model-knowledge]**. §4's single-entry description (Payment: Cr=Account, Dr=Particulars; Receipt/Contra: Dr=Account, Cr=Particulars) should note it is one of two F12-configurable modes ("Use Single Entry mode for Pymt/Rcpt/Contra"); with the toggle off, these use the full Double-Entry Dr/Cr grid like Journal.

14. **Alt+X Cancel vs Alt+D Delete are not interchangeable** **[model-knowledge]**. Alt+X marks the voucher cancelled but keeps its number in sequence and shows it greyed in Day Book (preserves audit trail; meaningful mainly for Automatic numbering). Alt+D deletes and can create numbering gaps. State the differing effect on voucher-number continuity.

15. **Payroll / Job Work voucher types require F11 features** **[model-knowledge]**. Correct any claim that all additional types are reachable via "Show Inactive" alone: Payroll and Job Work types appear only once their F11 features (Payroll, Job Costing/Order Processing) are enabled — unlike Memorandum/Reversing Journal which are hidden-but-present.

### §8 Banking

16. **Bank Allocation does NOT feed statutory challans** **[model-knowledge]**. Correct the §8 claim "feeds BRS and statutory challans". Bank Allocation (Transaction Type + Instrument/Cheque No. + Date + Favouring Name) feeds BRS and cheque printing/deposit slips only. Statutory challan fields (CPIN/CIN/BRN/Challan No.) come from the separate Stat Payment "Ctrl+F Autofill" screen (§12/§13).

### §12 GST

17. **GST ITC set-off order (Rule 88A / Sec 49/49A/49B, eff. 1 Apr 2019).** Replace any "IGST → IGST, then CGST, then SGST in that fixed order" text with:
    "IGST credit is used first (against IGST, then against CGST and/or SGST/UTGST in any order or proportion); CGST credit is used against CGST then IGST; SGST/UTGST credit is used against SGST/UTGST then IGST; CGST and SGST/UTGST credits cannot be cross-utilized. IGST credit must be fully utilized before CGST or SGST/UTGST credit is used."
    Source: https://irisgst.com/utilization-of-input-tax-credit-under-gst-rule-88a-section-49a-and-49b/

18. **GSTR-4 is the ANNUAL composition return** (not quarterly). If described as quarterly, correct: GSTR-4 is annual; the quarterly composition tax is paid via GST CMP-08.
    Source: https://help.tallysolutions.com/tally-prime/india-gst-composition/gstr-4-tally/

19. **GSTR-2A / GSTR-2B are auto-drafted ITC statements, not taxpayer-filed returns.** If labelled "filed returns", correct to: downloaded from the portal and reconciled in TallyPrime (GSTR-2B Reconciliation report). Also resolve the §12 ⚠verify flag — GSTR-2A/2B download-and-reconcile and the HSN summary are confirmed mature features, not open questions.
    Source: https://help.tallysolutions.com/tally-prime/gstr-2b/india-gst-status-gstr-2b-reconciliation-tally/

20. **e-Invoice / e-Way Bill workflow depth is confirmed — remove the ⚠verify.** e-Invoicing works online (Rel 1.1) and offline (Alt+Z Exchange > Send for e-Invoicing > Offline Export → JSON auto-split <2 MB), generates IRN + prints QR, supports IRN cancellation. e-Way Bill works online (Rel 2.0) and offline JSON (works even if TSS expired).
    Source: https://help.tallysolutions.com/generate-irn-print-qr-code/ ; https://help.tallysolutions.com/faq-e-way-bill-tally/

21. **GST slab constant is stale (GST 2.0, eff. 22 Sep 2025)** **[model-knowledge — law change; verify before publishing]**. The catalog's "0 / 5 / 12 / 18 / 28 % + Cess" predates the Sep-2025 reform to a 5% (merit) / 18% (standard) / 40% (demerit) structure (with residual special rates like 3% gold/silver, 0.25% rough diamonds, plus 0% and Cess). Either update to 5/18/40% or explicitly flag the old list as historical/pre-Sep-2025 and state which the clone targets. See §(C) — confirm exact residual-rate table.

### §13 TDS & TCS

22. **Form 26Q/27EQ filing needs FVU validation** **[model-knowledge]**. §13 "Report: Form 26Q (Alt+B to save return)" understates the flow: Tally exports Form 26Q/27EQ in NSDL/Protean File Validation Utility (FVU)-compatible format for mandatory validation before portal upload. Add the FVU export/validation step.

23. **TCS is not a simple "mirror of TDS"** **[model-knowledge]**. TCS is collected by the seller (not deducted by the payer), can trigger at billing OR at receipt of payment, and — as of FY 2025-26 — Section 206C(1H) (TCS on sale of goods) has been repealed/superseded by buyer-side TDS u/s 194Q. Decide whether to model 206C(1H) as current or legacy.

### General / cross-section (features older PDFs miss — all confirmed)

24. **Current version is TallyPrime 7.0 (19 Dec 2025).** If the catalog says the current version is 4.x/5.x, replace with: "Current release: TallyPrime 7.0 (19 Dec 2025); feature baseline should assume Connected GST (5.0), IMS (6.1), Edit Log/audit trail (2.1), dashboards & WhatsApp (4.0), plus 7.0 additions (Auto Backup/TallyDrive, PrimeBanking payments, SmartFind, JSON import/export)."
    Source: https://help.tallysolutions.com/release-notes-tallyprime-7-0/ ; https://help.tallysolutions.com/tallyprime-features-release-wise/

25. **Legacy tax regimes were NOT removed.** If the catalog says VAT/CST/Service Tax/Excise were removed after GST, replace with: "TallyPrime retains VAT, CST, Excise and Service Tax as optional statutory modules (enabled via F11 Company Features); GST is the primary indirect-tax module, but the legacy regimes remain for historical books, migration, and businesses under older frameworks."
    Source: https://tallysolutions.com/features/taxation/ ; https://help.tallysolutions.com/tally-prime/gst-adjustment-transactions/india-gst-transferring-tax-credits-of-vat-eccise-service-tax-to-gst-tally/

### Payroll statutory constants (§ payroll — all confirmed except the EPS/EPF split)

26. **Employer PF split is not a fixed 3.67%.** If the catalog states "Employer = 3.67% EPF + 8.33% EPS", replace with: "EPS = 8.33% of pensionable wages capped at Rs 15,000 (max Rs 1,250/month); employer-EPF = employee-share amount minus EPS (equals 3.67% only when wages = Rs 15,000). A payroll clone should compute EPS = 8.33% × min(wage, 15000) and employer-EPF = (12% × EPF-wage) − EPS, not hardcode 3.67%."
    Source: https://www.epfindia.gov.in/site_docs/PDFs/MiscPDFs/ContributionRate.pdf

27. **Confirmed payroll constants (no change; verify present & correct):** EPF employee 12% of Basic+DA (reduced 10% for <20 employees / sick / Jute-Beedi-Brick-Coir-Guar); EPS 8.33% (employer only, capped Rs 1,250); PF/EPS ceiling Rs 15,000; EDLI 0.50% employer (max Rs 75); EPF admin 0.50% (min Rs 500, EDLI admin NIL since 01-04-2017); ESI employee 0.75% / employer 3.25% (total 4%), ceiling Rs 21,000 (Rs 25,000 disability), threshold 10+; gratuity = (Basic+DA)×15×years/26, cap Rs 20 lakh; professional tax annual cap Rs 2,500 (Article 276).
    Source: https://www.epfindia.gov.in/site_docs/PDFs/MiscPDFs/ContributionRate.pdf ; https://tallysolutions.com/business-guides/esi-contribution-rate-2026-current-percentage-for-employer-employee/ ; https://cleartax.in/s/gratuity-calculator ; https://www.constitutionofindia.net/articles/article-276-taxes-on-professions-trades-callings-and-employments/

---

## (B) Missing features to add

### CORE (MVP)

- **Voucher Class default accounting allocations** — §4 (Voucher Type master). Default ledger + % split, "Default Accounting Allocation for Stock Items / Not Applicable", additional-ledger auto-entries (rounding/freight), exclude common ledgers. Describe what a class configures, not just the field name.
- **Bill-wise "Split" reference** — §5. A single voucher's amount split across several bill references with different due dates/credit periods (or partly against an advance) at entry time.
- **Bank statement auto-import + Auto Bank Reconciliation** — §8. Import CSV/Excel/PDF/OFX/QIF, auto-match instrument no./date/amount, auto-fill Bank Date, auto-create missing vouchers (Banking → Bank Reconciliation → Import).
- **Ledger "Credit Limit" field + PAN/IT No. + MSME/Udyam fields** — §3 (ledger fields). Credit Limit (amount + basis On Balance / On Total Outstanding / On Highest Credit; enforced at entry) is distinct from Default Credit Period.
- **Ledger "Inventory values are affected?" toggle** — §3. Governs whether a Sales/Purchase/Direct-Income/Direct-Expense ledger can be used in Item Invoice mode and participates in stock/COGS valuation.
- **Post-Dated voucher marking (Ctrl+T)** — §4 / §8. Future-dated voucher excluded from ledger/bank balance until its date; currently only in the §21 shortcut table with no functional description.
- **Connected GST / e-invoicing automation** — §12. Auto IRN + signed QR and e-Way Bill from the sales voucher; bulk/offline JSON; IRN cancellation within the window. Currently only "toggle present; workflow depth ⚠verify".
- **GSTR-2A / 2B auto-download + reconciliation** — §12 (Returns & reports). Matched / value-mismatch / only-in-books / only-in-portal summary to verify ITC before filing.
- **QRMP scheme + IFF** — §12 (Enable & configure + Returns). Turnover ≤ Rs 5 cr; IFF B2B upload for months 1–2; upload-status/rejection tracking; Fixed Sum vs Self-Assessment (PMT-06). Periodicity field currently only says Monthly/Quarterly.
- **TDS Section 194Q** — §13 (Nature of Payment master). Buyer-side TDS on purchase of goods (buyer turnover > Rs 10 cr, purchases from a resident seller > Rs 50 lakh); Tally override lets the buyer deduct on a purchase voucher.
- **Section 206AB / 206CCA "Specified Person" higher-rate TDS/TCS** — §13 (near the No-PAN → 20% line). Party-ledger flag for ITR non-filers (2× normal or 5%, whichever higher). Catalog covers only 206AA (No-PAN → 20%).
- **Broader TDS Nature-of-Payment coverage** — §13. Sections beyond 194J: 194C contractors, 194H commission, 194I rent, 194A interest; deduct-at-earlier-of-credit-or-payment; lower/nil-deduction certificate (Form 13/197). State the general threshold/rate table structure.

### PHASE-2

- **Bill Settlement from the Outstandings report** — §5. Ctrl+B "Settle Bill", spacebar multi-select, settle without opening a fresh voucher.
- **Multi-currency ledger mechanism** — §3. "Currency of Ledger" driving voucher entry + rate-of-exchange capture; Forex Gain/Loss ledger; period-end unrealized-forex adjustment.
- **Company-level Edit Log (Release 2.1/3+)** — §18. Field-level before/after tracking on every master and voucher, distinct from and more granular than Tally Audit. (Confirmed feature; see also §(A) item 24.)
- **Party multi-address (Additional Contact/Address Details)** — §3. Multiple billing/shipping addresses per party, selectable at Sales/Purchase entry (Party Details screen).
- **Direct GST-portal integration** — §12 (Returns). GSTIN/UIN verify + auto-fetch party details; one-click upload/file GSTR-1 & GSTR-3B with DSC/EVC; pre-filing exception resolution.
- **GST Rate Setup bulk screen** — §12 (Masters). Display More Reports → GST Reports → GST Rate Setup to mass-update HSN/SAC codes and rates across many items.
- **Multi-GSTIN / branch-wise registration** — §12 (Enable & configure). Separate GSTIN per state/branch under one set of books; per-registration filing + consolidated reporting.
- **Per-tax-ledger GST rounding method** — §12 (Masters, tax ledger fields). Normal / Always Up / Always Down / Not Applicable — a common exact-match discrepancy source.
- **TDS/TCS e-return validation + ancillary forms** — §13. FVU-compatible export of 26Q/27EQ, Form 27A control chart, TDS certificate Form 16A (only Form 27D for TCS currently mentioned).
- **TDS/TCS exception & outstanding reports** — §13. TDS Outstanding, TDS Not Deducted, Late Deduction/Payment, Nature-of-Payment-wise summaries, and TCS equivalents (only Challan Reconciliation Alt+R currently named).
- **GSTR-9A (composition annual)** — §12 (Returns). Add for completeness alongside GSTR-9/9C.

### NICHE

- **e-Payments integration** — §8. Bank-specific payment advice / NEFT-RTGS files or direct online payment initiation for supported banks.
- **Company "Use SMS for voucher/report sharing" + Books-from vs FY-from divergence** — §2. Explain mid-year-adoption effect (opening balances at Books-from, statutory year boundaries at FY-from).
- **GST TDS/TCS under CGST Sec 51/52 (GSTR-7 / GSTR-8)** — §12, cross-ref §13. Government deductors / e-commerce operators; distinct from Income-Tax TDS/TCS.
- **GST Composition transition mechanics** — §12. Switching regular↔composition mid-year; ITC reversal on switching in; stock/ITC treatment on the transition date.
- **GSTR-9C reconciliation statement + GST Annual Computation report** — §12. Audited-financials-to-annual-return reconciliation above the audit turnover threshold (only GSTR-9/9A listed).

### Confirmed enrichments worth adding (features older PDFs miss)

All confirmed against release-wise docs; add as feature bullets where they fit:
- **Edit Log / Audit Trail** (since Rel 2.1) — insert/alter/delete tracking with user/timestamp/version; dedicated "TallyPrime Edit Log" edition exists. Source: https://help.tallysolutions.com/tallyprime-features-release-wise/
- **Connected GST** (since Rel 5.0) — upload/download/file GSTR-1, GSTR-3B, CMP-08 with DSC/EVC; auto GSTR-2A/2B + ITC reconciliation. Source: https://tallysolutions.com/gst-accounting-and-return-filing-software/
- **IMS – Invoice Management System** (since Rel 6.1; extended 7.0) — download supplier invoices, accept/reject/pending, upload, recompute GSTR-2B. Source: https://tallysolutions.com/gst/gst-invoice-reconciliation-tallyprime-ims/
- **Online e-Invoice & e-Way Bill** (Rel 1.1 / 2.0; cancel-on-alteration 7.0). Source: https://help.tallysolutions.com/release-notes-tallyprime-7-0/
- **Graphical Dashboard** (since Rel 4.0) — customizable home dashboards with report tiles. Source: https://help.tallysolutions.com/tallyprime-features-release-wise/
- **WhatsApp sharing** (since Rel 4.0) — send invoices/reports via WhatsApp for Business. Source: same.
- **Multi-tasking via Go To** — start a new task mid-entry, hold in-progress work, run/merge multiple instances. Source: https://tallysolutions.com/tally/tallyprimes-go-to-a-powerful-capability-to-discover/
- **Save View** (since Rel 2.0) — save configured report layouts as reusable named views. Source: https://help.tallysolutions.com/tallyprime-features-release-wise/
- **More Details** side-panel — add occasional optional voucher/master fields without enabling them in the main flow. Source: https://help.tallysolutions.com/voucher-types-tally/

---

## (C) Residual uncertainties still needing an official/human check

1. **Physical Stock shortcut** — one TallyHelp article references Alt+F10 in older contexts; the reliable current path is F10 (Other Vouchers). Treat any single-key claim as uncertain; prefer "F10 (Other Vouchers) > Physical Stock". (Source hedges this itself.)
2. **GST 2.0 exact residual rate table (post 22-Sep-2025)** — the move to 5/18/40% is model-knowledge asserted in the critique with no TallyHelp URL; confirm the exact residual special rates (3% gold/silver, 0.25% rough diamonds, Cess scope) and which slab set the clone should target against an official CBIC/GST-council notification before publishing item (A)21.
3. **PF/EPS ceiling hike** — a proposed rise to Rs 21,000/Rs 25,000 has been discussed (Supreme Court asked govt to decide) but is NOT notified as of 2026-07-02. Keep Rs 15,000; re-check before any release dated later.
4. **Section 206C(1H) status** — critique states it is repealed/superseded by 194Q for FY 2025-26 (model-knowledge); confirm against an official source before removing it from the TCS model.
5. **Model-knowledge behavioral claims** (no external URL) needing a Tally spot-check: single-entry-mode toggle path (F12), Alt+X vs Alt+D numbering behavior, Payroll/Job-Work-requires-F11 availability, Bank Allocation vs Stat-Payment challan split, Stock-in-Hand derived-balance, rename-in-place semantics. Each is individually plausible; verify in-app or against TallyHelp before treating as authoritative.

---

## (D) Quick-apply edit list (highest value first)

1. Fix the group count to **28** and collapse **Bank OD A/c (alias Bank OCC A/c)** into one Loans-(Liability) sub-group; resolve the §3/§23 ⚠verify flag. (A1–A4)
2. Reclassify **Bank Accounts** and **Cash-in-Hand** as **sub-groups of Current Assets**; confirm the full 15-Primary / 13-Sub split. (A5–A7)
3. Apply the **voucher-shortcut corrections** table — especially Debit Note=Alt+F5, Credit Note=Alt+F6, and convert Physical Stock/Memorandum/Reversing Journal/Material/Job-Work/Attendance to "F10 (Other Vouchers) > …". (A12)
4. Rewrite the **GST ITC set-off order** to the Rule 88A "any order/proportion" phrasing. (A17)
5. Set **GSTR-4 = annual**, **CMP-08 = quarterly**; relabel **GSTR-2A/2B** as auto-drafted (not filed); remove the GSTR-2A/2B and e-Invoice/e-Way-Bill ⚠verify flags (confirmed mature). (A18–A20)
6. Update the **current version to TallyPrime 7.0 (19 Dec 2025)** and correct the "legacy tax regimes removed" claim (they remain). (A24–A25)
7. Fix the **employer PF split** to computed EPS/EPF (drop the hardcoded 3.67%). (A26)
8. Flag or update the **stale GST slab constant** (0/5/12/18/28 → GST 2.0 5/18/40) — after the §(C)2 check. (A21)
9. Add the **CORE missing features**: Voucher Class allocations, bill-wise Split, bank statement auto-import/auto-BRS, ledger Credit Limit + PAN/MSME, "Inventory values are affected?" toggle, Post-Dated (Ctrl+T), Connected GST e-invoicing, GSTR-2A/2B reconciliation, QRMP+IFF, TDS 194Q, 206AB/206CCA, broader TDS sections. (B-CORE)
10. Add the confirmed **release-wise enrichments** (Edit Log, Connected GST, IMS, online e-Invoice/e-Way Bill, Dashboard, WhatsApp, Go To, Save View, More Details) as feature bullets. (B enrichments)

---

*Sourcing note: every URL above is drawn verbatim from the supplied verification/critique inputs; none were invented. Items marked **[model-knowledge]** came from a critique with no external URL and should be spot-checked before being treated as authoritative.*
