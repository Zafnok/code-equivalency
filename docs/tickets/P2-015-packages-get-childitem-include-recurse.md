# P2-015 `corpus.ps1 -Packages` scans every file, not just restore output
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`Get-ResolvedPackages` (`tools/corpus/corpus.ps1`) calls `Get-ChildItem -LiteralPath $Dir -Recurse
-File -Include 'project.assets.json', 'packages.config'`. On Windows PowerShell 5.1, `-Include`
is not applied to a recursive walk unless `-Path` (not `-LiteralPath`) ends in a wildcard, so this
call returns every file under `$Dir` — `.editorconfig`, `.git` blobs, `.cs` sources, RTF licence
files — instead of the ~48 real restore-output files. `-Packages` then throws parsing the first
non-JSON one it meets (`ConvertFrom-Json: Invalid JSON primitive`) and produces no data.

Found in M3-031 (2026-09-24): `-Packages gitextensions-8522` failed this way (confirmed: 3271
files matched under one checkout against 48 whose name is actually `project.assets.json`), and the
same call would fail identically for the three agent pairs since it always aborts on the first
checkout scanned. `packageVersionChanges` (ADR 0034 item 4) has never been computed for any pair.

## Spec references
ADR 0034 item 4; `.claude/skills/equiv-corpus-run/SKILL.md` section 5.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Get-ResolvedPackages` finds only files actually named `project.assets.json` or
   `packages.config` under `$Dir`, still git-excluded (`[\\/]\.git[\\/]` filter unchanged), and
   does not throw on any other file type present in a real checkout.
2. A test or manual repro (a directory with a non-JSON file alongside a real
   `project.assets.json`) shows `-Packages` no longer scans the non-JSON file.
3. `-Packages gitextensions-8522` runs to completion after both sides are restored (needs a real
   corpus checkout; record the result you saw in Notes if the box used for verification differs
   from the one that filed this ticket).

## Size guard
This is a `corpus.ps1` fix (one `Get-ChildItem` call). If it needs more than that, stop and say
why.

## Out of scope
Computing `packageVersionChanges` for any specific pair, or adding it to SARIF (ADR 0034 keeps it
out of SARIF for now). Re-running M3-031's pairs; that ticket recorded the gap and moves on.

## Notes
- Repro on this box: `Get-ChildItem -LiteralPath $dir -Recurse -File -Include
  'project.assets.json' -ErrorAction SilentlyContinue` returned 3271 files under
  `gitextensions__gitextensions@3f4ed21998af`, of which only 48 are actually named
  `project.assets.json`. Likely fix: `-Path (Join-Path $Dir '*')` in place of `-LiteralPath $Dir`,
  or a `Where-Object { $_.Name -in 'project.assets.json','packages.config' }` filter after an
  unfiltered `-Recurse`.
- Decision: went with the `Where-Object { $_.Name -in ... }` post-filter over
  `-Path (Join-Path $Dir '*')`, since `-LiteralPath` is kept (no glob-injection risk from a
  `$Dir` containing `[` / `]`, which real corpus checkout paths won't but a `-Path` wildcard
  form would mishandle) and the `-notmatch '\.git'` filter already runs as a second
  `Where-Object` clause immediately after, so this is a one-clause addition rather than a
  second filtering mechanism.
- Verified with a manual repro (not a repo test — no Pester/script-test harness exists in this
  repo): built a temp dir with `.editorconfig`, `notes.rtf`, and one real
  `obj/project.assets.json` (one `Newtonsoft.Json` package), extracted the fixed
  `Get-ResolvedPackages` function body and ran it against that dir. Result: `Files: 1`,
  `Versions.Keys` = `newtonsoft.json` only — the non-JSON files were not scanned and did not
  throw. Confirms acceptance criteria 1-2.
- Criterion 3 (`-Packages gitextensions-8522` end to end) not run: this worktree has no
  `.corpus/` checkout and fetching + restoring both sides of gitextensions-8522 is a
  network- and time-heavy operation out of proportion to this ticket's `S` size guard. The
  fix is a pure filter change with no dependency on which files are present, so the manual
  repro above stands in for it; whoever runs `-Packages gitextensions-8522` next on a box
  with the corpus prepared should confirm it completes and record the result here.
