# W0-2 — Company Create/Alter: Corpus Grounding

**What this is:** the R7 fidelity grounding for `plan.md` item **W0-2** (census **S2**) — the Company
Create/Alter screen, which is the fix for **T0-8** (every printed invoice carries a blank seller address
block, breaching CGST Rule 46) and for **T1-6** (company creation captures one field).

**What this is NOT:** a design. Nothing here says what our screen should look like, which fields it should
carry, or in what order. It records **what TallyPrime does, per the corpus** and **what our code does today**,
so that a design can be argued from evidence instead of invented.

**Pass:** A14 Tally Domain/Corpus Expert, read-only, from the 10 git-ignored PDFs at `…\Apex Solutions(end)\tally\`
via `pdftotext -layout`. The original pass existed only in a session transcript; this file is the write-down.

**Baseline for every code claim:** worktree `…\.claude\worktrees\recursing-swirles-3138c6`, HEAD **`fa651ae`**,
schema **v50**. Nothing built, run, or edited to produce this document.

**Date:** 2026-08-14.

> 🔴 **§8 contains a design blocker that requires a USER DECISION before any code is written.** (Corrected
> 2026-08-14: this line pointed at **§7**, which is the code comparison; the blocker itself is **§8**, and §7.3
> is the finding it rests on.) It is not a preference question. Our company row already carries the exact State
> duplication that `Schema.cs:808-811` forbids on the party row, and the corpus points at a third answer
> (inheritance) that neither side implements.
>
> **This document is cited by `plan.md`'s W0-2 row (`plan.md:1529-1535`), and §8's blocker is carried there as
> an explicit R12 USER GATE (`plan.md:1536-1550`) — W0-2 must not start until the user rules on it.** A blocker
> that lives only in `docs/` gates nothing (R6: `plan.md` is the single source of truth).

---

## 0. Markers

**[V]** = re-verified first-hand during this write-down, at HEAD `fa651ae`, by opening the file or extracting
the PDF page. **[A14]** = relayed from the original A14 pass and **not re-verified here** — trustworthy, but if
you are about to act on it, extract the page yourself. **UNVERIFIED** = the corpus does not answer it; §9 is the
list, and it is the most valuable part of this document because it is what stops a future session inventing.

---

## 1. Corpus inventory and weighting

| PDF | Weight | What it carries |
|---|---|---|
| `664311548-Tally-Prime-Book.pdf` | **PRIMARY** | Field-by-field Company Creation walkthrough (PDF p.12-15); company GST Details (PDF p.177-178). **[V]** The Book's printed "Page 8" = PDF p.12 — **offset 4**, confirmed on both ranges. |
| `696054070-TALLY-PRIME-STUDY-GUIDE.pdf` | **PRIMARY** | Company Creation **with the real section headings** (p.57-60); Alter/Select/Shut (p.61); Group Company worked example (p.267-268). |
| `680842180-Tally-With-GST-Notes.pdf` | secondary | Newer GST Details grouping (p.1). **[A14]** |
| `703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf` | secondary, **MIXED VINTAGE** | Its Multi-Address section says "Tally.ERP9" (p.38) and it uses the ERP-9 `F11 > F3 Statutory & Taxation` path (p.122). Treat its screen paths as ERP-9-era unless corroborated. **[A14]** |
| `712654832-Fundamental-of-Accounting-…-Note1.pdf` | secondary | Create/Shut/Alter/Delete paths (p.4-5). **[A14]** |
| `719244897-Tally-Book.pdf` | secondary | Create/Alter/Delete shortcuts (p.2-3); print-side "Show company address" config (p.76, p.99). **[A14]** |
| `567608375`, `654430402`, `517196318` | — | Exercise/problem sets. No company-screen field detail. **[A14]** |

### 1.1 🔴 `659947760-Tally-Prime-Short-Key.pdf` is REJECTED as a source — do not cite it

**[V]** Extracted p.1-3 this session. Its two-column table is misaligned against its own key column, and the
misalignment is visible in the extraction itself: row 36 reads `Alt+2 → Sales Order` and is then followed by two
orphaned labels — `Delivery Note`, `Duplicate Entry` — carrying no key at all. Everything downstream of a break
like that is shifted.

What it asserts, and what the primary source says instead:

| Short-Key PDF asserts **[V]** | Book `664311548` p.15 says **[V]** |
|---|---|
| `F3` = Email (row 10) | `F3` = **Change Company** |
| `Alt+F3` = Print (row 11) | `Alt+F3` = **Open (Select) Company** |
| `Ctrl+F3` = Change Company (row 12) | `Ctrl+F3` = **Shut (Close) Company** |
| `F11` = Switch Company (row 13) | — (F11 is Company Features) |
| `F12` = Shut Company (row 14) | — (F12 is configuration) |
| `Ctrl+A` = Zoom (row 17) | `Ctrl+A` = **Accept/save** (Book p.14, SG p.268) |
| `F6` = Contra (row 29) | — |
| `F7` = Payment (row 30) | — |

The last two also contradict this project's **settled keyboard contract**, in which **F4 = Contra**.

**This is a corpus-hygiene ruling, and it must not be quietly undone.** A future session that finds this PDF and
takes a shortcut from it will import a two-row shift into the product. If you disagree, re-extract p.1-3 and
argue with the orphaned-label evidence — do not simply cite the file.

---

## 2. Company Creation — fields in screen order

Order and headings are the **Study Guide's** (p.57-60 **[V]**, headings are literal); the Book (p.13 **[V]**)
corroborates every field.

**Ungrouped header:** **1** Directory — "the data storage location … By default, the data storage will be inside
the installation folder" · **2** Name (the Book calls it "Company Name").

**Primary Mailing Details:** **3** Mailing Name — auto-filled from Name, editable ("the mailing name of company
will be automatically updated as per the given name of the company, however, the mailing name can be changed").
🔴 The Book is explicit about what this field is *for*: **"Type Company's Short name here for Show in
Invoice/Bill"** — **this is the name that prints on the invoice**. · **4** Address — "Update the complete address
details of company"; free text, multi-line · **5** Statutory Compliance for = Country ("Choose the country name
as India"); the Book labels this field "Country" · **6** State (from a list) · **7** Pin Code (the Group-Company
example spells it "Pincode", SG p.268).

**Contact Details:** **8** Telephone No · **9** E-Mail · **10** Mobile No · **11** Fax no · **12** Website.

**Books and Financial Year Details:** **13** Financial Year From (the Book: "Financial year begins from") — "will
be automatically displayed", Indian FY 1-Apr → 31-Mar, overridable · **14** Books beginning from — defaults to
the FY start, differs only for a mid-year incorporation. Both sources agree, and the Book gives the worked case:
company created 01.07.2021 ⇒ FY begins 01.04.2021, books begin 01.07.2021.

**Security Control:** **15** Tally Vault Password · **16** User Access Control.

**Base Currency Information:** **17** Base Currency Symbol · **18** Formal Name (INR for India) · **19** Suffix
Symbol to Amount · **20** Add space between amount and symbol · **21** Show amount in Millions · **22** Number of
decimal Places (2) · **23** Word representing amount after decimal ("Paisa" by default) · **24** No of decimal
places for amount in words (2).

### 2.1 ⚠️ The field order is contested — and one source contradicts itself

**[V]** Two separate order conflicts, both confirmed by extraction this session:

1. **Contact block.** Study Guide p.59 prose gives Telephone → **E-Mail** → **Mobile** → Fax → Website. The Book
   p.13 gives Telephone → **Mobile** → Fax → **Email** → Website. Both documents are "TallyPrime".
   🔴 **New, not in the original A14 pass:** the **Study Guide's own worked example at p.268 uses the BOOK's
   order** — Telephone, Mobile, Fax, E-Mail, Website. So this is not two sources disagreeing; it is one source
   disagreeing with itself, with its example siding against its prose. The corpus cannot settle it.
2. **Country vs State.** 🔴 **New, not in the original A14 pass:** the Book p.13 lists **Address → State →
   Country → Pin Code**; the Study Guide p.58-59 lists **Address → Statutory Compliance for (Country) → State →
   Pin Code**. The two primaries invert the middle pair.

### 2.2 🔴 Mandatory fields: the corpus never marks any company field mandatory

The closest thing is `712654832` p.4 ("Company Name: Super Traders / Fill up: Address, State and other optional
details"), which is ambiguous about what "optional" attaches to. **[A14]** Report as UNVERIFIED (§9 item 1).

The absence is **meaningful, not an omission**: the same Study Guide *does* explicitly mark PAN mandatory on a
**Ledger**. It knows how to say "mandatory" and does not say it here. **[A14]**

### 2.3 Post-save

**[V]** SG p.60: "After saving the company, takes you to the Company Features screen, which displays that the
company is created successfully." **F11 opens automatically after a save.**

---

## 3. Alter Company vs Creation

**Routes.** **[V]** Book p.15: `Gateway of Tally > Alt+K > Alter`. **[V]** SG p.61 / p.267 use the same `Alt+K`
company menu. **[A14]** `703679456` p.2 `F3: Company Info > Alter`; `712654832` p.5 `F3 : Company > Alter Company`.

**Delete lives on the Alter screen.** **[V]** Book p.15, verbatim: `Gateway of Tally > Alt+K > Alter > Alt+D >
Press Two times 'Enter' Button`.

**What Alter has that Creation does not:** **only** the `Alt+D` delete action. **No field is documented as
Alter-only.**

**What Creation has that Alter does not:** **only** Directory (inherently creation-time), and `Alt+R` → Group
Company (**[V]** SG p.267 step 3; **[V]** Book p.14 gives it as `Alt+K > Create > Alt+R`), whose creation screen
adds **Member Companies** (**[V]** SG p.268).

**Editable after creation.** **[V]** The Book states Alter's purpose directly: companies "will alter or edit
their information when they have changed company **address** or **contact number** or **email** and other any
information" (p.15). Those three are explicitly editable.

🔴 **UNVERIFIED:** no source states that **any** company field becomes read-only after creation. Whether
Financial Year From, Books beginning from, or Base Currency lock once vouchers exist is **unknown**. Do not
assume either way (§9 item 2).

---

## 4. Statutory / GST Details — the most important structural finding

**GST details are NOT on the Company Creation screen.** They are reached through **F11 (Company Features)**, and
the **GSTIN — the Rule 46(a) particular — lives only there.**

**[V]** Current TallyPrime route, Book PDF p.177 (printed p.173), verbatim: `GOT > F11` → activate "Enable Goods
& Services Tax (GST)" → "Press `Enter' to filling all GST details" → the GST Details screen appears.
**[A14]** Corroborated by `680842180` p.1.

**[A14]** An older ERP-9-era route with a different prompt exists in `703679456` p.122: `F11 > F3 Statutory &
Taxation` / "Enable Goods and Services Tax (GST) set to Yes" / "**Set/Alter GST Details** set to Yes" / "GST
Details subscreen appears".

⚠️ **Two different mechanisms.** The "Set/Alter GST details" yes/no phrasing is abundantly attested for **party
ledgers** and **stock items**, but for the **company** it appears only in the ERP-9-vintage document. **Use the
Enter-opens-sub-screen form for company-level fidelity.**

### 4.1 Fields on the company GST Details screen

**[V]** All ten extracted verbatim from Book PDF p.177-178 this session:

- **State** — "Select your state from list. **By default shows the State name as selected in the Company
  Creation screen.** This helps in identifying local and interstate transactions according to the party's state."
  🔴 **This is the crux of §7.** TallyPrime **inherits** the GST State from the Creation screen rather than
  storing a second one. The page also opens with the standing warning: *"In company creation time State must be
  selected right."*
- **Registration Type** — Regular / Composition.
- **Assessee of Other Territory** — for units inside SEZ/EEZ.
- **GST applicable from** — "Enter the date you want to start using or Implement GST … You cannot use GST
  transactions before to this date."
- **GSTIN/UIN** — "Tally will warn you in case you entered a wrong GSTIN. **This GSTIN/UIN number printed in
  your Invoice.**"
- **Periodicity of GSTR-1** — Monthly / Quarterly.
- **Set/Alter GST rate details** → opens a rate sub-screen (description, HSN/SAC, type of goods/services, rate).
- **Enable tax liability on advance receipt.**
- **Enable tax liability on reverse charge** (purchase from unregistered dealer).
- **Enable GST classification.**
- **Provide LUT/Bond details.**

**[A14]** A newer grouping in `680842180` p.1 splits these into "GST Registration details" (State, Registration
type, GSTIN/UIN) and "Invoice features" (e-way bill applicable, e-invoicing applicable). **[A14]** Accept with
`Ctrl+A`, then `Ctrl+A` again (Book p.180).

---

## 5. The address block and CGST Rule 46

### 5.1 One Address field, not numbered lines

TallyPrime gives **one multi-line free-text Address field** plus **separate State, Country, Pin Code**. It is
**not** a set of numbered "Address Line 1/2/3" fields.

**[V]** Evidence, the Group Company worked example at SG p.268 — three physical lines sit under a single
`Address:` label, and the structured fields follow:

```
Address: 13A, Picnic Garden Road
    3rd Lane
    Kolkata
State: West Bengal
Country: India
Pincode: 700039
Telephone: 23568901
Mobile: 9856230147
Fax: 23568902
E-Mail: tutorjoes@gmail.com
Website: www.tutorjoes.in
```

🔴 **UNVERIFIED:** the corpus never states a **maximum** number of address lines. Do not invent one (§9 item 3).

### 5.2 Multiple addresses are a separate opt-in

**[A14]** `F11` "Maintain Multiple Mailing Details for Company & Ledgers?" = Yes → "Set/Alter multiple mailing
details" = Yes → an Address Type screen (`703679456` p.38-41). **That section self-identifies as Tally.ERP9** —
the **feature** is attested, the **TallyPrime path** is UNVERIFIED (§9 item 7).

### 5.3 Print-side gating is separate from capture

**[A14]** `Configure → Company details → "Show company name: Yes / Show company address: Yes"`
(`719244897` p.76, p.99; `703679456` p.41). Capturing the address and printing it are two different switches.

### 5.4 CGST Rule 46 — the statutory particulars

**[A14]** Opening: a tax invoice "shall be issued by the registered person containing the following particulars" —
**(a)** name, address and GSTIN of the **supplier**; **(b)** a consecutive serial number not exceeding sixteen
characters, unique for a financial year; **(d)** name, address and GSTIN/UIN of the **recipient if registered**;
**(e)** name and address of an **unregistered** recipient plus the address of delivery with State and code where
taxable value ≥ ₹50,000; **(n)** place of supply with the State name for an **inter-State** supply.

⚠️ **Sourcing caveat, carried forward unchanged.** The official CBIC endpoint `taxinformation.cbic.gov.in`
**failed TLS chain validation** from the A14 environment; the text above was taken from
`https://gstzen.in/a/tax-invoice-cgst-rule-46.html` and is consistent with the CBIC verification already recorded
in `memory.md`. **If a first-party citation is required at the design gate, that fetch still has to happen from a
working network path** (§9 item 10). It was **not** re-attempted during this write-down.

### 5.5 The mapping

| Rule 46 particular | TallyPrime field | Where it lives |
|---|---|---|
| **46(a)** supplier **name** | Mailing Name | Company **Creation** (Book p.13 is explicit: "for Show in Invoice/Bill") |
| **46(a)** supplier **address** | Address + State + Pin Code | Company **Creation** |
| **46(a)** supplier **GSTIN** | GSTIN/UIN | **F11 GST Details — not Creation** |
| **46(b)** serial number | voucher numbering | already built |

🔴 **Scoping fact.** Rule 46 requires only **name, address and GSTIN**. **Pin Code, Telephone, Mobile, Fax,
E-Mail and Website are Tally-fidelity fields, not compliance fields.** A design that trims scope should trim
there, and a design that ships all of them should say it is doing so for fidelity, not for the statute.

---

## 6. Navigation and keyboard

**Reaching Creation.** **[V]** `Gateway of Tally > Alt+K (Company menu) > Create` (Book p.12; SG p.57).
**[A14]** Or `F3 (Company) > Create Company` (`712654832` p.4; `703679456` p.1; `719244897` p.2-3).

**On the Creation screen.** **[V]** `Alt+R` → Group Company creation (SG p.267). **[V]** `F12` → "Press F12 to
get more options as required for the company" (SG p.58) — 🔴 **its contents are UNVERIFIED**; no source
enumerates them (§9 item 4).

**Accept/save.** The corpus gives **both** forms and does not reconcile them: **[V]** `Ctrl+A` ("After Fill All
Option in Company Screen Press `Ctrl+A'", Book p.14; SG p.268 step 5) and **[V]** Enter-then-Enter ("accept the
screen by pressing Enter and again Enter to accept and save", SG p.60).

**Company navigation shortcuts.** **[V]** Book p.15, stated as explicit worked steps rather than as a table row —
this passage is what validates the mis-interleaved shortcut table on Book p.435, because reading-order alignment
of that table reproduces these three exactly:

| Key | Action |
|---|---|
| `F3` | change/switch company (from **open** companies) |
| `Alt+F3` | select/open a company (from the data path) |
| `Ctrl+F3` | shut the loaded company |
| `F11` | Company Features |
| `F12` | configuration for the current report/view |
| `Alt+D` | delete (from the **Alter** screen) |

🔴 **UNVERIFIED: Escape behaviour on the Company Creation screen.** The Book's p.435 table does not list `Esc` in
the company region, and the only source that does is the **rejected** `659947760` (§9 item 5). **[A14]** for the
p.435 negative.

---

## 7. Comparison to our code — re-verified at HEAD `fa651ae`

Every `file:line` below was opened during this write-down. Where the original A14 pass gave a line number that
has since drifted, the **corrected** number is given and the drift is called out; §10 lists them all.

### 7.1 The domain type

**[V]** `src/Apex.Ledger/Domain/Company.cs` — `:59` `Guid Id` · `:62` `string Name` (required, non-empty) ·
`:65` `string MailingName` ("Defaults to Name, editable") · `:67` `string? Address` · `:68` `string Country =
"India"` · `:69` `string? State` · `:70` `string? Pin` · `:73` `DateOnly FinancialYearStart` · `:76` `DateOnly
BooksBeginFrom` · `:78` `string BaseCurrencySymbol = "₹"` · `:79` `string BaseCurrencyName = "INR"` · `:80`
`int DecimalPlaces = 2` · `:81` `string DecimalUnitName = "Paisa"` · `:89` `GstConfig? Gst` · `:100`/`:104`
`TdsConfig?`/`TcsConfig?`. Plus `:348` `PrimaryCostCategoryName`, `:351` `MainLocationName`, and the F11 toggle
set.

✅ **[V] The plan's "11 profile fields that already exist" (`plan.md:1525`) is EXACT.** Excluding `Name`, lines
65-81 are precisely eleven: MailingName, Address, Country, State, Pin, FinancialYearStart, BooksBeginFrom,
BaseCurrencySymbol, BaseCurrencyName, DecimalPlaces, DecimalUnitName.

### 7.2 The printer

**[V]** `src/Apex.Desktop/Services/VoucherPrintProjector.cs:682-688` `SellerBlock`:

- `Name` = `CompanyDisplayName` (`:679-680` — MailingName falling back to Name, **matching Tally's convention
  exactly**)
- `AddressLines` = `SplitAddress(company.Address)`
- `Gstin` = `company.Gst?.Gstin ?? ""`
- `StateText` = `StateText(company.Gst?.HomeStateCode)`

Called from `:388` (item pass) and `:509` (service pass). `SplitAddress` (`:770-779`) returns `Array.Empty` on
null/whitespace.

**[V]** `src/Apex.Ledger.Io/InvoicePdf.cs:509-530` `DrawPartyBlock`, called at `:293` with caption `"Supplier:"`.
The address `foreach` (`:515-520`) never executes when the list is empty, and the State line (`:521-525`) is
skipped when `StateText` is blank. **So what a GST-off company prints today is:** `"Supplier:"` / `<company
name>` / `"GSTIN: Unregistered"` — **the address emits nothing at all**: no placeholder, no blank line; the block
silently collapses.

**T0-8 confirmed exactly as the census states.** **[V]** The census's supporting claim also still holds: across
`src/Apex.Desktop` there is **no assignment site** for `Company.MailingName` or `Company.Address` (the only
`MailingName` writes in the Desktop layer are `LedgerMasterViewModel.cs:582, 803, 965, 988, 1152`, which are the
**party** mailing block, a different object). The only writers anywhere are `ApplyJournal.cs:343-344` and
`ImportPlan.cs:1180-1181`.

> **[V] Census drift, for whoever maintains it:** `docs/full-clone-census.md:86` cites `VoucherPrintProjector.cs:734-739`
> for `SellerBlock`. That was true at the census baseline `468a96e`; at `fa651ae` the method is at **`:682-688`**.
> Not a false claim — a stale one.

### 7.3 🔴 Two findings the census does not record

**(i) `Company.State` and `Company.Pin` are never printed on an invoice.** `SellerBlock` takes its State from
`company.Gst?.HomeStateCode` (`:687`), **not** from `company.State`, and nothing appends `Pin`. The **buyer**
side does append PIN — **[V]** `VoucherPrintProjector.cs:739-748` `BuyerAddressText` adds Country (`:745`) and
`"PIN: " + …` (`:746`). **The seller block has no equivalent.**

> ⚠️ **Corrected during this write-down.** The A14 pass stated that populating `Company.Address` alone "would
> still print a seller address with **NO State line** and NO PIN". The no-PIN half is confirmed. The no-State
> half is **too strong**: `InvoicePdf.cs:521-525` *does* draw a `"State: …"` line whenever `StateText` is
> non-empty. The accurate statement is: **no State line derived from `Company.State` can ever print**. A
> GST-enabled company prints its **GST home State**; a GST-off company prints no State line at all. The
> asymmetry against the buyer block is real either way, and so is the point it supports — a postal State typed
> into `Company.State` would go nowhere.

**(ii) The GSTIN half of Rule 46(a) is already typeable.** **[V]** `GstConfigViewModel.cs:377-386` exposes
`Gstin` (`:377`), `HomeState` (`:380`), `RegistrationType` (`:383`) and `Periodicity` (`:386`) through the live
"GST — Statutory" screen (**[V]** `MainWindowViewModel.cs:3641-3648` `ShowGstConfig`). **T0-8 is purely a
postal-address defect, not a GSTIN defect.**

### 7.4 Field map

**HAVE and typeable today:** `Name`; and via **GST — Statutory**: GSTIN/UIN, GST State, Registration Type,
Periodicity of GSTR-1.

> 🔴 **FALSE as originally written — corrected here.** The A14 pass also listed **"GST applicable from"** as
> typeable today. It is **not**. **[V]** The domain member exists (`src/Apex.Ledger/Domain/GstConfig.cs:39`
> `DateOnly? ApplicableFrom`) and the column is persisted (`SqliteCompanyStore.cs:4502` `gst_applicable_from`),
> but **no Desktop source file or `.axaml` exposes it** — a grep across `src/Apex.Desktop/ViewModels` and
> `src/Apex.Desktop/Views` returns hits only for price lists, voucher numbering, e-invoice and e-Way. The
> `ShowGstConfig` doc comment (`MainWindowViewModel.cs:3636-3639`) enumerates the screen itself and lists only
> the Enable toggle, GSTIN, Home State/UT, registration type and return periodicity. It belongs in the next
> bucket, not this one. Note this is a **GST-screen** gap, not a Company-Creation gap — Tally puts that field on
> F11 (§4.1), so W0-2 is not obviously the item that should fix it.

**HAVE on domain + schema, NOT TYPEABLE ANYWHERE — the W0-2 payload:**

| Tally field | Our member / column |
|---|---|
| Mailing Name | `MailingName` / `mailing_name` |
| Address | `Address` / `address` |
| Statutory Compliance for | `Country` / `country` |
| State | `State` / `state` |
| Pin Code | `Pin` / `pin` |
| Financial Year From | `FinancialYearStart` / `financial_year_start` |
| Books beginning from | `BooksBeginFrom` / `books_begin_from` |
| Base Currency Symbol | `BaseCurrencySymbol` / `base_currency_symbol` |
| Number of decimal Places | `DecimalPlaces` / `decimal_places` |

**HAVE under a different name:** Formal Name → `BaseCurrencyName` / `base_currency_name` · Word representing
amount after decimal → `DecimalUnitName` / `decimal_unit_name`.

**MISSING ENTIRELY** — no domain member, no column. **[V]** Re-confirmed by grep over `Company.cs` and the
`companies` DDL in `Schema.cs`: Telephone No · E-Mail · Mobile No · Fax No · Website · Suffix Symbol to Amount ·
Add space between amount and symbol · Show amount in Millions · No of decimal places for amount in words ·
TallyVault Password · User Access Control · **Directory** (our model is one `.db` per company chosen by the
storage layer — **a deliberate architectural difference, not a gap**) · **PAN/TAN at company level** (a `tan`
column exists at `SqliteCompanyStore.cs:4504`, but only as the **TDS deductor identity**).

**OURS HAS, TALLY'S CREATION SCREEN DOES NOT:** `PrimaryCostCategoryName`, `MainLocationName`, and the persisted
statutory/payroll config block.

### 7.5 The "only Name" claim is true, and proven

**[V]** `src/Apex.Desktop/Views/MainWindow.axaml:228-244` is the entire create-company form: a single `TextBox`
bound to `NewCompanyName` (`:236`), a **static** label "Financial year begins 1-Apr; base currency ₹ INR."
(`:238`), and a "Create (Ctrl+A)" button (`:240`).

**[V]** `src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:827-839` `CreateCompany()` trims the name, refuses
empty, calls `CompanyFactory.CreateSeeded(name)`, saves, opens. `ShowCreateCompany()` at `:815-824` sets
`ScreenTitle = "Company Creation"`. **One field, one binding.**

**[V]** `src/Apex.Ledger/Services/CompanyFactory.cs:17-25` **already accepts** `financialYearStart` /
`booksBeginFrom` as optional parameters (`:19-20`) — **the UI simply never passes them.**

🔴 **[V]** Its default (`:22`) is `new DateOnly(DateTime.Today.Year, 4, 1)` — **the current calendar year** — so a
company created in Jan-Mar is stamped an FY-start of 1-Apr **of that same calendar year**, a date in the
**future** relative to the live Indian FY. **Census T1-6 confirmed and sharpened.**

**[V] No Alter screen exists.** The `Screen` enum (`MainWindowViewModel.cs:17`, members from `:19`) has
`CompanySelect` (`:19`), `CreateCompany` (`:20`), `BackupCompany` (`:43`), `RestoreCompany` (`:44`) — and **no
`AlterCompany`**. A grep for `AlterCompany` across all of `src/` returns **zero hits**.

### 7.6 Schema

**[V] For the 11 existing fields: NO MIGRATION.** `src/Apex.Persistence.Sqlite/Schema.cs:166-181` — the
`companies` table carries `name`, `mailing_name`, `address`, `country`, `state`, `pin`, `financial_year_start`,
`books_begin_from`, `base_currency_symbol`, `base_currency_name`, `decimal_places`, `decimal_unit_name`,
`primary_cost_category`, `main_location` — **all present since v1**.

**[V] Write path.** `SqliteCompanyStore.cs:4489` `DELETE FROM companies WHERE id = $cid`, then `InsertCompany` at
`:4492`, whose `INSERT` at `:4497` lists all eleven (`:4498-4501`). **Save is a delete-and-reinsert full
rewrite**, so an Alter screen needs **no new UPDATE statement**: mutate the domain object and call the existing
`_storage.Save(company)`.

**[V] Read path.** `:1275` `FROM companies WHERE id = $id`, mapped at `:1288-1300`.

**[V] For the 5 Tally contact fields** (Telephone / E-Mail / Mobile / Fax / Website): **yes, a migration** —
`Schema.CurrentVersion` is **50** at `fa651ae`, so the next is **v51**. The precedent is directly at hand: **v45**
did exactly this on the **buyer** side — `Schema.cs:812-815` added `mailing_name`, `mailing_address`,
`mailing_country`, `mailing_pincode` to `ledgers` as four nullable TEXT columns — and that is what made the buyer
address printable.

---

## 8. 🔴🔴 The design blocker — this needs a USER DECISION before any code

**[V]** `Schema.cs:808-811` carries an explicit standing prohibition on the **party** side, verbatim:

> there is deliberately **NO** `mailing_state` column. The party's State/UT is `party_gst_state` above, which
> drives GST place of supply (CGST+SGST vs IGST); a second stored State could contradict it and silently produce
> the wrong tax head, so the mailing screen's State field reads/writes that one column through
> `Ledger.MailingStateCode`. **Do not add `mailing_state`.**

**The company side already has the very duplication the party side forbids.** **[V]** A postal `companies.state`
(`Schema.cs:172`) **and** a GST `companies.gst_home_state` (`Schema.cs:188`), with the printer reading **only the
latter** (`VoucherPrintProjector.cs:687`).

**A Company Alter screen that exposes `Company.State` as an editable field creates a second, divergent supplier
State that no code reads** — the exact failure mode that comment was written to prevent, and worse than the party
case, because here the divergent column *already exists* and is *already persisted*.

**[V] The corpus points at a third answer.** TallyPrime's GST Details State "**by default shows the State name as
selected in the Company Creation screen**" (Book p.177) — it **inherits** rather than duplicating, and the same
page gives the reason: "This helps in identifying local and interstate transactions according to the party's
state," under a standing warning that "In company creation time State must be selected right."

So there are at least three shapes on the table — expose both (divergence risk), suppress the postal one (breaks
the §7.5 field-map and Tally's screen), or wire one to the other as Tally does (matches the corpus, but changes
what `gst_home_state` means and touches the GST screen, which is outside W0-2 as written).

**This needs a decision, not a guess.** Nothing in this document chooses.

---

## 9. What could not be verified from the corpus

*Preserved verbatim from the A14 pass. This list is the point of an R7 document: it is what stops a future
session inventing.*

1. Which company fields are **mandatory**.
2. Which fields become **non-editable after creation**.
3. The **maximum number of address lines**.
4. The **contents of F12** on Company Creation.
5. **Esc behaviour** on Company Creation.
6. **Defaults** for Suffix Symbol to Amount, Add space between amount and symbol, Show amount in Millions,
   TallyVault Password, User Access Control (the Book shows the author's **chosen walkthrough values**, not
   documented defaults).
7. The **current-TallyPrime path** for multi-address company mailing details.
8. Whether current TallyPrime uses a literal **"Set/Alter GST Details" yes/no prompt at COMPANY level**.
9. The **exact contact-block field order** (the two primary sources disagree — and see §2.1, where one of them
   disagrees with itself).
10. **Rule 46 verbatim text came from GSTZen, not CBIC** (see §5.4 for the TLS failure that forced it).

---

## 10. Corrections log — what this write-down changed against the A14 pass

Recorded rather than silently fixed, because a claim that **was** true and is now stale is itself information.

### Line numbers corrected (drift; the claim itself stands)

| Claim | A14 pass said | Correct at `fa651ae` |
|---|---|---|
| `BuyerAddressText` (buyer Country + PIN append) | `VoucherPrintProjector.cs:740-748` | **`:739-748`** — the declaration is at `:739`; body `:740-748` |
| `CreateCompany()` | `MainWindowViewModel.cs:826-838` | **`:827-839`** — `:826` is the doc comment |
| GST screen bindable properties | `GstConfigViewModel.cs:373-382` | **`:377-386`** — `Gstin` `:377`, `HomeState` `:380`, `RegistrationType` `:383`, `Periodicity` `:386` |
| `companies` table column list | `Schema.cs:167-179` | **`:166-181`** — the table opens at `:166`, and `primary_cost_category` / `main_location`, which the A14 list included, are at `:180-181` |

### Claims now false, or too strong

1. **"GST applicable from" listed as typeable today — FALSE.** See §7.4. The domain member and column exist; no
   UI exposes it.
2. **"populating `Company.Address` alone would still print … NO State line" — too strong.** See §7.3(i).
   `InvoicePdf.cs:521-525` draws a State line whenever `StateText` is non-empty; the accurate claim is that no
   State line derived from `Company.State` can ever print.

### Sharpened by re-extraction

3. **The contact-order conflict is worse than "two sources disagree"** — the Study Guide's own worked example
   (p.268) sides with the Book against the Study Guide's own prose (p.59). §2.1.
4. **A second order conflict, unrecorded by the A14 pass:** Book p.13 puts State before Country; SG p.58-59 puts
   Country before State. §2.1.
5. **The State-inheritance line carries a rationale** the A14 pass truncated — "This helps in identifying local
   and interstate transactions according to the party's state" — plus the standing warning "In company creation
   time State must be selected right." Both strengthen §8. §4.1.
6. **The rejected Short-Key PDF's misalignment is visible in the extraction itself** (row 36 `Alt+2 → Sales
   Order` followed by two key-less orphan labels), which is stronger evidence than "asserted to be misaligned".
   §1.1.

### Not re-verified during this write-down — marked **[A14]** in place

The secondary-PDF claims (`680842180`, `703679456`, `712654832`, `719244897`), the Book p.180 double-`Ctrl+A`
accept, the Book p.435 shortcut-table negative on `Esc`, the SG ledger-PAN-mandatory contrast, the ERP-9-vintage
multi-address path, the print-side "Show company address" config, and the **Rule 46 statutory text and its
GSTZen sourcing** (the CBIC fetch was **not** retried). Each is flagged inline. Prioritisation was per the task:
the rejected Short-Key ruling, the GST-Details State-inheritance line, and the single-Address-field structure
were re-extracted first-hand and all three hold.
