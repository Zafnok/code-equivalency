# sonar-triage

Turns SonarQube Cloud's *overall* issue backlog into batched GitHub issues.

`sonar.yml` grades only new code on each PR (ADR 0009), so nothing ever looks at what was
already there. This script does: it reads the SonarCloud API, drops findings that
`policy.jsonc` marks as accepted, groups the rest into coherent batches, and syncs each batch
to one GitHub issue labelled `sonar` that a fix session can pick up whole.

Design and rejected alternatives: [`docs/adr/0016-sonar-issue-triage.md`](../../docs/adr/0016-sonar-issue-triage.md).

## Running it

```powershell
# Dry run. Reads only; prints the batches and what it would create, edit or close.
./tools/sonar-triage/sonar-triage.ps1

# Sync GitHub issues for real.
./tools/sonar-triage/sonar-triage.ps1 -Apply
```

Reads are anonymous — the project is public — so a fresh clone can dry-run with no secret.
`gh` must be authenticated for anything that touches GitHub. Runs on Windows PowerShell 5.1
and on `pwsh` 7; CI uses the latter.

| Flag | Default | Effect |
|---|---|---|
| `-Apply` | off | Create, edit and close GitHub issues. Without it nothing is written. |
| `-PushResolutions` | off | Also mark policy-accepted findings Won't Fix in SonarCloud. Needs `SONAR_TOKEN` with issue-admin rights and prompts for confirmation, because it changes shared state. |
| `-RuleBatchMinFiles` | 3 | A rule found in at least this many files becomes one rule-wide batch. |
| `-FileBatchMin` | 2 | A file left with fewer findings than this rolls into its area's long-tail batch. |
| `-PolicyPath` | `policy.jsonc` beside the script | Useful for testing a policy change without editing the real file. |
| `-ProjectKey`, `-Organization`, `-HostUrl`, `-Label` | this repo's values | |

`.github/workflows/sonar-triage.yml` runs `-Apply` weekly and on demand.

## How findings are batched

Hybrid, because the right unit of work differs by finding:

1. A rule appearing in **`-RuleBatchMinFiles` or more distinct files** becomes one rule-wide
   batch. One rule is one decision, so it should be one PR — not eighteen arguments about the
   same thing.
2. Everything left groups **per file**.
3. A file left holding fewer than `-FileBatchMin` findings rolls into a **long-tail** batch
   for its top-level area (`src`, `tests`, `tools`), so the queue does not fill with
   one-finding issues.

Each issue body opens with `<!-- sonar-triage:v1 key=... -->`. That marker is the only state
the script keeps: it re-reads open `sonar` issues, matches markers, and creates, edits or
closes accordingly. Re-running is therefore idempotent, and renaming an issue's title or
adding labels by hand never orphans a batch.

## Saying a finding is wrong

Edit `policy.jsonc`, don't edit the code and don't add `// NOSONAR`:

```jsonc
{
  "rule": "csharpsquid:S2178",
  "verdict": "accept",
  "paths": ["src/Equiv.Core/**"],
  "reason": "Why this rule is wrong *here*, citing an ADR or a ticket."
}
```

`verdict` is `fix` (the default), `accept` or `defer`. `accept` and `defer` **require** a
`reason` — the script refuses to run otherwise. Suppressed counts are printed by rule on every
run, so nothing disappears silently.

## Known limits

- `/api/rules/show` returns a rule's name and clean-code attribute but serves an empty
  description on this plan, so issue bodies link to `rules.sonarsource.com` for
  `csharpsquid` rules rather than inlining the text. `external_roslyn` rules have no such
  page; their message is self-explanatory.
- Sonar issue keys are not stable across re-analysis, so batches are keyed by rule or path and
  a fix session re-verifies every finding against `HEAD` before acting.
- The API refuses paging past 10,000 issues; the script stops there rather than erroring.
