# Apex Solutions — Tally Prime Clone

A faithful, offline, keyboard-first desktop clone of **Tally Prime**: double-entry accounting +
inventory + Indian statutory (GST / TDS / TCS / Payroll). The authoritative feature reference is
[`docs/tally-feature-catalog.md`](docs/tally-feature-catalog.md); the plan of record is
[`plan.md`](plan.md); the governance rules are [`CLAUDE.md`](CLAUDE.md).

> **Status:** Phase 0 (Setup, scaffold, governance). Foundations only — no functional accounting yet.
> The double-entry ledger engine lands in Phase 1.

---

## Stack

| Layer | Choice | Why (see [ADR 0001](docs/adr/0001-tech-stack.md)) |
|---|---|---|
| Language / runtime | **C# / .NET 10** (cross-platform: Windows, Linux, macOS) | one strongly-typed language end-to-end; exact `decimal` money |
| Accounting core | **`Apex.Ledger`** — framework-agnostic class library (no UI / no DB deps) | the heart of the system; exhaustively unit-tested in isolation |
| Desktop shell | **Avalonia** (XAML, MVVM) — `Apex.Desktop` | single-window, keyboard-first UI; pixel-level Tally fidelity, cross-platform |
| Persistence | **SQLite** (embedded, single-file, offline) | zero-config local store; bundles with the app |
| Tests | **xUnit** — `Apex.Ledger.Tests` | TDD (Red → Green → Refactor) |
| CI/CD | **GitHub Actions** | build + format + tests on every push/PR; green-before-merge |

Design principles: **offline-first**, **keyboard-first**, exact **paisa-level** ledger math, and
**config-driven GST slabs** (statutory law lives in versioned config, never hardcoded).

---

## Repository layout

```
.
├─ src/
│  ├─ Apex.Ledger/          # framework-agnostic double-entry core (Money, DrCr, … engine in Phase 1)
│  └─ Apex.Desktop/         # Avalonia MVVM single-window shell (Phase-0 placeholder)
├─ tests/
│  └─ Apex.Ledger.Tests/    # xUnit tests
│     └─ Fixtures/          # robert.json, bright.json — deterministic study fixtures (DATA only)
├─ docs/
│  ├─ adr/0001-tech-stack.md          # architecture decision record (stack)
│  ├─ srs/SRS-0-skeleton.md           # SRS skeleton (filled per phase)
│  └─ tally-feature-catalog*.md       # what Tally Prime does (+ verification report)
├─ Apex.slnx                # solution (new .NET XML solution format)
├─ plan.md                  # the master plan — execute this
├─ CLAUDE.md                # governance rules
├─ agents.md                # agent roster
├─ memory.md                # running log
└─ tally/                   # third-party study PDFs — GIT-IGNORED, never committed
```

---

## Prerequisites — .NET SDK on the PATH (important)

The **.NET 10 SDK (10.0.301)** is installed under **`~/.dotnet`** and is **not** on the default `PATH`.
Every `dotnet` command in this environment must first put it on the `PATH`:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet --version   # must print 10.0.301
```

(Consider adding that `export` line to your shell profile so it's automatic.)

---

## Build / Test / Run

Run from the project root (the path contains a space and parentheses — quote it if you `cd`):

```bash
export PATH="$HOME/.dotnet:$PATH"

# Build the whole solution (Apex.Ledger + Apex.Ledger.Tests + Apex.Desktop)
dotnet build

# Run the test suite (xUnit)
dotnet test

# Run the desktop shell (Avalonia)
dotnet run --project src/Apex.Desktop
```

**Expected today (Phase 0):** build succeeds with 0 warnings; `dotnet test` reports
`Passed: 2, Skipped: 2, Failed: 0` — the two skipped tests are the Robert/Bright fixture loaders
(`Skip = "ledger engine arrives in Phase 1"`).

### The two study fixtures

`tests/Apex.Ledger.Tests/Fixtures/robert.json` and `bright.json` capture the two deterministic
regression baselines from the study (R8), as **data only**:

- **Robert** — transport, accounts-only, 13 deterministic vouchers; opening capital ₹100 000
  (Cash ₹70 000 + SBI Bank ₹30 000). Trial Balance balances; expected P&L and Balance Sheet totals
  are recorded in the file's `expected` block.
- **Bright** — trading; opening balances + 10 % machinery depreciation + closing stock ₹15 000;
  exercises inventory-integrated valuation (re-verified in Phase 3).

Both fixtures are internally consistent (Dr = Cr on every voucher; cash/bank never negative; statements
reconcile). The skipped loader tests track them and document intent; **real engine assertions arrive in
Phase 1**, at which point the `Skip` is removed and the totals are asserted to the paisa (NFR-3).

---

## Documentation & governance

- [`plan.md`](plan.md) — the master plan (single source of truth; execute in order, phase-gated).
- [`CLAUDE.md`](CLAUDE.md) — strict rules (agentic-first, `/software` lifecycle, GitHub Expert owns git,
  `tally/` never committed, TDD + verification, phase gates).
- [`docs/adr/0001-tech-stack.md`](docs/adr/0001-tech-stack.md) — the stack decision (context/decision/consequences).
- [`docs/srs/SRS-0-skeleton.md`](docs/srs/SRS-0-skeleton.md) — SRS skeleton, filled per phase.
- [`agents.md`](agents.md) — the agent roster.

> **Note on `tally/`:** the folder holds third-party Tally Prime study PDFs (copyrighted). It is
> **git-ignored** and must never be committed (CLAUDE.md R4).
