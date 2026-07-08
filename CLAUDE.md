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

**R2 — Agentic-first; keep the MAIN window clean (protect the session token budget).** The **maximum**
amount of work is delegated to agents in **agentic workflows** (the Workflow tool + the subagents in
`agents.md`). The main window/loop does **ONLY four things**: (1) decide what's next, (2) sequence/dispatch
the work into a Workflow, (3) synthesize the agents' **structured** results, and (4) talk to the user. It must
**NOT** do the heavy lifting itself — **no** reading files end-to-end, **no** implementation, **no** running
builds/tests/large searches, **no** multi-file review — **always spawn a Workflow/agent for that**; the only
things done directly in the main loop are lightweight orchestration (a one-file/one-block peek, a quick status
command, small governance edits, verifying a gate). Prefer **structured-output** agents so the main loop
consumes conclusions, not file dumps, and keep main-loop prose **terse**. This is a hard efficiency rule:
every avoidable token spent in the main loop shortens how far the session runs — **when in doubt, delegate.**

**R3 — Every operation is done by a named agent in `agents.md`.** All agents that carry out our
individual operations are defined there. If a needed capability has no agent, ADD the agent to
`agents.md` first, then use it.

**R4 — GitHub is the GitHub Expert's exclusive domain.** ALL git and GitHub actions — repo init,
branching, commits, pushes, pull requests, tags, releases, `.gitignore`, repo settings, and CI/CD — are
performed **only** by the hired **GitHub Expert** agent. He has **full standing authority** over
`https://github.com/Shuvrajit10101/Apex-Solutions` and manages everything there **without asking
permission** (20+ years of experience). No other agent, and not the main loop, touches git/GitHub.
**Never commit the `tally/` PDFs** (they are third-party IP) — they stay git-ignored.

**R5 — `memory.md` is the living log; document EVERY change without fail — even a single line.** Every step,
task, decision, and change we make — **no matter how small, down to a one-line edit** — is written to
`memory.md` as we go, at the moment it happens (never batched-up-and-forgotten, never skipped as "too small").
Nothing is EVER left unlogged; when in doubt, log it. Each entry records: **what** changed, **why**, the
**resulting state** (PR#/commit/tests where relevant), and anything a fresh session must know. `memory.md` must
stay complete, current, and **accurate** (explicitly mark any superseded entry as such) so a brand-new session
resumes with **zero re-explanation**. Update it at the end of every task, every workflow, and every session,
and keep the **`▶▶ NEXT-SESSION START HERE`** block at the end current. Treating any change as "too small to
log" is itself a violation of this rule.

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

## New-session bootstrap (resumes from a SINGLE-LINE prompt; keep it lean)
On **ANY** new session — **even a one-line/minimal prompt** ("continue", "go", "resume", or a single line) —
the main loop resumes with **zero re-explanation**, without asking the user to restate anything:
1. Read **`memory.md`'s `▶▶ NEXT-SESSION START HERE` block** (the authoritative lean summary at the end) to
   learn the current state + the next step; glance at **`plan.md`** for the current phase; keep this file
   (`CLAUDE.md`) + **`agents.md`** in mind. That is enough to greet the user and proceed.
2. Continue `plan.md` in order from where we left off. Pull deeper detail from the `memory.md` log or `docs/`
   **only when a task needs it**, and **delegate heavy reads to a Workflow/agent** (R2).
3. **The user talks ONLY to the main loop.** Everything substantive is delegated to workflows/agents; the main
   loop just orchestrates, synthesizes, and reports back. Never make the user re-explain context these files
   already hold — reconstruct it from them.
