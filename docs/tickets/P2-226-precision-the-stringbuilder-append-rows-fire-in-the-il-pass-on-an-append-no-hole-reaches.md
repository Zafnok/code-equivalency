# P2-226 Precision: the `StringBuilder` append rows fire in thorough's IL pass on an append no hole can reach
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-211

## Goal
The rows `System.Text.StringBuilder::Append(ref ` and `AppendLine(ref ` (`changedIn` net6.0, `runtime-changes.json`)
record that from .NET 6 an interpolated string is appended piece by piece, so a hole that reads or changes the builder
sees a different state. P2-164 and P2-211 lower such an append to `Append(string)` where no hole can tell, so the first
pass never meets the row there. Where a hole is one P2-086 and P2-139 do not join (a `DateTime`, a struct, a format
clause), the call keeps its handler, the pair stays Unknown, and thorough's IL pass reads `AppendLine(ref ...)` from IL.
The IL lowering passes no row suppression and no `requires` fact to the call's identity (`IlLowerer`; P2-114's Notes
call this the conservative side), so the row fires and the pair is EQ006 even where no hole can reach the builder.

P2-211's rerun of `gitextensions-9860` has one: `GitCommandsTests.Git.GitBlameCommitTests::TestToString()`, whose
builder is a local made by `new()` and used only as the receiver of its own members, outside every `try`, with two
`DateTime` holes and one call of the solution's `ObjectId.ToShortString()`. P2-124 adjudicated it a false positive.

When done, these two rows fire only on an append whose builder a hole may reach, in the IL pass as in the first pass.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (its header on `requires`), `docs/tickets/done/P2-114-*.md` and
`docs/tickets/done/P2-190-*.md` (how a `requires` fact is read and applied), `docs/tickets/done/P2-164-*.md` and
`docs/tickets/done/P2-211-*.md` (when no hole can reach the builder), ADR 0040 (runtime rows), ADR 0049 (the IL pass),
VERIFICATION-MODEL section 3.

## Acceptance criteria (all must hold; nothing beyond them)
1. The two rows carry a `requires` fact that holds of a method with an append to the row's member that binds a handler
   and whose builder a hole may reach, decided as P2-164 and P2-211 decide it; a unit test per side of the fact.
2. The IL pass applies the fact read from the method's source, so a pair the first pass leaves Unknown is not EQ006 by
   these rows where the fact does not hold; a unit test shows it, and one shows a hole that reads the builder still fires.
3. A thorough rerun of `gitextensions-9860` has `TestToString()` not EQ006, recorded in `## Notes`.

## Tests
The unit tests of criteria 1 and 2.

## Out of scope
Making `TestToString()` Equivalent: that needs a `DateTime` hole joined (P2-139's table, no owner). Applying the other
`requires` facts or the configured suppressions in the IL pass, beyond what criterion 2 needs.

## Notes
- Filed 2026-10-15 by P2-211, from its thorough rerun of `gitextensions-9860`.
