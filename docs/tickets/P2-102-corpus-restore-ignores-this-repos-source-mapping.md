# P2-102 A corpus restore does not inherit this repository's NuGet source mapping
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
`.corpus/` sits inside this repository, so NuGet merges this repository's `nuget.config` into every
corpus restore. Its `packageSourceMapping` maps every package to the source named `nuget.org`. A
corpus repository whose own `nuget.config` clears the sources and adds nuget.org under another name
then has no source any package maps to. `jellyfin-13023` (both sides) names it `nuget`, and
`dotnet restore` fails with NU1100 for every package ("PackageSourceMapping is enabled, the following
source(s) were not considered: nuget"). P2-066 restored with `--configfile <this repo>/nuget.config`
instead, which works only because that file also lists nuget.org.

`-Prepare` already writes sentinel MSBuild and format files under `.corpus/` so this repository's
settings stop there (P2-014). Add a sentinel `.corpus/nuget.config` that clears the package source
mapping, so a corpus repository's own sources apply as they do in its own CI.

## Spec references
`tools/corpus/corpus.ps1` (`-Prepare`), `.claude/skills/equiv-corpus-run/SKILL.md` section 3,
P2-014's Notes (the sentinel files), ADR 0030 (why this repository maps its sources).

## Acceptance criteria (all must hold; nothing beyond them)
1. `-Prepare` writes `.corpus/nuget.config` with `<packageSourceMapping><clear /></packageSourceMapping>`,
   and leaves this repository's own `nuget.config` unchanged.
2. `dotnet restore` of both `jellyfin-13023` solutions succeeds with the skill's command as written.
3. A `tools/corpus/tests` case asserts the sentinel's content.

## Tests
Named in criterion 3.

## Out of scope
Changing this repository's own source mapping.

## Notes
- Found by P2-066.
