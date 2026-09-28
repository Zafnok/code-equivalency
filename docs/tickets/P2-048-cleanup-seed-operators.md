# P2-048 Cleanup refactorings as Preserving seed operators
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-010, P2-035

## Goal
Nobody has measured whether `equiv` can check a code-cleanup commit. The corpus holds only
migrations, and `tools/corpus/migration-prompt.md` forbids refactoring. The Preserving seed
operators already put behaviour-preserving edits into real corpus code, but they are
micro-edits (rename a local, swap operands), not the refactorings a cleanup commit makes. Add five
operators that are cleanup refactorings and preserve behaviour by construction. After this, each
corpus run measures cleanup on real code: the share of these seeds that `equiv` proves Equivalent.
No new corpus data and no ADR are needed, because the seeded modes already exist (ADR 0028,
decision 4). Once P2-055 lands (ADR 0040), the cleanup proof rate can also be measured on a
same-runtime pair: the modern side against its seeded copy. That isolates the refactoring from the
migration.

## Spec references
`tools/corpus/seeds.md` (mechanical seeds), ticket M0-012 (the differential soundness gate that
shares `SyntaxMutator`), `.claude/skills/equiv-corpus-run/SKILL.md` section 5 (how Preserving seeds
are scored).

## Acceptance criteria (all must hold; nothing beyond them)
1. `MutationOperator` gains five Preserving operators, and `SyntaxMutator.IsPreserving` stays true
   for exactly the Preserving family. It is an ordinal comparison today, so place the new
   operators before `FlipComparison` or change the test.
   - `IfToConditional`: `if (c) x = a; else x = b;` becomes `x = c ? a : b;`, and
     `if (c) return a; else return b;` becomes `return c ? a : b;`. Only where `x` is a local or
     parameter, and `a` and `b` have exactly the target's type, so no conversion changes.
   - `CoalesceNullCheck`: `x != null ? x : y` and `x == null ? y : x` become `x ?? y`, where `x` is
     a local or parameter.
   - `ConcatToInterpolation`: a `+` chain whose operands are all `string`-typed becomes one
     interpolated string. Only `string` operands, so no culture-sensitive `ToString` enters.
   - `GuardClause`: a `void` method's last statement `if (c) { S }`, with no `else`, becomes
     `if (!c) return;` followed by `S`.
   - `ForToForeach`: `for (int i = 0; i < a.Length; i++)` over an array local or parameter `a` that
     the body never assigns, and whose body uses `i` only as `a[i]` reads, becomes
     `foreach (var e in a)` with each `a[i]` replaced by `e`.
2. Every new operator's output compiles (`CompileCheck`) and is drawn by `PairGen` in the M0-012
   gate. `DifferentialSoundnessTests.PreservingMutationIsNeverDivergent` stays green. If a new
   operator makes it fail, that is an `equiv` precision bug: file it as a P2 ticket and leave the
   operator out of the gate's draw until it lands, with the ticket id in a comment.
3. `tools/corpus/seeds.md` lists the five operators in the Preserving family with one line each.
4. `corpus.ps1 -SeedMechanical` draws the new operators. Its `seeds.json` records the operator, as it
   does today.
5. The SUMMARY template's "Mechanical seeds" section gains a per-operator table for the Preserving
   family (applied, Equivalent, Unknown with reason, Divergent) and the line "Cleanup proof rate:
   Equivalent / applied, over the five cleanup operators". It is reported only and sets no threshold.

## Files
`tools/corpus/seeder/MutationOperator.cs`, `tools/corpus/seeder/SyntaxMutator.cs`,
`tools/corpus/seeder.Tests/SyntaxMutatorTests.cs`, `tools/corpus/seeds.md`,
`.claude/skills/equiv-corpus-run/SKILL.md`, the M0-012 generator if its operator list is separate.

## Tests
One `SyntaxMutatorTests` case per operator for applying it, and one for a site it must refuse (the
guard in criterion 1). Plus `PairGenTests.EveryOperatorIsDrawn` and
`PairGenTests.PreservingOperatorsCompile`, extended.

## Size guard
An operator that needs more than the one method it rewrites (extract method, inline method) is out
of scope, because `SyntaxMutator.Apply` returns one method. Those live in P2-049's samples. More
than five new operators: stop.

## Out of scope
Running the corpus (P2-046 or a later run picks this up). Engine fixes for what the operators find.
New corpus pairs.

## Notes
