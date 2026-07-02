# Contributing to Apex Solutions

Thanks for helping build Apex Solutions. This project is developed in
well-defined phases with clearly assigned roles, and all contributions must
follow the workflow below.

## Ground rules

- **Follow the phases in [`plan.md`](plan.md).** Every unit of work maps to a
  `plan.md` item. If your change is not covered by the plan, raise it first.
- **Respect the roles in [`agents.md`](agents.md).** Each role has a defined
  scope of ownership; stay within it and hand off through the documented gates.
- **Obey [`CLAUDE.md`](CLAUDE.md).** Its rules are binding and override default
  behavior.
- **NEVER commit the `tally/` PDFs.** The reference PDFs under `tally/` are
  git-ignored on purpose. They are proprietary and must never be committed,
  pushed, or otherwise added to version control.

## Workflow

All work flows through pull requests. Direct pushes to `main` are blocked by
branch protection.

1. **Branch.** Create a feature branch off the latest `main`
   (e.g. `feat/phase-7-gst`, `fix/inventory-as-of-date`).
2. **Implement.** Make focused commits with clear, conventional messages
   (`feat(...)`, `fix(...)`, `chore(...)`, `docs(...)`).
3. **Open a pull request.** Fill in the PR template: link the `plan.md` item,
   describe how you tested, and complete the checklist.
4. **Green CI.** All CI status checks must pass. PRs cannot merge until CI is
   green and the branch is up to date with `main`.
5. **Review & merge.** The code owner reviews (see `.github/CODEOWNERS`), then
   the PR is merged.

## Before you open a PR

- Tests pass locally.
- No `tally/` PDFs (or any other ignored/proprietary asset) are staged.
- `memory.md` is updated if your change affects project state or handoff notes.
- The change stays within the current phase's scope.
