# P2-167 `corpus.ps1 -Packages` fails with "The property 'Files' cannot be found on this object"
Status: in-progress
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-032

## Goal
`./tools/corpus/corpus.ps1 -Packages <slug>` prints ADR 0034's package version changes between a pair's two
restored sides. On 2026-10-12, while working P2-124, it failed for both `jellyfin-13023` and
`gitextensions-9860` (both sides restored with `dotnet restore`; Jellyfin with `--configfile <repo>/NuGet.config`)
with "The property 'Files' cannot be found on this object. Verify that the property exists.", so the summaries
under `docs/runs/2026-10-12-full-*/SUMMARY.md` say "Package changes: n/a".

When this is done `-Packages` prints its report for a restored pair, and restore output that lacks a property
the report reads counts as holding no package.

## Spec references
`.claude/skills/equiv-corpus-run/SKILL.md` section 5 (`-Packages`), ADR 0034 (`packageVersionChanges`).

## Acceptance criteria (all must hold; nothing beyond them)
1. `-Packages` on a pair whose two sides are restored prints the "restore outputs read" line, the counts line
   and the table of changed and one-sided packages, and does not throw.
2. A `project.assets.json` with no `libraries`, with `libraries: null` or with a library that has no `type`,
   and a `packages.config` that lists no package, hold no package and do not throw.
3. `tools/corpus/tests/Packages.Tests.ps1` covers 1 and 2 on made-up checkouts, and passes under Windows
   PowerShell 5.1 (Pester 3.4) and PowerShell 7.
4. `## Notes` holds what `-Packages jellyfin-13023` prints after the fix, on both sides restored.

## Files
`tools/corpus/corpus.ps1`, `tools/corpus/tests/Packages.Tests.ps1`, `docs/ROADMAP.md`, this ticket.

## Tests
`Packages.Tests.ps1`: `Restored_Sides_Report_Changed_And_OneSided_Packages`,
`Side_Without_Restore_Output_Warns_And_Lists_The_Other_Side`, `Same_Packages_On_Both_Sides_Print_No_Table`,
`Restore_Output_Without_The_Expected_Property_Holds_No_Package`.

## Size guard
A change under `src/` or `tests/`, or to any other mode of `corpus.ps1`, means the ticket has been misread: stop.

## Out of scope
Filling in "Package changes" in the 2026-10-12 run summaries (they belong to P2-124's PR #483). Changing what
`-Packages` reports. The same shadowing in `-Fetch` (see Notes).

## Notes
- Cause, reproduced on `jellyfin-13023` fetched and restored in this worktree: the failure is at the `-Packages`
  branch's read of `$modern.Files`, not inside `Get-ResolvedPackages`, which returned
  `@{Files=37; Versions=...}` when called on the same checkout directly. PowerShell names are case-insensitive,
  and P1-032 gave the script a `[string]$Modern` parameter (for `-Compare`). The branch's
  `$modern = Get-ResolvedPackages ...` therefore assigned to that typed variable, the result became the text
  `@{Files=37; Versions=System.Collections.Hashtable}`, and a string has no `Files`. Every pair fails the same
  way, restored or not; the mode has not worked since P1-032 (PR #415).
- Decision: how the collision is removed -> the branch's body moves into a function, `Show-PackageChanges`,
  whose locals are its own. Alternative: rename the two variables in place, which leaves the next statement
  added to the branch one `$modern` away from the same failure and leaves the report untestable (the branch
  reads `.corpus/pairs/<slug>/pair.json`, and `$CorpusRoot` is not a parameter). Rule: the smaller change that
  a test can hold.
- The test calls `Show-PackageChanges`, not `corpus.ps1 -Packages <slug>`: the branch is now the slug lookup
  and one call. That one line is proved by the run on the real pair below, not by Pester.
- A small fixture found three more reads that throw under `Set-StrictMode -Version Latest`, none of them the
  reported one: `.libraries` of an assets file without it, `.type` of a library without it, and
  `.packages.package` of a `packages.config` that lists no package. All three are fixed here (criterion 2),
  since the report's caller cannot tell them from the reported failure.
- `-Fetch` also assigns `$modern` (a checkout path) at script scope. The value is a string, so the coercion
  changes nothing; left as written.
- After the fix, both sides restored (`-Env`, then `dotnet restore Jellyfin.sln --configfile NuGet.config`):

  ```
  > ./tools/corpus/corpus.ps1 -Packages jellyfin-13023
  restore outputs read: legacy 37, modern 37 (project.assets.json + packages.config)
  == package version changes: 0 changed, 0 legacy only, 0 modern only
  ```

  `gitextensions-9860` was not fetched for this ticket; its failure had the same cause.
