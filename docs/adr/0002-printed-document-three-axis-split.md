# ADR 0002 — Printed documents: the three-axis split (entitlement / rendering / orientation)

- **Status:** Accepted. The DECISION is accepted; the status of the WORK is the ledger on the next line and
  nowhere else in this file. Slice **S0** of the T0-11 chain records the decision, slices **S1–S4** are its
  allocated implementation, and **S5** is deliberately deferred and recorded rather than left silent.
  ~~*"**S1–S4** implement it"*~~ — **struck 2026-08-21, T0-11 review C25/L3-11.** That clause was written at
  **S0**, before anything shipped, as a forward-looking ALLOCATION; no amendment revised it while this ADR
  grew **dated completion blocks** (*"AMENDMENT (slice S1)"*, *"AMENDED BY SLICE S2 — the record document is
  BUILT"*), which turn an ADR into a status-carrying document a reader takes at its word. **S3 and S4 have
  not shipped**, the only unbuilt work the Status line named was S5, and nothing anywhere in this file said
  so — while `plan.md` (no ✅ on S3/S4) and census rows 4.6 / 12.2 (*"STILL PARTIAL"*) both recorded the gap
  honestly. This ADR was the sole outlier, and it is the one `plan.md` makes mandatory reading first.
- **Slice status (machine-checked against `plan.md` Phase 10.13 by
  `tests/Apex.Ledger.Tests/SliceStatusClaimTests.cs`):** S0 SHIPPED · S1 SHIPPED · S2 CODE-COMPLETE ·
  S3 NOT-YET-BUILT · S4 NOT-YET-BUILT · S5 DEFERRED. **S2 is CODE-COMPLETE and not SHIPPED because its
  governing R12 question — whether a purchase record states the supplier's tax — is ASKED AND OUTSTANDING
  (see the open-decision bullet below and `plan.md` Phase 10.13); the code shipped the recommendation ahead
  of the ruling.** The test asserts this ledger BOTH ways against `plan.md`'s completion stamps, so an
  over-claim and an under-claim are equally red.
- **Date:** 2026-08-20
- **Phase:** 10.13 (T0-11 — printed documents for recipient-side vouchers), `plan.md`
- **Deciders:** A14 (Tally domain / corpus) + A13 (technical writer), on the grounded T0-11 design pass
  (corpus + CGST Act + a code survey). Two objections raised by the reviewing architect — **OBJ-3** and
  **OBJ-4** below — are binding and **override the design document where they conflict with it**.
- **Related:** `docs/phase5-reports-io-requirements.md` **RQ-11 / RQ-11a / RQ-11b**;
  `docs/full-clone-census.md` rows **4.6, 4.7, 12.2** and gap-register **T0-11**; CLAUDE.md **R6** (the plan
  is the single source of truth), **R7** (fidelity), **R8** (test-driven), and standing **user ruling 9**
  (behaviour the corpus cannot settle ships as a **documented divergence labelled as OURS** and can never
  join the verified set).

---

## Context

Gap-register row **T0-11** says a Purchase item-invoice, a Credit Note and a Debit Note *"never print in
invoice format"* and blames the print gate. **The symptom is real and worse than stated; the cause is wrong
and the row bundles two different defects under one id.**

**The symptom, verified end to end.** `GstReportSupport.IsTaxInvoice`
(`src/Apex.Ledger/Reports/GstReportSupport.cs:1636`) returns false for anything whose base type is not Sales.
The printer's wrapper is a **pure forward** to it (`src/Apex.Desktop/Services/VoucherPrintProjector.cs:116-117`),
so `BuildPrintPreview` (`src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107`) takes the else branch
into the plain voucher projection, whose only loop walks the accounting `Lines`. The voucher's
**`InventoryLines` are never read on that path at all**, `VoucherPrintData` has nowhere to put them, and the
voucher PDF can only draw a Particulars / Debit / Credit table. **This is not a predicate flip. It is a
missing projection at three layers.**

**The implied fix — widen `IsTaxInvoice` to Purchase — is forbidden, for two independent reasons.**

1. **Statute.** CGST Act **§31(1)** puts the tax invoice on *"a registered person **supplying**"*. On a
   Purchase we are the **recipient**. Titling a supplier's document as *our* tax invoice is a false
   statutory statement of exactly the class FIX-W1e already cost this project once.
2. **🔴 THE THREE-CONSUMER HAZARD — the one the census never saw, and the reason the naive fix is
   *dangerous* rather than merely wrong.** `IsTaxInvoice` has **three** consumers that move together:
   - the **printer**, through the pure forward above;
   - **`IsBillOfSupply`'s limb 2**, which gates on it at
     `src/Apex.Ledger/Reports/GstReportSupport.cs:1340` (`if (!IsTaxInvoice(company, voucher)) return false;`);
   - the **NIC e-Way portal document code**, because `IsBillOfSupplyForFiling`
     (`src/Apex.Ledger/Reports/GstReportSupport.cs:1390`) feeds `EWayBillService.PartACodesFor` at
     `src/Apex.Ledger/Services/EWayBillService.cs:482`.

   So flipping the Sales gate would **also** title a wholly-exempt purchase **"BILL OF SUPPLY"** — a document
   CGST **Rule 49** likewise puts on the *supplier* — **and silently move the `docType` we file with a
   government portal.** One edit, three consequences, only one of them intended. **The method's NAME is the
   conflation.**

**The real diagnosis.** `IsTaxInvoice` is **not wrong**. *Sales-only is the CORRECT answer to the question it
is named for* — *"are we entitled to issue a Rule-46 tax invoice?"* Its only sin is being **used** at
`src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:104-107` to answer a **different** question — *"should
this document render with item detail?"* **The defect lives at the call site, not in the predicate.**

---

## Decision

**One question was doing the work of three. Split it into three axes, and answer each from its own source.**

### Axis 1 — ENTITLEMENT: *are we entitled to ISSUE this document under law?*

| Voucher | Entitled? | Statutory basis |
|---|---|---|
| **Sales** (item or accounting invoice) | **YES** — tax invoice, or a bill of supply where the supply or the dealer is exempt | CGST Act **§31(1)/(2)**; **§31(3)(c)**; Rule 46; Rule 49 |
| **Purchase**, registered supplier, forward charge | **NO** — the supplier issues; we hold a **record** | CGST Act **§31(1)** (*"a registered person supplying"*) |
| **Purchase**, exempt goods or composition counterparty | **NO** — and specifically **not** a bill of supply, which Rule 49 also puts on the supplier | CGST **Rule 49** |
| **Purchase** under reverse charge from an **UNREGISTERED** supplier | **YES — the one exception, and it is DEFERRED** | CGST Act **§31(3)(f)** with **Rule 47A** |
| **Credit Note**, sales return | **YES** (the section's *"may"*) | CGST Act **§34(1)** — *"the registered person **who has supplied**"* |
| **Debit Note**, upward revision of our **own sale** | **YES** (*"shall"*) | CGST Act **§34(3)** |
| **Debit Note**, purchase return | **NO** — the statutory document is a credit note issued by **our supplier**; ours is a record | CGST Act **§34** |

**This axis is what `GstReportSupport.IsTaxInvoice` already answers, correctly, for the outward case. It is
therefore NOT EDITED by this chain — only re-documented to say that it answers entitlement and must never be
used to choose a renderer.** That decision is what freezes the NIC e-Way `docType` and the bill-of-supply
rule at zero risk.

**🔴 Base type alone cannot decide debit-note entitlement.** The two Debit Note rows above have the *same*
base type and *opposite* entitlement. The discriminator is the **ORIGINAL document**: resolve the persisted
credit/debit-note link to the original voucher and read **its** base type — original **Sales** ⇒ we supplied
⇒ entitled; original **Purchase**, or the link absent ⇒ **record**. The adjustment-direction enum is **not**
the field to key on: it encodes direction, not entitlement.

### 🔴 AMENDMENT (slice S1, 2026-08-20) — the role axis has THREE values, not two

This ADR specified the role as `Issued | Recorded`. Building the seam under a byte-identity constraint showed
that pair cannot express what the app already does: there is a shipped outcome in which **neither** statutory
document may be issued and the voucher prints as the plain Dr/Cr page naming no document kind at all. Two
reachable shapes take it — an ordinary As-Voucher sale, and the §10 contradiction (a composition dealer's
outward supply that nonetheless recorded forward tax), the latter already pinned by a test asserting it prints
*no statutory title at all*. Collapsing it into `Recorded` would assert that a plain voucher is a
recipient-side record document, which is a different and false statement. The enum therefore ships as
`NoStatutoryDocument | Issued | Recorded`.

Two further S1 notes for whoever implements S2 and S4:

- **`Recorded` and `WeAreRecipient` are declared but unreachable**, and a test asserts they are unreachable.
  That assertion *is* S1's contract — a refactor that may move no bytes must not be able to create a document
  class. S2 flips it by changing the classifier, and only the classifier.
- **The record carries a `ScreenLabel` alongside `Title`.** The drill badge spells the same decision
  differently ("Bill of Supply", not "BILL OF SUPPLY"), and a mechanical title-case of the printed title
  yields "Bill Of Supply". Deriving one from the other would put a second derivation back into the view
  model — the very drift this ADR exists to remove — so both spellings ride on the one record.

### Axis 2 — RENDERING: *should this document render WITH ITEM DETAIL?*

**Orthogonal to entitlement, and that orthogonality is the whole point of this ADR.** A purchase item-invoice
posts real stock lines; a reader needs them to verify the input tax credit being claimed. Entitlement says we
may not *issue* the document; it says nothing about whether the document shows items. The corpus affirms the
behaviour is wanted (BOOK PDF p.33 Purchase F9, *"item wise bills can be printed"*) while showing **no
printed specimen** of it.

Conversely a **Credit / Debit Note is entitled and still renders at value level**, because CGST **Rule 53**
requires only the nature of the document, the corresponding tax-invoice serial and date, and the value, rate
and amount credited or debited — **no HSN, no quantity, no per-item lines**.

### Axis 3 — ORIENTATION: *whose identity HEADS the document — the customer's or the supplier's?*

On every shipped Sales document **we** are the supplier and the seller block is stamped from the company. On
a **Purchase record the supplier heads the document** and we sit in the recipient block. Without this axis a
purchase record would print **our GSTIN as the supplier's** — the FIX-W1e failure class, on paper. Basis:
CGST **Rule 46**'s first particular is *"name, address and GSTIN of the **supplier**"*.

The same axis drives the **suppression set**, because Rule 46's place-of-supply, address-of-delivery,
reverse-charge and signature particulars are all *supplier* particulars: on a record we suppress the **place
of supply**, **our declaration** and **our signature**. We do not determine the place of supply of a supply
made **to** us, and the signature on a supplier's document is the supplier's.

---

## 🔴 OBJ-3 (BINDING) — the document number is dual, and the caption is the honest half

A **record** document is headed by the **SUPPLIER**. A field captioned *"Invoice No."* carrying **our**
voucher number, under the supplier's identity, is therefore **a false statement** — not a cosmetic label.

**The ruling:** the **supplier's** number goes in the existing **ReferenceNo / ReferenceCaption** pair, whose
helper at `src/Apex.Desktop/Services/VoucherPrintProjector.cs:1125-1126` **already returns *"Supplier Invoice
No."* for a Purchase**; and the number field itself **acquires a caption** so ours reads **"Our Record Ref."**
A caption is presentational, so the *no-new-money-fields* rule survives intact.

*(Whether the reference product prints its own number or the supplier's on this document is **UNREACHED** in
all ten corpus PDFs. The choice above is decided on the statute, not on fidelity.)*

## 🔴 OBJ-4 (BINDING) — Rule 53's clause lettering is UNREACHED and must not be written down

The **SUBSTANCE** of the Rule 53(1A) particulars used above is verified at primary source. **The CLAUSE
LETTERS ARE NOT.** `taxinformation.cbic.gov.in` fails TLS chain verification for both a fetch tool and plain
`curl`; `cbic-gst.gov.in/pdf/CGST-Rules-2017-Part-A.pdf` returns **404**; and the CBIC consolidated rules PDF
that *does* read cleanly is updated only to **30-09-2020**, so it contains neither Rule 47A nor the 2024
§31(3)(f) Explanation.

**Therefore: cite "Rule 53" and mark the lettering UNREACHED. Do NOT write a clause letter into a code
comment, a test name, a requirement, or a printed legend.** A second reader must re-verify every quoted limb
before any of it becomes a constant. **This project has already had to strip mis-attributed citations out of
shipped code once**; the rule exists because of that, not in anticipation of it.

---

## Ruling-9 divergence register — what is OURS and can never join the verified set

The corpus names **no title** for a purchase print, a credit note or a debit note; shows **no specimen**; and
evidences **no law-driven title derivation** anywhere. The only title mechanism it attests is a free-text
per-voucher-type *"default title to print"*, which carries four unrelated values across the corpus. So:

1. **The title strings themselves — `PURCHASE RECORD`, `PURCHASE RETURN RECORD`, `CREDIT NOTE`, `DEBIT
   NOTE`.** **OURS.** *(Ruling-9 category: corpus SILENT, ours by design.)* The word "Tally" appears in none
   of them (ER-11).
2. **The three-axis split itself.** **OURS** — the corpus documents no such distinction.
3. **Suppressing the tax-charged columns, our declaration and our signature on a recipient-side record.**
   **OURS.**
4. **Rendering item detail on a note above Rule 53's value-level minimum**, if we ever do. **OURS.**
5. **Using the persisted original-invoice link as the debit-note entitlement discriminator.** **OURS** — the
   corpus supplies both debit-note directions as facts (BOOK PDF p.60) but **no rule for telling them apart
   at print time**.
6. **The §31(3)(f) conjunction as an implemented predicate.** **OURS.**
7. **The 30-day / per-supply self-invoice discipline.** **OURS.**
8. **Rule 53(3)'s "input tax credit not admissible" legend.** **NOT BUILT** — no field exists; recorded as a
   known gap, not as a divergence.

**These two R7 categories stay strictly apart, per §1.3 of the census:** *"corpus silent, ours by design"* is
a **different claim** from *"the corpus attests X and we deliberately ship a narrower Y"*. Every item above is
the **first** category.

---

## Consequences

**Positive**

- **The NIC e-Way `docType` and the bill-of-supply rule do not move.** `IsTaxInvoice` and `IsBillOfSupply`
  are doc-only changes; the classifier **consults** them. The highest-consequence risk in the whole chain is
  retired by structure rather than by care.
- **Screen and paper cannot drift.** The badge (`src/Apex.Desktop/ViewModels/VoucherDetailViewModel.cs:67`),
  the PDF title and the on-screen preview mirror all re-derive from **one** classification record. Today each
  derives the document kind independently — the exact split that produced FIX-W1e.
- **The Credit / Debit Note document is DECOUPLED from census T0-10.** A note cannot carry inventory lines at
  all: `src/Apex.Ledger/Services/VoucherValidator.cs:257-259` throws on every post (reached from
  `src/Apex.Ledger/Services/VoucherValidator.cs:150-151`) and
  `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:67-68` makes the item-invoice chord inert. Because
  Rule 53 is value-level, a **legally complete note ships without waiting for T0-10**.
- **No schema change, and no collision with the contested version number.** Every discriminator already
  exists on the posted voucher or in an existing table; the classification is computed at print time and
  never stored.
- **A refutation is recorded rather than repeated.** Census rows 4.7 and 12.2 blamed the print gate for the
  notes. That is refuted here and re-attributed to T0-10 in the census itself.

**Negative / risks & mitigations**

- **A test in the repository currently asserts the defect as CORRECT** (the service accounting-invoice print
  suite asserts an empty item set and the plain-voucher print kind as HEAD's behaviour). Re-pointing it is
  indistinguishable in a diff from this project's documented failure mode — *a golden edited to match the
  code*. *Mitigation:* it is re-pointed in **its own slice (S3)**, justified by **S0's amended RQ-11** and
  never by the new code, and it needs an explicit reviewer callout rather than a quiet edit.
- **There is ZERO existing regression cover on the purchase print shape** — no test in the repository posts a
  Purchase voucher with inventory lines and prints it, and PDF assertions are substring probes, not byte
  goldens. *Mitigation:* the oracle is built **before** the fix, from the fixture's own arithmetic; matrix
  tests must assert the **answer**, never merely that two layers agree.
- **ER-13 byte-identity on already-issued Sales documents.** *Mitigation:* **S1 is a deliberate
  zero-behaviour-change slice** whose only job is to prove nothing moved, shipped before any new document
  exists. The residual exposure is the PDF title branch, which must accept record titles **without**
  weakening its case-insensitive refusal to print "TAX INVOICE" on a non-entitled document.
- **A crash on the new route was considered and RETIRED.** The invoice projection refuses a §10 contradiction
  by throwing; a Purchase now reaches that line. It cannot fire: the composition predicate requires
  `BaseType == Sales` (`src/Apex.Ledger/Reports/GstReportSupport.cs:552`). *Mitigation:* pin it with a test
  so a future edit to the composition limb cannot silently make purchase printing throw.

**Neutral / explicitly not decided here**

- **No new UI surface.** Everything lands behind the existing Vouchers → drill → Print cascade, so the
  Miller-column and keyboard contracts are untouched.
- **Whether a purchase record shows the tax the SUPPLIER charged, or suppresses all tax**, is an open user
  decision (recorded in `plan.md` Phase 10.13). Either way the constraint binds: any tax shown must be
  captioned as tax the **supplier** charged, never as tax **we** charged.

---

## Deferred, and recorded rather than silent

**CGST §31(3)(f) self-invoice and §31(3)(g) payment voucher are NOT built.** These are documents the law
obliges the *recipient* to issue. The deferral is deliberate and it is cheap, for a measured reason:

**the item-invoice acceptance path contains no reverse-charge construction at all** — reverse charge is built
only on the as-voucher and accounting-invoice paths — **so an RCM purchase can never today BE an item
invoice. The shape is unreachable by any user, and therefore no existing book is non-compliant through this
path.** That is *why* deferring costs nothing, and it is written down so the deferral is not mistaken for
negligence.

Building it needs three things this chain cannot absorb: a reverse-charge path on the item-invoice
acceptance; a change to the posted-rate reader, which **deliberately skips reverse-charge legs** and would
otherwise print **zero tax** on a self-invoice — the opposite of Rule 46's tax particulars; and persistence
of the §9(3)-vs-§9(4) limb, which is computed and then thrown away, i.e. a schema column. Inferring that limb
from the party's **live** master is refused: the registration type is editable after posting, so a
self-invoice's statutory basis would be re-derived from mutable data at reprint time. **Persist the limb, or
do not issue the document.**

**What ships in this chain instead:** a classifier branch that **REFUSES** to title any purchase a
self-invoice unless the persisted facts support the conjunction *"liable under §9(3)/(4) **AND** supplier
unregistered"*, plus the compliance gaps written into `plan.md` and the §1.3 fidelity row.

---

## ▶ AMENDED BY SLICE S2, 2026-08-20 — the record document is BUILT, and building it corrected the decision in three places

> **▶ 🔴 STATUS OF THIS AMENDMENT, ADDED 2026-08-21 (T0-11 review C25/L3-11).** BUILT is not DONE here. S2 is
> **CODE-COMPLETE**, and the ledger in the Status block above is the single place this file states that. Its
> governing R12 question — *"whether a purchase record shows the tax the SUPPLIER charged, or suppresses all
> tax"*, the open decision recorded above at the head of this ADR — **is ASKED AND OUTSTANDING**, and correction 1 of this very
> amendment is the answer that shipped ahead of it. **S3 (the purchase accounting/service record) and S4 (the
> Rule 53 note) have NOT shipped**: `IsRecipientRecordDocument` returns false unless the voucher has
> inventory lines, so a purchase ACCOUNTING invoice still classifies `NoStatutoryDocument` and prints the
> plain Dr/Cr voucher, and S4's specified *"Original Invoice No"* caption occurs in **zero** files under
> `src/`. Do not read BUILT, here or in the Status line, as covering either.

**S2 shipped the recipient-side record for a PURCHASE ITEM INVOICE.** What the ADR said held, with these
corrections — each recorded because a later reader will otherwise re-litigate them.

1. 🔴 **THE TAX AXIS NEEDED A THIRD VALUE.** The decision above carries `StatesTaxWeCharged` as a boolean,
   defined as the exact negation of *is-a-bill-of-supply*, and that boolean drives **every** tax suppression in
   the projector. A recipient-side record breaks it: the record **must state the tax** — it is what
   substantiates the input tax credit we claim, and a purchase record with no tax on it cannot do its job —
   while the tax is emphatically **not ours**. `true` there would have asserted we charged it; `false` would
   have blanked the figures. It ships as `TaxParticulars { None | AsChargedByUs | AsChargedByTheSupplier }`,
   with `None` exactly where the boolean was false, so every outward document is unmoved.
2. 🔴 **OBJ-3'S CAPTION NEEDED A COMPANION SUPPRESSION THE ADR DOES NOT NAME: THE SIGNATURE BLOCK.** Once
   the party blocks are swapped, the shipped renderer's *"For {seller name} / Authorised Signatory"* prints the
   **SUPPLIER's** name over a signature line on a page **we** produced. That is not a mislabelled caption, it
   is an attestation in someone else's name. CGST Rule 46(q) puts the signature on the ISSUER; the block is
   dropped on a record, and the outward declaration is replaced by a legend that says what the page is.
3. **DECISION 4's "consult `IsInwardBillOfSupply`" IS INERT AND WAS DELIBERATELY NOT IMPLEMENTED.** The title,
   the orientation and the suppression set of a record are identical whether the inward supply was taxed,
   exempt or from a §10 counterparty — Rule 49's bill of supply is the SUPPLIER's document in all three — so
   consulting it would ship a branch **no test could distinguish**. The predicate stays with its only
   consumers, the e-Way engine, and the wholly-exempt-purchase hazard is pinned **by outcome** instead
   (`PurchaseRecordPrintTests.A_wholly_exempt_purchase_is_never_titled_BILL_OF_SUPPLY`).
4. **ONE GUARD WAS ADDED THAT THE ADR DOES NOT MENTION.** The record takes 100% of its tax from posted
   metadata, so a purchase whose Input tax legs carry none would print a Grand Total short of the posted
   **supplier** leg by the whole tax. `PostedInputTaxIsFullyTagged` is the inward twin of the outward guard,
   sharing one body; a voucher that fails it is not a record document and prints the plain Dr/Cr voucher.

**Two more strings join the ruling-9 register above, both category (a) — corpus SILENT, OURS by design:**
the number caption **`Our Record Ref.`** (the prohibition it satisfies is statutory — RQ-11a and CGST Rule
46(a)/(b) — but the wording is ours), the tax caption **`Tax Charged by the Supplier`**, and the record
legend printed where the outward declaration sits. The word "Tally" appears in none of them (ER-11 / RQ-13).
