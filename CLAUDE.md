# CLAUDE.md — Apex Solutions (Tally Prime Clone)

> Read this file, then **memory.md** (full history), then **plan.md** (current phase) at the start of
> every session. These four files — `CLAUDE.md`, `agents.md`, `memory.md`, `plan.md` — govern the
> project. Do not deviate from the rules below.

## What we are building
A faithful clone of **Tally Prime** with the same features. The authoritative description of those
features is **`docs/tally-feature-catalog.md`** (with sourced corrections in
`docs/tally-feature-catalog-verification-report.md`). The primary source corpus is the **git-ignored
PDFs in `tally/`**. The code repository is **https://github.com/Shuvrajit10101/Apex-Solutions**.

---

## STRICT RULES (do not break)

**R1 — `/software` governs everything.** Every action — planning, requirements, design, implementation,
testing, release, documentation, maintenance — is carried out using the **`/software` skill** and its
lifecycle/frameworks/reference files. Invoke it and follow it for each task.

**R2 — Agentic-first; keep the MAIN window clean.** The maximum amount of work is delegated to agents
running in **agentic workflows** (the Workflow tool + the subagents defined in `agents.md`). The main
window/loop ONLY: decides, sequences work, synthesizes agent results, and talks to the user. No heavy
reading, implementation, or review happens in the main loop. Prefer structured-output agents so the
main loop consumes conclusions, not file dumps.

**R3 — Every operation is done by a named agent in `agents.md`.** All agents that carry out our
individual operations are defined there. If a needed capability has no agent, ADD the agent to
`agents.md` first, then use it.

**R4 — GitHub is the GitHub Expert's exclusive domain.** ALL git and GitHub actions — repo init,
branching, commits, pushes, pull requests, tags, releases, `.gitignore`, repo settings, and CI/CD — are
performed **only** by the hired **GitHub Expert** agent. He has **full standing authority** over
`https://github.com/Shuvrajit10101/Apex-Solutions` and manages everything there **without asking
permission** (20+ years of experience). No other agent, and not the main loop, touches git/GitHub.
**Never commit the `tally/` PDFs** (they are third-party IP) — they stay git-ignored.

**R5 — `memory.md` is the living log; document EVERY step without fail.** Every step we take and every
task we perform is written to `memory.md` as we go. It must be complete enough that a brand-new session
can resume smoothly with **zero re-explanation**. Update it at the end of every task and every session.

**R6 — `plan.md` is the single source of truth.** `plan.md` (built from the study via `/software`) is
the ONE plan we execute — thoroughly and in order. Minor deviations are permitted in some cases but must
be recorded in `memory.md` with the reason. No work is done outside `plan.md` without first updating
`plan.md`.

---

## ADDITIONAL RULES (engineering discipline — added by Claude)

**R7 — Fidelity to Tally.** Every feature is grounded in `docs/tally-feature-catalog.md` and the
`tally/` PDFs, cited. The Tally Domain/Corpus Expert agent resolves any fidelity doubt against the
corpus; edition/law facts are web-verified against official sources, never asserted from memory.

**R8 — Test-driven & verified.** Every feature ships with tests (unit + integration). Nothing is
declared "done" without running it and showing evidence. The two deterministic fixtures from the study —
**"Robert"** (transport, accounts-only) and **"Bright"** (trading) — are baseline regression tests for
the ledger engine.

**R9 — Phase gates.** Work proceeds phase-by-phase per `plan.md`. Each phase ends with: tests green
(shown) → review pass → GitHub Expert commits & pushes → run the real app → `memory.md` updated → then
the **user's go-ahead** for the next phase.

**R10 — Small, reviewed commits.** The GitHub Expert commits in small, coherent, conventional-commit
units, each tied to a `plan.md` item; every substantial change gets a Code Reviewer pass before merge.

**R11 — Definition of Done (per feature):** behavior + navigation + shortcuts match the catalog; tests
written and green; reviewed; docs/user-notes updated; committed & pushed by the GitHub Expert;
`memory.md` updated.

**R12 — Decisions go to the user at gates.** Architecture, scope, or stack changes and phase transitions
are surfaced to the user for go/no-go. We never silently expand scope.

**R13 — Secrets & safety.** No secrets in the repo; credentials via environment/secret store; the GitHub
Expert manages repo authentication.

**R14 — Token discipline.** Keep main-loop output lean; push detail into agents and into
`memory.md`/`plan.md`.

---

## Key files
| File | Role |
|---|---|
| `plan.md` | The master plan — **execute this**. |
| `agents.md` | The agent roster — who does what, with which tools. |
| `memory.md` | The running log — **read first** in a new session. |
| `docs/tally-feature-catalog.md` | What Tally Prime does — the requirements reference. |
| `docs/tally-feature-catalog-verification-report.md` | Sourced corrections & enrichments. |
| `tally/` | Source PDFs — **never commit**. |

## New-session bootstrap
1. Read `memory.md` (full history) → `plan.md` (current phase) → this file.
2. Resume from the last `memory.md` entry, continuing `plan.md` in order.
