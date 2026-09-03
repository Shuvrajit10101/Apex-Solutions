# WF-1 owed review — the 34 findings, per lens, with severities

**Why this file exists.** The review that WF-1 owed was paid on 2026-08-16 (three sequential adversarial
lenses, fixed forward on top of `e49b88e`, landed as `31c476b`). Its finding count was then quoted three
times and **counted zero times**. ⚠️ The first draft of this sentence said "quoted three times" and then
enumerated two — an uncounted number, in the one file whose whole thesis is that figures must carry the count
that produced them. A12 caught it on review. **Enumerated: FOUR sites carried an uncounted MAJOR/MINOR split, and THREE of them
carried the same wrong one.** `plan.md`, `memory.md` and — permanently, because the branch is pushed and no
amend is permitted — the commit body of `31c476b` all read "14 MAJOR, 19 MINOR"; the fix-agent brief carried a
*different* uncounted figure, "15 MAJOR". The true split is **18 MAJOR, 14 MINOR**.

⚠️ **AND THE CHAIN RAN ONE ROUND FURTHER, WHICH IS THE POINT OF THIS PARAGRAPH.** The commit that added the
pointers asserted a `grep` result — that it "now returns `plan.md`, `memory.md` and the file itself" — without
running it. It returns **two** files: a content grep lists files whose CONTENT matches, and this file does not
contain its own name. The pointer defect was real and is closed (0 → 2 pointers); only the arithmetic
describing it was assumed. That claim survives, uncorrectable, in the body of `750d27a`.
**⇒ Six times in one session a plausible figure propagated because it LOOKED measured, and the last two
occurred inside the remedy for the first four. The defence is not care — it is putting the figure next to a
derivation something can re-run. That is what this file is for; it is not a claim that the habit is cured.** The correction landed in `4cf5501` — but A12 observed
that the corrected figure was *still* only a quoted number, because the lens records lived in agent output
and no tracked file in this repository could re-derive it. **That is the same failure mode one level out.**
This table is the derivation. Re-count it rather than quoting the header.

**Totals: 34 findings = 1 BLOCKER + 18 MAJOR + 14 MINOR + 1 carrying no severity** (lens 3 F16, a correction
to the review brief's own premise rather than a defect in the code). Severities therefore sum to 33 and
findings to 34; that gap is the reason the earlier arithmetic looked plausible and was wrong.

**Citation policy for this file.** Findings are summarised WITHOUT `file:line` pointers. The lenses' own
citations were taken against `e49b88e`, before the fix pass moved lines; reproducing them here would either
fail the citation invariant in `DocumentCodeAgreementTests` or push a dozen knowingly-stale entries onto the
historical allow-list, weakening a check this very review pass strengthened. The live pointers live in
`plan.md`'s S4 row and in `memory.md`'s entry for this pass, both of which are re-anchored on every edit.

---

## Lens 1 — migration and data safety (5 findings: 2 MAJOR, 3 MINOR)

| # | Severity | Finding |
|---|---|---|
| 1 | **MAJOR** | The `StockItemFirst` back-fill is silently destroyed on the first ordinary save of any migrated book that did not already have GST enabled. The two source-order columns are `NOT NULL` on `companies` but live on `GstConfig`, which the store builds only when GST is enabled; the delete-and-reinsert then fabricates `LedgerFirst` over the migration's `UPDATE`. Measured `1|1` → one save → `0|0`, reachable from ~40 screens. **Fixed.** |
| 2 | **MAJOR** | `SchemaDowngrade.V51ToV50` is not the true inverse it is documented as: a populated round trip permanently loses two indexes and 81 column contracts (primary key, NOT NULLs, defaults, declared BLOB types). `integrity_check` still reports ok; `foreign_key_check` throws; the store cannot save the result. Indexes **fixed**; the contract loss is **recorded, pinned, not fixed**. |
| 3 | MINOR | The "byte for byte" survival test renders every cell with `ToString()`, so a BLOB reads as the literal string `System.Byte[]` — changing an encrypted NIC credential leaves the snapshot identical. It also sorts rows, so row order is not compared. **Fixed.** |
| 4 | MINOR | No idempotency guard: a `schema_version` that disagrees with the actual columns leaves the book permanently unopenable with a raw SQLite error. Proved NOT reachable from a crash — the forward migration is correctly transactional — so a robustness gap, not a live hole. The downgrade path is not transactional and can produce the state. |
| 5 | MINOR | The "no GST block" marker is one column but the block is four; three values are silently discarded when the marker is null while remaining in the row. Latent — unreachable through the store or the importer. |

## Lens 2 — test quality and mutation (12 findings: 5 MAJOR, 7 MINOR)

**No doctored test was found in WF-1.** All 17 new test bodies were read against their names and doc comments;
none asserts the inverse of its own name. What was found instead is a cluster of tests that cannot fail.

| # | Severity | Finding |
|---|---|---|
| 1 | **MAJOR** | The fresh-vs-upgraded split's own guard is blind. Performing exactly the simplification its doc comment forbids left both named guardian tests green; the only red was a hardcoded string literal in a third test. No production insert ever falls through to the column default, so no behavioural test can observe it. **Fixed.** |
| 2 | **MAJOR** | Taxability is never exported at a non-default value. A mapper hardcoding `Taxable` for all three levels left both the Io and Sqlite suites completely green — the exact Io-bypass class that test file exists to catch. **Fixed.** |
| 3 | **MAJOR** | The company-level default block's taxability and supply type are unpinned in SQLite for the same reason: both fixtures use the enum zero. The taxability column is also the null-marker for the whole block. **Fixed.** |
| 4 | **MAJOR** | `MasterGstDetails.EnsureValid` is reachable on exactly one of five write paths — the canonical import. Nine values it exists to reject were saved and reloaded byte-for-byte through SQLite, and the resulting book cannot be re-imported: export parses with zero errors, then the importer refuses it. **Recorded, not closed.** |
| 5 | **MAJOR** | The master-vs-item parity test asserts the implementation against itself: its only failure mode is disagreement, never the rule. Relaxing the same rule in both validators left it green, including the two rows that exist for that rule. It also emerged that the item block's own guard has no test in the project that owns it. **Fixed.** |
| 6 | MINOR | The XML reader defaults a missing taxability; the JSON reader hard-fails on it as a required property. The same logical document is accepted by one reader and rejected by the other. No test covers either. |
| 7 | MINOR | Basis points round-trip losslessly, but there is no upper bound anywhere — 10 000 % and `int.MaxValue` both validate, persist and reload. Sub-basis-point rates are unrepresentable rather than silently wrong. **Recorded, not closed.** |
| 8 | MINOR | The HSN/SAC "4/6/8 digits" rule is enforced in exactly one place and it is not the schema; there is no CHECK constraint. An empty string is a stable third state alongside null and a real code. |
| 9 | MINOR | The ~50 "version-bumped" schema tests assert nothing about v51 — their version assertions are version-agnostic and pass at any `CurrentVersion`. The real v51 coverage is one file. |
| 10 | MINOR | The company-default validation call has zero coverage in the project that owns it; its only proof of life is in a different project, behind an import. |
| 11 | MINOR | The all-or-nothing rejection test exercises one of the three validation rules; the two rate rules have no Io-bypass coverage at all, which is the one thing that file exists to provide. |
| 12 | MINOR | The "a company that never uses the hierarchy is unchanged" test uses a company with no GST config, so the claim is trivially true; a GST-enabled company that never touches the hierarchy is *not* unchanged. A sibling assertion also became ambiguous once the element name was reused at three levels. |

## Lens 3 — Tally fidelity, scope and record (17 findings: 1 BLOCKER, 11 MAJOR, 4 MINOR, 1 unclassified)

| # | Severity | Finding |
|---|---|---|
| 1 | **MAJOR** | The R7 grounding presents a corpus quote that does not contain the level the model actually added. The corpus lists five **methods**, one of which is GST Classification (deliberately excluded here) and none of which is the accounting Group (which we added). The register already said so; the code never did. **Sourcing fixed, feature kept.** |
| 2 | **MAJOR** | The field-set citation points at the Stock **Item** screen. The one corpus page that shows a Stock **Group** GST sub-screen shows **two** fields, not four, and was cited nowhere in the slice. **Fixed.** |
| 3 | **MAJOR** | Two files label the stock group "level 2 of the five-level hierarchy". That is the corpus's method-list ordinal, not a resolution position — under the shipped default order it is position 4. A sibling file states it correctly. **Fixed.** |
| 4 | **MAJOR** | `plan.md` and `memory.md` both assert the corpus enumerates five hierarchy levels and shows the Stock-Group field shape. The register says the opposite and is right. This is the upstream origin of findings 1 and 2. **Fixed.** |
| 5 | **MAJOR** | The two-independent-source-order claim is asserted in three shipped files with no citation; the one code-side pointer names a register row that does not carry it. The corpus is silent, and the A14 confirmation that bullet requires never ran, because the design agent died. **Marked web-sourced and unverified.** |
| 6 | **BLOCKER** | `memory.md` had no entry for either of the two preceding commits — a living log missing two commits and a schema bump, on the only slice this phase carrying a migration. A new session would have believed the schema was still v50. **Fixed.** |
| 7 | **MAJOR** | `memory.md` stated that v51 was still unconsumed. It was consumed. Two pointers alongside it were also dead. **Fixed.** |
| 8 | **MAJOR** | The W0-2 grounding document still described WF-1 as uncommitted at three sites — in a file the same commit had rewritten, and the file the next author is sent to read. **Fixed.** |
| 9 | **MAJOR** | The invented-vs-cloned register states the opposite of the code in five places, in the direction that **understates** what shipped — a future session reading it alone would rebuild masters that already exist. **Fixed.** |
| 10 | MINOR | Four shipped comments carry the dead workflow's slice number, which points at an unrelated slice in the plan. **Fixed.** |
| 11 | MINOR | The new citation-content test has zero WF-1 anchors: the tool built because reach-only checks have a blind spot was pointed exclusively at the reviewed half. **Fixed.** |
| 12 | MINOR | One test file rebuilds two tables via drop-and-rename to manufacture a pre-v51 shape, silently dropping the same two indexes as finding 2 — a second location for the same root cause. **Fixed.** |
| 13 | MINOR | A bullet headed "read before using the register" still said the next free schema version was v51, after v51 was spent. **Fixed.** |
| 14 | **MAJOR** | **v54 was double-allocated** — promised to two different rows by sentences that never referenced each other, one of them added by the very commit under review. Resolved structurally: the allocation now ends at v53 and nothing is reserved beyond it; whoever needs v54 takes it and must amend the allocation line in the same commit. |
| 15 | **MAJOR** | A fourth colliding "v50 → v51" claim survived on a number already spent, missed by the allocation that was written to replace three such claims. **Fixed.** |
| 16 | *(none)* | The review brief's own premise was wrong: the resolver that will read these fields is the unshipped second half of WF-1, not the next slice. All carry-forwards were re-addressed to a WF-1 continuation. |
| 17 | **MAJOR** | A binding decision promised the resolver author that the back-fill "provably changes zero currently-resolvable figures" — which lens 1 had already disproved for non-GST books. Amended to its real scope, with the two validation gaps added as named carry-forwards. |

---

## What this review did NOT close

Recorded as known-limit tests that assert the limit rather than the fix:

- the GST-off → GST-on transition still loses the back-fill (closing it moves fields off the GST config onto
  the company, a canonical-format change and a design call);
- `MasterGstDetails.EnsureValid` remains reachable on one of five write paths;
- there is no upper bound on the basis-point rate and no CHECK constraint on the HSN rule;
- the downgrade path still loses primary keys, NOT NULLs and defaults, and a downgraded book **cannot be
  saved at all** — a fact neither lens found, surfaced by the fix pass itself;
- WF-1 has no **design** gate. The review debt is paid; the design gate is not retroactively granted.
