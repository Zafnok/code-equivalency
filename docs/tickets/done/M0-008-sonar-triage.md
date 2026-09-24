# M0-008 SonarQube debt triage into batched GitHub issues
Status: done (PR #32)
Effort: L
Model: Opus, medium effort. Sonnet high is acceptable for the fix PRs this ticket generates, but not for this ticket.
Depends on: M0-006

## Goal
SonarCloud analyses this repo on every PR and every push to `main`, but its quality gate
grades only *new* code and nothing ever looks at the overall backlog. There are 211 open
issues and no way to work them. This ticket adds `tools/sonar-triage/sonar-triage.ps1`,
which reads the SonarCloud API, drops findings listed in a checked-in policy register,
groups the rest into coherent batches, and syncs each batch to a GitHub issue that a fresh
session can fix without rediscovering context. Plus the two skills that drive it, a weekly
workflow, and ADR 0016. No production code changes; nothing enters `Equiv.slnx`.

## Spec references
docs/adr/0009-sonarqube-cloud-changegate.md; docs/adr/0016-sonar-issue-triage.md;
docs/QUALITY-GATES.md (SonarQube row)

## Acceptance criteria (all must hold; nothing beyond them)
1. `pwsh ./tools/sonar-triage/sonar-triage.ps1` with no arguments performs no writes and
   prints a summary line of the form `fetched N / suppressed N / batches N`, followed by
   one line per batch. `-Apply` is required for any GitHub write.
2. Issues are batched by the hybrid rule: a rule appearing in `-RuleBatchMinFiles` (default
   3) or more distinct files becomes one rule-wide batch; remaining findings group per file;
   files left holding fewer than `-FileBatchMin` (default 2) findings roll up into one
   long-tail batch per top-level area (`src`, `tests`, `tools`).
3. `tools/sonar-triage/policy.jsonc` entries with `verdict: accept` are excluded from every
   batch and counted in the `suppressed` total. An entry matches on `rule`, optionally
   narrowed by `paths` (glob) and `message` (regex). An `accept` entry without a non-empty
   `reason` is a hard error that stops the run.
4. Every rendered issue body begins with `<!-- sonar-triage:v1 key=<batch key> -->` and
   contains: the rule name and a SonarCloud rule link, a findings table of
   `file:line | message | permalink`, and numbered acceptance criteria.
5. `-Apply` is idempotent: a second consecutive run creates 0 issues and edits 0 bodies.
   A batch whose findings have all disappeared is closed with an explanatory comment.
6. `.github/workflows/sonar-triage.yml` runs the script on a weekly schedule and on
   `workflow_dispatch`, with `permissions: contents: read, issues: write`, and pins every
   action by commit SHA. The scheduled run applies; a manual run defaults to a dry run and
   applies only when `dry_run` is unticked.
7. `./build.ps1 -Integration` is green and its output is unchanged by this ticket: no file
   added here is compiled, covered, or analysed.

## Files
- `tools/sonar-triage/sonar-triage.ps1`
- `tools/sonar-triage/policy.jsonc`
- `tools/sonar-triage/README.md`
- `.claude/skills/equiv-sonar-triage/SKILL.md`
- `.claude/skills/equiv-sonar-fix/SKILL.md`
- `.github/workflows/sonar-triage.yml`
- `docs/adr/0016-sonar-issue-triage.md`, `docs/adr/README.md`
- `docs/QUALITY-GATES.md`, `docs/ROADMAP.md`, `docs/tickets/README.md`, `CLAUDE.md`

## Tests
No automated tests: this is a repo script, not a `src/` assembly, and follows `build.ps1`'s
precedent of being exercised by running it. Verified by hand, recorded in Notes:
1. Dry run against live SonarCloud reproduces the batch table in the ADR.
2. Flipping one rule to `accept` removes exactly that batch and raises `suppressed`.
3. `-Apply` twice in a row reports 0 created / 0 edited on the second run.

## Size guard
Nine new files plus five one-line doc edits. A tenth new file, or any edit under `src/`
or `tests/`, means the ticket has been misread.

## Out of scope
- Fixing any Sonar finding. This ticket files them; the fix PRs are separate.
- Promoting `sonar` to a required check, or removing `continue-on-error` from `sonar.yml`.
- Any change to `Equiv.slnx`, `Directory.Packages.props`, or `.editorconfig` severities.
  (`.editorconfig` pinning belongs to each fix PR, per `equiv-sonar-fix`.)
- A C# tool under `tools/` with its own test project.

## Notes

- Decision: ticket id -> `M0-008`, not the `P2-001` the plan sketched. Alternatives: a new
  `P2` milestone, `P1-005`. Rule: 1 (mirror the consumer) — this is CI/gate tooling, the
  same lane as M0-004, M0-006 and M0-007, both of which landed long after M0 was "done".
  A `P2` milestone holding one process ticket would misrepresent the roadmap.
- Decision: ADR number -> `0016`. Alternatives: `0015`. Rule: 4 (smaller change) — `0015`
  is taken by the open PR #31 (`adr-0015-heap-model-gaps`); it is absent from `main` only
  because that PR has not merged. Skipping to 0016 avoids a duplicate number whichever
  merges first.
- Decision: implementation language -> a single PowerShell 7 script under `tools/`.
  Alternatives: a C# console project like `tools/check-coverage`, a composite GitHub Action.
  Rule: 4 (smaller change) — a `.csproj` would join `Equiv.slnx`, the analyzer gate and the
  `tools/check-coverage.Tests` convention, for a utility that is mostly HTTP and `gh`
  shell-outs. `build.ps1` is the precedent for an untested repo script.
- Decision: SonarCloud auth -> anonymous for reads, `SONAR_TOKEN` only when present.
  Alternatives: always require the token. Rule: 3 (what tests can pin) — the project is
  public, so `/api/issues/search` answers unauthenticated; requiring a secret would make the
  dry run unrunnable on a fresh clone. The token is still read for `-PushResolutions`.
- Decision: batch identity -> `<!-- sonar-triage:v1 key=rule:<ruleKey> -->` /
  `key=file:<path>` / `key=tail:<area>`, matched by substring against open issue bodies.
  Alternatives: a GitHub issue label per batch, an external state file. Rule: 2 (closed
  types over open) — the marker travels with the issue, so the script holds no state and a
  hand-edited title or label never orphans a batch.
- Decision: fix-PR commit footer -> `Ticket: GH-<issue number>`. Alternatives: omit the
  footer, invent `SONAR-<n>` ids. Rule: 1 (mirror the consumer) — CLAUDE.md requires one
  ticket id in the footer; these units of work are GitHub issues, so the id is the issue.

### Observed

- The dev box has no PowerShell 7 (`pwsh` is not installed; only Windows PowerShell 5.1),
  although `build.ps1` carries a `#!/usr/bin/env pwsh` shebang and CI runs `shell: pwsh`.
  The script is therefore written to 5.1: explicit retry instead of `-MaximumRetryCount`,
  `File.WriteAllText` instead of `-Encoding utf8NoBOM`, and an explicit
  `SecurityProtocol = Tls12` because 5.1 still offers TLS 1.0, which sonarcloud.io refuses.
  `$PSScriptRoot` is also unbound while 5.1 binds parameters, so `-PolicyPath` defaults in
  the body rather than in `param()`.
- `/api/rules/show` serves `name` and `cleanCodeAttribute` for both `csharpsquid:*` and
  `external_roslyn:*`, but `htmlDesc`/`mdDesc` come back empty on this plan even with an
  explicit `f=` field list. Issue bodies therefore link to `rules.sonarsource.com` for
  `csharpsquid` rules instead of inlining the description. Recorded in the tool README.
- The backlog grew from 211 to 245 findings between planning and implementation, because
  M2-004 (PR #30) merged in between. Batch count landed at 26, as predicted.

### Verification (acceptance criteria)

1. Dry run with no arguments printed `fetched 245 / suppressed 3 / batches 26` and wrote
   nothing. PASS
2. 26 batches: 11 rule-wide (MA0003 106 findings/19 files down to MA0182 3/3), 12 per-file,
   3 long-tail (`src` 6, `tests` 4, `tools` 1). PASS
3. The two seeded `accept` entries suppressed 3 of S2094's 8 findings — exactly the three
   `AssemblyMarker.cs` ones, leaving a 5-finding S2094 batch. Adding a temporary
   `csharpsquid:S3267` accept moved the counts to `suppressed 8 / batches 25` and removed
   that batch; reverted. A policy entry with `verdict: accept` and no `reason` aborted the
   run with a named error. A `//` comment containing a URL parsed correctly. PASS
4. Verified by inspection of the rendered bodies in the dry run. PASS
5. Deferred to the first `-Apply`: not run in this PR, because filing 26 public issues is the
   user's call, not the implementer's. The second-run check is step 5 of `equiv-sonar-triage`.
6. `.github/workflows/sonar-triage.yml`: weekly cron + `workflow_dispatch`,
   `contents: read` + `issues: write`, `actions/checkout` pinned to the same SHA as ci.yml.
   PASS
7. `./build.ps1 -Integration` green; 46 + 1 + 32 tests pass; coverage 100% line and branch on
   all four `src/` assemblies. PASS
- Note: the branch is named `P2-001-sonar-triage`, from the plan's provisional ticket id,
  and was already pushed when the id settled on M0-008. Left as is rather than re-opening
  the PR; the commit footer and this file carry the real id.
