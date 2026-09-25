# P2-018 A loaded project with source files that yields no procedures is a load failure, not an empty project
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-024

## Goal
In the 2026-09-24 census, `pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s modern side
reported a 100% load rate with `projectsSkipped: 0`. It extracted 0 procedures, against the 312
matched pairs the previous day's run found on the same commit (P2-016). A tool whose output is
"trust this" must never let a vacuous side look like a clean one. P2-016 finds that repo's cause.
This ticket adds the general guard:
- a project that loaded, holds at least one `.cs` document with a type declaration, and yields
  zero procedures is treated as a project that failed to load (ADR 0029): skipped, reported, and
  exit 4;
- the project load rate counts it as not loaded.

## Spec references
ADR 0029 (blast radius; project-level failure); ADR 0028 (project load rate); ARCHITECTURE.md
(exit 4, "a silent partial load is a bug").

## Acceptance criteria (all must hold; nothing beyond them)
1. After symbol enumeration, a loaded C# project with at least one syntax tree that declares a
   type, and with zero `ProcedureIdentity` values, is moved to `projectsSkipped` with reason
   `no-procedures`. It is reported as a tool-execution notification naming the project.
2. The run exits 4 when this happens, following ARCHITECTURE.md's precedence. `--lower-only` does
   the same.
3. A project with no type declarations (for example one that only holds assembly attributes) is
   not affected.
4. Census and SUMMARY load rates count the project as not loaded. `docs/runs/README.md`'s template
   says so in one sentence.

## Files
The frontend's enumeration or loader file that owns `projectsSkipped`, `src/Equiv.Cli/*` only if
the reason enum lives there, `docs/runs/README.md`, tests.

## Tests
`ProjectWithTypesButNoProcedures_IsSkipped`, `ProjectWithOnlyAssemblyAttributes_IsNotSkipped`,
`NoProcedures_ExitsFour`.

## Size guard
Fixing why ShortestPaths yields no procedures is P2-016, not this ticket.

## Out of scope
P2-016's root cause. Thresholds such as "suspiciously few procedures".

## Notes
