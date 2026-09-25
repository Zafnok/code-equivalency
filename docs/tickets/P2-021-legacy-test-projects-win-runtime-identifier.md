# P2-021 ShortestPaths' legacy test projects fail to load: "doesn't list 'win' as a RuntimeIdentifier"
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
In the 2026-09-24 census (M3-031) and again in P2-016's rerun (2026-09-25),
`pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s 6 legacy non-SDK test and example projects
(`TargetFrameworkVersion` v4.7.2) are skipped with "Your project file doesn't list 'win' as a
"RuntimeIdentifier"". M3-022 (2026-09-23, same commit, before `corpus.ps1 -Prepare`/`-Env` existed)
loaded them. Their procedures (320) are listed in `run.properties.unverified` and the run exits 4.

## Spec references
`.claude/skills/equiv-corpus-run/SKILL.md` sections 3 and 5; P2-014 (`-Prepare`, `-Env`); ADR 0029.

## Acceptance criteria (all must hold; nothing beyond them)
1. Find whether the cause is the corpus environment (`-Env`'s `TargetFrameworkRootPath`, `NoWarn`,
   the restore command the skill gives) or how MSBuildWorkspace evaluates these projects, and say
   which in Notes.
2. Fix it where it lives (`corpus.ps1` or the skill's restore step if environmental; the loader if
   not), or record why it cannot be.
3. A `census` rerun of this pair skips 0 legacy projects, or Notes say why not.

## Out of scope
Anything P2-016 fixed (multi-target flavours going Ambiguous).

## Notes
- Filed from P2-016 (its Out of scope asked for this finding to be filed separately).
