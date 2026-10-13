# P2-169 Precision bug: thorough's IL pass reports EQ002 on a collection expression with a spread, against the loop it replaces
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-032

## Goal
On `gitextensions-11372` (IDE0028, collection expressions) three test methods replace "make an empty
collection, then `Add` each element of a sequence in a `foreach`" with a collection expression that
holds one spread element. The two forms behave the same: the same constructor, then one `Add` per
element in order.

In compare mode quick the three pairs are Unknown (`opaque`, the modern side's
`CollectionExpression`; P2-128 owns that). In compare mode thorough the IL pass reports all three
EQ002 (`decidedBy: il-pass`, `lowering: il`, `divergenceKind: result-changed`), at equiv `78853202`
(`docs/runs/2026-10-13-cleanup-verdict.md`):
- `GitExtUtilsTests.ArgumentBuilderTests::Add(string[],int,string)`
- `GitExtUtilsTests.LazyStringSplitTests::None(string,char,string[])`
- `GitExtUtilsTests.LazyStringSplitTests::RemoveEmptyEntries(string,char,string[])`

In each counterexample the modern side throws a `NullReferenceException` at a point where the
legacy side goes on. In the first, the legacy trace runs the whole method and the modern trace ends
straight after the collection's constructor. In the other two, both traces reach the first
`MoveNext` call, then the legacy side calls `Add` and the modern side throws. Nothing in either
source form dereferences a value at that point that the other form does not. So the Divergent
rests on a null dereference the IL lowering sees on one side only, and it is a false positive.

This is not P2-135's cause: the two sides are on one runtime and bind to the same callees. It is
not P2-128's either. P2-128 lowers a spread in the first pass, which would keep these pairs away
from the IL pass, but the IL pass would still be wrong about the same two shapes of IL.

The first deliverable is the repro: the smallest sample pair (a `foreach` with `Add` against a
spread, for an array source and for a struct enumerator) on which the IL pass gives EQ002, with
what makes one side throw: a null check the IL lowering puts on one loop shape and not on the
other, a stack slot it loses, or something else. Then make the result Equivalent, or Unknown if the
cause does not allow more.

## Spec references
ADR 0049 (the passes of thorough mode), ADR 0039 (the IL lowering), `IL-COVERAGE.md`,
VERIFICATION-MODEL section 3 (the collection expression paragraph),
`docs/tickets/P2-135-il-pass-divergent-from-binding-only-differences.md` (the other known cause of
a false Divergent from the IL pass), `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` names the cause, with the sample that reproduces it.
2. The sample is not EQ002 in compare mode thorough, with an integration test.
3. A seeded behaviour change in the same sample (the spread's source is another sequence, or one
   element is added twice) is still EQ002 in compare mode thorough.
4. One thorough run of `gitextensions-11372` through `equiv-corpus-run`: none of the three
   procedures above is EQ002. Counts in `## Notes`.

## Tests
- An integration test over the new sample, in both compare modes.
- A unit test of the IL lowering for whatever the cause turns out to be.

## Size guard
More than one cause in the IL lowering: fix the one behind these three results and file the others.

## Out of scope
Lowering a spread in the first pass (P2-128); the binding-only cause (P2-135).

## Notes
- Filed 2026-10-13 from P2-110's thorough run of `gitextensions-11372`. Quick mode, the default, is
  not affected: the three pairs are Unknown there.
