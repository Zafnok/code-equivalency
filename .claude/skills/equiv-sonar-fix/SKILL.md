---
name: equiv-sonar-fix
description: Fix one batch of SonarQube findings filed as a GitHub issue labelled sonar. Use when pointed at a sonar(...) issue, asked to fix Sonar code smells, clear a sonar batch, or work the sonar debt queue.
---

# Fix a Sonar batch

One `sonar`-labelled GitHub issue is one PR. The issue body **is** the ticket — goal,
findings table, acceptance criteria — and this is the single documented exception to
CLAUDE.md's "work is defined in `docs/tickets/`" (ADR 0016).

`equiv-task-loop` still governs everything it does not contradict: gates never move, decisions
are made not asked, and the session stops after the PR.

## Loop

1. **Read the issue.** `gh issue view <n>` (add `--json body --jq .body` for the raw table).
   Its acceptance criteria are the checklist — copy them into your first message. Do not
   re-plan the batch or widen it.

   **Parent or part?** A batch over 25 findings is split (ADR 0022): a *parent* issue with a
   `## Parts` table and no findings, and one *part* sub-issue per slice, each holding at most
   25 findings. Never work a parent: if pointed at one, pick its first open sub-issue and
   say which. For a part, also read the parent and its comments
   (`gh issue view <parent> --comments`) — the rule-wide decision and the shape of fix
   already used live there — and look at the merged PRs of closed sibling parts so your fix
   matches theirs. List siblings with
   `gh api repos/{owner}/{repo}/issues/<parent>/sub_issues --jq '.[] | "\(.number) \(.state) \(.title)"'`.

2. **Re-verify every finding against `HEAD`.** Sonar's data is as old as the last analysis of
   `main`, and this batch may have been filed before the last merge. For each row, open the
   file at that line and confirm the finding is still there and still means what the message
   says. A finding that no longer reproduces is **called out in the PR body**, not silently
   dropped. If more than about a third have evaporated, stop and re-run
   `equiv-sonar-triage` instead — the issue is stale.

3. **Branch** from `main`: `git switch -c sonar/<short-slug>` (e.g. `sonar/ma0003`,
   `sonar/ir-lowerer`).

4. **Fix, smallest change first.** For a rule-wide batch, apply the same shape of fix
   everywhere; inconsistency across call sites is worse than the original finding. Prefer the
   compiler-verified route: `dotnet format analyzers --diagnostics <ID>` handles many
   `external_roslyn` rules, but read its diff — it is not always what the rule intends.

5. **Pin the severity** when the batch is an `external_roslyn:<ID>` rule. In the same PR, add
   or raise `dotnet_diagnostic.<ID>.severity = error` in `.editorconfig`, in the section that
   matches the files involved. This is the point of the batch: the finding existed only
   because the rule sat below warning level, and pinning it means the build now catches a
   regression that Sonar would otherwise re-file next week.

   For a **part**, pin only if every sibling part is already closed; then the PR closes both
   the part and the parent. Otherwise do not pin: the build would fail on the parts not yet
   fixed. The first part to land settles the shape of the fix; post a one-paragraph comment
   on the parent saying what it was, so later parts copy it.

6. **Or accept it.** If a finding turns out to be wrong for this repo, add an entry to
   `tools/sonar-triage/policy.jsonc` with a `reason` citing an ADR or a ticket. Use the
   verdict ladder in `equiv-sonar-triage`. Never reach for `// NOSONAR`, `#pragma warning
   disable`, a coverage exclusion, or a lowered gate.

7. **Gate:** `./build.ps1 -Integration` fully green before every commit. Coverage stays at
   100% line and branch — a refactor that moves code out of a covered path needs its test
   moved too.

8. **Commit** with Conventional Commits and the footer `Ticket: GH-<issue number>`:

   ```
   refactor(core): name arguments at 106 MA0003 call sites

   Pins dotnet_diagnostic.MA0003.severity = error so the rule cannot
   drop back below warning level.

   Ticket: GH-42
   ```

9. **PR:** `gh pr create`, title matching the issue, body listing each acceptance criterion
   with what proves it, plus `Closes #<n>` and any findings that no longer reproduced. Then
   stop — do not pick up the next batch in the same session. A part is a batch: do not pick
   up its sibling either.

## Rules that trip agents up here

- **The batch is the scope.** A rule-wide batch touching 19 files is still one PR; a tempting
  adjacent cleanup in one of those files is not part of it. For a part, the same finding in a
  file listed in a sibling part is the sibling's, even if you are already in the file.
- **Accepting from a part accepts the rule.** A `policy.jsonc` entry is rule-wide unless its
  `paths` narrow it. Say so on the parent before accepting from one part.
- **Do not edit the issue body.** The next triage run overwrites it. Say it in the PR instead.
- **Do not re-run the triage script** to "refresh" the issue mid-fix. Finish the PR; triage
  reconciles afterwards and closes the issue when Sonar next analyses `main`.
- `Equiv.Core` still may not reference Roslyn or Z3, and unsupported constructs still lower to
  `IrOpaque`. A Sonar finding never justifies crossing an architecture boundary.
- If a fix would change observable behaviour — a verdict, a rule id, SARIF shape — it is not a
  cleanup. Stop and write an ADR (`equiv-adr`).
