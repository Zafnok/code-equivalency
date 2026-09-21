# ADR 0022: A Sonar batch over 25 findings splits into a parent issue and sub-issues

Status: accepted (2026-09-21)

## Context
ADR 0016 made each rule spanning three or more files a single issue: "one rule, one decision,
one PR". It sized that against a 211-finding backlog in which the biggest batch, `MA0003`, had
88 findings. By 2026-09-21 `MA0003` (#33) had grown to 156 findings across 28 files, and
nothing capped it. A rule-wide PR that size is more call sites than one fix session can
re-verify, change and gate in a single run, so the batch never gets picked up.

## Decision
`sonar-triage.ps1 -MaxFindingsPerIssue` (default 25) caps every issue. A batch within the cap
is filed exactly as before. A bigger batch keeps its key and becomes a parent issue that holds
the rule-wide decision and a table of parts but no findings; its findings are cut into parts
of at most 25, each filed as its own issue and linked as a GitHub sub-issue of the parent.
Parts follow the directory tree — a project, a directory, or a file — with small neighbours
packed back together in path order, and a single file over the cap cut by line. A part's key is
`<parent key>@<first path>[#<chunk>]`. One part is one PR. For an `external_roslyn` rule, only
the PR that closes the last open part pins the `.editorconfig` severity, because pinning
earlier would fail the build on the parts not yet fixed.

## Why
- The cap keeps each PR a size one fix session finishes, whatever the backlog grows to.
- The decision stays in one place: the parent. An `accept` is still one `policy.jsonc` entry,
  and the next triage run closes every part at once.
- Keeping the parent's key means the issue already filed for a batch (#33) turns into the
  parent, so its history and links stay.
- Cutting along directories rather than by count keeps a fixed part from moving its
  neighbours' findings between issues on the next run.
- GitHub sub-issues show progress on the parent with no extra state: the marker comment is
  still the script's only state, and linking reads existing links first, so it stays
  idempotent.

## Rejected
- **A cap with plain numbered issues and no parent.** Nowhere to record the rule-wide decision,
  and "part 3 of 7" renumbers every time a part closes.
- **Fixed-size chunks in path order.** Simplest to write, but closing the first chunk moves
  every later finding into a different issue.
- **Raising `-RuleBatchMinFiles` or batching per file only.** Hides the rule-wide decision the
  rule batch exists for, and does not cap one large file.
- **Leaving it to the fix session to split.** The session would then choose its own scope,
  which `equiv-sonar-fix` forbids for good reason.

## Consequences
- The weekly job also needs to link sub-issues. The sub-issues REST API is covered by the
  existing `issues: write` permission; no new secret or tool.
- The script's summary gains a `linked` count. A settled `-Apply` must report
  `created 0 / edited 0 / linked 0 / closed 0`.
- Part titles include their finding count, so a partly fixed part gets a title edit, not a new
  issue.
- `equiv-sonar-fix` learns to spot a part, read the parent's decision, and pin only on the
  last part. `equiv-sonar-triage` and `tools/sonar-triage/README.md` document the cap.
