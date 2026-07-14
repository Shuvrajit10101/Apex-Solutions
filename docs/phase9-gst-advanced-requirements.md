# Phase 9 — GST-Advanced Requirements & Implementation Plan (Apex Solutions, Tally Prime clone)

> **Status:** PLAN-ONLY (requirements + decision-ready plan for user approval per R9/R12). **Nothing here is
> executed.** No application code is written by this document. Schema head is **v37** (Phase 8 Payroll
> complete @ merge `39b31e3` on `origin/main`). Phase 9 adds **v38 → v44** incrementally (additive migrations,
> each folded into the `Apex.Ledger.Io` canonical model in its introducing slice). Governed by CLAUDE.md
> R1–R14; built via `/software`, sequenced by the main loop, executed by named agents (A14 leads law/fidelity;
> A10 adversarial review; A12 GitHub Expert owns all git per R4). Phase 9 branches from the updated `main`
> **only after the user's go-ahead** (the project is paused at the Phase-8→9 boundary awaiting that go-ahead).
>
> **A14 MUST re-verify every rate / threshold / notification number / section-code against official sources
> at Phase-9 kickoff and per slice (R7).** All law in §2 is web-verified as-of the dates shown; treat every
> "⚠ RE-VERIFY" flag as a build-time gate, not a settled fact. GST law is the fastest-drifting corpus in the
> project — **GST 2.0 (eff. 22-Sep-2025)** rationalised the slabs mid-FY, compensation cess is phasing out in
> three dated windows inside FY2025-26, and IMS/2B mechanics changed by notification in Oct-2025 — so
> re-verification is mandatory even where a source is cited. **Where a verifier contradicted a research
> finding, this document follows the verifier and flags the correction inline.**
>
> **Reuse posture:** Phase 9 is the **breadth completion of GST** and deliberately extends machinery already
> shipped — the **Phase-4 GST engine** (`GstService`, `GstLineTax`, `Duties & Taxes` tax ledgers,
> `GstReportSupport`, `Gstr1`/`Gstr3b`), the **Phase-5 `Apex.Ledger.Io`** (JSON+XML canonical model,
> deterministic writers `FvuWriter`/`EcrWriter`/`TabularExport`), and the **Phase-7 stat-payment / challan**
> pattern (CPIN/CIN/BRN, `TdsDepositService`). Advanced GST **extends** these seams (RCM branch in
> `ComputeInvoiceTax`, the dormant `GstTaxHead.Cess` seam, `GstRegistrationType.Composition` stored-but-inert)
> **rather than forking** them.

---

## 1. Scope & Fidelity

### 1.1 What Tally Prime Advanced-GST does (grounded in the catalog + corpus)

Tally Prime consolidates all advanced GST in **one catalog section (§12)** — there are no dedicated
per-feature sections, so every citation below is a §12 sub-heading. The base engine (enable/configure,
masters, GSTR-1/3B, Tax Analysis) shipped in Phase 4; Phase 9 delivers the seven advanced pillars. Defining
behaviours a faithful clone must preserve:

- **RCM (Reverse Charge)** — RCM is **data-driven per master**, not a single switch: on a party/expense/
  purchase ledger the user sets `Is reverse charge applicable = Yes` and the nature-of-supply/HSN carries the
  notified category. On an RCM inward voucher Tally posts **no tax on the supplier value**; it raises the
  **recipient's** liability to a reverse-charge account and books a matching ITC, via **Alt+J Stat
  Adjustment** ("Increase of Tax Liability / Input Tax Credit — Reverse Charge"), surfacing in **GSTR-3B
  3.1(d) + 4A(3)/4A(2)** and **GSTR-1 4B**. It supports the recipient **self-invoice**. *(Catalog §12 —
  Masters reverse-charge flag; Transactions > Scenarios (unregistered/notified/imports/advance-receipt);
  Adjustments Alt+J.)*
- **Composition** — `F11 → GST → Registration Type = Composition` opens composition fields: tax rate for
  turnover (1% traders/manufacturers, 5% restaurants, 6% service providers), basis of computation, and a
  purchase/RCM-rate toggle. The dealer issues a **Bill of Supply** (no tax invoice, cannot collect GST), takes
  **no ITC**, pays quarterly via **CMP-08** and files annual **GSTR-4** (+ **GSTR-9A**). *(Catalog §12 —
  Enable & configure; Returns. Verification-report A18 corrects GSTR-4 = **annual**, CMP-08 = **quarterly**.)*
- **GST Compensation Cess** — a duty ledger under `Duties & Taxes` with `Tax Type = Cess`, valued **on value**
  or **on quantity**, with a cess % on the item/S-P-ledger rate block or a GST Classification; on the invoice
  Tally auto-computes cess alongside CGST/SGST/IGST, shown in **Tax Analysis (Alt+A / Ctrl+I)**. *(Catalog §12
  — Masters (Cess tax type, value/quantity valuation). Catalog is thin on cess specifics — deep fidelity per
  slice from the corpus + CBIC.)*
- **e-Invoice (IRN + QR)** — `F11 → GST → e-Invoicing` sets applicable-from, default report period, and "send
  e-Way Bill along with e-Invoice"; on saving a covered B2B/export/SEZ/RCM sales voucher Tally obtains the
  **IRN + IRP-signed QR** (online via Connected GST, or offline via **Alt+Z Exchange > Send for e-Invoicing >
  Offline Export** → JSON, auto-split under 2 MB, re-import the signed response), prints the IRN/QR, and
  supports **cancellation within 24 h** (no amend-on-IRP). *(Catalog §12 — e-Invoicing fields; verification-
  report A20 clears the depth flag + adds Alt+Z offline JSON + IRN cancellation.)*
- **e-Way Bill** — `F11 → GST → e-Way Bill` sets threshold (default ₹50,000), computation basis, and
  intra-state applicability; the sales invoice's e-Way Bill sub-screen collects transporter ID, mode, vehicle,
  distance, ship-from/ship-to; export **online (Connected/Rel 2.0)** or **offline JSON** (works even if the
  subscription/TSS lapsed), optionally bundled with the e-Invoice. *(Catalog §12 — e-Way Bill threshold/basis;
  verification-report A20.)*
- **GSTR-2A / GSTR-2B reconciliation** — **downloaded, not filed**: the portal statement is fetched/imported
  and **reconciled** against booked purchases into buckets (Matched / value-mismatch / only-in-books /
  only-in-portal), so the user verifies eligible ITC before GSTR-3B. **IMS (Rel 6.1+)** extends this with
  **accept / reject / keep-pending** per supplier invoice and a 2B recompute. *(Catalog §12 — Returns (base
  is a ⚠verify one-liner); verification-report A19 + IMS supply the substance. Deepest fidelity gap of the
  seven — mostly web-sourced, not from the Tally PDFs.)*
- **ITC set-off & GST payment** — period-end output liability is offset against ITC per **Rule 88A / §49A-49B**
  ordering, then net cash is deposited: record set-off via **Alt+J Stat Adjustment (ITC set-off)** and pay net
  cash via **Ctrl+F Autofill > Stat Payment** capturing **CPIN / CIN / BRN**; GSTR-3B shows liability vs credit
  vs net cash. *(Catalog §12 — Adjustments & payment; verification-report A17 + header item 3 give the
  corrected "any order/any proportion" Rule 88A ordering.)*

### 1.2 In scope (Phase 9)

- **GST 2.0 rate framework:** dated rate-history master (slabs **0 / 5 / 18 / 40** + surviving special rates
  **3% / 1.5% / 0.25%** + the retained **28% + cess** tobacco/pan-masala carve-out), effective-date
  resolution by voucher date, RSP-based valuation flag, **GST Rate Setup bulk screen** (mass HSN/rate update —
  plan.md C-6, binding).
- **Compensation Cess:** activate the `GstTaxHead.Cess` seam — dated cess master (ad-valorem %, per-unit/
  quantity, RSP-factor), a cess `AddHead` line, Output/Input Cess ledgers, and a **ring-fenced cess credit
  pool** (cess-against-cess only).
- **RCM:** notified-category masters (13/2017-CT(R), 10/2017-IGST(R) services; 4/2017-CT(R) goods), the §9(4)
  real-estate-promoter path (80%/cement/capital-goods), GTA Annexure-V forward-charge flag, dual-leg posting
  (output liability + input ITC), **cash-only discharge** of the RCM liability, 30/60-day time-of-supply,
  auto **self-invoice (§31(3)(f)/Rule 47A)** + **payment voucher (§31(3)(g)/Rule 52)**, GSTR-3B 3.1(d)/4A
  mapping.
- **Composition:** activate `GstRegistrationType.Composition` (sub-type Manufacturer/Trader/Restaurant/§10(2A)
  service), **Bill of Supply** (output-tax suppressed, ITC blocked), state-driven threshold table
  (₹1.5cr/₹75L/₹50L), tax-on-turnover engine, **CMP-08 (quarterly)** + **GSTR-4 (annual)**; inward RCM still at
  normal rate.
- **e-Invoice:** F11 applicability (AATO > ₹5cr, configurable + override) + typed exemption flags; **INV-01
  JSON** offline export/import (pluggable IRP/GSP adapter, no live creds); store IRN/Ack-No/signed-QR/status;
  24-h cancellation, no amend-on-IRP; GSTR-1 auto-population feed.
- **e-Way Bill:** F11 threshold (₹50,000 default, per-state override) + basis; **EWB-01** Part A/B +
  consolidated **EWB-02**; date-aware consignment-value engine; validity calculator (200 km / 20 km ODC);
  Part-B ≤50 km relaxation; 24-h cancel / 72-h accept-reject; offline JSON export + write-back.
- **GSTR-2A/2B & IMS:** the **first inbound GST data path** — JSON import (2B immutable dated snapshot, 2A
  dynamic), reconciliation engine (Matched / partial / in-2B-only / in-books-only) with configurable
  tolerance, **offline IMS mirror** (Accept/Reject/Pending + deemed-accept + remarks + declared reversal),
  advisory ITC-eligibility view feeding GSTR-3B.
- **ITC set-off & payment:** electronic **Credit Ledger** (four pools: IGST/CGST/SGST/UTGST + ring-fenced
  Cess) + **Cash Ledger** (by minor head), **Rule 88A** deterministic set-off engine (editable Table 6.1,
  provisos non-overridable, Rule 86B 1%-cash cap), **GST payment voucher** — **PMT-06 cash-ledger deposit
  challan** (CPIN→CIN/BRN) for the stat-payment leg and **DRC-03 voluntary-payment / ITC-reversal** as the
  landing instrument for reconciliation-driven reversals — so **GSTR-3B posts real set-off** instead of
  display-only net.
- **Credit / Debit Notes (§34):** GST CDN as a **first-class outward transaction linked to the original
  invoice** — output-tax liability adjusted in the correct direction (credit note ↓, debit note ↑), the
  §34(2) **30-Nov-following-FY** declaration cut-off enforced as a guard, GSTR-1 **Table 9B** feed, and
  e-Invoice coverage of B2B CDN (S4). *(Phase 9 formalises CDN as a GST document — see DP-27 for the
  reuse-vs-newly-built decision; not silently inherited from Phase 4.)*
- **GST on advances (§13):** advance-receipt handling for **services** (goods advances de-taxed by Notn
  66/2017-CT) and for RCM time-of-supply — **Receipt Voucher (§31(3)(d)/Rule 50)** on advance, **Refund
  Voucher (§31(3)(e)/Rule 51)** on non-supply, advance **adjustment against the subsequent invoice**, and
  GSTR-1 **Table 11A/11B** feed. Time-of-supply becomes a posting/document flow, not just a compute.
- **ITC reversal engine & blocked credits:** **§17(5) blocked-credit** classification + a per-purchase
  **ITC-eligibility flag** (motor vehicles, personal consumption, works contract, etc.); **Rule 42/43**
  common-credit apportionment (exempt/non-business turnover ratio, capital-goods 60-month spread); **Rule 37**
  (180-day non-payment reversal + re-availment) and **Rule 37A** (supplier-non-payment reversal), all posting
  to **GSTR-3B Table 4(B)** and reducing the electronic credit ledger, with **DRC-03** as the off-return
  reversal instrument.
- **B2C Dynamic QR (Notn 14/2020-CT):** a **self-generated dynamic (UPI/payment) QR** on B2C invoices gated to
  **AATO > ₹500 cr** — rendered on the invoice, carrying payer/payee/UPI-ID/amount — **entirely distinct from
  the IRP-signed e-Invoice QR** and **never routed through the IRN flow** (see DP-28).
- **Return amendments:** model the **GSTR-1 amendment tables (9A B2B/B2C-large/export-amend, 9C CDN-amend, 10
  B2C-others-amend, 11 advances)** and prior-period GSTR-3B correction, so the RQ-18 "corrections route to
  GSTR-1 amendment" promise has an actual build target (RQ-29).
- **Returns breadth:** QRMP + IFF election; annual returns **GSTR-9 / GSTR-9A / GSTR-9C** (light projections);
  HSN summary (reuse Phase-4).
- **Io losslessness:** every new master + voucher extension + inbound snapshot folded into `Apex.Ledger.Io`
  CanonicalModel/CanonicalMapper (JSON + XML round-trip) **and mirrored all-or-nothing into
  CompanyImportService** — never deferred (the recurring Io-bypass A10 defect class).

### 1.3 Explicitly OUT of scope (deferred / decide at gate)

- **Live GSP round-trips + live returns filing** — no GSTN auto-fetch of 2A/2B, no live IMS accept/reject
  push, no live GSTR-1/3B filing, no GSP integration, no DSC/EVC filing, no WhatsApp sharing. Returns / 2A / 2B
  / IMS are **offline JSON only** (plan.md §1.3, Open Q4). **Exception (§1.5, approved 2026-07-14):** e-Invoice
  IRN and e-Way Bill generation MAY additionally use an **optional live path via the customer's OWN direct NIC
  credentials** (qualifying taxpayers only) alongside the offline JSON baseline; the live GSP/IRP path for
  everything else is architected as a **pluggable `IGstPortalConnector` adapter boundary** (R13, RQ-30, ER-16)
  but not built.
- **Post-1-Feb-2026 tobacco/pan-masala successor levies** — the additional Central Excise / NCCD (Notn
  4/2025-CE) and the **Health Security & National Security Cess** (HSNS Act 2025) are **non-GST central
  levies**; out of scope. The clone stops applying compensation cess per the dated schedule and (DP) may flag
  that a separate excise applies (see §2.2 / DP-4).
- **§9(5) ECO deemed-supplier liability** (passenger transport / restaurant via e-commerce operator) — a
  distinct "deemed supplier pays" mechanism, **not** classical RCM; a separate module (DP-5, recommend
  distinct/deferred).
- **GST TDS §51 (GSTR-7) / GST TCS §52 (GSTR-8)** — decide at DP-20 (recommend **out**; a separate withholding
  module, not core advanced GST).
- **§206C(1G) LRS / overseas-tour TCS** — deferred from Phase 7 (D6); this is **income-tax TCS** (Phase-7
  domain), decide at DP-21 (recommend **out**).
- **Multi-GSTIN / multi-branch registration** — decide at DP-22 (recommend **defer**; the app is single-company).
- **ISD (Input Service Distributor) distribution + GSTR-6** — **mandatory from 01-Apr-2025** (Finance Act 2024
  amendments to §2(61)/§20, inside the target FY2025-26) for distributing common input-service ITC (incl. RCM
  services) across GSTINs via an ISD invoice, filing **GSTR-6** monthly. **Decide at DP-26 (recommend defer** —
  it is tied to multi-GSTIN/DP-22 and the single-company posture, but the mandatory-from date is flagged so it
  is a conscious deferral, **not a silent omission).**
- **Zero-rated refund mechanics** (LUT-vs-with-payment **RFD-01**, and **inverted-duty-structure** refund made
  prominent by GST 2.0's 5%-output / 18%-input compression) — **decide at DP-29 (recommend defer/flag);** the
  zero-rated/SEZ/deemed-export supply *classification* (RQ-11) is in scope, but the *refund application* path is
  gated.
- **Interest / penalty auto-computation** (§50 late RCM cash discharge / late deposit, §16(4) time-bar
  interest) — **decide at DP-34 (recommend flag-only, not auto-calculated);** RCM's hard 30/60-day
  time-of-supply with cash-only discharge makes §50 late-payment interest a realistic FY2025-26 exposure, so
  the flag-only stance is surfaced as an explicit gate rather than buried here (carry-forward if approved).

### 1.4 Fidelity anchors (fixtures)

Phase 9 adds a **GST-advanced fixture** (a small deterministic company exercising: one RCM inward supply with
self-invoice; one cess-bearing line; one composition dealer; one e-Invoice IRN payload; one B2C dynamic-QR
invoice; one e-Way Bill; one imported GSTR-2B reconciled to the purchase register; one Rule-88A set-off; one
§34 credit note; one advance receipt adjusted against an invoice; one Rule-37 ITC reversal via DRC-03), with
exact expected paisa,
kept green in every subsequent phase (R8). The existing **Robert** (accounts-only) and **Bright** (trading)
ledger fixtures **must stay green**, and — critically — the **Phase-4 base-GST fixtures (GSTR-1/3B)** must
stay byte-identical: **advanced-GST off ⇒ ER-13 byte-identical to a v37 Phase-8 company**.

### 1.5 Connectivity posture (approved 2026-07-14)

**Approved amendment to the connection posture (user, 2026-07-14).** Phase 9 was approved at **all**
recommended Decision-Point defaults (DP-1…DP-34) with **one amendment** — the connectivity stance moves from
"offline-only (adapter stubbed)" to a **HYBRID** posture that keeps the wiring for a future full-API (GSP)
integration. This subsection is the authoritative record; **DP-10 / DP-12 / DP-25 are updated to match** (it
supersedes the "Offline JSON paths only" framing of §1.3 **for e-Invoice and e-Way Bill only**).

- **Offline JSON is the baseline for ALL GST features.** Returns / GSTR-1 / GSTR-3B / **GSTR-2A / GSTR-2B /
  IMS** stay **strictly offline** — JSON exported/imported under the **customer's own portal login**, no live
  round-trip.
- **e-Invoice AND e-Way Bill gain an OPTIONAL live path over the CUSTOMER'S OWN direct NIC credentials**
  (**₹0 to NIC**). Only qualifying taxpayers can self-provision — **e-Invoice AATO > ₹500 cr**, **e-Way AATO >
  ₹5 cr**. The app stores the customer-configured **NIC Client-ID / Client-Secret + per-GSTIN API username /
  password** (with **static-IP-whitelist** awareness), **protected-at-rest** (OS keystore / DPAPI — never
  plaintext), and calls the **NIC IRP / e-Way APIs directly**. Offline JSON stays the **default** and the
  **fallback** for taxpayers who do not qualify or do not configure the live path.
- **Future-GSP seam kept, not built.** A clean **pluggable connectivity-adapter interface
  `IGstPortalConnector`** abstracts the transport; implementations are **`OfflineJsonConnector` (default)** +
  **`CustomerNicDirect` (e-Invoice/e-Way live)** plus a **stubbed `GspConnector`**, so a future GSP-partner
  full live integration (returns / 2A / 2B / IMS, and e-Invoice/e-Way for **sub-threshold** taxpayers) can be
  added **without rework**. The GSP integration itself is **NOT built in Phase 9**.
- **Cost basis.** Government / NIC / GSTN charge **₹0 per transaction**; the future GSP route costs
  **~₹9,000–50,000/yr per active GSTIN** (floor ≈ GSTZen 18 paise/doc × 50k/yr minimum); the closest packaged
  analogue is **Tally "Connected GST" via TSS at ₹4,500–13,500/yr per seat**; **offline + customer-own-NIC =
  ₹0 external**.
- **Hard security invariant (ER-16).** The app **NEVER** stores or transmits a **GSP/vendor** credential, and
  **NEVER** holds or auto-enters the customer's GST-portal **login password or DSC** — all portal
  signing/consent uses the taxpayer's **OWN OTP/DSC**; live NIC calls use **only** the customer-configured NIC
  API credentials, stored protected-at-rest.

Cross-references: **DP-10** (e-Invoice connection mode), **DP-12** (e-Way generation mode), **DP-25**
(offline/connectivity posture); the adapter is **RQ-30**, the security rule is **ER-16**.

---

## 2. Web-Verified Statutory Law (FY 2025-26 basis, post GST 2.0) + re-verify flags

> **Target FY assumption:** **FY 2025-26** — the in-force window this phase targets. **GST 2.0 took effect
> 22-Sep-2025 mid-FY**, so several rules have **dated intra-year windows** the engine must select by voucher
> date. All figures below are web-verified as-of the dates shown; **every value is config-driven / dated /
> seedable, never hardcoded** (mirror `GstRateSlab`/`SeedGstRates`). Where the adversarial verifier corrected
> a research finding, the correction is stated and marked **[verifier]**.

### 2.1 GST 2.0 rate structure — as-of 2026-07-14 **[verifier-confirmed]**

| Rate | Class | Notes |
|---|---|---|
| **0%** | NIL / exempt | Continues. |
| **5%** | Merit | ~99% of former 12% items moved here. |
| **18%** | Standard | ~90% of former 28% items moved here; coal 5%→18%. |
| **40%** | Special / de-merit | Plain GST (normal cross-utilisable ITC), **NOT a cess** — luxury cars, SUVs, motorcycles >350cc, aerated/sugary drinks, yachts, betting/gaming, etc. |
| **3% / 1.5% / 0.25%** | Special (retained) | 3% bullion/jewellery (making charges 5%); 1.5% cut & polished diamonds; 0.25% rough diamonds. **Still in force alongside the slabs.** |
| **28% + Compensation Cess (RSP-based)** | Tobacco carve-out | pan masala, gutkha, cigarettes, chewing/unmanufactured tobacco, bidi — **did NOT move to 40% on 22-Sep-2025**; continue at **28% + cess on Retail Sale Price** until the compensation-cess loan is discharged; transition date to be notified by the FM/Council. |

- **The app's modelled slabs `0/5/18/40` are correct** for the main ad-valorem structure, but full fidelity
  **also** requires the surviving **3% / 1.5% / 0.25%** special rates and the retained **28%+cess** tobacco
  path. **Do NOT delete the legacy 12% / 28% rates** — mark them **inactive-by-date** so historical
  (pre-22-Sep-2025) vouchers still reprint (Tally keeps rate history).
- **⚠ RE-VERIFY:** the **40% de-merit list is HSN-specific** (not a "expensive goods" heuristic) — drive it
  from the HSN/rate master. RSP-based valuation (new under GST 2.0) needs a **per-item valuation-basis flag**.
- **Legal instrument:** Notification **09/2025-Central Tax (Rate)** & **09/2025-Integrated Tax (Rate)** dated
  17-Sep-2025 (Schedule III = 40%), eff. **22-Sep-2025**, superseding 01/2017-CT(R). 56th GST Council (PIB
  PRID 2163555, 03-Sep-2025).
- Sources (as-of 2026-07-14): PIB 56th Council <https://www.pib.gov.in/PressReleasePage.aspx?PRID=2163555>;
  GST Council press release <https://gstcouncil.gov.in/sites/default/files/2025-09/press_release_press_information_bureau.pdf>;
  Mondaq GST 2.0 notifications <https://www.mondaq.com/india/tax-authorities/1681366/gst-20-notification-and-other-indirect-tax-developments>.

### 2.2 GST Compensation Cess — three dated windows inside FY2025-26 — as-of 2026-07-14 **[verifier-confirmed]**

The cess **levy legally subsists to 31-Mar-2026** (extended for loan repayment) but the **rate has been driven
to Nil in two steps**, so the engine must select by voucher date across **three windows**:

| Window | Dates | Cess position |
|---|---|---|
| **(a)** | 01-Apr-2025 → 21-Sep-2025 | Old cess schedule on all de-merit goods (cars 1–22%, aerated 12%, coal ₹400/tonne, tobacco, etc.). |
| **(b)** | 22-Sep-2025 → 31-Jan-2026 | **Nil on ALL goods EXCEPT tobacco/pan-masala** (Notn 02/2025-Comp Cess (Rate), 17-Sep-2025, u/s 8(2)). Tobacco/pan-masala **retain old cess** at pre-reform rates. |
| **(c)** | from 01-Feb-2026 | **Nil on everything** incl. tobacco/pan-masala (Notn 03/2025-Comp Cess (Rate), 31-Dec-2025). Replaced by non-GST excise/HSNS + 40% GST. |

- **Valuation is mixed** — the cess engine must support **both**: ad-valorem % on the §15 value **and** a
  specific/quantity component. Cigarettes = **% + specific per 1,000 sticks**; coal = **₹400/tonne**;
  pan masala / chewing tobacco = **RSP-factor per unit** (e.g. pan masala ~0.32R–0.51R, chewing/zarda 0.56R,
  unmanufactured 0.36R, where R = declared RSP).
- **ITC ring-fence (hard invariant):** cess ITC discharges **only** cess output liability — never CGST/SGST/
  IGST, and those credits never pay cess (§11(2) proviso, Compensation Act). A distinct credit-ledger bucket.
- **Keep the cess plumbing even at Nil:** FY2025-26 has real cess in windows (a)–(b), accumulated cess ITC/
  refunds persist, and the Act subsists to 31-Mar-2026. **Do NOT delete cess columns/ledgers.** Stranded cess
  credit at cut-over is unsettled (Supreme Court pending) → **user-configurable** treatment (DP-4).
- **⚠ RE-VERIFY:** the 40% slab **absorbs** the rate burden for most former-cess goods and is **not** a cess —
  keep 40% classified as ordinary CGST+SGST/IGST. A shared "de-merit" flag must **not** re-attach cess to
  40%-slab items after 22-Sep-2025.
- Sources: Notn 02/2025-Comp Cess (Rate) <https://taxguru.in/goods-and-service-tax/notification-no-02-2025-compensation-cess-rate-dated-17th-september-2025.html>;
  Notn 03/2025 (Nil on tobacco 01-Feb-2026) <https://a2ztaxcorp.net/government-notifies-nil-rate-of-compensation-cess-on-pan-masala-and-tobacco-products-w-e-f-1st-february-2026/>;
  India Briefing cess removal <https://www.india-briefing.com/news/gst-cess-removal-india-2025-business-benefits-39879.html/>.

### 2.3 RCM — Reverse Charge — as-of 2026-07-14 **[verifier-confirmed on §9(4) scope]**

- **Two streams:** **§9(3) CGST / §5(3) IGST** (notified categories) and **§9(4) CGST / §5(4) IGST**
  (unregistered supplier → **notified class of registered recipients only**). §24(iii): the RCM person must
  register irrespective of turnover.
- **§9(4) is NOT a blanket "all unregistered purchases" RCM [verifier].** The original blanket 9(4) + the
  ₹5,000/day de-minimis (Notn 8/2017-CT(R)) were **rescinded 01-Feb-2019** (Notn 1/2019-CT(R)). The **only
  class currently notified** under substituted §9(4) is **real-estate promoters** (Notn 7/2019-CT(R),
  01-Apr-2019): RCM on the shortfall where <80% procured from registered suppliers (**18%**), **cement** from
  an unregistered supplier **always** RCM at the applicable cement rate (no 80% test), and capital goods from
  unregistered suppliers. **⚠ Cement RCM rate is date-effective: 28% before 22-Sep-2025, 18% from 22-Sep-2025
  [verifier] — do NOT hardcode 28%.**
- **Notified RCM services** (Notn 13/2017-CT(R) intra / 10/2017-IGST(R) inter): GTA, legal/advocate, arbitral,
  sponsorship, specified govt services, director services, insurance/recovery/DSA agents, security (non-body-
  corporate), motor-vehicle renting to a body corporate, copyright (author/artist), **+ renting of commercial
  immovable property by an unregistered → registered person (18% RCM, Notn 09/2024, eff. 10-Oct-2024)**. Most
  at **18%**; **GTA 5% no-ITC** (or Annexure-V forward-charge 18% with ITC — the **12% option was removed under
  GST 2.0**); motor-vehicle renting **5%**.
- **Import of GOODS is NOT RCM [verifier]:** IGST collected at Customs on the Bill of Entry (§3(7) Customs
  Tariff Act) → **GSTR-3B Table 4A(1)**, never 3.1(d). Only **import of SERVICES** is RCM (IGST, 3.1(d)/4A(2)).
- **Self-invoice §31(3)(f) + Rule 47A:** recipient issues a self-invoice for unregistered-supplier RCM within
  **30 days of receipt** (Notn 20/2024-CT, eff. 01-Nov-2024); **payment voucher §31(3)(g)/Rule 52** at payment.
- **Time of supply:** goods §12(3) = 30-day rule; services §13(3) = 60-day rule (earliest of receipt/payment/
  31st or 61st day).
- **Cash-only discharge [verifier]:** RCM output is excluded from "output tax" (§2(82)), so it **cannot** be
  paid from the electronic credit ledger (§49(4)) — must be paid in **cash**; ITC of the RCM tax is a separate
  downstream credit.
- **Returns:** output → **3.1(d)**; ITC → **4A(2)** (import of services) / **4A(3)** (all other RCM); supplier
  side → **GSTR-1 4B** (zero tax). RCM records **bypass IMS** and go straight to GSTR-3B.
- Sources: GST Council RCM flyer <https://gstcouncil.gov.in/sites/default/files/e-version-gst-flyers/Reverse%20charge%20Mechanism.pdf>;
  Notn 13/2017 list <https://gstcouncil.gov.in/node/4434>; Rule 47A <https://www.taxtmi.com/article/detailed?id=13066>;
  §9(4) scope <https://taxguru.in/goods-and-service-tax/section-94-central-goods-services-tax-act-2017.html>.

### 2.4 Composition scheme (§10 + Rule 7) — as-of 2026-07-14 **[verifier-confirmed; unchanged by GST 2.0]**

| Dealer type | Rate (of turnover) | Base | Threshold (preceding-FY aggregate) |
|---|---|---|---|
| **Manufacturer** (non-excluded goods) | **1%** (0.5% C + 0.5% S) | **Total** turnover in State/UT (incl. exempt) | ₹1.5 cr (₹75 L special states) |
| **Trader / other §10(1)** | **1%** (0.5% + 0.5%) | Turnover of **taxable** supplies only (exempt excluded) | ₹1.5 cr (₹75 L special states) |
| **Restaurant (Sch II 6(b))** | **5%** (2.5% + 2.5%) | **Total** turnover in State/UT | ₹1.5 cr (₹75 L special states) |
| **Service provider / mixed §10(2A)** | **6%** (3% + 3%) | Turnover of taxable supplies | **₹50 L** |

- **8 special-category states at ₹75 L [verifier]:** Arunachal Pradesh, Manipur, Meghalaya, Mizoram, Nagaland,
  Sikkim, Tripura, **Uttarakhand** (note: Assam, HP, J&K are at the general ₹1.5 cr).
- **No tax collection, no ITC**; issues a **Bill of Supply** with the "composition taxable person, not eligible
  to collect tax" declaration; **RCM inward still at normal rates** (never the composition rate).
- **Incidental services** allowance for a §10(1) goods dealer: higher of **10% of turnover or ₹5 L**.
- **Excluded manufacturers** (ineligible): ice cream, pan masala, tobacco, aerated water, fly-ash/building
  bricks & tiles.
- **Returns:** **CMP-08** quarterly (due 18th after quarter) + **GSTR-4 annual** (due **30-June** after FY,
  from FY2024-25 — was 30-April) + **GSTR-9A**. Opt-in **CMP-02** (+ ITC-03 reversal); withdrawal **CMP-04**
  (+ ITC-01).
- **⚠ RE-VERIFY:** the stale CBIC FAQ PDF still shows ₹75 L general + 2% manufacturer — do **not** use it;
  Rule-7 verbatim to be pulled from CBIC when the TLS-blocked portal is reachable.
- Sources: CBIC "GST — An Update" (01-May-2019) <https://cbic-gst.gov.in/pdf/01052019-GST-An-Update.pdf>;
  Notn 14/2019-CT (thresholds) <https://web.lawcrux.com/Web/Assets/Data5t/gt/gtnoti/gtnoti_cgst19_14.htm>;
  §10 CBIC repository (§10(2A) "not exceeding 3%").

### 2.5 e-Invoice (IRN/QR) — as-of 2026-07-14 **[verifier-confirmed threshold ₹5cr]**

- **Applicability: AATO > ₹5 crore** (PAN-level, any FY from 2017-18), enabling **Notn 10/2023-CT (10-May-2023,
  eff. 01-Aug-2023)** amending 13/2020-CT. **Still ₹5 cr in FY2025-26** — the **₹2 cr figure circulating in
  blogs is an unnotified PROPOSAL; do NOT bake it in [verifier].** Keep the threshold configurable.
- **IRN = 64-char SHA-256** hash of {Supplier GSTIN + Doc type + Doc number + FY}; the **IRP-signed QR** carries
  supplier/recipient GSTIN, doc no/date, value, line count, main HSN, IRN. **Never computed/signed locally** —
  the clone renders exactly what the IRP returns.
- **Covered:** B2B, exports (with/without payment), SEZ supplies, deemed exports, RCM (supplier-liable), B2B
  CDN. **Excluded:** B2C, Bills of Supply, ISD, imports. **Exempt classes** (any turnover): SEZ **unit** (NOT
  developer), insurer/bank/NBFC, GTA, passenger transport, multiplex cinema, govt dept/local authority, OIDAR.
- **Cancellation: 24 h only, full-document only**, cancelled doc-no not reusable; **no amend-on-IRP** —
  corrections route through GSTR-1 amendment. **Uppercase doc numbers** before building the IRN (IRP
  case-insensitive from 01-Jun-2025).
- **30-day reporting limit** applies only to **AATO ≥ ₹10 cr** (eff. 01-Apr-2025) — model **independently** of
  the ₹5 cr applicability threshold. GSTR-1 auto-populates from IRN-tagged docs (T+2, by document date).
- **GST 2.0 did NOT change e-invoice applicability or the INV-01 schema** — only the tax **values** flowing
  through (40% slab must be accepted; cess fields trend to zero).
- **Connectivity (hybrid, approved 2026-07-14):** the clone's baseline is **offline NIC-JSON export/import**;
  it **additionally** offers an **OPTIONAL live path over the customer's OWN direct NIC IRP credentials**
  (qualifying self-provisioners, **AATO > ₹500 cr**; creds stored **protected-at-rest** — OS keystore / DPAPI)
  behind the pluggable **`IGstPortalConnector`** adapter (`OfflineJsonConnector` default + `CustomerNicDirect`
  live), with a **stubbed `GspConnector` seam** kept for a future GSP integration (§1.5, RQ-30, ER-16, DP-10).
  IRN/QR are still exactly what the IRP returns — never computed/signed locally.
- Sources: GST Council Notn 10/2023-CT <https://www.gstcouncil.gov.in/node/4365>; GSTN e-invoice FAQ v1.4
  <https://www.gstn.org.in/assets/mainDashboard/Pdf/GST%20e-invoice%20System%20-%20FAQs%20-%20Version%201.4%20Dt.%2030-3-2021.pdf>;
  IRN cancellation <https://einvoice6.gst.gov.in/content/irn-cancellation-all-about-e-invoice-cancellation-in-detail/>.

### 2.6 e-Way Bill (Rule 138) — as-of 2026-07-14 **[verifier-confirmed]**

- **Threshold: consignment value EXCEEDING ₹50,000** (> ₹50,000; ₹50,000.00 itself is NOT covered). Value =
  §15 value **incl. CGST+SGST+IGST+cess** but **excl. exempt-supply portion**. **State intra-state overrides
  exist** (Delhi/Maharashtra/Bihar ₹1 L; Rajasthan ₹1 L / ₹2 L intra-city; WB ₹50,000) — ship a **per-state,
  per-transaction-type override table**, default ₹50,000.
- **Mandatory irrespective of value:** inter-state principal↔job-worker; inter-state handicraft goods.
- **Validity [verifier — use the CBIC "active" 4-row table, NOT the stale portal PDF]:** normal cargo = **1 day
  per 200 km** (or part); **ODC / multimodal-ship = 1 day per 20 km**. "Day" expires at **midnight of the day
  following generation**. (200 km substituted for 100 km by Notn 94/2020-CT, eff. 01-Jan-2021.)
- **Part B** required except intra-state distance **≤ 50 km** (consignor↔transporter or transporter↔consignee).
- **Cancellation 24 h**; other-party **accept/reject 72 h** (else deemed accepted); **base-doc age limit 180
  days**; **extension window** 8 h before/after expiry, **cap 360 days** (eff. 01-Jan-2025). **EWB-02**
  consolidated. **Rule 138E** blocks generation on non-filing (2 periods) — cannot be enforced offline →
  **warning only**.
- **GST 2.0** did NOT touch Rule 138; only the **cess component of consignment value** is date-aware (drop cess
  for non-tobacco goods on/after 22-Sep-2025).
- **⚠ Forward-compat (just outside FY2025-26):** mandatory **Ship-To GSTIN** and voluntary **EWB "closure"**
  eff. **01-Aug-2026** — add the fields now, gate by effective date (DP-12).
- **Connectivity (hybrid, approved 2026-07-14):** baseline is **offline EWB-01/EWB-02 JSON export +
  write-back**; it **additionally** offers an **OPTIONAL live path over the customer's OWN direct NIC e-Way
  credentials** (qualifying self-provisioners, **AATO > ₹5 cr**; creds stored **protected-at-rest** — OS
  keystore / DPAPI) behind the same pluggable **`IGstPortalConnector`** adapter (`OfflineJsonConnector`
  default + `CustomerNicDirect` live), with the **stubbed `GspConnector` seam** kept for a future GSP
  integration (§1.5, RQ-30, ER-16, DP-12).
- Sources: Rule 138 CBIC active repository <https://taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter16/rule138_v1.00.html>;
  Notn 94/2020-CT <https://docs.ewaybillgst.gov.in/Documents/notfctn-94-central-tax-english-2020.pdf>;
  time-limits/extension <https://cleartax.in/s/time-limit-for-e-way-bill-generation>.

### 2.7 GSTR-2A / GSTR-2B & IMS — as-of 2026-07-14 **[verifier-confirmed]**

- **GSTR-2A** = dynamic, real-time auto-drafted inward statement (Rule 60(1)); **GSTR-2B** = **static**, ITC
  gatekeeper generated on the **14th** of the succeeding month (Rule 60(7)), cut-off window 12th of M → 11th
  of M+1. **QRMP** filers get 2B **quarterly** only. 2B is now **sequential** (period N's 2B only after N-1's
  GSTR-3B filed) and **recomputes** on post-14th IMS action.
- **§16(2)(aa) [verifier — ground in the statutory wording]:** ITC only if the supplier **furnished** the
  invoice in GSTR-1 **and it is communicated** to the recipient (i.e. appears in 2B); provisional ITC removed
  (Rule 36(4) → 0% from 01-Jan-2022). Plus §16(2)(ba) (not restricted u/s 38, eff. 01-Oct-2022) and the
  §16(4) outer time-limit.
- **IMS (live 01-Oct-2024; statutory backing §38/Rule 60 from 01-Oct-2025, Notn 16/2025-CT):** each supplier
  record → **Accept / Reject / Pending**; Accept → 2B "ITC Available" + 3B; Reject → excluded; Pending →
  carried forward; **no action = deemed accepted** at 2B generation. **Oct-2025 changes:** credit notes may be
  kept Pending one period/quarter then deemed-accepted; on Accept a CN the recipient may declare **partial/no
  ITC reversal** (remarks mandatory); **Rule 67B** governs supplier re-adjustment on a rejected CN.
- **Records that bypass IMS:** inward RCM (GSTR-1 Table 4B) and §16(4)/POS-ineligible ITC → straight to 3B.
- **Reconciliation buckets:** Matched / Partial-mismatch / **In-2B-only** (portal, not in books) / **In-books-
  only** (supplier not filed → ITC ineligible).
- **Clone posture:** offline **import-and-reconcile** (like Form-26AS / Form-24Q import), **advisory**, no live
  GSTN/IMS API. FY2025-26 tobacco 2Bs still carry a **cess** column.
- Sources: GSTR-2B FAQ <https://tutorial.gst.gov.in/userguide/returns/FAQ_gstr2b.htm>; IMS advisory
  <https://tutorial.gst.gov.in/downloads/news/revised_advisory_on_ims.pdf>; §16(2)(aa)
  <https://cleartax.in/s/gst-section-162aa-avail-itc>; Oct-2025 IMS changes
  <https://a2ztaxcorp.net/major-changes-in-gst-invoice-management-system-ims-from-october-2025-tax-period/>.

### 2.8 ITC set-off order (§49(5), §49A/49B, Rule 88A) — as-of 2026-07-14 **[verifier-confirmed; unchanged by GST 2.0]**

- **Step 1 — IGST credit first, exhausted fully** (§49A): IGST credit → (I) IGST liability [mandatory], then
  (II)/(III) CGST and SGST/UTGST in **any order and any proportion** (Rule 88A relaxes the old rigid
  IGST→CGST→SGST). IGST credit **must be fully exhausted** before touching CGST/SGST credit.
- **Step 2 — own-head priority:** CGST credit → CGST then IGST; SGST/UTGST credit → SGST/UTGST then IGST.
  **CGST↔SGST cross-utilisation is banned** (§49(5)(e),(f)) — no such path may exist in the schema.
  **Proviso (§49(5)(c)/(d)):** SGST credit for IGST only after CGST credit for IGST is exhausted (CGST-before-
  SGST for residual IGST — **non-overridable**).
- **Step 3 — shortfall → electronic cash ledger.** **RCM liability, interest, penalty, late fee = cash only**
  (§49(4)/§2(82)).
- **Compensation cess ITC** = separate ring-fenced pool (cess-against-cess only).
- **Rule 86B (99% cap):** where monthly taxable supply (excl. exempt/zero-rated) > ₹50 L, **min 1% of output
  tax must be paid in cash** — applied **after** set-off (Notn 94/2020-CT, eff. 01-Jan-2021).
- **⚠ RE-VERIFY:** the **portal's default residual-IGST split** (CGST-first vs proportionate) — confirm from a
  GSTR-3B/portal manual and against the Tally corpus (A14) before hardcoding the default (DP-16).
- **Golden invariant (Circular 98/17/2019 illustration):** IGST out 1000/ITC 1300; CGST 300/200; SGST
  300/200 → cash payable = **0** in **both** valid splits (Option 1 leaves ₹100 CGST credit, Option 2 leaves
  ₹100 SGST credit). Encode both as reachable.
- Sources: Circular 98/17/2019-GST <https://cbic-gst.gov.in/pdf/Circular-98-17-2019-GST.pdf>; §49A/49B CBIC
  repository; Rule 88A (Notn 16/2019-CT).

### 2.9 Credit / Debit Notes (§34) — ⚠ RE-VERIFY at kickoff (not in the supplied verifier set)

- **A GST credit/debit note is issued against an original tax invoice** (§34): a **credit note** (§34(1)) where
  the invoice's taxable value or tax **exceeds** the actual (post-supply price reduction, sales return,
  deficient supply), a **debit note** (§34(3)) where it is **less** than actual. Each must **reference the
  original invoice** (or, from Sep-2020, may be **consolidated** against multiple invoices of a party).
- **Output-tax adjustment:** a credit note **reduces** the supplier's output liability — but **only if the tax
  incidence has not been passed on** (unjust-enrichment bar, §34(2) proviso); a debit note **increases** it.
- **§34(2) time-limit (credit notes only):** a credit note that reduces liability must be **declared no later
  than 30-November following the end of the FY** of the original supply, **or** the date of the annual return,
  whichever is earlier. **Debit notes have no issuance cut-off**, but ITC on a debit note is governed by the
  **§16(4)** window keyed to the **debit-note's own FY** (delinked from the original invoice, eff. 01-Jan-2021).
- **Returns:** CDN → **GSTR-1 Table 9B** (registered + unregistered); amendments to CDN → **Table 9C**. B2B CDN
  is a **covered e-Invoice document** (§2.5). Recipient side flows through **GSTR-2B / IMS** (a rejected/partly-
  reversed credit note is the Oct-2025 IMS case, §2.7).
- **⚠ RE-VERIFY:** whether the shipped Phase-4 engine already books CDN as a plain sales/purchase-return
  voucher **without** the §34 original-invoice link, 9B mapping, or 30-Nov guard (DP-27); confirm the current
  GSTR-1 9B/9C JSON schema and the unjust-enrichment declaration wording from CBIC.
- Sources (to fetch at kickoff): §34 CGST Act (CBIC repository); Rule 53 (revised documents); GSTR-1 format
  Table 9B/9C; Notn 01/2021-CT (delinked §16(4) for debit notes).

### 2.10 GST on advances (§12/§13, Rule 50/51) — ⚠ RE-VERIFY at kickoff

- **Services:** time of supply (§13) is the **earliest of invoice-date (if within 30 days) or receipt of
  payment** — so an **advance received for a service is taxable on receipt**. **Goods:** GST on advances was
  **removed by Notn 66/2017-CT** (except composition), so **no tax on goods advances** — model the branch.
- **Documents:** a **Receipt Voucher (§31(3)(d)/Rule 50)** is issued when an advance is received; a **Refund
  Voucher (§31(3)(e)/Rule 51)** if the supply does not happen and the advance is returned; the advance is later
  **adjusted against the tax invoice**. Where the rate/POS is unknown at receipt, treat rate as 18% / supply as
  inter-state per the Rule-50 fallbacks.
- **RCM link:** the §12(3)/§13(3) **30/60-day RCM time-of-supply** (§2.3) is the same advance/earliest-event
  machinery — RQ-25 and RQ-7 share it.
- **Returns:** advances received → **GSTR-1 Table 11A**; advances adjusted → **Table 11B**.
- Sources (to fetch): §13 CGST Act; Notn 66/2017-CT; Rules 50/51; GSTR-1 Table 11.

### 2.11 §17(5) blocked credits + ITC reversal (Rule 37/37A, 42/43) — ⚠ RE-VERIFY at kickoff

- **§17(5) blocked/ineligible ITC:** credit is **barred** on specified inward supplies — motor vehicles (≤13
  seats, save specified uses), food & beverages / outdoor catering / health, membership of clubs, works-
  contract & construction of immovable property (own account), goods lost/stolen/destroyed/written-off, gifts
  & free samples, personal consumption, etc. The clone needs a **per-purchase ITC-eligibility flag / blocked-
  credit determination**, not just the 2B "ITC-avail" flag — ineligible ITC is **not availed** (or availed then
  reversed) and reported in **GSTR-3B Table 4(B)(1)** / disclosed in **4(D)**.
- **Rule 42 (inputs & input services):** where common credit serves both taxable and exempt / non-business use,
  reverse the exempt-turnover proportion **D1** plus the **5% deemed non-business D2**, computed **monthly**
  with an **annual true-up** by 30-Nov following FY.
- **Rule 43 (capital goods):** common capital-goods credit is spread over **60 months** and the exempt-turnover
  share reversed monthly.
- **Rule 37 (180-day non-payment):** if the recipient does **not pay the supplier within 180 days**, the ITC is
  **reversed with interest** and **re-availed** on later payment (no time bar on re-availment).
- **Rule 37A (supplier non-payment):** reverse ITC by **30-Nov** following FY if the **supplier has not filed
  its GSTR-3B / paid tax** by 30-Sep; re-avail when the supplier subsequently pays.
- **Posting:** all reversals hit **GSTR-3B Table 4(B)** and **reduce the electronic credit ledger**; off-return
  reversals land on **DRC-03** (§2.14). The reconciliation engine (RQ-13/14) **surfaces** Rule-37A / IMS-
  rejected-CN candidates but is **advisory** — the actual reversal is posted through the S7 set-off/stat-
  adjustment engine, never auto-posted by the reconciler.
- **⚠ RE-VERIFY:** the current 4(B)(1)/4(B)(2) split and Rule-42 D1/D2 formula wording; whether Rule 37A's date
  anchors moved in any FY2025-26 notification.
- Sources (to fetch): §17(5), §16(2)(c)/(d) CGST Act; Rules 37/37A/42/43; GSTR-3B Table 4 format.

### 2.12 B2C Dynamic QR (Notn 14/2020-CT) — ⚠ RE-VERIFY at kickoff

- **A distinct obligation from the e-Invoice IRP QR:** a registered person with **AATO > ₹500 crore** must show
  a **dynamic QR code on B2C invoices** — **self-generated** (the supplier builds it), carrying **payment
  credentials (payee/UPI-ID/amount)** so the buyer can pay digitally. It is **NOT issued by the IRP**, has **no
  IRN**, and must **never enter the e-Invoice/IRN flow** (that flow excludes B2C entirely, §2.5).
- **Enabling law:** **Notn 14/2020-CT (21-Mar-2020)**, effective (after deferrals via 71/2020 & 89/2020, with a
  penalty waiver up to 31-Mar-2021) — enforcement from **01-Apr-2021**. Deemed compliant where a **dynamic QR
  displaying the supplier's UPI/payment link** is made available and the payment cross-links to the invoice.
- **Exemptions:** insurers/banks/NBFCs, GTA, passenger transport, multiplex admission, OIDAR, and exports (B2C
  exports are treated separately) — mirror the e-Invoice exempt classes plus the CBIC B2C-QR FAQ carve-outs.
- **⚠ RE-VERIFY:** the ₹500-cr threshold and exempt-class list against the latest CBIC B2C dynamic-QR FAQ; how
  Tally renders the UPI QR (static-per-company vs per-invoice dynamic).
- Sources (to fetch): Notn 14/2020-CT & amendments; CBIC "Dynamic QR Code for B2C invoices" FAQ.

### 2.13 ISD (mandatory 01-Apr-2025) + GSTR-6 — ⚠ RE-VERIFY at kickoff

- **ISD became MANDATORY from 01-Apr-2025** (Finance Act 2024 substituting **§2(61)** and **§20**, with Rule 39
  amendments) for a head office **distributing common input-service ITC** — now expressly **including ITC on
  RCM inward services** — across the entity's other GSTINs, via an **ISD invoice**. ISD registers separately
  under **§24(viii)** and files **GSTR-6 monthly (due 13th)**; recipients see it in their 2B.
- **Phase-9 posture:** tied to **multi-GSTIN (DP-22)** and the single-company model — **recommend defer
  (DP-26)** but record the **mandatory-from 01-Apr-2025** date so the deferral is deliberate.
- Sources (to fetch): §2(61)/§20 CGST Act (Finance Act 2024); Rule 39; GSTR-6 format; CBIC ISD advisory 2025.

### 2.14 Payment instruments (PMT-06, DRC-03) + interest §50 — ⚠ RE-VERIFY at kickoff

- **PMT-06** is the **challan to deposit money into the electronic cash ledger** — it generates the **CPIN**,
  and on bank payment the **CIN / BRN**; this is the concrete instrument behind the RQ-22 stat-payment leg
  (previously described only as "CPIN/CIN/BRN").
- **DRC-03** is the form for a **voluntary payment / self-ascertained tax / ITC reversal made outside the
  return** — it is the **landing document** for reconciliation-driven reversals (rejected credit notes, Rule
  37/37A, 2B/IMS mismatches) that cannot be netted in GSTR-3B. **DRC-03A** (2024) links a DRC-03 to a demand.
- **Interest §50:** §50(1) interest on delayed cash payment (incl. late **RCM** discharge / late deposit) and
  §50(3) on wrongly-availed-and-utilised ITC — **flag-only** in Phase 9 (DP-34), not auto-computed.
- Sources (to fetch): Rule 87 (PMT-06); Rule 142(2)/(3) (DRC-03); Circular 172/04/2022 (DRC-03 for reversals);
  §50 CGST Act.

### 2.15 Zero-rated refunds (LUT / RFD-01 / inverted duty) — ⚠ RE-VERIFY at kickoff

- **Zero-rated supply (§16 IGST Act)** = exports + SEZ supplies, effected **either** (a) **under LUT/bond
  without payment of IGST → refund of unutilised ITC**, **or** (b) **with payment of IGST → refund of the IGST
  paid** — both applied via **Form RFD-01**.
- **Inverted duty structure (§54(3)(ii))** refund of accumulated ITC (Rule 89(5) formula) is **newly prominent
  in FY2025-26** because **GST 2.0 compressed many outputs to 5% while inputs remain 18%**, manufacturing
  widespread inverted structures mid-year.
- **Phase-9 posture:** the supply *classification* (zero-rated / SEZ / deemed export) is **in scope (RQ-11)**;
  the *refund application* (RFD-01, LUT register, inverted-duty computation) is **gated at DP-29 (recommend
  defer/flag)** given the target FY.
- Sources (to fetch): §16 IGST Act; §54(3) + Rule 89(5) CGST Rules; RFD-01/RFD-11(LUT) formats.

### 2.16 GSTR-1 / GSTR-3B amendments — ⚠ RE-VERIFY at kickoff

- **GSTR-1 amendment tables:** **9A** (amended B2B / B2C-large / exports), **9C** (amended credit/debit notes),
  **10** (amended B2C-others), **11** (advances 11A received / 11B adjusted). Corrections to a prior period are
  reported in the **current** period's amendment table (invoice-level, one amendment per document).
- **GSTR-1A** (live Aug-2024) lets a filer amend the **same period's GSTR-1 before filing that period's
  GSTR-3B**; **GSTR-3B** corrections otherwise flow through the subsequent period, and Table 4 ITC / 3.1
  liability are increasingly **auto-populated + hard-locked** from GSTR-1/2B.
- **Phase-9 target:** this is the concrete build behind RQ-18's "corrections route to GSTR-1 amendment"
  (no amend-on-IRP) — model the amendment tables + prior-period correction as projections (RQ-29).
- **⚠ RE-VERIFY:** whether GSTR-1A and the GSTR-3B Table-4 hard-lock are in force / mandatory for FY2025-26.
- Sources (to fetch): GSTR-1 format Tables 9A/9C/10/11; GSTR-1A advisory (Aug-2024); GSTR-3B Table-4 auto-lock
  advisory.

### 2.17 Build-time re-verify checklist (A14, at kickoff)

1. Target FY confirmed (DP-1) → pick the slab table + GST 2.0 dated windows.
2. GST 2.0 slabs 0/5/18/40 **+** special 3/1.5/0.25 **+** tobacco 28%+cess carve-out; 40% HSN list; legacy
   12/28 kept inactive-by-date (§2.1).
3. Cess three dated windows (a/b/c), mixed valuation (%/specific/RSP), ring-fence, stranded-credit treatment
   (§2.2, DP-4).
4. §9(4) = promoter-only; cement RCM rate **date-effective** (28→18); RCM service list + rates incl. GST 2.0
   GTA change; import of goods NOT RCM; self-invoice 30-day/Rule 47A; 30/60-day time-of-supply; cash-only
   discharge (§2.3).
5. Composition rates 1/5/6 unchanged; 8 special states = ₹75 L (incl. Uttarakhand); base by dealer type;
   CMP-08 quarterly / GSTR-4 annual due 30-June (§2.4).
6. e-Invoice threshold **₹5 cr** (NOT ₹2 cr proposal); 24-h cancel; no amend-on-IRP; 30-day only ≥₹10 cr;
   INV-01 schema version pin (§2.5).
7. e-Way ₹50,000 (+ state overrides) exceeding; validity 200 km / 20 km ODC (CBIC active table); Part-B ≤50 km;
   180-day/360-day/8-h; Ship-To GSTIN 01-Aug-2026 (§2.6).
8. GSTR-2B 14th + sequential + IMS deemed-accept + Oct-2025 CN partial reversal; §16(2)(aa) wording; buckets;
   confirm current 2A/2B JSON schema version from a real sample (§2.7).
9. Rule 88A any-order; provisos non-overridable; cess ring-fence; Rule 86B 1% cash; portal default split
   (§2.8).
10. **CDN §34:** 30-Nov cut-off (credit notes), no cut-off for debit notes but §16(4) keyed to DN's own FY;
    9B/9C mapping; unjust-enrichment bar; confirm Phase-4 CDN reuse gap (§2.9, DP-27).
11. **Advances:** services taxable on receipt / goods de-taxed (Notn 66/2017); Receipt/Refund voucher; 11A/11B;
    shared 30/60-day RCM time-of-supply (§2.10).
12. **§17(5) blocked credits + reversals:** Rule 42 D1/D2 monthly + annual true-up; Rule 43 60-month; Rule 37
    180-day; Rule 37A 30-Nov; 4(B)(1)/(2) split; DRC-03 landing (§2.11).
13. **B2C dynamic QR ₹500 cr** (Notn 14/2020-CT) distinct from IRP QR; **ISD mandatory 01-Apr-2025 + GSTR-6**
    (defer, DP-26); **PMT-06/DRC-03** instruments; **§50** interest flag-only (§§2.12–2.14).
14. **Zero-rated/inverted-duty refund (RFD-01)** posture (DP-29); **GSTR-1/3B amendment tables 9A/9C/10/11**,
    GSTR-1A + Table-4 hard-lock currency (§§2.15–2.16).
15. Confirm no post-GST-2.0 notification re-touched RCM/IMS/Rule 88A wording for FY2025-26.

---

## 3. Engine Mapping + New Masters/Entities (ER rules)

Advanced GST **extends the shipped GST engine**: config-driven, **framework/DB/clock/RNG-free** pure services
over the `Company` aggregate, producing **additive `EntryLine`s** carrying a self-describing `GstLineTax`
detail that reports read back (**pure projection — never recompute**). New inbound data (2A/2B) is staged and
reconciled by a pure engine; all money is **decimal rupees in-engine, integer paisa at the DB/Io boundary**,
**compute-once-then-split**.

### 3.1 Services

- **`GstService` (extended)** — the RCM/Cess branch lives in `ComputeInvoiceTax` (GstService.cs:274) and
  `AddHead` (GstService.cs:312): an **RCM inward** posts **both** an Output-liability leg **and** an Input-ITC
  leg; **Cess** is a 4th `AddHead(GstTaxHead.Cess,…)` in the per-rate loop (GstService.cs:327), with
  `EnableGst` (GstService.cs:60) gaining `EnsureTaxLedger` for **Output/Input Cess** (GstService.cs:77).
  Composition switches `EnableGst`/`ComputeInvoiceTax` to a **flat-turnover** branch (no tax-ledger split, no
  ITC). Keep the **total-then-split** invariant (round once → split) and `RoundToPaisa`.
- **`RcmService` (new, or a GstService sub-module)** — resolves RCM applicability from the notified-category
  master + party/ledger flag, computes time-of-supply (30/60-day), emits the dual-leg lines + the
  **self-invoice** (§31(3)(f)/Rule 47A) and **payment voucher** (§31(3)(g)) — mirror `TdsService` shape.
- **`CompositionService` (new)** — tax-on-turnover per dealer sub-type + base; **CMP-08 / GSTR-4** projections
  mirroring `Gstr1.Build`.
- **`EInvoiceService` / `EWayBillService` (new)** — **pure read-only projections** over posted outward
  vouchers (model on `TaxAnalysis.Build`) emitting **INV-01** / **EWB-01** JSON via new `Apex.Ledger.Io`
  writers (siblings of `FvuWriter`/`EcrWriter`); store/ingest the returned IRN/Ack/QR / EWB no. + validity.
- **`Gstr2bReconciliationService` (new — first INBOUND path)** — a pure function over (imported portal docs,
  purchase register): match on GSTIN + normalised doc-no + date-window + value within a **configurable
  tolerance** → buckets; an **IMS status** projection. **Advisory only — never posts ITC.**
- **`GstSetOffService` (new)** — deterministic **Rule 88A** utilisation over the four credit pools + cash
  ledger; produces the editable **GSTR-3B Table 6.1** and the set-off/payment postings so **`Gstr3b`** reads
  **posted** set-off instead of the current **display-only net** (Gstr3b.cs:16,34-53). Emits the **PMT-06**
  cash-deposit challan (CPIN/CIN/BRN) and **DRC-03** for off-return reversals.
- **`CreditDebitNoteService` (new, or a GstService sub-module)** — books a §34 CDN **against its original
  invoice**, adjusts output tax in the correct direction (unjust-enrichment bar on reduction), enforces the
  §34(2) **30-Nov** guard (warn/block), and feeds **GSTR-1 9B/9C**. Mirror `ComputeInvoiceTax` (a CDN is a
  signed delta over the original line set).
- **`AdvanceReceiptService` (new)** — issues the **Receipt/Refund voucher**, computes services-only advance tax
  (goods de-taxed), and reconciles the **advance→invoice adjustment** into GSTR-1 11A/11B; shares the 30/60-day
  time-of-supply helper with `RcmService`.
- **`ItcReversalService` (new)** — **Rule 42/43** common-credit apportionment (D1/D2 monthly + annual true-up;
  capital-goods 60-month), **Rule 37/37A** reversals, and **§17(5)** blocked-credit determination, posting to
  **GSTR-3B Table 4(B)** and reducing the credit ledger via `GstSetOffService`/DRC-03. **Advisory recon never
  auto-reverses** — this service is the only poster.
- **`B2cQrService` (new)** — a **pure projection** building the **self-generated dynamic (UPI) QR** payload for
  B2C invoices when AATO > ₹500 cr; **completely separate from `EInvoiceService`** — no IRN, never touches the
  IRP adapter.
- **`GstReportSupport.DirectionOf` (extended)** — RCM breaks the 1:1 base-type→direction map (a Purchase yields
  an **Output** liability) → add an RCM branch (GstReportSupport.cs:24); composition/e-invoice/e-way follow the
  existing read-only projection over posted lines.

### 3.2 New masters / entities (mirror existing types)

| New master | Mirrors | Key fields |
|---|---|---|
| **GST rate history (dated)** | `GstRateSlab` / `SeedGstRates` | rate_bp, class (standard/merit/special/de-merit/carve-out), effective-from/to, valuation-basis (transaction/RSP), active-by-date; **the 40% HSN list + 3/1.5/0.25 specials + 28% tobacco carve-out**. |
| **Cess master** | `GstRateSlab` + `StockItemGstDetails` | cess_rate_bp (ad-valorem), cess_per_unit_paisa (specific), cess_rsp_factor + rsp_paisa, valuation basis, dated windows. |
| **RCM category** | `GstRateSlab` (seeded, dated) | notification (13/2017 / 10/2017 / 4/2017 / 7/2019), supply nature, rate, recipient/supplier qualifier (business-entity / body-corporate), stream (§9(3)/§9(4)). |
| **RCM flags** | `PartyGstDetails` / `StockItemGstDetails` | is_rcm_applicable, gta_forward_charge (Annexure-V), promoter_profile. |
| **Composition config** | `GstConfig` | reg_type=Composition, sub_type (Mfr/Trader/Restaurant/§10(2A)), state threshold, rate base. |
| **e-Invoice config + record** | `TdsConfig` + a posted-doc projection | applicable_from, AATO flag + override, typed exemption; per-voucher IRN, ack_no, ack_date, signed_qr, signed_json, status enum. |
| **e-Way Bill config + record** | `GstConfig` + `Voucher`/`VoucherInventoryLine` | threshold (per-state), basis, intra-state flag; ewb_no, gen_datetime, validity_till, Part-A/Part-B fields (transporter ID/TRANSIN, mode, vehicle, distance, ship-from/to, **Ship-To GSTIN**), status. |
| **2A/2B staging + recon** | Form-26AS/24Q import staging | portal doc (GSTIN, doc type/no/date, POS, taxable, IGST/CGST/SGST/**Cess**, ITC-avail flag, section); recon-result (bucket, matched voucher link, variance); **IMS status** (Accept/Reject/Pending/Deemed + remarks + declared reversal). |
| **Electronic ledgers** | `GstService.EnsureTaxLedger` | Credit Ledger 4 pools (IGST/CGST/SGST-UTGST + ring-fenced **Cess**); Cash Ledger by minor head (tax/interest/penalty/fee/other); Liability register. |
| **CDN link (§34)** | `Voucher` + `GstLineTax` | original_invoice_ref (or consolidated-party ref), cdn_type (credit/debit), reason code, §34(2)-cutoff date, 9B/9C target flag. |
| **Advance receipt** | `Voucher` (Receipt sub-type) | advance_amount, service-vs-goods flag, rate/POS fallback, adjusted_against_invoice_ref, 11A/11B linkage. |
| **ITC reversal + eligibility** | `PartyGstDetails`/`StockItemGstDetails` + a reversal record | itc_eligibility (eligible/§17(5)-blocked/ineligible), reversal_rule (37/37A/42/43), period, D1/D2 basis, drc03_ref, 4(B) bucket. |
| **B2C dynamic QR** | `GstConfig` + posted-doc projection | b2c_qr_applicable (AATO > ₹500 cr + override), upi_id / payee, per-invoice QR payload (self-generated, no IRN). |
| **Return amendment** | posted-doc projection | amend_target_table (9A/9C/10/11), original-period ref, one-amendment-per-doc guard. |

### 3.3 New voucher types

- **RCM self-invoice** and **RCM payment voucher** — model as **flags/sub-types on existing base types**
  (Sales/Payment) like the Phase-7 Stat-Payment reuse, since they carry balanced Dr/Cr lines (contrast Phase-8
  Attendance/Payroll which needed genuinely new base types). Audit that they route correctly through
  `GstReportSupport.DirectionOf` (an RCM purchase → Output).
- **GST payment / stat-adjustment voucher** — a **Journal/Payment reuse** (Alt+J set-off + Ctrl+F stat-payment)
  with CPIN/CIN/BRN via the **PMT-06** challan; **DRC-03** off-return reversals are a stat-adjustment sub-type,
  mirror Phase-7 `TdsDepositService`.
- **Credit / Debit Note (§34)** — a **flag/sub-type on the existing Sales/Purchase base types linked to the
  original invoice** (balanced Dr/Cr like the Phase-7 reuse); audit it routes correctly through
  `GstReportSupport.DirectionOf` and feeds GSTR-1 9B/9C (not a new base type).
- **Advance Receipt / Refund voucher** — a **Receipt/Payment base-type reuse** carrying the advance-tax leg for
  services and the later adjustment against the invoice (11A/11B).
- **e-Invoice / e-Way Bill / B2C-QR** are **not** vouchers — they are read-only projections + stored artefacts
  attached to the source voucher (B2C-QR is a self-generated payload, e-Invoice/e-Way are IRP/portal artefacts).

### 3.4 Data-model additions (SQLite, additive from v37)

Per the ground-map migration pattern — bump `Schema.CurrentVersion` 37→38…→44 (Schema.cs:86); add every table/
column to **BOTH** `Schema.CreateV1` **AND** the new `MigrateV(N-1)ToVN` (ALTER … ADD COLUMN with DEFAULT
0/NULL so a v37 company reloads byte-identical — ER-13; CREATE TABLE/INDEX for new tables); wire an
`if (version == N)` step in `SqliteCompanyStore.EnsureSchema` (SqliteCompanyStore.cs:65+);
`SchemaMigrationEquivalenceTests` enforces CreateV1 == migrate-chain; **update the `DowngradeToV9/V11`
helpers** (InventoryVoucherRoundTripTests.cs:252, ItemInvoiceRoundTripTests.cs) to `DROP TABLE IF EXISTS`
every new table. New tables (all with `company_id` FK + `ix_*_company`, empty when advanced-GST off ⇒ ER-13):
`gst_rate_history`, `gst_cess_rates`, `rcm_categories`, `composition_config`, `einvoice_records`,
`eway_bills`, `gstr2b_snapshots`, `gstr2b_lines`, `gstr2b_recon`, `ims_status`, `electronic_credit_ledger`,
`electronic_cash_ledger`, `gst_setoff_lines`, and the gap-closing additions **`gst_cdn_links`** (§34
original-invoice link + 9B/9C target + 30-Nov cutoff), **`gst_advance_receipts`** (advance + adjustment +
11A/11B), **`itc_reversals`** (Rule 37/37A/42/43 + §17(5) + DRC-03 ref + 4(B) bucket), **`gst_drc03`**
(off-return reversal instrument). ALTER `ledgers`/`stock_items` ADD RCM + cess + **itc-eligibility (§17(5))**
columns; ALTER `companies` ADD e-invoice/e-way/composition + **B2C-dynamic-QR (AATO>₹500cr) config** columns;
ALTER the voucher tables ADD CDN/advance/DRC-03 sub-type flags; extend `entry_lines.gst_tax_head` usage for the
Cess line (column already exists). Each addition lands in its slice's single migration (no extra schema
versions — CDN/advances fold into S2, blocked-credit/reversal into S6/S7, B2C-QR into S4, amendments into S8's
projection-only pass).

---

## 4. Numbered Requirements

### Masters & configuration
- **RQ-1 (GST 2.0)** — Dated **GST rate-history master**: slabs 0/5/18/40 + retained specials 3%/1.5%/0.25% +
  the **28%+cess tobacco/pan-masala carve-out**, resolved by **voucher date**; legacy 12%/28% kept
  **inactive-by-date** (never deleted); per-item **valuation-basis** (transaction vs RSP). **GST Rate Setup
  bulk screen** (mass HSN/rate update — plan.md C-6, binding).
- **RQ-2 (Cess)** — Cess master on stock item / S-P ledger: **ad-valorem %**, **specific per-unit/quantity**,
  and **RSP-factor** (RSP field on the item); dated across the three FY2025-26 windows; drives an
  `AddHead(Cess)` line and Output/Input Cess ledgers.
- **RQ-3 (RCM)** — RCM applicability flag on party/expense/stock master + a **notified-category master**
  (13/2017 / 10/2017 services; 4/2017 goods; 7/2019 promoter §9(4)); **GTA Annexure-V** forward-charge flag;
  supplier/recipient qualifiers so the flag fires only on in-scope counterparties.
- **RQ-4 (Composition)** — Activate `GstRegistrationType.Composition`: sub-type (Manufacturer / Trader /
  Restaurant / §10(2A) service), state-driven threshold table (₹1.5cr / ₹75L / ₹50L), rate base by sub-type;
  gate the 6 GST tax ledgers off for a composition dealer.
- **RQ-5 (e-Invoice)** — F11 e-Invoicing config: applicability from **AATO > ₹5 cr** (configurable + manual
  override, sticky), **typed exemption** flags (SEZ-unit/insurer-bank-NBFC/GTA/passenger/multiplex/govt/OIDAR),
  document scope (B2B/export/SEZ/RCM/CDN; exclude B2C/BoS/ISD/import).
- **RQ-6 (e-Way Bill)** — F11 e-Way config: threshold **₹50,000 default, per-state / per-transaction-type
  override table**, computation basis, intra-state applicability; mandatory-irrespective-of-value overrides
  (inter-state job-work / handicraft).

### Transaction engine (RCM · Cess · Composition · Imports/Exports/SEZ)
- **RQ-7 (RCM)** — On an RCM inward supply post **no supplier-value tax** and a **balanced dual leg** — Output
  RCM liability + Input RCM ITC — split CGST+SGST (intra) or IGST (inter/import-of-services) by place of
  supply; enforce **cash-only discharge** of the RCM liability (never from the credit ledger). Compute
  **time-of-supply** (goods 30-day §12(3); services 60-day §13(3)).
- **RQ-8 (RCM docs)** — Auto-generate the **self-invoice** (§31(3)(f), own series, Rule 47A **30-day** window)
  for unregistered-supplier RCM and the **payment voucher** (§31(3)(g)/Rule 52) at payment; branch on
  counterparty GSTIN presence (registered §9(3) supplier → no self-invoice).
- **RQ-9 (Cess)** — Compute cess per line honouring **both** valuation bases (ad-valorem + specific/RSP),
  **date-aware** across the three windows, rounded to paisa (total-then-split), posted to a **ring-fenced**
  Output/Input Cess ledger; a 40%-slab item **must not** silently attract cess after 22-Sep-2025.
- **RQ-10 (Composition)** — Force **Bill of Supply** (no tax invoice), **suppress output tax**, **block ITC**;
  compute quarterly **tax-on-turnover** by dealer base (manufacturer/restaurant on total; trader/§10(2A) on
  taxable); **RCM inward still at normal rate** (never composition rate).
- **RQ-11 (Import/Export/SEZ)** — Keep **import of GOODS out of RCM** (BoE IGST → GSTR-3B **4A(1)**); **import
  of SERVICES** = RCM IGST (3.1(d)/4A(2)); model **exports** (LUT / with-payment), **SEZ** and **deemed
  exports** as zero-rated supply *types* feeding GSTR-1/3B and e-Invoice covered-doc scope. The **refund
  application** path (RFD-01 unutilised-ITC vs IGST-paid; inverted-duty §54(3)(ii)/Rule 89(5)) is **gated at
  DP-29** — classification in scope, refund forms deferred/flag.
- **RQ-24 (Credit/Debit Notes §34)** — Book a GST CDN **linked to its original invoice** (or consolidated-party
  ref), adjusting output tax in the correct direction (credit ↓ with the **unjust-enrichment** bar / debit ↑),
  enforce the §34(2) **30-Nov-following-FY** declaration guard on credit notes (debit notes uncapped, ITC keyed
  to the DN's own §16(4) FY), and feed **GSTR-1 Table 9B** (amendments → 9C). B2B CDN is an e-Invoice covered
  document (RQ-18). **State the reuse posture at DP-27** (newly formalised here vs the Phase-4 return voucher).
- **RQ-25 (GST on advances §13)** — Issue a **Receipt Voucher (Rule 50)** on advance receipt and a **Refund
  Voucher (Rule 51)** on non-supply; charge advance tax for **services only** (goods de-taxed, Notn 66/2017-CT);
  **adjust the advance against the later invoice**; feed **GSTR-1 11A/11B**; reuse the 30/60-day time-of-supply
  helper shared with RCM (RQ-7).

### Returns & reconciliation
- **RQ-12 (GSTR-2A/2B)** — Import portal **JSON**: store **GSTR-2B as an immutable dated snapshot** per period,
  **GSTR-2A** re-importable/dynamic; parse B2B / imports / ISD / RCM sections incl. the **Cess** column;
  version the parser against a confirmed current schema.
- **RQ-13 (Reconciliation)** — Reconcile the snapshot against the purchase register into buckets **Matched /
  Partial-mismatch / In-2B-only / In-books-only** with a **configurable tolerance** (normalised doc-no, date
  window, value/tax rounding); drill-down to voucher + portal line; **advisory only** (no auto-post).
- **RQ-14 (IMS)** — Offline **IMS mirror**: per-line **Accept / Reject / Pending** + **deemed-accept**, remarks,
  and **declared ITC reversal** on accepted credit notes (Oct-2025); IMS-bypass records (RCM 4B, §16(4)/POS)
  shown in a separate non-actionable section; UI states clearly that the real action happens on the portal.
- **RQ-15 (GSTR-3B ITC gate)** — Surface **ITC-in-books vs ITC-in-2B vs ITC-claimed-in-3B** per §16(2)(aa)/
  Rule 36(4); flag the difference; **advisory** — reversals route through the set-off/stat-adjustment engine,
  never auto-posted here.
- **RQ-26 (§17(5) blocked credits)** — A per-purchase **ITC-eligibility flag / blocked-credit determination**
  (motor vehicles, food & beverages, works-contract/construction, personal consumption, gifts/free-samples,
  written-off goods, etc.) — beyond the 2B "ITC-avail" flag; **ineligible ITC is not availed** (or availed and
  reversed), reported to **GSTR-3B Table 4(B)(1) / 4(D)**.
- **RQ-27 (ITC reversal engine)** — **Rule 42/43** common-credit apportionment (exempt/non-business D1 + 5% D2,
  monthly + **annual true-up**; capital-goods 60-month spread), **Rule 37** (180-day non-payment reversal + re-
  availment) and **Rule 37A** (supplier-non-payment 30-Nov reversal), all posting to **GSTR-3B Table 4(B)** and
  **reducing the electronic credit ledger** (via RQ-21) with **DRC-03** as the off-return instrument. The 2B/IMS
  reconciler (RQ-13/14) only **surfaces** reversal candidates — **this engine is the sole poster** (advisory
  recon never auto-reverses).
- **RQ-16 (Composition returns)** — **CMP-08** (quarterly) + **GSTR-4** (annual, due 30-June) projections;
  on-screen computation + offline JSON export.
- **RQ-17 (Annual returns)** — **GSTR-9 / GSTR-9A / GSTR-9C** (reconciliation-statement mechanics distinct from
  9/9A — plan.md C-6) as **light projections** over posted GST data; **QRMP + IFF** election.
- **RQ-29 (GSTR-1/3B amendments)** — Model the **GSTR-1 amendment tables 9A** (B2B/B2C-large/export-amend),
  **9C** (CDN-amend), **10** (B2C-others-amend), **11** (advances 11A/11B) and **prior-period GSTR-3B
  correction** — the concrete target for RQ-18's "corrections route to GSTR-1 amendment (no amend-on-IRP)";
  enforce one-amendment-per-document; reflect **GSTR-1A** (same-period pre-3B amend) and the **Table-4/3.1
  auto-lock** currency at kickoff (§2.16). Pure projections; **advisory** where the portal hard-locks.

### e-Invoice & e-Way
- **RQ-18 (e-Invoice)** — Serialize the **INV-01** JSON (TranDtls/DocDtls/Seller/Buyer/ItemList with HSN/GstRt
  accepting **40%**/CesRt/CesNonAdvlAmt/ValDtls/EwbDtls) as a **pure projection** over posted covered vouchers;
  offline export/import (auto-split < 2 MB) via a **pluggable IRP/GSP adapter** (no live creds — R13); store
  **IRN / Ack-No / Ack-date / signed-QR / signed-JSON / status** (Not-applicable/Pending/Generated/Cancelled/
  Failed); **24-h full cancel**, cancelled doc-no not reusable, **no amend-on-IRP** (route to GSTR-1);
  uppercase doc-no; feed GSTR-1 auto-population (T+2, by doc date, Source/IRN tags cleared on edit); enforce
  the **30-day** reporting age only for AATO ≥ ₹10 cr; **amendment corrections land in RQ-29's GSTR-1
  amendment tables** (no amend-on-IRP).
- **RQ-28 (B2C Dynamic QR)** — A **self-generated dynamic (UPI/payment) QR** on **B2C** invoices, gated to
  **AATO > ₹500 cr** (configurable + override) per **Notn 14/2020-CT**, carrying payee/UPI-ID/amount so the
  buyer can pay digitally and the payment cross-links to the invoice. **Entirely distinct from the IRP-signed
  e-Invoice QR (RQ-18)** — **no IRN, never routed through the IRP/INV-01 flow**, rendered on the printed B2C
  invoice; exempt classes mirror the e-Invoice list plus the B2C-QR FAQ carve-outs.
- **RQ-19 (e-Way Bill)** — Compute **consignment value = §15 value + CGST+SGST+IGST + (date-aware) cess −
  exempt portion**, compare to the (state-configurable) threshold (**exceeding**); build **EWB-01** Part A/B
  (Part B optional ≤50 km intra-state) and consolidated **EWB-02**; a **validity calculator**
  (ceil(dist/200); ODC ceil(dist/20); midnight-expiry) with extension (8 h / 360-day cap) and 180-day
  base-doc block; **cancellation 24 h / accept-reject 72 h**; **offline JSON** export + write-back of EWB no./
  date/validity; **Rule 138E** as a warning; add **Ship-To GSTIN** + **closure** fields (gated to 01-Aug-2026).
- **RQ-30 (GST-portal connectivity adapter)** — A **pluggable connectivity-adapter interface
  `IGstPortalConnector`** abstracting the transport for GST-portal interchange, with implementations
  **`OfflineJsonConnector` (default)** — export/import JSON under the customer's own portal login — and
  **`CustomerNicDirect`** — an **OPTIONAL** live path calling the **NIC IRP / e-Way APIs directly with the
  customer's OWN NIC credentials** (Client-ID/Client-Secret + per-GSTIN API username/password, static-IP-
  whitelist aware, stored **protected-at-rest**) — plus a **stubbed `GspConnector` seam** kept but **NOT built**
  so a future GSP-partner full integration (returns / 2A / 2B / IMS + sub-threshold e-Invoice/e-Way) drops in
  **without rework**. **e-Invoice (RQ-18) and e-Way Bill (RQ-19) each expose BOTH transports** — offline JSON
  (baseline, any taxpayer) and customer-NIC-live (qualifying self-provisioners: e-Invoice AATO > ₹500 cr,
  e-Way AATO > ₹5 cr); **returns / GSTR-1/3B / 2A / 2B / IMS stay offline-only** in Phase 9. Live NIC creds are
  configured by the customer and are **never** a GSP/vendor credential (ER-16). *(Hybrid connectivity approved
  2026-07-14 — §1.5, DP-10/DP-12/DP-25.)*

### ITC set-off & payment
- **RQ-20 (Electronic ledgers)** — Maintain the **electronic Credit Ledger** as four independent balances
  (IGST / CGST / SGST-UTGST / **ring-fenced Cess**) and the **Cash Ledger** by minor head; **no CGST↔SGST
  transfer path may exist** in the schema (§49(5)(e),(f) hard invariant).
- **RQ-21 (Rule 88A)** — Deterministic set-off: IGST credit first & exhausted (→ IGST, then CGST/SGST any
  order/proportion per the chosen default), then own-head priority (CGST→CGST/IGST, SGST→SGST/IGST), with the
  **CGST-before-SGST-for-IGST proviso non-overridable**; **editable Table 6.1** (portal-like override); **RCM/
  interest/penalty/fee cash-only**; **Rule 86B** 1%-cash cap applied after set-off; cess set off only against
  cess.
- **RQ-22 (GST payment)** — **Stat-adjustment journal** (Alt+J: ITC set-off / liability increase) + **stat-
  payment** (Ctrl+F Autofill) via the **PMT-06** cash-ledger deposit challan, capturing **CPIN/CIN/BRN**;
  **DRC-03** (voluntary payment / ITC reversal) as the named instrument for off-return reversals surfaced by
  RQ-14/RQ-15/RQ-27; **`Gstr3b` reads posted set-off** (no longer display-only), reconciling liability vs
  credit-utilised vs net cash.

### I/O
- **RQ-23 (Io)** — Every new master, voucher extension, IRP/portal artefact, and inbound 2A/2B snapshot
  round-trips **lossless in JSON + XML** (paisa + count exact), folded into CanonicalModel/CanonicalMapper/
  CanonicalXml **in the introducing slice**, and **mirrored all-or-nothing into `CompanyImportService`** so
  Io-import cannot bypass the engine guards (RCM dual-leg, cess ring-fence, composition suppression).

### Engine/entity invariants (ER)
- **ER-1** GST 2.0 rate/cess resolved by **voucher date**; legacy slabs **inactive-by-date, never deleted**.
- **ER-2** **Cess ring-fence:** cess credit discharges only cess; GST credit never pays cess and vice-versa
  (activates the dormant `GstTaxHead.Cess` seam).
- **ER-3** RCM posts a **balanced dual leg**; the RCM **output liability is cash-only** (never nettable against
  the electronic credit ledger).
- **ER-4** Composition **suppresses only outward output tax**; inward RCM stays at the normal rate; no ITC.
- **ER-5** IRN/QR are **never computed or signed locally** — stored exactly as the IRP returns; **no
  amend-on-IRP** path is offered.
- **ER-6** **GSTR-2B is an immutable dated snapshot**; reconciliation and the ITC-gate view are **advisory —
  never auto-post ITC or reversals**.
- **ER-7** Set-off follows the deterministic **Rule 88A** order; **CGST↔SGST cross-utilisation is
  schema-impossible**; the §49(5)(c)/(d) proviso is non-overridable.
- **ER-8** GST still posts **only to `Duties & Taxes`** tax ledgers (excluded from item-invoice pairing via
  `ClassificationRules.IsDutiesAndTaxesLedger`) so `VoucherValidator` keeps passing — advanced-GST postings
  preserve this.
- **ER-9** Reports are **pure projections** over posted `GstLineTax` / the imported snapshot — **never
  recompute** tax.
- **ER-10** Money = **decimal rupees in-engine / integer paisa at the boundary**; **compute-once-then-split**;
  no paisa leak (the recurring A10 finding class).
- **ER-11** No `Tally` / brand strings in advanced-GST UI, code, JSON/exports (de-brand scan).
- **ER-12** A §34 CDN **must link to its original invoice** (or a consolidated-party ref) and adjust output tax
  in the **correct direction**; the §34(2) **30-Nov** cut-off (credit notes) is enforced (warn/block) and the
  unjust-enrichment bar respected — a CDN never becomes a free-floating tax delta.
- **ER-14** ITC reversals (§17(5), Rule 37/37A/42/43) post to **GSTR-3B Table 4(B)** and **reduce the
  electronic credit ledger** through the set-off engine / **DRC-03**; the **2B/IMS reconciler never auto-posts
  a reversal** (advisory-only, consistent with ER-6).
- **ER-15** The **B2C dynamic QR is self-generated (UPI/payment) and NEVER enters the IRP/IRN flow**; the
  IRP-signed e-Invoice QR (ER-5) and the B2C QR are separate paths that must not be conflated.
- **ER-13 (standing, project-wide)** — **feature-off ⇒ byte-identical serialization**: new columns DEFAULT
  0/NULL, new tables empty. Framed for Phase 9 as **"advanced-GST-off ⇒ byte-identical to a v37 Phase-8
  company."** A hard gate in every schema slice and a numbered exit-gate item.
- **ER-16 (connectivity security invariant)** — The app **NEVER** stores or transmits a **GSP/vendor**
  credential, and **NEVER** holds or auto-enters the customer's GST-portal **login password or DSC** — all
  portal signing/consent uses the taxpayer's **OWN OTP/DSC**. The optional live e-Invoice/e-Way path (RQ-30)
  calls the NIC APIs using **only** the customer-configured **NIC API credentials**, stored **protected-at-rest**
  (OS keystore / DPAPI — never plaintext). Offline JSON remains the **default and the fallback**; a taxpayer
  who configures no live path behaves exactly as the offline baseline (§1.5, DP-10/DP-12/DP-25).

---

## 5. Ordered Slice Plan (schema v37 → v44)

Sequential build (Phase 6/7/8 constraint): every schema slice edits `Schema.cs` + `SqliteCompanyStore.cs` +
`Company.cs` on **one migration chain** → slices **cannot** run in parallel. Each migration is one additive
`MigrateV(N-1)ToVN` in a txn that bumps `schema_version`; a fresh DB stamps straight to head via `CreateV1`;
each paired with a **legacy-DDL upgrade test** (mirror `JobWorkSchemaTests`/`TdsSchemaTests`) proving the
upgrade keeps existing rows untouched + **ER-13 byte-identical when advanced-GST off**. A12 commits each slice
in small conventional units; A10 adversarial pass before each PR gate.

> **Schema progression: v37 → v38 → v39 → v40 → v41 → v42 → v43 → v44.** (S8 adds no schema — pure
> projections/reports; the numbering leaves the persistence slices contiguous.)

### P9-S1 — GST 2.0 rate framework + Compensation Cess → **v37→v38**
- **Scope:** Dated **GST rate-history master** (0/5/18/40 + 3/1.5/0.25 + 28%-tobacco carve-out; legacy 12/28
  inactive-by-date; RSP valuation flag) with **voucher-date resolution**; **GST Rate Setup bulk screen**;
  activate the **Cess seam** — cess master (ad-valorem/specific/RSP), `AddHead(Cess)` in `ComputeInvoiceTax`,
  `EnsureTaxLedger` Output/Input Cess in `EnableGst`, and a **ring-fenced cess credit pool**. Fold into
  `Apex.Ledger.Io` + `CompanyImportService` now. **Foundational — every later rate/cess-bearing slice depends
  on this.**
- **Schema v38:** `gst_rate_history`, `gst_cess_rates` (+ indices); ALTER `stock_items`/`ledgers` ADD cess +
  RSP + valuation-basis columns (DEFAULT 0/NULL); a cess pool column on the (S7) credit-ledger seam or a
  placeholder.
- **PR-1 gate:** rate resolves by voucher date; three cess windows compute (20-Sep vs 25-Sep car = cess vs
  no-cess golden pair); cess ring-fenced; total-then-split conserves to paisa; legacy-DDL v37→v38; **ER-13
  byte-identical off**; Io + CompanyImportService round-trip; **Robert + Bright green**; de-brand clean.
- **Key RQs:** RQ-1, RQ-2, RQ-9. **Files to mirror:** `GstRateSlab.cs`, `SeedGstRates.cs`, `GstService.cs`
  (EnableGst/ComputeInvoiceTax/AddHead/EnsureTaxLedger), `StockItemGstDetails.cs`, `Schema.cs`,
  `SqliteCompanyStore.cs`, `CanonicalModel.cs`/`CanonicalMapper.cs`/`CanonicalXml.cs`, `CompanyImportService.cs`,
  `TdsSchemaTests.cs`.

### P9-S2 — RCM (reverse charge) + self-invoice/payment voucher → **v38→v39**
- **Scope:** notified-category master (13/2017 / 10/2017 / 4/2017 / 7/2019 promoter §9(4)); RCM flags on party/
  ledger/stock + GTA Annexure-V; **dual-leg posting** (Output liability + Input ITC) with POS split;
  **cash-only discharge** guard; 30/60-day time-of-supply; auto **self-invoice** (Rule 47A 30-day) + **payment
  voucher**; extend `GstReportSupport.DirectionOf` (purchase→Output); GSTR-3B 3.1(d)/4A(2)/4A(3) mapping;
  cement RCM **date-effective** rate. **Also lands the two core outward-document gap-closers that reuse the
  same rate/time-of-supply seam:** **§34 Credit/Debit Notes** (original-invoice link, output-tax adjustment,
  §34(2) 30-Nov guard, GSTR-1 9B/9C) and **GST-on-advances** (Receipt/Refund voucher, services-only advance
  tax, advance→invoice adjustment, 11A/11B). Fold into Io + CompanyImportService now.
- **Schema v39:** `rcm_categories` (dated, seeded); `gst_cdn_links`, `gst_advance_receipts`; ALTER
  `ledgers`/`stock_items` ADD RCM columns; self-invoice / payment-voucher / CDN / advance-receipt sub-type
  flags on the voucher tables.
- **PR-2 gate:** RCM dual-leg balances to paisa; §9(4) fires **only** for the promoter profile (blanket toggle
  default OFF); import-of-goods excluded from 3.1(d); cash-only discharge enforced; self-invoice within 30
  days; **CDN links to original + adjusts output tax in the right direction + 30-Nov guard fires (ER-12) + 9B
  mapping**; **advance tax on services only (goods de-taxed) + Receipt Voucher + 11A/11B**; DirectionOf handles
  RCM and CDN; legacy-DDL v38→v39; ER-13; Io + import mirror; Robert + Bright green.
- **Key RQs:** RQ-3, RQ-7, RQ-8, RQ-11 (import-of-services part), RQ-24, RQ-25. **Files to mirror:**
  `GstService.cs` (ComputeInvoiceTax/AddHead), `TdsService.cs`, `GstReportSupport.cs` (DirectionOf), `Gstr3b.cs`,
  `Gstr1.cs` (9B/11 tables), `PartyGstDetails.cs`, `TdsDepositService.cs` (payment-voucher pattern).

### P9-S3 — Composition scheme + CMP-08 / GSTR-4 → **v39→v40**
- **Scope:** activate `GstRegistrationType.Composition` (sub-type + state threshold + rate base);
  **Bill of Supply** (output-tax suppressed, ITC blocked); tax-on-turnover engine; **CMP-08** (quarterly) +
  **GSTR-4** (annual) projections + offline JSON; inward RCM still at normal rate (reuse S2). Fold into Io now.
- **Schema v40:** `composition_config`; ALTER `companies` ADD composition columns; a bill-of-supply document
  flag.
- **PR-3 gate:** composition dealer issues Bill of Supply with no output tax + no ITC; tax-on-turnover base
  correct per sub-type (manufacturer/restaurant total vs trader/§10(2A) taxable); CMP-08/GSTR-4 reconcile;
  RCM-at-normal-rate preserved; legacy-DDL v39→v40; ER-13; Io + import mirror.
- **Key RQs:** RQ-4, RQ-10, RQ-16. **Files to mirror:** `GstService.cs` (EnableGst branch), `GstConfig.cs`,
  `Gstr1.cs` (Build/projection pattern), `SeedGstRates.cs`.

### P9-S4 — e-Invoice (IRN/QR, INV-01 offline JSON) → **v40→v41**
- **Scope:** F11 e-Invoicing config (AATO > ₹5 cr + override + typed exemptions + doc scope); **INV-01** JSON
  writer (new `Apex.Ledger.Io` writer, sibling of `FvuWriter`), offline export/import (auto-split < 2 MB) as
  the **baseline transport**, behind the **pluggable connectivity adapter `IGstPortalConnector`**
  (`OfflineJsonConnector` default) — **plus an OPTIONAL live `CustomerNicDirect` transport** calling the NIC
  IRP APIs directly with the **customer's OWN NIC credentials** (qualifying self-provisioners, AATO > ₹500 cr;
  creds stored **protected-at-rest** — OS keystore / DPAPI, a build-time detail) and a **stubbed `GspConnector`
  seam** kept but not built (§1.5, RQ-30, ER-16); store IRN/Ack/QR/JSON/status; **24-h full cancel**, no
  amend-on-IRP; uppercase doc-no; GSTR-1 auto-population feed; 30-day age only ≥₹10 cr. **Also lands the
  distinct B2C dynamic-QR obligation** (RQ-28): a **self-generated UPI/payment QR** on B2C invoices gated to
  AATO > ₹500 cr — **no IRN, never through the IRP flow** — rendered on the B2C print. Fold into Io + import
  mirror now.
- **Schema v41:** `einvoice_records` (voucher_id, irn, ack_no, ack_date, signed_qr, signed_json, status);
  ALTER `companies` ADD e-invoice **and B2C-dynamic-QR (AATO>₹500cr, UPI-id/payee)** config columns **plus the
  connector-mode + NIC-API-credential columns** (Client-ID/Client-Secret/username/password held encrypted /
  protected-at-rest — never plaintext) — additive within this slice's single migration (no extra schema
  version).
- **PR-4 gate:** INV-01 JSON validates against the pinned NIC schema version (golden fixture); IRN/QR stored
  as-returned (never locally computed); 24-h cancel + no-amend enforced; covered-doc scope correct (B2B/export/
  SEZ/RCM/CDN; excludes B2C/BoS/ISD/import); **B2C dynamic QR is self-generated + carries no IRN + never enters
  the IRP/INV-01 path (ER-15)**; **`OfflineJsonConnector` is the default and works with zero credentials; the
  optional `CustomerNicDirect` live path is gated on customer-configured NIC creds stored protected-at-rest,
  and the app holds no GSP/vendor credential or portal password/DSC (ER-16)**; GSTR-1 reconciles to IRN-tagged
  docs; JSON **byte-stable + de-branded**; legacy-DDL v40→v41; ER-13; Io + import mirror.
- **Key RQs:** RQ-5, RQ-18, RQ-28. **Files to mirror:** `TaxAnalysis.cs` (projection), `FvuWriter.cs`/
  `EcrWriter.cs` (Io writer pattern), `CanonicalModel.cs`/`CanonicalXml.cs`, `Gstr1.cs`.

### P9-S5 — e-Way Bill (EWB-01/02 offline JSON) → **v41→v42**
- **Scope:** F11 e-Way config (₹50,000 default + per-state override + basis + intra-state); date-aware
  **consignment-value engine**; **EWB-01** Part A/B (+ consolidated **EWB-02**); **validity calculator**
  (200 km / 20 km ODC, midnight expiry, 8-h/360-day extension, 180-day block); Part-B ≤50 km relaxation;
  24-h cancel / 72-h accept-reject; **offline JSON** export + write-back as the **baseline transport** behind
  the same **pluggable `IGstPortalConnector`** adapter (`OfflineJsonConnector` default) — **plus an OPTIONAL
  live `CustomerNicDirect` transport** calling the NIC e-Way APIs directly with the **customer's OWN NIC
  credentials** (qualifying self-provisioners, AATO > ₹5 cr; creds stored **protected-at-rest** — OS keystore /
  DPAPI, a build-time detail) and the **stubbed `GspConnector` seam** kept but not built (§1.5, RQ-30, ER-16);
  **Rule 138E** warning; **Ship-To GSTIN + closure** fields gated to 01-Aug-2026; optional bundling with the S4
  e-Invoice payload. Fold into Io.
- **Schema v42:** `eway_bills` (voucher_id + Part-A/Part-B + validity + status); ALTER `companies` ADD e-way
  config + per-state threshold **plus the connector-mode + NIC-API-credential columns** (encrypted /
  protected-at-rest — never plaintext); transport fields on `Voucher`/`VoucherInventoryLine` — additive within
  this slice's single migration (no extra schema version).
- **PR-5 gate:** consignment value = §15 + taxes + date-aware cess − exempt; "exceeding ₹50,000" boundary
  exact; validity days correct (200/20 km, midnight); Part-B ≤50 km optional; state-override threshold applies;
  **`OfflineJsonConnector` default works with zero credentials; the optional `CustomerNicDirect` live path is
  gated on customer-configured NIC creds stored protected-at-rest, and the app holds no GSP/vendor credential
  or portal password/DSC (ER-16)**; EWB JSON byte-stable + de-branded; legacy-DDL v41→v42; ER-13; Io + import
  mirror.
- **Key RQs:** RQ-6, RQ-19. **Files to mirror:** S4 Io writer set, `GstReportSupport.cs`
  (InvoiceTaxableValue), `Voucher.cs`/`VoucherInventoryLine.cs`.

### P9-S6 — GSTR-2A/2B import + reconciliation + IMS (first inbound path) → **v42→v43**
- **Scope:** the **first INBOUND GST data path** — GSTR-2B **JSON** parser (immutable dated snapshot) + GSTR-2A
  parser (dynamic/re-importable), staging tables (incl. Cess column); pure **reconciliation engine** (buckets +
  configurable tolerance) mirroring the Form-26AS/24Q import scaffolding; **offline IMS mirror** (Accept/Reject/
  Pending + deemed-accept + remarks + declared CN reversal; bypass records section); the **ITC-gate advisory
  view** (books vs 2B vs 3B). **Also lands the §17(5) blocked-credit determination** (RQ-26): a per-purchase
  **ITC-eligibility flag** feeding the gate view, and **surfacing** (not posting) Rule-37A / IMS-rejected-CN
  reversal candidates for S7. **Advisory only — no auto-post.** Fold into Io + import mirror now.
- **Schema v43:** `gstr2b_snapshots`, `gstr2b_lines`, `gstr2b_recon`, `ims_status` (+ indices); ALTER
  `ledgers`/`stock_items` ADD the **itc-eligibility / §17(5)-blocked** column.
- **PR-6 gate:** 2B snapshot immutable + versioned; recon buckets correct on a golden fixture (matched/partial/
  in-2B-only/in-books-only); tolerance configurable; IMS status per line + deemed-accept; RCM/16(4)/POS records
  in the non-actionable section; **§17(5)-blocked ITC flagged ineligible**; **advisory — asserts no ITC or
  reversal posted (ER-14)**; reconciles to Input tax-ledger postings; legacy-DDL v42→v43; ER-13; Io + import
  mirror.
- **Key RQs:** RQ-12, RQ-13, RQ-14, RQ-15, RQ-26. **Files to mirror:** the Phase-5/7 import parser + staging
  pattern, `GstReportSupport.cs` (inward projection), `Gstr3b.cs` (ITC side).

### P9-S7 — Rule-88A ITC set-off + electronic ledgers + GST payment → **v43→v44**
- **Scope:** electronic **Credit Ledger** (four pools incl. ring-fenced Cess) + **Cash Ledger** (minor heads) +
  liability register; **`GstSetOffService`** — deterministic Rule 88A (any-order residual IGST default +
  editable Table 6.1; provisos non-overridable; Rule 86B 1% cash cap; RCM/interest cash-only); **GST payment
  voucher** (Alt+J stat-adjustment + Ctrl+F stat-payment with CPIN/CIN/BRN, reuse `TdsDepositService`); make
  **`Gstr3b` read posted set-off** (retire the display-only net, Gstr3b.cs:16,34-53). **Names the concrete
  payment instruments** — **PMT-06** cash-ledger deposit challan (CPIN/CIN/BRN) for the stat-payment leg and
  **DRC-03** for off-return reversals — and **lands the ITC reversal engine** (RQ-27): **Rule 42/43** common-
  credit apportionment (D1/D2 monthly + annual true-up; capital-goods 60-month), **Rule 37/37A** reversals,
  posting to GSTR-3B Table 4(B) and reducing the credit ledger (the sole poster for candidates S6 surfaced).
  Fold into Io + import mirror now.
- **Schema v44:** `electronic_credit_ledger`, `electronic_cash_ledger`, `gst_setoff_lines`, `itc_reversals`,
  `gst_drc03` (+ indices); GST stat-payment/adjustment/DRC-03 sub-type flags on the voucher tables.
- **PR-7 gate:** **Circular 98/17/2019 golden** — both valid IGST splits reachable, cash payable 0; IGST-first
  exhausted; **no CGST↔SGST path** (schema-asserted); provisos enforced; cess set off only vs cess; Rule 86B
  1% cash; **PMT-06 challan + DRC-03 reversal post correctly**; **Rule 42 D1/D2 + Rule 37 180-day reversal foot
  to Table 4(B) and reduce the credit ledger (ER-14)**; GSTR-3B reconciles to posted set-off + payable-ledger;
  **byte-stable golden test**; legacy-DDL v43→v44; ER-13; Io + import mirror.
- **Key RQs:** RQ-20, RQ-21, RQ-22, RQ-27. **Files to mirror:** `GstService.cs`, `Gstr3b.cs`,
  `TdsDepositService.cs` (CPIN/CIN/BRN), `GstReportSupport.cs`.

### P9-S8 — GST-advanced reports + annual returns + QRMP/IFF → **no schema** (pure projections)
- **Scope:** consolidate the advanced-GST report tree (**Reports → Statutory → GST Reports** cascading
  Miller-column) — RCM Liability/ITC statement (3.1(d) vs 4A(2)+4A(3)), e-Invoice status dashboard, e-Way
  pending/exception report, GSTR-2B recon report, set-off statement; **annual returns GSTR-9 / GSTR-9A /
  GSTR-9C** (light projections; 9C reconciliation-statement mechanics distinct from 9/9A — plan.md C-6);
  **QRMP + IFF** election; **GSTR-1/3B amendment tables** (9A/9C/10/11 + prior-period correction, RQ-29 — the
  build target behind RQ-18's no-amend-on-IRP routing); reuse Phase-4 HSN summary. All **pure projections** over
  posted data.
- **PR-8 gate:** every report reconciles to its underlying postings / imported snapshot; **amendment tables
  project prior-period corrections (one-amendment-per-doc)**; PDFs/exports deterministic + de-branded; golden
  report test; no schema change (ER-13 trivially holds).
- **Key RQs:** RQ-17, RQ-29 (+ report facets of RQ-7…RQ-28). **Files to mirror:** `Gstr1.cs`, `Gstr3b.cs`,
  `GstReportSupport.cs`, `ReportPdf.cs`/`TabularExport.cs`, `ReportsViewModel.cs`.

### P9-S9 — Exit gate → **no schema** (see §6)

---

## 6. Exit Gate (P9-S9) — R9 + PR gates + golden worked example

Mirror the Phase-6/7/8 exit gate. **Does not pass until all of:**

1. **All prior PR gates (PR-1…PR-8) re-green** with evidence.
2. **Full test suite green** (Io / Ledger / Sqlite / Desktop) with counts shown; **Robert + Bright** stay
   green; the **Phase-4 base-GST fixtures** stay byte-identical; the new **GST-advanced fixture** reconciled
   across all engines.
3. **Migration chain v37→v44 proven** — each `MigrateV(N-1)ToVN` + its legacy-DDL upgrade test; a fresh DB
   stamps to head via `CreateV1`; `SchemaMigrationEquivalenceTests` green; `DowngradeToV9/V11` helpers DROP
   every new table.
4. **ER-13 byte-identical when advanced-GST off** — a company that never enables any Phase-9 feature serializes
   **byte-identical to a v37 Phase-8 company** (every new column DEFAULT 0/NULL, every new table empty).
5. **Io losslessness re-verified** — JSON + XML round-trip **paisa + count exact** for **ALL** Phase-9 masters,
   voucher extensions, IRP/portal artefacts, and inbound 2A/2B snapshots; **CompanyImportService mirror** proven
   all-or-nothing (RCM dual-leg, cess ring-fence, composition suppression cannot be bypassed on import);
   explicit "no silent drop" check.
6. **De-brand scan** clean (no "Tally" in app UI / code / JSON exports).
7. **A10 adversarial sign-off** on the highest-risk items: RCM dual-leg + cash-only discharge; cess
   ring-fence + three-window date-selection; Rule-88A CGST↔SGST impossibility + both-split golden; 2B recon
   advisory (no auto-post); e-Invoice IRN-never-local + no-amend; consignment-value/validity edge cases;
   **CDN original-invoice link + 30-Nov §34(2) guard (ER-12); ITC reversal posts to 4(B) + reduces the credit
   ledger while the reconciler never auto-reverses (ER-14); B2C dynamic QR never enters the IRN flow (ER-15).**
8. **REAL `Apex.Desktop` app launched** (kill any running exe first), de-branded, advanced-GST screens
   exercised (RCM voucher + self-invoice, cess line, composition Bill of Supply + CMP-08, e-Invoice JSON, e-Way
   Bill, 2B reconciliation + IMS, Rule-88A set-off + GST payment, **§34 credit note, advance-receipt voucher,
   ITC reversal + DRC-03, B2C dynamic-QR invoice**).
9. **A12 commits & pushes**; PR CI green across all 3 OS (audit Windows-only-passing tests — the Phase-6
   path-separator lesson).

### Golden worked example (the R9 deliverable, shown numerically)

**(A) RCM import of a legal service (inter-state), FY2025-26, 18%:** taxable ₹1,00,000; recipient liable
under §9(3)/§5(3). Dual leg — `Dr Legal Expense 1,00,000 · Dr Input IGST (RCM) 18,000 / Cr Output IGST-RCM
Payable 18,000 · Cr Party 1,00,000`. **Output IGST-RCM ₹18,000 is cash-only** (not from the credit ledger);
Input IGST-RCM ₹18,000 flows to the credit ledger once paid. Self-invoice issued in the recipient's series
within 30 days; **GSTR-3B 3.1(d) = ₹18,000, 4A(2) = ₹18,000**. *(Assert dual leg balances to the paisa;
assert the output leg cannot be discharged from credit.)*

**(B) Cess date-window golden pair:** the same aerated-drink line (assessable ₹10,000) dated **20-Sep-2025**
→ 28% GST ₹2,800 **+ 12% cess ₹1,200**; dated **25-Sep-2025** → **40% GST ₹4,000, cess ₹0**. *(Assert the
voucher-date switch; assert cess ₹1,200 is ring-fenced — usable only against cess output.)*

**(C) e-Invoice IRN payload:** a B2B sale (taxable ₹50,000, 18% → CGST ₹4,500 + SGST ₹4,500, invoice value
₹59,000) serializes to **INV-01** JSON; the IRP-returned **IRN (64-char)**, Ack-No, and signed QR are stored
and reprinted; the doc auto-populates **GSTR-1 B2B** (by document date). *(Assert IRN is stored as-returned,
never locally computed; JSON byte-stable.)*

**(D) e-Way Bill validity:** consignment value ₹59,000 (> ₹50,000) over **410 km** normal cargo → validity =
ceil(410/200) = **3 days**, expiring at midnight of day 3; Part-B required (> 50 km). EWB-01 JSON exported,
EWB-no written back. *(Assert the 200-km/midnight rule; assert the "exceeding ₹50,000" boundary.)*

**(E) GSTR-2B reconciliation:** an imported 2B row (supplier GSTIN, inv X, taxable ₹1,00,000, IGST ₹18,000)
matches the purchase voucher within tolerance → **Matched**; a books-only purchase not in 2B → **In-books-only**
(ITC flagged ineligible per §16(2)(aa)); IMS status **Accept** (deemed on no-action). *(Assert advisory — no
ITC posted by the reconciler.)*

**(F) Rule-88A set-off (Circular 98/17/2019):** liability IGST 1,000 / CGST 300 / SGST 300; credit IGST 1,300
/ CGST 200 / SGST 200. IGST credit (1,300) → IGST 1,000, then ₹300 residual to CGST **or** SGST (any
order/proportion) → cash payable = **₹0** in **both** splits (Option 1 leaves ₹100 CGST credit; Option 2 leaves
₹100 SGST credit). GSTR-3B Table 6.1 posts the utilisation; the cash ledger is untouched. *(Assert both splits
reachable + no CGST↔SGST cross-utilisation.)*

**(G) §34 credit note:** original B2B invoice (taxable ₹50,000, 18% → CGST ₹4,500 + SGST ₹4,500) is followed
by a **credit note** for a ₹10,000 price reduction dated within the FY → output tax **reduced** by CGST ₹900 +
SGST ₹900, **linked to the original invoice**, fed to **GSTR-1 Table 9B**; a credit note dated after the
**30-Nov-following-FY** cut-off is **blocked/warned** (§34(2)). *(Assert the directional adjustment, the
original-invoice link, and the 30-Nov guard — ER-12.)*

**(H) ITC reversal (Rule 37, 180-day):** ITC of ₹18,000 availed on a purchase whose supplier is **unpaid for
> 180 days** → reversal of ₹18,000 posted to **GSTR-3B Table 4(B)**, **reducing the electronic credit ledger**,
landed on **DRC-03**; on later payment the ₹18,000 is **re-availed**. *(Assert the reconciler only surfaced the
candidate; the reversal engine posted it — ER-14.)*

> **Realism note (fidelity):** the eight sub-examples are deliberately separate — a single company rarely
> exercises RCM, cess, composition, e-Invoice, 2B, set-off, CDN and ITC-reversal on one voucher, but the engine
> must handle each, and each foots to the paisa and reconciles to its postings.

---

## 7. Dependencies + Regression Risks

### Dependencies
- **Ordered/sequential build:** one migration chain → no parallel slice builds. **Order:** S1 (rates/cess) →
  S2 (RCM) → S3 (composition) → S4 (e-Invoice) → S5 (e-Way) → S6 (2A/2B + IMS) → S7 (set-off/payment) →
  S8 (reports/annual returns) → S9 (gate). S2–S7 each depend on **S1** (rate/cess resolution); S7 depends on
  S2 (RCM cash-only) and S6 (ITC-gate view).
- **Reuse:** **Phase-4 GST** (`GstService`, `GstLineTax`, `Duties & Taxes`, `GstReportSupport`, `Gstr1`/
  `Gstr3b`, the dormant `GstTaxHead.Cess` seam, `GstRegistrationType.Composition` stub); **Phase-5
  `Apex.Ledger.Io`** (JSON+XML DTOs, deterministic writers, `CompanyImportService`); **Phase-7 stat-payment/
  challan** (CPIN/CIN/BRN, `TdsDepositService`) for the GST payment voucher. Extend, do not fork.
- **External:** **A14 leads** (heavy law verification, §2.17 checklist at kickoff + per slice); A10 adversarial
  pass per slice; A12 all git (R4). Confirm the merged `main` (@`39b31e3`, v37) is the branch point.

### Regression risks (ranked)
1. **RCM breaks the base-type→direction 1:1 map (HIGHEST).** A Purchase now yields an **Output** liability —
   every `GstReportSupport.DirectionOf` / exhaustive base-type switch must gain an RCM arm, or 3.1(d)/4A
   silently mis-project. Audit all switches in S2; a green suite can hide it (the Phase-6/7/8 lesson).
2. **Cess ring-fence + three date-windows.** Cess credit leaking into GST heads (or GST credit paying cess), or
   a 40%-slab item wrongly attracting cess after 22-Sep-2025, is a silent correctness bug. Date-select by
   voucher date; assert the ring-fence and the 20-Sep/25-Sep golden pair.
3. **Rule-88A CGST↔SGST cross-utilisation.** Must be **schema-impossible** (§49(5)(e),(f)); the any-order
   residual-IGST split + non-overridable provisos are subtle. Encode the Circular-98 both-splits golden.
4. **RCM cash-only discharge.** The RCM output liability must **not** be nettable against the credit ledger
   (§49(4)/§2(82)); easy to mis-wire into the normal set-off. Assert an explicit guard.
5. **2A/2B inbound path — advisory only.** The first inbound path must **never auto-post ITC/reversals**; a
   reconciler that posts would corrupt the ledger. Assert "no ITC posted" in the golden.
6. **e-Invoice IRN locality / no-amend.** IRN/QR must be stored as-returned (never computed) and the UI must
   **not** offer amend-on-IRP. Pluggable adapter must not hard-fail without creds (R13).
7. **Io silent-drop / import bypass.** Every new master + artefact + inbound snapshot folded into
   CanonicalModel/CanonicalMapper **and** mirrored all-or-nothing into `CompanyImportService` in its slice —
   the recurring A10 Io-bypass class.
8. **ER-13 byte-identical when off.** New columns DEFAULT 0/NULL, new tables empty; legacy-DDL + equivalence
   test per slice; `DowngradeToV9/V11` helpers DROP new tables.
9. **Consignment-value / validity edge cases.** "Exceeding ₹50,000" boundary (₹50,000 not covered),
   date-aware cess in the value, 200-km midnight rule, per-state overrides.
10. **Migration-chain fragility.** v37 is head; one additive `MigrateV(N-1)ToVN` per slice, txn-wrapped, fresh
    DB via `CreateV1`, each proven by a legacy-DDL upgrade test; **Windows-only-passing tests** audited before
    the PR CI gate (the Phase-6 path-separator lesson).
11. **CDN wrong-direction / unlinked / stale.** A §34 credit note must reduce (not raise) output tax, link to
    its original invoice, respect the unjust-enrichment bar, and honour the 30-Nov cut-off — a mis-signed or
    free-floating CDN silently corrupts GSTR-1 9B and the liability (ER-12); assert example (G).
12. **ITC reversal double-post / reconciler auto-reversal.** The reversal engine (S7) is the **sole poster**;
    if the 2B/IMS reconciler (S6) also posts, or Rule 37 re-availment double-counts, the credit ledger drifts
    (ER-14). Assert "reconciler surfaced only; engine posted once" in example (H).

---

## 8. Decision Points for the User (approve before execution)

- **DP-1 — Target FY & GST 2.0 slab set.** *Recommended:* **FY2025-26 with GST 2.0 slabs `0/5/18/40` +
  retained specials `3%/1.5%/0.25%` + the `28%+cess` tobacco carve-out, all config-driven via a dated
  rate-history master**, keeping legacy 12%/28% **inactive-by-date** for reprinting old vouchers (§2.1,
  verifier-confirmed; plan C-9). **Approve, or name a different target FY / slab basis.**
- **DP-2 — Tobacco/pan-masala carve-out.** *Recommended:* **model the `28% + compensation-cess (RSP-based)`
  path as still live in FY2025-26 with an effective-date switch to 40%**, since the FM may notify the 40%
  transition mid-year and it went Nil on 01-Feb-2026 (§2.1/§2.2). **Approve configurable carve-out, or drop
  tobacco fidelity as out-of-scope.**
- **DP-3 — RSP-based valuation.** *Recommended:* **implement RSP-factor cess** (a per-item RSP field + factor
  for pan masala/tobacco) alongside ad-valorem and specific/per-unit, for cess fidelity (§2.2). **Approve, or
  approximate tobacco cess as ad-valorem only (lower fidelity).**
- **DP-4 — Cess engine retention & stranded-credit treatment.** *Recommended:* **keep the full cess engine even
  at Nil** (three dated windows, ring-fenced credit), and make stranded cess-credit treatment at cut-over a
  **user-configurable option** (refund / frozen cess-only balance / write-off) since the lapse-vs-refund law is
  unsettled (SC pending). Exclude the post-01-Feb-2026 non-GST excise/HSNS levies. **Approve.**
- **DP-5 — §9(4) RCM scope.** *Recommended:* **ship §9(4) strictly as the notified real-estate-promoter path
  (80% / cement-always / capital-goods)**, with any generic "unregistered-purchase RCM" toggle **default OFF +
  a warning** (the blanket 9(4) was rescinded 01-Feb-2019 — verifier). Keep §9(5) ECO deemed-supplier a
  **separate/deferred** module. **Approve promoter-only, or additionally expose the generic toggle.**
- **DP-6 — GTA default treatment.** *Recommended:* **default new GTA suppliers to RCM 5% (no ITC)**, with an
  **Annexure-V "forward-charge opted" flag** (18% with ITC) stored with an effective FY; encode the GST 2.0
  removal of the 12% option (§2.3). **Approve, or default GTA to forward-charge.**
- **DP-7 — Self-invoice numbering & auto-issue.** *Recommended:* **a separate self-invoice series, auto-issued
  on RCM-voucher save**, plus a **30-day sweep** (Rule 47A) flagging still-unissued unregistered-supplier RCM.
  **Approve, or reuse the purchase series / issue on a scheduled sweep only.**
- **DP-8 — RCM ITC timing.** *Recommended:* **auto-avail RCM ITC in the period the tax is paid** (self-invoice
  present), with the Sec 16(4) FY = self-invoice-year rule (Circular 211/5/2024). **Approve, or require explicit
  user confirmation before availing.**
- **DP-9 — Composition depth & defaults.** *Recommended:* **new company default = Trader, ₹1.5 cr threshold**;
  deliver **CMP-08 (quarterly) + GSTR-4 (annual) on-screen + offline JSON**; the 8 special-category states
  (incl. Uttarakhand) at ₹75 L as a per-state master; the separate **brick-kiln special composition scheme
  OUT** (§2.4). **Approve, or set a different default sub-type / pull the brick scheme in.**
- **DP-10 — e-Invoice connection mode.** *Approved (hybrid, 2026-07-14):* **offline NIC-JSON export/import is
  the baseline (zero credentials), PLUS an OPTIONAL live path that calls the NIC IRP APIs directly using the
  customer's OWN NIC credentials** — Client-ID/Client-Secret + per-GSTIN API username/password, static-IP-
  whitelist aware, stored **protected-at-rest** (OS keystore / DPAPI) — available only to qualifying
  self-provisioners (**e-Invoice AATO > ₹500 cr**). Both transports sit behind a **pluggable connectivity
  adapter `IGstPortalConnector`** (`OfflineJsonConnector` default + `CustomerNicDirect` live) with a **stubbed
  `GspConnector` seam kept but NOT built** for a future GSP-partner full integration (§1.5, RQ-30, ER-16; cost
  basis: NIC ₹0/txn, future GSP ~₹9,000–50,000/yr per GSTIN). The app **never** holds a GSP/vendor credential
  or the customer's portal password/DSC (ER-16). Offline JSON stays the default + fallback for non-qualifying
  or unconfigured taxpayers.
- **DP-11 — e-Invoice threshold.** *Recommended:* **hold at AATO > ₹5 cr (configurable), NOT the unnotified
  ₹2 cr proposal**; model the 30-day reporting age **only** for AATO ≥ ₹10 cr, independent of applicability
  (§2.5, verifier). **Confirm ₹5 cr + configurable.**
- **DP-12 — e-Way generation mode & 2026 fields.** *Approved (hybrid, 2026-07-14):* **offline EWB-01/EWB-02
  JSON export + write-back as the baseline, PLUS an OPTIONAL live path calling the NIC e-Way APIs directly with
  the customer's OWN NIC credentials** (qualifying self-provisioners, **e-Way AATO > ₹5 cr**; creds stored
  **protected-at-rest** — OS keystore / DPAPI), a **per-state threshold override table** (default ₹50,000), and
  **Ship-To GSTIN + EWB-closure fields added now, gated to 01-Aug-2026** (§2.6). Both transports sit behind the
  same pluggable **`IGstPortalConnector`** adapter (`OfflineJsonConnector` default + `CustomerNicDirect` live)
  with the **stubbed `GspConnector` seam kept but NOT built** (§1.5, RQ-30, ER-16). Offline JSON stays the
  default + fallback.
- **DP-13 — GSTR-2A vs 2B reconciliation basis.** *Recommended:* **import BOTH; make GSTR-2B the ITC-eligibility
  basis (statutory gatekeeper since 01-Jan-2022) and GSTR-2A the supplementary dynamic view**; JSON canonical,
  Excel fallback; **configurable matching tolerance** (§2.7). **Approve.**
- **DP-14 — IMS depth.** *Recommended:* **a full per-line offline IMS mirror** (Accept/Reject/Pending +
  deemed-accept + Oct-2025 remarks/declared-reversal), clearly labelled OFFLINE (real action is on the portal),
  vs buckets-only. **Approve the fuller mirror, or ship reconciliation buckets only.**
- **DP-15 — Reconciliation posture (advisory vs auto-post).** *Recommended:* **advisory only** — the 2B recon
  and ITC-gate view never post ITC/reversals; reversals (rejected CN, Rule 37A) route through the S7
  set-off/stat-adjustment engine. **Approve advisory-only (informational — first inbound path, high risk).**
- **DP-16 — Rule-88A residual-IGST default split.** *Recommended:* **CGST-first default with a user-editable
  Table 6.1 override** (portal-like), the §49(5)(c)/(d) proviso and the CGST↔SGST ban **non-overridable**
  (§2.8). A14 confirms the portal's actual default before hardcoding. **Approve CGST-first + editable, or an
  auto-optimise-to-minimise-cash default.**
- **DP-17 — Rule 86B (1% cash cap).** *Recommended:* **a configurable company/period flag** that, when
  triggered (monthly taxable supply > ₹50 L, no exemption), forces ≥ 1% of output tax through cash **after**
  set-off. **Approve.**
- **DP-18 — Annual returns depth.** *Recommended:* **light GSTR-9 / GSTR-9A / GSTR-9C projections** (9C
  reconciliation-statement distinct from 9/9A — plan C-6), on-screen + offline JSON. **Approve, or defer annual
  returns to a later phase.**
- **DP-19 — QRMP + IFF scope.** *Recommended:* **include QRMP election + IFF (invoice furnishing facility) and
  quarterly-2B behaviour**, offline. **Approve, or defer QRMP/IFF.**
- **DP-20 — GST TDS §51 (GSTR-7) / TCS §52 (GSTR-8).** *Recommended:* **OUT** of Phase 9 — a separate
  withholding module, not core advanced GST (deferred from Phase 7). **Approve OUT, or pull GSTR-7/8 in.**
- **DP-21 — §206C(1G) LRS / overseas-tour TCS.** *Recommended:* **OUT** — this is income-tax TCS (Phase-7
  domain, D6 deferral), not GST (§1.3). **Approve OUT, or schedule it into Phase 9.**
- **DP-22 — Multi-GSTIN / branch registration.** *Recommended:* **defer** — the app is single-company; model a
  single GSTIN and treat multi-GSTIN as a later phase. **Approve defer, or pull multi-GSTIN in.**
- **DP-23 — Per-tax-ledger rounding method.** *Recommended:* **keep the existing total-then-split paisa
  rounding** (`RoundToPaisa`, compute-once-then-derive) for CGST/SGST/IGST/Cess, no per-ledger method choice.
  **Approve, or expose a configurable rounding method per tax ledger.**
- **DP-24 — GST Rate Setup bulk screen.** *Recommended:* **include the bulk mass-HSN/rate-update screen**
  (plan.md C-6, binding). **Confirm in scope.**
- **DP-25 — Offline / connectivity posture (informational).** *Approved (hybrid, 2026-07-14):* **returns /
  GSTR-1 / GSTR-3B / GSTR-2A / GSTR-2B / IMS remain STRICTLY offline JSON** (export/import under the customer's
  own portal login — no live GSTN/TRACES round-trip), consistent with plan.md §1.3 / Open Q4; **e-Invoice and
  e-Way Bill MAY additionally go live via the customer's OWN direct NIC credentials** (DP-10 / DP-12), and a
  **pluggable `IGstPortalConnector` adapter with a stubbed `GspConnector` seam is preserved** so a future
  GSP-partner integration (returns / 2A / 2B / IMS + sub-threshold e-Invoice/e-Way) can be added **without
  rework** — not built in Phase 9 (§1.5, RQ-30, ER-16). **Approve (informational — bounds e-Invoice/e-Way/2B/
  IMS depth).**
- **DP-26 — ISD (mandatory 01-Apr-2025) + GSTR-6.** *Recommended:* **defer** — ISD distribution + GSTR-6 is
  bound to multi-GSTIN (DP-22) and the single-company model, but the **mandatory-from 01-Apr-2025** date (§2.13,
  Finance Act 2024 §2(61)/§20) is flagged so this is a **conscious deferral, not a silent omission**. **Approve
  defer, or pull ISD/GSTR-6 in (implies multi-GSTIN scope).**
- **DP-27 — Credit/Debit Note (§34) build posture.** *Recommended:* **newly formalise CDN as a §34 GST
  document in Phase 9** — original-invoice link, directional output-tax adjustment (unjust-enrichment bar),
  §34(2) **30-Nov** guard, GSTR-1 9B/9C — rather than silently reusing the Phase-4 sales/purchase-return
  voucher (which lacks the link/guard/9B mapping). **Confirm the Phase-4 gap at kickoff (A14/A10); approve
  newly-built, or confirm Phase-4 already carries §34 fidelity.**
- **DP-28 — B2C Dynamic QR.** *Recommended:* **model the self-generated dynamic (UPI) QR on B2C invoices gated
  to AATO > ₹500 cr** (Notn 14/2020-CT), **distinct from the IRP e-Invoice QR and outside the IRN flow**
  (§2.12). **Approve, or scope B2C dynamic QR out (state it explicitly rather than leave it unaddressed).**
- **DP-29 — Zero-rated / inverted-duty refund posture.** *Recommended:* **keep the zero-rated/SEZ/deemed-export
  supply *classification* in scope (RQ-11) but defer the *refund application* forms** — RFD-01 (LUT vs IGST-
  paid) and inverted-duty §54(3)(ii)/Rule 89(5), the latter made prominent by GST 2.0's 5%-out/18%-in
  compression (§2.15). **Approve defer/flag, or pull RFD-01 + inverted-duty computation into Phase 9.**
- **DP-30 — ITC reversal engine depth.** *Recommended:* **build Rule 42/43 (common-credit apportionment, D1/D2
  monthly + annual true-up, capital-goods 60-month) + Rule 37 (180-day) + Rule 37A (supplier-non-payment)**,
  posting to GSTR-3B Table 4(B) via the set-off engine / **DRC-03**, with the reconciler advisory-only (§2.11).
  **Approve full reversal engine, or ship Rule 37A-only + manual Rule 42/43 (lower fidelity).**
- **DP-31 — §17(5) blocked credits.** *Recommended:* **a per-purchase ITC-eligibility flag + blocked-credit
  determination** (motor vehicles, F&B, works-contract, personal consumption, etc.) beyond the 2B "ITC-avail"
  flag, feeding GSTR-3B 4(B)(1)/4(D) (§2.11). **Approve, or rely on manual user tagging only.**
- **DP-32 — GST on advances.** *Recommended:* **charge advance tax on services only** (goods de-taxed, Notn
  66/2017-CT) with **Receipt/Refund vouchers (Rule 50/51)** and advance→invoice adjustment (11A/11B, §2.10).
  **Approve, or treat advances as a compute-only note without receipt-voucher documents (lower fidelity).**
- **DP-33 — GSTR-1/3B amendment handling.** *Recommended:* **model the amendment tables (9A/9C/10/11) +
  prior-period correction as projections** so RQ-18's "route to GSTR-1 amendment" has a real target; reflect
  GSTR-1A + Table-4 hard-lock currency at kickoff (§2.16, RQ-29). **Approve, or defer amendment tables (leaving
  the RQ-18 routing promise unbuilt).**
- **DP-34 — Interest §50 posture.** *Recommended:* **flag-only, not auto-computed** — §50(1) on late RCM cash
  discharge / late deposit and §50(3) on wrongly-availed ITC are surfaced as warnings (carry-forward), since
  RCM's hard 30/60-day time-of-supply makes late-payment interest a realistic FY2025-26 exposure (§2.14).
  **Approve flag-only, or require §50 auto-computation in Phase 9.**

---

## 9. Appendix — key reference files to mirror (relative to repo root)

Services/engine: `src/Apex.Ledger/Services/{GstService,TdsService,TdsDepositService,VoucherValidator}.cs`
(RCM/Cess branch in `GstService.ComputeInvoiceTax`/`AddHead`/`EnableGst`/`EnsureTaxLedger`).
Domain: `src/Apex.Ledger/Domain/{GstConfig,GstEnums,GstRateSlab,GstLineTax,PartyGstDetails,
StockItemGstDetails,LedgerGstClassification,EntryLine,Voucher,VoucherBaseType,VoucherType,Gstin,Ledger,
Company}.cs`.
Reports: `src/Apex.Ledger/Reports/{TaxAnalysis,Gstr1,Gstr3b,GstReportSupport,ClassificationRules}.cs`;
seed `src/Apex.Ledger/Seed/SeedGstRates.cs`.
Persistence: `src/Apex.Persistence.Sqlite/{Schema,SqliteCompanyStore}.cs` (CurrentVersion 37→44;
MigrateV37ToV38…MigrateV43ToV44).
Io: `src/Apex.Ledger.Io/{CanonicalModel,CanonicalMapper,CanonicalXml,CanonicalJson,CompanyImportService,
FvuWriter,EcrWriter,TabularExport,CsvWriter,ReportPdf,IndianAmountInWords}.cs` (new INV-01 / EWB-01 / GSTR
writers as siblings).
UI: `src/Apex.Desktop/ViewModels/{ReportsViewModel,VoucherEntryViewModel,PosBillingViewModel}.cs`.
Tests: `tests/Apex.Persistence.Sqlite.Tests/{JobWorkSchemaTests,TdsSchemaTests,ItemInvoiceRoundTripTests,
InventoryVoucherRoundTripTests}.cs` (+ `SchemaMigrationEquivalenceTests`; update `DowngradeToV9/V11`).
Corpus: `docs/tally-feature-catalog.md` §12 + `docs/tally-feature-catalog-verification-report.md` A17–A21 +
(B)CORE; `tally/` GST PDFs (read via `pdftotext -layout`, A14, never commit).

---

*Change log: Phase 9 (GST-advanced) requirements + slice plan drafted 2026-07-14 via `/software`, grounded in
`docs/tally-feature-catalog.md` §12 + verification-report A17–A21, with FY2025-26 (post GST 2.0, 22-Sep-2025)
statutory law web-verified per R7 (sources + as-of dates in §2; adversarial-verifier corrections adopted over
research where they conflicted). PLAN-ONLY — no code executed. Schema head confirmed v37 (Schema.cs:86, merge
`39b31e3`); Phase 9 adds v38→v44. Awaiting user approval of DP-1…DP-34 before build. Any deviation during
execution is recorded in `memory.md` with its reason (R6).*

*Revision (2026-07-14, gap-closure pass): closed critic-identified breadth gaps — **B2C Dynamic QR** (Notn
14/2020-CT, ₹500 cr, distinct from IRP QR: RQ-28, DP-28, §2.12, ER-15); **§34 Credit/Debit Notes** as a
first-class GST document (RQ-24, DP-27, §2.9, ER-12); **PMT-06 + DRC-03** named in the payment/reversal
machinery (RQ-22, S6/S7, §2.14); **ISD + GSTR-6** mandatory-01-Apr-2025 surfaced as an explicit out-of-scope
DP (DP-26, §2.13, §1.3); **ITC reversal engine** Rule 42/43 + Rule 37/37A + **§17(5)** blocked credits (RQ-26,
RQ-27, DP-30, DP-31, §2.11, ER-14); **GST on advances** §13 Receipt/Refund vouchers (RQ-25, DP-32, §2.10);
**zero-rated/inverted-duty refund** posture (DP-29, §2.15); **GSTR-1/3B amendment tables** (RQ-29, DP-33,
§2.16); **§50 interest** promoted from a buried out-of-scope line to an explicit DP (DP-34, §2.14). New law
subsections §2.9–§2.16 are ⚠ RE-VERIFY (outside the supplied verifier set) — A14 confirms at kickoff; the
re-verify checklist moved to §2.17 with items 10–14 added. Slice placement: CDN/advances→S2, B2C-QR→S4,
§17(5)/reversal-surfacing→S6, reversal engine/PMT-06/DRC-03→S7, amendments→S8 — all additive within existing
migrations (still v37→v44).*

*Approval + connectivity amendment (2026-07-14): the user **APPROVED Phase 9 at ALL recommended Decision-Point
defaults (DP-1…DP-34)** with **one amendment to the connectivity posture** — moved from "offline-only (adapter
stubbed)" to a **HYBRID** stance, recorded authoritatively in new **§1.5**. Offline JSON stays the baseline for
every GST feature and the **strict** posture for **returns / GSTR-1/3B / GSTR-2A / GSTR-2B / IMS**, while
**e-Invoice and e-Way Bill gain an OPTIONAL live path over the customer's OWN direct NIC credentials**
(qualifying self-provisioners — e-Invoice AATO > ₹500 cr, e-Way AATO > ₹5 cr; NIC ₹0/txn), all behind a
**pluggable `IGstPortalConnector` adapter** (`OfflineJsonConnector` default + `CustomerNicDirect` live) with a
**stubbed `GspConnector` seam kept but NOT built** for a future GSP-partner integration
(~₹9,000–50,000/yr per GSTIN; closest analogue Tally "Connected GST" via TSS ₹4,500–13,500/yr per seat).
New **RQ-30** (connectivity adapter) and **ER-16** (security invariant: no GSP/vendor credential, no customer
portal password/DSC held — taxpayer's own OTP/DSC only; live calls use only customer-configured NIC API creds
stored protected-at-rest / OS keystore / DPAPI). Amendments: **§1.5** added; **DP-10 / DP-12 / DP-25** rewritten
and marked approved; **§2.5 / §2.6** each gain a connectivity bullet; **S4 (v40→v41) / S5 (v41→v42)** slice
scope + schema ALTERs extended with the connector-mode + protected-at-rest NIC-credential columns (**no new
schema versions**). All other content unchanged; every interface/class identifier proposed is **de-branded**.*
