# ADR 0028: Turn off Copilot Autofix for code scanning; CodeQL itself stays on

Status: accepted (2026-09-23)

## Context
GitHub auto-injects a "Code scanning AI findings on PR #N" workflow on every PR that has
code scanning results — Copilot Autofix, distinct from this repo's own `codeql.yml` /
`analyze` check. It fails intermittently: PR #114's run failed in job
`github-advanced-security`, step "Processing Request (Linux)", while PR #113 and PR #115
ran clean with no repo changes in between. This repo has no GitHub Copilot seat, and
Autofix's backend needs one; the failure is a licensing/entitlement error from Autofix
itself, not a code scanning finding. `codeql.yml`'s `analyze` check, the actual required
security gate (QUALITY-GATES.md), has never failed for this reason.

## Decision
Disable Copilot Autofix at the repository level: Settings -> Advanced Security -> Code
Security -> deselect "Copilot Autofix". This is a repo-admin-only UI toggle; there is no
REST/GraphQL API for it, so it cannot be done from this PR and must be flipped by hand.
`codeql.yml` and the `analyze` check are unchanged and remain required.

## Why
- The failures are noise about Autofix's own inability to call an entitled model, not a
  security signal — nothing about the scanned code changes between a "pass" and a "fail".
- No workflow-file edit can fix this: GitHub injects the Autofix workflow itself outside
  `.github/workflows/`; the repo Settings toggle is the only control surface.
- Reversible in one click and free: turning it back on costs nothing but re-adds the
  AI-suggested-fix comments; it doesn't reopen or lose any CodeQL alert.

## Rejected
- **Disable `codeql.yml` / CodeQL scanning.** Conflates an unlicensed AI-suggestion feature
  with the actual security gate. Would drop a required check (QUALITY-GATES.md) and cut
  against the ADR 0017 rationale for staying public pre-revenue, which counts CodeQL's free
  tier as a reason not to go private.
- **Buy a Copilot seat to silence it.** Recurring cost pre-revenue for a suggestion feature
  that isn't blocking anything.

## Consequences
- PRs stop showing the "Code scanning AI findings" check. `analyze` (CodeQL) remains the
  only code-scanning check and was already the only one in the required list.
- Re-enable trigger: a Copilot seat exists for this org/repo — most likely on monetisation,
  alongside ADR 0017's other pre-revenue exemptions. Flip the same toggle back on; nothing
  else to undo. Tracked in `docs/ROADMAP.md`'s backlog list.
