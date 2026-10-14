# P2-187 Precision bug: a field renamed with the same initializer is two unrelated heap inputs
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-130's adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 1 false EQ002 on
`gitextensions-8522`: `GitExtensions.UITests.Script.ScriptRunnerTests::Setup()`. The pull request
renames a private static field (and makes it `readonly`) and keeps its initializer. `Setup()` only
tests the field for null. The two fields are two `field.*` maps with free initial values, so the model
makes one null and the other not, although both hold the same value at run time.

When done, a field removed on one side and added on the other in the same type, with the same type and
initializer, is either paired as one field or makes a read of it Unknown rather than Divergent,
decided through `equiv-adr`.

## Spec references
ADR 0018 (heap maps), ADR 0024 (what a fingerprint holds), the matching of procedures
(`Equiv.Core.Matching`), `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, through `equiv-adr`, how a renamed field with the same declaration is treated.
2. A sample pair whose body reads a renamed static field is not Divergent; one whose field's
   initializer changed is.
3. Rerun `gitextensions-8522` in compare mode quick; Notes give the procedure's new result.

## Tests
As criterion 2, plus the rule's unit tests.

## Size guard
Pairing renamed methods: stop, that is matching work.

## Out of scope
Fields whose type or initializer changed.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
