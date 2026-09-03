# Diverged rules (d) and (e) — Place of Supply: Statutory & Corpus Grounding

**What this is:** the R7 fidelity grounding for the two rows of the "diverged rule copies" register that were
**deliberately left untouched** by W0-11 — row **(d)** `IsInterState` (two answers) and row **(e)** place of
supply (three derivations). They were not unified because deciding them is a **statutory** question — what an
unrouteable supply *is* — and nobody had answered it. `plan.md` carries this as W0-11 carry-forward **(b)**.

**What this is NOT — read this before using anything below:** a design. Nothing here says which method should
survive, what a unified signature should look like, where a refusal should fire, or what any screen should
capture. It records **what the statute says**, **what the corpus says**, and **what our code does today**, so
that a design can be argued from evidence instead of invented. **§11 is a USER RULING, not a recommendation
to implement.**

**Pass:** A14 Tally Domain/Corpus Expert. **Read-only** — nothing was built, run, edited or committed to produce
this document. The test suite was not executed (another workflow owns the build output).

**Baseline for every code claim:** worktree `…\.claude\worktrees\recursing-swirles-3138c6`, branch
`claude/apex-wrong-figures-bc45f4`, HEAD **`c56e5c3`**. Every file:line below was opened this session.

**Date:** 2026-08-15.

> 🔴 **§11 contains a question that NO source can settle and that requires a USER DECISION.** It is not a style
> preference — it decides whether a book with an undeclared supplier location refuses loudly, refuses quietly,
> or keeps posting figures derived from a fact it does not have.

---

## 0. Markers

**[V]** = verified first-hand this session by opening the file, extracting the PDF page, or retrieving and
parsing the source document. **UNVERIFIED** = the sources do not settle it; **§10 is the list, and it is the
most valuable part of this document**, because it is what stops a future session filling the gap by invention.

---

## 1. Sources, and exactly how each was retrieved

**No statutory proposition in this document rests on a secondary source.** Web *search* was used only to
locate first-party URLs; every legal text quoted below was retrieved from a `.gov.in` host, saved, and
extracted locally. Where a retrieval failed, the failure is recorded rather than papered over.

| Source | Host | Method | Result |
|---|---|---|---|
| IGST Act, 2017 (Gazette, Act 13 of 2017) — `…/2024-03/annexure-5-igst-act_2017.pdf` | `gstcouncil.gov.in` | HTTPS GET, 1.1 MB PDF saved, `pdftotext -layout` locally | **200** **[V]** |
| CGST Act, 2017 (as on 30.09.2020) — `/pdf/CGST-Act-Updated-30092020.pdf` | `cbic-gst.gov.in` | same | **200** **[V]** |
| CGST Rules, 2017, Part A (consolidated **as at 01.06.2021**) — `…/2024-04/01062021-cgst-rules-2017-part-a-rules.pdf` | `gstcouncil.gov.in` | same | **200** **[V]** |
| **Circular No. 209/3/2024-GST** dated 26.06.2024 — `…/2024-09/circular-no-209-03-2024.pdf` | `gstcouncil.gov.in` | same | **200** **[V]** |
| **Circular No. 184/16/2022-GST** dated 27.12.2022 — `/pdf/circular-184.pdf` | `cbic-gst.gov.in` | same | **200** **[V]** |
| NIC **State master codes** — `/Others/MasterCodes` | `einvoice1.gst.gov.in` | HTTPS GET, HTML | **200** **[V]** |
| NIC **e-invoice schema workbook** — `/Documents/EInvoice_Schema.xlsx` | `einvoice1.gst.gov.in` | HTTPS GET, 193.7 KB saved, unzipped, `xl/sharedStrings.xml` parsed directly | **200** **[V]** |
| The 10 git-ignored TallyPrime PDFs at `<repo>\tally` | — | `pdftotext -layout`, all 10 converted, grepped | **[V]** |

### 1.1 Retrievals that FAILED, and how

- **`taxinformation.cbic.gov.in`** — `unable to verify the first certificate`. **Confirms the prior pass's
  finding**; the TLS chain still does not validate from this environment. CBIC-GST's own "GST Acts" nav link
  points off-portal to this host, which is why the Acts are unreachable by the obvious route.
- **`egazette.gov.in`** — `unable to verify the first certificate`. **New finding**, same failure mode. This
  is why the Finance-Act/amendment instrument could not be read at source (see §10, item 4).
- **`indiacode.nic.in`** — **HTTP 403** to an automated fetcher.
- **`cbic-gst.gov.in/cgst-rules.html`**, **`/cgst-igst-utgst-cess-act.html`** — **HTTP 404**. These paths appear
  in search results but do not exist; do not cite them.
- **`cbic-gst.gov.in/gst-invoice-rules.html`** — **200, but it serves the ORIGINAL DRAFT invoice rules
  (numbered rule 1–8), not the notified rules 46–56.** Its "rule 1(e)" is the text now at rule 46(e). **Do not
  cite this page for a rule number** — it will read as rule 1 and be wrong.

### 1.2 Corpus weighting for this question

Carried forward from `docs/w0-2-company-screen-grounding.md` §1: `664311548-Tally-Prime-Book.pdf` and
`696054070-TALLY-PRIME-STUDY-GUIDE.pdf` are **PRIMARY**; `703679456` is **MIXED VINTAGE** (ERP-9-era screen
paths unless corroborated); **`659947760-Tally-Prime-Short-Key.pdf` is REJECTED and is not cited here.**
Book page references below are the Book's own printed page numbers as they appear in the extraction
(printed page = PDF page − 4, per the offset established in W0-2).

---

## 2. Row (d) — `IsInterState` answers two ways. Verified.

**The shape is representable because the home State code is nullable.** **[V]**
`src/Apex.Ledger/Domain/GstConfig.cs:33` —

```csharp
public string? HomeStateCode { get; set; }
```

with the doc comment "required when enabled". `GstConfig.EnsureValid()` (`:200-205`) does enforce
`IndianState.IsValidCode(HomeStateCode)` — **but only when `Enabled`**, and it is a method that must be
*called*, not an invariant of the property.

### 2.1 The two forms, verbatim, at HEAD `c56e5c3`

**Form A — THROW.** **[V]** `src/Apex.Ledger/Services/GstService.cs:332-339`:

```csharp
public bool IsInterState(string? partyStateCode)
{
    var home = _company.Gst?.HomeStateCode;
    if (home is null) throw new InvalidOperationException("GST is not enabled (no home state) — cannot route a supply.");
    // No recorded place of supply ⇒ default to the company home State ⇒ intra-state (B2C local sale, DP-8).
    if (string.IsNullOrWhiteSpace(partyStateCode)) return false;
    return !string.Equals(home, partyStateCode, StringComparison.Ordinal);
}
```

**Form B — RETURN FALSE.** **[V]** `src/Apex.Ledger/Services/EWayBillService.cs:145-150`:

```csharp
private bool IsInterState(Voucher voucher)
{
    var home = _company.Gst?.HomeStateCode;
    var pos = GstReportSupport.PlaceOfSupply(_company, voucher);
    return pos is not null && home is not null && !string.Equals(pos, home, StringComparison.Ordinal);
}
```

`home is not null` short-circuits to **`false`** — and `false` in this codebase is not "unknown", it is the
positive assertion **intra-state (CGST+SGST)**. It is consumed as such immediately: `EWayBillService.cs:69`
feeds it to `CoverageOf`, where `!interState` selects the intra-state exemption branch (`:75-77`) and
`EffectiveThreshold` (`:132-143`) then looks up a **per-State intra-state threshold override** keyed on the
place of supply. A book that cannot say where its supplier is therefore silently acquires a State-specific
e-Way threshold.

### 2.2 Callers of each form in `src/` — counted and named **[V]**

**Form A (`GstService.IsInterState`, public, throwing) — 6 call sites across 4 files:**

1. `src/Apex.Desktop/Services/VoucherPrintProjector.cs:275`
2. `src/Apex.Desktop/ViewModels/PosBillingViewModel.cs:387`
3. `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:3638`
4. `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:3825`
5. `src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:4250`
6. `src/Apex.Ledger/Services/RcmService.cs:93`

**Form B (`EWayBillService.IsInterState`, private) — 1 call site:** `src/Apex.Ledger/Services/EWayBillService.cs:69`.

*(The other `IsInterState` hits in `src/` are not calls to either form: `InvoicePrintData.cs:140` and
`PosReceiptData.cs:103` declare a DTO property; `VoucherPrintProjector.cs:407,:519` and
`PosBillingViewModel.cs:817` assign it; `PrintPreviewViewModel.cs:373,:459`, `InvoicePdf.cs:238,324,391,428,445`
and `PosReceiptPdf.cs:130,154,167` read it. `GstService.cs:16` is a `<see cref>`.)*

### 2.3 Reachability — one leg PROVEN, one leg not

**The throw is reachable, and the repo already says so.** **[V]** `VoucherPrintProjector.cs:61-66` carries a
standing defect note **F7**: *"printing after GST is switched off throws … `GstService.IsInterState`, which
raises 'GST is not enabled (no home state) — cannot route a supply'"*. I verified the gate independently:
`VoucherDetailViewModel.BuildPrintPreview` (`:66-68`) routes on `GstReportSupport.IsTaxInvoice`, and
`IsTaxInvoice` (`GstReportSupport.cs:976-991`) has **no `GstEnabled` gate at all** — it tests base type,
the §10 composition contradiction, and posted-tax tagging. `ProjectInvoice` then calls
`gst.IsInterState(partyState)` **unconditionally** at `:272`. On a company where `Gst` is `null` — e.g. one
created through `CsvCanonicalBridge` (`:194` sets `Gst = null`) — `home` is null and an already-issued Sales
invoice **cannot be printed at all**.

**The `return false` leg is guarded, and I could not close the loop.** `EWayBillService.CoverageOf` opens with
`if (gst is not { Enabled: true } || !gst.EWayBillEnabled) return EWayCoverage.NotApplicable;` (`:59-61`), so
Form B is only reached with `Enabled == true` — and `EnsureValid` demands a valid home code in exactly that
state. Whether `Enabled: true` **with** a null `HomeStateCode` is reachable through a UI path I did **not**
exhaustively trace. See §10, item 5. What is certain is that **the two methods answer differently on a shape
the type system permits**, which is the register's criterion.

---

## 3. Row (e) — place of supply derived three ways. Verified.

**(A) The report/engine rule.** **[V]** `src/Apex.Ledger/Reports/GstReportSupport.cs:74-79`:

```csharp
public static string? PlaceOfSupply(Company company, Voucher voucher)
{
    if (voucher.PartyId is Guid pid && company.FindLedger(pid)?.PartyGst?.StateCode is { } code)
        return code;
    return company.Gst?.HomeStateCode;
}
```

Party State, else company home. **6 call sites in 2 files:** `Gstr1.cs:259`, `Gstr1.cs:409`,
`EWayBillService.cs:137`, `:148`, `:197`, `:457`.

**(B) The print rule.** **[V]** `src/Apex.Desktop/Services/VoucherPrintProjector.cs:766-771`:

```csharp
private static string PlaceOfSupply(Company company, string? buyerStateCode, bool postedInterState)
{
    var code = buyerStateCode;
    if (string.IsNullOrWhiteSpace(code) && !postedInterState) code = company.Gst?.HomeStateCode;
    return StateText(code);
}
```

The home-State fallback fires **only on an intra-state supply**; on an inter-state supply the field is
deliberately left **blank** (`StateText(null)` returns `string.Empty`, `:762-766`). 2 call sites, both in the
same file: `:399` (item pass) and `:518` (service pass).

**(C) The e-invoice rule.** **[V]** `src/Apex.Ledger.Io/EInvoiceJson.cs:267`:

```csharp
var stateCode = isExport || isSez ? OverseasPlaceOfSupply : partyGst?.StateCode ?? homeStateCode;
```

`OverseasPlaceOfSupply` is the const `"96"` (`:283`). Applied to **both** `Pos` and `Stcd` (`:272-273`).

### 3.1 🔴 The divergence that matters — CONFIRMED, and it is worse than "home vs blank"

For a **B2C inter-state supply with no recorded party State**, (A) returns the **company home code** and (B)
returns **blank**. Confirmed by reading the code, not inferred.

**The exact book/voucher shape that reaches it** — every link verified:

1. **A GST-enabled company** with a home State (say `19` West Bengal) and `EWayBillEnabled` irrelevant here.
2. **A party ledger** whose `PartyGst.StateCode` is a *different* State (say `07` Delhi). At entry,
   `GstService.IsInterState("07")` → `true`, so the voucher **posts IGST**.
3. **The State is then cleared on the live master.** This is permitted: `PartyGstDetails.StateCode` is
   `string?` (**[V]** `src/Apex.Ledger/Domain/PartyGstDetails.cs:22`) and `EnsureValid` (`:50-56`) rejects only
   an *invalid* code — `null` passes. *(Equivalently: an imported document carrying IGST legs against a
   stateless party. `ReadPostedRateGroups`/`PostedForwardRouting` read the posted legs, not the master.)*
4. **Reprint.** `VoucherPrintProjector` reads `PostedForwardRouting` (**[V]** `GstReportSupport.cs:1202`) →
   `true`. `ConsistentBuyerStateCode` (**[V]** `VoucherPrintProjector.cs:662-673`) computes
   `liveIsInterState == false` (the live code is blank), sees it contradict `postedInterState == true`, and
   returns **`null`** — its documented "inter ⇒ unrecoverable" limb (`:661`). `PlaceOfSupply(company, null, true)`
   then declines the home fallback and prints **blank**.
5. **The same voucher in GSTR-1.** `Gstr1.cs:259` calls form (A), which sees a null party State and returns
   the **home code `19`**.

**So the same document is simultaneously labelled place-of-supply `19` (West Bengal — the supplier's own
State) in the return, and place-of-supply *nothing* on the paper, while carrying posted IGST.** The GSTR-1
label is not merely different from the printed one — it **contradicts the tax the voucher posted**: a POS
equal to the supplier's State is the definition of an intra-state supply, which IGST denies. The print path's
whole purpose (its FIX-3 comment, `:632-648`) is to stop the paper contradicting the posted tax; form (A)
does exactly what form (B) was written to prevent, on the same voucher.

---

## 4. Q1 — What the LAW says when the recipient's State is not recorded

### 4.1 Goods — IGST s.10(1)(ca), and it is decisive

Quoted verbatim from **Circular No. 209/3/2024-GST** dated 26.06.2024, para 2, which reproduces the clause
(**[V]**, retrieved from `gstcouncil.gov.in` and extracted locally):

> "(ca) where the supply of goods is made to a person other than a registered person, the place of supply
> shall, notwithstanding anything contrary contained in clause (a) or clause (c), be the location as per the
> address of the said person recorded in the invoice issued in respect of the said supply and **the location of
> the supplier where the address of the said person is not recorded in the invoice**.
>
> Explanation.—For the purposes of this clause, **recording of the name of the State of the said person in the
> invoice shall be deemed to be the recording of the address of the said person**;"

The circular records that this took effect **01.10.2023** and that the 2023 amendment package was brought into
force by **Notification 02/2023-Integrated Tax dated 29.09.2023**. Para 2.1 confirms it is a **non-obstante**
provision overriding s.10(1)(a) and s.10(1)(c).

**Answer:** where the recipient is unregistered and no address is on the invoice, **the place of supply is the
location of the supplier** — i.e. the supply is **intra-State**. And per the Explanation, **recording the State
name alone is enough** to make it the recipient's address.

### 4.2 Services, domestic — IGST s.12(2)(b)

Quoted verbatim from the **Gazette of India** text of the IGST Act, 2017 (**[V]**):

> "(2) The place of supply of services, except the services specified in sub-sections (3) to (14),—
> (a) made to a registered person shall be the location of such person;
> (b) made to any person other than a registered person shall be,—
> (i) the location of the recipient **where the address on record exists**; and
> (ii) **the location of the supplier of services in other cases**."

**"Address on record"** is defined in **CGST s.2(3)** (**[V]**, `cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf`):

> "(3) 'address on record' means the address of the recipient as available in the records of the supplier;"

Note the difference from goods: for services the test is the supplier's **records**, not the invoice.

### 4.3 Services, cross-border — IGST s.13(2)

Verbatim from the same Gazette text (**[V]**):

> "(2) The place of supply of services except the services specified in sub-sections (3) to (13) shall be the
> location of the recipient of services:
>
> Provided that **where the location of the recipient of services is not available in the ordinary course of
> business, the place of supply shall be the location of the supplier of services**."

### 4.4 Is there a statutory DEFAULT to the supplier's location?

**Yes — three times over, and it is the same default each time.** s.10(1)(ca) (goods to an unregistered
person, no address on the invoice), s.12(2)(b)(ii) (domestic services, no address on record) and the s.13(2)
proviso (cross-border services, recipient's location unavailable) **all** name **the location of the supplier**.

There is a fourth, residual power the legislature took but which does **not** help here — **IGST s.10(2)**
(**[V]**, Gazette): *"Where the place of supply of goods cannot be determined, the place of supply shall be
determined in such manner as may be prescribed."* I could **not** locate any rule prescribed under it (§10,
item 3).

**The load-bearing conclusion for both rows:** the statute **never leaves the place of supply undetermined for
want of a recipient location**. Every gap is closed by falling back to the supplier. The one thing the statute
**does not** contemplate is a supplier whose own location is unknown — because a registered supplier always
has one: the first two digits of its GSTIN *are* its State code. **"No home State" is not a statutory
scenario. It is a data-integrity scenario.**

---

## 5. Q2 — Is the THROW or the RETURN-FALSE correct?

**Neither, and the question as posed has a false premise: row (d) is not a place-of-supply question at all.**

Row (d) fires on a missing **supplier** State, not a missing recipient State. §4.4 shows the statute has an
answer for every missing-recipient case and **no** answer for a missing-supplier case. So there is no rule to
be faithful to; there is only a book asserting two contradictory things at once — "route this supply under
GST" and "I have not said where I am."

In one sentence: **`return false` is a wrong-figure defect and the `throw` is right in kind but fires in the
wrong place.**

Unpacking that, strictly on what §4 establishes:

- **"Route it intra-state silently" is NOT a defensible reading of the fallback.** The statutory fallback is
  *"the location of the supplier"* — a **named, positive** value. `return false` does not supply that value;
  it asserts *"place of supply == home State"* while the home State is precisely the thing that is missing.
  It is not the statute's default applied with an unknown; it is an arithmetic accident of comparing two
  nulls. And it is consumed as a positive fact: an intra-state e-Way exemption branch and a per-State
  threshold lookup (`EWayBillService.cs:75-77`, `:132-143`) both key off it. **A figure derived from a fact
  the book does not have is the definition of a wrong figure.**
- **The `throw` is the right *kind* of answer** — refusal — because the only truthful thing to say about this
  book is "I cannot route this." But it is thrown from `IsInterState`, which sits on the **print** path
  (`VoucherPrintProjector.cs:275`), so its observed effect is that **an already-issued invoice cannot be
  reprinted** (defect F7, §2.3). A refusal at print time punishes a document that was correct when issued; the
  book became unroutable afterwards. **Where the refusal should fire is a design and user question, not a
  statutory one** — see §11.
- **The asymmetry is the actual bug.** One caller of a routing predicate treats "no supplier location" as
  fatal and another treats it as a routable fact. Both cannot be right, and §4 says the truth is neither.

**One caveat, stated so it is not read past:** §4 settles what the *place of supply* is. It does **not** say
what an accounting package must *do* when the book is incoherent — no source I retrieved addresses that. That
is why §11 exists.

---

## 6. Q3 — What does TallyPrime actually do? **The corpus does not say.**

This is a legitimate result, and it is a clean one.

**The term does not occur.** **[V]** All 10 corpus PDFs were converted with `pdftotext -layout` and grepped
case-insensitively: **"place of supply" returns ZERO hits across the entire corpus.** (Control: "supply"
returns 85 hits in the Book, 67 in the Study Guide, 36 in `703679456`, 8 in `719244897`; "IGST" and
"inter-state" likewise return dozens. The extraction is sound — the phrase genuinely is not there.) Three
PDFs (`517196318`, `567608375`, `654430402`, `712654832`) extract to little or no body text and carry nothing
on this topic either way.

**So the corpus cannot tell us whether TallyPrime forces, defaults, or blanks a "Place of Supply" field on a
voucher.** It never shows the field.

**What the corpus DOES show — and it is only indirect evidence:** **[V]**

- **Every party ledger records a State, for every registration type.** The Book's field-by-field walkthroughs
  give `State — Select Customer's state from list` for **Regular** (printed p.182), **Composition** (p.183),
  **Unregistered** (p.184, "Like I select 'Delhi'") and **Consumer** (p.185, "Like I select 'Delhi'"). The
  Practice Exercise table (p.186) lists a State for all four rows including the Consumer row
  (`Satish Kumar | Sundry Debtor | Jharkhand | Consumer | No`). *(The Book prints its page number in a
  **footer**, so content sits on the page whose marker follows it; page numbers here are read that way and
  cross-check against the Book's own table of contents.)*
- **B2C inter-state is modelled with a recorded State, never without one.** Book p.195-196 (the Book's own ToC
  gives 195 for this section): *"B2C Large: When any regular dealer makes supply to consumer exceeds 2.5 lacs
  and Inter-state"*, and Step 1 is literally *"Create Consumer ledger with **another state**"* (Danish Mishra
  Consumer / Delhi / Consumer), with *"Tax Ledger — IGST"*. Book p.196: B2C Small, *"Create Consumer ledger
  with **same state**"* (Bihar), with *"Tax Ledger — CGST+SGST"*.

**What that does and does not license.** It establishes that TallyPrime's own teaching material treats the
party's State as **always present** and as **the thing that drives IGST vs CGST+SGST** — which is our form (A)
minus the fallback. It does **not** establish what TallyPrime does when the State is absent, because the
corpus never constructs that case. **Do not read "the Book always fills it in" as "TallyPrime makes it
mandatory."** That inference is exactly the kind this project's dominant failure mode produces.

---

## 7. Q4 — Is the print path's "blank on inter-state" a Rule 46 breach?

**Rule 46(n), CGST Rules, 2017, verbatim** (**[V]**, consolidated Part A as at 01.06.2021, from
`gstcouncil.gov.in`):

> "(n) **place of supply along with the name of the State, in the case of a supply in the course of inter-State
> trade or commerce**;"

And the adjacent particulars that matter for a B2C document (**[V]**, same source):

> "(e) name and address of the recipient and the address of delivery, along with the name of the State and its
> code, if such recipient is un-registered and where the value of the taxable supply is fifty thousand rupees
> or more;
> (f) name and address of the recipient and the address of delivery, along with the name of the State and its
> code, if such recipient is un-registered and where the value of the taxable supply is less than fifty
> thousand rupees **and the recipient requests** that such details be recorded in the tax invoice;
> …
> (o) address of delivery where the same is different from the place of supply;"

**Answer: yes — printing blank on an inter-State supply omits a particular rule 46(n) requires.** The rule is
unconditional for inter-State supplies; there is no de-minimis and no "where known" qualifier. A document that
carries IGST and no place of supply is not a compliant tax invoice.

**But the rule does not choose between the two bad outputs, and it should not be read as endorsing the
alternative.** The projector's own comment (`VoucherPrintProjector.cs:653-657`) frames it as *"Blank is a
Rule-46 omission; a self-contradicting document is a Rule-46 falsehood"* — that reasoning is sound **as a
choice between two defective reprints**, and rule 46 supplies no text ranking an omission against a
falsehood. What rule 46 actually says is something neither branch addresses: the particular must be **on the
invoice when it is issued**. Read together with **s.10(1)(ca)'s Explanation** (§4.1) — recording the State
name *is* recording the address, and is what fixes the place of supply — the statute's concern is **capture at
issue**, not reconstruction at reprint.

**That is a finding about where the defect lives, not a design.** Whether the State should be snapshotted onto
the voucher, and what a reprint of an unreconstructable historical document should show, are design questions
this document does not answer. **UNVERIFIED:** no source I retrieved addresses whether a *reprint* of a
historical document is itself a fresh rule 46 breach, as distinct from the original issue (§10, item 7).

---

## 8. Q5 — The EInvoiceJson "96" path. **The prior pass was RIGHT. Confirmed, first-hand.**

**The state master, retrieved directly** (**[V]**, `einvoice1.gst.gov.in/Others/MasterCodes`, HTTP 200):

| Code | Official label |
|---|---|
| **96** | **OTHER COUNTRIES** |
| **97** | **Other Territory** |
| **99** | **OTHER COUNTRIES** |

**Confirmed exactly as the earlier pass stated**, including the oddity that **96 and 99 carry identical
labels** while 97 is distinct. And **97 is a DOMESTIC GST territory, not an overseas one** — corroborated in
the repo's own grounding (`GstReportSupport.cs:118-131`) against CGST §2(114)(g), which lists "other territory"
among the States/Union territories; it is the place of supply for India's continental shelf and EEZ.

**The INV-01 requirement, retrieved from the NIC schema workbook itself** — not from a summary. I downloaded
`EInvoice_Schema.xlsx` (193.7 KB), unzipped it, and parsed `xl/sharedStrings.xml` directly (**[V]**):

- Field description: **"State code of Place of supply. If POS lies outside the country, the code shall be 96."**
- Validation **15**: *"Recipient GSTIN should be registered and active. In case of transaction of direct
  export, recipient GSTIN has to be URP and state code has to be 96, POS should be 96."*
- Validation **16**: *"In case, Recipient is SEZ unit or SEZ developer, the Bill to State code should be 96 and
  also POS should be 96."*
- Validation **17**: *"First two digits of the Supplier / Recipient GSTIN should match with the state code
  passed in the Supplier / Recipient details accordingly except if supply type is SEZ or exports wherein
  Recipient state code will be 96."*
- Validation **24**: *"The state code of the Supplier GSTIN and POS will decide whether the supply type is
  Interstate or Intrastate…"* — and **25**: *"In case of Exports and SEZ, the supply is always Interstate"*.

**Corroboration from a separate first-party instrument:** **Circular No. 184/16/2022-GST** dated 27.12.2022
(**[V]**, `cbic-gst.gov.in/pdf/circular-184.pdf`) directs a supplier to report the place of supply *"by
selecting State code as '96- Foreign Country' from the list of codes in the drop-down menu available on the
portal in FORM GSTR-1."*

**Verdict: our use of `"96"` for export/SEZ at `EInvoiceJson.cs:267` is CORRECT, and it is MANDATED.** Note it
is mandated for **both** `Pos` and `Stcd`, which is what `:272-273` does. Validation 16 also settles the SEZ
limb specifically, which is the limb most likely to be second-guessed: an SEZ recipient is domestically
registered and has a real State, and the rule overrides it anyway.

---

## 9. Q6 — Can these be unified? **Partly. One row must stay different, and one must not.**

**Row (e)(C) — the e-invoice "96" limb MUST remain different. Refuse this one.** This is the HSN-sentinel
situation exactly. Validations 15/16/17 require a value that **no domestic place-of-supply rule can produce**:
96 is not in `IndianState.All` (**[V]** — the master carries 97 but neither 96 nor 99,
`GstReportSupport.cs:125-129`), and s.10/s.12/s.13 never yield it. Collapsing (C) onto a shared domestic rule
would break INV-01 conformance. **The correct shape is a shared rule that stops at the domestic derivation and
an e-invoice-side normalisation layered on top** — which is what the HSN row concluded, and it is not a
concession, it is the answer.

**Row (e)(A) — the report/engine rule IS the statute, and it is the one to unify on.** "Party State, else
company home" is precisely s.10(1)(ca)'s ladder: address on record, else the location of the supplier. It is
the only one of the three that is directly grounded.

**Row (e)(B) — the print rule is NOT a third place-of-supply derivation and should stop being classified as
one.** It answers a different question: *given a posted tax leg and a live master that may since have been
edited, what State may this document truthfully print?* That is a **reconciliation**, not a derivation. Its
purpose legitimately differs. **But its blank output has no statutory warrant** (§7), and §3.1 shows form (A)
producing, on the same voucher, exactly the self-contradiction form (B) exists to prevent. **The two are not
reconcilable as they stand** — and the mismatch is a real wrong-figure exposure between the printed invoice
and GSTR-1, not a cosmetic inconsistency.

**Row (d) — NOT statutorily required to differ. Unifiable in principle, but not into a `bool`.** §4.4 shows
there is no statutory scenario behind either behaviour, so nothing in law requires two answers. The obstacle
is the return type: `bool` has no room for "cannot route", so any unification must decide **whether the
unroutable case is an exception, a third state, or a value** — and **where** it is detected. That is §11.

**Net:** of the two open rows, **(e) is one-third statutorily locked and two-thirds a genuine defect**, and
**(d) is wholly a defect**. Neither row is a pure de-duplication, and neither should be closed by picking the
more popular copy.

---

## 10. 🔴 What the sources could NOT settle

**This section is the point of the document. Do not fill any of these in from memory.**

1. **What TallyPrime does with a "Place of Supply" field.** The corpus contains **zero** occurrences of the
   phrase (§6). Whether TallyPrime shows the field, forces it, defaults it, or leaves it blank is
   **UNVERIFIED**. The corpus is study material, not TallyPrime's official help; its silence is evidence about
   the corpus, not about the product.
2. **Whether TallyPrime permits a party ledger with no State.** Every corpus walkthrough records one (§6). The
   negative case is never constructed. **UNVERIFIED.**
3. **Whether any rule has been prescribed under IGST s.10(2).** I found none in the consolidated CGST Rules
   Part A I retrieved, and could not locate IGST Rules text on a reachable `.gov.in` host. **A negative cannot
   be proven from a failed search** — treat as **UNVERIFIED**, not as "no rule exists".
4. **Which instrument inserted clause (ca).** Circular 209/3/2024 attributes the 01.10.2023 commencement to
   **Notification 02/2023-Integrated Tax** and the **IGST (Amendment) Act, 2023 (31 of 2023)**; several
   secondary sources instead say the Finance Act, 2023. I could not read either at source (`egazette.gov.in`
   and `indiacode.nic.in` both failed, §1.1). **UNVERIFIED — and immaterial**: the clause's text and its
   01.10.2023 effect are first-party confirmed either way.
5. **Whether `Enabled: true` with `HomeStateCode == null` is reachable through a UI path.** The shape is
   representable and the two methods disagree on it, but `EnsureValid` guards the enable path and I did not
   trace every writer of `GstConfig` (`GstConfigViewModel` mutates fields directly in at least three places:
   `:1545`, `:1702`, `:1794`). **UNVERIFIED.** The `Gst == null` leg of the throw **is** proven (§2.3).
6. **The vintage of the rule text.** The CGST Rules PDF is the consolidation **as at 01.06.2021**
   (`01062021-…`) and the CGST Act PDF is **as on 30.09.2020**. Rule 46(n) and s.2(3) are quoted from those. I
   did **not** verify either is unamended since. **UNVERIFIED** — low risk, but do not present these as
   current-as-of-today.
7. **Whether a REPRINT of a historical document is itself a rule 46 breach**, as distinct from the original
   issue. No source addresses it. **UNVERIFIED** — and it is the hinge of §7.
8. **The corpus's "2.5 lacs" B2C-large threshold** (Book p.195-196) was **not** verified against current law
   and is not relied on anywhere above. Do not carry it forward as a fact.
9. **What an accounting package must DO with an incoherent book.** §4 settles the place of supply. Nothing I
   retrieved says whether software must refuse, warn, or proceed. This is the gap §11 hands to the user.

---

## 11. 🔴 Q7 — What the USER must decide

No source settles this. It is a product decision with a statutory floor, and it gates any unification of row (d).

> **Question.** A GST-routing book that does not declare its own home State cannot compute a place of supply —
> the statute's fallback is *"the location of the supplier"*, and that is the missing value (§4.4). Today one
> code path throws and another silently routes the supply intra-state (§2). **Where should the refusal live,
> and what should already-issued documents do?**

**Options** (each consistent with §4; the law does not choose among them):

- **(A) Refuse at the gate.** Make a null/blank home State impossible while GST is enabled — enforce it as an
  invariant at every write, not only in `EnsureValid`. Routing then never sees the case, and both copies
  collapse to one plain `bool`. *Cost:* existing books in that state must be migrated or blocked on open;
  someone must decide what happens to them.
- **(B) Refuse at the routing call, but only for NEW postings.** Keep a loud failure when computing tax, and
  give read-only paths (reprint, reports, e-Way coverage) a non-throwing form that yields "unknown" rather
  than "intra-state". *This is the option that would close defect F7 as a side effect.* *Cost:* the return
  type stops being `bool`, and every one of the 7 call sites in §2.2 must say what it does with "unknown".
- **(C) Warn and proceed on the statutory default.** Treat the absent home State as a data defect, surface it,
  and continue. *Cost:* this is today's `return false` with a message attached, and §5 explains why the figure
  it produces is still derived from a fact the book does not have.

**A14's reading, offered as evidence and not as a decision:** §5 rules **(C)** out on wrong-figure grounds.
Between **(A)** and **(B)**, the statute is silent; **(B)** additionally resolves F7, which **(A)** does not.

**A second, narrower ruling is also needed** and is separable from the first: **should the party's State be
snapshotted onto the voucher at posting**, so that §3.1's shape — a printed blank contradicting a GSTR-1 home
code on a voucher carrying IGST — becomes unreachable rather than being arbitrated at print time? §7 shows the
statute's concern is capture at issue. **This is a schema question and it is outside this document's scope.**

---

## 12. Cross-reference

This document is cited by `plan.md`'s **W0-11 carry-forward (b)**. The exact text to add there is reproduced
in the A14 report that accompanied this file; `plan.md` was **not** modified by this pass (another workflow
owns it).
