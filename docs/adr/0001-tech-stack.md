# ADR 0001 — Technology Stack

- **Status:** Accepted
- **Date:** 2026-07-02
- **Phase:** 0 (Setup, scaffold, governance)
- **Deciders:** Software Architect, Build Engineer; ratified by the user (stack approved 2026-07-02, `plan.md` §3).
- **Related:** `plan.md` §3 (confirmed stack), CLAUDE.md R7 (fidelity), NFR-1/NFR-5/NFR-6 (offline, portability, maintainability).

---

## Context

Apex Solutions is a **faithful clone of Tally Prime**: an offline, keyboard-first, single-window,
double-entry accounting + inventory + Indian statutory (GST/TDS/TCS/Payroll) desktop application
(`plan.md` §1). The stack must satisfy the non-functional constraints that shape the whole build:

- **NFR-1 Offline-first** — every core function works with no network; data lives in local files.
- **NFR-2 Keyboard-first** — every catalogued action is reachable by its documented shortcut in a single
  window (Gateway of Tally hub, Alt+G Go To, Ctrl+G Switch To). This demands a UI toolkit with precise,
  low-latency keyboard focus control and custom key routing.
- **NFR-3 Correctness/fidelity** — ledger math is exact; the Robert & Bright fixtures must reproduce known
  totals to the paisa. This argues for a strongly-typed language and a UI-independent core that can be
  exhaustively unit-tested in isolation.
- **NFR-4 Performance** — a year of vouchers renders a Trial Balance / Day Book in < 1 s; voucher save is
  perceptibly instant.
- **NFR-5 Portability** — Windows is the primary target; the domain core must be portable to Linux/macOS.
- **NFR-6 Maintainability** — the accounting core is a standalone, UI-independent library with a high test
  coverage threshold on posting/valuation logic.
- **NFR-7 Security** — company data can be password-encrypted (TallyVault-style); audit trail (Edit Log).
- **Fidelity (R7)** — pixel-level reproduction of Tally Prime's dense, grid-based, character-cell-styled UI
  (the classic blue chrome, right-hand button panel, bottom bars) is a first-class requirement.
- **Config-driven statutory law** — GST slabs / rates and other statutory thresholds must be **data/config
  driven**, never hardcoded, so law changes are a config edit, not a code change.

We evaluated three realistic stacks:

1. **C# / .NET 10 + Avalonia + SQLite** — one strongly-typed language end to end; a mature cross-platform
   XAML UI (Avalonia) with full control over keyboard focus, custom controls, and pixel styling; a
   framework-agnostic class library for the ledger core; embedded SQLite for local, offline, single-file
   persistence; xUnit for tests; GitHub Actions for CI.
2. **Rust + Tauri (web front end) + SQLite** — excellent performance and a small binary, but the UI is
   HTML/CSS/JS in a webview, making the dense character-grid keyboard UX and precise focus model harder to
   reproduce faithfully, and it splits the codebase across two languages.
3. **Electron/TypeScript + SQLite** — fast UI iteration and a huge ecosystem, but heavier runtime, weaker
   numeric/decimal story for exact accounting math, and again a webview keyboard model that fights the
   Tally single-window, no-mouse feel.

## Decision

We adopt **C# / .NET 10 + Avalonia + SQLite**, structured as follows:

- **Language / runtime:** **C# on .NET 10** (LTS-track), cross-platform (Windows, Linux, macOS).
- **Accounting core:** a **framework-agnostic class library, `Apex.Ledger`** (`net10.0`, no UI or DB
  dependency) — the double-entry posting engine + report projections. This is the heart of the system and
  is unit-tested in isolation (NFR-6); the Robert & Bright fixtures are its regression baseline (R8).
- **Desktop shell:** **Avalonia** (XAML, MVVM) in `Apex.Desktop` — a single-window, keyboard-first UI that
  reproduces Tally Prime's chrome pixel-for-pixel. Avalonia gives us native cross-platform rendering,
  custom-drawn controls, and full keyboard-focus / input-routing control that a webview cannot match.
- **Persistence:** **SQLite** (embedded, single-file, offline) reached through a thin repository layer so
  the core stays storage-agnostic; a **schema-version** number is tracked separately for migrations
  (`plan.md` §7).
- **Money / numbers:** exact `decimal`-based value types (`Money`, `DrCr`) in the core — never binary
  floating point — so statements reconcile to the paisa (NFR-3).
- **Statutory config:** GST slabs, rates, and thresholds live in **versioned config/data**, resolved at
  runtime, never hardcoded.
- **Tests:** **xUnit** (`Apex.Ledger.Tests`), TDD (Red → Green → Refactor).
- **CI/CD:** **GitHub Actions** — build + format + full test suite on every push/PR; branch protection
  requires green before merge; tagged builds package the desktop installer (GitHub Expert's domain, R4).

## Consequences

**Positive**
- One strongly-typed language across core, UI, and tests — less context-switching, shared value types.
- `decimal` value types give exact accounting math, directly serving NFR-3 and the paisa-level fixtures.
- A UI-independent `Apex.Ledger` library is exhaustively testable and portable, serving NFR-5/NFR-6; if the
  UI shell or DB ever change, the engine is untouched.
- Avalonia's custom control model and precise keyboard/focus handling make the dense, no-mouse, single-window
  Tally UX and pixel-level fidelity achievable (NFR-2, R7) on all three OSes from one codebase.
- SQLite is a zero-configuration, single-file, fully-offline store (NFR-1) that bundles with the app.
- xUnit + GitHub Actions are the mainstream, well-supported .NET test + CI toolchain.

**Negative / risks & mitigations**
- Avalonia is less ubiquitous than WPF/WinForms; smaller (but active) community. *Mitigation:* the UI is
  cleanly separated from the core, so a shell swap never risks the accounting engine.
- Pixel-level Tally fidelity in a modern toolkit is real effort (custom controls, character-grid metrics).
  *Mitigation:* fidelity is grounded in `docs/tally-feature-catalog.md` + the `tally/` PDFs (R7) and built
  incrementally, phase-gated.
- .NET 10 is recent; occasional tooling churn. *Mitigation:* pin the SDK (`~/.dotnet`, see README) and let
  CI run on a clean, pinned runner.
- Cross-platform desktop packaging/signing differs per OS. *Mitigation:* Windows is the primary release
  target; Linux/macOS builds are validated in CI but packaged as needed.

**Neutral**
- The desktop shell (`Apex.Desktop`) is intentionally kept out of the CI critical path for the ledger core:
  if the Avalonia template/build is unavailable on a runner, the **core + tests still build green** and the
  shell is added when its toolchain is present. This keeps CI honest and unblocked (Phase 0 scaffold rule).
