# Gap decisions — multiple-choice set for the user

**Author:** A1 (Business Analyst) · **Date:** 2026-08-01 · **Branch:** `claude/confident-ellis-dedef5`
**Input:** `docs/tally-version-and-voucher-gap-audit.md` (same session, same author)
**Scope:** read-only. No source, test, `plan.md` or `memory.md` file was modified; no build or test was run.

---

## How to use this document

Each question has a number (**D1**, **D2**, …), 2–4 **mutually exclusive** options, and a **recommendation with
its reason**. Answer with the number and the letter — "D3 = B" — and nothing else is needed.

Questions are grouped by area and **ordered within each area by consequence**. §1 (the yardstick) comes first
because several later answers change meaning depending on it. §2 is **voucher entry**, given its own prominent
section at the user's request.

### What I decided myself, without asking

Per the brief, anything with an obvious right answer or one the corpus already settles is **decided, not asked**.
These are listed in **§10** with the reason. They are not open questions — they are work items awaiting
scheduling. Read §10 so you know what is *not* on the ballot.

### Effort language, and its honesty

Where I say "weeks", I mean it. Where I can anchor an estimate to work this project has actually done, I do —
e.g. "comparable to the voucher-numbering feature", which took five committed slices and one schema bump
(`memory.md` numbering entry, S1–S5, schema v47→v48). Where I cannot, I say **"unestimated"** rather than
inventing a number. No estimate below has been validated by a build or a spike.

### Citation tags (same key as the audit)

`[CODE]` verified by me this session at the stated path · `[CORPUS-BOOK]` `tally/664311548-Tally-Prime-Book.pdf`,
author's printed page numbers, licensed and git-ignored, never quoted at length · `[CORPUS-SG]`
`tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf` · `[OFFICIAL]` Tally Solutions help site, URL given ·
`[AUDIT]` established in `docs/tally-version-and-voucher-gap-audit.md`, section cited · `[UNCITED]` my judgement,
flagged as such.

---

## 1. The yardstick — answer this first

Both questions here are **R12 scope decisions**. Every other answer in this document is read against them.

---

### D1. Which Tally is the acceptance yardstick?

The project targets **TallyPrime**: `CLAUDE.md` names it, and **all ten** corpus PDFs are TallyPrime documents —
there is no 7.2, no Tally 9 and no ERP 9 primary material in `tally/` at all `[AUDIT §2.1]`. The user evaluates
against **Tally 7.2**, a 2005 product. That is roughly twenty years and five product generations of divergence,
and it cuts both ways: Apex has GST, e-invoice, e-Way, IMS and TCS that 7.2 has **no counterpart for**; 7.2 has
VAT, CST and Service Tax that Apex has **zero files for** `[AUDIT §2.4, §4.3]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **TallyPrime stays the yardstick.** 7.2 feedback is triaged through the nine known divergences in `[AUDIT §2.4]` before anything is logged as a defect. | No rework. But the user must accept that some things they "know" Tally does — Ctrl+V / Alt+I as separate mode keys, Credit Note on Ctrl+F8, the 1990s menu tree — are **7.2 behaviours TallyPrime deliberately removed**, and Apex is correct to not have them `[OFFICIAL: help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/]`. Expect friction at every acceptance round until the list is internalised. |
| **B** | **Switch the yardstick to Tally 7.2.** | Roughly half the shipped statutory work — the entire GST surface, 20+ report modules, e-invoice, e-Way, IMS, TCS `[AUDIT §2.4 item 4]` — goes **out of scope**, and a VAT/CST/Service Tax module comes **in**. This is a multi-month scope inversion and it throws away completed, tested, merged work. It also has no corpus support: we own **no 7.2 documentation**, so every fidelity call would be unsourced. |
| **C** | **Hybrid: TallyPrime is the yardstick, but a named 7.2-only list is added as scope.** The user names the specific 7.2 things they actually use (most likely: VAT if their business is pre-GST-era, Tally Audit, TallyVault, ODBC) and those become plan items. Everything else is judged against TallyPrime. | Keeps the shipped work and closes the gaps the user actually feels. Cost is bounded by how long the named list is. Risk: the list grows at each acceptance round unless it is frozen at one R12 gate. |

**Recommendation: C.** A is technically correct but ignores that the user has a real workflow in 7.2 that they
expect to carry over. B destroys completed work for a product line that Tally itself discontinued and that we
have no documentation for. C is the only option that both preserves the build and answers the user's actual
complaint. **Freeze the 7.2 list at one gate** — that is the whole discipline of this option.

> **Note on the pirated copy.** The installed 7.2 at `C:\Users\dkpho\Downloads\Tally7.2` is out of bounds and was
> not opened, listed or launched for the audit or for this document. If the answer is B or C, the 7.2 feature
> list must come from a **legitimate 7.2 manual**, not from the installed product. I could not find a citable
> official 7.2 voucher-type list on the public web `[AUDIT §6.3]` — this is a real sourcing problem for B and C.

---

### D2. Do we implement VAT / CST / Service Tax at all?

Grep across `src/Apex.Ledger` for `VAT`, `CST`, `ServiceTax`, `Excise`, `FBT` returns **zero files** `[AUDIT §4.3]`.
This is 7.2's central indirect-tax module and Apex's largest single absence relative to it.

| | Option | Trade-off |
|---|---|---|
| **A** | **No — never.** VAT/CST/Service Tax were subsumed by GST on 1 July 2017 `[OFFICIAL: help.tallysolutions.com/docs/te9rel60/release_notes_6_0/release_6_0_2.htm]`. No Indian business files them on current transactions. | Zero cost. The user loses the ability to reproduce their historical 7.2 books inside Apex — but they can keep 7.2 for that. |
| **B** | **Yes — a historical/read-only VAT module**, enough to import and report pre-GST data, no new filing capability. | Unestimated but large: state-wise VAT means per-state rate schedules and forms. The corpus contains **nothing** on VAT — TallyPrime dropped it — so every rule would be sourced from outside the licensed corpus, which is exactly the R7 situation that has bitten this project before. |
| **C** | **Yes — full VAT/CST/Service Tax with filing.** | Months. Statutory forms for a tax regime that no longer exists. I recommend against this strongly enough that I would want a written reason before starting. |

**Recommendation: A.** Even under D1=C, VAT should not make the named list. It is a dead tax regime; building it
means sourcing statutory rules with no corpus backing for returns nobody files. If the user needs their old
books, the right answer is data migration or keeping 7.2 read-only alongside — not reimplementing VAT.

---

## 2. VOUCHER ENTRY — the section the user asked for

This is where the audit found the defects that **cost money**. Nine of the 24 predefined types are PARTIAL for
reasons in this section `[AUDIT §3.8]`.

---

### D3. Credit / Debit Notes do not move stock. What do we do?

**The defect, verified by me this session.** `ItemInvoiceStock.Counts()` ends with
`type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales`
`[CODE: src/Apex.Ledger/Services/ItemInvoiceStock.cs]`. The XML comment above it states the rule plainly: "Only
Purchase/Sales base kinds are valid carriers (the validator enforces this at post time)" — and the validator does
`[CODE: src/Apex.Ledger/Services/VoucherValidator.cs:103 → EnsureItemInvoiceValid]`. Consequently a Credit Note
**cannot even carry** inventory lines, let alone move stock.

**What this does to the books.** A sales return credits the customer but leaves the goods off the books; a
purchase return debits the supplier but leaves phantom goods on hand. Closing stock, Balance Sheet and gross
profit all drift, silently, and the drift **compounds with every return**.

**What TallyPrime does.** The corpus documents all three entry modes on both notes, with worked examples:
Credit Note item-invoice, accounting-invoice and as-voucher `[CORPUS-BOOK pp.55, 57, 58]`; Debit Note the same
`[CORPUS-BOOK pp.61, 63, 65]`. The item-invoice examples carry the returned stock item, quantity and rate.

| | Option | Trade-off |
|---|---|---|
| **A** | **Full parity: all three modes on both notes, stock included.** Extend `CanBeItemInvoice` and `CanBeAccountingInvoice` to CN/DN, relax the validator carrier rule, extend `ItemInvoiceStock.Counts()`, and stamp the movement direction (Credit Note ⇒ Inward, Debit Note ⇒ Outward — the mirror of the existing Purchase⇒Inward / Sales⇒Outward stamp `[CODE: ItemInvoiceStock.cs Movement doc-comment]`). | The complete fix, and it also closes the "no invoice modes on CN/DN" gap in one pass. **This is not a small change**: it touches the posting validator, the inventory replay ordering, the stock valuation service and the GST CN/DN linkage that already exists `[CODE: VoucherEntryViewModel.cs:1326, 1338]`. Comparable in size to the numbering feature (five slices) or larger, plus a heavy regression burden — the Robert and Bright fixtures must stay byte-identical. Realistically **weeks, not days**. |
| **B** | **Stock only, via item-invoice mode on CN/DN. Skip accounting-invoice mode on the notes for now.** | Fixes the money-losing half. Smaller surface than A but touches the same four seams — the validator, `Counts()`, valuation, replay ordering — so it saves design time, not risk. Leaves CN/DN accounting-invoice (the credit-note-for-a-service-billing-error case) unbuilt. |
| **C** | **Do not change posting. Add a hard block instead:** reject any attempt to enter a goods return, with a message telling the user to use a Sales/Purchase voucher with reversed quantities. | Cheap and honest — it stops the silent drift, which is the actual danger. But it is **not Tally**, it makes returns clumsy, and reversed-quantity sales corrupt the Sales Register and GSTR-1 the other way. A stopgap, not a fix. |
| **D** | **Leave it.** | Free, and the books stay wrong. Not defensible for anyone running a business on this. |

**Recommendation: A, scheduled as its own phase with its own gate — but only after an oracle harness exists.**
The reason for that qualifier is written in this project's own history: negative-stock valuation was attempted
three times and produced **three different unbounded Balance-Sheet errors, each of which passed the full test
suite** `[AUDIT §4.5; plan.md:1126]`. That happened in exactly this code neighbourhood — inventory valuation
replay. Do not walk into it again with the same method. Build the reference oracle first, then change `Counts()`.

If the appetite for A is not there right now, **B** is a legitimate staging point and **C** is a legitimate
holding action. **D is not** — the drift is silent and compounding, and silent is the part that matters.

---

### D4. Purchase has no Accounting Invoice mode. Service purchases cannot be invoiced.

**Verified by me this session.** `CanBeAccountingInvoice => _type.BaseType == VoucherBaseType.Sales`
`[CODE: src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:80]`. TallyPrime documents the mode on Purchase
`[CORPUS-BOOK p.39 — "Press F9 … Ctrl+H (For Accounting Invoice)"]`.

**Why it is gated off is important, and it is a point in favour of fixing it properly.** The XML comment at
`:71-79` is unusually candid `[CODE, read in full this session]`: shipping the purchase side **silently broke
money**. `TdsPossible` and `DetectTdsShape` read the plain `Lines` collection, which is **empty** in accounting-
invoice mode, so a professional-fee purchase posted with **no §194J TDS carve-out at all**, and RCM
mis-evaluated the same way. Crucially, the comment records that **the purchase-side code was deliberately kept,
dormant behind the gate**, so the slice can be finished by wiring TDS/RCM to the Particulars lines and flipping
the predicate.

**What it costs the user today.** Rent, consultancy, professional fees, freight and audit fees cannot be entered
as invoices at all. The workaround is the raw Dr/Cr grid — slower and more error-prone `[AUDIT §5 Tier 2 item 6]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **Finish the slice: rewire TDS/RCM detection to read the Particulars lines, then flip the predicate.** | The code is already written and dormant — this is the **cheapest high-value voucher fix in the whole audit**. The work is the TDS/RCM rewire and its tests, not a new screen. But the rewire is precisely where the money broke last time, so it needs §194J and RCM regression tests written **before** the predicate flips, and a reviewer pass that specifically re-checks the carve-out. |
| **B** | **Flip the predicate now, fix TDS later.** | **Do not.** This is the exact change that already shipped a silent TDS failure. Named here only so it is visibly rejected. |
| **C** | **Leave it deferred.** | Free. Service purchases stay unenterable as invoices, indefinitely. |

**Recommendation: A.** Best value-to-effort ratio of any voucher-entry item — the implementation exists, only the
tax-detection seam needs work, and the failure mode is already documented so we know exactly what to test.

---

### D5. Single Entry mode is missing on Contra, Payment and Receipt.

Apex gates Ctrl+H to Purchase/Sales only `[AUDIT §3.1; CODE-H: MainWindowViewModel.cs:4841 IsInvoiceableEntry]`,
so Single Entry mode does not exist on the three cash/bank vouchers.

**How central this is to the corpus — I checked this specifically, and it is stronger than the audit implies.**
The Book has dedicated Single-Entry sections for Contra, Receipt and Payment, each with a "Dr & Cr not shown"
note `[CORPUS-BOOK pp.26, 29, 31]`. More tellingly, Single Entry is the routine path in the **worked exercises**
throughout the book, not just the tutorial chapter — `GOT > Voucher > F5 > Ctrl+H > Single Entry Mode > Enter`
and its F6 equivalent recur across the advanced chapters, including on the statutory-payment path
(`… > Single Entry > Ctrl+F`) `[CORPUS-BOOK, multiple worked examples; also CORPUS-SG p.75]`. This is not an
optional display preference — it is how the corpus expects cash and bank vouchers to be entered.

| | Option | Trade-off |
|---|---|---|
| **A** | **Implement Single Entry on all three, as a third `VoucherEntryMode`.** The mode enum and Ctrl+H routing already exist `[CODE: VoucherEntryViewModel.cs `_mode`, VoucherEntryMode]` — this adds a display mode and its Ctrl+H gate, not a new posting path. The comment at `:80` confirms the mode "is transient screen state, never persisted", so **no schema change and no posting change**: same legs, different grid. | Moderate and low-risk *because* posting is untouched — the riskiest thing about it is the keyboard/tab-order work inside the new grid. Should be checked against D14 (type-to-filter) so the two do not collide in the same fields. |
| **B** | **Implement on Payment and Receipt only**, leave Contra double-entry. | Covers the high-frequency cases. Saves little, since the third is the same grid. Arbitrary. |
| **C** | **Leave all three double-entry.** | Free. Every cash and bank voucher stays slower than in Tally, forever, on the most-used screens in the product. |

**Recommendation: A.** High daily value, **no posting or schema risk** (mode is screen state only), and the
corpus treats it as the default path rather than an alternative. This is the safest large usability win available.

---

### D6. Voucher Class — how much of it?

Apex has **no general voucher-class feature**; grep returns only the POS tender-ledger pre-map
`[AUDIT §3.7; CODE: PosConfig.cs:9,39; VoucherType.cs:103]`.

**A finding that changes this decision, which I verified this session.** The corpus's demonstrated use of Voucher
Class is **specifically the interest-accounting workflow**, not general default-ledger classes: alter the Debit
Note type, define a class name, and enable **"Use Class for Interest Accounting"**, then enter with
`Alt+F5 > Select Class > Ctrl+H > As Voucher` `[CORPUS-BOOK, Advance Accounting chapter, author p.121, and the
mirrored Credit Note instruction on the same page]`. I found **no** worked example in the Book of the general
percentage-allocation or default-ledger class. **Apex already has an interest module** (an Interest report and
interest calculation exist `[AUDIT §4.2]`), so the class is the missing front-end to a built engine.

| | Option | Trade-off |
|---|---|---|
| **A** | **Narrow: interest-accounting classes on Credit/Debit Note only**, matching the corpus's one demonstrated use. | Smallest scope that satisfies the corpus. Depends on the Voucher Type master (**D8**) to define the class, so it is naturally sequenced after it. Does not give the user general classes if their 7.2 workflow used them. |
| **B** | **General voucher classes** — named classes per type with default ledgers, percentage-of-value allocations and class selection at entry. | The real Tally feature, and it cuts across Sales, Purchase, Payment and more. Unestimated and large: it changes the voucher-entry data flow on every type, and it needs a schema change plus the Voucher Type master to author the classes. **Weeks.** |
| **C** | **None.** | Free. If the user's 7.2 habit is class-driven invoicing, they will hit this constantly. |

**Recommendation: A**, with B reconsidered only if the user names classes on the D1=C 7.2 list. The corpus
justifies A; B is justified only by the user's actual workflow, which I do not know. **Ask the user directly
whether they used voucher classes in 7.2** — that single answer decides between A and B.

---

### D7. Physical Stock's advertised key is wrong and dead. How far do we go?

The seed says `"F10"` and the menu row prints `"F10"` `[AUDIT §3.3 #16; CODE: SeedVoucherTypes.cs:31]`.
TallyPrime's key is **Ctrl+F7**, and **F10** is "view list of all vouchers or masters"
`[OFFICIAL: help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/]`. In Apex,
Ctrl+F7 is bound to **nothing** and F10 opens Other Vouchers. So the UI advertises a key that does the wrong
thing.

Binding Ctrl+F7 and correcting the seed string is **not a question** — see §10, decided. The open question is
what happens to **F10**.

| | Option | Trade-off |
|---|---|---|
| **A** | **Fix Ctrl+F7 only. Leave F10 as Apex's "Other Vouchers" menu.** | Minimal, safe. F10 stays a deliberate Apex divergence — reasonable, since Other Vouchers *is* a list of vouchers, "close enough in spirit" `[AUDIT §2.4 item 8]`. |
| **B** | **Also re-cut F10 to TallyPrime's semantic** (list all vouchers/masters), and move Other Vouchers elsewhere. | Truer to TallyPrime. But it changes an existing, working, discoverable route, and the Other Vouchers menu is currently the **only** way to reach Memorandum, Reversing Journal and all four Job Work types `[AUDIT §3.2, §3.5]`. Breaking it to chase a shortcut label is a bad trade. |

**Recommendation: A.** Fix the dead key; leave the working menu alone. Record F10 in `memory.md` as a **known,
accepted divergence** so it is not re-raised as a defect at the next acceptance round.

---

### D8. Voucher entry field-level parity — six areas were never assessed. Do we look?

The audit is explicit that these are **absent from its findings because nobody looked, not because they are
fine** `[AUDIT §6.8]`: Sales Order's Order Details / Dispatch Details sub-screens `[CORPUS-BOOK pp.73-74]`; the
Job Work order screens' Process Instruction / Tracking Components / Fill-Components-using-BOM `[pp.83-93]`;
Rejections In's quantity-only (no Rate) asymmetry `[pp.51, 53]`; Delivery Note triplicate print markings
`[p.76]`; Attendance and Payroll Autofill `[CORPUS-SG ~pp.213-214]`; and Credit/Debit Note's "Reason for issuing
note", supplier's note number and Ctrl+I original-invoice reference `[CORPUS-BOOK pp.54-66]`.

**Why this matters more than it looks.** Five of the thirteen types rated FULL are rated so on **posting-
semantics evidence only** `[AUDIT §3.8]`. Sales Order already dropped from FULL to PARTIAL when someone looked
closely. The "13 FULL" number is an upper bound, and the true figure is unknown.

I did confirm the CN/DN reference fields exist in the corpus's worked examples — the item-invoice Credit Note
carries a **Buyer's Debit Note No** and its date `[CORPUS-BOOK, Credit Note item-invoice example]`. Whether Apex
has them, I did not check.

| | Option | Trade-off |
|---|---|---|
| **A** | **Commission a field-level parity sweep** across all six areas before any of D3–D6 is scheduled. | Read-only, bounded, no code risk. Will almost certainly demote some FULL ratings and may find defects cheaper to fix than the ones already known. Cost is analyst time and it delays the start of build work. |
| **B** | **Sweep only the two areas adjacent to work we are already doing** — CN/DN reference fields (feeds D3) and Sales Order sub-screens. | Cheaper, and the findings land where they are usable. Leaves Job Work, Delivery Note and the autofills unassessed — Job Work is four of the thirteen FULL ratings, so the headline number stays unreliable. |
| **C** | **Don't sweep.** Fix what is known. | Fastest to first fix. Accept that "13 FULL" is a number we cannot defend, and that field gaps will surface as user complaints instead of as planned work. |

**Recommendation: A**, and specifically **before** D3 is scheduled, not after. If the sweep finds that CN/DN also
lack their reference fields, that is the same slice of work as D3 — discovering it afterwards means opening the
same code twice. The cost of A is a few analyst-hours; the cost of getting it wrong is a re-opened phase.

---

### D9. Credit and Debit Note have no menu row anywhere.

Verified by exhaustive grep — the strings do not occur in `MainWindowViewModel.cs` at all `[AUDIT §1, §3.1]`.
They are reachable only via Alt+F6 / Alt+F5 and the Day-Book Alt+A picker.

**Adding the menu rows is decided, not asked** (§10 — there is no trade-off, only an omission). The open question
is where they go, because it interacts with the Miller-column navigation contract.

| | Option | Trade-off |
|---|---|---|
| **A** | **Under the same Vouchers column as Sales and Purchase**, in TallyPrime's ordering `[CORPUS-BOOK p.24 lists Credit Note at #11 and Debit Note at #12 of the 24]`. | Matches the corpus ordering and puts returns next to the sales they reverse. Lengthens an already-long column. |
| **B** | **Under Other Vouchers**, with Memorandum and Reversing Journal. | Keeps the main column short. But CN/DN are ordinary accounting vouchers used weekly, not exotic ones — burying them repeats the discoverability failure in a quieter way. |

**Recommendation: A.** The corpus groups them with the accounting vouchers, and frequency of use says the same.

---

## 3. Masters

---

### D10. No Voucher Type master. Build one?

There is **no** `VoucherTypeMasterViewModel` among the 118 files in `src/Apex.Desktop/ViewModels/`; the only
voucher-type editor is `VoucherNumberingConfigViewModel` (numbering affixes only), and the only two non-seeded
types in the system are **auto-created by the app** `[AUDIT §4.1; CODE: directory listing, verified this session]`.
TallyPrime has full Create / Alter / Display / Delete `[CORPUS-BOOK pp.17-18; CORPUS-SG p.229]`.

**Effort anchor, measured this session.** The project has **25 master ViewModels** already, ranging from
`ScenarioMasterViewModel.cs` at 184 lines to `LedgerMasterViewModel.cs` at 1,043 lines `[CODE: wc -l]`. The
master pattern, the create-dispatch table (`MasterCreateKind` / `MasterCreateFields`) and the export interface
(`IMasterListExportSource`) are all established `[CODE]`. A Voucher Type master is a **well-trodden path in this
codebase**, not novel architecture — most likely a mid-sized master VM plus its view, plus persistence for the
user-created rows.

**This decision unblocks others.** D6 (voucher classes) and D11 (show-inactive) both require it. So does any
future "Cash Sales vs Credit Sales" workflow, which the audit notes "every real Tally deployment does in week
one" `[AUDIT §5 Tier 2 item 5]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **Full master: Create / Alter / Display / Delete**, including abbreviation, parent type, active flag, print messages and declaration. | The real feature, and it unblocks D6 and D11. Needs a schema change to persist user-created types and a careful delete rule (a type with posted vouchers must not be deletable). Sizeable but conventional — the pattern is proven 25 times over. |
| **B** | **Alter-only.** User can rename, set abbreviation and toggle active on the 24 seeded types plus the two auto-created ones. No creating new types. | Materially cheaper — no new persistence for user types, no delete-safety rule. Covers renaming and activation. Does **not** cover "Cash Sales vs Credit Sales", which is the single most common thing users do here. |
| **C** | **Neither.** | Free. The user cannot create, rename, deactivate or delete a voucher type, ever, and D6/D11 stay blocked behind it. |

**Recommendation: A.** The unblocking effect is what tips it — B leaves two other decisions stranded and still
does not deliver the headline use case. Given 25 precedents in the codebase, this is one of the more predictable
large items in this document.

---

### D11. "Show Inactive" activation flow

TallyPrime activates a dormant type in-flow: F10 > Show Inactive > select > Enter > Yes `[CORPUS-SG p.74]`.
Apex's F10 shows a fixed menu column with no inactive list `[AUDIT §4.1]`. Combined with the Payroll `IsActive`
bug (§10, decided), an inactive type can currently be activated **only by changing code**.

| | Option | Trade-off |
|---|---|---|
| **A** | **Build it as part of D10's master** (an Active toggle in the master, plus a "show inactive" filter in the type list). | Natural home, near-zero marginal cost once D10 exists. Not the exact TallyPrime keystroke flow. |
| **B** | **Build TallyPrime's exact in-flow version** (F10 > Show Inactive > Enter > Yes). | Faithful. Requires the F10 menu to become dynamic, which collides with D7 option B and with the Other Vouchers menu. Extra work for a keystroke. |
| **C** | **Skip.** | Free if the Payroll `IsActive` bug is fixed (it is, §10) and D10 ships with an Active toggle. Then nothing is stranded inactive in practice. |

**Recommendation: A.** Delivers the capability inside work already being done. B buys fidelity on a rarely-used
path at a cost that lands on the F10 key, which is already contested (D7).

---

## 4. Data safety — the excluded Phase 10

**Phase 10 (TallyVault, Security Control + roles, Edit Log / Tally Audit, backup/restore, split-by-FY, group
company) is EXCLUDED by standing user decision, and so is Phase 11 (hardening, packaging, v1.0 release)**
`[CODE: plan.md:14, verified this session; contents at plan.md:420-428 and :935-943]`.

The audit's Tier 1 finding on this is blunt, and I verified the plan lines: `plan.md` names backup/restore as the
mitigation for its **own top-ranked data-loss risk R-7** `[CODE: plan.md:1041]`, and puts it in the excluded
phase. **The stated mitigation for the stated top risk is not built.**

---

### D12. Backup / restore

Import/Export data screens exist, which is **not** the same thing `[AUDIT §4.8]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **Carve backup/restore out of Phase 10 and build it now**, as a small standalone slice, leaving the rest of Phase 10 excluded. | The single highest safety-per-hour item in this document. The persistence layer is SQLite with versioned migrations to v49 `[AUDIT §4.8]`, so a consistent file-level backup with a version stamp is tractable — this is not the same scale as TallyVault or roles. Requires a restore round-trip test to be worth anything. |
| **B** | **Un-exclude Phase 10 entirely** (D13 folds into this). | Everything gets fixed, including the 7.2 regressions. Months of work, and it re-opens a phase the user closed deliberately. |
| **C** | **Leave excluded.** Tell the user to copy the data file manually and document it as the official procedure. | Free, and honest if it is actually written down and the file location is documented. Fragile: manual copies are not taken, and a mid-write copy of a SQLite file is not necessarily restorable. |

**Recommendation: A.** Anyone running a business on this is currently one file corruption away from total loss,
and the project's own risk register says so. This is the one item I would carve out of an excluded phase without
hesitation. If the answer is C, **the manual procedure must actually be written into user docs** — otherwise C is
just B-with-extra-steps-nobody-takes.

---

### D13. TallyVault, Security Control / roles, and Edit Log / Tally Audit

All three absent `[AUDIT §4.8]`. `plan.md` C-7 records Edit Log and Tally Audit as **two separate deliverables**
`[CODE: plan.md:1093]`.

**The part that changes the framing:** Tally 7.2 has **both TallyVault and Tally Audit** `[AUDIT §5 Tier 1 item 3,
SECONDARY sourcing]`. So relative to what the user runs **today**, this is a **regression**, not a missing modern
feature. TallyPrime's Edit Log specifically is a 2.1 feature `[OFFICIAL: help.tallysolutions.com/tallyprime-features-release-wise/]`,
but Tally Audit predates Tally 9.

| | Option | Trade-off |
|---|---|---|
| **A** | **Build the audit trail only** (who changed what, when) and defer encryption and roles. | Addresses the accountability half — the half a CA cares about, and the half `plan.md` WI-3 already deferred an alteration hook into `[CODE: plan.md:434]`. Cheaper than roles. Leaves the data unencrypted and unrestricted. |
| **B** | **Build TallyVault (encryption) only** and defer audit and roles. | Addresses confidentiality. Doesn't tell anyone who changed what. Encryption-at-rest with key handling is its own risk surface — get it wrong and data is lost, not merely exposed. |
| **C** | **All three** — un-exclude the security half of Phase 10. | Complete, and closes the regression against 7.2. Months, and touches every persistence path. |
| **D** | **Stay excluded**, accept single-user, unencrypted, unaudited operation, and say so in the user documentation. | Free and defensible **if and only if** the deployment really is one trusted person on one machine — which it may well be. The documentation clause is not optional. |

**Recommendation: D for now, with A as the first thing to un-exclude** if a second user, an accountant or an
external reviewer ever touches the file. The reason D over A today: the user excluded Phase 10 deliberately, and
a single-operator deployment genuinely does not need roles. The reason A next: an audit trail is what makes books
defensible to a CA, and this project has already had a CA audit round (`docs/ca-audit-backlog.md`). **D12 is not
covered by this answer** — back-up is a different risk from security and should be answered separately.

---

## 5. Inventory

---

### D14. Negative stock — the item that has already failed three times

`plan.md` records Phase 10.8 as **STOPPED, engine reverted to HEAD**, by user decision on 2026-07-29
`[CODE: plan.md:1126]`. Three attempts produced **three different unbounded Balance-Sheet errors, each of which
passed the full test suite** `[AUDIT §4.5]`. A `NegativeStock` **report** exists; the allow-negative
**valuation** does not.

| | Option | Trade-off |
|---|---|---|
| **A** | **Stay stopped.** Keep the report; do not attempt valuation. | Zero risk of a fourth unbounded error. Any company that goes stock-negative has untrustworthy valuation, and the report tells them so without fixing it. |
| **B** | **Retry, oracle-harness first.** Build an independent reference valuation, prove it disagrees with the engine on the known failure cases, *then* change the engine. | The only method that addresses why the first three failed — the suite could not see the error. Note the honest caveat: a harness was already attempted, and `memory.md` records it as scoped back to "what its reference can prove" (commit `0e20cc2`). The oracle problem is itself unsolved, so this is **not** a guaranteed path. |
| **C** | **Hard block instead:** refuse to post a voucher that would drive stock negative, with a clear message. | Sidesteps valuation entirely — if stock can never go negative, there is nothing to value. Diverges from Tally, which allows it with a warning, and it will block legitimate entry-order cases (invoice keyed before the GRN) that are extremely common in practice. |

**Recommendation: A for now, C as a considered alternative, B only with a fresh gate.** The user already stopped
this once, on good evidence. Reopening it needs a *new* idea, not a fourth attempt at the same one. If negative
stock is a real operational need, **C deserves a serious look** — it is the one option that removes the hazard
instead of modelling it, and its cost is a validation rule rather than a valuation rewrite. Its weakness
(blocking invoice-before-GRN) may be acceptable if the user's workflow doesn't do that; **that is a question only
the user can answer.**

---

### D15. "Ignore physical stock difference" is not configurable

The corpus documents it as a **toggle** `[CORPUS-BOOK p.82]`. Apex hardcodes the non-ignoring arm — subsequent
transactions use the counted balance `[AUDIT §3.3 #16; CODE: InventoryLedger.cs:164, :269, :273]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **Add the toggle** on the Physical Stock voucher type. | Restores the documented choice. Touches inventory replay ordering — the same neighbourhood as D3 and D14, so it should ride along with one of them rather than open that code on its own. |
| **B** | **Leave hardcoded**, and document that Apex always honours the physical count. | Free. The hardcoded arm is the sensible default — a physical count that doesn't override the book is of limited use. Users who want the other behaviour cannot get it. |

**Recommendation: B for now, A folded into D3 if D3 goes ahead.** Not worth opening the inventory replay path on
its own; very cheap to add while that path is already open.

---

### D16. Stock Journal loses Alt+F7 when BOM is enabled

When `Company.SetComponentsBom` is true, Alt+F7 opens **Manufacturing Journal** instead, leaving Stock Journal
menu-only `[AUDIT §3.3 #15; CODE: MainWindow.axaml.cs:662-664]`. TallyPrime keeps Alt+F7 on Stock Journal and
selects Manufacturing Journal from the voucher-type list.

| | Option | Trade-off |
|---|---|---|
| **A** | **Restore Alt+F7 to Stock Journal always**; reach Manufacturing Journal from the type list, as TallyPrime does. | Correct fidelity. Removes a keyboard shortcut that BOM users may now rely on — a small but real behaviour change to announce. |
| **B** | **Keep the current behaviour**, and record it as an accepted divergence. | Free. BOM users keep a fast key. Non-obvious and undocumented behaviour: the same key does different things depending on a company setting. |

**Recommendation: A.** Fidelity is cheap here and the current behaviour is the kind of state-dependent keybinding
that generates support questions forever. But it does change something that works today, so it should be
announced rather than slipped in.

---

## 6. Keyboard

---

### D17. Prefix type-to-filter in dropdowns (KB-3)

The **settled, user-confirmed** keyboard contract requires dropdowns to filter by typed prefix with the typed
text visible. What ships is type-to-**JUMP**. `plan.md` records this as **KB-3, new work, with three design
rounds NOT-READY** `[AUDIT §4.6; CODE: plan.md:528-533]`.

The audit calls this "the single biggest drag on data-entry speed" `[AUDIT §5 Tier 2 item 7]` — in a ledger list
of several hundred names, jump is not a substitute for filter.

| | Option | Trade-off |
|---|---|---|
| **A** | **Run the real-windowed spike first** (project memory records that this is what the three failed design rounds were missing), then design, then build. | The method that matches the diagnosis. Costs a spike before any user-visible progress. Highest chance of actually landing. |
| **B** | **Fourth design round without a spike.** | Cheapest to start. Three rounds already failed this way. I would not. |
| **C** | **Drop the requirement.** Keep type-to-jump and update the settled contract accordingly. | Free, and honest — better than an unbuilt promise sitting in the plan. Costs the daily speed permanently, on a contract the user personally confirmed clause by clause. |

**Recommendation: A.** Three failed rounds is evidence about *method*, not about difficulty. If the spike also
fails, **C is the right answer** and the contract should be formally amended rather than left open — an
indefinitely-unbuilt confirmed requirement is worse than a withdrawn one.

---

### D18. 15 `SelectedIndex`-bound ListBoxes deferred from the keyboard-parity slice

`[AUDIT §4.6; CODE: plan.md:550]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **Finish all 15** as one mechanical slice. | The method is proven from the earlier slice; this is repetition, not design. Bounded and predictable. Real but unglamorous effort across 15 files. |
| **B** | **Do only the ones on voucher-entry screens**; leave report and master screens. | Targets the highest-traffic surface for a fraction of the work. Leaves inconsistent keyboard behaviour across the app, which is itself a usability defect. |
| **C** | **Leave all 15.** | Free. Keyboard parity stays partial, which undermines the whole keyboard-first premise. |

**Recommendation: A.** Mechanical, proven-method work with a known end. Good candidate to run in parallel with a
design-heavy item like D17 or D10.

---

### D19. Ctrl+H's report "Change View" arm

In TallyPrime, Ctrl+H changes **entry mode** in a voucher and **view** in a report `[OFFICIAL]`. Apex consumes
Ctrl+H only on Purchase/Sales voucher screens `[AUDIT §2.4 item 3, §4.2]`, so the report arm does not exist.

| | Option | Trade-off |
|---|---|---|
| **A** | **Implement the report arm.** | Fidelity, plus it exposes report views users may not know exist. Needs a per-report definition of what "views" are available — that is the real cost, and it is unestimated because it depends on how many of the 45 report kinds have meaningful alternate views. |
| **B** | **Leave it.** | Free. Rated LOW in the audit. Reports remain reachable and readable; only the view-switching affordance is absent. |

**Recommendation: B.** Genuinely low value next to everything else on this list, and its cost is unknown rather
than small. Revisit only after the Tier 1 and Tier 2 items are closed.

---

## 7. Reports

---

### D20. Per-voucher-type registers (Sales / Purchase / Credit Note / Debit Note / Journal Register)

Only `PosRegister`, `MemorandumRegister`, `ReversingJournalRegister` and the inventory/payroll registers exist —
I confirmed the directory listing myself `[CODE: ls src/Apex.Ledger/Reports/]`. The audit left open whether Day
Book covers this.

**A corpus finding that firms this up, which I verified this session.** The Book routinely navigates to
`GOT > Display More Reports > Account Books > Credit Note Register` and the identical Debit Note Register path
as the standard way to review and edit those vouchers `[CORPUS-BOOK, Credit Note and Debit Note chapters]`. This
is not an obscure report — it is the corpus's prescribed review path for the voucher types in D3. So the gap is
**corpus-confirmed**, not merely inferred.

| | Option | Trade-off |
|---|---|---|
| **A** | **Add a generic voucher-type register** — one report parameterised by voucher type, covering Sales, Purchase, Journal, Credit Note, Debit Note and any user-created type from D10. | One implementation covers the whole family, including future user-created types. The existing single-purpose registers suggest the pattern is well understood. Moderate. |
| **B** | **Add only Sales and Purchase Registers.** | Covers the two most-requested. Leaves CN/DN without the corpus's prescribed review path — awkward if D3 goes ahead, since you would build the notes and not the way to review them. |
| **C** | **Rely on Day Book with a type filter.** | Cheapest if Day Book already filters by type. Not the corpus's navigation, and register-specific columns and totals differ from a day book. |

**Recommendation: A**, sequenced **with or after D3**. A parameterised register is strictly better than N
hand-written ones and automatically serves whatever types D10 lets the user create. If cost forces a smaller
answer, C is acceptable as an interim — but check first whether Day Book actually filters by type; the audit did
not assess it and neither did I.

---

### D21. Group Summary, Stock Query (Alt+S), Movement Analysis

All three return **zero grep hits** `[AUDIT §4.2]`. Note that Apex already binds Alt+S to a Reorder Levels
toggle, so Stock Query would need a different key or a rebind.

| | Option | Trade-off |
|---|---|---|
| **A** | **Build all three.** | Closes the known report gaps. Three separate reports; unestimated individually but none looks architecturally hard given 78 existing report modules. Stock Query forces an Alt+S decision. |
| **B** | **Build Stock Query only** — the one with a dedicated Tally accelerator and the most day-to-day use. | Targeted. **Verify the key first**: the audit's Alt+S = Stock Query mapping comes from the shortcut PDF that is documented as **not machine-trustworthy** `[AUDIT §6.1]`. Confirm against the official page before binding anything. |
| **C** | **None.** | Free. Three standard Tally reports stay absent. |

**Recommendation: B**, with the key verified against `[OFFICIAL]` first, and A revisited later. Group Summary and
Movement Analysis are real but not blocking; Stock Query is the one people reach for mid-entry.

---

## 8. Configuration & numbering

---

### D22. FY restart of voucher numbering

Deferred by user decision because it **collides with e-invoice statutory numbering** `[AUDIT §4.7; CODE: plan.md:595]`.
Most Indian businesses restart invoice numbers on 1 April.

| | Option | Trade-off |
|---|---|---|
| **A** | **Stay deferred.** | Free, and the original reason is a good one — an e-invoice IRN is tied to a document number that must be unique in ways an FY reset can break. |
| **B** | **Implement FY restart with a company-level guard** that disables it whenever e-invoicing is enabled. | Gives the feature to the (many) users who don't e-invoice, without re-creating the collision. Adds a mode interaction that must be tested both ways, and a migration question for a company that later turns e-invoicing on with restarted numbers already posted. That last case is the hard part. |
| **C** | **Implement unconditionally.** | Do not. It re-creates the exact collision the deferral was for. |

**Recommendation: B, but only after the "turns e-invoicing on later" case has a written answer.** That case is
the whole risk, and it is a design question, not a coding one. If nobody wants to write that answer now, **A** is
the correct holding position — it is a real deferral for a real reason, not neglect.

---

### D23. Additional numbering methods

Apex has `Automatic`, `Manual`, `None` `[CODE: src/Apex.Ledger/Domain/NumberingMethod.cs]`. The audit believes
TallyPrime also offers **Automatic (Manual Override)** and multi-user auto, but flags this as **`[UNCITED]` — not
found in the corpus this session, no official page fetched** `[AUDIT §4.7]`.

| | Option | Trade-off |
|---|---|---|
| **A** | **Verify first** (one A14 corpus/web check), then decide. | Correct order. Per R7 this project has been bitten repeatedly by claims-that-turned-out-not-to-be-facts — this is exactly such a claim, and it is self-flagged. Costs one verification task. |
| **B** | **Build Automatic (Manual Override) now.** | It is a plausible and useful mode. But building to an unverified spec is the failure pattern R7 exists to prevent. |
| **C** | **Leave at three methods.** | Free. If the mode does exist in TallyPrime, we have a known unclosed gap. |

**Recommendation: A.** This is not really a scope decision yet — it is an unverified premise. Verify, then it
becomes a one-line decision.

---

## 9. Payroll

---

### D24. The Attendance voucher type is dead seed data

`VoucherBaseType.Attendance` occurs in the **entire repository** only at its enum member and its seed row —
verified by exhaustive grep of `src/` and `tests/` `[AUDIT §1, §3.6 #23]`. The Attendance *screen* exists and
works, but writes `AttendanceEntry` rows, never a `Voucher`.

The related **`IsActive` bug** — nothing ever flips Payroll/Attendance active, so they are excluded from the
Day-Book picker and from Scenarios — is a defect, and fixing it is **decided, not asked** (§10).

| | Option | Trade-off |
|---|---|---|
| **A** | **Make Attendance a real voucher type**: the screen posts an Attendance voucher, and the existing `AttendanceEntry` becomes its detail. | True to Tally, and it puts attendance into the Day Book and Scenarios like every other voucher. Real work: a posting path, a migration for existing `AttendanceEntry` rows, and a non-financial voucher that affects neither stock nor accounts (the pattern exists — order vouchers already do this `[AUDIT §3.4]`). |
| **B** | **Remove the dead seed row and the enum member.** Attendance stays a non-voucher screen, and Apex honestly seeds **23** predefined types, not 24. | Honest and nearly free. The user loses attendance from the Day Book and Scenarios, and the "24 of 24" claim — which the audit calls corrosive precisely because it hides this `[AUDIT §5 Tier 3 item 14]` — has to be restated as 23. |
| **C** | **Leave the dead row.** | Free. A master row that nothing reads, and a completeness claim that is not true. |

**Recommendation: B, unless attendance-in-the-Day-Book is something the user actually wants.** A is correct
fidelity but buys little day-to-day: attendance is already recorded, reportable (Attendance Register exists) and
feeds payroll. C is the only clearly wrong answer — it keeps a false completeness claim alive. **This is a
question about honesty of the status line as much as about function.**

---

## 10. Decided without asking — not on the ballot

Per the brief, these have an obvious right answer or are settled by the corpus. Listed so the user can object,
not so they must choose. Each is a work item awaiting scheduling, not an open question.

| # | Decision | Why it needed no question |
|---|---|---|
| **X1** | **Bind Ctrl+F7 to Physical Stock and correct the seed's `"F10"` string and the menu row.** | The UI currently advertises a key that does something else. `[OFFICIAL]` settles the correct key. There is no trade-off — only a defect. (What happens to F10 *is* a question: **D7**.) |
| **X2** | **Fix the Payroll / Attendance `IsActive` bug** so `PayrollService.EnablePayroll` activates the types. | A payroll voucher cannot be added from the Day-Book picker or included in a Scenario `[CODE: ScenarioMasterViewModel.cs:92 verified]`. That is a bug with no upside. |
| **X3** | **Add menu rows for Credit Note and Debit Note.** | Two of the 24 predefined types are invisible in every menu. No trade-off exists in *whether*; **D9** asks only *where*. |
| **X4** | **Give TCS Stat Payment an accelerator**, ideally by making Ctrl+F on a Payment voucher a dispatcher offering TDS or TCS — which is what TallyPrime's single Stat Payment button does `[AUDIT §3.7]`. | The current empty shortcut was a deliberate collision-avoidance, but a dispatcher removes the collision instead of surrendering the key. Low risk, clearly better. |
| **X5** | **Never cite `tally/659947760-Tally-Prime-Short-Key.pdf` for key mappings, and re-check any prior project work that did.** | Its two-column layout extracts with systematic label/key misalignment — it yields "F6 = Contra", "F8 = Stock Journal", both wrong against `[OFFICIAL]` and against `[CORPUS-BOOK p.25]` `[AUDIT §6.1]`. This is a documented data-integrity finding, not a preference. |
| **X6** | **Record F10 = Other Vouchers and the Miller-column navigation as accepted, documented divergences** from TallyPrime. | Both are deliberate existing design decisions. Undocumented, they will be re-raised as defects at every acceptance round. Writing them down costs nothing. |
| **X7** | **Do not re-run or re-quote "3491 tests green" as a current figure** without running the suite. | It is the last figure recorded in `memory.md`, not a verified-today figure `[AUDIT §6.9]`. Status facts go stale fastest. |

---

## 11. Things that need verification, not a decision

These are listed so they are not mistaken for either answered questions or open scope.

1. **The 4% cess for TY2026-27.** Carried in project memory as an open user decision — an unverified cess on the
   default payroll path with no retrievable statutory basis. Neither the audit nor I opened the source file this
   session `[AUDIT §6.10, explicitly `[UNCITED]`]`. It is a **live money-affecting payroll deduction** and it
   outranks most of this document on urgency. It needs an A14 statutory verification, then a decision.
2. **Whether Day Book filters by voucher type** — decides whether D20 option C is even available. Unassessed.
3. **The `Automatic (Manual Override)` numbering mode** — D23, unverified premise.
4. **`Alt+S` = Stock Query** — sourced from the untrustworthy shortcut PDF (X5). Verify before binding.
5. **The Tally 7.2 predefined voucher list** — reconstructed, not sourced; no citable official list was found
   `[AUDIT §6.3]`. Matters only if D1 = B or C.
6. **Job Costing's release attribution** and **Job Work's ERP-9 attribution** — both weak `[AUDIT §6.4, §6.6]`.
   Do not build scope arguments on either.

---

## 12. Suggested sequence, if the recommendations are taken

Not a decision — a proposal, offered because several items unblock others.

1. **D1** (yardstick) — everything else is read against it.
2. **X1–X7** — the decided defects. Small, independent, immediately visible in the running app.
3. **D12** (backup/restore) — highest safety-per-hour, and independent of every other item.
4. **D4** (Purchase accounting invoice) — best value-to-effort; the code is already written and dormant.
5. **D8** (parity sweep) — read-only, and it must land **before** D3 is scoped, or the same code gets opened twice.
6. **D5** (Single Entry) and **D18** (the 15 ListBoxes) — large usability wins with no posting risk; can run in
   parallel with design-heavy work.
7. **D10** (Voucher Type master) — unblocks D6 and D11.
8. **D3** (CN/DN stock) — the big one. Oracle-first. Its own phase, its own gate.
9. **D17** (type-to-filter) — spike first; if the spike fails, amend the contract rather than leave it open.
10. Everything else, in the order the user's answers imply.

---

## Sources

**Official:**
- [TallyPrime keyboard shortcuts — TallyHelp](https://help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/)
- [TallyPrime features, release-wise — TallyHelp](https://help.tallysolutions.com/tallyprime-features-release-wise/)
- [Tally.ERP 9 Release 6.0.2 release notes — TallyHelp](https://help.tallysolutions.com/docs/te9rel60/release_notes_6_0/release_6_0_2.htm)

**Licensed corpus** (git-ignored, `…\Apex Solutions(end)\tally\`, never quoted at length — page/section cited only):
- `664311548-Tally-Prime-Book.pdf` — author pp. 17-18, 24-31, 39, 51-95, 121 (Advance Accounting, interest classes)
- `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` — pp. 74-75, 213-214, 229
- `659947760-Tally-Prime-Short-Key.pdf` — **not machine-trustworthy, see X5**

**In-repo, verified this session:** `src/Apex.Ledger/Services/ItemInvoiceStock.cs`,
`src/Apex.Ledger/Services/VoucherValidator.cs`, `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:55-95`,
`src/Apex.Desktop/ViewModels/MasterCreateKind.cs`, `src/Apex.Ledger/Reports/` listing,
`src/Apex.Desktop/ViewModels/` listing, `plan.md:14, 420-428, 434, 550, 595, 935-943, 1041, 1093, 1126`.

**Prior analysis:** `docs/tally-version-and-voucher-gap-audit.md` (A1, 2026-08-01).
