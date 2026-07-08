# ▶▶ NEXT-SESSION KICKOFF — Apex Solutions

**Paste the "COPY-PASTE PROMPT" below as your first message in the new session.** This file is the self-contained resume point. (Written 2026-07-05, after Phase 4 completed.)

---

## COPY-PASTE PROMPT (paste this verbatim to start the next session)

> Continue Apex Solutions where we left off. **First read the governance files in order:** repo-root `CLAUDE.md` → `memory.md` (the living log — read the tail for current state) → `plan.md` → `agents.md`, plus the phase requirements docs in `docs/` (`phase3-inventory-requirements.md`, `phase4-gst-requirements.md`, `phase5-*-requirements.md`) and this `docs/NEXT_SESSION_KICKOFF.md`. Do NOT re-do finished work.
>
> **This is the .NET / Avalonia (C#) desktop Tally-Prime-clone accounting app** at `C:\Users\dkpho\OneDrive\Desktop\Apex Solutions(end)`. Working branch `claude/interesting-mirzakhani-30e51e` @ `8812d72`, **SQLite schema v13, 570 tests green**, de-branded. ✅ **Phases 3 (Inventory) + 4 (GST core) are COMPLETE**, committed & pushed (no PR yet). **Resume at Phase 5** (reports depth + printing / export / import / email) per `plan.md`; a `docs/phase5-*-requirements.md` may already be authored — read it and adopt/confirm its decision points.
>
> **Operating model (per CLAUDE.md — R1/R2: do the MAXIMUM amount of work through agentic workflows + subagents; the main loop ONLY decides, sequences, synthesizes, and talks — no heavy reading/impl/review inline):** run each slice **gated + adversarially verified** — CA/A14 author a requirements doc with a sign-off checklist up front → engine TDD → cascade Miller-column UI → **A10 adversarial review** (bounded, reproduce every suspected bug with a throwaway test) → fix → **full gate green** (`export PATH="$HOME/.dotnet:$PATH" && dotnet build -c Release && dotnet test -c Release`, ALL green — re-run it YOURSELF, don't trust an integrator's "green") → **GitHub Expert (A12) alone** commits + pushes the slice (plain git, GCM auth; commit trailer `Co-Authored-By: Claude Opus 4.8`) → append a `memory.md` slice log. Verify UI slices by a **headless Skia render** (flip `tests/Apex.Desktop.Tests/TestAppBuilder.cs` to `UseSkia()` + `UseHeadlessDrawing=false` in a throwaway `[AvaloniaFact]`, `CaptureRenderedFrame(...).Save(png)`, then revert) and Read the PNG. **Web-verify any tax/law facts** (R7 — never from memory). **Kill any running `Apex.Desktop.exe` before building** (a running exe locks the build output). The shipped app/code must NEVER contain the word "Tally". **Checkpoint after every slice; if a usage-limit signal appears, STOP at a clean committed checkpoint.**
>
> **Then run this loop:** `/loop complete all the phases till they are perfect, and carry out /loop for all the phases` — i.e., self-pace via the loop and drive **Phase 5 and every remaining plan.md phase (6–11)** to a perfect, gated, adversarially-verified finish.

---

## Fast facts for the resuming session

- **Repo:** https://github.com/Shuvrajit10101/Apex-Solutions · branch `claude/interesting-mirzakhani-30e51e` (all phase work accumulates here; open the PR to `main` when the user asks). Latest commit `8812d72`.
- **Build/test/run** (dotnet on PATH via `$HOME/.dotnet`):
  - Build: `export PATH="$HOME/.dotnet:$PATH" && dotnet build -c Release`
  - Test (the gate): `dotnet test -c Release` — currently **570 green** (Apex.Ledger 339 · Apex.Persistence.Sqlite 46 · Apex.Desktop 185)
  - Run app: `dotnet run --project "src/Apex.Desktop" -c Release` (or launch `src/Apex.Desktop/bin/Release/net10.0/Apex.Desktop.exe`)
- **Done so far:** Phases 0–2 (accounting core, bill-wise, banking, cost centres, budgets, scenarios, interest, multi-currency) shipped earlier; **Phase 3 (Inventory)** = masters + order/stock vouchers + valuation engine + item-invoice mode + full inventory reports + the **Bright re-verification gate**; **Phase 4 (GST core)** = F11 config + CGST/SGST/IGST tax ledgers + party/item GST masters + **GST 2.0 slabs 0/5/18/40** (user-approved) + intra/inter split (compute-total-then-split, CGST+SGST==IGST to the paisa) + GSTR-1/GSTR-3B/Tax-Analysis (engine + h-scroll UI) + item-invoice GST integration.
- **Deferred to Phase 9 (GST advanced), per approved DPs:** RCM, composition, cess, e-invoice/e-way, GSTR-2A/2B reconciliation, Rule-88A ITC set-off + Alt+J stat-adjustment / Ctrl+F stat-payment posting.
- **Remaining phases (plan.md):** 5 reports/print/export/import/email · 6 advanced inventory (BOM, batches-deep, POS, job-work, period reorder) · 7 TDS/TCS · 8 payroll · 9 GST advanced · 10 security/audit/data-mgmt (TallyVault→"vault", Edit Log, backup/restore, group-company consolidation) · 11 hardening + v1.0.0 release.
- **Hard-won lessons:** a green gate hides real bugs — adversarial review has caught ~24 real defects this run (several critical: same-date physical-count no-negative-stock bypass, zero-rate item-invoice phantom-profit, CGST+SGST≠IGST parity). Reproduce a suspected bug with a throwaway test first; eyeball staged files before every commit (scratch `_Capture*`/`_Probe*` files sneak in — delete them); agent file-edits survive an API/limit death (recover via `git status` + resume the agent by `SendMessage`); the bottom status bar shows the live DB schema version (v13 now).
