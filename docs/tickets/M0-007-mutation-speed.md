# M0-007 Faster mutation job on PRs
Status: done (PR #18)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-004

## Goal
The Stryker job re-mutates all of `Equiv.Core` on every PR, even CI-only ones: the
M1-002 run tested 749 of 1015 mutants in 43 minutes, and PR #17 (no `src/` changes) paid
the same cost. On PRs, mutate only what the PR changed; keep the full sweep nightly.

## Spec references
docs/QUALITY-GATES.md (Mutation row), docs/adr/0007-testing-and-gates.md

## Deliverables
- [x] `.github/workflows/mutation.yml`: on `pull_request`, pass
      `--since:origin/<base_ref>` so only mutants in changed files (and code covered by
      changed tests) are tested; the `schedule` run stays a full sweep
- [x] `actions/checkout` with `fetch-depth: 0` so the base branch is available for the diff
- [x] `--concurrency 4` (GitHub's `ubuntu-latest` has 4 vCPUs; Stryker's default is half)
- [x] `docs/QUALITY-GATES.md` Mutation row notes PR = incremental, nightly = full

## Out of scope
Promoting the job to blocking (M2). Stryker baseline storage (`--with-baseline`), which
needs a report store. Changing mutation thresholds.

## Notes
- Verified locally: `dotnet stryker --test-runner mtp --since:origin/main` on Equiv.Core
  with a docs/CI-only diff -> 873 mutants "Removed by since filter", 0 tested, ~1 min
  (build + initial test run), exit code 0 despite "unable to calculate a mutation score".
- Stryker's `--since` default target is `master`, so the base ref is passed explicitly.
- Decision: incremental mode -> `--since:origin/<base_ref>` on PRs only. Alternatives:
  `paths:` filter on the workflow; `--with-baseline`. Rule: 4 — smallest change; a paths
  filter still re-mutates all of Core on any `src/` touch, and baseline needs a report store.
